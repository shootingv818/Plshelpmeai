using IvaScanner.Core.Models;
using IvaScanner.Master.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IvaScanner.Master.Services
{
    public class ScanJobService : IScanJobService
    {
        private readonly MasterDbContext _context;
        private readonly ILogger<ScanJobService> _logger;
        private readonly ITaskDistributionService _taskDistribution;

        public ScanJobService(MasterDbContext context, ILogger<ScanJobService> logger, ITaskDistributionService taskDistribution)
        {
            _context = context;
            _logger = logger;
            _taskDistribution = taskDistribution;
        }

        public async Task<ScanJob> CreateJobAsync(CreateScanJobRequest request)
        {
            var job = new ScanJob
            {
                CardNumber = request.CardNumber,
                PhoneNumbers = JsonSerializer.Serialize(request.PhoneNumbers),
                Status = ScanJobStatus.Created,
                CreatedBy = request.CreatedBy,
                CreatedAt = DateTime.UtcNow
            };

            _context.ScanJobs.Add(job);
            await _context.SaveChangesAsync();

            // Generate tasks for the job
            await _taskDistribution.CreateTasksForJobAsync(job.Id, request.CardNumber, request.PhoneNumbers);

            // Update job status and task counts
            var taskCount = await _context.ScanTasks.CountAsync(t => t.JobId == job.Id);
            job.TotalTasks = taskCount;
            job.Status = ScanJobStatus.Queued;
            
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created scan job {JobId} for card {CardNumber} with {TaskCount} tasks", 
                job.Id, request.CardNumber, taskCount);

            return job;
        }

        public async Task<ScanJob?> GetJobAsync(string jobId)
        {
            return await _context.ScanJobs
                .Include(j => j.Tasks)
                .FirstOrDefaultAsync(j => j.Id == jobId);
        }

        public async Task<IEnumerable<ScanJob>> GetJobsAsync(int skip = 0, int take = 50)
        {
            return await _context.ScanJobs
                .OrderByDescending(j => j.CreatedAt)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<IEnumerable<ScanJob>> GetActiveJobsAsync()
        {
            return await _context.ScanJobs
                .Where(j => j.Status == ScanJobStatus.Running || j.Status == ScanJobStatus.Queued)
                .OrderBy(j => j.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> UpdateJobStatusAsync(string jobId, ScanJobStatus status)
        {
            var job = await _context.ScanJobs.FindAsync(jobId);
            if (job == null) return false;

            job.Status = status;
            
            if (status == ScanJobStatus.Running && job.StartedAt == null)
            {
                job.StartedAt = DateTime.UtcNow;
            }
            else if (status == ScanJobStatus.Completed || status == ScanJobStatus.Failed || status == ScanJobStatus.Cancelled)
            {
                job.CompletedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateJobProgressAsync(string jobId)
        {
            var job = await _context.ScanJobs
                .Include(j => j.Tasks)
                .FirstOrDefaultAsync(j => j.Id == jobId);
            
            if (job == null) return false;

            var completedTasks = job.Tasks.Count(t => t.Status == TaskStatus.Completed);
            var failedTasks = job.Tasks.Count(t => t.Status == TaskStatus.Failed || t.Status == TaskStatus.DeadLetter);
            var totalTasks = job.Tasks.Count;

            job.CompletedTasks = completedTasks;
            job.FailedTasks = failedTasks;
            job.Progress = totalTasks > 0 ? (int)((completedTasks + failedTasks) * 100.0 / totalTasks) : 0;

            // Check if job should be completed
            if (completedTasks + failedTasks >= totalTasks)
            {
                job.Status = ScanJobStatus.Completed;
                job.CompletedAt = DateTime.UtcNow;
            }
            else if (job.Status == ScanJobStatus.Queued)
            {
                job.Status = ScanJobStatus.Running;
                job.StartedAt ??= DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> CompleteJobAsync(string jobId, CardInfo result)
        {
            var job = await _context.ScanJobs.FindAsync(jobId);
            if (job == null) return false;

            job.Status = ScanJobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.Result = JsonSerializer.Serialize(result);

            // Cancel any pending tasks
            var pendingTasks = await _context.ScanTasks
                .Where(t => t.JobId == jobId && t.Status == TaskStatus.Pending)
                .ToListAsync();

            foreach (var task in pendingTasks)
            {
                task.Status = TaskStatus.Cancelled;
            }

            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Job {JobId} completed successfully with result", jobId);
            return true;
        }

        public async Task<bool> FailJobAsync(string jobId, string errorMessage)
        {
            var job = await _context.ScanJobs.FindAsync(jobId);
            if (job == null) return false;

            job.Status = ScanJobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = errorMessage;

            await _context.SaveChangesAsync();
            
            _logger.LogWarning("Job {JobId} failed: {ErrorMessage}", jobId, errorMessage);
            return true;
        }

        public async Task<bool> CancelJobAsync(string jobId)
        {
            var job = await _context.ScanJobs.FindAsync(jobId);
            if (job == null) return false;

            job.Status = ScanJobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;

            // Cancel any pending or in-progress tasks
            var activeTasks = await _context.ScanTasks
                .Where(t => t.JobId == jobId && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
                .ToListAsync();

            foreach (var task in activeTasks)
            {
                task.Status = TaskStatus.Cancelled;
                task.WorkerId = null;
                task.LeaseExpiry = null;
            }

            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Job {JobId} cancelled, {TaskCount} tasks cancelled", jobId, activeTasks.Count);
            return true;
        }

        public async Task<bool> PauseJobAsync(string jobId)
        {
            var job = await _context.ScanJobs.FindAsync(jobId);
            if (job == null || job.Status != ScanJobStatus.Running) return false;

            job.Status = ScanJobStatus.Paused;
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Job {JobId} paused", jobId);
            return true;
        }

        public async Task<bool> ResumeJobAsync(string jobId)
        {
            var job = await _context.ScanJobs.FindAsync(jobId);
            if (job == null || job.Status != ScanJobStatus.Paused) return false;

            job.Status = ScanJobStatus.Running;
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Job {JobId} resumed", jobId);
            return true;
        }

        public async Task<int> GetActiveJobCountAsync()
        {
            return await _context.ScanJobs
                .CountAsync(j => j.Status == ScanJobStatus.Running || j.Status == ScanJobStatus.Queued);
        }

        public async Task<ScanJob?> GetJobByCardNumberAsync(string cardNumber)
        {
            return await _context.ScanJobs
                .Where(j => j.CardNumber == cardNumber)
                .OrderByDescending(j => j.CreatedAt)
                .FirstOrDefaultAsync();
        }
    }
}