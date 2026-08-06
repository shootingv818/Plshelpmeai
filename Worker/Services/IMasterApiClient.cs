using IvaScanner.Core.Models;
using System.Text.Json;

namespace IvaScanner.Worker.Services;

public interface IMasterApiClient
{
    Task<bool> RegisterWorkerAsync(WorkerRegistrationRequest request, CancellationToken cancellationToken = default);
    Task<bool> SendHeartbeatAsync(WorkerHeartbeatRequest request, CancellationToken cancellationToken = default);
    Task<ScanTaskDto?> GetNextTaskAsync(string workerId, CancellationToken cancellationToken = default);
    Task<bool> CompleteTaskAsync(TaskCompletionRequest request, CancellationToken cancellationToken = default);
    Task<bool> ReportTaskFailureAsync(TaskFailureRequest request, CancellationToken cancellationToken = default);
    Task<ProxyServerDto?> GetProxyAsync(string workerId, CancellationToken cancellationToken = default);
    Task<bool> ReportProxyStatusAsync(ProxyStatusReport report, CancellationToken cancellationToken = default);
}

public class MasterApiClient : IMasterApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MasterApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public MasterApiClient(HttpClient httpClient, ILogger<MasterApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task<bool> RegisterWorkerAsync(WorkerRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Registering worker: {WorkerId}", request.WorkerId);
            
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("/api/workers/register", content, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Worker registered successfully: {WorkerId}", request.WorkerId);
                return true;
            }
            
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Worker registration failed: {StatusCode} - {Content}", 
                response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering worker: {WorkerId}", request.WorkerId);
            return false;
        }
    }

    public async Task<bool> SendHeartbeatAsync(WorkerHeartbeatRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogTrace("Sending heartbeat for worker: {WorkerId}", request.WorkerId);
            
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("/api/workers/heartbeat", content, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogTrace("Heartbeat sent successfully: {WorkerId}", request.WorkerId);
                return true;
            }
            
            _logger.LogWarning("Heartbeat failed: {StatusCode} for worker {WorkerId}", 
                response.StatusCode, request.WorkerId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending heartbeat for worker: {WorkerId}", request.WorkerId);
            return false;
        }
    }

    public async Task<ScanTaskDto?> GetNextTaskAsync(string workerId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting next task for worker: {WorkerId}", workerId);
            
            var response = await _httpClient.GetAsync($"/api/workers/{workerId}/next-task", cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                
                if (string.IsNullOrWhiteSpace(json) || json == "null")
                {
                    _logger.LogTrace("No tasks available for worker: {WorkerId}", workerId);
                    return null;
                }
                
                var task = JsonSerializer.Deserialize<ScanTaskDto>(json, _jsonOptions);
                _logger.LogInformation("Received task {TaskId} for worker {WorkerId}", 
                    task?.TaskId, workerId);
                return task;
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                _logger.LogTrace("No tasks available for worker: {WorkerId}", workerId);
                return null;
            }
            
            _logger.LogWarning("Failed to get next task: {StatusCode} for worker {WorkerId}", 
                response.StatusCode, workerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting next task for worker: {WorkerId}", workerId);
            return null;
        }
    }

    public async Task<bool> CompleteTaskAsync(TaskCompletionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Completing task {TaskId} for worker {WorkerId}", 
                request.TaskId, request.WorkerId);
            
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("/api/tasks/complete", content, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Task completed successfully: {TaskId}", request.TaskId);
                return true;
            }
            
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Task completion failed: {StatusCode} - {Content}", 
                response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing task {TaskId}", request.TaskId);
            return false;
        }
    }

    public async Task<bool> ReportTaskFailureAsync(TaskFailureRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Reporting task failure {TaskId} for worker {WorkerId}", 
                request.TaskId, request.WorkerId);
            
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("/api/tasks/failure", content, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Task failure reported successfully: {TaskId}", request.TaskId);
                return true;
            }
            
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Task failure report failed: {StatusCode} - {Content}", 
                response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reporting task failure {TaskId}", request.TaskId);
            return false;
        }
    }

    public async Task<ProxyServerDto?> GetProxyAsync(string workerId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting proxy for worker: {WorkerId}", workerId);
            
            var response = await _httpClient.GetAsync($"/api/workers/{workerId}/proxy", cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                
                if (string.IsNullOrWhiteSpace(json) || json == "null")
                {
                    _logger.LogTrace("No proxy available for worker: {WorkerId}", workerId);
                    return null;
                }
                
                var proxy = JsonSerializer.Deserialize<ProxyServerDto>(json, _jsonOptions);
                _logger.LogInformation("Received proxy {ProxyId} for worker {WorkerId}", 
                    proxy?.Id, workerId);
                return proxy;
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                _logger.LogTrace("No proxy available for worker: {WorkerId}", workerId);
                return null;
            }
            
            _logger.LogWarning("Failed to get proxy: {StatusCode} for worker {WorkerId}", 
                response.StatusCode, workerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting proxy for worker: {WorkerId}", workerId);
            return null;
        }
    }

    public async Task<bool> ReportProxyStatusAsync(ProxyStatusReport report, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Reporting proxy status for proxy {ProxyId}", report.ProxyId);
            
            var json = JsonSerializer.Serialize(report, _jsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("/api/proxy/status", content, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Proxy status reported successfully: {ProxyId}", report.ProxyId);
                return true;
            }
            
            _logger.LogWarning("Proxy status report failed: {StatusCode} for proxy {ProxyId}", 
                response.StatusCode, report.ProxyId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reporting proxy status {ProxyId}", report.ProxyId);
            return false;
        }
    }
}