using IvaScanner.Core.Models;
using IvaScanner.Worker.Configuration;
using System.Diagnostics;

namespace IvaScanner.Worker.Services;

public interface IWorkerStateManager
{
    string WorkerId { get; }
    string WorkerName { get; }
    WorkerStatus Status { get; }
    DateTime LastHeartbeat { get; }
    int ActiveTasks { get; }
    int CompletedTasks { get; }
    int FailedTasks { get; }
    Dictionary<string, object> Capabilities { get; }
    
    Task InitializeAsync();
    Task SetStatusAsync(WorkerStatus status);
    Task UpdateHeartbeatAsync();
    Task IncrementActiveTasksAsync();
    Task DecrementActiveTasksAsync();
    Task IncrementCompletedTasksAsync();
    Task IncrementFailedTasksAsync();
    Task SetCapabilityAsync(string key, object value);
    Task<WorkerRegistrationRequest> GetRegistrationRequestAsync();
    Task<WorkerHeartbeatRequest> GetHeartbeatRequestAsync();
}

public class WorkerStateManager : IWorkerStateManager
{
    private readonly ILogger<WorkerStateManager> _logger;
    private readonly WorkerConfiguration _config;
    private readonly object _lock = new();
    
    private string _workerId = string.Empty;
    private string _workerName = string.Empty;
    private WorkerStatus _status = WorkerStatus.Offline;
    private DateTime _lastHeartbeat = DateTime.UtcNow;
    private int _activeTasks = 0;
    private int _completedTasks = 0;
    private int _failedTasks = 0;
    private Dictionary<string, object> _capabilities = new();

    public WorkerStateManager(
        ILogger<WorkerStateManager> logger,
        Microsoft.Extensions.Options.IOptions<WorkerConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
    }

    public string WorkerId => _workerId;
    public string WorkerName => _workerName;
    public WorkerStatus Status => _status;
    public DateTime LastHeartbeat => _lastHeartbeat;
    public int ActiveTasks => _activeTasks;
    public int CompletedTasks => _completedTasks;
    public int FailedTasks => _failedTasks;
    public Dictionary<string, object> Capabilities => new(_capabilities);

    public Task InitializeAsync()
    {
        lock (_lock)
        {
            _workerId = !string.IsNullOrEmpty(_config.Id) 
                ? _config.Id 
                : Environment.MachineName + "-" + Guid.NewGuid().ToString("N")[..8];
                
            _workerName = _config.Name.Replace("{MachineName}", Environment.MachineName);
            
            _capabilities = new Dictionary<string, object>
            {
                ["maxConcurrentTasks"] = _config.MaxConcurrentTasks,
                ["supportedTaskTypes"] = new[] { "iva_scan" },
                ["version"] = "1.0.0",
                ["platform"] = Environment.OSVersion.Platform.ToString(),
                ["framework"] = Environment.Version.ToString()
            };
            
            _logger.LogInformation("Worker initialized - ID: {WorkerId}, Name: {WorkerName}", 
                _workerId, _workerName);
        }
        
        return Task.CompletedTask;
    }

    public Task SetStatusAsync(WorkerStatus status)
    {
        lock (_lock)
        {
            if (_status != status)
            {
                var oldStatus = _status;
                _status = status;
                _logger.LogInformation("Worker status changed from {OldStatus} to {NewStatus}", 
                    oldStatus, status);
            }
        }
        
        return Task.CompletedTask;
    }

    public Task UpdateHeartbeatAsync()
    {
        lock (_lock)
        {
            _lastHeartbeat = DateTime.UtcNow;
        }
        
        return Task.CompletedTask;
    }

    public Task IncrementActiveTasksAsync()
    {
        lock (_lock)
        {
            _activeTasks++;
        }
        
        return Task.CompletedTask;
    }

    public Task DecrementActiveTasksAsync()
    {
        lock (_lock)
        {
            if (_activeTasks > 0)
                _activeTasks--;
        }
        
        return Task.CompletedTask;
    }

    public Task IncrementCompletedTasksAsync()
    {
        lock (_lock)
        {
            _completedTasks++;
        }
        
        return Task.CompletedTask;
    }

    public Task IncrementFailedTasksAsync()
    {
        lock (_lock)
        {
            _failedTasks++;
        }
        
        return Task.CompletedTask;
    }

    public Task SetCapabilityAsync(string key, object value)
    {
        lock (_lock)
        {
            _capabilities[key] = value;
        }
        
        return Task.CompletedTask;
    }

    public Task<WorkerRegistrationRequest> GetRegistrationRequestAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(new WorkerRegistrationRequest
            {
                WorkerId = _workerId,
                Name = _workerName,
                MaxConcurrentTasks = _config.MaxConcurrentTasks,
                Capabilities = new Dictionary<string, object>(_capabilities)
            });
        }
    }

    public Task<WorkerHeartbeatRequest> GetHeartbeatRequestAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(new WorkerHeartbeatRequest
            {
                WorkerId = _workerId,
                Status = _status,
                ActiveTasks = _activeTasks,
                CompletedTasks = _completedTasks,
                FailedTasks = _failedTasks,
                SystemInfo = new Dictionary<string, object>
                {
                    ["cpuUsage"] = 0, // TODO: Implement CPU monitoring
                    ["memoryUsage"] = GC.GetTotalMemory(false),
                    ["uptime"] = DateTime.UtcNow - Process.GetCurrentProcess().StartTime
                }
            });
        }
    }
}