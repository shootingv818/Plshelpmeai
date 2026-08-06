using IvaScanner.Core.Models;

namespace IvaScanner.Master.Services
{
    public interface IScanJobService
    {
        Task<ScanJob> CreateJobAsync(CreateScanJobRequest request);
        Task<ScanJob?> GetJobAsync(string jobId);
        Task<IEnumerable<ScanJob>> GetJobsAsync(int skip = 0, int take = 50);
        Task<IEnumerable<ScanJob>> GetActiveJobsAsync();
        Task<bool> UpdateJobStatusAsync(string jobId, ScanJobStatus status);
        Task<bool> UpdateJobProgressAsync(string jobId);
        Task<bool> CompleteJobAsync(string jobId, CardInfo result);
        Task<bool> FailJobAsync(string jobId, string errorMessage);
        Task<bool> CancelJobAsync(string jobId);
        Task<bool> PauseJobAsync(string jobId);
        Task<bool> ResumeJobAsync(string jobId);
        Task<int> GetActiveJobCountAsync();
        Task<ScanJob?> GetJobByCardNumberAsync(string cardNumber);
    }
}