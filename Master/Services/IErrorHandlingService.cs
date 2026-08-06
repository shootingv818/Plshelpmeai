using IvaScanner.Core.Models;

namespace IvaScanner.Master.Services
{
    public interface IErrorHandlingService
    {
        // Retry policies
        Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            RetryPolicy? policy = null,
            string? operationName = null,
            CancellationToken cancellationToken = default);

        Task ExecuteWithRetryAsync(
            Func<Task> operation,
            RetryPolicy? policy = null,
            string? operationName = null,
            CancellationToken cancellationToken = default);

        // Circuit breaker
        Task<T> ExecuteWithCircuitBreakerAsync<T>(
            string circuitBreakerKey,
            Func<Task<T>> operation,
            CircuitBreakerPolicy? policy = null);

        Task ExecuteWithCircuitBreakerAsync(
            string circuitBreakerKey,
            Func<Task> operation,
            CircuitBreakerPolicy? policy = null);

        // Combined retry + circuit breaker
        Task<T> ExecuteWithResilienceAsync<T>(
            string operationKey,
            Func<Task<T>> operation,
            ResiliencePolicy? policy = null,
            CancellationToken cancellationToken = default);

        // Error classification
        bool IsRetryableError(Exception exception);
        bool IsTransientError(Exception exception);
        ErrorSeverity GetErrorSeverity(Exception exception);

        // Error recovery
        Task<ErrorRecoveryResult> AttemptErrorRecoveryAsync(
            string componentName,
            Exception error,
            string? context = null);

        // Health check and monitoring
        Task<ComponentHealth> CheckComponentHealthAsync(string componentName);
        Task<Dictionary<string, ComponentHealth>> GetSystemHealthAsync();
        Task RecordErrorAsync(string componentName, Exception error, string? context = null);

        // Configuration
        RetryPolicy GetDefaultRetryPolicy();
        CircuitBreakerPolicy GetDefaultCircuitBreakerPolicy();
        RetryPolicy GetRetryPolicyForOperation(string operationType);
    }

    public class RetryPolicy
    {
        public int MaxRetryAttempts { get; set; } = 3;
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
        public double BackoffMultiplier { get; set; } = 2.0;
        public bool UseJitter { get; set; } = true;
        public List<Type> RetryableExceptions { get; set; } = new();
        public List<Type> NonRetryableExceptions { get; set; } = new();
        public Func<Exception, bool>? CustomRetryCondition { get; set; }
    }

    public class CircuitBreakerPolicy
    {
        public int FailureThreshold { get; set; } = 5;
        public TimeSpan OpenTimeout { get; set; } = TimeSpan.FromMinutes(1);
        public int MinimumThroughput { get; set; } = 10;
        public double FailureRate { get; set; } = 0.5; // 50%
        public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromMinutes(1);
    }

    public class ResiliencePolicy
    {
        public RetryPolicy? RetryPolicy { get; set; }
        public CircuitBreakerPolicy? CircuitBreakerPolicy { get; set; }
        public TimeSpan? Timeout { get; set; }
        public bool EnableBulkhead { get; set; } = false;
        public int BulkheadMaxConcurrency { get; set; } = 10;
    }

    public class ErrorRecoveryResult
    {
        public bool IsRecovered { get; set; }
        public string? RecoveryAction { get; set; }
        public List<string> RecoverySteps { get; set; } = new();
        public TimeSpan RecoveryTime { get; set; }
        public Exception? RecoveryError { get; set; }
    }

    public class ComponentHealth
    {
        public string ComponentName { get; set; } = "";
        public HealthStatus Status { get; set; }
        public DateTime LastChecked { get; set; }
        public int ErrorCount { get; set; }
        public TimeSpan? LastErrorTime { get; set; }
        public string? LastError { get; set; }
        public double SuccessRate { get; set; }
        public bool IsCircuitBreakerOpen { get; set; }
        public Dictionary<string, object> Metrics { get; set; } = new();
    }

    public enum HealthStatus
    {
        Healthy,
        Degraded,
        Unhealthy,
        Critical
    }

    public enum ErrorSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum CircuitBreakerState
    {
        Closed,
        Open,
        HalfOpen
    }

    public class CircuitBreaker
    {
        public string Key { get; set; } = "";
        public CircuitBreakerState State { get; set; }
        public DateTime LastStateChange { get; set; }
        public int FailureCount { get; set; }
        public int SuccessCount { get; set; }
        public CircuitBreakerPolicy Policy { get; set; } = new();
        public DateTime NextAttemptTime { get; set; }
    }

    // Pre-defined retry policies for different operations
    public static class RetryPolicies
    {
        public static RetryPolicy Database => new()
        {
            MaxRetryAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(500),
            MaxDelay = TimeSpan.FromSeconds(5),
            BackoffMultiplier = 2.0,
            UseJitter = true,
            RetryableExceptions = new List<Type> 
            { 
                typeof(TimeoutException),
                typeof(TaskCanceledException),
                typeof(HttpRequestException)
            }
        };

        public static RetryPolicy Network => new()
        {
            MaxRetryAttempts = 5,
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
            BackoffMultiplier = 1.5,
            UseJitter = true,
            RetryableExceptions = new List<Type> 
            { 
                typeof(HttpRequestException),
                typeof(TimeoutException),
                typeof(TaskCanceledException)
            }
        };

        public static RetryPolicy Redis => new()
        {
            MaxRetryAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(250),
            MaxDelay = TimeSpan.FromSeconds(2),
            BackoffMultiplier = 2.0,
            UseJitter = false,
            RetryableExceptions = new List<Type> 
            { 
                typeof(TimeoutException),
                typeof(TaskCanceledException)
            }
        };

        public static RetryPolicy IvaApi => new()
        {
            MaxRetryAttempts = 4,
            InitialDelay = TimeSpan.FromSeconds(2),
            MaxDelay = TimeSpan.FromMinutes(1),
            BackoffMultiplier = 2.0,
            UseJitter = true,
            RetryableExceptions = new List<Type> 
            { 
                typeof(HttpRequestException),
                typeof(TimeoutException)
            },
            NonRetryableExceptions = new List<Type>
            {
                typeof(UnauthorizedAccessException),
                typeof(ArgumentException)
            }
        };

        public static RetryPolicy ProxyTest => new()
        {
            MaxRetryAttempts = 2,
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(5),
            BackoffMultiplier = 2.0,
            UseJitter = true
        };
    }

    // Pre-defined circuit breaker policies
    public static class CircuitBreakerPolicies
    {
        public static CircuitBreakerPolicy Database => new()
        {
            FailureThreshold = 5,
            OpenTimeout = TimeSpan.FromMinutes(2),
            MinimumThroughput = 10,
            FailureRate = 0.6
        };

        public static CircuitBreakerPolicy ExternalApi => new()
        {
            FailureThreshold = 3,
            OpenTimeout = TimeSpan.FromMinutes(5),
            MinimumThroughput = 5,
            FailureRate = 0.5
        };

        public static CircuitBreakerPolicy ProxyService => new()
        {
            FailureThreshold = 10,
            OpenTimeout = TimeSpan.FromMinutes(1),
            MinimumThroughput = 20,
            FailureRate = 0.7
        };
    }
}