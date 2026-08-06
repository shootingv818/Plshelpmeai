using IvaScanner.Core.Models;
using IvaScanner.Worker.Configuration;

namespace IvaScanner.Worker.Services;

public interface IProxyManager
{
    bool IsEnabled { get; }
    Task<ProxyServerDto?> GetProxyAsync();
    Task ReportProxyStatusAsync(int proxyId, bool isWorking, string? errorMessage = null);
    Task RefreshProxiesAsync();
}

public class ProxyManager : IProxyManager
{
    private readonly ILogger<ProxyManager> _logger;
    private readonly IMasterApiClient _masterClient;
    private readonly IWorkerStateManager _stateManager;
    private readonly ProxyConfiguration _config;
    private readonly object _lock = new();
    
    private ProxyServerDto? _currentProxy;
    private DateTime _lastProxyRefresh = DateTime.MinValue;
    private readonly TimeSpan _proxyRefreshInterval = TimeSpan.FromMinutes(5);

    public ProxyManager(
        ILogger<ProxyManager> logger,
        IMasterApiClient masterClient,
        IWorkerStateManager stateManager,
        Microsoft.Extensions.Options.IOptions<ProxyConfiguration> config)
    {
        _logger = logger;
        _masterClient = masterClient;
        _stateManager = stateManager;
        _config = config.Value;
    }

    public bool IsEnabled => _config.Enabled;

    public async Task<ProxyServerDto?> GetProxyAsync()
    {
        if (!IsEnabled)
        {
            _logger.LogTrace("Proxy is disabled");
            return null;
        }

        lock (_lock)
        {
            // Return current proxy if it's still valid
            if (_currentProxy != null && 
                DateTime.UtcNow - _lastProxyRefresh < _proxyRefreshInterval)
            {
                _logger.LogTrace("Using cached proxy {ProxyId}", _currentProxy.Id);
                return _currentProxy;
            }
        }

        // Get new proxy from master
        await RefreshProxiesAsync();
        
        lock (_lock)
        {
            return _currentProxy;
        }
    }

    public async Task ReportProxyStatusAsync(int proxyId, bool isWorking, string? errorMessage = null)
    {
        if (!IsEnabled)
            return;

        try
        {
            _logger.LogDebug("Reporting proxy {ProxyId} status: {Status}", proxyId, isWorking ? "Working" : "Failed");

            var report = new ProxyStatusReport
            {
                ProxyId = proxyId,
                WorkerId = _stateManager.WorkerId,
                IsWorking = isWorking,
                ErrorMessage = errorMessage,
                Timestamp = DateTime.UtcNow
            };

            await _masterClient.ReportProxyStatusAsync(report);

            // If current proxy failed, clear it to force refresh
            if (!isWorking)
            {
                lock (_lock)
                {
                    if (_currentProxy?.Id == proxyId)
                    {
                        _logger.LogWarning("Current proxy {ProxyId} failed, clearing cache", proxyId);
                        _currentProxy = null;
                        _lastProxyRefresh = DateTime.MinValue;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reporting proxy {ProxyId} status", proxyId);
        }
    }

    public async Task RefreshProxiesAsync()
    {
        if (!IsEnabled)
            return;

        try
        {
            _logger.LogDebug("Refreshing proxy for worker {WorkerId}", _stateManager.WorkerId);

            var proxy = await _masterClient.GetProxyAsync(_stateManager.WorkerId);

            lock (_lock)
            {
                _currentProxy = proxy;
                _lastProxyRefresh = DateTime.UtcNow;
            }

            if (proxy != null)
            {
                _logger.LogInformation("Received new proxy {ProxyId} - {Host}:{Port}", 
                    proxy.Id, proxy.Host, proxy.Port);
            }
            else
            {
                _logger.LogWarning("No proxy available for worker {WorkerId}", _stateManager.WorkerId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing proxy for worker {WorkerId}", _stateManager.WorkerId);
        }
    }
}