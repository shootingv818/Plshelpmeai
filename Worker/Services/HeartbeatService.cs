using Microsoft.Extensions.Hosting;
using IvaScanner.Worker.Configuration;

namespace IvaScanner.Worker.Services;

public class HeartbeatService : BackgroundService
{
    private readonly ILogger<HeartbeatService> _logger;
    private readonly IWorkerStateManager _stateManager;
    private readonly IMasterApiClient _masterClient;
    private readonly WorkerConfiguration _config;

    public HeartbeatService(
        ILogger<HeartbeatService> logger,
        IWorkerStateManager stateManager,
        IMasterApiClient masterClient,
        Microsoft.Extensions.Options.IOptions<WorkerConfiguration> config)
    {
        _logger = logger;
        _stateManager = stateManager;
        _masterClient = masterClient;
        _config = config.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Heartbeat service with interval {Interval}", _config.HeartbeatInterval);

        // Wait for worker to initialize
        while (_stateManager.Status == Core.Models.WorkerStatus.Offline && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Skip heartbeat if worker is not online
                if (_stateManager.Status != Core.Models.WorkerStatus.Online &&
                    _stateManager.Status != Core.Models.WorkerStatus.Busy)
                {
                    await Task.Delay(_config.HeartbeatInterval, stoppingToken);
                    continue;
                }

                // Update heartbeat timestamp
                await _stateManager.UpdateHeartbeatAsync();

                // Send heartbeat to master
                var heartbeatRequest = await _stateManager.GetHeartbeatRequestAsync();
                var success = await _masterClient.SendHeartbeatAsync(heartbeatRequest, stoppingToken);

                if (success)
                {
                    _logger.LogTrace("Heartbeat sent successfully for worker {WorkerId}", _stateManager.WorkerId);
                }
                else
                {
                    _logger.LogWarning("Failed to send heartbeat for worker {WorkerId}", _stateManager.WorkerId);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending heartbeat for worker {WorkerId}", _stateManager.WorkerId);
            }

            try
            {
                await Task.Delay(_config.HeartbeatInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Heartbeat service stopped");
    }
}