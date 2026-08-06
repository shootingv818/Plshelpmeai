using IvaScanner.Core.Models;

namespace IvaScanner.Master.Services
{
    public interface ISignalRNotificationService
    {
        // System notifications
        Task NotifySystemStatusAsync(object systemStatus);
        Task NotifySystemAlertAsync(string message, string type = "info");
        Task NotifyConnectionCountAsync(int count);
        Task NotifySystemLogAsync(SystemLog log);
        Task NotifyLogSystemHealthAsync(LogSystemHealth health);

        // Worker notifications
        Task NotifyWorkerStatusChangedAsync(Worker worker);
        Task NotifyWorkerRegisteredAsync(Worker worker);
        Task NotifyWorkerDeregisteredAsync(string workerId);
        Task NotifyWorkersStatsAsync(object stats);

        // Job notifications
        Task NotifyJobCreatedAsync(ScanJob job);
        Task NotifyJobStatusChangedAsync(string jobId, ScanJobStatus status);
        Task NotifyJobProgressUpdatedAsync(string jobId, JobProgress progress);
        Task NotifyJobCompletedAsync(string jobId, object result);

        // Task notifications
        Task NotifyTaskAssignedAsync(string taskId, string workerId);
        Task NotifyTaskCompletedAsync(string taskId, bool success, string? result = null);

        // Log notifications
        Task NotifyNewLogAsync(SystemLog log);
        Task NotifyLogStatsAsync(object stats);

        // Account notifications
        Task NotifyAccountStatusChangedAsync(IvaAccount account);
    }
}