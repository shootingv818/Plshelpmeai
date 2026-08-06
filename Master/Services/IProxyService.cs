using IvaScanner.Core.Models;

namespace IvaScanner.Master.Services
{
    public interface IProxyService
    {
        // Proxy CRUD operations
        Task<ProxyServer> CreateProxyAsync(CreateProxyRequest request);
        Task<ProxyServer?> GetProxyAsync(string proxyId);
        Task<List<ProxyServer>> GetProxiesAsync(int skip = 0, int take = 100, ProxyStatus? status = null);
        Task<ProxyServer?> UpdateProxyAsync(string proxyId, UpdateProxyRequest request);
        Task<bool> DeleteProxyAsync(string proxyId);
        Task<List<ProxyServer>> GetProxiesByStatusAsync(ProxyStatus status);
        Task<List<ProxyServer>> GetProxiesByCountryAsync(string country);
        
        // Proxy testing and health checks
        Task<ProxyTestResult> TestProxyAsync(string proxyId, string? testUrl = null);
        Task<List<ProxyTestResult>> TestMultipleProxiesAsync(List<string> proxyIds, string? testUrl = null);
        Task<bool> PerformHealthCheckAsync(string proxyId);
        Task PerformHealthChecksAsync(List<string> proxyIds);
        Task PerformAllHealthChecksAsync();
        
        // Proxy selection and rotation
        Task<ProxyServer?> GetNextProxyForWorkerAsync(string workerId, string? poolId = null);
        Task<List<ProxyServer>> GetAvailableProxiesAsync(int count, string? country = null);
        Task<ProxyServer?> GetFastestProxyAsync(string? country = null);
        Task<ProxyServer?> GetLeastUsedProxyAsync();
        Task RotateProxiesAsync();
        
        // Usage tracking
        Task LogProxyUsageAsync(string proxyId, string? workerId, string? jobId, string? taskId, 
            TimeSpan duration, bool success, string? errorMessage = null, int httpStatusCode = 0, 
            double responseTime = 0, string? targetUrl = null);
        Task<List<ProxyUsageLog>> GetProxyUsageHistoryAsync(string proxyId, int days = 7);
        Task<Dictionary<string, int>> GetProxyUsageStatsAsync(DateTime? fromDate = null, DateTime? toDate = null);
        
        // Proxy pools
        Task<ProxyPool> CreateProxyPoolAsync(CreateProxyPoolRequest request);
        Task<ProxyPool?> GetProxyPoolAsync(string poolId);
        Task<List<ProxyPool>> GetProxyPoolsAsync();
        Task<bool> DeleteProxyPoolAsync(string poolId);
        Task<bool> AddProxyToPoolAsync(string poolId, string proxyId, int weight = 1);
        Task<bool> RemoveProxyFromPoolAsync(string poolId, string proxyId);
        Task<List<ProxyServer>> GetPoolProxiesAsync(string poolId, bool activeOnly = true);
        
        // Statistics and monitoring
        Task<ProxyStats> GetProxyStatsAsync();
        Task<Dictionary<string, object>> GetProxyMetricsAsync(string proxyId);
        Task<List<ProxyHealthCheck>> GetRecentHealthChecksAsync(string proxyId, int count = 10);
        Task<Dictionary<ProxyStatus, List<ProxyServer>>> GetProxiesGroupedByStatusAsync();
        
        // Bulk operations
        Task<List<ProxyServer>> ImportProxiesAsync(List<CreateProxyRequest> proxies);
        Task<int> BulkUpdateProxyStatusAsync(List<string> proxyIds, ProxyStatus status);
        Task<int> BulkDeleteInactiveProxiesAsync(TimeSpan inactiveFor);
        Task<List<ProxyServer>> BulkTestProxiesAsync(List<string> proxyIds);
        
        // Maintenance operations
        Task CleanupOldUsageLogsAsync(TimeSpan olderThan);
        Task CleanupOldHealthChecksAsync(TimeSpan olderThan);
        Task UpdateProxyGeoLocationAsync(string proxyId);
        Task OptimizeProxyPoolsAsync();
    }
}