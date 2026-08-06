using IvaScanner.Core.Models;

namespace IvaScanner.Master.Services
{
    public interface IScanOrchestrator
    {
        Task<ScanJob> StartScanJobAsync(CreateScanJobRequest request);
        Task<bool> PauseScanJobAsync(string jobId);
        Task<bool> ResumeScanJobAsync(string jobId);
        Task<bool> CancelScanJobAsync(string jobId);
        Task ProcessCompletedTaskAsync(string taskId, string result);
        Task ProcessFailedTaskAsync(string taskId, string errorMessage);
        Task MonitorJobProgressAsync();
        Task<JobProgress> GetJobProgressAsync(string jobId);
        Task<List<ScanResult>> GetJobResultsAsync(string jobId);
        Task<object> GetJobTasksSummaryAsync(string jobId);
        Task<int> GetJobResultsCountAsync(string jobId);
        Task<List<object>> GetJobWorkersAsync(string jobId);
        Task<List<object>> GetRecentResultsAsync(string jobId, int limit);
    }

    public class JobProgress
    {
        public string JobId { get; set; } = "";
        public ScanJobStatus Status { get; set; }
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int FailedTasks { get; set; }
        public int InProgressTasks { get; set; }
        public double ProgressPercentage { get; set; }
        public DateTime? ExpiryDetectedAt { get; set; }
        public string? DetectedExpiry { get; set; }
        public string? FinalResult { get; set; }
        public TimeSpan? EstimatedTimeRemaining { get; set; }
    }

    public class ScanResult
    {
        public string Id { get; set; } = "";
        public string JobId { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string? AccountName { get; set; }
        public bool IsSuccess { get; set; }
        public string Status { get; set; } = "";
        public string TestType { get; set; } = "";
        public string? Password { get; set; }
        public string? CVV { get; set; }
        public int? ExpiryMonth { get; set; }
        public int? ExpiryYear { get; set; }
        public string? WorkerName { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}