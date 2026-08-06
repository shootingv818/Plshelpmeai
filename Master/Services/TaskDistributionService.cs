using IvaScanner.Core.Models;
using IvaScanner.Master.Data;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Text.Json;

namespace IvaScanner.Master.Services
{
    public class TaskDistributionService : ITaskDistributionService
    {
        private readonly MasterDbContext _context;
        private readonly IConnectionMultiplexer _redis;
        private readonly IConfiguration _config;
        private readonly ILogger<TaskDistributionService> _logger;
        private readonly IWorkerService _workerService;
        private readonly IIvaAccountService _accountService;
        private readonly ISignalRNotificationService _signalRNotification;

        private const string TASK_QUEUE_KEY = "tasks:pending";
        private const string TASK_PROCESSING_KEY_PREFIX = "tasks:processing:";
        private const int CVV_CHUNK_SIZE = 100; // 100 CVVs per task

        public TaskDistributionService(
            MasterDbContext context,
            IConnectionMultiplexer redis,
            IConfiguration config,
            ILogger<TaskDistributionService> logger,
            IWorkerService workerService,
            IIvaAccountService accountService,
            ISignalRNotificationService signalRNotification)
        {
            _context = context;
            _redis = redis;
            _config = config;
            _logger = logger;
            _workerService = workerService;
            _accountService = accountService;
            _signalRNotification = signalRNotification;
        }

        public async Task CreateTasksForJobAsync(string jobId, string cardNumber, List<string> phoneNumbers)
        {
            var database = _redis.GetDatabase();

            // Phase 1: Create expiry date detection task (60 combinations)
            // Years: 1406-1410 (5 years) * Months: 01-12 (12 months) = 60 tasks
            var expiryTask = new ScanTask
            {
                Id = Guid.NewGuid().ToString(),
                JobId = jobId,
                RangeStart = 0, // Special marker for expiry detection
                RangeEnd = 0,
                Status = TaskStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _context.ScanTasks.Add(expiryTask);

            // Phase 2: Create CVV scanning tasks (100 CVVs per task)
            // CVV range: 100-9999 = 9900 values = 99 tasks (100 CVVs each)
            var tasks = new List<ScanTask>();
            
            for (int cvvStart = 100; cvvStart <= 9999; cvvStart += CVV_CHUNK_SIZE)
            {
                int cvvEnd = Math.Min(cvvStart + CVV_CHUNK_SIZE - 1, 9999);
                
                var task = new ScanTask
                {
                    Id = Guid.NewGuid().ToString(),
                    JobId = jobId,
                    RangeStart = cvvStart,
                    RangeEnd = cvvEnd,
                    Status = TaskStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };

                tasks.Add(task);
            }

            _context.ScanTasks.AddRange(tasks);
            await _context.SaveChangesAsync();

            // Add tasks to Redis queue
            var allTasks = new List<ScanTask> { expiryTask };
            allTasks.AddRange(tasks);

            foreach (var task in allTasks)
            {
                var taskMessage = new
                {
                    TaskId = task.Id,
                    JobId = jobId,
                    CardNumber = cardNumber,
                    PhoneNumbers = phoneNumbers,
                    RangeStart = task.RangeStart,
                    RangeEnd = task.RangeEnd,
                    CreatedAt = task.CreatedAt
                };

                await database.StreamAddAsync(TASK_QUEUE_KEY, "task", JsonSerializer.Serialize(taskMessage));
            }

            _logger.LogInformation("Created {TaskCount} tasks for job {JobId} (1 expiry + {CvvTasks} CVV tasks)", 
                allTasks.Count, jobId, tasks.Count);
        }

        public async Task<TaskAssignment?> GetNextTaskAsync(string workerId)
        {
            var database = _redis.GetDatabase();
            var leaseTimeoutMinutes = _config.GetValue<int>("WorkerSettings:TaskLeaseTimeoutMinutes", 2);

            // Check if worker exists and is online
            var worker = await _workerService.GetWorkerAsync(workerId);
            if (worker == null || worker.Status != WorkerStatus.Online)
            {
                return null;
            }

            // Check if worker already has a task assigned
            if (!string.IsNullOrEmpty(worker.CurrentTaskId))
            {
                var currentTask = await GetTaskAsync(worker.CurrentTaskId);
                if (currentTask?.Status == TaskStatus.InProgress && 
                    currentTask.LeaseExpiry > DateTime.UtcNow)
                {
                    // Worker still has valid task, don't assign new one
                    return null;
                }
            }

            try
            {
                // Read from Redis Streams with consumer group
                var consumerGroup = "scanners";
                var consumerName = $"worker-{workerId}";

                // Ensure consumer group exists
                try
                {
                    await database.StreamCreateConsumerGroupAsync(TASK_QUEUE_KEY, consumerGroup, "0", createStream: true);
                }
                catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
                {
                    // Consumer group already exists, ignore
                }

                // Read pending messages for this consumer first
                var pendingMessages = await database.StreamReadGroupAsync(
                    TASK_QUEUE_KEY, consumerGroup, consumerName, "0", count: 1);

                StreamEntry[] messages;
                if (pendingMessages.Any())
                {
                    messages = pendingMessages;
                }
                else
                {
                    // No pending messages, read new ones
                    var newMessages = await database.StreamReadGroupAsync(
                        TASK_QUEUE_KEY, consumerGroup, consumerName, ">", count: 1);
                    messages = newMessages;
                }

                if (!messages.Any())
                {
                    return null; // No tasks available
                }

                var message = messages[0];
                var taskData = JsonSerializer.Deserialize<dynamic>(message.Values[0].Value!);
                var taskId = taskData.GetProperty("TaskId").GetString()!;

                // Update task in database
                var task = await _context.ScanTasks.FindAsync(taskId);
                if (task == null || task.Status != TaskStatus.Pending)
                {
                    // Task not found or already assigned, acknowledge and try next
                    await database.StreamAcknowledgeAsync(TASK_QUEUE_KEY, consumerGroup, message.Id);
                    return null;
                }

                // Assign task to worker
                var leaseExpiry = DateTime.UtcNow.AddMinutes(leaseTimeoutMinutes);
                task.Status = TaskStatus.InProgress;
                task.WorkerId = workerId;
                task.StartedAt = DateTime.UtcNow;
                task.LeaseExpiry = leaseExpiry;

                // Update worker current task
                await _workerService.UpdateWorkerStatusAsync(workerId, WorkerStatus.Working);
                worker.CurrentTaskId = taskId;
                worker.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // Get IVA account for worker
                var account = await _accountService.GetAccountAsync(worker.IvaAccountId ?? "");

                var assignment = new TaskAssignment
                {
                    TaskId = taskId,
                    JobId = taskData.GetProperty("JobId").GetString()!,
                    CardNumber = taskData.GetProperty("CardNumber").GetString()!,
                    PhoneNumbers = JsonSerializer.Deserialize<List<string>>(
                        taskData.GetProperty("PhoneNumbers").GetRawText())!,
                    RangeStart = taskData.GetProperty("RangeStart").GetInt32(),
                    RangeEnd = taskData.GetProperty("RangeEnd").GetInt32(),
                    IvaAccountPhone = account?.PhoneNumber,
                    ProxyUrl = worker.ProxyUrl,
                    LeaseExpiry = leaseExpiry
                };

                _logger.LogInformation("Assigned task {TaskId} (range {RangeStart}-{RangeEnd}) to worker {WorkerId}", 
                    taskId, assignment.RangeStart, assignment.RangeEnd, workerId);

                // Store message ID for later acknowledgment
                await database.HashSetAsync($"task:msg:{taskId}", "messageId", message.Id);

                return assignment;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting next task for worker {WorkerId}", workerId);
                return null;
            }
        }

        public async Task<bool> CompleteTaskAsync(string taskId, string workerId, bool success, string? result = null, string? errorMessage = null)
        {
            var database = _redis.GetDatabase();
            
            var task = await _context.ScanTasks
                .Include(t => t.Job)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null || task.WorkerId != workerId)
            {
                return false;
            }

            // Update task status
            task.Status = success ? TaskStatus.Completed : TaskStatus.Failed;
            task.CompletedAt = DateTime.UtcNow;
            task.ProcessingTime = task.StartedAt.HasValue 
                ? DateTime.UtcNow - task.StartedAt.Value 
                : null;
            
            if (!string.IsNullOrEmpty(result))
            {
                task.Result = result;
            }
            
            if (!string.IsNullOrEmpty(errorMessage))
            {
                task.ErrorMessage = errorMessage;
            }

            // Update worker status
            var worker = await _context.Workers.FindAsync(workerId);
            if (worker != null)
            {
                worker.CurrentTaskId = null;
                worker.Status = WorkerStatus.Online;
                worker.UpdatedAt = DateTime.UtcNow;
            }

            // Update worker counters
            await _workerService.IncrementTaskCountersAsync(workerId, success);

            await _context.SaveChangesAsync();

            // Acknowledge message in Redis
            try
            {
                var messageId = await database.HashGetAsync($"task:msg:{taskId}", "messageId");
                if (messageId.HasValue)
                {
                    await database.StreamAcknowledgeAsync(TASK_QUEUE_KEY, "scanners", messageId!);
                    await database.KeyDeleteAsync($"task:msg:{taskId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to acknowledge Redis message for task {TaskId}", taskId);
            }

            _logger.LogInformation("Task {TaskId} completed by worker {WorkerId}, success: {Success}", 
                taskId, workerId, success);

            return true;
        }

        public async Task<bool> FailTaskAsync(string taskId, string workerId, string errorMessage)
        {
            return await CompleteTaskAsync(taskId, workerId, false, null, errorMessage);
        }

        public async Task<IEnumerable<ScanTask>> GetExpiredTasksAsync()
        {
            return await _context.ScanTasks
                .Where(t => t.Status == TaskStatus.InProgress && 
                           t.LeaseExpiry.HasValue && 
                           t.LeaseExpiry.Value < DateTime.UtcNow)
                .Include(t => t.Worker)
                .ToListAsync();
        }

        public async Task ReturnExpiredTasksToQueueAsync()
        {
            var expiredTasks = await GetExpiredTasksAsync();
            var database = _redis.GetDatabase();

            foreach (var task in expiredTasks)
            {
                // Return task to pending status
                task.Status = TaskStatus.Pending;
                task.WorkerId = null;
                task.LeaseExpiry = null;
                task.RetryCount++;
                task.ErrorMessage = "Task lease expired";

                // Update worker status
                if (task.Worker != null)
                {
                    task.Worker.CurrentTaskId = null;
                    task.Worker.Status = WorkerStatus.Offline;
                    task.Worker.LastError = "Task lease expired";
                    task.Worker.UpdatedAt = DateTime.UtcNow;
                }

                // Re-add to Redis queue
                var taskMessage = new
                {
                    TaskId = task.Id,
                    JobId = task.JobId,
                    CardNumber = task.Job?.CardNumber ?? "",
                    PhoneNumbers = !string.IsNullOrEmpty(task.Job?.PhoneNumbers) 
                        ? JsonSerializer.Deserialize<List<string>>(task.Job.PhoneNumbers) 
                        : new List<string>(),
                    RangeStart = task.RangeStart,
                    RangeEnd = task.RangeEnd,
                    CreatedAt = task.CreatedAt,
                    RetryCount = task.RetryCount
                };

                await database.StreamAddAsync(TASK_QUEUE_KEY, "task", JsonSerializer.Serialize(taskMessage));

                _logger.LogWarning("Returned expired task {TaskId} to queue (retry #{RetryCount})", 
                    task.Id, task.RetryCount);
            }

            if (expiredTasks.Any())
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Returned {Count} expired tasks to queue", expiredTasks.Count());
            }
        }

        public async Task<int> GetPendingTaskCountAsync()
        {
            return await _context.ScanTasks.CountAsync(t => t.Status == TaskStatus.Pending);
        }

        public async Task<int> GetInProgressTaskCountAsync()
        {
            return await _context.ScanTasks.CountAsync(t => t.Status == TaskStatus.InProgress);
        }

        public async Task<ScanTask?> GetTaskAsync(string taskId)
        {
            return await _context.ScanTasks
                .Include(t => t.Job)
                .Include(t => t.Worker)
                .FirstOrDefaultAsync(t => t.Id == taskId);
        }

        public async Task<bool> CancelTasksForJobAsync(string jobId)
        {
            var pendingTasks = await _context.ScanTasks
                .Where(t => t.JobId == jobId && t.Status == TaskStatus.Pending)
                .ToListAsync();

            foreach (var task in pendingTasks)
            {
                task.Status = TaskStatus.Cancelled;
            }

            if (pendingTasks.Any())
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Cancelled {Count} pending tasks for job {JobId}", 
                    pendingTasks.Count, jobId);
            }

            return true;
        }
    }
}