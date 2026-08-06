using Microsoft.Extensions.Hosting;
using IvaScanner.Worker.Configuration;

namespace IvaScanner.Worker.Services;

public class WorkerService : BackgroundService
{
    private readonly ILogger<WorkerService> _logger;
    private readonly IWorkerStateManager _stateManager;
    private readonly IMasterApiClient _masterClient;
    private readonly WorkerConfiguration _config;

    public WorkerService(
        ILogger<WorkerService> logger,
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
        try
        {
            _logger.LogInformation("Starting Worker service...");

            // Initialize worker state
            await _stateManager.InitializeAsync();
            await _stateManager.SetStatusAsync(Core.Models.WorkerStatus.Starting);

            // Create working directory
            if (!Directory.Exists(_config.WorkingDirectory))
            {
                Directory.CreateDirectory(_config.WorkingDirectory);
                _logger.LogDebug("Created working directory: {Directory}", _config.WorkingDirectory);
            }

            // Register with master
            var registrationRequest = await _stateManager.GetRegistrationRequestAsync();
            var registered = await _masterClient.RegisterWorkerAsync(registrationRequest, stoppingToken);

            if (!registered)
            {
                _logger.LogError("Failed to register with master server");
                await _stateManager.SetStatusAsync(Core.Models.WorkerStatus.Error);
                return;
            }

            _logger.LogInformation("Successfully registered with master server");
            await _stateManager.SetStatusAsync(Core.Models.WorkerStatus.Online);

            // Main service loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Service is mainly coordinating other background services
                    // The actual work is done by TaskProcessingService
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                    // Periodic health check
                    _logger.LogTrace("Worker service heartbeat - Status: {Status}, Active Tasks: {ActiveTasks}", 
                        _stateManager.Status, _stateManager.ActiveTasks);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in worker service loop");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in worker service");
            await _stateManager.SetStatusAsync(Core.Models.WorkerStatus.Error);
        }
        finally
        {
            _logger.LogInformation("Worker service stopping...");
            await _stateManager.SetStatusAsync(Core.Models.WorkerStatus.Stopping);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker service stop requested");
        await _stateManager.SetStatusAsync(Core.Models.WorkerStatus.Stopping);
        
        using var timeoutCts = new CancellationTokenSource(_config.ShutdownTimeout);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        await base.StopAsync(combinedCts.Token);
        
        await _stateManager.SetStatusAsync(Core.Models.WorkerStatus.Offline);
        _logger.LogInformation("Worker service stopped");
    }
}