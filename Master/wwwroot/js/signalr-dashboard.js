// SignalR Dashboard Connection and Event Handlers

// Global SignalR connection
let dashboardConnection = null;
let isConnected = false;

// Initialize SignalR connection
async function initializeSignalR() {
    try {
        dashboardConnection = new signalR.HubConnectionBuilder()
            .withUrl("/dashboardHub")
            .withAutomaticReconnect([0, 2000, 10000, 30000])
            .configureLogging(signalR.LogLevel.Information)
            .build();

        // Connection event handlers
        dashboardConnection.onclose(onConnectionClosed);
        dashboardConnection.onreconnecting(onReconnecting);
        dashboardConnection.onreconnected(onReconnected);

        // Register event handlers
        registerEventHandlers();

        // Start connection
        await dashboardConnection.start();
        isConnected = true;
        
        console.log("SignalR connected to dashboard hub");
        
        // Subscribe to updates based on current page
        await subscribeToPageUpdates();
        
        // Update connection indicator
        updateConnectionStatus(true);
        
    } catch (error) {
        console.error("SignalR connection failed:", error);
        updateConnectionStatus(false);
        
        // Retry connection after 5 seconds
        setTimeout(initializeSignalR, 5000);
    }
}

// Register all SignalR event handlers
function registerEventHandlers() {
    // System events
    dashboardConnection.on("SystemStatusUpdated", onSystemStatusUpdated);
    dashboardConnection.on("SystemAlert", onSystemAlert);
    dashboardConnection.on("ConnectionCountUpdated", onConnectionCountUpdated);

    // Worker events
    dashboardConnection.on("WorkerStatusChanged", onWorkerStatusChanged);
    dashboardConnection.on("WorkerRegistered", onWorkerRegistered);
    dashboardConnection.on("WorkerDeregistered", onWorkerDeregistered);
    dashboardConnection.on("WorkersStatsUpdated", onWorkersStatsUpdated);

    // Job events
    dashboardConnection.on("JobCreated", onJobCreated);
    dashboardConnection.on("JobStatusChanged", onJobStatusChanged);
    dashboardConnection.on("JobProgressUpdated", onJobProgressUpdated);
    dashboardConnection.on("JobCompleted", onJobCompleted);

    // Task events
    dashboardConnection.on("TaskAssigned", onTaskAssigned);
    dashboardConnection.on("TaskCompleted", onTaskCompleted);

    // Log events
    dashboardConnection.on("NewLogEntry", onNewLogEntry);
    dashboardConnection.on("LogStatsUpdated", onLogStatsUpdated);

    // Account events
    dashboardConnection.on("AccountStatusChanged", onAccountStatusChanged);
}

// Subscribe to page-specific updates
async function subscribeToPageUpdates() {
    if (!isConnected) return;

    const controller = getCurrentController();
    
    try {
        switch (controller) {
            case 'home':
                // Dashboard needs all updates
                await dashboardConnection.invoke("JoinWorkerUpdates");
                await dashboardConnection.invoke("JoinSystemUpdates");
                break;
                
            case 'workers':
                await dashboardConnection.invoke("JoinWorkerUpdates");
                break;
                
            case 'scan':
                // Subscribe to all job updates
                break;
                
            case 'logs':
                await dashboardConnection.invoke("JoinSystemUpdates");
                break;
        }
    } catch (error) {
        console.error("Error subscribing to updates:", error);
    }
}

// Subscribe to specific job updates
async function subscribeToJobUpdates(jobId) {
    if (!isConnected) return;
    
    try {
        await dashboardConnection.invoke("JoinJobUpdates", jobId);
        console.log(`Subscribed to job updates: ${jobId}`);
    } catch (error) {
        console.error(`Error subscribing to job ${jobId}:`, error);
    }
}

// Unsubscribe from job updates
async function unsubscribeFromJobUpdates(jobId) {
    if (!isConnected) return;
    
    try {
        await dashboardConnection.invoke("LeaveJobUpdates", jobId);
        console.log(`Unsubscribed from job updates: ${jobId}`);
    } catch (error) {
        console.error(`Error unsubscribing from job ${jobId}:`, error);
    }
}

// Connection event handlers
function onConnectionClosed(error) {
    isConnected = false;
    console.log("SignalR connection closed:", error);
    updateConnectionStatus(false);
    
    // Show connection lost notification
    IvaScanner.showNotification("اتصال قطع شد، در حال تلاش برای اتصال مجدد...", "warning");
}

function onReconnecting(error) {
    console.log("SignalR reconnecting:", error);
    updateConnectionStatus(false, "در حال اتصال مجدد...");
}

async function onReconnected(connectionId) {
    isConnected = true;
    console.log("SignalR reconnected:", connectionId);
    updateConnectionStatus(true);
    
    // Re-subscribe to updates
    await subscribeToPageUpdates();
    
    // Show reconnection notification
    IvaScanner.showNotification("اتصال برقرار شد", "success", 2000);
}

// Update connection status indicator
function updateConnectionStatus(connected, message = "") {
    const statusElement = document.getElementById('real-time-stats');
    if (!statusElement) return;
    
    const badge = statusElement.querySelector('.badge');
    if (!badge) return;
    
    if (connected) {
        badge.className = 'badge bg-success me-2';
        badge.innerHTML = '<i class="bi bi-check-circle me-1"></i>آنلاین';
    } else {
        badge.className = 'badge bg-danger me-2';
        badge.innerHTML = `<i class="bi bi-x-circle me-1"></i>${message || 'قطع'}`;
    }
}

// System event handlers
function onSystemStatusUpdated(systemStatus) {
    console.log("System status updated:", systemStatus);
    
    // Update dashboard stats if on home page
    if (getCurrentController() === 'home') {
        updateElement('workers-online', systemStatus.activeWorkers);
        updateElement('jobs-running', systemStatus.activeJobs);
        updateElement('tasks-pending', systemStatus.pendingTasks);
        updateElement('tasks-progress', systemStatus.inProgressTasks);
        updateElement('accounts-active', systemStatus.activeAccounts);
    }
}

function onSystemAlert(alert) {
    console.log("System alert:", alert);
    
    // Show alert notification
    IvaScanner.showNotification(alert.Message, alert.Type);
    
    // Add to alerts list if exists
    addToAlertsList(alert);
}

function onConnectionCountUpdated(count) {
    console.log("Connection count updated:", count);
    
    // Update connection count display if exists
    const element = document.getElementById('connection-count');
    if (element) {
        element.textContent = count;
    }
}

// Worker event handlers
function onWorkerStatusChanged(worker) {
    console.log("Worker status changed:", worker);
    
    // Update worker in table if on workers page
    if (getCurrentController() === 'workers') {
        updateWorkerInTable(worker);
    }
    
    // Update dashboard stats
    if (typeof updateWorkerStats === 'function') {
        updateWorkerStats();
    }
}

function onWorkerRegistered(worker) {
    console.log("Worker registered:", worker);
    
    // Add worker to table if on workers page
    if (getCurrentController() === 'workers') {
        addWorkerToTable(worker);
    }
    
    // Show notification
    IvaScanner.showNotification(`ورکر ${worker.Name} متصل شد`, "success");
}

function onWorkerDeregistered(workerId) {
    console.log("Worker deregistered:", workerId);
    
    // Remove worker from table if on workers page
    if (getCurrentController() === 'workers') {
        removeWorkerFromTable(workerId);
    }
    
    // Show notification
    IvaScanner.showNotification(`ورکر قطع شد`, "warning");
}

function onWorkersStatsUpdated(stats) {
    console.log("Workers stats updated:", stats);
    
    // Update stats on workers page
    if (getCurrentController() === 'workers') {
        updateElement('stat-online', stats.online);
        updateElement('stat-working', stats.working);
        updateElement('stat-offline', stats.offline);
        updateElement('stat-error', stats.error);
    }
}

// Job event handlers
function onJobCreated(job) {
    console.log("Job created:", job);
    
    // Add job to table if on scan page
    if (getCurrentController() === 'scan') {
        addJobToTable(job);
    }
    
    // Show notification
    IvaScanner.showNotification(`اسکن جدید برای کارت ${job.CardNumber} شروع شد`, "info");
}

function onJobStatusChanged(statusUpdate) {
    console.log("Job status changed:", statusUpdate);
    
    // Update job in table if on scan page
    if (getCurrentController() === 'scan') {
        updateJobStatusInTable(statusUpdate.JobId, statusUpdate.Status);
    }
}

function onJobProgressUpdated(progress) {
    console.log("Job progress updated:", progress);
    
    // Update progress bar if on scan page or job details
    updateJobProgressDisplay(progress);
}

function onJobCompleted(completion) {
    console.log("Job completed:", completion);
    
    // Update job status
    if (getCurrentController() === 'scan') {
        updateJobStatusInTable(completion.JobId, "Completed");
    }
    
    // Show notification with result
    const message = completion.Result?.Success 
        ? `اسکن تکمیل شد - CVV معتبر یافت شد!`
        : `اسکن تکمیل شد - CVV معتبر یافت نشد`;
    
    IvaScanner.showNotification(message, completion.Result?.Success ? "success" : "warning");
}

// Task event handlers
function onTaskAssigned(assignment) {
    console.log("Task assigned:", assignment);
    
    // Update real-time task counter
    updateTaskCounters();
}

function onTaskCompleted(completion) {
    console.log("Task completed:", completion);
    
    // Update real-time task counter
    updateTaskCounters();
}

// Log event handlers
function onNewLogEntry(log) {
    console.log("New log entry:", log);
    
    // Add to logs table if on logs page
    if (getCurrentController() === 'logs') {
        addLogToTable(log);
    }
    
    // Show critical/error logs as notifications
    if (log.Level === 'Error' || log.Level === 'Critical') {
        IvaScanner.showNotification(log.Message, "error");
    }
}

function onLogStatsUpdated(stats) {
    console.log("Log stats updated:", stats);
    
    // Update log stats if on logs page
    if (getCurrentController() === 'logs') {
        updateLogStats(stats);
    }
}

// Account event handlers
function onAccountStatusChanged(account) {
    console.log("Account status changed:", account);
    
    // Update account in table if on accounts page
    if (getCurrentController() === 'accounts') {
        updateAccountInTable(account);
    }
}

// Helper functions for UI updates
function updateWorkerInTable(worker) {
    const row = document.querySelector(`tr[data-worker-id="${worker.Id}"]`);
    if (row) {
        // Update specific cells
        const statusCell = row.children[1];
        if (statusCell) {
            statusCell.innerHTML = getStatusBadge(worker.Status, 'worker');
        }
        
        const heartbeatCell = row.children[3];
        if (heartbeatCell) {
            heartbeatCell.innerHTML = `<span class="heartbeat-time">${new Date(worker.LastHeartbeat).toLocaleString('fa-IR')}</span>`;
        }
        
        // Add visual feedback
        row.style.backgroundColor = '#e3f2fd';
        setTimeout(() => {
            row.style.backgroundColor = '';
        }, 2000);
    }
}

function addWorkerToTable(worker) {
    // Implementation depends on existing table structure
    if (typeof refreshWorkers === 'function') {
        refreshWorkers();
    }
}

function removeWorkerFromTable(workerId) {
    const row = document.querySelector(`tr[data-worker-id="${workerId}"]`);
    if (row) {
        row.style.backgroundColor = '#ffebee';
        setTimeout(() => {
            row.remove();
        }, 1000);
    }
}

function updateJobProgressDisplay(progress) {
    // Update progress bar on scan page
    const progressBar = document.querySelector(`#job-${progress.JobId} .progress-bar`);
    if (progressBar) {
        progressBar.style.width = `${progress.ProgressPercentage}%`;
        progressBar.textContent = `${Math.round(progress.ProgressPercentage)}%`;
        
        if (progress.Status === 'Running') {
            progressBar.classList.add('progress-bar-striped', 'progress-bar-animated');
        } else {
            progressBar.classList.remove('progress-bar-striped', 'progress-bar-animated');
        }
    }
    
    // Update job details page if open
    updateJobDetailsProgress(progress);
}

function updateJobDetailsProgress(progress) {
    updateElement('job-progress-percentage', Math.round(progress.ProgressPercentage));
    updateElement('job-completed-tasks', progress.CompletedTasks);
    updateElement('job-failed-tasks', progress.FailedTasks);
    updateElement('job-in-progress-tasks', progress.InProgressTasks);
    
    if (progress.EstimatedTimeRemaining) {
        updateElement('job-time-remaining', formatDuration(progress.EstimatedTimeRemaining));
    }
}

function updateTaskCounters() {
    // Refresh task counters on dashboard
    if (getCurrentController() === 'home') {
        setTimeout(updateDashboardStats, 1000);
    }
}

function addLogToTable(log) {
    // Add new log entry to top of logs table
    const tbody = document.querySelector('#logs-table-body');
    if (tbody) {
        const row = createLogRow(log);
        tbody.insertBefore(row, tbody.firstChild);
        
        // Remove last row if too many
        if (tbody.children.length > 50) {
            tbody.removeChild(tbody.lastChild);
        }
        
        // Highlight new row
        row.style.backgroundColor = getLogLevelColor(log.Level);
        setTimeout(() => {
            row.style.backgroundColor = '';
        }, 3000);
    }
}

function createLogRow(log) {
    const row = document.createElement('tr');
    row.innerHTML = `
        <td><span class="badge bg-${getLogLevelBadgeClass(log.Level)}">${log.Level}</span></td>
        <td>${log.Source}</td>
        <td>${log.Message}</td>
        <td><small>${new Date(log.Timestamp).toLocaleString('fa-IR')}</small></td>
    `;
    return row;
}

function getLogLevelColor(level) {
    const colors = {
        'Error': '#ffebee',
        'Warning': '#fff8e1',
        'Information': '#e8f5e8',
        'Debug': '#f3e5f5'
    };
    return colors[level] || '#f5f5f5';
}

function getLogLevelBadgeClass(level) {
    const classes = {
        'Error': 'danger',
        'Warning': 'warning',
        'Information': 'info',
        'Debug': 'secondary'
    };
    return classes[level] || 'light';
}

function updateLogStats(stats) {
    updateElement('log-stat-total', stats.total);
    updateElement('log-stat-error', stats.error);
    updateElement('log-stat-warning', stats.warning);
    updateElement('log-stat-info', stats.info);
    updateElement('log-stat-debug', stats.debug);
}

function addToAlertsList(alert) {
    // Add alert to alerts container if exists
    const alertsContainer = document.getElementById('alerts-container');
    if (alertsContainer) {
        const alertElement = document.createElement('div');
        alertElement.className = `alert alert-${alert.Type} alert-dismissible fade show`;
        alertElement.innerHTML = `
            ${alert.Message}
            <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
        `;
        
        alertsContainer.appendChild(alertElement);
        
        // Auto-dismiss after 10 seconds
        setTimeout(() => {
            if (alertElement.parentNode) {
                bootstrap.Alert.getOrCreateInstance(alertElement).close();
            }
        }, 10000);
    }
}

// Initialize SignalR when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    initializeSignalR();
});

// Cleanup on page unload
window.addEventListener('beforeunload', function() {
    if (dashboardConnection) {
        dashboardConnection.stop();
    }
});

// Make functions globally available
window.SignalRDashboard = {
    subscribeToJobUpdates,
    unsubscribeFromJobUpdates,
    isConnected: () => isConnected,
    connection: () => dashboardConnection
};

// Log viewer specific functions
function updateLogViewer(logData) {
    // Add new log to the top of the table if we're on page 1
    if (typeof currentPage !== 'undefined' && currentPage === 1) {
        const tbody = document.getElementById('logsTableBody');
        if (tbody) {
            const newRow = createLogRow(logData);
            tbody.insertAdjacentHTML('afterbegin', newRow);
            
            // Remove last row if we have more than 50 rows
            const rows = tbody.querySelectorAll('tr');
            if (rows.length > 50) {
                rows[rows.length - 1].remove();
            }
        }
    }
}

function createLogRow(log) {
    const levelBadge = getLevelBadge(log.level);
    const timeFormatted = formatDateTime(log.timestamp);
    
    return `
        <tr class="log-row" data-level="${log.level}">
            <td>
                <small class="text-muted">${timeFormatted}</small>
            </td>
            <td>${levelBadge}</td>
            <td>
                <small class="text-primary">${log.component || 'System'}</small>
            </td>
            <td>
                <div class="log-message" style="max-width: 400px; overflow: hidden; text-overflow: ellipsis;" 
                     title="${log.message}">
                    ${log.message}
                </div>
            </td>
            <td>
                <small class="text-muted">${log.context || '-'}</small>
            </td>
        </tr>
    `;
}

function updateLogHealthDisplay(health) {
    if (document.getElementById('totalLogsCount')) {
        document.getElementById('totalLogsCount').textContent = health.totalLogs.toLocaleString();
    }
    if (document.getElementById('logsLastHour')) {
        document.getElementById('logsLastHour').textContent = health.logsLastHour.toLocaleString();
    }
    if (document.getElementById('avgLogsPerMinute')) {
        document.getElementById('avgLogsPerMinute').textContent = health.averageLogsPerMinute.toFixed(1);
    }
    if (document.getElementById('topComponent')) {
        document.getElementById('topComponent').textContent = health.topComponents[0] || '-';
    }
}