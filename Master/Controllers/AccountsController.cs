using IvaScanner.Core.Models;
using IvaScanner.Master.Services;
using Microsoft.AspNetCore.Mvc;

namespace IvaScanner.Master.Controllers
{
    public class AccountsController : Controller
    {
        private readonly IIvaAccountService _accountService;
        private readonly ILogger<AccountsController> _logger;

        public AccountsController(
            IIvaAccountService accountService,
            ILogger<AccountsController> logger)
        {
            _accountService = accountService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var accounts = await _accountService.GetAllAccountsAsync();
                return View(accounts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading IVA accounts");
                ViewBag.ErrorMessage = "خطا در بارگذاری لیست اکانت‌های ایوا";
                return View(new List<IvaAccount>());
            }
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateIvaAccountRequest request)
        {
            if (!ModelState.IsValid)
            {
                return View(request);
            }

            try
            {
                var account = await _accountService.CreateAccountAsync(request);
                TempData["SuccessMessage"] = $"اکانت ایوا {account.PhoneNumber} با موفقیت ایجاد شد";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating IVA account for {PhoneNumber}", request.PhoneNumber);
                ModelState.AddModelError("", "خطا در ایجاد اکانت ایوا");
                return View(request);
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
                var account = await _accountService.GetAccountAsync(id);
                if (account == null)
                {
                    return NotFound();
                }

                var model = new UpdateIvaAccountRequest
                {
                    Id = account.Id,
                    PhoneNumber = account.PhoneNumber,
                    IsActive = account.IsActive
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading IVA account for edit {AccountId}", id);
                ViewBag.ErrorMessage = "خطا در بارگذاری اکانت ایوا";
                return View();
            }
        }

        [HttpPost]
        public async Task<IActionResult> Edit(UpdateIvaAccountRequest request)
        {
            if (!ModelState.IsValid)
            {
                return View(request);
            }

            try
            {
                var success = await _accountService.UpdateAccountAsync(request);
                if (success)
                {
                    TempData["SuccessMessage"] = "اکانت ایوا با موفقیت بروزرسانی شد";
                    return RedirectToAction(nameof(Index));
                }
                else
                {
                    ModelState.AddModelError("", "خطا در بروزرسانی اکانت ایوا");
                    return View(request);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating IVA account {AccountId}", request.Id);
                ModelState.AddModelError("", "خطا در بروزرسانی اکانت ایوا");
                return View(request);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                var success = await _accountService.DeleteAccountAsync(id);
                if (success)
                {
                    TempData["SuccessMessage"] = "اکانت ایوا با موفقیت حذف شد";
                }
                else
                {
                    TempData["ErrorMessage"] = "خطا در حذف اکانت ایوا";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting IVA account {AccountId}", id);
                TempData["ErrorMessage"] = "خطا در حذف اکانت ایوا";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Toggle(string id)
        {
            try
            {
                var account = await _accountService.GetAccountAsync(id);
                if (account == null)
                {
                    TempData["ErrorMessage"] = "اکانت ایوا یافت نشد";
                    return RedirectToAction(nameof(Index));
                }

                var request = new UpdateIvaAccountRequest
                {
                    Id = account.Id,
                    PhoneNumber = account.PhoneNumber,
                    IsActive = !account.IsActive
                };

                var success = await _accountService.UpdateAccountAsync(request);
                if (success)
                {
                    TempData["SuccessMessage"] = account.IsActive ? "اکانت ایوا غیرفعال شد" : "اکانت ایوا فعال شد";
                }
                else
                {
                    TempData["ErrorMessage"] = "خطا در تغییر وضعیت اکانت ایوا";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling IVA account status {AccountId}", id);
                TempData["ErrorMessage"] = "خطا در تغییر وضعیت اکانت ایوا";
            }

            return RedirectToAction(nameof(Index));
        }

        // API endpoints for real-time updates
        [HttpGet]
        public async Task<JsonResult> GetAccountsJson()
        {
            try
            {
                var accounts = await _accountService.GetAllAccountsAsync();
                return Json(accounts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting accounts JSON");
                return Json(new { error = "خطا در دریافت اطلاعات اکانت‌ها" });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetAccountStats()
        {
            try
            {
                var accounts = await _accountService.GetAllAccountsAsync();
                var stats = new
                {
                    total = accounts.Count,
                    active = accounts.Count(a => a.IsActive),
                    inactive = accounts.Count(a => !a.IsActive),
                    withSession = accounts.Count(a => !string.IsNullOrEmpty(a.SessionData))
                };

                return Json(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting account stats");
                return Json(new { error = "خطا در دریافت آمار اکانت‌ها" });
            }
        }
    }
}