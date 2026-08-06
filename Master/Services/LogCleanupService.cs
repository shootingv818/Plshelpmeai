using IvaScanner.Master.Services;

namespace IvaScanner.Master.Services
{
    public class LogCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<LogCleanupService> _logger;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(24); // Run daily
        private readonly TimeSpan _retentionPeriod = TimeSpan.FromDays(90); // Keep logs for 90 days

        public LogCleanupService(
            IServiceProvider serviceProvider,
            ILogger<LogCleanupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Log Cleanup Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PerformCleanupAsync();
                    await Task.Delay(_cleanupInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Service is stopping
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during log cleanup");
                    
                    // Wait a shorter time before retrying on error
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Log Cleanup Service stopped");
        }

        private async Task PerformCleanupAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var logService = scope.ServiceProvider.GetRequiredService<ISystemLogService>();

            _logger.LogInformation("Starting scheduled log cleanup...");

            try
            {
                // Get log health before cleanup
                var healthBefore = await logService.GetLogSystemHealthAsync();
                _logger.LogInformation("Log count before cleanup: {TotalLogs}", healthBefore.TotalLogs);

                // Cleanup old logs
                await logService.CleanupOldLogsAsync(_retentionPeriod);

                // Archive logs older than 30 days
                var archiveDate = DateTime.UtcNow.AddDays(-30);
                await logService.ArchiveLogsAsync(archiveDate);

                // Get log health after cleanup
                var healthAfter = await logService.GetLogSystemHealthAsync();
                _logger.LogInformation("Log count after cleanup: {TotalLogs}", healthAfter.TotalLogs);

                var cleaned = healthBefore.TotalLogs - healthAfter.TotalLogs;
                if (cleaned > 0)
                {
                    await logService.LogInfoAsync(
                        $"Automatic log cleanup completed. Removed {cleaned} old log entries",
                        "automatic_cleanup",
                        "LogCleanupService"
                    );
                }

                _logger.LogInformation("Scheduled log cleanup completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to perform scheduled log cleanup");
                
                // Try to log the error via the log service
                try
                {
                    await logService.LogErrorAsync(ex, 
                        "Automatic log cleanup failed",
                        "automatic_cleanup",
                        "LogCleanupService"
                    );
                }
                catch
                {
                    // Ignore if we can't even log the error
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Log Cleanup Service is stopping...");
            await base.StopAsync(cancellationToken);
        }
    }
}