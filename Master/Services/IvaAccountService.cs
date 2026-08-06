using IvaScanner.Core.Models;
using IvaScanner.Master.Data;
using Microsoft.EntityFrameworkCore;

namespace IvaScanner.Master.Services
{
    public class IvaAccountService : IIvaAccountService
    {
        private readonly MasterDbContext _context;
        private readonly ILogger<IvaAccountService> _logger;
        private readonly ISignalRNotificationService _signalRNotification;

        public IvaAccountService(
            MasterDbContext context, 
            ILogger<IvaAccountService> logger,
            ISignalRNotificationService signalRNotification)
        {
            _context = context;
            _logger = logger;
            _signalRNotification = signalRNotification;
        }

        public async Task<IvaAccount?> GetAccountAsync(string accountId)
        {
            return await _context.IvaAccounts
                .Include(a => a.AssignedWorker)
                .FirstOrDefaultAsync(a => a.Id == accountId);
        }

        public async Task<IvaAccount?> GetAccountByPhoneAsync(string phoneNumber)
        {
            return await _context.IvaAccounts
                .Include(a => a.AssignedWorker)
                .FirstOrDefaultAsync(a => a.PhoneNumber == phoneNumber);
        }

        public async Task<IEnumerable<IvaAccount>> GetAllAccountsAsync()
        {
            return await _context.IvaAccounts
                .Include(a => a.AssignedWorker)
                .OrderByDescending(a => a.LastUsed)
                .ToListAsync();
        }

        public async Task<IEnumerable<IvaAccount>> GetActiveAccountsAsync()
        {
            return await _context.IvaAccounts
                .Include(a => a.AssignedWorker)
                .Where(a => a.Status == AccountStatus.Active)
                .OrderByDescending(a => a.LastUsed)
                .ToListAsync();
        }

        public async Task<IvaAccount> CreateAccountAsync(CreateIvaAccountRequest request)
        {
            // Check if account already exists
            var existing = await GetAccountByPhoneAsync(request.PhoneNumber);
            if (existing != null)
            {
                throw new InvalidOperationException($"Account with phone number {request.PhoneNumber} already exists");
            }

            var account = new IvaAccount
            {
                PhoneNumber = request.PhoneNumber,
                SessionData = request.SessionData,
                Status = AccountStatus.Active,
                IsActive = request.IsActive,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastUsed = DateTime.UtcNow
            };

            _context.IvaAccounts.Add(account);
            await _context.SaveChangesAsync();

            // Send SignalR notification
            await _signalRNotification.NotifyAccountStatusChangedAsync(account);

            _logger.LogInformation("Created IVA account {AccountId} for phone {PhoneNumber}", 
                account.Id, request.PhoneNumber);

            return account;
        }

        public async Task<bool> UpdateAccountAsync(UpdateIvaAccountRequest request)
        {
            var account = await _context.IvaAccounts.FindAsync(request.Id);
            if (account == null) return false;

            account.PhoneNumber = request.PhoneNumber;
            account.SessionData = request.SessionData;
            account.IsActive = request.IsActive;
            account.UpdatedAt = DateTime.UtcNow;
            account.LastUsed = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Send SignalR notification
            await _signalRNotification.NotifyAccountStatusChangedAsync(account);

            _logger.LogInformation("Updated IVA account {AccountId}", request.Id);
            return true;
        }

        public async Task<bool> DeleteAccountAsync(string accountId)
        {
            var account = await _context.IvaAccounts.FindAsync(accountId);
            if (account == null) return false;

            // Unassign from any worker
            if (!string.IsNullOrEmpty(account.AssignedWorkerId))
            {
                var worker = await _context.Workers.FindAsync(account.AssignedWorkerId);
                if (worker != null)
                {
                    worker.IvaAccountId = null;
                }
            }

            _context.IvaAccounts.Remove(account);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted IVA account {AccountId}", accountId);
            return true;
        }

        public async Task<IvaAccount> CreateAccountAsync(string phoneNumber, string? sessionData = null)
        {
            var request = new CreateIvaAccountRequest
            {
                PhoneNumber = phoneNumber,
                SessionData = sessionData,
                IsActive = true
            };
            
            return await CreateAccountAsync(request);
        }

        public async Task<bool> UpdateAccountAsync(string accountId, string? sessionData = null, AccountStatus? status = null)
        {
            var account = await _context.IvaAccounts.FindAsync(accountId);
            if (account == null) return false;

            if (sessionData != null)
            {
                account.SessionData = sessionData;
            }

            if (status.HasValue)
            {
                account.Status = status.Value;
            }

            account.UpdatedAt = DateTime.UtcNow;
            account.LastUsed = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Send SignalR notification
            await _signalRNotification.NotifyAccountStatusChangedAsync(account);

            return true;
        }

        public async Task<IvaAccount?> GetAvailableAccountAsync()
        {
            return await _context.IvaAccounts
                .Where(a => a.Status == AccountStatus.Active && a.AssignedWorkerId == null)
                .OrderBy(a => a.LastUsed)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> AssignAccountToWorkerAsync(string accountId, string workerId)
        {
            var account = await _context.IvaAccounts.FindAsync(accountId);
            var worker = await _context.Workers.FindAsync(workerId);

            if (account == null || worker == null) return false;

            // Unassign any existing account from worker
            if (!string.IsNullOrEmpty(worker.IvaAccountId))
            {
                var oldAccount = await _context.IvaAccounts.FindAsync(worker.IvaAccountId);
                if (oldAccount != null)
                {
                    oldAccount.AssignedWorkerId = null;
                }
            }

            // Unassign account from any existing worker
            if (!string.IsNullOrEmpty(account.AssignedWorkerId))
            {
                var oldWorker = await _context.Workers.FindAsync(account.AssignedWorkerId);
                if (oldWorker != null)
                {
                    oldWorker.IvaAccountId = null;
                }
            }

            // Make new assignment
            account.AssignedWorkerId = workerId;
            account.UpdatedAt = DateTime.UtcNow;
            
            worker.IvaAccountId = accountId;
            worker.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Assigned IVA account {AccountId} to worker {WorkerId}", 
                accountId, workerId);

            return true;
        }

        public async Task<bool> UnassignAccountFromWorkerAsync(string accountId)
        {
            var account = await _context.IvaAccounts.FindAsync(accountId);
            if (account == null) return false;

            if (!string.IsNullOrEmpty(account.AssignedWorkerId))
            {
                var worker = await _context.Workers.FindAsync(account.AssignedWorkerId);
                if (worker != null)
                {
                    worker.IvaAccountId = null;
                    worker.UpdatedAt = DateTime.UtcNow;
                }

                account.AssignedWorkerId = null;
                account.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Unassigned IVA account {AccountId} from worker", accountId);
            }

            return true;
        }

        public async Task<bool> MarkAccountBlockedAsync(string accountId, string error)
        {
            var account = await _context.IvaAccounts.FindAsync(accountId);
            if (account == null) return false;

            account.Status = AccountStatus.Blocked;
            account.LastError = error;
            account.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogWarning("Marked IVA account {AccountId} as blocked: {Error}", accountId, error);
            return true;
        }

        public async Task<bool> MarkAccountActiveAsync(string accountId)
        {
            var account = await _context.IvaAccounts.FindAsync(accountId);
            if (account == null) return false;

            account.Status = AccountStatus.Active;
            account.LastError = null;
            account.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Marked IVA account {AccountId} as active", accountId);
            return true;
        }

        public async Task<int> GetActiveAccountCountAsync()
        {
            return await _context.IvaAccounts
                .CountAsync(a => a.Status == AccountStatus.Active);
        }
    }
}