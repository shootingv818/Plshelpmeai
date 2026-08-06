using IvaScanner.Core.Models;
using IvaScanner.Master.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace IvaScanner.Master.Controllers
{
    public class ScanController : Controller
    {
        private readonly IScanJobService _jobService;
        private readonly ITaskDistributionService _taskDistribution;
        private readonly IScanOrchestrator _scanOrchestrator;
        private readonly IIvaAccountService _accountService;
        private readonly ILogger<ScanController> _logger;

        public ScanController(
            IScanJobService jobService,
            ITaskDistributionService taskDistribution,
            IScanOrchestrator scanOrchestrator,
            IIvaAccountService accountService,
            ILogger<ScanController> logger)
        {
            _jobService = jobService;
            _taskDistribution = taskDistribution;
            _scanOrchestrator = scanOrchestrator;
            _accountService = accountService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var jobs = await _jobService.GetJobsAsync(0, 50);
                return View(jobs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading scan jobs");
                ViewBag.ErrorMessage = "خطا در بارگذاری لیست اسکن‌ها";
                return View(new List<ScanJob>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            // Load available IVA accounts for phone number selection
            var accounts = await _accountService.GetAllAccountsAsync();
            ViewBag.Accounts = accounts;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateScanJobRequest request)
        {
            if (!ModelState.IsValid)
            {
                var accounts = await _accountService.GetAllAccountsAsync();
                ViewBag.Accounts = accounts;
                return View(request);
            }

            try
            {
                var job = await _scanOrchestrator.StartScanJobAsync(request);
                TempData["SuccessMessage"] = $"اسکن جدید با شناسه {job.Id} ایجاد شد";
                return RedirectToAction(nameof(Details), new { id = job.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating scan job for card {CardNumber}", request.CardNumber);
                ModelState.AddModelError("", "خطا در ایجاد اسکن جدید");
                
                var accounts = await _accountService.GetAllAccountsAsync();
                ViewBag.Accounts = accounts;
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
                var job = await _jobService.GetJobAsync(id);
                if (job == null)
                {
                    return NotFound();
                }

                return View(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading scan job details for {JobId}", id);
                ViewBag.ErrorMessage = "خطا در بارگذاری جزئیات اسکن";
                return View();
            }
        }

        [HttpGet]
        public async Task<IActionResult> Results(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            try
            {
                var job = await _jobService.GetJobAsync(id);
                if (job == null)
                {
                    return NotFound();
                }

                return View(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading scan job results for {JobId}", id);
                ViewBag.ErrorMessage = "خطا در بارگذاری نتایج اسکن";
                return View();
            }
        }

        [HttpPost]
        public async Task<IActionResult> Cancel(string id)
        {
            try
            {
                var success = await _scanOrchestrator.CancelScanJobAsync(id);
                if (success)
                {
                    TempData["SuccessMessage"] = "اسکن با موفقیت لغو شد";
                }
                else
                {
                    TempData["ErrorMessage"] = "خطا در لغو اسکن";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling scan job {JobId}", id);
                TempData["ErrorMessage"] = "خطا در لغو اسکن";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Pause(string id)
        {
            try
            {
                var success = await _scanOrchestrator.PauseScanJobAsync(id);
                if (success)
                {
                    TempData["SuccessMessage"] = "اسکن با موفقیت متوقف شد";
                }
                else
                {
                    TempData["ErrorMessage"] = "خطا در توقف اسکن";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pausing scan job {JobId}", id);
                TempData["ErrorMessage"] = "خطا در توقف اسکن";
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        public async Task<IActionResult> Resume(string id)
        {
            try
            {
                var success = await _scanOrchestrator.ResumeScanJobAsync(id);
                if (success)
                {
                    TempData["SuccessMessage"] = "اسکن با موفقیت ادامه یافت";
                }
                else
                {
                    TempData["ErrorMessage"] = "خطا در ادامه اسکن";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resuming scan job {JobId}", id);
                TempData["ErrorMessage"] = "خطا در ادامه اسکن";
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // API endpoints for real-time updates
        [HttpGet]
        public async Task<JsonResult> GetJobProgress(string jobId)
        {
            try
            {
                var progress = await _scanOrchestrator.GetJobProgressAsync(jobId);
                return Json(progress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job progress for {JobId}", jobId);
                return Json(new { error = "خطا در دریافت پیشرفت اسکن" });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetJobsJson()
        {
            try
            {
                var jobs = await _jobService.GetJobsAsync(0, 100);
                return Json(jobs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting jobs JSON");
                return Json(new { error = "خطا در دریافت اطلاعات اسکن‌ها" });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetScanStats()
        {
            try
            {
                var jobs = await _jobService.GetJobsAsync(0, 1000);
                var stats = new
                {
                    total = jobs.Count,
                    active = jobs.Count(j => j.Status == JobStatus.Running || j.Status == JobStatus.Paused),
                    completed = jobs.Count(j => j.Status == JobStatus.Completed),
                    failed = jobs.Count(j => j.Status == JobStatus.Failed),
                    cancelled = jobs.Count(j => j.Status == JobStatus.Cancelled)
                };

                return Json(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scan stats");
                return Json(new { error = "خطا در دریافت آمار اسکن‌ها" });
            }
        }

        // Results API endpoints
        [HttpGet]
        public async Task<JsonResult> GetJobResults(string jobId)
        {
            try
            {
                var results = await _scanOrchestrator.GetJobResultsAsync(jobId);
                var summary = new
                {
                    totalFound = results.Count(r => r.IsSuccess),
                    totalTested = results.Count,
                    successRate = results.Count > 0 ? (double)results.Count(r => r.IsSuccess) / results.Count * 100 : 0
                };

                return Json(new { results, summary });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job results for {JobId}", jobId);
                return Json(new { error = "خطا در دریافت نتایج اسکن" });
            }
        }
    }
}

        [HttpGet]
        public async Task<JsonResult> GetJobTasksSummary(string jobId)
        {
            try
            {
                var summary = await _scanOrchestrator.GetJobTasksSummaryAsync(jobId);
                return Json(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job tasks summary for {JobId}", jobId);
                return Json(new { error = "خطا در دریافت خلاصه تسک‌ها" });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetJobResultsCount(string jobId)
        {
            try
            {
                var count = await _scanOrchestrator.GetJobResultsCountAsync(jobId);
                return Json(new { count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job results count for {JobId}", jobId);
                return Json(new { count = 0 });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetJobWorkers(string jobId)
        {
            try
            {
                var workers = await _scanOrchestrator.GetJobWorkersAsync(jobId);
                return Json(workers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job workers for {JobId}", jobId);
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetRecentResults(string jobId)
        {
            try
            {
                var results = await _scanOrchestrator.GetRecentResultsAsync(jobId, 10);
                return Json(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent results for {JobId}", jobId);
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportResults(string jobId, string format = "excel", 
            string search = "", string status = "", string type = "")
        {
            try
            {
                var results = await _scanOrchestrator.GetJobResultsAsync(jobId);
                
                // Apply filters
                if (!string.IsNullOrEmpty(search))
                {
                    results = results.Where(r => 
                        r.PhoneNumber.Contains(search) ||
                        (r.Password != null && r.Password.Contains(search)) ||
                        (r.CVV != null && r.CVV.Contains(search))).ToList();
                }

                if (!string.IsNullOrEmpty(status))
                {
                    results = results.Where(r => r.Status.ToString().ToLower() == status.ToLower()).ToList();
                }

                if (!string.IsNullOrEmpty(type))
                {
                    results = results.Where(r => r.TestType.ToString().ToLower() == type.ToLower()).ToList();
                }

                var job = await _jobService.GetJobAsync(jobId);
                string fileName = $"scan_results_{jobId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

                if (format.ToLower() == "json")
                {
                    var jsonData = JsonSerializer.Serialize(new { job, results }, new JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                    
                    return File(System.Text.Encoding.UTF8.GetBytes(jsonData), 
                               "application/json", $"{fileName}.json");
                }
                else
                {
                    // Excel export would be implemented here
                    // For now, return CSV format
                    var csvData = GenerateCSV(results);
                    return File(System.Text.Encoding.UTF8.GetBytes(csvData), 
                               "text/csv", $"{fileName}.csv");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting results for {JobId}", jobId);
                TempData["ErrorMessage"] = "خطا در دانلود نتایج";
                return RedirectToAction(nameof(Results), new { id = jobId });
            }
        }

        private string GenerateCSV(IEnumerable<ScanResult> results)
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("شماره تلفن,وضعیت,نوع تست,پسورد,CVV,ماه انقضا,سال انقضا,نام ورکر,زمان");

            foreach (var result in results)
            {
                csv.AppendLine($"{result.PhoneNumber},{result.Status},{result.TestType}," +
                              $"{result.Password ?? ""},{result.CVV ?? ""}," +
                              $"{result.ExpiryMonth ?? 0},{result.ExpiryYear ?? 0}," +
                              $"{result.WorkerName ?? ""},{result.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            }

            return csv.ToString();
        }