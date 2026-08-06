using IvaScanner.Core.Models;
using IvaScanner.Master.Data;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace IvaScanner.Master.Services
{
    public class ProxyService : IProxyService
    {
        private readonly MasterDbContext _context;
        private readonly ISystemLogService _systemLog;
        private readonly ILogger<ProxyService> _logger;
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, int> _workerProxyRotation;
        private readonly Random _random;

        public ProxyService(
            MasterDbContext context,
            ISystemLogService systemLog,
            ILogger<ProxyService> logger,
            HttpClient httpClient)
        {
            _context = context;
            _systemLog = systemLog;
            _logger = logger;
            _httpClient = httpClient;
            _workerProxyRotation = new Dictionary<string, int>();
            _random = new Random();
        }

        // Proxy CRUD operations
        public async Task<ProxyServer> CreateProxyAsync(CreateProxyRequest request)
        {
            _logger.LogInformation("Creating new proxy {Host}:{Port}", request.Host, request.Port);

            // Check if proxy already exists
            var existingProxy = await _context.ProxyServers
                .FirstOrDefaultAsync(p => p.Host == request.Host && p.Port == request.Port);

            if (existingProxy != null)
            {
                throw new InvalidOperationException($"Proxy {request.Host}:{request.Port} already exists");
            }

            var proxy = new ProxyServer
            {
                Host = request.Host.Trim(),
                Port = request.Port,
                Type = request.Type,
                Username = request.Username?.Trim(),
                Password = request.Password,
                Country = request.Country?.Trim(),
                City = request.City?.Trim(),
                Provider = request.Provider?.Trim(),
                Priority = Math.Max(1, Math.Min(10, request.Priority)),
                Status = ProxyStatus.Unknown,
                IsActive = true
            };

            _context.ProxyServers.Add(proxy);
            await _context.SaveChangesAsync();

            await _systemLog.LogInfoAsync($"Created new proxy {proxy.DisplayName}", "proxy_management", "ProxyService");

            // Perform initial health check
            _ = Task.Run(async () =>
            {
                try
                {
                    await PerformHealthCheckAsync(proxy.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed initial health check for proxy {ProxyId}", proxy.Id);
                }
            });

            return proxy;
        }

        public async Task<ProxyServer?> GetProxyAsync(string proxyId)
        {
            return await _context.ProxyServers
                .Include(p => p.HealthChecks.OrderByDescending(h => h.CheckedAt).Take(5))
                .Include(p => p.UsageLogs.OrderByDescending(u => u.UsedAt).Take(10))
                .FirstOrDefaultAsync(p => p.Id == proxyId);
        }

        public async Task<List<ProxyServer>> GetProxiesAsync(int skip = 0, int take = 100, ProxyStatus? status = null)
        {
            var query = _context.ProxyServers.AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(p => p.Status == status.Value);
            }

            return await query
                .OrderBy(p => p.Priority)
                .ThenByDescending(p => p.SuccessRate)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<ProxyServer?> UpdateProxyAsync(string proxyId, UpdateProxyRequest request)
        {
            var proxy = await _context.ProxyServers.FindAsync(proxyId);
            if (proxy == null) return null;

            if (!string.IsNullOrEmpty(request.Host))
                proxy.Host = request.Host.Trim();

            if (request.Port.HasValue)
                proxy.Port = request.Port.Value;

            if (request.Type.HasValue)
                proxy.Type = request.Type.Value;

            if (request.Username != null)
                proxy.Username = request.Username.Trim();

            if (request.Password != null)
                proxy.Password = request.Password;

            if (request.Country != null)
                proxy.Country = request.Country.Trim();

            if (request.City != null)
                proxy.City = request.City.Trim();

            if (request.Provider != null)
                proxy.Provider = request.Provider.Trim();

            if (request.Priority.HasValue)
                proxy.Priority = Math.Max(1, Math.Min(10, request.Priority.Value));

            if (request.IsActive.HasValue)
                proxy.IsActive = request.IsActive.Value;

            proxy.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _systemLog.LogInfoAsync($"Updated proxy {proxy.DisplayName}", "proxy_management", "ProxyService");

            return proxy;
        }

        public async Task<bool> DeleteProxyAsync(string proxyId)
        {
            var proxy = await _context.ProxyServers.FindAsync(proxyId);
            if (proxy == null) return false;

            // Remove from all pools first
            var poolMembers = await _context.ProxyPoolMembers
                .Where(m => m.ProxyId == proxyId)
                .ToListAsync();

            _context.ProxyPoolMembers.RemoveRange(poolMembers);

            // Remove the proxy (cascade will handle usage logs and health checks)
            _context.ProxyServers.Remove(proxy);

            await _context.SaveChangesAsync();

            await _systemLog.LogInfoAsync($"Deleted proxy {proxy.DisplayName}", "proxy_management", "ProxyService");

            return true;
        }

        public async Task<List<ProxyServer>> GetProxiesByStatusAsync(ProxyStatus status)
        {
            return await _context.ProxyServers
                .Where(p => p.Status == status && p.IsActive)
                .OrderBy(p => p.Priority)
                .ThenByDescending(p => p.SuccessRate)
                .ToListAsync();
        }

        public async Task<List<ProxyServer>> GetProxiesByCountryAsync(string country)
        {
            return await _context.ProxyServers
                .Where(p => p.Country == country && p.IsActive && p.Status == ProxyStatus.Working)
                .OrderBy(p => p.Priority)
                .ThenByDescending(p => p.SuccessRate)
                .ToListAsync();
        }

        // Proxy testing and health checks
        public async Task<ProxyTestResult> TestProxyAsync(string proxyId, string? testUrl = null)
        {
            var proxy = await _context.ProxyServers.FindAsync(proxyId);
            if (proxy == null)
            {
                return new ProxyTestResult
                {
                    IsSuccessful = false,
                    ErrorMessage = "Proxy not found"
                };
            }

            testUrl ??= "https://httpbin.org/ip";
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var proxyAddress = $"http://{proxy.Host}:{proxy.Port}";
                var webProxy = new WebProxy(proxyAddress);

                if (!string.IsNullOrEmpty(proxy.Username) && !string.IsNullOrEmpty(proxy.Password))
                {
                    webProxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);
                }

                using var handler = new HttpClientHandler()
                {
                    Proxy = webProxy,
                    UseProxy = true
                };

                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var response = await client.GetAsync(testUrl);
                stopwatch.Stop();

                var responseData = await response.Content.ReadAsStringAsync();

                return new ProxyTestResult
                {
                    IsSuccessful = response.IsSuccessStatusCode,
                    ResponseTime = stopwatch.Elapsed.TotalMilliseconds,
                    HttpStatusCode = (int)response.StatusCode,
                    ResponseData = responseData.Length > 1000 ? responseData[..1000] + "..." : responseData
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                return new ProxyTestResult
                {
                    IsSuccessful = false,
                    ResponseTime = stopwatch.Elapsed.TotalMilliseconds,
                    ErrorMessage = ex.Message,
                    HttpStatusCode = 0
                };
            }
        }

        public async Task<List<ProxyTestResult>> TestMultipleProxiesAsync(List<string> proxyIds, string? testUrl = null)
        {
            var tasks = proxyIds.Select(id => TestProxyAsync(id, testUrl));
            return (await Task.WhenAll(tasks)).ToList();
        }

        public async Task<bool> PerformHealthCheckAsync(string proxyId)
        {
            var testResult = await TestProxyAsync(proxyId);
            
            var healthCheck = new ProxyHealthCheck
            {
                ProxyId = proxyId,
                IsHealthy = testResult.IsSuccessful,
                ResponseTime = testResult.ResponseTime,
                ErrorMessage = testResult.ErrorMessage,
                HttpStatusCode = testResult.HttpStatusCode,
                ResponseData = testResult.ResponseData
            };

            _context.ProxyHealthChecks.Add(healthCheck);

            // Update proxy status and statistics
            var proxy = await _context.ProxyServers.FindAsync(proxyId);
            if (proxy != null)
            {
                proxy.LastChecked = DateTime.UtcNow;
                proxy.ResponseTime = testResult.ResponseTime;

                if (testResult.IsSuccessful)
                {
                    proxy.Status = testResult.ResponseTime > 5000 ? ProxyStatus.Slow : ProxyStatus.Working;
                    proxy.SuccessCount++;
                    proxy.LastError = null;
                }
                else
                {
                    proxy.FailureCount++;
                    proxy.LastError = testResult.ErrorMessage;
                    
                    // Determine status based on error
                    proxy.Status = testResult.ErrorMessage?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true
                        ? ProxyStatus.Timeout
                        : ProxyStatus.Failed;
                }
            }

            await _context.SaveChangesAsync();

            return testResult.IsSuccessful;
        }

        public async Task PerformHealthChecksAsync(List<string> proxyIds)
        {
            var tasks = proxyIds.Select(PerformHealthCheckAsync);
            await Task.WhenAll(tasks);

            await _systemLog.LogInfoAsync($"Completed health checks for {proxyIds.Count} proxies", "proxy_health", "ProxyService");
        }

        public async Task PerformAllHealthChecksAsync()
        {
            var activeProxies = await _context.ProxyServers
                .Where(p => p.IsActive)
                .Select(p => p.Id)
                .ToListAsync();

            await PerformHealthChecksAsync(activeProxies);
        }

        // Proxy selection and rotation
        public async Task<ProxyServer?> GetNextProxyForWorkerAsync(string workerId, string? poolId = null)
        {
            if (!string.IsNullOrEmpty(poolId))
            {
                return await GetNextProxyFromPoolAsync(poolId, workerId);
            }

            // Get working proxies ordered by priority and success rate
            var availableProxies = await _context.ProxyServers
                .Where(p => p.IsActive && p.Status == ProxyStatus.Working)
                .OrderBy(p => p.Priority)
                .ThenByDescending(p => p.SuccessRate)
                .ThenBy(p => p.LastUsed ?? DateTime.MinValue)
                .ToListAsync();

            if (!availableProxies.Any())
                return null;

            // Simple round-robin per worker
            if (!_workerProxyRotation.ContainsKey(workerId))
            {
                _workerProxyRotation[workerId] = 0;
            }

            var index = _workerProxyRotation[workerId] % availableProxies.Count;
            _workerProxyRotation[workerId] = index + 1;

            var selectedProxy = availableProxies[index];
            selectedProxy.LastUsed = DateTime.UtcNow;
            
            await _context.SaveChangesAsync();

            return selectedProxy;
        }

        private async Task<ProxyServer?> GetNextProxyFromPoolAsync(string poolId, string workerId)
        {
            var pool = await _context.ProxyPools
                .Include(p => p.Members)
                .ThenInclude(m => m.Proxy)
                .FirstOrDefaultAsync(p => p.Id == poolId && p.IsActive);

            if (pool == null) return null;

            var availableProxies = pool.Members
                .Where(m => m.IsEnabled && m.Proxy.IsActive && m.Proxy.Status == ProxyStatus.Working)
                .Select(m => m.Proxy)
                .ToList();

            if (!availableProxies.Any()) return null;

            ProxyServer? selectedProxy = null;

            switch (pool.Strategy)
            {
                case ProxyPoolStrategy.RoundRobin:
                    if (!_workerProxyRotation.ContainsKey($"{poolId}_{workerId}"))
                        _workerProxyRotation[$"{poolId}_{workerId}"] = 0;
                    
                    var index = _workerProxyRotation[$"{poolId}_{workerId}"] % availableProxies.Count;
                    _workerProxyRotation[$"{poolId}_{workerId}"] = index + 1;
                    selectedProxy = availableProxies[index];
                    break;

                case ProxyPoolStrategy.Random:
                    selectedProxy = availableProxies[_random.Next(availableProxies.Count)];
                    break;

                case ProxyPoolStrategy.LeastUsed:
                    selectedProxy = availableProxies.OrderBy(p => p.LastUsed ?? DateTime.MinValue).First();
                    break;

                case ProxyPoolStrategy.FastestResponse:
                    selectedProxy = availableProxies.OrderBy(p => p.ResponseTime).First();
                    break;

                case ProxyPoolStrategy.HighestSuccess:
                    selectedProxy = availableProxies.OrderByDescending(p => p.SuccessRate).First();
                    break;
            }

            if (selectedProxy != null)
            {
                selectedProxy.LastUsed = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return selectedProxy;
        }

        public async Task<List<ProxyServer>> GetAvailableProxiesAsync(int count, string? country = null)
        {
            var query = _context.ProxyServers
                .Where(p => p.IsActive && p.Status == ProxyStatus.Working);

            if (!string.IsNullOrEmpty(country))
            {
                query = query.Where(p => p.Country == country);
            }

            return await query
                .OrderBy(p => p.Priority)
                .ThenByDescending(p => p.SuccessRate)
                .Take(count)
                .ToListAsync();
        }

        public async Task<ProxyServer?> GetFastestProxyAsync(string? country = null)
        {
            var query = _context.ProxyServers
                .Where(p => p.IsActive && p.Status == ProxyStatus.Working);

            if (!string.IsNullOrEmpty(country))
            {
                query = query.Where(p => p.Country == country);
            }

            return await query
                .OrderBy(p => p.ResponseTime)
                .FirstOrDefaultAsync();
        }

        public async Task<ProxyServer?> GetLeastUsedProxyAsync()
        {
            return await _context.ProxyServers
                .Where(p => p.IsActive && p.Status == ProxyStatus.Working)
                .OrderBy(p => p.LastUsed ?? DateTime.MinValue)
                .FirstOrDefaultAsync();
        }

        public async Task RotateProxiesAsync()
        {
            _workerProxyRotation.Clear();
            await _systemLog.LogInfoAsync("Rotated proxy assignments for all workers", "proxy_rotation", "ProxyService");
        }

        // Usage tracking
        public async Task LogProxyUsageAsync(string proxyId, string? workerId, string? jobId, string? taskId,
            TimeSpan duration, bool success, string? errorMessage = null, int httpStatusCode = 0,
            double responseTime = 0, string? targetUrl = null)
        {
            var usageLog = new ProxyUsageLog
            {
                ProxyId = proxyId,
                WorkerId = workerId,
                JobId = jobId,
                TaskId = taskId,
                Duration = duration,
                Success = success,
                ErrorMessage = errorMessage,
                HttpStatusCode = httpStatusCode,
                ResponseTime = responseTime,
                TargetUrl = targetUrl
            };

            _context.ProxyUsageLogs.Add(usageLog);

            // Update proxy statistics
            var proxy = await _context.ProxyServers.FindAsync(proxyId);
            if (proxy != null)
            {
                if (success)
                {
                    proxy.SuccessCount++;
                }
                else
                {
                    proxy.FailureCount++;
                    proxy.LastError = errorMessage;
                }

                proxy.LastUsed = DateTime.UtcNow;
                proxy.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        public async Task<List<ProxyUsageLog>> GetProxyUsageHistoryAsync(string proxyId, int days = 7)
        {
            var fromDate = DateTime.UtcNow.AddDays(-days);
            return await _context.ProxyUsageLogs
                .Where(l => l.ProxyId == proxyId && l.UsedAt >= fromDate)
                .OrderByDescending(l => l.UsedAt)
                .ToListAsync();
        }

        public async Task<Dictionary<string, int>> GetProxyUsageStatsAsync(DateTime? fromDate = null, DateTime? toDate = null)
        {
            fromDate ??= DateTime.UtcNow.AddDays(-7);
            toDate ??= DateTime.UtcNow;

            var usageLogs = await _context.ProxyUsageLogs
                .Where(l => l.UsedAt >= fromDate && l.UsedAt <= toDate)
                .Join(_context.ProxyServers,
                      log => log.ProxyId,
                      proxy => proxy.Id,
                      (log, proxy) => new { log.Success, proxy.Country })
                .ToListAsync();

            return usageLogs
                .GroupBy(x => x.Country ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());
        }

        // Proxy pools (simplified for now)
        public async Task<ProxyPool> CreateProxyPoolAsync(CreateProxyPoolRequest request)
        {
            var pool = new ProxyPool
            {
                Name = request.Name.Trim(),
                Description = request.Description.Trim(),
                Strategy = request.Strategy,
                MaxProxiesPerWorker = request.MaxProxiesPerWorker,
                HealthCheckInterval = TimeSpan.FromMinutes(request.HealthCheckIntervalMinutes),
                RotationInterval = TimeSpan.FromHours(request.RotationIntervalHours),
                MinSuccessRate = request.MinSuccessRate,
                MaxFailures = request.MaxFailures
            };

            _context.ProxyPools.Add(pool);
            await _context.SaveChangesAsync();

            // Add proxy members
            foreach (var proxyId in request.ProxyIds)
            {
                await AddProxyToPoolAsync(pool.Id, proxyId);
            }

            await _systemLog.LogInfoAsync($"Created proxy pool '{pool.Name}' with {request.ProxyIds.Count} proxies", 
                "proxy_pool", "ProxyService");

            return pool;
        }

        public async Task<ProxyPool?> GetProxyPoolAsync(string poolId)
        {
            return await _context.ProxyPools
                .Include(p => p.Members)
                .ThenInclude(m => m.Proxy)
                .FirstOrDefaultAsync(p => p.Id == poolId);
        }

        public async Task<List<ProxyPool>> GetProxyPoolsAsync()
        {
            return await _context.ProxyPools
                .Include(p => p.Members)
                .OrderBy(p => p.Name)
                .ToListAsync();
        }

        public async Task<bool> DeleteProxyPoolAsync(string poolId)
        {
            var pool = await _context.ProxyPools.FindAsync(poolId);
            if (pool == null) return false;

            _context.ProxyPools.Remove(pool);
            await _context.SaveChangesAsync();

            await _systemLog.LogInfoAsync($"Deleted proxy pool '{pool.Name}'", "proxy_pool", "ProxyService");
            return true;
        }

        public async Task<bool> AddProxyToPoolAsync(string poolId, string proxyId, int weight = 1)
        {
            // Check if already exists
            var existingMember = await _context.ProxyPoolMembers
                .FirstOrDefaultAsync(m => m.ProxyPoolId == poolId && m.ProxyId == proxyId);

            if (existingMember != null) return false;

            var member = new ProxyPoolMember
            {
                ProxyPoolId = poolId,
                ProxyId = proxyId,
                Weight = weight
            };

            _context.ProxyPoolMembers.Add(member);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<bool> RemoveProxyFromPoolAsync(string poolId, string proxyId)
        {
            var member = await _context.ProxyPoolMembers
                .FirstOrDefaultAsync(m => m.ProxyPoolId == poolId && m.ProxyId == proxyId);

            if (member == null) return false;

            _context.ProxyPoolMembers.Remove(member);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<List<ProxyServer>> GetPoolProxiesAsync(string poolId, bool activeOnly = true)
        {
            var query = _context.ProxyPoolMembers
                .Where(m => m.ProxyPoolId == poolId)
                .Select(m => m.Proxy);

            if (activeOnly)
            {
                query = query.Where(p => p.IsActive && p.Status == ProxyStatus.Working);
            }

            return await query.ToListAsync();
        }

        // Statistics and monitoring
        public async Task<ProxyStats> GetProxyStatsAsync()
        {
            var proxies = await _context.ProxyServers.ToListAsync();

            var stats = new ProxyStats
            {
                TotalProxies = proxies.Count,
                ActiveProxies = proxies.Count(p => p.IsActive),
                WorkingProxies = proxies.Count(p => p.Status == ProxyStatus.Working),
                FailedProxies = proxies.Count(p => p.Status == ProxyStatus.Failed),
                AverageResponseTime = proxies.Where(p => p.ResponseTime > 0).DefaultIfEmpty()
                    .Average(p => p?.ResponseTime ?? 0),
                AverageSuccessRate = proxies.DefaultIfEmpty().Average(p => p?.SuccessRate ?? 0)
            };

            stats.ProxiesByCountry = proxies
                .GroupBy(p => p.Country ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());

            stats.ProxiesByStatus = proxies
                .GroupBy(p => p.Status)
                .ToDictionary(g => g.Key, g => g.Count());

            return stats;
        }

        public async Task<Dictionary<string, object>> GetProxyMetricsAsync(string proxyId)
        {
            var proxy = await GetProxyAsync(proxyId);
            if (proxy == null) return new Dictionary<string, object>();

            var recentUsage = await GetProxyUsageHistoryAsync(proxyId, 1);
            var hourlyUsage = recentUsage.Count;

            return new Dictionary<string, object>
            {
                ["success_rate"] = proxy.SuccessRate,
                ["total_uses"] = proxy.SuccessCount + proxy.FailureCount,
                ["hourly_usage"] = hourlyUsage,
                ["avg_response_time"] = proxy.ResponseTime,
                ["last_check"] = proxy.LastChecked,
                ["last_used"] = proxy.LastUsed,
                ["status"] = proxy.Status.ToString(),
                ["failure_count"] = proxy.FailureCount
            };
        }

        public async Task<List<ProxyHealthCheck>> GetRecentHealthChecksAsync(string proxyId, int count = 10)
        {
            return await _context.ProxyHealthChecks
                .Where(h => h.ProxyId == proxyId)
                .OrderByDescending(h => h.CheckedAt)
                .Take(count)
                .ToListAsync();
        }

        public async Task<Dictionary<ProxyStatus, List<ProxyServer>>> GetProxiesGroupedByStatusAsync()
        {
            var proxies = await _context.ProxyServers
                .Where(p => p.IsActive)
                .ToListAsync();

            return proxies
                .GroupBy(p => p.Status)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        // Bulk operations
        public async Task<List<ProxyServer>> ImportProxiesAsync(List<CreateProxyRequest> proxies)
        {
            var importedProxies = new List<ProxyServer>();

            foreach (var request in proxies)
            {
                try
                {
                    var proxy = await CreateProxyAsync(request);
                    importedProxies.Add(proxy);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to import proxy {Host}:{Port}", request.Host, request.Port);
                }
            }

            await _systemLog.LogInfoAsync($"Imported {importedProxies.Count} out of {proxies.Count} proxies", 
                "proxy_import", "ProxyService");

            return importedProxies;
        }

        public async Task<int> BulkUpdateProxyStatusAsync(List<string> proxyIds, ProxyStatus status)
        {
            var proxies = await _context.ProxyServers
                .Where(p => proxyIds.Contains(p.Id))
                .ToListAsync();

            foreach (var proxy in proxies)
            {
                proxy.Status = status;
                proxy.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            await _systemLog.LogInfoAsync($"Bulk updated {proxies.Count} proxies to status {status}", 
                "proxy_bulk_update", "ProxyService");

            return proxies.Count;
        }

        public async Task<int> BulkDeleteInactiveProxiesAsync(TimeSpan inactiveFor)
        {
            var cutoffDate = DateTime.UtcNow - inactiveFor;
            
            var inactiveProxies = await _context.ProxyServers
                .Where(p => (p.LastUsed ?? p.CreatedAt) < cutoffDate && 
                           p.Status == ProxyStatus.Failed)
                .ToListAsync();

            _context.ProxyServers.RemoveRange(inactiveProxies);
            await _context.SaveChangesAsync();

            await _systemLog.LogInfoAsync($"Bulk deleted {inactiveProxies.Count} inactive proxies", 
                "proxy_cleanup", "ProxyService");

            return inactiveProxies.Count;
        }

        public async Task<List<ProxyServer>> BulkTestProxiesAsync(List<string> proxyIds)
        {
            var results = await TestMultipleProxiesAsync(proxyIds);
            var workingProxies = new List<ProxyServer>();

            for (int i = 0; i < proxyIds.Count; i++)
            {
                if (results[i].IsSuccessful)
                {
                    var proxy = await _context.ProxyServers.FindAsync(proxyIds[i]);
                    if (proxy != null)
                    {
                        workingProxies.Add(proxy);
                    }
                }
            }

            return workingProxies;
        }

        // Maintenance operations
        public async Task CleanupOldUsageLogsAsync(TimeSpan olderThan)
        {
            var cutoffDate = DateTime.UtcNow - olderThan;
            
            var oldLogs = await _context.ProxyUsageLogs
                .Where(l => l.UsedAt < cutoffDate)
                .ToListAsync();

            _context.ProxyUsageLogs.RemoveRange(oldLogs);
            await _context.SaveChangesAsync();

            await _systemLog.LogInfoAsync($"Cleaned up {oldLogs.Count} old proxy usage logs", 
                "proxy_cleanup", "ProxyService");
        }

        public async Task CleanupOldHealthChecksAsync(TimeSpan olderThan)
        {
            var cutoffDate = DateTime.UtcNow - olderThan;
            
            var oldChecks = await _context.ProxyHealthChecks
                .Where(h => h.CheckedAt < cutoffDate)
                .ToListAsync();

            _context.ProxyHealthChecks.RemoveRange(oldChecks);
            await _context.SaveChangesAsync();

            await _systemLog.LogInfoAsync($"Cleaned up {oldChecks.Count} old proxy health checks", 
                "proxy_cleanup", "ProxyService");
        }

        public async Task UpdateProxyGeoLocationAsync(string proxyId)
        {
            // This would integrate with a geolocation service
            // For now, just a placeholder
            await Task.CompletedTask;
        }

        public async Task OptimizeProxyPoolsAsync()
        {
            // This would implement pool optimization logic
            // For now, just a placeholder
            await Task.CompletedTask;
        }
    }
}