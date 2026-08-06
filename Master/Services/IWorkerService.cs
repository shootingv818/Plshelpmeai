using IvaScanner.Core.Models;

namespace IvaScanner.Master.Services
{
    public interface IWorkerService
    {
        Task<Worker?> GetWorkerAsync(string workerId);
        Task<IEnumerable<Worker>> GetAllWorkersAsync();
        Task<IEnumerable<Worker>> GetOnlineWorkersAsync();
        Task<Worker> RegisterWorkerAsync(WorkerRegistrationRequest request);
        Task<Worker> UpdateWorkerHeartbeatAsync(WorkerHeartbeatRequest request);
        Task<bool> DeregisterWorkerAsync(string workerId);
        Task<IEnumerable<Worker>> GetStaleWorkersAsync(TimeSpan timeout);
        Task MarkWorkerOfflineAsync(string workerId, string? error = null);
        Task<WorkerStatus> GetWorkerStatusAsync(string workerId);
        Task UpdateWorkerStatusAsync(string workerId, WorkerStatus status, string? error = null);
        Task IncrementTaskCountersAsync(string workerId, bool success);
        Task<Worker?> GetWorkerWithLeastLoadAsync();
        Task<int> GetActiveWorkerCountAsync();
    }
}