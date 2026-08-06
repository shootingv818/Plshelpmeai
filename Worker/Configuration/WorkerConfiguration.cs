namespace IvaScanner.Worker.Configuration;

public class WorkerConfiguration
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MaxConcurrentTasks { get; set; } = 2;
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan TaskTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromMinutes(1);
    public bool EnableAutoRestart { get; set; } = true;
    public string WorkingDirectory { get; set; } = "./temp";
    public string LogLevel { get; set; } = "Information";
}

public class MasterConfiguration
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public int RetryAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);
}

public class IvaScannerConfiguration
{
    public TimeSpan RequestDelay { get; set; } = TimeSpan.FromSeconds(2);
    public int RetryAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);
}

public class ProxyConfiguration
{
    public bool Enabled { get; set; } = false;
    public string RotationStrategy { get; set; } = "RoundRobin";
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxFailures { get; set; } = 3;
    public string TestUrl { get; set; } = "https://httpbin.org/ip";
}