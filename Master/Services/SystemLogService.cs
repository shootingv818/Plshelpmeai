using IvaScanner.Core.Models;
using IvaScanner.Master.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IvaScanner.Master.Services
{
    public class SystemLogService : ISystemLogService
    {
        private readonly MasterDbContext _context;
        private readonly ISignalRNotificationService _signalRNotification;
        private readonly ILogger<SystemLogService> _logger;

        public SystemLogService(
            MasterDbContext context,
            ISignalRNotificationService signalRNotification,
            ILogger<SystemLogService> logger)
        {
            _context = context;
            _signalRNotification = signalRNotification;
            _logger = logger;
        }

        public async Task LogAsync(LogLevel level, string message, string? context = null, 
            string? component = null, Dictionary<string, object>? metadata = null)
        {
            try
            {
                var log = new SystemLog
                {
                    Id = Guid.NewGuid().ToString(),
                    Level = level,
                    Message = message,
                    Context = context,
                    Component = component ?? "System",
                    Metadata = metadata != null ? JsonSerializer.Serialize(metadata) : null,
                    CreatedAt = DateTime.UtcNow
                };

                _context.SystemLogs.Add(log);
                await _context.SaveChangesAsync();

                // Send real-time notification for important logs
                if (level >= LogLevel.Warning)
                {
                    await _signalRNotification.NotifySystemLogAsync(log);
                }
            }
            catch (Exception ex)
            {
                // Fallback logging to prevent infinite recursion
                _logger.LogError(ex, "Failed to write system log: {Message}", message);
            }
        }

        public async Task LogAsync(LogLevel level, Exception exception, string message, string? context = null,
            string? component = null, Dictionary<string, object>? metadata = null)
        {
            var enhancedMetadata = metadata ?? new Dictionary<string, object>();
            enhancedMetadata["exception_type"] = exception.GetType().Name;
            enhancedMetadata["exception_message"] = exception.Message;
            enhancedMetadata["stack_trace"] = exception.StackTrace;
            
            if (exception.InnerException != null)
            {
                enhancedMetadata["inner_exception"] = exception.InnerException.Message;
            }

            await LogAsync(level, $"{message} - {exception.Message}", context, component, enhancedMetadata);
        }

        // Convenience methods
        public async Task LogInfoAsync(string message, string? context = null, string? component = null)
            => await LogAsync(LogLevel.Information, message, context, component);

        public async Task LogWarningAsync(string message, string? context = null, string? component = null)
            => await LogAsync(LogLevel.Warning, message, context, component);

        public async Task LogErrorAsync(string message, string? context = null, string? component = null)
            => await LogAsync(LogLevel.Error, message, context, component);

        public async Task LogErrorAsync(Exception exception, string message, string? context = null, string? component = null)
            => await LogAsync(LogLevel.Error, exception, message, context, component);

        public async Task LogDebugAsync(string message, string? context = null, string? component = null)
            => await LogAsync(LogLevel.Debug, message, context, component);

        public async Task LogCriticalAsync(string message, string? context = null, string? component = null)
            => await LogAsync(LogLevel.Critical, message, context, component);

        public async Task LogCriticalAsync(Exception exception, string message, string? context = null, string? component = null)
            => await LogAsync(LogLevel.Critical, exception, message, context, component);

        // System event logs
        public async Task LogWorkerEventAsync(string workerId, WorkerEventType eventType, string details)
        {
            var metadata = new Dictionary<string, object>
            {
                ["worker_id"] = workerId,
                ["event_type"] = eventType.ToString()
            };

            var level = eventType switch
            {
                WorkerEventType.Disconnected => LogLevel.Warning,
                WorkerEventType.TaskFailed => LogLevel.Error,
                WorkerEventType.HeartbeatMissed => LogLevel.Warning,
                _ => LogLevel.Information
            };

            await LogAsync(level, $"Worker {workerId}: {details}", $"worker_{workerId}", "WorkerService", metadata);
        }

        public async Task LogScanEventAsync(string jobId, ScanEventType eventType, string details, Dictionary<string, object>? metadata = null)
        {
            var enhancedMetadata = metadata ?? new Dictionary<string, object>();
            enhancedMetadata["job_id"] = jobId;
            enhancedMetadata["event_type"] = eventType.ToString();

            var level = eventType switch
            {
                ScanEventType.JobFailed => LogLevel.Error,
                ScanEventType.JobCancelled => LogLevel.Warning,
                ScanEventType.ResultFound => LogLevel.Information,
                ScanEventType.ExpiryDetected => LogLevel.Information,
                _ => LogLevel.Information
            };

            await LogAsync(level, $"Scan Job {jobId}: {details}", $"job_{jobId}", "ScanOrchestrator", enhancedMetadata);
        }

        public async Task LogTaskEventAsync(string taskId, TaskEventType eventType, string details)
        {
            var metadata = new Dictionary<string, object>
            {
                ["task_id"] = taskId,
                ["event_type"] = eventType.ToString()
            };

            var level = eventType switch
            {
                TaskEventType.Failed => LogLevel.Error,
                TaskEventType.TimedOut => LogLevel.Warning,
                TaskEventType.Retried => LogLevel.Warning,
                _ => LogLevel.Debug
            };

            await LogAsync(level, $"Task {taskId}: {details}", $"task_{taskId}", "TaskProcessor", metadata);
        }

        public async Task LogSecurityEventAsync(SecurityEventType eventType, string details, string? ipAddress = null)
        {
            var metadata = new Dictionary<string, object>
            {
                ["event_type"] = eventType.ToString(),
                ["ip_address"] = ipAddress ?? "unknown"
            };

            var level = eventType switch
            {
                SecurityEventType.UnauthorizedAccess => LogLevel.Warning,
                SecurityEventType.InvalidApiKey => LogLevel.Warning,
                SecurityEventType.SuspiciousActivity => LogLevel.Error,
                SecurityEventType.RateLimitExceeded => LogLevel.Warning,
                _ => LogLevel.Information
            };

            await LogAsync(level, $"Security Event: {details}", "security", "SecurityService", metadata);
        }

        public async Task LogPerformanceAsync(string component, string operation, TimeSpan duration, Dictionary<string, object>? metrics = null)
        {
            var metadata = metrics ?? new Dictionary<string, object>();
            metadata["operation"] = operation;
            metadata["duration_ms"] = duration.TotalMilliseconds;
            metadata["duration_seconds"] = duration.TotalSeconds;

            var level = duration.TotalSeconds > 10 ? LogLevel.Warning : LogLevel.Debug;
            var message = $"Performance: {operation} took {duration.TotalMilliseconds:F2}ms";

            await LogAsync(level, message, $"perf_{component}", component, metadata);
        }

        // Query logs
        public async Task<List<SystemLog>> GetLogsAsync(int skip = 0, int take = 100, LogLevel? minLevel = null, 
            string? component = null, string? context = null, DateTime? fromDate = null, DateTime? toDate = null)
        {
            var query = _context.SystemLogs.AsQueryable();

            if (minLevel.HasValue)
            {
                query = query.Where(l => l.Level >= minLevel.Value);
            }

            if (!string.IsNullOrEmpty(component))
            {
                query = query.Where(l => l.Component == component);
            }

            if (!string.IsNullOrEmpty(context))
            {
                query = query.Where(l => l.Context != null && l.Context.Contains(context));
            }

            if (fromDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt <= toDate.Value);
            }

            return await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<SystemLog>> SearchLogsAsync(string searchTerm, int skip = 0, int take = 100)
        {
            return await _context.SystemLogs
                .Where(l => l.Message.Contains(searchTerm) || 
                           (l.Context != null && l.Context.Contains(searchTerm)) ||
                           (l.Component != null && l.Component.Contains(searchTerm)))
                .OrderByDescending(l => l.CreatedAt)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<Dictionary<LogLevel, int>> GetLogCountsByLevelAsync(DateTime? fromDate = null, DateTime? toDate = null)
        {
            var query = _context.SystemLogs.AsQueryable();

            if (fromDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt <= toDate.Value);
            }

            var counts = await query
                .GroupBy(l => l.Level)
                .Select(g => new { Level = g.Key, Count = g.Count() })
                .ToListAsync();

            return counts.ToDictionary(c => c.Level, c => c.Count);
        }

        public async Task<List<LogStatsItem>> GetLogStatsByComponentAsync(DateTime? fromDate = null, DateTime? toDate = null)
        {
            var query = _context.SystemLogs.AsQueryable();

            if (fromDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt <= toDate.Value);
            }

            var stats = await query
                .GroupBy(l => l.Component)
                .Select(g => new LogStatsItem
                {
                    Component = g.Key ?? "Unknown",
                    TotalCount = g.Count(),
                    ErrorCount = g.Count(l => l.Level == LogLevel.Error || l.Level == LogLevel.Critical),
                    WarningCount = g.Count(l => l.Level == LogLevel.Warning),
                    LastLogAt = g.Max(l => l.CreatedAt)
                })
                .OrderByDescending(s => s.TotalCount)
                .ToListAsync();

            return stats;
        }

        public async Task<List<SystemLog>> GetRecentLogsAsync(int count = 50)
        {
            return await _context.SystemLogs
                .OrderByDescending(l => l.CreatedAt)
                .Take(count)
                .ToListAsync();
        }

        public async Task<long> GetLogCountAsync(LogLevel? minLevel = null, DateTime? fromDate = null, DateTime? toDate = null)
        {
            var query = _context.SystemLogs.AsQueryable();

            if (minLevel.HasValue)
            {
                query = query.Where(l => l.Level >= minLevel.Value);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt <= toDate.Value);
            }

            return await query.CountAsync();
        }

        // Log management
        public async Task CleanupOldLogsAsync(TimeSpan olderThan)
        {
            var cutoffDate = DateTime.UtcNow - olderThan;
            
            var oldLogs = await _context.SystemLogs
                .Where(l => l.CreatedAt < cutoffDate && l.Level < LogLevel.Error) // Keep errors longer
                .ToListAsync();

            if (oldLogs.Any())
            {
                _context.SystemLogs.RemoveRange(oldLogs);
                await _context.SaveChangesAsync();
                
                await LogInfoAsync($"Cleaned up {oldLogs.Count} old log entries older than {olderThan.TotalDays} days", 
                    "cleanup", "SystemLogService");
            }
        }

        public async Task ArchiveLogsAsync(DateTime beforeDate)
        {
            // Implementation for archiving logs to external storage
            // For now, just mark as archived in metadata
            var logsToArchive = await _context.SystemLogs
                .Where(l => l.CreatedAt < beforeDate)
                .ToListAsync();

            foreach (var log in logsToArchive)
            {
                var metadata = string.IsNullOrEmpty(log.Metadata) ? 
                    new Dictionary<string, object>() : 
                    JsonSerializer.Deserialize<Dictionary<string, object>>(log.Metadata) ?? new();
                
                metadata["archived"] = true;
                metadata["archived_at"] = DateTime.UtcNow;
                
                log.Metadata = JsonSerializer.Serialize(metadata);
            }

            await _context.SaveChangesAsync();
            
            await LogInfoAsync($"Archived {logsToArchive.Count} log entries before {beforeDate:yyyy-MM-dd}", 
                "archive", "SystemLogService");
        }

        public async Task<LogSystemHealth> GetLogSystemHealthAsync()
        {
            var now = DateTime.UtcNow;
            var oneHourAgo = now.AddHours(-1);
            var oneDayAgo = now.AddDays(-1);

            var totalLogs = await _context.SystemLogs.CountAsync();
            var logsLastHour = await _context.SystemLogs.CountAsync(l => l.CreatedAt >= oneHourAgo);
            var logsLastDay = await _context.SystemLogs.CountAsync(l => l.CreatedAt >= oneDayAgo);

            var levelDistribution = await GetLogCountsByLevelAsync(oneDayAgo, now);

            var topComponents = await _context.SystemLogs
                .Where(l => l.CreatedAt >= oneDayAgo)
                .GroupBy(l => l.Component)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key ?? "Unknown")
                .ToListAsync();

            var oldestLog = await _context.SystemLogs
                .OrderBy(l => l.CreatedAt)
                .Select(l => l.CreatedAt)
                .FirstOrDefaultAsync();

            var newestLog = await _context.SystemLogs
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => l.CreatedAt)
                .FirstOrDefaultAsync();

            var avgLogsPerMinute = logsLastHour / 60.0;

            return new LogSystemHealth
            {
                TotalLogs = totalLogs,
                LogsLastHour = logsLastHour,
                LogsLastDay = logsLastDay,
                LevelDistribution = levelDistribution,
                TopComponents = topComponents,
                AverageLogsPerMinute = avgLogsPerMinute,
                OldestLog = oldestLog == default ? null : oldestLog,
                NewestLog = newestLog == default ? null : newestLog,
                DatabaseSizeMB = 0 // Would be calculated from database stats
            };
        }
    }
}