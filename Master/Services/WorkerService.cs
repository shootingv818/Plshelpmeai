using IvaScanner.Core.Models;
using IvaScanner.Master.Data;
using Microsoft.EntityFrameworkCore;

namespace IvaScanner.Master.Services
{
    public class WorkerService : IWorkerService
    {
        private readonly MasterDbContext _context;
        private readonly ILogger<WorkerService> _logger;
        private readonly ISignalRNotificationService _signalRNotification;

        public WorkerService(
            MasterDbContext context, 
            ILogger<WorkerService> logger,
            ISignalRNotificationService signalRNotification)
        {
            _context = context;
            _logger = logger;
            _signalRNotification = signalRNotification;
        }

        public async Task<Worker?> GetWorkerAsync(string workerId)
        {
            return await _context.Workers
                .Include(w => w.IvaAccount)
                .FirstOrDefaultAsync(w => w.Id == workerId);
        }

        public async Task<IEnumerable<Worker>> GetAllWorkersAsync()
        {
            return await _context.Workers
                .Include(w => w.IvaAccount)
                .OrderByDescending(w => w.LastHeartbeat)
                .ToListAsync();
        }

        public async Task<IEnumerable<Worker>> GetOnlineWorkersAsync()
        {
            return await _context.Workers
                .Include(w => w.IvaAccount)
                .Where(w => w.Status == WorkerStatus.Online || w.Status == WorkerStatus.Working)
                .OrderByDescending(w => w.LastHeartbeat)
                .ToListAsync();
        }

        public async Task<Worker> RegisterWorkerAsync(WorkerRegistrationRequest request)
        {
            var existingWorker = await _context.Workers.FindAsync(request.WorkerId);
            
            if (existingWorker != null)
            {
                // Update existing worker
                existingWorker.Name = request.WorkerName;
                existingWorker.ProxyUrl = request.ProxyUrl;
                existingWorker.IvaAccountId = request.IvaAccountId;
                existingWorker.Status = WorkerStatus.Online;
                existingWorker.LastHeartbeat = DateTime.UtcNow;
                existingWorker.UpdatedAt = DateTime.UtcNow;
                existingWorker.LastError = null;

                await _context.SaveChangesAsync();
                _logger.LogInformation("Worker {WorkerId} re-registered", request.WorkerId);
                return existingWorker;
            }
            
            var worker = new Worker
            {
                Id = request.WorkerId,
                Name = request.WorkerName,
                ProxyUrl = request.ProxyUrl,
                IvaAccountId = request.IvaAccountId,
                Status = WorkerStatus.Online,
                LastHeartbeat = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Workers.Add(worker);
            await _context.SaveChangesAsync();
            
            // Send SignalR notification
            await _signalRNotification.NotifyWorkerRegisteredAsync(worker);
            
            _logger.LogInformation("Worker {WorkerId} registered successfully", request.WorkerId);
            return worker;
        }

        public async Task<Worker> UpdateWorkerHeartbeatAsync(WorkerHeartbeatRequest request)
        {
            var worker = await _context.Workers.FindAsync(request.WorkerId);
            if (worker == null)
            {
                throw new ArgumentException($"Worker {request.WorkerId} not found");
            }

            worker.Status = request.Status;
            worker.LastHeartbeat = DateTime.UtcNow;
            worker.UpdatedAt = DateTime.UtcNow;
            worker.Latency = request.Latency;
            worker.CurrentTaskId = request.CurrentTaskId;
            
            if (!string.IsNullOrEmpty(request.LastError))
            {
                worker.LastError = request.LastError;
            }

            await _context.SaveChangesAsync();
            
            // Send SignalR notification
            await _signalRNotification.NotifyWorkerStatusChangedAsync(worker);
            
            return worker;
        }

        public async Task<bool> DeregisterWorkerAsync(string workerId)
        {
            var worker = await _context.Workers.FindAsync(workerId);
            if (worker == null)
            {
                return false;
            }

            // Mark any assigned tasks as failed and return to queue
            var assignedTasks = await _context.ScanTasks
                .Where(t => t.WorkerId == workerId && t.Status == TaskStatus.InProgress)
                .ToListAsync();

            foreach (var task in assignedTasks)
            {
                task.Status = TaskStatus.Pending;
                task.WorkerId = null;
                task.LeaseExpiry = null;
                task.ErrorMessage = "Worker deregistered";
            }

            _context.Workers.Remove(worker);
            await _context.SaveChangesAsync();
            
            // Send SignalR notification
            await _signalRNotification.NotifyWorkerDeregisteredAsync(workerId);
            
            _logger.LogInformation("Worker {WorkerId} deregistered, {TaskCount} tasks returned to queue", 
                workerId, assignedTasks.Count);
            return true;
        }

        public async Task<IEnumerable<Worker>> GetStaleWorkersAsync(TimeSpan timeout)
        {
            var cutoff = DateTime.UtcNow - timeout;
            return await _context.Workers
                .Where(w => w.LastHeartbeat < cutoff && w.Status != WorkerStatus.Offline)
                .ToListAsync();
        }

        public async Task MarkWorkerOfflineAsync(string workerId, string? error = null)
        {
            var worker = await _context.Workers.FindAsync(workerId);
            if (worker != null)
            {
                worker.Status = WorkerStatus.Offline;
                worker.UpdatedAt = DateTime.UtcNow;
                worker.CurrentTaskId = null;
                
                if (!string.IsNullOrEmpty(error))
                {
                    worker.LastError = error;
                }

                // Return assigned tasks to queue
                var assignedTasks = await _context.ScanTasks
                    .Where(t => t.WorkerId == workerId && t.Status == TaskStatus.InProgress)
                    .ToListAsync();

                foreach (var task in assignedTasks)
                {
                    task.Status = TaskStatus.Pending;
                    task.WorkerId = null;
                    task.LeaseExpiry = null;
                    task.ErrorMessage = error ?? "Worker went offline";
                }

                await _context.SaveChangesAsync();
                
                // Send SignalR notification
                await _signalRNotification.NotifyWorkerStatusChangedAsync(worker);
                
                _logger.LogWarning("Worker {WorkerId} marked offline, {TaskCount} tasks returned to queue", 
                    workerId, assignedTasks.Count);
            }
        }

        public async Task<WorkerStatus> GetWorkerStatusAsync(string workerId)
        {
            var worker = await _context.Workers.FindAsync(workerId);
            return worker?.Status ?? WorkerStatus.Offline;
        }

        public async Task UpdateWorkerStatusAsync(string workerId, WorkerStatus status, string? error = null)
        {
            var worker = await _context.Workers.FindAsync(workerId);
            if (worker != null)
            {
                worker.Status = status;
                worker.UpdatedAt = DateTime.UtcNow;
                
                if (!string.IsNullOrEmpty(error))
                {
                    worker.LastError = error;
                }

                await _context.SaveChangesAsync();
            }
        }

        public async Task IncrementTaskCountersAsync(string workerId, bool success)
        {
            var worker = await _context.Workers.FindAsync(workerId);
            if (worker != null)
            {
                if (success)
                {
                    worker.TasksCompleted++;
                }
                else
                {
                    worker.TasksFailed++;
                }

                worker.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        public async Task<Worker?> GetWorkerWithLeastLoadAsync()
        {
            return await _context.Workers
                .Include(w => w.IvaAccount)
                .Where(w => w.Status == WorkerStatus.Online && w.CurrentTaskId == null)
                .OrderBy(w => w.TasksCompleted + w.TasksFailed) // Simple load balancing
                .FirstOrDefaultAsync();
        }

        public async Task<int> GetActiveWorkerCountAsync()
        {
            return await _context.Workers
                .CountAsync(w => w.Status == WorkerStatus.Online || w.Status == WorkerStatus.Working);
        }
    }
}