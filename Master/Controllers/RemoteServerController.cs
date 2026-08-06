using Microsoft.AspNetCore.Mvc;
using IvaScanner.Core.Models;
using IvaScanner.Master.Services;

namespace IvaScanner.Master.Controllers
{
    public class RemoteServerController : Controller
    {
        private readonly IRemoteServerService _remoteServerService;
        private readonly ISystemLogService _systemLogService;
        private readonly ISignalRNotificationService _signalRService;
        private readonly ILogger<RemoteServerController> _logger;

        public RemoteServerController(
            IRemoteServerService remoteServerService,
            ISystemLogService systemLogService,
            ISignalRNotificationService signalRService,
            ILogger<RemoteServerController> logger)
        {
            _remoteServerService = remoteServerService;
            _systemLogService = systemLogService;
            _signalRService = signalRService;
            _logger = logger;
        }

        // GET: /RemoteServer
        public async Task<IActionResult> Index()
        {
            try
            {
                var servers = await _remoteServerService.GetServersAsync();
                return View(servers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading remote servers");
                TempData["Error"] = "خطا در بارگیری لیست سرورها";
                return View(new List<RemoteServer>());
            }
        }

        // GET: /RemoteServer/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: /RemoteServer/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateRemoteServerRequest request)
        {
            if (!ModelState.IsValid)
            {
                return View(request);
            }

            try
            {
                var server = await _remoteServerService.CreateServerAsync(request);
                
                TempData["Success"] = $"سرور {server.Name} با موفقیت اضافه شد";
                
                await _systemLogService.LogInformationAsync("RemoteServerController", 
                    $"New remote server created: {server.Name}");

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating remote server");
                ModelState.AddModelError("", "خطا در ایجاد سرور: " + ex.Message);
                return View(request);
            }
        }

        // GET: /RemoteServer/Details/5
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            try
            {
                var server = await _remoteServerService.GetServerAsync(id);
                if (server == null)
                {
                    return NotFound();
                }

                // Get deployment history
                var deploymentHistory = await _remoteServerService.GetDeploymentHistoryAsync(id);
                
                ViewBag.DeploymentHistory = deploymentHistory;
                
                return View(server);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading server details for {ServerId}", id);
                TempData["Error"] = "خطا در بارگیری اطلاعات سرور";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: /RemoteServer/TestConnection
        [HttpPost]
        public async Task<IActionResult> TestConnection(string serverId)
        {
            try
            {
                var result = await _remoteServerService.TestConnectionAsync(serverId);
                
                await _signalRService.SendServerStatusUpdateAsync(serverId, result);
                
                return Json(new
                {
                    success = result.CanConnect,
                    hasDotNet = result.HasDotNet,
                    dotNetVersion = result.DotNetVersion,
                    hasSystemd = result.HasSystemd,
                    hasSudo = result.HasSudo,
                    errorMessage = result.ErrorMessage,
                    systemInfo = result.SystemInfo
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing connection to server {ServerId}", serverId);
                return Json(new { success = false, errorMessage = ex.Message });
            }
        }

        // POST: /RemoteServer/Deploy
        [HttpPost]
        public async Task<IActionResult> DeployWorkers([FromBody] DeployWorkerRequest request)
        {
            try
            {
                var jobId = await _remoteServerService.DeployWorkersAsync(request);
                
                await _systemLogService.LogInformationAsync("RemoteServerController", 
                    $"Worker deployment started: Server {request.ServerId}, Count {request.WorkerCount}");

                return Json(new { success = true, jobId = jobId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deploying workers to server {ServerId}", request.ServerId);
                return Json(new { success = false, errorMessage = ex.Message });
            }
        }

        // GET: /RemoteServer/DeploymentProgress/job-123
        public async Task<IActionResult> DeploymentProgress(string jobId)
        {
            try
            {
                var progress = await _remoteServerService.GetDeploymentProgressAsync(jobId);
                return Json(progress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting deployment progress for job {JobId}", jobId);
                return Json(new { error = ex.Message });
            }
        }

        // POST: /RemoteServer/ManageWorkers
        [HttpPost]
        public async Task<IActionResult> ManageWorkers(string serverId, string action, List<string>? workerIds = null)
        {
            try
            {
                bool result = action.ToLower() switch
                {
                    "start" => await _remoteServerService.StartWorkersAsync(serverId, workerIds),
                    "stop" => await _remoteServerService.StopWorkersAsync(serverId, workerIds),
                    "restart" => await _remoteServerService.RestartWorkersAsync(serverId, workerIds),
                    "remove" => await _remoteServerService.RemoveWorkersAsync(serverId, workerIds ?? new List<string>()),
                    _ => throw new ArgumentException($"Unknown action: {action}")
                };

                var actionPersian = action.ToLower() switch
                {
                    "start" => "راه‌اندازی",
                    "stop" => "توقف",
                    "restart" => "راه‌اندازی مجدد",
                    "remove" => "حذف",
                    _ => action
                };

                if (result)
                {
                    await _systemLogService.LogInformationAsync("RemoteServerController", 
                        $"Worker management action completed: {action} on server {serverId}");
                    
                    return Json(new { success = true, message = $"{actionPersian} Workerها با موفقیت انجام شد" });
                }
                else
                {
                    return Json(new { success = false, message = $"خطا در {actionPersian} Workerها" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error managing workers on server {ServerId}: {Action}", serverId, action);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: /RemoteServer/WorkerLogs/server-id/worker-id
        public async Task<IActionResult> WorkerLogs(string serverId, string workerId, int lines = 100)
        {
            try
            {
                var logs = await _remoteServerService.GetWorkerLogsAsync(serverId, workerId, lines);
                return Json(new { success = true, logs = logs });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting worker logs: Server {ServerId}, Worker {WorkerId}", 
                    serverId, workerId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /RemoteServer/ExecuteCommand
        [HttpPost]
        public async Task<IActionResult> ExecuteCommand(string serverId, string command, int timeoutSeconds = 30)
        {
            try
            {
                var result = await _remoteServerService.ExecuteCommandAsync(serverId, command, timeoutSeconds);
                
                await _systemLogService.LogInformationAsync("RemoteServerController", 
                    $"Remote command executed on server {serverId}: {command}");

                return Json(new 
                { 
                    success = result.Success,
                    output = result.Output,
                    errorOutput = result.ErrorOutput,
                    exitCode = result.ExitCode,
                    duration = result.Duration.TotalMilliseconds
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing command on server {ServerId}: {Command}", serverId, command);
                return Json(new { success = false, errorMessage = ex.Message });
            }
        }

        // DELETE: /RemoteServer/Delete/5
        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                var success = await _remoteServerService.DeleteServerAsync(id);
                
                if (success)
                {
                    TempData["Success"] = "سرور با موفقیت حذف شد";
                    
                    await _systemLogService.LogInformationAsync("RemoteServerController", 
                        $"Remote server deleted: {id}");
                }
                else
                {
                    TempData["Error"] = "خطا در حذف سرور";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting server {ServerId}", id);
                TempData["Error"] = "خطا در حذف سرور: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: /RemoteServer/Statistics/server-id
        public async Task<IActionResult> Statistics(string serverId)
        {
            try
            {
                var stats = await _remoteServerService.GetServerStatisticsAsync(serverId);
                return Json(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting server statistics for {ServerId}", serverId);
                return Json(new { error = ex.Message });
            }
        }
    }
}