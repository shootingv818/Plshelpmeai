using IvaScanner.Core.Models;

namespace IvaScanner.Master.Services
{
    public interface ITaskDistributionService
    {
        Task CreateTasksForJobAsync(string jobId, string cardNumber, List<string> phoneNumbers);
        Task<TaskAssignment?> GetNextTaskAsync(string workerId);
        Task<bool> CompleteTaskAsync(string taskId, string workerId, bool success, string? result = null, string? errorMessage = null);
        Task<bool> FailTaskAsync(string taskId, string workerId, string errorMessage);
        Task<IEnumerable<ScanTask>> GetExpiredTasksAsync();
        Task ReturnExpiredTasksToQueueAsync();
        Task<int> GetPendingTaskCountAsync();
        Task<int> GetInProgressTaskCountAsync();
        Task<ScanTask?> GetTaskAsync(string taskId);
        Task<bool> CancelTasksForJobAsync(string jobId);
    }
}