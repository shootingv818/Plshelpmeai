using IvaScanner.Master.Services;

namespace IvaScanner.Master.Services
{
    public class TaskProcessorService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TaskProcessorService> _logger;
        private readonly IConfiguration _config;

        public TaskProcessorService(
            IServiceProvider serviceProvider,
            ILogger<TaskProcessorService> logger,
            IConfiguration config)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Task Processor Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var taskDistribution = scope.ServiceProvider.GetRequiredService<ITaskDistributionService>();
                    var scanOrchestrator = scope.ServiceProvider.GetRequiredService<IScanOrchestrator>();
                    var jobService = scope.ServiceProvider.GetRequiredService<IScanJobService>();

                    // Return expired tasks to queue
                    await taskDistribution.ReturnExpiredTasksToQueueAsync();

                    // Monitor job progress for all active jobs
                    await scanOrchestrator.MonitorJobProgressAsync();

                    // Update job progress statistics
                    var activeJobs = await jobService.GetActiveJobsAsync();
                    foreach (var job in activeJobs)
                    {
                        await jobService.UpdateJobProgressAsync(job.Id);
                    }

                    // Wait before next iteration
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in TaskProcessorService execution");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("Task Processor Service stopped");
        }
    }
}