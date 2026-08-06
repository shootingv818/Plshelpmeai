using Microsoft.AspNetCore.SignalR;
using IvaScanner.Master.Services;
using System.Collections.Concurrent;

namespace IvaScanner.Master.Hubs
{
    public class DashboardHub : Hub
    {
        private readonly ILogger<DashboardHub> _logger;
        private static readonly ConcurrentDictionary<string, string> _connections = new();

        public DashboardHub(ILogger<DashboardHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _connections[Context.ConnectionId] = Context.User?.Identity?.Name ?? "Anonymous";
            
            _logger.LogInformation("Dashboard client connected: {ConnectionId}", Context.ConnectionId);
            
            // Join general dashboard group
            await Groups.AddToGroupAsync(Context.ConnectionId, "Dashboard");
            
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _connections.TryRemove(Context.ConnectionId, out _);
            
            _logger.LogInformation("Dashboard client disconnected: {ConnectionId}", Context.ConnectionId);
            
            await base.OnDisconnectedAsync(exception);
        }

        // Client can subscribe to specific updates
        public async Task JoinWorkerUpdates()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "WorkerUpdates");
            _logger.LogDebug("Client {ConnectionId} joined WorkerUpdates", Context.ConnectionId);
        }

        public async Task LeaveWorkerUpdates()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "WorkerUpdates");
            _logger.LogDebug("Client {ConnectionId} left WorkerUpdates", Context.ConnectionId);
        }

        public async Task JoinJobUpdates(string jobId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Job-{jobId}");
            _logger.LogDebug("Client {ConnectionId} joined job updates for {JobId}", Context.ConnectionId, jobId);
        }

        public async Task LeaveJobUpdates(string jobId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Job-{jobId}");
            _logger.LogDebug("Client {ConnectionId} left job updates for {JobId}", Context.ConnectionId, jobId);
        }

        public async Task JoinSystemUpdates()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "SystemUpdates");
            _logger.LogDebug("Client {ConnectionId} joined SystemUpdates", Context.ConnectionId);
        }

        public async Task LeaveSystemUpdates()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "SystemUpdates");
            _logger.LogDebug("Client {ConnectionId} left SystemUpdates", Context.ConnectionId);
        }

        // Get current connection count
        public static int GetConnectionCount()
        {
            return _connections.Count;
        }

        // Get connected users
        public static IEnumerable<string> GetConnectedUsers()
        {
            return _connections.Values.Distinct();
        }
    }
}