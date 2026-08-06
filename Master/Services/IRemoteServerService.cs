using IvaScanner.Core.Models;

namespace IvaScanner.Master.Services
{
    public interface IRemoteServerService
    {
        // Server Management
        Task<RemoteServer> CreateServerAsync(CreateRemoteServerRequest request);
        Task<RemoteServer?> GetServerAsync(string serverId);
        Task<List<RemoteServer>> GetServersAsync();
        Task<RemoteServer?> UpdateServerAsync(string serverId, UpdateRemoteServerRequest request);
        Task<bool> DeleteServerAsync(string serverId);

        // Connection and Health
        Task<ServerConnectionTest> TestConnectionAsync(string serverId);
        Task<ServerHealthCheck> CheckServerHealthAsync(string serverId);
        Task<List<ServerHealthCheck>> GetServerHealthHistoryAsync(string serverId, int limit = 50);

        // Worker Deployment
        Task<string> DeployWorkersAsync(DeployWorkerRequest request);
        Task<DeploymentProgress> GetDeploymentProgressAsync(string jobId);
        Task<List<DeploymentJob>> GetDeploymentHistoryAsync(string serverId, int limit = 20);
        Task<bool> CancelDeploymentAsync(string jobId);

        // Worker Management
        Task<bool> StartWorkersAsync(string serverId, List<string>? workerIds = null);
        Task<bool> StopWorkersAsync(string serverId, List<string>? workerIds = null);
        Task<bool> RestartWorkersAsync(string serverId, List<string>? workerIds = null);
        Task<bool> RemoveWorkersAsync(string serverId, List<string> workerIds);

        // Remote Commands
        Task<RemoteCommandResult> ExecuteCommandAsync(string serverId, string command, int timeoutSeconds = 30);
        Task<RemoteCommandResult> ExecuteScriptAsync(string serverId, string script, int timeoutSeconds = 300);

        // System Preparation
        Task<string> PrepareSystemAsync(string serverId);
        Task<bool> InstallDotNetAsync(string serverId);
        Task<bool> InstallSystemdServiceAsync(string serverId, string workerId);

        // File Operations
        Task<bool> UploadWorkerFilesAsync(string serverId, string targetPath);
        Task<bool> DownloadLogsAsync(string serverId, string localPath);
        Task<string> GetWorkerConfigAsync(string serverId, string workerId);
        Task<bool> UpdateWorkerConfigAsync(string serverId, string workerId, WorkerDeploymentConfig config);

        // Monitoring
        Task<Dictionary<string, object>> GetServerStatisticsAsync(string serverId);
        Task<List<string>> GetWorkerLogsAsync(string serverId, string workerId, int lines = 100);
    }
}