using IvaScanner.Core.Models;
using IvaScanner.Master.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace IvaScanner.Master.Services
{
    public class SignalRNotificationService : ISignalRNotificationService
    {
        private readonly IHubContext<DashboardHub> _hubContext;
        private readonly ILogger<SignalRNotificationService> _logger;

        public SignalRNotificationService(
            IHubContext<DashboardHub> hubContext,
            ILogger<SignalRNotificationService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task NotifySystemStatusAsync(object systemStatus)
        {
            try
            {
                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("SystemStatusUpdated", systemStatus);
                
                _logger.LogDebug("Sent system status update to dashboard clients");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending system status update");
            }
        }

        public async Task NotifySystemAlertAsync(string message, string type = "info")
        {
            try
            {
                var alert = new
                {
                    Message = message,
                    Type = type,
                    Timestamp = DateTime.UtcNow
                };

                await _hubContext.Clients.Group("SystemUpdates")
                    .SendAsync("SystemAlert", alert);
                
                _logger.LogDebug("Sent system alert: {Message} (type: {Type})", message, type);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending system alert");
            }
        }

        public async Task NotifyConnectionCountAsync(int count)
        {
            try
            {
                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("ConnectionCountUpdated", count);
                
                _logger.LogDebug("Sent connection count update: {Count}", count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending connection count update");
            }
        }

        public async Task NotifyWorkerStatusChangedAsync(Worker worker)
        {
            try
            {
                var workerUpdate = new
                {
                    Id = worker.Id,
                    Name = worker.Name,
                    Status = worker.Status.ToString(),
                    LastHeartbeat = worker.LastHeartbeat,
                    CurrentTaskId = worker.CurrentTaskId,
                    TasksCompleted = worker.TasksCompleted,
                    TasksFailed = worker.TasksFailed,
                    Latency = worker.Latency,
                    LastError = worker.LastError,
                    UpdatedAt = worker.UpdatedAt
                };

                await _hubContext.Clients.Group("WorkerUpdates")
                    .SendAsync("WorkerStatusChanged", workerUpdate);
                
                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("WorkerStatusChanged", workerUpdate);
                
                _logger.LogDebug("Sent worker status change for {WorkerId}: {Status}", 
                    worker.Id, worker.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending worker status change for {WorkerId}", worker.Id);
            }
        }

        public async Task NotifyWorkerRegisteredAsync(Worker worker)
        {
            try
            {
                var workerData = new
                {
                    Id = worker.Id,
                    Name = worker.Name,
                    Status = worker.Status.ToString(),
                    CreatedAt = worker.CreatedAt,
                    IvaAccountId = worker.IvaAccountId,
                    ProxyUrl = worker.ProxyUrl
                };

                await _hubContext.Clients.Group("WorkerUpdates")
                    .SendAsync("WorkerRegistered", workerData);
                
                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("WorkerRegistered", workerData);
                
                _logger.LogInformation("Sent worker registration notification for {WorkerId}", worker.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending worker registration notification for {WorkerId}", worker.Id);
            }
        }

        public async Task NotifyWorkerDeregisteredAsync(string workerId)
        {
            try
            {
                await _hubContext.Clients.Group("WorkerUpdates")
                    .SendAsync("WorkerDeregistered", workerId);
                
                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("WorkerDeregistered", workerId);
                
                _logger.LogInformation("Sent worker deregistration notification for {WorkerId}", workerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending worker deregistration notification for {WorkerId}", workerId);
            }
        }

        public async Task NotifyWorkersStatsAsync(object stats)
        {
            try
            {
                await _hubContext.Clients.Group("WorkerUpdates")
                    .SendAsync("WorkersStatsUpdated", stats);
                
                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("WorkersStatsUpdated", stats);
                
                _logger.LogDebug("Sent workers stats update");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending workers stats update");
            }
        }

        public async Task NotifyJobCreatedAsync(ScanJob job)
        {
            try
            {
                var jobData = new
                {
                    Id = job.Id,
                    CardNumber = job.CardNumber,
                    Status = job.Status.ToString(),
                    Progress = job.Progress,
                    TotalTasks = job.TotalTasks,
                    CompletedTasks = job.CompletedTasks,
                    FailedTasks = job.FailedTasks,
                    CreatedAt = job.CreatedAt,
                    CreatedBy = job.CreatedBy
                };

                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("JobCreated", jobData);
                
                _logger.LogInformation("Sent job creation notification for {JobId}", job.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending job creation notification for {JobId}", job.Id);
            }
        }

        public async Task NotifyJobStatusChangedAsync(string jobId, ScanJobStatus status)
        {
            try
            {
                var statusUpdate = new
                {
                    JobId = jobId,
                    Status = status.ToString(),
                    Timestamp = DateTime.UtcNow
                };

                await _hubContext.Clients.Group($"Job-{jobId}")
                    .SendAsync("JobStatusChanged", statusUpdate);
                
                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("JobStatusChanged", statusUpdate);
                
                _logger.LogDebug("Sent job status change for {JobId}: {Status}", jobId, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending job status change for {JobId}", jobId);
            }
        }

        public async Task NotifyJobProgressUpdatedAsync(string jobId, JobProgress progress)
        {
            try
            {
                await _hubContext.Clients.Group($"Job-{jobId}")
                    .SendAsync("JobProgressUpdated", progress);
                
                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("JobProgressUpdated", progress);
                
                _logger.LogDebug("Sent job progress update for {JobId}: {Progress}%", 
                    jobId, progress.ProgressPercentage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending job progress update for {JobId}", jobId);
            }
        }

        public async Task NotifyJobCompletedAsync(string jobId, object result)
        {
            try
            {
                var completionData = new
                {
                    JobId = jobId,
                    Result = result,
                    CompletedAt = DateTime.UtcNow
                };

                await _hubContext.Clients.Group($"Job-{jobId}")
                    .SendAsync("JobCompleted", completionData);
                
                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("JobCompleted", completionData);
                
                _logger.LogInformation("Sent job completion notification for {JobId}", jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending job completion notification for {JobId}", jobId);
            }
        }

        public async Task NotifyTaskAssignedAsync(string taskId, string workerId)
        {
            try
            {
                var assignment = new
                {
                    TaskId = taskId,
                    WorkerId = workerId,
                    AssignedAt = DateTime.UtcNow
                };

                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("TaskAssigned", assignment);
                
                _logger.LogDebug("Sent task assignment notification: {TaskId} -> {WorkerId}", 
                    taskId, workerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending task assignment notification");
            }
        }

        public async Task NotifyTaskCompletedAsync(string taskId, bool success, string? result = null)
        {
            try
            {
                var completion = new
                {
                    TaskId = taskId,
                    Success = success,
                    Result = result,
                    CompletedAt = DateTime.UtcNow
                };

                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("TaskCompleted", completion);
                
                _logger.LogDebug("Sent task completion notification: {TaskId} (success: {Success})", 
                    taskId, success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending task completion notification");
            }
        }

        public async Task NotifyNewLogAsync(SystemLog log)
        {
            try
            {
                var logData = new
                {
                    Id = log.Id,
                    Level = log.Level.ToString(),
                    Message = log.Message,
                    Source = log.Source,
                    Category = log.Category,
                    Timestamp = log.Timestamp,
                    WorkerId = log.WorkerId,
                    JobId = log.JobId,
                    TaskId = log.TaskId
                };

                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("NewLogEntry", logData);
                
                // Send high-priority logs as alerts
                if (log.Level == LogLevel.Error || log.Level == LogLevel.Critical)
                {
                    await NotifySystemAlertAsync(log.Message, "error");
                }
                else if (log.Level == LogLevel.Warning)
                {
                    await NotifySystemAlertAsync(log.Message, "warning");
                }
                
                _logger.LogDebug("Sent new log entry notification: {Level} - {Message}", 
                    log.Level, log.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending new log entry notification");
            }
        }

        public async Task NotifyLogStatsAsync(object stats)
        {
            try
            {
                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("LogStatsUpdated", stats);
                
                _logger.LogDebug("Sent log stats update");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending log stats update");
            }
        }

        public async Task NotifyAccountStatusChangedAsync(IvaAccount account)
        {
            try
            {
                var accountData = new
                {
                    Id = account.Id,
                    PhoneNumber = account.PhoneNumber,
                    Status = account.Status.ToString(),
                    IsActive = account.IsActive,
                    AssignedWorkerId = account.AssignedWorkerId,
                    LastUsed = account.LastUsed,
                    UpdatedAt = account.UpdatedAt
                };

                await _hubContext.Clients.Group("Dashboard")
                    .SendAsync("AccountStatusChanged", accountData);
                
                _logger.LogDebug("Sent account status change for {AccountId}: {Status}", 
                    account.Id, account.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending account status change for {AccountId}", account.Id);
            }
        }
    }
}

        public async Task NotifySystemLogAsync(SystemLog log)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("SystemLog", new
                {
                    id = log.Id,
                    level = log.Level.ToString(),
                    message = log.Message,
                    component = log.Component,
                    context = log.Context,
                    timestamp = log.CreatedAt,
                    metadata = log.Metadata
                });
                
                _logger.LogDebug("Sent system log notification for log {LogId}", log.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SystemLog notification for log {LogId}", log.Id);
            }
        }

        public async Task NotifyLogSystemHealthAsync(LogSystemHealth health)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("LogSystemHealth", health);
                _logger.LogDebug("Sent log system health notification");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send LogSystemHealth notification");
            }
        }