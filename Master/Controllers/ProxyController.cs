using IvaScanner.Core.Models;
using IvaScanner.Master.Services;
using Microsoft.AspNetCore.Mvc;

namespace IvaScanner.Master.Controllers
{
    public class ProxyController : Controller
    {
        private readonly IProxyService _proxyService;
        private readonly ISystemLogService _systemLog;
        private readonly ILogger<ProxyController> _logger;

        public ProxyController(
            IProxyService proxyService,
            ISystemLogService systemLog,
            ILogger<ProxyController> logger)
        {
            _proxyService = proxyService;
            _systemLog = systemLog;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var stats = await _proxyService.GetProxyStatsAsync();
                ViewBag.ProxyStats = stats;
                
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading proxy management page");
                ViewBag.ErrorMessage = "خطا در بارگذاری صفحه مدیریت پروکسی‌ها";
                return View();
            }
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateProxyRequest request)
        {
            if (!ModelState.IsValid)
            {
                return View(request);
            }

            try
            {
                var proxy = await _proxyService.CreateProxyAsync(request);
                TempData["SuccessMessage"] = $"پروکسی {proxy.DisplayName} با موفقیت ایجاد شد";
                return RedirectToAction(nameof(Details), new { id = proxy.Id });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating proxy {Host}:{Port}", request.Host, request.Port);
                ModelState.AddModelError("", "خطا در ایجاد پروکسی");
                return View(request);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            try
            {
                var proxy = await _proxyService.GetProxyAsync(id);
                if (proxy == null)
                {
                    return NotFound();
                }

                // Get additional metrics
                var metrics = await _proxyService.GetProxyMetricsAsync(id);
                ViewBag.ProxyMetrics = metrics;

                return View(proxy);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading proxy details for {ProxyId}", id);
                ViewBag.ErrorMessage = "خطا در بارگذاری جزئیات پروکسی";
                return View();
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            try
            {
                var proxy = await _proxyService.GetProxyAsync(id);
                if (proxy == null)
                {
                    return NotFound();
                }

                var updateRequest = new UpdateProxyRequest
                {
                    Host = proxy.Host,
                    Port = proxy.Port,
                    Type = proxy.Type,
                    Username = proxy.Username,
                    Password = proxy.Password,
                    Country = proxy.Country,
                    City = proxy.City,
                    Provider = proxy.Provider,
                    Priority = proxy.Priority,
                    IsActive = proxy.IsActive
                };

                ViewBag.ProxyId = id;
                return View(updateRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading proxy edit form for {ProxyId}", id);
                TempData["ErrorMessage"] = "خطا در بارگذاری فرم ویرایش پروکسی";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        public async Task<IActionResult> Edit(string id, UpdateProxyRequest request)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.ProxyId = id;
                return View(request);
            }

            try
            {
                var proxy = await _proxyService.UpdateProxyAsync(id, request);
                if (proxy == null)
                {
                    return NotFound();
                }

                TempData["SuccessMessage"] = $"پروکسی {proxy.DisplayName} با موفقیت بروزرسانی شد";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating proxy {ProxyId}", id);
                ModelState.AddModelError("", "خطا در بروزرسانی پروکسی");
                ViewBag.ProxyId = id;
                return View(request);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                var success = await _proxyService.DeleteProxyAsync(id);
                if (success)
                {
                    TempData["SuccessMessage"] = "پروکسی با موفقیت حذف شد";
                }
                else
                {
                    TempData["ErrorMessage"] = "پروکسی یافت نشد";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting proxy {ProxyId}", id);
                TempData["ErrorMessage"] = "خطا در حذف پروکسی";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<JsonResult> TestProxy(string id)
        {
            try
            {
                var result = await _proxyService.TestProxyAsync(id);
                
                await _systemLog.LogInfoAsync($"Manual proxy test: {id} - {(result.IsSuccessful ? "Success" : "Failed")}", 
                    "proxy_test_manual", "ProxyController");

                return Json(new
                {
                    success = true,
                    result = new
                    {
                        isSuccessful = result.IsSuccessful,
                        responseTime = result.ResponseTime,
                        httpStatusCode = result.HttpStatusCode,
                        errorMessage = result.ErrorMessage,
                        testedAt = result.TestedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing proxy {ProxyId}", id);
                return Json(new { success = false, error = "خطا در تست پروکسی" });
            }
        }

        // API endpoints for real-time updates
        [HttpGet]
        public async Task<JsonResult> GetProxiesJson(int skip = 0, int take = 50, string? status = null)
        {
            try
            {
                ProxyStatus? statusEnum = null;
                if (!string.IsNullOrEmpty(status) && Enum.TryParse<ProxyStatus>(status, out var parsedStatus))
                {
                    statusEnum = parsedStatus;
                }

                var proxies = await _proxyService.GetProxiesAsync(skip, take, statusEnum);
                return Json(proxies);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting proxies JSON");
                return Json(new { error = "خطا در دریافت اطلاعات پروکسی‌ها" });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetProxyStats()
        {
            try
            {
                var stats = await _proxyService.GetProxyStatsAsync();
                return Json(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting proxy statistics");
                return Json(new { error = "خطا در دریافت آمار پروکسی‌ها" });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetProxiesByStatus()
        {
            try
            {
                var groupedProxies = await _proxyService.GetProxiesGroupedByStatusAsync();
                var result = groupedProxies.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => kvp.Value.Select(p => new
                    {
                        id = p.Id,
                        displayName = p.DisplayName,
                        country = p.Country,
                        successRate = p.SuccessRate,
                        responseTime = p.ResponseTime,
                        lastChecked = p.LastChecked.ToString("yyyy-MM-dd HH:mm:ss")
                    }).ToList()
                );

                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting proxies grouped by status");
                return Json(new { error = "خطا در دریافت پروکسی‌های گروه‌بندی شده" });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetProxyMetrics(string id)
        {
            try
            {
                var metrics = await _proxyService.GetProxyMetricsAsync(id);
                return Json(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting proxy metrics for {ProxyId}", id);
                return Json(new { error = "خطا در دریافت معیارهای پروکسی" });
            }
        }

        [HttpPost]
        public async Task<JsonResult> BulkTestProxies([FromBody] List<string> proxyIds)
        {
            try
            {
                var workingProxies = await _proxyService.BulkTestProxiesAsync(proxyIds);
                
                await _systemLog.LogInfoAsync($"Bulk tested {proxyIds.Count} proxies, {workingProxies.Count} working", 
                    "proxy_bulk_test", "ProxyController");

                return Json(new
                {
                    success = true,
                    tested = proxyIds.Count,
                    working = workingProxies.Count,
                    proxies = workingProxies.Select(p => new
                    {
                        id = p.Id,
                        displayName = p.DisplayName,
                        status = p.Status.ToString()
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error bulk testing proxies");
                return Json(new { success = false, error = "خطا در تست گروهی پروکسی‌ها" });
            }
        }

        [HttpPost]
        public async Task<JsonResult> BulkUpdateStatus([FromBody] BulkStatusUpdateRequest request)
        {
            try
            {
                if (!Enum.TryParse<ProxyStatus>(request.Status, out var status))
                {
                    return Json(new { success = false, error = "وضعیت نامعتبر" });
                }

                var updatedCount = await _proxyService.BulkUpdateProxyStatusAsync(request.ProxyIds, status);
                
                await _systemLog.LogInfoAsync($"Bulk updated {updatedCount} proxies to status {status}", 
                    "proxy_bulk_update", "ProxyController");

                return Json(new
                {
                    success = true,
                    updated = updatedCount,
                    message = $"{updatedCount} پروکسی بروزرسانی شد"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error bulk updating proxy status");
                return Json(new { success = false, error = "خطا در بروزرسانی گروهی وضعیت" });
            }
        }

        [HttpPost]
        public async Task<JsonResult> ImportProxies([FromBody] List<CreateProxyRequest> proxies)
        {
            try
            {
                var importedProxies = await _proxyService.ImportProxiesAsync(proxies);
                
                await _systemLog.LogInfoAsync($"Imported {importedProxies.Count} out of {proxies.Count} proxies", 
                    "proxy_import", "ProxyController");

                return Json(new
                {
                    success = true,
                    imported = importedProxies.Count,
                    total = proxies.Count,
                    message = $"{importedProxies.Count} پروکسی از {proxies.Count} وارد شد"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing proxies");
                return Json(new { success = false, error = "خطا در وارد کردن پروکسی‌ها" });
            }
        }

        // Proxy Pools
        [HttpGet]
        public async Task<IActionResult> Pools()
        {
            try
            {
                var pools = await _proxyService.GetProxyPoolsAsync();
                return View(pools);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading proxy pools");
                ViewBag.ErrorMessage = "خطا در بارگذاری مجموعه‌های پروکسی";
                return View(new List<ProxyPool>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> CreatePool()
        {
            var availableProxies = await _proxyService.GetProxiesAsync(0, 1000);
            ViewBag.AvailableProxies = availableProxies.Where(p => p.IsActive).ToList();
            
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreatePool(CreateProxyPoolRequest request)
        {
            if (!ModelState.IsValid)
            {
                var availableProxies = await _proxyService.GetProxiesAsync(0, 1000);
                ViewBag.AvailableProxies = availableProxies.Where(p => p.IsActive).ToList();
                return View(request);
            }

            try
            {
                var pool = await _proxyService.CreateProxyPoolAsync(request);
                TempData["SuccessMessage"] = $"مجموعه پروکسی '{pool.Name}' با موفقیت ایجاد شد";
                return RedirectToAction(nameof(PoolDetails), new { id = pool.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating proxy pool {PoolName}", request.Name);
                ModelState.AddModelError("", "خطا در ایجاد مجموعه پروکسی");
                
                var availableProxies = await _proxyService.GetProxiesAsync(0, 1000);
                ViewBag.AvailableProxies = availableProxies.Where(p => p.IsActive).ToList();
                return View(request);
            }
        }

        [HttpGet]
        public async Task<IActionResult> PoolDetails(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            try
            {
                var pool = await _proxyService.GetProxyPoolAsync(id);
                if (pool == null)
                {
                    return NotFound();
                }

                return View(pool);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading proxy pool details for {PoolId}", id);
                ViewBag.ErrorMessage = "خطا در بارگذاری جزئیات مجموعه پروکسی";
                return View();
            }
        }
    }

    public class BulkStatusUpdateRequest
    {
        public List<string> ProxyIds { get; set; } = new();
        public string Status { get; set; } = "";
    }
}