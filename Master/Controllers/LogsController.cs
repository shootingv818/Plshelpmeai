using IvaScanner.Core.Models;
using IvaScanner.Master.Services;
using Microsoft.AspNetCore.Mvc;

namespace IvaScanner.Master.Controllers
{
    public class LogsController : Controller
    {
        private readonly ISystemLogService _logService;
        private readonly ILogger<LogsController> _logger;

        public LogsController(ISystemLogService logService, ILogger<LogsController> logger)
        {
            _logService = logService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var health = await _logService.GetLogSystemHealthAsync();
                ViewBag.LogHealth = health;
                
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading logs page");
                ViewBag.ErrorMessage = "خطا در بارگذاری صفحه لاگ‌ها";
                return View();
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetLogsJson(int skip = 0, int take = 50, 
            string? level = null, string? component = null, string? context = null,
            string? fromDate = null, string? toDate = null)
        {
            try
            {
                var minLevel = string.IsNullOrEmpty(level) ? null : Enum.Parse<LogLevel>(level);
                var from = string.IsNullOrEmpty(fromDate) ? null : DateTime.Parse(fromDate);
                var to = string.IsNullOrEmpty(toDate) ? null : DateTime.Parse(toDate);

                var logs = await _logService.GetLogsAsync(skip, take, minLevel, component, context, from, to);
                var totalCount = await _logService.GetLogCountAsync(minLevel, from, to);

                return Json(new { logs, totalCount });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting logs JSON");
                return Json(new { error = "خطا در دریافت لاگ‌ها" });
            }
        }

        [HttpGet]
        public async Task<JsonResult> SearchLogs(string q, int skip = 0, int take = 50)
        {
            try
            {
                var logs = await _logService.SearchLogsAsync(q, skip, take);
                return Json(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching logs for term: {SearchTerm}", q);
                return Json(new { error = "خطا در جستجوی لاگ‌ها" });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetLogStats(string? fromDate = null, string? toDate = null)
        {
            try
            {
                var from = string.IsNullOrEmpty(fromDate) ? null : DateTime.Parse(fromDate);
                var to = string.IsNullOrEmpty(toDate) ? null : DateTime.Parse(toDate);

                var levelCounts = await _logService.GetLogCountsByLevelAsync(from, to);
                var componentStats = await _logService.GetLogStatsByComponentAsync(from, to);

                return Json(new { levelCounts, componentStats });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting log statistics");
                return Json(new { error = "خطا در دریافت آمار لاگ‌ها" });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetRecentLogs(int count = 20)
        {
            try
            {
                var logs = await _logService.GetRecentLogsAsync(count);
                return Json(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent logs");
                return Json(new { error = "خطا در دریافت لاگ‌های اخیر" });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetLogHealth()
        {
            try
            {
                var health = await _logService.GetLogSystemHealthAsync();
                return Json(health);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting log system health");
                return Json(new { error = "خطا در دریافت وضعیت سیستم لاگ" });
            }
        }

        [HttpPost]
        public async Task<JsonResult> CleanupLogs(int days = 30)
        {
            try
            {
                await _logService.CleanupOldLogsAsync(TimeSpan.FromDays(days));
                await _logService.LogInfoAsync($"Manual log cleanup initiated for logs older than {days} days", 
                    "manual_cleanup", "LogsController");
                
                return Json(new { success = true, message = $"لاگ‌های قدیمی‌تر از {days} روز پاک شدند" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during manual log cleanup");
                return Json(new { success = false, error = "خطا در پاک‌سازی لاگ‌ها" });
            }
        }

        [HttpPost]
        public async Task<JsonResult> ArchiveLogs(string beforeDate)
        {
            try
            {
                var date = DateTime.Parse(beforeDate);
                await _logService.ArchiveLogsAsync(date);
                await _logService.LogInfoAsync($"Manual log archive initiated for logs before {date:yyyy-MM-dd}", 
                    "manual_archive", "LogsController");
                
                return Json(new { success = true, message = $"لاگ‌های قبل از {date:yyyy/MM/dd} آرشیو شدند" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during manual log archive");
                return Json(new { success = false, error = "خطا در آرشیو لاگ‌ها" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Export(string? level = null, string? component = null, 
            string? fromDate = null, string? toDate = null, string format = "csv")
        {
            try
            {
                var minLevel = string.IsNullOrEmpty(level) ? null : Enum.Parse<LogLevel>(level);
                var from = string.IsNullOrEmpty(fromDate) ? null : DateTime.Parse(fromDate);
                var to = string.IsNullOrEmpty(toDate) ? null : DateTime.Parse(toDate);

                var logs = await _logService.GetLogsAsync(0, 10000, minLevel, component, null, from, to);
                
                if (format.ToLower() == "json")
                {
                    var jsonData = System.Text.Json.JsonSerializer.Serialize(logs, new System.Text.Json.JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                    
                    return File(System.Text.Encoding.UTF8.GetBytes(jsonData), 
                               "application/json", $"logs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
                }
                else
                {
                    var csvData = GenerateLogCSV(logs);
                    return File(System.Text.Encoding.UTF8.GetBytes(csvData), 
                               "text/csv", $"logs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting logs");
                TempData["ErrorMessage"] = "خطا در دانلود لاگ‌ها";
                return RedirectToAction("Index");
            }
        }

        private string GenerateLogCSV(List<SystemLog> logs)
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("تاریخ و زمان,سطح,پیام,کامپوننت,کانتکست,متادیتا");

            foreach (var log in logs)
            {
                var message = log.Message?.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", " ");
                var component = log.Component?.Replace("\"", "\"\"");
                var context = log.Context?.Replace("\"", "\"\"");
                var metadata = log.Metadata?.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", " ");
                
                csv.AppendLine($"\"{log.CreatedAt:yyyy-MM-dd HH:mm:ss}\",\"{log.Level}\"," +
                              $"\"{message}\",\"{component}\",\"{context}\",\"{metadata}\"");
            }

            return csv.ToString();
        }
    }
}