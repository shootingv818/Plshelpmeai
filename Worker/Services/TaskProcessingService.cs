using Microsoft.Extensions.Hosting;
using IvaScanner.Worker.Configuration;
using IvaScanner.Core.Models;
using System.Collections.Concurrent;

namespace IvaScanner.Worker.Services;

public class TaskProcessingService : BackgroundService
{
    private readonly ILogger<TaskProcessingService> _logger;
    private readonly IWorkerStateManager _stateManager;
    private readonly IMasterApiClient _masterClient;
    private readonly ITaskExecutor _taskExecutor;
    private readonly IProxyManager _proxyManager;
    private readonly WorkerConfiguration _config;
    
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeTasks = new();
    private readonly SemaphoreSlim _concurrencySemaphore;

    public TaskProcessingService(
        ILogger<TaskProcessingService> logger,
        IWorkerStateManager stateManager,
        IMasterApiClient masterClient,
        ITaskExecutor taskExecutor,
        IProxyManager proxyManager,
        Microsoft.Extensions.Options.IOptions<WorkerConfiguration> config)
    {
        _logger = logger;
        _stateManager = stateManager;
        _masterClient = masterClient;
        _taskExecutor = taskExecutor;
        _proxyManager = proxyManager;
        _config = config.Value;
        _concurrencySemaphore = new SemaphoreSlim(_config.MaxConcurrentTasks, _config.MaxConcurrentTasks);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Task Processing service with max {MaxTasks} concurrent tasks", 
            _config.MaxConcurrentTasks);

        // Wait for worker to be online
        while (_stateManager.Status != WorkerStatus.Online && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Skip if worker is not available for tasks
                if (_stateManager.Status != WorkerStatus.Online && 
                    _stateManager.Status != WorkerStatus.Busy)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }

                // Check if we can accept more tasks
                if (!await _concurrencySemaphore.WaitAsync(100, stoppingToken))
                {
                    continue; // No slots available, try again
                }

                try
                {
                    // Get next task from master
                    var task = await _masterClient.GetNextTaskAsync(_stateManager.WorkerId, stoppingToken);
                    
                    if (task == null)
                    {
                        _concurrencySemaphore.Release();
                        
                        // Set status to online if no active tasks
                        if (_activeTasks.IsEmpty && _stateManager.Status == WorkerStatus.Busy)
                        {
                            await _stateManager.SetStatusAsync(WorkerStatus.Online);
                        }
                        
                        // No tasks available, wait before checking again
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                        continue;
                    }

                    // Set status to busy
                    await _stateManager.SetStatusAsync(WorkerStatus.Busy);
                    await _stateManager.IncrementActiveTasksAsync();

                    // Process task in background
                    var taskCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    _activeTasks.TryAdd(task.TaskId, taskCts);

                    _ = ProcessTaskAsync(task, taskCts.Token);
                }
                catch
                {
                    _concurrencySemaphore.Release();
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in task processing loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("Task processing service stopping...");
        
        // Cancel all active tasks
        foreach (var kvp in _activeTasks)
        {
            kvp.Value.Cancel();
        }

        // Wait for tasks to complete or timeout
        var timeout = DateTime.UtcNow.Add(_config.ShutdownTimeout);
        while (!_activeTasks.IsEmpty && DateTime.UtcNow < timeout)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        _logger.LogInformation("Task processing service stopped with {RemainingTasks} remaining tasks", 
            _activeTasks.Count);
    }

    private async Task ProcessTaskAsync(ScanTaskDto task, CancellationToken cancellationToken)
    {
        var taskId = task.TaskId;
        
        try
        {
            _logger.LogInformation("Starting task {TaskId} - Type: {TaskType}, CVVs: {CvvCount}", 
                taskId, task.TaskType, task.CvvList.Count);

            // Set task timeout
            using var timeoutCts = new CancellationTokenSource(_config.TaskTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            // Execute the task
            var result = await _taskExecutor.ExecuteAsync(task, combinedCts.Token);

            // Report completion to master
            if (result.Success)
            {
                var completionRequest = new TaskCompletionRequest
                {
                    TaskId = taskId,
                    WorkerId = _stateManager.WorkerId,
                    Results = result.Results,
                    CompletedAt = DateTime.UtcNow,
                    ProcessingTime = result.Duration,
                    ProcessedItems = result.ProcessedItems
                };

                var reported = await _masterClient.CompleteTaskAsync(completionRequest, cancellationToken);
                
                if (reported)
                {
                    await _stateManager.IncrementCompletedTasksAsync();
                    _logger.LogInformation("Task {TaskId} completed successfully in {Duration}ms", 
                        taskId, result.Duration.TotalMilliseconds);
                }
                else
                {
                    _logger.LogWarning("Task {TaskId} completed but failed to report to master", taskId);
                }
            }
            else
            {
                var failureRequest = new TaskFailureRequest
                {
                    TaskId = taskId,
                    WorkerId = _stateManager.WorkerId,
                    ErrorMessage = string.Join("; ", result.Errors),
                    FailedAt = DateTime.UtcNow,
                    ProcessingTime = result.Duration,
                    ProcessedItems = result.ProcessedItems
                };

                await _masterClient.ReportTaskFailureAsync(failureRequest, cancellationToken);
                await _stateManager.IncrementFailedTasksAsync();
                
                _logger.LogWarning("Task {TaskId} failed: {Errors}", 
                    taskId, string.Join("; ", result.Errors));
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Task {TaskId} was cancelled", taskId);
            
            var failureRequest = new TaskFailureRequest
            {
                TaskId = taskId,
                WorkerId = _stateManager.WorkerId,
                ErrorMessage = "Task was cancelled",
                FailedAt = DateTime.UtcNow,
                ProcessingTime = TimeSpan.Zero,
                ProcessedItems = 0
            };

            try
            {
                await _masterClient.ReportTaskFailureAsync(failureRequest, CancellationToken.None);
                await _stateManager.IncrementFailedTasksAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reporting task cancellation for {TaskId}", taskId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing task {TaskId}", taskId);
            
            var failureRequest = new TaskFailureRequest
            {
                TaskId = taskId,
                WorkerId = _stateManager.WorkerId,
                ErrorMessage = ex.Message,
                FailedAt = DateTime.UtcNow,
                ProcessingTime = TimeSpan.Zero,
                ProcessedItems = 0
            };

            try
            {
                await _masterClient.ReportTaskFailureAsync(failureRequest, CancellationToken.None);
                await _stateManager.IncrementFailedTasksAsync();
            }
            catch (Exception reportEx)
            {
                _logger.LogError(reportEx, "Error reporting task failure for {TaskId}", taskId);
            }
        }
        finally
        {
            // Cleanup
            _activeTasks.TryRemove(taskId, out var cts);
            cts?.Dispose();
            
            await _stateManager.DecrementActiveTasksAsync();
            _concurrencySemaphore.Release();

            // Update status if no more active tasks
            if (_activeTasks.IsEmpty && _stateManager.Status == WorkerStatus.Busy)
            {
                await _stateManager.SetStatusAsync(WorkerStatus.Online);
            }
        }
    }

    public override void Dispose()
    {
        _concurrencySemaphore?.Dispose();
        
        foreach (var kvp in _activeTasks)
        {
            kvp.Value?.Dispose();
        }
        
        base.Dispose();
    }
}