using IvaScanner.Core.Models;
using IvaScanner.Master.Data;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;
using System.Text;
using System.Text.Json;

namespace IvaScanner.Master.Services
{
    public class RemoteServerService : IRemoteServerService
    {
        private readonly MasterDbContext _context;
        private readonly ILogger<RemoteServerService> _logger;
        private readonly ISystemLogService _systemLogService;
        private readonly IConfiguration _configuration;

        public RemoteServerService(
            MasterDbContext context,
            ILogger<RemoteServerService> logger,
            ISystemLogService systemLogService,
            IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _systemLogService = systemLogService;
            _configuration = configuration;
        }

        public async Task<RemoteServer> CreateServerAsync(CreateRemoteServerRequest request)
        {
            _logger.LogInformation("Creating remote server: {Name} ({IpAddress})", 
                request.Name, request.IpAddress);

            var server = new RemoteServer
            {
                Name = request.Name,
                IpAddress = request.IpAddress,
                SshPort = request.SshPort,
                Username = request.Username,
                Password = request.Password,
                PrivateKeyPath = request.PrivateKeyPath,
                OperatingSystem = request.OperatingSystem,
                WorkerPath = request.WorkerPath ?? GetDefaultWorkerPath(request.OperatingSystem),
                MaxWorkers = request.MaxWorkers,
                AutoDeploy = request.AutoDeploy,
                Status = ServerStatus.Unknown
            };

            _context.RemoteServers.Add(server);
            await _context.SaveChangesAsync();

            await _systemLogService.LogInformationAsync("RemoteServerService", 
                $"Remote server created: {server.Name} ({server.IpAddress})");

            return server;
        }

        public async Task<ServerConnectionTest> TestConnectionAsync(string serverId)
        {
            var server = await GetServerAsync(serverId);
            if (server == null)
            {
                throw new ArgumentException($"Server not found: {serverId}");
            }

            _logger.LogDebug("Testing connection to server: {ServerName}", server.Name);

            var result = new ServerConnectionTest
            {
                ServerId = serverId,
                TestedAt = DateTime.UtcNow
            };

            try
            {
                using var client = CreateSshClient(server);
                client.Connect();

                if (!client.IsConnected)
                {
                    result.ErrorMessage = "Unable to establish SSH connection";
                    return result;
                }

                result.CanConnect = true;

                // Test .NET installation
                var dotnetResult = await ExecuteCommandViaSshAsync(client, "dotnet --version");
                if (dotnetResult.Success)
                {
                    result.HasDotNet = true;
                    result.DotNetVersion = dotnetResult.Output.Trim();
                }

                // Test systemd (Linux)
                if (server.OperatingSystem == ServerOS.Linux)
                {
                    var systemdResult = await ExecuteCommandViaSshAsync(client, "systemctl --version");
                    result.HasSystemd = systemdResult.Success;
                }

                // Test sudo access
                var sudoResult = await ExecuteCommandViaSshAsync(client, "sudo -n true");
                result.HasSudo = sudoResult.Success;

                // Get system info
                await GetSystemInformation(client, result, server.OperatingSystem);

                client.Disconnect();

                // Update server status
                server.Status = ServerStatus.Online;
                server.LastChecked = DateTime.UtcNow;
                server.LastError = null;
                await _context.SaveChangesAsync();

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection test failed for server {ServerName}", server.Name);
                result.ErrorMessage = ex.Message;
                result.CanConnect = false;

                // Update server status
                server.Status = ServerStatus.Error;
                server.LastError = ex.Message;
                server.LastChecked = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return result;
        }
    }

        public async Task<string> DeployWorkersAsync(DeployWorkerRequest request)
        {
            var server = await GetServerAsync(request.ServerId);
            if (server == null)
            {
                throw new ArgumentException($"Server not found: {request.ServerId}");
            }

            _logger.LogInformation("Starting worker deployment to server: {ServerName}, Count: {WorkerCount}", 
                server.Name, request.WorkerCount);

            var job = new DeploymentJob
            {
                ServerId = request.ServerId,
                Type = DeploymentType.InstallWorker,
                Status = DeploymentStatus.Pending,
                CreatedBy = "System",
                Steps = GenerateDeploymentSteps(server, request)
            };

            _context.DeploymentJobs.Add(job);
            await _context.SaveChangesAsync();

            // Start deployment in background
            _ = Task.Run(async () => await ExecuteDeploymentJobAsync(job.Id));

            return job.Id;
        }

        private async Task ExecuteDeploymentJobAsync(string jobId)
        {
            var job = await _context.DeploymentJobs
                .Include(j => j.Server)
                .Include(j => j.Steps)
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (job == null) return;

            try
            {
                job.Status = DeploymentStatus.Running;
                await _context.SaveChangesAsync();

                using var client = CreateSshClient(job.Server);
                client.Connect();

                foreach (var step in job.Steps.OrderBy(s => s.Order))
                {
                    await ExecuteDeploymentStepAsync(client, step);
                    await _context.SaveChangesAsync();

                    if (step.Status == StepStatus.Failed)
                    {
                        job.Status = DeploymentStatus.Failed;
                        job.ErrorMessage = step.ErrorOutput;
                        break;
                    }
                }

                if (job.Status == DeploymentStatus.Running)
                {
                    job.Status = DeploymentStatus.Completed;
                }

                job.CompletedAt = DateTime.UtcNow;
                client.Disconnect();

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deployment job failed: {JobId}", jobId);
                job.Status = DeploymentStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.CompletedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        private async Task ExecuteDeploymentStepAsync(SshClient client, DeploymentStep step)
        {
            _logger.LogDebug("Executing deployment step: {StepName}", step.Name);

            step.Status = StepStatus.Running;
            step.StartedAt = DateTime.UtcNow;

            try
            {
                var result = await ExecuteCommandViaSshAsync(client, step.Command);
                
                step.Output = result.Output;
                step.ErrorOutput = result.ErrorOutput;
                step.Status = result.Success ? StepStatus.Completed : StepStatus.Failed;
                step.CompletedAt = DateTime.UtcNow;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deployment step failed: {StepName}", step.Name);
                step.Status = StepStatus.Failed;
                step.ErrorOutput = ex.Message;
                step.CompletedAt = DateTime.UtcNow;
            }
        }

        private List<DeploymentStep> GenerateDeploymentSteps(RemoteServer server, DeployWorkerRequest request)
        {
            var steps = new List<DeploymentStep>();
            var order = 1;

            if (server.OperatingSystem == ServerOS.Linux)
            {
                // Linux deployment steps
                steps.AddRange(new[]
                {
                    new DeploymentStep
                    {
                        Name = "Create worker directory",
                        Command = $"sudo mkdir -p {server.WorkerPath}",
                        Order = order++
                    },
                    new DeploymentStep
                    {
                        Name = "Create worker user",
                        Command = "sudo useradd --system --home /opt/iva-worker --shell /usr/sbin/nologin iva-worker || true",
                        Order = order++
                    },
                    new DeploymentStep
                    {
                        Name = "Set directory permissions",
                        Command = $"sudo chown -R iva-worker:iva-worker {server.WorkerPath}",
                        Order = order++
                    },
                    new DeploymentStep
                    {
                        Name = "Install .NET Runtime",
                        Command = GetDotNetInstallCommand(),
                        Order = order++
                    },
                    new DeploymentStep
                    {
                        Name = "Download worker files",
                        Command = GenerateDownloadCommand(server),
                        Order = order++
                    },
                    new DeploymentStep
                    {
                        Name = "Extract worker files",
                        Command = $"sudo tar -xzf /tmp/iva-worker.tar.gz -C {server.WorkerPath}",
                        Order = order++
                    },
                    new DeploymentStep
                    {
                        Name = "Create configuration",
                        Command = GenerateConfigCreationCommand(server, request.Configuration),
                        Order = order++
                    },
                    new DeploymentStep
                    {
                        Name = "Install systemd service",
                        Command = GenerateSystemdInstallCommand(server),
                        Order = order++
                    },
                    new DeploymentStep
                    {
                        Name = "Enable and start service",
                        Command = "sudo systemctl enable iva-worker && sudo systemctl start iva-worker",
                        Order = order++
                    }
                });
            }
            else if (server.OperatingSystem == ServerOS.Windows)
            {
                // Windows deployment steps
                steps.AddRange(new[]
                {
                    new DeploymentStep
                    {
                        Name = "Create worker directory",
                        Command = $"mkdir \"{server.WorkerPath}\" -Force",
                        Order = order++
                    },
                    new DeploymentStep
                    {
                        Name = "Download worker files",
                        Command = GenerateWindowsDownloadCommand(server),
                        Order = order++
                    },
                    new DeploymentStep
                    {
                        Name = "Extract worker files", 
                        Command = $"Expand-Archive -Path C:\\temp\\iva-worker.zip -DestinationPath \"{server.WorkerPath}\" -Force",
                        Order = order++
                    },
                    new DeploymentStep
                    {
                        Name = "Create configuration",
                        Command = GenerateWindowsConfigCommand(server, request.Configuration),
                        Order = order++
                    },
                    new DeploymentStep
                    {
                        Name = "Install Windows Service",
                        Command = $"sc create \"IvaWorker\" binPath=\"{server.WorkerPath}\\IvaScanner.Worker.exe\" start=auto",
                        Order = order++
                    },
                    new DeploymentStep
                    {
                        Name = "Start Windows Service",
                        Command = "sc start \"IvaWorker\"",
                        Order = order++
                    }
                });
            }

            return steps;
        }

        private SshClient CreateSshClient(RemoteServer server)
        {
            ConnectionInfo connectionInfo;

            if (!string.IsNullOrEmpty(server.Password))
            {
                connectionInfo = new ConnectionInfo(server.IpAddress, server.SshPort, server.Username,
                    new PasswordAuthenticationMethod(server.Username, server.Password));
            }
            else if (!string.IsNullOrEmpty(server.PrivateKeyPath))
            {
                var keyFile = new PrivateKeyFile(server.PrivateKeyPath);
                connectionInfo = new ConnectionInfo(server.IpAddress, server.SshPort, server.Username,
                    new PrivateKeyAuthenticationMethod(server.Username, keyFile));
            }
            else
            {
                throw new InvalidOperationException("No authentication method configured for server");
            }

            return new SshClient(connectionInfo);
        }

        private async Task<RemoteCommandResult> ExecuteCommandViaSshAsync(SshClient client, string command, int timeoutSeconds = 30)
        {
            var startTime = DateTime.UtcNow;
            
            try
            {
                using var cmd = client.CreateCommand(command);
                cmd.CommandTimeout = TimeSpan.FromSeconds(timeoutSeconds);
                
                var result = cmd.Execute();
                var output = cmd.Result;
                var errorOutput = cmd.Error;
                
                return new RemoteCommandResult
                {
                    Success = cmd.ExitStatus == 0,
                    Output = output ?? string.Empty,
                    ErrorOutput = errorOutput ?? string.Empty,
                    ExitCode = cmd.ExitStatus,
                    Duration = DateTime.UtcNow - startTime,
                    ExecutedAt = startTime
                };
            }
            catch (Exception ex)
            {
                return new RemoteCommandResult
                {
                    Success = false,
                    ErrorOutput = ex.Message,
                    Duration = DateTime.UtcNow - startTime,
                    ExecutedAt = startTime
                };
            }
        }

        // Helper methods
        private string GetDefaultWorkerPath(ServerOS os)
        {
            return os switch
            {
                ServerOS.Linux => "/opt/iva-worker",
                ServerOS.Windows => "C:\\IvaWorker",
                ServerOS.MacOS => "/usr/local/iva-worker",
                _ => "/opt/iva-worker"
            };
        }

        private string GetDotNetInstallCommand()
        {
            return @"
if ! command -v dotnet &> /dev/null; then
    wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
    sudo dpkg -i /tmp/packages-microsoft-prod.deb
    sudo apt-get update
    sudo apt-get install -y aspnetcore-runtime-8.0
fi";
        }

        private string GenerateDownloadCommand(RemoteServer server)
        {
            var masterUrl = _configuration["Master:PublicUrl"] ?? "http://localhost:5000";
            return $"wget {masterUrl}/api/deployment/worker-package -O /tmp/iva-worker.tar.gz";
        }

        private string GenerateConfigCreationCommand(RemoteServer server, Dictionary<string, string> config)
        {
            var masterUrl = _configuration["Master:PublicUrl"] ?? "http://localhost:5000";
            var apiKey = _configuration["Worker:DefaultApiKey"] ?? Guid.NewGuid().ToString();

            var workerConfig = new
            {
                Master = new
                {
                    BaseUrl = masterUrl,
                    ApiKey = apiKey
                },
                Worker = new
                {
                    Name = $"Worker-{server.Name}",
                    MaxConcurrentTasks = 2,
                    HeartbeatInterval = "00:00:30",
                    WorkingDirectory = $"{server.WorkerPath}/temp"
                },
                Logging = new
                {
                    LogLevel = new { Default = "Information" }
                }
            };

            var configJson = JsonSerializer.Serialize(workerConfig, new JsonSerializerOptions { WriteIndented = true });
            var encodedConfig = Convert.ToBase64String(Encoding.UTF8.GetBytes(configJson));

            return $"echo '{encodedConfig}' | base64 -d | sudo tee {server.WorkerPath}/appsettings.json";
        }

        private string GenerateSystemdInstallCommand(RemoteServer server)
        {
            var serviceContent = $@"[Unit]
Description=IVA Scanner Worker
After=network.target

[Service]
Type=notify
User=iva-worker
Group=iva-worker
WorkingDirectory={server.WorkerPath}
ExecStart=/usr/bin/dotnet {server.WorkerPath}/IvaScanner.Worker.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target";

            var encodedService = Convert.ToBase64String(Encoding.UTF8.GetBytes(serviceContent));
            return $"echo '{encodedService}' | base64 -d | sudo tee /etc/systemd/system/iva-worker.service && sudo systemctl daemon-reload";
        }

        // Additional required method implementations
        public async Task<RemoteServer?> GetServerAsync(string serverId)
        {
            return await _context.RemoteServers
                .Include(s => s.Workers)
                .Include(s => s.HealthChecks)
                .FirstOrDefaultAsync(s => s.Id == serverId);
        }

        public async Task<List<RemoteServer>> GetServersAsync()
        {
            return await _context.RemoteServers
                .Include(s => s.Workers)
                .OrderBy(s => s.Name)
                .ToListAsync();
        }

        // Placeholder implementations for other interface methods
        public Task<RemoteServer?> UpdateServerAsync(string serverId, UpdateRemoteServerRequest request) => throw new NotImplementedException();
        public Task<bool> DeleteServerAsync(string serverId) => throw new NotImplementedException();
        public Task<ServerHealthCheck> CheckServerHealthAsync(string serverId) => throw new NotImplementedException();
        public Task<List<ServerHealthCheck>> GetServerHealthHistoryAsync(string serverId, int limit = 50) => throw new NotImplementedException();
        public Task<DeploymentProgress> GetDeploymentProgressAsync(string jobId) => throw new NotImplementedException();
        public Task<List<DeploymentJob>> GetDeploymentHistoryAsync(string serverId, int limit = 20) => throw new NotImplementedException();
        public Task<bool> CancelDeploymentAsync(string jobId) => throw new NotImplementedException();
        public Task<bool> StartWorkersAsync(string serverId, List<string>? workerIds = null) => throw new NotImplementedException();
        public Task<bool> StopWorkersAsync(string serverId, List<string>? workerIds = null) => throw new NotImplementedException();
        public Task<bool> RestartWorkersAsync(string serverId, List<string>? workerIds = null) => throw new NotImplementedException();
        public Task<bool> RemoveWorkersAsync(string serverId, List<string> workerIds) => throw new NotImplementedException();
        public Task<RemoteCommandResult> ExecuteCommandAsync(string serverId, string command, int timeoutSeconds = 30) => throw new NotImplementedException();
        public Task<RemoteCommandResult> ExecuteScriptAsync(string serverId, string script, int timeoutSeconds = 300) => throw new NotImplementedException();
        public Task<string> PrepareSystemAsync(string serverId) => throw new NotImplementedException();
        public Task<bool> InstallDotNetAsync(string serverId) => throw new NotImplementedException();
        public Task<bool> InstallSystemdServiceAsync(string serverId, string workerId) => throw new NotImplementedException();
        public Task<bool> UploadWorkerFilesAsync(string serverId, string targetPath) => throw new NotImplementedException();
        public Task<bool> DownloadLogsAsync(string serverId, string localPath) => throw new NotImplementedException();
        public Task<string> GetWorkerConfigAsync(string serverId, string workerId) => throw new NotImplementedException();
        public Task<bool> UpdateWorkerConfigAsync(string serverId, string workerId, WorkerDeploymentConfig config) => throw new NotImplementedException();
        public Task<Dictionary<string, object>> GetServerStatisticsAsync(string serverId) => throw new NotImplementedException();
        public Task<List<string>> GetWorkerLogsAsync(string serverId, string workerId, int lines = 100) => throw new NotImplementedException();

        private async Task GetSystemInformation(SshClient client, ServerConnectionTest result, ServerOS os)
        {
            // Get basic system info
            var commands = new Dictionary<string, string>
            {
                ["hostname"] = "hostname",
                ["uptime"] = "uptime",
                ["memory"] = "free -h",
                ["disk"] = "df -h",
                ["cpu"] = "nproc"
            };

            foreach (var cmd in commands)
            {
                var cmdResult = await ExecuteCommandViaSshAsync(client, cmd.Value);
                if (cmdResult.Success)
                {
                    result.SystemInfo[cmd.Key] = cmdResult.Output.Trim();
                }
            }
        }

        private string GenerateWindowsDownloadCommand(RemoteServer server)
        {
            var masterUrl = _configuration["Master:PublicUrl"] ?? "http://localhost:5000";
            return $"Invoke-WebRequest -Uri '{masterUrl}/api/deployment/worker-package-windows' -OutFile 'C:\\temp\\iva-worker.zip'";
        }

        private string GenerateWindowsConfigCommand(RemoteServer server, Dictionary<string, string> config)
        {
            var masterUrl = _configuration["Master:PublicUrl"] ?? "http://localhost:5000";
            var apiKey = _configuration["Worker:DefaultApiKey"] ?? Guid.NewGuid().ToString();

            var workerConfig = new
            {
                Master = new
                {
                    BaseUrl = masterUrl,
                    ApiKey = apiKey
                },
                Worker = new
                {
                    Name = $"Worker-{server.Name}",
                    MaxConcurrentTasks = 2,
                    HeartbeatInterval = "00:00:30",
                    WorkingDirectory = $"{server.WorkerPath}\\temp"
                }
            };

            var configJson = JsonSerializer.Serialize(workerConfig, new JsonSerializerOptions { WriteIndented = true });
            return $"@'\n{configJson}\n'@ | Out-File -FilePath '{server.WorkerPath}\\appsettings.json' -Encoding UTF8";
        }
    }
}