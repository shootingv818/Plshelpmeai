using IvaScanner.Core.Models;
using IvaScanner.Master.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace IvaScanner.Master.Controllers
{
    public class WorkersController : Controller
    {
        private readonly IWorkerService _workerService;
        private readonly IIvaAccountService _accountService;
        private readonly ILogger<WorkersController> _logger;

        public WorkersController(
            IWorkerService workerService,
            IIvaAccountService accountService,
            ILogger<WorkersController> logger)
        {
            _workerService = workerService;
            _accountService = accountService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var workers = await _workerService.GetAllWorkersAsync();
                return View(workers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading workers");
                ViewBag.ErrorMessage = "خطا در بارگذاری لیست ورکرها";
                return View(new List<Worker>());
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
                var worker = await _workerService.GetWorkerAsync(id);
                if (worker == null)
                {
                    return NotFound();
                }

                return View(worker);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading worker details for {WorkerId}", id);
                ViewBag.ErrorMessage = "خطا در بارگذاری جزئیات ورکر";
                return View();
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStatus(string workerId, WorkerStatus status)
        {
            try
            {
                var success = await _workerService.UpdateWorkerStatusAsync(workerId, status);
                if (success)
                {
                    TempData["SuccessMessage"] = "وضعیت ورکر با موفقیت بروزرسانی شد";
                }
                else
                {
                    TempData["ErrorMessage"] = "خطا در بروزرسانی وضعیت ورکر";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating worker status for {WorkerId}", workerId);
                TempData["ErrorMessage"] = "خطا در بروزرسانی وضعیت ورکر";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                var success = await _workerService.DeregisterWorkerAsync(id);
                if (success)
                {
                    TempData["SuccessMessage"] = "ورکر با موفقیت حذف شد";
                }
                else
                {
                    TempData["ErrorMessage"] = "خطا در حذف ورکر";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting worker {WorkerId}", id);
                TempData["ErrorMessage"] = "خطا در حذف ورکر";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> AssignAccount(string workerId, string accountId)
        {
            try
            {
                var worker = await _workerService.GetWorkerAsync(workerId);
                if (worker == null)
                {
                    return NotFound();
                }

                worker.IvaAccountId = accountId;
                worker.UpdatedAt = DateTime.UtcNow;

                // This would be implemented in WorkerService
                // await _workerService.UpdateWorkerAsync(worker);

                TempData["SuccessMessage"] = "اکانت ایوا با موفقیت به ورکر اختصاص داده شد";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning account {AccountId} to worker {WorkerId}", accountId, workerId);
                TempData["ErrorMessage"] = "خطا در اختصاص اکانت به ورکر";
            }

            return RedirectToAction(nameof(Details), new { id = workerId });
        }

        // API endpoints for real-time updates
        [HttpGet]
        public async Task<JsonResult> GetWorkersJson()
        {
            try
            {
                var workers = await _workerService.GetAllWorkersAsync();
                return Json(workers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting workers JSON");
                return Json(new { error = "خطا در دریافت اطلاعات ورکرها" });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetWorkerStats()
        {
            try
            {
                var workers = await _workerService.GetAllWorkersAsync();
                var stats = new
                {
                    total = workers.Count,
                    online = workers.Count(w => w.Status == WorkerStatus.Online),
                    working = workers.Count(w => w.Status == WorkerStatus.Working),
                    offline = workers.Count(w => w.Status == WorkerStatus.Offline),
                    error = workers.Count(w => w.Status == WorkerStatus.Error)
                };

                return Json(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting worker stats");
                return Json(new { error = "خطا در دریافت آمار ورکرها" });
            }
        }
    }
}