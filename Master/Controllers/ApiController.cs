using IvaScanner.Core.Models;
using IvaScanner.Master.Services;
using Microsoft.AspNetCore.Mvc;

namespace IvaScanner.Master.Controllers
{
    [ApiController]
    [Route("api")]
    public class ApiController : ControllerBase
    {
        private readonly IWorkerService _workerService;
        private readonly ITaskDistributionService _taskDistribution;
        private readonly IScanJobService _jobService;
        private readonly IScanOrchestrator _scanOrchestrator;
        private readonly IIvaAccountService _accountService;
        private readonly ILogger<ApiController> _logger;

        public ApiController(
            IWorkerService workerService,
            ITaskDistributionService taskDistribution,
            IScanJobService jobService,
            IScanOrchestrator scanOrchestrator,
            IIvaAccountService accountService,
            ILogger<ApiController> logger)
        {
            _workerService = workerService;
            _taskDistribution = taskDistribution;
            _jobService = jobService;
            _scanOrchestrator = scanOrchestrator;
            _accountService = accountService;
            _logger = logger;
        }

        #region Worker Management

        [HttpPost("workers/register")]
        public async Task<IActionResult> RegisterWorker([FromBody] WorkerRegistrationRequest request)
        {
            try
            {
                var worker = await _workerService.RegisterWorkerAsync(request);
                return Ok(worker);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering worker {WorkerId}", request.WorkerId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("workers/heartbeat")]
        public async Task<IActionResult> WorkerHeartbeat([FromBody] WorkerHeartbeatRequest request)
        {
            try
            {
                var worker = await _workerService.UpdateWorkerHeartbeatAsync(request);
                return Ok(worker);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing heartbeat for worker {WorkerId}", request.WorkerId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpDelete("workers/{workerId}")]
        public async Task<IActionResult> DeregisterWorker(string workerId)
        {
            try
            {
                var success = await _workerService.DeregisterWorkerAsync(workerId);
                if (success)
                {
                    return Ok(new { message = "Worker deregistered successfully" });
                }
                return NotFound(new { error = "Worker not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deregistering worker {WorkerId}", workerId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("workers")]
        public async Task<IActionResult> GetWorkers()
        {
            try
            {
                var workers = await _workerService.GetAllWorkersAsync();
                return Ok(workers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting workers");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("workers/{workerId}")]
        public async Task<IActionResult> GetWorker(string workerId)
        {
            try
            {
                var worker = await _workerService.GetWorkerAsync(workerId);
                if (worker == null)
                {
                    return NotFound(new { error = "Worker not found" });
                }
                return Ok(worker);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting worker {WorkerId}", workerId);
                return BadRequest(new { error = ex.Message });
            }
        }

        #endregion

        #region Task Management

        [HttpGet("workers/{workerId}/task")]
        public async Task<IActionResult> GetTask(string workerId)
        {
            try
            {
                var task = await _taskDistribution.GetNextTaskAsync(workerId);
                if (task == null)
                {
                    return Ok(new { message = "No tasks available" });
                }
                return Ok(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting task for worker {WorkerId}", workerId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("tasks/complete")]
        public async Task<IActionResult> CompleteTask([FromBody] TaskCompleteRequest request)
        {
            try
            {
                var success = await _taskDistribution.CompleteTaskAsync(
                    request.TaskId, request.WorkerId, request.Success, request.Result, request.ErrorMessage);
                
                if (success)
                {
                    // Process the completed task through orchestrator
                    if (request.Success && !string.IsNullOrEmpty(request.Result))
                    {
                        await _scanOrchestrator.ProcessCompletedTaskAsync(request.TaskId, request.Result);
                    }
                    else if (!request.Success && !string.IsNullOrEmpty(request.ErrorMessage))
                    {
                        await _scanOrchestrator.ProcessFailedTaskAsync(request.TaskId, request.ErrorMessage);
                    }
                    
                    return Ok(new { message = "Task completed successfully" });
                }
                return BadRequest(new { error = "Failed to complete task" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing task {TaskId} by worker {WorkerId}", 
                    request.TaskId, request.WorkerId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("tasks/{taskId}")]
        public async Task<IActionResult> GetTask(string taskId)
        {
            try
            {
                var task = await _taskDistribution.GetTaskAsync(taskId);
                if (task == null)
                {
                    return NotFound(new { error = "Task not found" });
                }
                return Ok(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting task {TaskId}", taskId);
                return BadRequest(new { error = ex.Message });
            }
        }

        #endregion

        #region Job Management

        [HttpPost("jobs")]
        public async Task<IActionResult> CreateJob([FromBody] CreateScanJobRequest request)
        {
            try
            {
                var job = await _scanOrchestrator.StartScanJobAsync(request);
                return Ok(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating scan job for card {CardNumber}", request.CardNumber);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("jobs/{jobId}/progress")]
        public async Task<IActionResult> GetJobProgress(string jobId)
        {
            try
            {
                var progress = await _scanOrchestrator.GetJobProgressAsync(jobId);
                return Ok(progress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job progress for {JobId}", jobId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("jobs")]
        public async Task<IActionResult> GetJobs([FromQuery] int skip = 0, [FromQuery] int take = 50)
        {
            try
            {
                var jobs = await _jobService.GetJobsAsync(skip, take);
                return Ok(jobs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting jobs");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("jobs/{jobId}")]
        public async Task<IActionResult> GetJob(string jobId)
        {
            try
            {
                var job = await _jobService.GetJobAsync(jobId);
                if (job == null)
                {
                    return NotFound(new { error = "Job not found" });
                }
                return Ok(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job {JobId}", jobId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("jobs/{jobId}/cancel")]
        public async Task<IActionResult> CancelJob(string jobId)
        {
            try
            {
                var success = await _scanOrchestrator.CancelScanJobAsync(jobId);
                if (success)
                {
                    return Ok(new { message = "Job cancelled successfully" });
                }
                return BadRequest(new { error = "Failed to cancel job" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling job {JobId}", jobId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("jobs/{jobId}/pause")]
        public async Task<IActionResult> PauseJob(string jobId)
        {
            try
            {
                var success = await _scanOrchestrator.PauseScanJobAsync(jobId);
                if (success)
                {
                    return Ok(new { message = "Job paused successfully" });
                }
                return BadRequest(new { error = "Failed to pause job" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pausing job {JobId}", jobId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("jobs/{jobId}/resume")]
        public async Task<IActionResult> ResumeJob(string jobId)
        {
            try
            {
                var success = await _scanOrchestrator.ResumeScanJobAsync(jobId);
                if (success)
                {
                    return Ok(new { message = "Job resumed successfully" });
                }
                return BadRequest(new { error = "Failed to resume job" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resuming job {JobId}", jobId);
                return BadRequest(new { error = ex.Message });
            }
        }

        #endregion

        #region System Status

        [HttpGet("status")]
        public async Task<IActionResult> GetSystemStatus()
        {
            try
            {
                var activeWorkers = await _workerService.GetActiveWorkerCountAsync();
                var activeJobs = await _jobService.GetActiveJobCountAsync();
                var pendingTasks = await _taskDistribution.GetPendingTaskCountAsync();
                var inProgressTasks = await _taskDistribution.GetInProgressTaskCountAsync();
                var activeAccounts = await _accountService.GetActiveAccountCountAsync();

                return Ok(new
                {
                    activeWorkers,
                    activeJobs,
                    pendingTasks,
                    inProgressTasks,
                    activeAccounts,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system status");
                return BadRequest(new { error = ex.Message });
            }
        }

        #endregion
    }
}