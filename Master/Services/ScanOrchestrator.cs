using IvaScanner.Core.Models;
using IvaScanner.Master.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IvaScanner.Master.Services
{
    public class ScanOrchestrator : IScanOrchestrator
    {
        private readonly MasterDbContext _context;
        private readonly ITaskDistributionService _taskDistribution;
        private readonly IScanJobService _jobService;
        private readonly ISignalRNotificationService _signalRNotification;
        private readonly ILogger<ScanOrchestrator> _logger;

        public ScanOrchestrator(
            MasterDbContext context,
            ITaskDistributionService taskDistribution,
            IScanJobService jobService,
            ISignalRNotificationService signalRNotification,
            ILogger<ScanOrchestrator> logger)
        {
            _context = context;
            _taskDistribution = taskDistribution;
            _jobService = jobService;
            _signalRNotification = signalRNotification;
            _logger = logger;
        }

        public async Task<ScanJob> StartScanJobAsync(CreateScanJobRequest request)
        {
            _logger.LogInformation("Starting scan job for card {CardNumber}", request.CardNumber);

            // Create the job
            var job = await _jobService.CreateJobAsync(request);

            // Start the job (set status to Running)
            await _jobService.UpdateJobStatusAsync(job.Id, ScanJobStatus.Running);
            
            // Send SignalR notification
            await _signalRNotification.NotifyJobCreatedAsync(job);
            await _signalRNotification.NotifyJobStatusChangedAsync(job.Id, ScanJobStatus.Running);
            
            _logger.LogInformation("Started scan job {JobId} with {TotalTasks} tasks", 
                job.Id, job.TotalTasks);

            return job;
        }

        public async Task<bool> PauseScanJobAsync(string jobId)
        {
            _logger.LogInformation("Pausing scan job {JobId}", jobId);
            
            var success = await _jobService.PauseJobAsync(jobId);
            
            if (success)
            {
                _logger.LogInformation("Paused scan job {JobId}", jobId);
            }
            
            return success;
        }

        public async Task<bool> ResumeScanJobAsync(string jobId)
        {
            _logger.LogInformation("Resuming scan job {JobId}", jobId);
            
            var success = await _jobService.ResumeJobAsync(jobId);
            
            if (success)
            {
                _logger.LogInformation("Resumed scan job {JobId}", jobId);
            }
            
            return success;
        }

        public async Task<bool> CancelScanJobAsync(string jobId)
        {
            _logger.LogInformation("Cancelling scan job {JobId}", jobId);
            
            // Cancel all pending tasks for this job
            await _taskDistribution.CancelTasksForJobAsync(jobId);
            
            var success = await _jobService.CancelJobAsync(jobId);
            
            if (success)
            {
                _logger.LogInformation("Cancelled scan job {JobId}", jobId);
            }
            
            return success;
        }

        public async Task ProcessCompletedTaskAsync(string taskId, string result)
        {
            var task = await _taskDistribution.GetTaskAsync(taskId);
            if (task == null)
            {
                _logger.LogWarning("Task {TaskId} not found for completion processing", taskId);
                return;
            }

            _logger.LogInformation("Processing completed task {TaskId} for job {JobId}", taskId, task.JobId);

            // Check if this is an expiry detection task (RangeStart = 0, RangeEnd = 0)
            if (task.RangeStart == 0 && task.RangeEnd == 0)
            {
                await ProcessExpiryDetectionResult(task.JobId, result);
            }
            else
            {
                await ProcessCvvScanResult(task.JobId, result);
            }

            // Update job progress
            await UpdateJobProgress(task.JobId);
        }

        public async Task ProcessFailedTaskAsync(string taskId, string errorMessage)
        {
            var task = await _taskDistribution.GetTaskAsync(taskId);
            if (task == null)
            {
                _logger.LogWarning("Task {TaskId} not found for failure processing", taskId);
                return;
            }

            _logger.LogWarning("Processing failed task {TaskId} for job {JobId}: {ErrorMessage}", 
                taskId, task.JobId, errorMessage);

            // Update job progress
            await UpdateJobProgress(task.JobId);

            // Check if we should retry the task or mark job as failed
            if (task.RetryCount >= 3)
            {
                _logger.LogError("Task {TaskId} failed permanently after {RetryCount} retries", taskId, task.RetryCount);
                
                // Check if this failure should fail the entire job
                await CheckJobFailureCondition(task.JobId);
            }
        }

        public async Task MonitorJobProgressAsync()
        {
            var activeJobs = await _jobService.GetActiveJobsAsync();
            
            foreach (var job in activeJobs)
            {
                await UpdateJobProgress(job.Id);
            }
        }

        public async Task<JobProgress> GetJobProgressAsync(string jobId)
        {
            var job = await _context.ScanJobs
                .Include(j => j.Tasks)
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (job == null)
            {
                throw new ArgumentException("Job not found", nameof(jobId));
            }

            var totalTasks = job.Tasks.Count;
            var completedTasks = job.Tasks.Count(t => t.Status == TaskStatus.Completed);
            var failedTasks = job.Tasks.Count(t => t.Status == TaskStatus.Failed);
            var inProgressTasks = job.Tasks.Count(t => t.Status == TaskStatus.InProgress);

            var progressPercentage = totalTasks > 0 ? (double)completedTasks / totalTasks * 100 : 0;

            // Check for detected expiry
            var expiryTask = job.Tasks.FirstOrDefault(t => t.RangeStart == 0 && t.RangeEnd == 0 && t.Status == TaskStatus.Completed);
            string? detectedExpiry = null;
            DateTime? expiryDetectedAt = null;

            if (expiryTask != null && !string.IsNullOrEmpty(expiryTask.Result))
            {
                try
                {
                    var expiryResult = JsonSerializer.Deserialize<ExpiryDetectionResult>(expiryTask.Result);
                    if (expiryResult?.Success == true)
                    {
                        detectedExpiry = expiryResult.Expiry;
                        expiryDetectedAt = expiryTask.CompletedAt;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse expiry detection result for task {TaskId}", expiryTask.Id);
                }
            }

            // Estimate time remaining
            TimeSpan? estimatedTimeRemaining = null;
            if (completedTasks > 0 && inProgressTasks + (totalTasks - completedTasks - failedTasks) > 0)
            {
                var avgTaskTime = job.Tasks
                    .Where(t => t.Status == TaskStatus.Completed && t.ProcessingTime.HasValue)
                    .Select(t => t.ProcessingTime!.Value)
                    .DefaultIfEmpty(TimeSpan.FromSeconds(30))
                    .Average(ts => ts.TotalSeconds);

                var remainingTasks = totalTasks - completedTasks - failedTasks;
                estimatedTimeRemaining = TimeSpan.FromSeconds(avgTaskTime * remainingTasks);
            }

            return new JobProgress
            {
                JobId = jobId,
                Status = job.Status,
                TotalTasks = totalTasks,
                CompletedTasks = completedTasks,
                FailedTasks = failedTasks,
                InProgressTasks = inProgressTasks,
                ProgressPercentage = progressPercentage,
                ExpiryDetectedAt = expiryDetectedAt,
                DetectedExpiry = detectedExpiry,
                FinalResult = job.Result,
                EstimatedTimeRemaining = estimatedTimeRemaining
            };
        }

        private async Task ProcessExpiryDetectionResult(string jobId, string result)
        {
            _logger.LogInformation("Processing expiry detection result for job {JobId}", jobId);

            try
            {
                var expiryResult = JsonSerializer.Deserialize<ExpiryDetectionResult>(result);
                if (expiryResult?.Success == true)
                {
                    _logger.LogInformation("Expiry detected for job {JobId}: {Expiry}", jobId, expiryResult.Expiry);
                    
                    // Store the detected expiry in job result
                    var job = await _context.ScanJobs.FindAsync(jobId);
                    if (job != null)
                    {
                        var jobResult = new
                        {
                            ExpiryDetected = true,
                            Expiry = expiryResult.Expiry,
                            DetectedAt = DateTime.UtcNow
                        };
                        job.Result = JsonSerializer.Serialize(jobResult);
                        await _context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process expiry detection result for job {JobId}", jobId);
            }
        }

        private async Task ProcessCvvScanResult(string jobId, string result)
        {
            _logger.LogInformation("Processing CVV scan result for job {JobId}", jobId);

            try
            {
                var cvvResult = JsonSerializer.Deserialize<CvvScanResult>(result);
                if (cvvResult?.Success == true && !string.IsNullOrEmpty(cvvResult.ValidCvv))
                {
                    _logger.LogInformation("Valid CVV found for job {JobId}: {Cvv}", jobId, cvvResult.ValidCvv);
                    
                    // Valid CVV found - complete the job
                    var job = await _context.ScanJobs.FindAsync(jobId);
                    if (job != null)
                    {
                        var finalResult = new
                        {
                            Success = true,
                            ValidCvv = cvvResult.ValidCvv,
                            CardInfo = cvvResult.CardInfo,
                            CompletedAt = DateTime.UtcNow
                        };
                        
                        job.Result = JsonSerializer.Serialize(finalResult);
                        job.Status = ScanJobStatus.Completed;
                        job.CompletedAt = DateTime.UtcNow;
                        
                        await _context.SaveChangesAsync();
                        
                        // Cancel remaining tasks
                        await _taskDistribution.CancelTasksForJobAsync(jobId);
                        
                        _logger.LogInformation("Job {JobId} completed successfully with CVV {Cvv}", jobId, cvvResult.ValidCvv);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process CVV scan result for job {JobId}", jobId);
            }
        }

        private async Task UpdateJobProgress(string jobId)
        {
            var job = await _context.ScanJobs
                .Include(j => j.Tasks)
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (job == null) return;

            var completedTasks = job.Tasks.Count(t => t.Status == TaskStatus.Completed);
            var failedTasks = job.Tasks.Count(t => t.Status == TaskStatus.Failed);
            var totalTasks = job.Tasks.Count;

            job.CompletedTasks = completedTasks;
            job.FailedTasks = failedTasks;
            job.Progress = totalTasks > 0 ? (int)((double)completedTasks / totalTasks * 100) : 0;

            // Check if job should be marked as completed or failed
            if (job.Status == ScanJobStatus.Running)
            {
                if (completedTasks + failedTasks >= totalTasks)
                {
                    // All tasks finished
                    if (string.IsNullOrEmpty(job.Result) || !job.Result.Contains("\"Success\":true"))
                    {
                        // No valid result found
                        job.Status = ScanJobStatus.Failed;
                        job.CompletedAt = DateTime.UtcNow;
                        job.ErrorMessage = "No valid CVV found after scanning all combinations";
                    }
                }
            }

            await _context.SaveChangesAsync();
        }

        private async Task CheckJobFailureCondition(string jobId)
        {
            var job = await _context.ScanJobs
                .Include(j => j.Tasks)
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (job == null) return;

            var failedTasks = job.Tasks.Count(t => t.Status == TaskStatus.Failed && t.RetryCount >= 3);
            var totalTasks = job.Tasks.Count;

            // If more than 50% of tasks failed permanently, mark job as failed
            if (failedTasks > totalTasks * 0.5)
            {
                _logger.LogError("Job {JobId} failed due to high task failure rate: {FailedTasks}/{TotalTasks}", 
                    jobId, failedTasks, totalTasks);
                
                job.Status = ScanJobStatus.Failed;
                job.CompletedAt = DateTime.UtcNow;
                job.ErrorMessage = $"Job failed due to high task failure rate: {failedTasks}/{totalTasks} tasks failed permanently";
                
                await _context.SaveChangesAsync();
                
                // Cancel remaining tasks
                await _taskDistribution.CancelTasksForJobAsync(jobId);
            }
        }
    }

    // Result models for task processing
    public class ExpiryDetectionResult
    {
        public bool Success { get; set; }
        public string Expiry { get; set; } = "";
        public string? ErrorMessage { get; set; }
    }

    public class CvvScanResult
    {
        public bool Success { get; set; }
        public string ValidCvv { get; set; } = "";
        public object? CardInfo { get; set; }
        public string? ErrorMessage { get; set; }
    }
}

        
        public async Task<List<ScanResult>> GetJobResultsAsync(string jobId)
        {
            try
            {
                // Get job tasks with results from database
                var tasks = await _context.ScanTasks
                    .Where(t => t.JobId == jobId && t.Status == TaskStatus.Completed)
                    .OrderByDescending(t => t.CompletedAt)
                    .ToListAsync();

                var results = new List<ScanResult>();
                
                foreach (var task in tasks)
                {
                    if (!string.IsNullOrEmpty(task.Result))
                    {
                        try
                        {
                            // Parse task result JSON
                            var taskResult = JsonSerializer.Deserialize<Dictionary<string, object>>(task.Result);
                            
                            var scanResult = new ScanResult
                            {
                                Id = task.Id,
                                JobId = jobId,
                                PhoneNumber = taskResult.GetValueOrDefault("phoneNumber")?.ToString() ?? "",
                                AccountName = taskResult.GetValueOrDefault("accountName")?.ToString(),
                                IsSuccess = taskResult.GetValueOrDefault("success")?.ToString() == "True",
                                Status = taskResult.GetValueOrDefault("success")?.ToString() == "True" ? "success" : "failed",
                                TestType = taskResult.GetValueOrDefault("testType")?.ToString() ?? "",
                                Password = taskResult.GetValueOrDefault("password")?.ToString(),
                                CVV = taskResult.GetValueOrDefault("cvv")?.ToString(),
                                ExpiryMonth = int.TryParse(taskResult.GetValueOrDefault("expiryMonth")?.ToString(), out var month) ? month : null,
                                ExpiryYear = int.TryParse(taskResult.GetValueOrDefault("expiryYear")?.ToString(), out var year) ? year : null,
                                WorkerName = taskResult.GetValueOrDefault("workerName")?.ToString(),
                                ErrorMessage = taskResult.GetValueOrDefault("error")?.ToString(),
                                CreatedAt = task.CompletedAt ?? task.CreatedAt
                            };
                            
                            results.Add(scanResult);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to parse task result for task {TaskId}", task.Id);
                        }
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job results for {JobId}", jobId);
                return new List<ScanResult>();
            }
        }

        public async Task<object> GetJobTasksSummaryAsync(string jobId)
        {
            try
            {
                var tasks = await _context.ScanTasks
                    .Where(t => t.JobId == jobId)
                    .GroupBy(t => t.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .ToListAsync();

                var summary = new
                {
                    pending = tasks.FirstOrDefault(t => t.Status == TaskStatus.Pending)?.Count ?? 0,
                    running = tasks.FirstOrDefault(t => t.Status == TaskStatus.InProgress)?.Count ?? 0,
                    completed = tasks.FirstOrDefault(t => t.Status == TaskStatus.Completed)?.Count ?? 0,
                    failed = tasks.FirstOrDefault(t => t.Status == TaskStatus.Failed)?.Count ?? 0
                };

                return summary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job tasks summary for {JobId}", jobId);
                return new { pending = 0, running = 0, completed = 0, failed = 0 };
            }
        }

        public async Task<int> GetJobResultsCountAsync(string jobId)
        {
            try
            {
                return await _context.ScanTasks
                    .Where(t => t.JobId == jobId && 
                               t.Status == TaskStatus.Completed && 
                               !string.IsNullOrEmpty(t.Result) &&
                               t.Result.Contains("\"success\":true"))
                    .CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job results count for {JobId}", jobId);
                return 0;
            }
        }

        public async Task<List<object>> GetJobWorkersAsync(string jobId)
        {
            try
            {
                var workers = await _context.ScanTasks
                    .Where(t => t.JobId == jobId && !string.IsNullOrEmpty(t.AssignedWorker))
                    .GroupBy(t => t.AssignedWorker)
                    .Select(g => new 
                    {
                        name = g.Key,
                        taskCount = g.Count(),
                        completedTasks = g.Count(t => t.Status == TaskStatus.Completed),
                        status = "Online", // Would be determined from Worker table
                        currentTask = g.FirstOrDefault(t => t.Status == TaskStatus.InProgress) != null ? 
                                     $"Task {g.FirstOrDefault(t => t.Status == TaskStatus.InProgress)!.Id}" : 
                                     null,
                        ipAddress = "192.168.1.1" // Would come from Worker table
                    })
                    .ToListAsync();

                return workers.Cast<object>().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job workers for {JobId}", jobId);
                return new List<object>();
            }
        }

        public async Task<List<object>> GetRecentResultsAsync(string jobId, int limit)
        {
            try
            {
                var recentTasks = await _context.ScanTasks
                    .Where(t => t.JobId == jobId && 
                               t.Status == TaskStatus.Completed && 
                               !string.IsNullOrEmpty(t.Result) &&
                               t.Result.Contains("\"success\":true"))
                    .OrderByDescending(t => t.CompletedAt)
                    .Take(limit)
                    .ToListAsync();

                var results = new List<object>();
                
                foreach (var task in recentTasks)
                {
                    try
                    {
                        var taskResult = JsonSerializer.Deserialize<Dictionary<string, object>>(task.Result!);
                        
                        results.Add(new
                        {
                            cardNumber = taskResult.GetValueOrDefault("cardNumber")?.ToString()?.Substring(12, 4) ?? "****",
                            phoneNumber = taskResult.GetValueOrDefault("phoneNumber")?.ToString() ?? "",
                            password = taskResult.GetValueOrDefault("password")?.ToString() ?? "",
                            foundAt = task.CompletedAt ?? task.CreatedAt
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse recent result for task {TaskId}", task.Id);
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent results for {JobId}", jobId);
                return new List<object>();
            }
        }