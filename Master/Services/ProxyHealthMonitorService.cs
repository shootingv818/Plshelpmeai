using IvaScanner.Master.Services;

namespace IvaScanner.Master.Services
{
    public class ProxyHealthMonitorService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ProxyHealthMonitorService> _logger;
        private readonly TimeSpan _healthCheckInterval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(6);
        private DateTime _lastCleanup = DateTime.UtcNow;

        public ProxyHealthMonitorService(
            IServiceProvider serviceProvider,
            ILogger<ProxyHealthMonitorService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Proxy Health Monitor Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PerformHealthChecksAsync();
                    
                    // Perform cleanup if needed
                    if (DateTime.UtcNow - _lastCleanup >= _cleanupInterval)
                    {
                        await PerformCleanupAsync();
                        _lastCleanup = DateTime.UtcNow;
                    }

                    await Task.Delay(_healthCheckInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during proxy health monitoring");
                    
                    // Wait shorter time on error
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("Proxy Health Monitor Service stopped");
        }

        private async Task PerformHealthChecksAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var proxyService = scope.ServiceProvider.GetRequiredService<IProxyService>();
            var systemLog = scope.ServiceProvider.GetRequiredService<ISystemLogService>();

            try
            {
                _logger.LogDebug("Starting scheduled proxy health checks...");

                // Perform health checks on all active proxies
                await proxyService.PerformAllHealthChecksAsync();

                // Get proxy stats for monitoring
                var stats = await proxyService.GetProxyStatsAsync();

                _logger.LogInformation("Completed proxy health checks. Working: {Working}, Failed: {Failed}", 
                    stats.WorkingProxies, stats.FailedProxies);

                // Log critical issues
                if (stats.WorkingProxies == 0 && stats.TotalProxies > 0)
                {
                    await systemLog.LogCriticalAsync(
                        "NO WORKING PROXIES AVAILABLE! All proxies are down or failed.",
                        "proxy_health_critical",
                        "ProxyHealthMonitorService"
                    );
                }
                else if (stats.WorkingProxies < stats.TotalProxies * 0.2) // Less than 20% working
                {
                    await systemLog.LogWarningAsync(
                        $"Low proxy availability: Only {stats.WorkingProxies} out of {stats.TotalProxies} proxies are working",
                        "proxy_health_warning",
                        "ProxyHealthMonitorService"
                    );
                }

                // Auto-deactivate consistently failing proxies
                await DeactivateFailingProxiesAsync(proxyService, systemLog);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to perform scheduled proxy health checks");
                
                try
                {
                    await systemLog.LogErrorAsync(ex,
                        "Scheduled proxy health check failed",
                        "proxy_health_error",
                        "ProxyHealthMonitorService"
                    );
                }
                catch
                {
                    // Ignore if we can't even log the error
                }
            }
        }

        private async Task DeactivateFailingProxiesAsync(IProxyService proxyService, ISystemLogService systemLog)
        {
            try
            {
                // Get proxies that have failed too many times
                var failingProxies = await proxyService.GetProxiesAsync(0, 1000);
                var proxyIdsToDeactivate = new List<string>();

                foreach (var proxy in failingProxies.Where(p => p.IsActive))
                {
                    // Deactivate if failure rate is > 80% and has been tested at least 10 times
                    var totalTests = proxy.SuccessCount + proxy.FailureCount;
                    if (totalTests >= 10 && proxy.SuccessRate < 20.0)
                    {
                        proxyIdsToDeactivate.Add(proxy.Id);
                    }
                    
                    // Deactivate if it has failed more than 20 consecutive times
                    if (proxy.FailureCount >= 20 && proxy.SuccessCount == 0)
                    {
                        proxyIdsToDeactivate.Add(proxy.Id);
                    }
                }

                if (proxyIdsToDeactivate.Any())
                {
                    foreach (var proxyId in proxyIdsToDeactivate)
                    {
                        await proxyService.UpdateProxyAsync(proxyId, new UpdateProxyRequest
                        {
                            IsActive = false
                        });
                    }

                    await systemLog.LogWarningAsync(
                        $"Auto-deactivated {proxyIdsToDeactivate.Count} consistently failing proxies",
                        "proxy_auto_deactivation",
                        "ProxyHealthMonitorService"
                    );

                    _logger.LogWarning("Auto-deactivated {Count} failing proxies", proxyIdsToDeactivate.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deactivate failing proxies");
            }
        }

        private async Task PerformCleanupAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var proxyService = scope.ServiceProvider.GetRequiredService<IProxyService>();
            var systemLog = scope.ServiceProvider.GetRequiredService<ISystemLogService>();

            try
            {
                _logger.LogInformation("Starting proxy data cleanup...");

                // Cleanup old usage logs (older than 30 days)
                await proxyService.CleanupOldUsageLogsAsync(TimeSpan.FromDays(30));

                // Cleanup old health checks (older than 7 days)
                await proxyService.CleanupOldHealthChecksAsync(TimeSpan.FromDays(7));

                // Delete inactive proxies that have been failing for more than 7 days
                var deletedCount = await proxyService.BulkDeleteInactiveProxiesAsync(TimeSpan.FromDays(7));

                if (deletedCount > 0)
                {
                    await systemLog.LogInfoAsync(
                        $"Cleanup completed. Deleted {deletedCount} inactive proxies",
                        "proxy_cleanup",
                        "ProxyHealthMonitorService"
                    );
                }

                _logger.LogInformation("Proxy cleanup completed. Deleted {Count} inactive proxies", deletedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to perform proxy cleanup");
                
                try
                {
                    await systemLog.LogErrorAsync(ex,
                        "Proxy cleanup failed",
                        "proxy_cleanup_error",
                        "ProxyHealthMonitorService"
                    );
                }
                catch
                {
                    // Ignore if we can't even log the error
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Proxy Health Monitor Service is stopping...");
            await base.StopAsync(cancellationToken);
        }
    }
}