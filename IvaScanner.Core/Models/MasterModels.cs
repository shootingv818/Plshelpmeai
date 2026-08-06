using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace IvaScanner.Core.Models
{
    public enum WorkerStatus
    {
        Offline = 0,
        Online = 1,
        Working = 2,
        Degraded = 3,
        Error = 4
    }

    public enum TaskStatus
    {
        Pending = 0,
        InProgress = 1,
        Completed = 2,
        Failed = 3,
        DeadLetter = 4,
        Cancelled = 5
    }

    public enum ScanJobStatus
    {
        Created = 0,
        Queued = 1,
        Running = 2,
        Paused = 3,
        Completed = 4,
        Failed = 5,
        Cancelled = 6
    }

    // Alias for convenience
    public enum JobStatus
    {
        Created = 0,
        Queued = 1,
        Running = 2,
        Paused = 3,
        Completed = 4,
        Failed = 5,
        Cancelled = 6
    }

    public enum AccountStatus
    {
        Active = 0,
        Blocked = 1,
        Error = 2,
        Expired = 3
    }

    public class Worker
    {
        [Key]
        public string Id { get; set; } = string.Empty;
        
        [Required]
        public string Name { get; set; } = string.Empty;
        
        public WorkerStatus Status { get; set; } = WorkerStatus.Offline;
        
        public DateTime LastHeartbeat { get; set; }
        
        public string? CurrentTaskId { get; set; }
        
        public int TasksCompleted { get; set; }
        
        public int TasksFailed { get; set; }
        
        public string? ProxyUrl { get; set; }
        
        public string? IvaAccountId { get; set; }
        
        public TimeSpan Latency { get; set; }
        
        public string? LastError { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public IvaAccount? IvaAccount { get; set; }
        public ICollection<ScanTask> Tasks { get; set; } = new List<ScanTask>();
    }

    public class IvaAccount
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string PhoneNumber { get; set; } = string.Empty;
        
        public string? SessionData { get; set; }
        
        public AccountStatus Status { get; set; } = AccountStatus.Active;
        
        public bool IsActive { get; set; } = true;
        
        public string? AssignedWorkerId { get; set; }
        
        public DateTime LastUsed { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        public string? LastError { get; set; }

        // Navigation properties
        public Worker? AssignedWorker { get; set; }
    }

    public class ScanJob
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string CardNumber { get; set; } = string.Empty;
        
        public string PhoneNumbers { get; set; } = string.Empty; // JSON array
        
        public ScanJobStatus Status { get; set; } = ScanJobStatus.Created;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? StartedAt { get; set; }
        
        public DateTime? CompletedAt { get; set; }
        
        public string? Result { get; set; } // JSON serialized CardInfo
        
        public int Progress { get; set; } = 0;
        
        public int TotalTasks { get; set; }
        
        public int CompletedTasks { get; set; }
        
        public int FailedTasks { get; set; }
        
        public string? ErrorMessage { get; set; }
        
        public string? CreatedBy { get; set; }

        // Navigation properties
        public ICollection<ScanTask> Tasks { get; set; } = new List<ScanTask>();
    }

    public class ScanTask
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string JobId { get; set; } = string.Empty;
        
        public int RangeStart { get; set; }
        
        public int RangeEnd { get; set; }
        
        public TaskStatus Status { get; set; } = TaskStatus.Pending;
        
        public string? WorkerId { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? StartedAt { get; set; }
        
        public DateTime? CompletedAt { get; set; }
        
        public DateTime? LeaseExpiry { get; set; }
        
        public int RetryCount { get; set; } = 0;
        
        public string? Result { get; set; } // JSON result
        
        public string? ErrorMessage { get; set; }
        
        public TimeSpan? ProcessingTime { get; set; }

        // Navigation properties
        public ScanJob Job { get; set; } = null!;
        public Worker? Worker { get; set; }
    }

    public class SystemLog
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public LogLevel Level { get; set; }
        
        [Required]
        public string Message { get; set; } = string.Empty;
        
        public string? Exception { get; set; }
        
        public string? WorkerId { get; set; }
        
        public string? JobId { get; set; }
        
        public string? TaskId { get; set; }
        
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        public string? Properties { get; set; } // JSON additional properties
        
        public string Source { get; set; } = string.Empty;
        
        public string Category { get; set; } = string.Empty;
    }

    // DTOs for API communication
    public class WorkerRegistrationRequest
    {
        [Required]
        public string WorkerId { get; set; } = string.Empty;
        
        [Required]
        public string Name { get; set; } = string.Empty;
        
        public int MaxConcurrentTasks { get; set; } = 1;
        
        public Dictionary<string, object> Capabilities { get; set; } = new();
    }

    public class WorkerHeartbeatRequest
    {
        [Required]
        public string WorkerId { get; set; } = string.Empty;
        
        public WorkerStatus Status { get; set; }
        
        public int ActiveTasks { get; set; }
        
        public int CompletedTasks { get; set; }
        
        public int FailedTasks { get; set; }
        
        public Dictionary<string, object> SystemInfo { get; set; } = new();
    }

    public class TaskRequest
    {
        [Required]
        public string WorkerId { get; set; } = string.Empty;
    }

    public class TaskCompleteRequest
    {
        [Required]
        public string TaskId { get; set; } = string.Empty;
        
        [Required]
        public string WorkerId { get; set; } = string.Empty;
        
        public bool Success { get; set; }
        
        public string? Result { get; set; }
        
        public string? ErrorMessage { get; set; }
    }

    public class CreateScanJobRequest
    {
        [Required]
        public string CardNumber { get; set; } = string.Empty;
        
        [Required]
        public List<string> PhoneNumbers { get; set; } = new();
        
        public string? CreatedBy { get; set; }
    }

    public class TaskAssignment
    {
        public string TaskId { get; set; } = string.Empty;
        public string JobId { get; set; } = string.Empty;
        public string CardNumber { get; set; } = string.Empty;
        public List<string> PhoneNumbers { get; set; } = new();
        public int RangeStart { get; set; }
        public int RangeEnd { get; set; }
        public string? IvaAccountPhone { get; set; }
        public string? ProxyUrl { get; set; }
        public DateTime LeaseExpiry { get; set; }
    }

    // Additional DTOs for Worker
    public class ScanTaskDto
    {
        public string TaskId { get; set; } = string.Empty;
        public string JobId { get; set; } = string.Empty;
        public string TaskType { get; set; } = string.Empty;
        public List<string> CvvList { get; set; } = new();
        public IvaAccountDto IvaAccount { get; set; } = null!;
        public DateTime LeaseExpiry { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public class IvaAccountDto
    {
        public string Id { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string? SessionData { get; set; }
        public AccountStatus Status { get; set; }
        public bool IsActive { get; set; }
    }

    public class ProxyServerDto
    {
        public int Id { get; set; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public ProxyType Type { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Country { get; set; }
        public ProxyStatus Status { get; set; }
    }

    public class TaskCompletionRequest
    {
        public string TaskId { get; set; } = string.Empty;
        public string WorkerId { get; set; } = string.Empty;
        public List<IvaResult> Results { get; set; } = new();
        public DateTime CompletedAt { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public int ProcessedItems { get; set; }
    }

    public class TaskFailureRequest
    {
        public string TaskId { get; set; } = string.Empty;
        public string WorkerId { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime FailedAt { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public int ProcessedItems { get; set; }
    }

    public class ProxyStatusReport
    {
        public int ProxyId { get; set; }
        public string WorkerId { get; set; } = string.Empty;
        public bool IsWorking { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
        public double ResponseTime { get; set; }
    }
    // IVA Account management requests
    {
        [Required]
        public string PhoneNumber { get; set; } = "";
        public string? SessionData { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class UpdateIvaAccountRequest
    {
        [Required]
        public string Id { get; set; } = "";
        [Required]
        public string PhoneNumber { get; set; } = "";
        public string? SessionData { get; set; }
        public bool IsActive { get; set; }
    }
}

    
    // Proxy Management Models
    public class ProxyServer
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public ProxyType Type { get; set; } = ProxyType.Http;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public ProxyStatus Status { get; set; } = ProxyStatus.Unknown;
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? Provider { get; set; }
        public int Priority { get; set; } = 1; // 1=highest, 10=lowest
        public double ResponseTime { get; set; } // in milliseconds
        public DateTime LastChecked { get; set; } = DateTime.UtcNow;
        public DateTime? LastUsed { get; set; }
        public int FailureCount { get; set; } = 0;
        public int SuccessCount { get; set; } = 0;
        public string? LastError { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public List<ProxyUsageLog> UsageLogs { get; set; } = new();
        public List<ProxyHealthCheck> HealthChecks { get; set; } = new();

        public string DisplayName => $"{Host}:{Port}";
        public double SuccessRate => (SuccessCount + FailureCount) > 0 ? 
            (double)SuccessCount / (SuccessCount + FailureCount) * 100 : 0;
    }

    public class ProxyUsageLog
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ProxyId { get; set; } = "";
        public string? WorkerId { get; set; }
        public string? JobId { get; set; }
        public string? TaskId { get; set; }
        public DateTime UsedAt { get; set; } = DateTime.UtcNow;
        public TimeSpan Duration { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int HttpStatusCode { get; set; }
        public double ResponseTime { get; set; }
        public string? UserAgent { get; set; }
        public string? TargetUrl { get; set; }

        // Navigation property
        public ProxyServer Proxy { get; set; } = null!;
    }

    public class ProxyHealthCheck
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ProxyId { get; set; } = "";
        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
        public bool IsHealthy { get; set; }
        public double ResponseTime { get; set; } // milliseconds
        public string? ErrorMessage { get; set; }
        public string TestUrl { get; set; } = "https://httpbin.org/ip";
        public string? ResponseData { get; set; }
        public int HttpStatusCode { get; set; }

        // Navigation property
        public ProxyServer Proxy { get; set; } = null!;
    }

    public class ProxyPool
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public ProxyPoolStrategy Strategy { get; set; } = ProxyPoolStrategy.RoundRobin;
        public bool IsActive { get; set; } = true;
        public int MaxProxiesPerWorker { get; set; } = 3;
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan RotationInterval { get; set; } = TimeSpan.FromHours(1);
        public double MinSuccessRate { get; set; } = 80.0;
        public int MaxFailures { get; set; } = 5;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public List<ProxyPoolMember> Members { get; set; } = new();
    }

    public class ProxyPoolMember
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ProxyPoolId { get; set; } = "";
        public string ProxyId { get; set; } = "";
        public int Weight { get; set; } = 1; // For weighted strategies
        public bool IsEnabled { get; set; } = true;
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ProxyPool ProxyPool { get; set; } = null!;
        public ProxyServer Proxy { get; set; } = null!;
    }

    public enum ProxyType
    {
        Http,
        Https,
        Socks4,
        Socks5
    }

    public enum ProxyStatus
    {
        Unknown,
        Working,
        Failed,
        Slow,
        Banned,
        Timeout
    }

    public enum ProxyPoolStrategy
    {
        RoundRobin,
        Random,
        LeastUsed,
        FastestResponse,
        HighestSuccess,
        WeightedRandom
    }

    // DTOs for API communication
    public class CreateProxyRequest
    {
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public ProxyType Type { get; set; } = ProxyType.Http;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? Provider { get; set; }
        public int Priority { get; set; } = 1;
    }

    public class UpdateProxyRequest
    {
        public string? Host { get; set; }
        public int? Port { get; set; }
        public ProxyType? Type { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? Provider { get; set; }
        public int? Priority { get; set; }
        public bool? IsActive { get; set; }
    }

    public class ProxyTestRequest
    {
        public string ProxyId { get; set; } = "";
        public string? TestUrl { get; set; }
        public int TimeoutSeconds { get; set; } = 10;
    }

    public class ProxyTestResult
    {
        public bool IsSuccessful { get; set; }
        public double ResponseTime { get; set; }
        public int HttpStatusCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ResponseData { get; set; }
        public DateTime TestedAt { get; set; } = DateTime.UtcNow;
    }

    public class CreateProxyPoolRequest
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public ProxyPoolStrategy Strategy { get; set; } = ProxyPoolStrategy.RoundRobin;
        public int MaxProxiesPerWorker { get; set; } = 3;
        public int HealthCheckIntervalMinutes { get; set; } = 5;
        public int RotationIntervalHours { get; set; } = 1;
        public double MinSuccessRate { get; set; } = 80.0;
        public int MaxFailures { get; set; } = 5;
        public List<string> ProxyIds { get; set; } = new();
    }

    public class ProxyStats
    {
        public int TotalProxies { get; set; }
        public int ActiveProxies { get; set; }
        public int WorkingProxies { get; set; }
        public int FailedProxies { get; set; }
        public double AverageResponseTime { get; set; }
        public double AverageSuccessRate { get; set; }
        public Dictionary<string, int> ProxiesByCountry { get; set; } = new();
        public Dictionary<ProxyStatus, int> ProxiesByStatus { get; set; } = new();
    }

    // Additional DTOs for Worker
    public class ScanTaskDto
    {
        public string TaskId { get; set; } = string.Empty;
        public string JobId { get; set; } = string.Empty;
        public string TaskType { get; set; } = string.Empty;
        public List<string> CvvList { get; set; } = new();
        public IvaAccountDto IvaAccount { get; set; } = null!;
        public DateTime LeaseExpiry { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public class IvaAccountDto
    {
        public string Id { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string? SessionData { get; set; }
        public AccountStatus Status { get; set; }
        public bool IsActive { get; set; }
    }

    public class ProxyServerDto
    {
        public int Id { get; set; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public ProxyType Type { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Country { get; set; }
        public ProxyStatus Status { get; set; }
    }

    public class TaskCompletionRequest
    {
        public string TaskId { get; set; } = string.Empty;
        public string WorkerId { get; set; } = string.Empty;
        public List<IvaResult> Results { get; set; } = new();
        public DateTime CompletedAt { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public int ProcessedItems { get; set; }
    }

    public class TaskFailureRequest
    {
        public string TaskId { get; set; } = string.Empty;
        public string WorkerId { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime FailedAt { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public int ProcessedItems { get; set; }
    }

    public class ProxyStatusReport
    {
        public int ProxyId { get; set; }
        public string WorkerId { get; set; } = string.Empty;
        public bool IsWorking { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
        public double ResponseTime { get; set; }
    }