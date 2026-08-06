using IvaScanner.Core.Models;

namespace IvaScanner.Master.Services
{
    public interface ITaskProcessor
    {
        Task<TaskResult> ProcessTaskAsync(TaskAssignment task);
        Task<ExpiryDetectionResult> DetectExpiryAsync(TaskAssignment task);
        Task<CvvScanResult> ScanCvvRangeAsync(TaskAssignment task);
    }

    public class TaskResult
    {
        public bool Success { get; set; }
        public string? Result { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan ProcessingTime { get; set; }
    }
}