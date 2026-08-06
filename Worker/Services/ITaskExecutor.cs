using IvaScanner.Core.Models;

namespace IvaScanner.Worker.Services;

public interface ITaskExecutor
{
    Task<TaskExecutionResult> ExecuteAsync(ScanTaskDto task, CancellationToken cancellationToken = default);
}

public class TaskExecutor : ITaskExecutor
{
    private readonly ILogger<TaskExecutor> _logger;
    private readonly IIvaWorkerClient _ivaClient;
    private readonly IProxyManager _proxyManager;

    public TaskExecutor(
        ILogger<TaskExecutor> logger,
        IIvaWorkerClient ivaClient,
        IProxyManager proxyManager)
    {
        _logger = logger;
        _ivaClient = ivaClient;
        _proxyManager = proxyManager;
    }

    public async Task<TaskExecutionResult> ExecuteAsync(ScanTaskDto task, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var results = new List<IvaResult>();
        var errors = new List<string>();

        try
        {
            _logger.LogInformation("Starting task execution: {TaskId} - {TaskType}", task.TaskId, task.TaskType);

            // Get proxy if needed
            ProxyServerDto? proxy = null;
            if (_proxyManager.IsEnabled)
            {
                proxy = await _proxyManager.GetProxyAsync();
                if (proxy != null)
                {
                    _logger.LogDebug("Using proxy {ProxyId} for task {TaskId}", proxy.Id, task.TaskId);
                }
            }

            // Set up IVA client with account and proxy
            await _ivaClient.ConfigureAsync(task.IvaAccount, proxy, cancellationToken);

            switch (task.TaskType.ToLower())
            {
                case "expiry_detection":
                    results = await ExecuteExpiryDetectionAsync(task, cancellationToken);
                    break;
                    
                case "cvv_scan":
                    results = await ExecuteCvvScanAsync(task, cancellationToken);
                    break;
                    
                default:
                    throw new NotSupportedException($"Task type '{task.TaskType}' is not supported");
            }

            var endTime = DateTime.UtcNow;
            var duration = endTime - startTime;

            _logger.LogInformation("Task {TaskId} completed successfully in {Duration}ms with {ResultCount} results", 
                task.TaskId, duration.TotalMilliseconds, results.Count);

            return new TaskExecutionResult
            {
                Success = true,
                Results = results,
                Errors = errors,
                StartTime = startTime,
                EndTime = endTime,
                Duration = duration,
                ProcessedItems = results.Count
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Task {TaskId} was cancelled", task.TaskId);
            errors.Add("Task was cancelled");
            
            return new TaskExecutionResult
            {
                Success = false,
                Results = results,
                Errors = errors,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Duration = DateTime.UtcNow - startTime,
                ProcessedItems = results.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Task {TaskId} failed with error: {Error}", task.TaskId, ex.Message);
            errors.Add($"Execution error: {ex.Message}");
            
            return new TaskExecutionResult
            {
                Success = false,
                Results = results,
                Errors = errors,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Duration = DateTime.UtcNow - startTime,
                ProcessedItems = results.Count
            };
        }
    }

    private async Task<List<IvaResult>> ExecuteExpiryDetectionAsync(ScanTaskDto task, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing expiry detection for task {TaskId}", task.TaskId);
        
        var results = new List<IvaResult>();
        
        // Process CVV list to detect expiry patterns
        foreach (var cvvGroup in task.CvvList.GroupBy(c => c.Substring(0, Math.Min(4, c.Length))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Test a sample CVV from each group to detect expiry date pattern
            var sampleCvv = cvvGroup.First();
            
            try
            {
                var result = await _ivaClient.TestCvvAsync(sampleCvv, cancellationToken);
                
                if (result != null)
                {
                    results.Add(result);
                    
                    // If we find a successful result, we can infer the expiry pattern
                    if (result.IsSuccessful && !string.IsNullOrEmpty(result.ExpiryDate))
                    {
                        _logger.LogInformation("Expiry pattern detected: {ExpiryDate} for CVV group {Group}", 
                            result.ExpiryDate, cvvGroup.Key);
                    }
                }
                
                // Add delay between requests
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to test CVV {Cvv} in expiry detection", sampleCvv);
            }
        }
        
        return results;
    }

    private async Task<List<IvaResult>> ExecuteCvvScanAsync(ScanTaskDto task, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing CVV scan for task {TaskId} with {Count} CVVs", 
            task.TaskId, task.CvvList.Count);
        
        var results = new List<IvaResult>();
        
        // Process each CVV in the task
        for (int i = 0; i < task.CvvList.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var cvv = task.CvvList[i];
            
            try
            {
                var result = await _ivaClient.ScanCvvAsync(cvv, cancellationToken);
                
                if (result != null)
                {
                    results.Add(result);
                    
                    _logger.LogDebug("CVV {Cvv} processed - Success: {Success}", 
                        cvv, result.IsSuccessful);
                }
                
                // Add delay between requests
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process CVV {Cvv}", cvv);
                
                // Add failed result
                results.Add(new IvaResult
                {
                    CardNumber = cvv,
                    IsSuccessful = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        
        return results;
    }
}

public class TaskExecutionResult
{
    public bool Success { get; set; }
    public List<IvaResult> Results { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public int ProcessedItems { get; set; }
}