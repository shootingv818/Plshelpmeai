using IvaScanner.Master.Services;
using Microsoft.AspNetCore.Mvc;

namespace IvaScanner.Master.Controllers
{
    public class HomeController : Controller
    {
        private readonly IWorkerService _workerService;
        private readonly IScanJobService _jobService;
        private readonly ITaskDistributionService _taskDistribution;
        private readonly IIvaAccountService _accountService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            IWorkerService workerService,
            IScanJobService jobService,
            ITaskDistributionService taskDistribution,
            IIvaAccountService accountService,
            ILogger<HomeController> logger)
        {
            _workerService = workerService;
            _jobService = jobService;
            _taskDistribution = taskDistribution;
            _accountService = accountService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                // Get dashboard statistics
                var activeWorkers = await _workerService.GetActiveWorkerCountAsync();
                var totalWorkers = (await _workerService.GetAllWorkersAsync()).Count();
                var activeJobs = await _jobService.GetActiveJobCountAsync();
                var totalJobs = (await _jobService.GetJobsAsync(0, 1000)).Count();
                var pendingTasks = await _taskDistribution.GetPendingTaskCountAsync();
                var inProgressTasks = await _taskDistribution.GetInProgressTaskCountAsync();
                var activeAccounts = await _accountService.GetActiveAccountCountAsync();

                // Get recent activity
                var recentJobs = await _jobService.GetJobsAsync(0, 5);
                var allWorkers = await _workerService.GetAllWorkersAsync();

                ViewBag.ActiveWorkers = activeWorkers;
                ViewBag.TotalWorkers = totalWorkers;
                ViewBag.ActiveJobs = activeJobs;
                ViewBag.TotalJobs = totalJobs;
                ViewBag.PendingTasks = pendingTasks;
                ViewBag.InProgressTasks = inProgressTasks;
                ViewBag.ActiveAccounts = activeAccounts;
                ViewBag.RecentJobs = recentJobs;
                ViewBag.Workers = allWorkers;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading dashboard");
                ViewBag.ErrorMessage = "خطا در بارگذاری داشبورد";
                return View();
            }
        }

        public IActionResult Error()
        {
            return View();
        }
    }
}