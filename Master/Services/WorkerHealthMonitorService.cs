using IvaScanner.Master.Services;

namespace IvaScanner.Master.Services
{
    public class WorkerHealthMonitorService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<WorkerHealthMonitorService> _logger;
        private readonly IConfiguration _config;

        public WorkerHealthMonitorService(
            IServiceProvider serviceProvider,
            ILogger<WorkerHealthMonitorService> logger,
            IConfiguration config)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker Health Monitor Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var workerService = scope.ServiceProvider.GetRequiredService<IWorkerService>();

                    var heartbeatTimeoutSeconds = _config.GetValue<int>("WorkerSettings:HeartbeatTimeoutSeconds", 60);
                    var timeout = TimeSpan.FromSeconds(heartbeatTimeoutSeconds);

                    // Get stale workers (haven't sent heartbeat recently)
                    var staleWorkers = await workerService.GetStaleWorkersAsync(timeout);

                    foreach (var worker in staleWorkers)
                    {
                        _logger.LogWarning("Worker {WorkerId} is stale (last heartbeat: {LastHeartbeat})", 
                            worker.Id, worker.LastHeartbeat);

                        await workerService.MarkWorkerOfflineAsync(worker.Id, "Heartbeat timeout");
                    }

                    // Wait before next check
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in WorkerHealthMonitorService execution");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("Worker Health Monitor Service stopped");
        }
    }
}