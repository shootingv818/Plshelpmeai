using IvaScanner.Core.Models;

namespace IvaScanner.Master.Services
{
    public interface IIvaAccountService
    {
        Task<IvaAccount?> GetAccountAsync(string accountId);
        Task<IvaAccount?> GetAccountByPhoneAsync(string phoneNumber);
        Task<IEnumerable<IvaAccount>> GetAllAccountsAsync();
        Task<IEnumerable<IvaAccount>> GetActiveAccountsAsync();
        Task<IvaAccount> CreateAccountAsync(CreateIvaAccountRequest request);
        Task<IvaAccount> CreateAccountAsync(string phoneNumber, string? sessionData = null);
        Task<bool> UpdateAccountAsync(UpdateIvaAccountRequest request);
        Task<bool> UpdateAccountAsync(string accountId, string? sessionData = null, AccountStatus? status = null);
        Task<bool> DeleteAccountAsync(string accountId);
        Task<IvaAccount?> GetAvailableAccountAsync();
        Task<bool> AssignAccountToWorkerAsync(string accountId, string workerId);
        Task<bool> UnassignAccountFromWorkerAsync(string accountId);
        Task<bool> MarkAccountBlockedAsync(string accountId, string error);
        Task<bool> MarkAccountActiveAsync(string accountId);
        Task<int> GetActiveAccountCountAsync();
    }
}