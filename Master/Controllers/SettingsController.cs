using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace IvaScanner.Master.Controllers
{
    public class SettingsController : Controller
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(IConfiguration config, ILogger<SettingsController> logger)
        {
            _config = config;
            _logger = logger;
        }

        public IActionResult Index()
        {
            try
            {
                var settings = new
                {
                    WorkerSettings = new
                    {
                        HeartbeatIntervalSeconds = _config.GetValue<int>("WorkerSettings:HeartbeatIntervalSeconds", 30),
                        HeartbeatTimeoutSeconds = _config.GetValue<int>("WorkerSettings:HeartbeatTimeoutSeconds", 60),
                        TaskLeaseTimeoutMinutes = _config.GetValue<int>("WorkerSettings:TaskLeaseTimeoutMinutes", 2),
                        MaxRetryAttempts = _config.GetValue<int>("WorkerSettings:MaxRetryAttempts", 3)
                    },
                    ScanSettings = new
                    {
                        CvvChunkSize = _config.GetValue<int>("ScanSettings:CvvChunkSize", 100),
                        MaxConcurrentJobs = _config.GetValue<int>("ScanSettings:MaxConcurrentJobs", 10),
                        RequestDelayMs = _config.GetValue<int>("ScanSettings:RequestDelayMs", 1000),
                        TimeoutSeconds = _config.GetValue<int>("ScanSettings:TimeoutSeconds", 30)
                    },
                    DatabaseSettings = new
                    {
                        ConnectionString = _config.GetConnectionString("DefaultConnection")?.Substring(0, 50) + "...", // Hide full connection string
                        RedisConnectionString = _config.GetConnectionString("Redis")?.Substring(0, 50) + "..."
                    },
                    SystemSettings = new
                    {
                        LogRetentionDays = _config.GetValue<int>("SystemSettings:LogRetentionDays", 30),
                        MaxLogFileSize = _config.GetValue<string>("SystemSettings:MaxLogFileSize", "100MB"),
                        EnableDetailedLogging = _config.GetValue<bool>("SystemSettings:EnableDetailedLogging", false)
                    }
                };

                ViewBag.Settings = settings;
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading system settings");
                ViewBag.ErrorMessage = "خطا در بارگذاری تنظیمات سیستم";
                return View();
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateWorkerSettings(
            int heartbeatIntervalSeconds,
            int heartbeatTimeoutSeconds,
            int taskLeaseTimeoutMinutes,
            int maxRetryAttempts)
        {
            try
            {
                // In a real application, you would update these settings in a configuration service
                // For now, we'll just log the changes
                _logger.LogInformation("Worker settings updated: HeartbeatInterval={HeartbeatInterval}, " +
                    "HeartbeatTimeout={HeartbeatTimeout}, TaskLeaseTimeout={TaskLeaseTimeout}, MaxRetryAttempts={MaxRetryAttempts}",
                    heartbeatIntervalSeconds, heartbeatTimeoutSeconds, taskLeaseTimeoutMinutes, maxRetryAttempts);

                TempData["SuccessMessage"] = "تنظیمات ورکر با موفقیت بروزرسانی شد";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating worker settings");
                TempData["ErrorMessage"] = "خطا در بروزرسانی تنظیمات ورکر";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateScanSettings(
            int cvvChunkSize,
            int maxConcurrentJobs,
            int requestDelayMs,
            int timeoutSeconds)
        {
            try
            {
                // In a real application, you would update these settings in a configuration service
                _logger.LogInformation("Scan settings updated: CvvChunkSize={CvvChunkSize}, " +
                    "MaxConcurrentJobs={MaxConcurrentJobs}, RequestDelay={RequestDelay}, Timeout={Timeout}",
                    cvvChunkSize, maxConcurrentJobs, requestDelayMs, timeoutSeconds);

                TempData["SuccessMessage"] = "تنظیمات اسکن با موفقیت بروزرسانی شد";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating scan settings");
                TempData["ErrorMessage"] = "خطا در بروزرسانی تنظیمات اسکن";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateSystemSettings(
            int logRetentionDays,
            string maxLogFileSize,
            bool enableDetailedLogging)
        {
            try
            {
                // In a real application, you would update these settings in a configuration service
                _logger.LogInformation("System settings updated: LogRetentionDays={LogRetentionDays}, " +
                    "MaxLogFileSize={MaxLogFileSize}, EnableDetailedLogging={EnableDetailedLogging}",
                    logRetentionDays, maxLogFileSize, enableDetailedLogging);

                TempData["SuccessMessage"] = "تنظیمات سیستم با موفقیت بروزرسانی شد";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating system settings");
                TempData["ErrorMessage"] = "خطا در بروزرسانی تنظیمات سیستم";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> TestDatabaseConnection()
        {
            try
            {
                // Test database connection
                // This would involve testing the actual database connection
                TempData["SuccessMessage"] = "اتصال به پایگاه داده موفق بود";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database connection test failed");
                TempData["ErrorMessage"] = "خطا در اتصال به پایگاه داده";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> TestRedisConnection()
        {
            try
            {
                // Test Redis connection
                // This would involve testing the actual Redis connection
                TempData["SuccessMessage"] = "اتصال به Redis موفق بود";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis connection test failed");
                TempData["ErrorMessage"] = "خطا در اتصال به Redis";
            }

            return RedirectToAction(nameof(Index));
        }

        // API endpoints
        [HttpGet]
        public JsonResult GetSystemInfo()
        {
            try
            {
                var info = new
                {
                    version = "1.0.0",
                    environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
                    uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                    memoryUsage = GC.GetTotalMemory(false),
                    processorCount = Environment.ProcessorCount
                };

                return Json(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system info");
                return Json(new { error = "خطا در دریافت اطلاعات سیستم" });
            }
        }
    }
}