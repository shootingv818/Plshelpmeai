using IvaScanner.Master.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace IvaScanner.Master.Services
{
    public interface IResilientDatabaseService
    {
        Task<T> ExecuteAsync<T>(Func<MasterDbContext, Task<T>> operation, string? operationName = null);
        Task ExecuteAsync(Func<MasterDbContext, Task> operation, string? operationName = null);
        Task<T> ExecuteInTransactionAsync<T>(Func<MasterDbContext, Task<T>> operation, string? operationName = null);
        Task ExecuteInTransactionAsync(Func<MasterDbContext, Task> operation, string? operationName = null);
        Task<bool> IsHealthyAsync();
        Task<Dictionary<string, object>> GetDatabaseMetricsAsync();
    }

    public class ResilientDatabaseService : IResilientDatabaseService
    {
        private readonly IDbContextFactory<MasterDbContext> _contextFactory;
        private readonly IErrorHandlingService _errorHandling;
        private readonly ISystemLogService _systemLog;
        private readonly ILogger<ResilientDatabaseService> _logger;

        public ResilientDatabaseService(
            IDbContextFactory<MasterDbContext> contextFactory,
            IErrorHandlingService errorHandling,
            ISystemLogService systemLog,
            ILogger<ResilientDatabaseService> logger)
        {
            _contextFactory = contextFactory;
            _errorHandling = errorHandling;
            _systemLog = systemLog;
            _logger = logger;
        }

        public async Task<T> ExecuteAsync<T>(Func<MasterDbContext, Task<T>> operation, string? operationName = null)
        {
            operationName ??= "DatabaseOperation";

            return await _errorHandling.ExecuteWithResilienceAsync(
                $"db_{operationName}",
                async () =>
                {
                    using var context = await _contextFactory.CreateDbContextAsync();
                    
                    try
                    {
                        var result = await operation(context);
                        return result;
                    }
                    catch (Exception ex) when (IsDatabaseException(ex))
                    {
                        await HandleDatabaseError(ex, operationName);
                        throw;
                    }
                },
                new ResiliencePolicy
                {
                    RetryPolicy = RetryPolicies.Database,
                    CircuitBreakerPolicy = CircuitBreakerPolicies.Database,
                    Timeout = TimeSpan.FromSeconds(30)
                });
        }

        public async Task ExecuteAsync(Func<MasterDbContext, Task> operation, string? operationName = null)
        {
            await ExecuteAsync(async context =>
            {
                await operation(context);
                return true;
            }, operationName);
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<MasterDbContext, Task<T>> operation, string? operationName = null)
        {
            operationName ??= "DatabaseTransaction";

            return await _errorHandling.ExecuteWithResilienceAsync(
                $"db_tx_{operationName}",
                async () =>
                {
                    using var context = await _contextFactory.CreateDbContextAsync();
                    
                    await using var transaction = await context.Database.BeginTransactionAsync();
                    
                    try
                    {
                        var result = await operation(context);
                        await transaction.CommitAsync();
                        
                        return result;
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            await transaction.RollbackAsync();
                        }
                        catch (Exception rollbackEx)
                        {
                            await _systemLog.LogErrorAsync(rollbackEx,
                                $"Failed to rollback transaction for operation '{operationName}'",
                                "database_rollback_error",
                                "ResilientDatabaseService"
                            );
                        }

                        if (IsDatabaseException(ex))
                        {
                            await HandleDatabaseError(ex, operationName);
                        }
                        
                        throw;
                    }
                },
                new ResiliencePolicy
                {
                    RetryPolicy = CreateTransactionRetryPolicy(),
                    CircuitBreakerPolicy = CircuitBreakerPolicies.Database,
                    Timeout = TimeSpan.FromSeconds(60)
                });
        }

        public async Task ExecuteInTransactionAsync(Func<MasterDbContext, Task> operation, string? operationName = null)
        {
            await ExecuteInTransactionAsync(async context =>
            {
                await operation(context);
                return true;
            }, operationName);
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                return await ExecuteAsync(async context =>
                {
                    // Simple health check query
                    return await context.Database.CanConnectAsync();
                }, "HealthCheck");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database health check failed");
                return false;
            }
        }

        public async Task<Dictionary<string, object>> GetDatabaseMetricsAsync()
        {
            var metrics = new Dictionary<string, object>();

            try
            {
                await ExecuteAsync(async context =>
                {
                    // Get basic connection info
                    metrics["can_connect"] = await context.Database.CanConnectAsync();
                    
                    // Get table counts (sample some key tables)
                    metrics["workers_count"] = await context.Workers.CountAsync();
                    metrics["scan_jobs_count"] = await context.ScanJobs.CountAsync();
                    metrics["system_logs_count"] = await context.SystemLogs.CountAsync();
                    metrics["proxy_servers_count"] = await context.ProxyServers.CountAsync();
                    
                    // Get recent activity
                    var oneHourAgo = DateTime.UtcNow.AddHours(-1);
                    metrics["recent_logs"] = await context.SystemLogs
                        .CountAsync(l => l.CreatedAt >= oneHourAgo);
                    
                    metrics["active_workers"] = await context.Workers
                        .CountAsync(w => w.Status == WorkerStatus.Online);
                    
                    metrics["running_jobs"] = await context.ScanJobs
                        .CountAsync(j => j.Status == ScanJobStatus.Running);
                    
                    metrics["last_checked"] = DateTime.UtcNow;
                    
                    return true;
                }, "GetMetrics");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get database metrics");
                metrics["error"] = ex.Message;
                metrics["healthy"] = false;
            }

            return metrics;
        }

        private async Task HandleDatabaseError(Exception ex, string operationName)
        {
            await _errorHandling.RecordErrorAsync("database", ex, operationName);

            // Attempt recovery for specific database errors
            if (IsConnectionError(ex))
            {
                await _errorHandling.AttemptErrorRecoveryAsync("database", ex, "connection_error");
            }
            else if (IsTimeoutError(ex))
            {
                await _systemLog.LogWarningAsync(
                    $"Database timeout in operation '{operationName}': {ex.Message}",
                    "database_timeout",
                    "ResilientDatabaseService"
                );
            }
            else if (IsDeadlockError(ex))
            {
                await _systemLog.LogWarningAsync(
                    $"Database deadlock in operation '{operationName}': {ex.Message}",
                    "database_deadlock",
                    "ResilientDatabaseService"
                );
            }
        }

        private static bool IsDatabaseException(Exception ex)
        {
            return ex is DbUpdateException ||
                   ex is InvalidOperationException ||
                   ex is TimeoutException ||
                   ex.Message.Contains("database", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsConnectionError(Exception ex)
        {
            return ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("server", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTimeoutError(Exception ex)
        {
            return ex is TimeoutException ||
                   ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDeadlockError(Exception ex)
        {
            return ex.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("1205", StringComparison.OrdinalIgnoreCase); // SQL Server deadlock error
        }

        private static RetryPolicy CreateTransactionRetryPolicy()
        {
            return new RetryPolicy
            {
                MaxRetryAttempts = 3,
                InitialDelay = TimeSpan.FromMilliseconds(100),
                MaxDelay = TimeSpan.FromSeconds(2),
                BackoffMultiplier = 2.0,
                UseJitter = true,
                CustomRetryCondition = ex => IsDeadlockError(ex) || IsTimeoutError(ex)
            };
        }
    }
}