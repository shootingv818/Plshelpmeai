using System.ComponentModel.DataAnnotations;

namespace IvaScanner.Core.Models
{
    // Remote Server Management Models
    public class RemoteServer
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        public string IpAddress { get; set; } = string.Empty;
        
        public int SshPort { get; set; } = 22;
        
        [Required]
        public string Username { get; set; } = string.Empty;
        
        public string? Password { get; set; }
        
        public string? PrivateKeyPath { get; set; }
        
        public ServerOS OperatingSystem { get; set; } = ServerOS.Linux;
        
        public ServerStatus Status { get; set; } = ServerStatus.Unknown;
        
        public string? WorkerPath { get; set; } = "/opt/iva-worker";
        
        public int MaxWorkers { get; set; } = 2;
        
        public int ActiveWorkers { get; set; } = 0;
        
        public DateTime LastChecked { get; set; } = DateTime.UtcNow;
        
        public string? LastError { get; set; }
        
        public bool AutoDeploy { get; set; } = false;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public List<RemoteWorker> Workers { get; set; } = new();
        public List<ServerHealthCheck> HealthChecks { get; set; } = new();
    }

    public class RemoteWorker
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string ServerId { get; set; } = string.Empty;
        
        [Required]
        public string WorkerId { get; set; } = string.Empty;
        
        public string ProcessId { get; set; } = string.Empty;
        
        public WorkerStatus Status { get; set; } = WorkerStatus.Offline;
        
        public DateTime DeployedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? LastHeartbeat { get; set; }
        
        public string? Version { get; set; }
        
        public string? ConfigPath { get; set; }
        
        public string? LogPath { get; set; }

        // Navigation properties
        public RemoteServer Server { get; set; } = null!;
    }

    public class ServerHealthCheck
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string ServerId { get; set; } = string.Empty;
        
        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
        
        public bool IsOnline { get; set; }
        
        public bool HasDotNet { get; set; }
        
        public string? DotNetVersion { get; set; }
        
        public double CpuUsage { get; set; }
        
        public double MemoryUsage { get; set; }
        
        public double DiskUsage { get; set; }
        
        public int RunningWorkers { get; set; }
        
        public string? ErrorMessage { get; set; }

        // Navigation property
        public RemoteServer Server { get; set; } = null!;
    }

    public class DeploymentJob
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string ServerId { get; set; } = string.Empty;
        
        public DeploymentType Type { get; set; }
        
        public DeploymentStatus Status { get; set; } = DeploymentStatus.Pending;
        
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? CompletedAt { get; set; }
        
        public string? ErrorMessage { get; set; }
        
        public List<DeploymentStep> Steps { get; set; } = new();
        
        public string CreatedBy { get; set; } = string.Empty;

        // Navigation property
        public RemoteServer Server { get; set; } = null!;
    }

    public class DeploymentStep
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string JobId { get; set; } = string.Empty;
        
        [Required]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        public string Command { get; set; } = string.Empty;
        
        public StepStatus Status { get; set; } = StepStatus.Pending;
        
        public DateTime? StartedAt { get; set; }
        
        public DateTime? CompletedAt { get; set; }
        
        public string? Output { get; set; }
        
        public string? ErrorOutput { get; set; }
        
        public int Order { get; set; }

        // Navigation property
        public DeploymentJob Job { get; set; } = null!;
    }

    public enum ServerOS
    {
        Linux,
        Windows,
        MacOS
    }

    public enum ServerStatus
    {
        Unknown,
        Online,
        Offline,
        Deploying,
        Error,
        Maintenance
    }

    public enum DeploymentType
    {
        InstallWorker,
        UpdateWorker,
        StartWorker,
        StopWorker,
        RestartWorker,
        RemoveWorker,
        HealthCheck,
        SystemPrep
    }

    public enum DeploymentStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    public enum StepStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Skipped
    }

    // DTOs for Remote Server Management
    public class CreateRemoteServerRequest
    {
        [Required]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        public string IpAddress { get; set; } = string.Empty;
        
        public int SshPort { get; set; } = 22;
        
        [Required]
        public string Username { get; set; } = string.Empty;
        
        public string? Password { get; set; }
        
        public string? PrivateKeyPath { get; set; }
        
        public ServerOS OperatingSystem { get; set; } = ServerOS.Linux;
        
        public string? WorkerPath { get; set; }
        
        public int MaxWorkers { get; set; } = 2;
        
        public bool AutoDeploy { get; set; } = false;
    }

    public class UpdateRemoteServerRequest
    {
        public string? Name { get; set; }
        public string? IpAddress { get; set; }
        public int? SshPort { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? PrivateKeyPath { get; set; }
        public ServerOS? OperatingSystem { get; set; }
        public string? WorkerPath { get; set; }
        public int? MaxWorkers { get; set; }
        public bool? AutoDeploy { get; set; }
    }

    public class DeployWorkerRequest
    {
        [Required]
        public string ServerId { get; set; } = string.Empty;
        
        public int WorkerCount { get; set; } = 1;
        
        public Dictionary<string, string> Configuration { get; set; } = new();
        
        public bool StartImmediately { get; set; } = true;
        
        public bool EnableAutoRestart { get; set; } = true;
    }

    public class ServerConnectionTest
    {
        public string ServerId { get; set; } = string.Empty;
        public bool CanConnect { get; set; }
        public bool HasDotNet { get; set; }
        public string? DotNetVersion { get; set; }
        public bool HasSystemd { get; set; }
        public bool HasSudo { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime TestedAt { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> SystemInfo { get; set; } = new();
    }

    public class RemoteCommandResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = string.Empty;
        public string ErrorOutput { get; set; } = string.Empty;
        public int ExitCode { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    }

    public class DeploymentProgress
    {
        public string JobId { get; set; } = string.Empty;
        public DeploymentStatus Status { get; set; }
        public int TotalSteps { get; set; }
        public int CompletedSteps { get; set; }
        public int FailedSteps { get; set; }
        public string? CurrentStep { get; set; }
        public double Progress { get; set; }
        public List<DeploymentStep> Steps { get; set; } = new();
        public DateTime StartedAt { get; set; }
        public DateTime? EstimatedCompletion { get; set; }
    }

    // Specific deployment configurations
    public class WorkerDeploymentConfig
    {
        public string MasterUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public int MaxConcurrentTasks { get; set; } = 2;
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan TaskTimeout { get; set; } = TimeSpan.FromMinutes(10);
        public bool EnableProxy { get; set; } = false;
        public string LogLevel { get; set; } = "Information";
        public bool EnableAutoRestart { get; set; } = true;
        public Dictionary<string, string> CustomSettings { get; set; } = new();
    }
}