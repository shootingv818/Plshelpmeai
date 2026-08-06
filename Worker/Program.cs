using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using IvaScanner.Worker.Services;
using IvaScanner.Worker.Configuration;
using System.Text.Json;

namespace IvaScanner.Worker;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("Starting IVA Scanner Worker...");
            
            var host = CreateHostBuilder(args).Build();
            
            // Start the worker
            await host.RunAsync();
            
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Worker terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .UseWindowsService()
            .UseSystemd()
            .ConfigureAppConfiguration((hostContext, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddJsonFile($"appsettings.{hostContext.HostingEnvironment.EnvironmentName}.json", 
                    optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables();
                config.AddCommandLine(args);
            })
            .ConfigureServices((hostContext, services) =>
            {
                // Configuration
                services.Configure<WorkerConfiguration>(
                    hostContext.Configuration.GetSection("Worker"));
                services.Configure<MasterConfiguration>(
                    hostContext.Configuration.GetSection("Master"));

                // HTTP Client
                services.AddHttpClient<IMasterApiClient, MasterApiClient>((serviceProvider, client) =>
                {
                    var config = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MasterConfiguration>>();
                    client.BaseAddress = new Uri(config.Value.BaseUrl);
                    client.Timeout = TimeSpan.FromMinutes(5);
                });

                // Services (IMasterApiClient is already registered above via AddHttpClient
                // as a typed client, so it must NOT be re-registered here or the
                // configured BaseAddress/Timeout would be lost)
                services.AddSingleton<IWorkerStateManager, WorkerStateManager>();
                services.AddSingleton<ITaskExecutor, TaskExecutor>();
                services.AddSingleton<IIvaWorkerClient, IvaWorkerClient>();
                services.AddSingleton<IProxyManager, ProxyManager>();

                // Background Services
                services.AddHostedService<WorkerService>();
                services.AddHostedService<HeartbeatService>();
                services.AddHostedService<TaskProcessingService>();

                // JSON Serialization Options
                services.ConfigureHttpJsonOptions(options =>
                {
                    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                    options.SerializerOptions.WriteIndented = true;
                });
            });
}