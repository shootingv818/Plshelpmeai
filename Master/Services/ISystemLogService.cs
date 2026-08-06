using IvaScanner.Core.Models;
using Microsoft.Extensions.Logging;

namespace IvaScanner.Master.Services
{
    public interface ISystemLogService
    {
        // Write logs
        Task LogAsync(LogLevel level, string message, string? context = null, 
            string? component = null, Dictionary<string, object>? metadata = null);
        Task LogAsync(LogLevel level, Exception exception, string message, string? context = null,
            string? component = null, Dictionary<string, object>? metadata = null);
        
        // Convenience methods
        Task LogInfoAsync(string message, string? context = null, string? component = null);
        Task LogWarningAsync(string message, string? context = null, string? component = null);
        Task LogErrorAsync(string message, string? context = null, string? component = null);
        Task LogErrorAsync(Exception exception, string message, string? context = null, string? component = null);
        Task LogDebugAsync(string message, string? context = null, string? component = null);
        Task LogCriticalAsync(string message, string? context = null, string? component = null);
        Task LogCriticalAsync(Exception exception, string message, string? context = null, string? component = null);
        
        // System event logs
        Task LogWorkerEventAsync(string workerId, WorkerEventType eventType, string details);
        Task LogScanEventAsync(string jobId, ScanEventType eventType, string details, Dictionary<string, object>? metadata = null);
        Task LogTaskEventAsync(string taskId, TaskEventType eventType, string details);
        Task LogSecurityEventAsync(SecurityEventType eventType, string details, string? ipAddress = null);
        Task LogPerformanceAsync(string component, string operation, TimeSpan duration, Dictionary<string, object>? metrics = null);
        
        // Query logs
        Task<List<SystemLog>> GetLogsAsync(int skip = 0, int take = 100, LogLevel? minLevel = null, 
            string? component = null, string? context = null, DateTime? fromDate = null, DateTime? toDate = null);
        Task<List<SystemLog>> SearchLogsAsync(string searchTerm, int skip = 0, int take = 100);
        Task<Dictionary<LogLevel, int>> GetLogCountsByLevelAsync(DateTime? fromDate = null, DateTime? toDate = null);
        Task<List<LogStatsItem>> GetLogStatsByComponentAsync(DateTime? fromDate = null, DateTime? toDate = null);
        Task<List<SystemLog>> GetRecentLogsAsync(int count = 50);
        Task<long> GetLogCountAsync(LogLevel? minLevel = null, DateTime? fromDate = null, DateTime? toDate = null);
        
        // Log management
        Task CleanupOldLogsAsync(TimeSpan olderThan);
        Task ArchiveLogsAsync(DateTime beforeDate);
        Task<LogSystemHealth> GetLogSystemHealthAsync();
    }

    public enum WorkerEventType
    {
        Connected,
        Disconnected,
        Registered,
        TaskAssigned,
        TaskCompleted,
        TaskFailed,
        HeartbeatMissed,
        StatusChanged
    }

    public enum ScanEventType
    {
        JobCreated,
        JobStarted,
        JobPaused,
        JobResumed,
        JobCompleted,
        JobCancelled,
        JobFailed,
        ResultFound,
        ExpiryDetected
    }

    public enum TaskEventType
    {
        Created,
        Assigned,
        Started,
        Completed,
        Failed,
        Retried,
        Cancelled,
        TimedOut
    }

    public enum SecurityEventType
    {
        UnauthorizedAccess,
        InvalidApiKey,
        SuspiciousActivity,
        RateLimitExceeded,
        LoginAttempt,
        ConfigurationChanged
    }

    public class LogStatsItem
    {
        public string Component { get; set; } = "";
        public int TotalCount { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public DateTime LastLogAt { get; set; }
    }

    public class LogSystemHealth
    {
        public long TotalLogs { get; set; }
        public long LogsLastHour { get; set; }
        public long LogsLastDay { get; set; }
        public Dictionary<LogLevel, int> LevelDistribution { get; set; } = new();
        public List<string> TopComponents { get; set; } = new();
        public double AverageLogsPerMinute { get; set; }
        public DateTime? OldestLog { get; set; }
        public DateTime? NewestLog { get; set; }
        public long DatabaseSizeMB { get; set; }
    }
}