// Remote Server Management (vanilla JS - no jQuery dependency)
window.RemoteServerManager = {
    currentJobId: null,
    progressInterval: null,

    init: function () {
        this.bindEvents();
        this.initSignalR();
        this.startPeriodicRefresh();
    },

    bindEvents: function () {
        document.addEventListener('click', (e) => {
            const testBtn = e.target.closest('.test-connection');
            if (testBtn) {
                this.testConnection(testBtn.dataset.serverId);
                return;
            }

            const deployBtn = e.target.closest('.deploy-workers');
            if (deployBtn) {
                this.showDeployModal(deployBtn.dataset.serverId);
                return;
            }

            const manageBtn = e.target.closest('.manage-workers');
            if (manageBtn) {
                this.manageWorkers(manageBtn.dataset.serverId, manageBtn.dataset.action);
                return;
            }
        });

        document.getElementById('startDeployment')?.addEventListener('click', () => this.startDeployment());
        document.getElementById('cancelDeployment')?.addEventListener('click', () => this.cancelDeployment());
        document.getElementById('refresh-servers')?.addEventListener('click', () => this.refreshServers());
    },

    initSignalR: function () {
        if (window.dashboardConnection) {
            window.dashboardConnection.on('ServerStatusUpdate', (data) => this.updateServerStatus(data));
            window.dashboardConnection.on('DeploymentProgress', (data) => this.updateDeploymentProgress(data));
        }
    },

    startPeriodicRefresh: function () {
        setInterval(() => this.refreshServerStatuses(), 30000);
    },

    testConnection: function (serverId) {
        const button = document.querySelector(`.test-connection[data-server-id="${serverId}"]`);
        const originalHtml = button ? button.innerHTML : '';

        if (button) {
            button.innerHTML = '<i class="bi bi-hourglass-split"></i> تست...';
            button.disabled = true;
        }

        fetch('/RemoteServer/TestConnection', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: 'serverId=' + encodeURIComponent(serverId)
        })
            .then(r => r.json())
            .then(result => {
                if (result.success) {
                    this.showNotification('success', 'اتصال موفق!');
                    this.updateServerRow(serverId, result);
                } else {
                    this.showNotification('error', `اتصال ناموفق: ${result.errorMessage}`);
                }
            })
            .catch(() => this.showNotification('error', 'خطا در تست اتصال'))
            .finally(() => {
                if (button) {
                    button.innerHTML = originalHtml;
                    button.disabled = false;
                }
            });
    },

    showDeployModal: function (serverId) {
        document.getElementById('deployServerId').value = serverId;
        new bootstrap.Modal(document.getElementById('deployModal')).show();
    },

    startDeployment: function () {
        const serverId = document.getElementById('deployServerId').value;
        const workerCount = document.getElementById('workerCount').value;
        const customConfig = document.getElementById('customConfig').value;
        const startImmediately = document.getElementById('startImmediately').checked;

        let configuration = {};
        if (customConfig) {
            try {
                configuration = JSON.parse(customConfig);
            } catch (e) {
                this.showNotification('error', 'فرمت JSON نامعتبر');
                return;
            }
        }

        const request = {
            serverId: serverId,
            workerCount: parseInt(workerCount, 10),
            configuration: configuration,
            startImmediately: startImmediately
        };

        fetch('/RemoteServer/DeployWorkers', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(request)
        })
            .then(r => r.json())
            .then(result => {
                if (result.success) {
                    bootstrap.Modal.getInstance(document.getElementById('deployModal'))?.hide();
                    this.currentJobId = result.jobId;
                    this.showProgressModal();
                    this.startProgressMonitoring();
                    this.showNotification('info', 'Deploy آغاز شد...');
                } else {
                    this.showNotification('error', `خطا در شروع Deploy: ${result.errorMessage}`);
                }
            })
            .catch(() => this.showNotification('error', 'خطا در ارسال درخواست Deploy'));
    },

    showProgressModal: function () {
        const bar = document.getElementById('deployProgress');
        bar.style.width = '0%';
        bar.textContent = '0%';
        document.getElementById('currentStep').textContent = 'در انتظار شروع...';
        document.getElementById('deploymentSteps').innerHTML = '';
        document.getElementById('cancelDeployment').style.display = '';
        new bootstrap.Modal(document.getElementById('progressModal')).show();
    },

    startProgressMonitoring: function () {
        if (this.progressInterval) clearInterval(this.progressInterval);
        this.progressInterval = setInterval(() => {
            if (this.currentJobId) this.checkDeploymentProgress();
        }, 2000);
    },

    checkDeploymentProgress: function () {
        fetch(`/RemoteServer/DeploymentProgress/${this.currentJobId}`)
            .then(r => r.json())
            .then(progress => this.updateDeploymentProgress(progress))
            .catch(() => console.error('Error checking deployment progress'));
    },

    updateDeploymentProgress: function (progress) {
        const percentage = Math.round(progress.progress || 0);
        const bar = document.getElementById('deployProgress');
        bar.style.width = `${percentage}%`;
        bar.textContent = `${percentage}%`;

        if (progress.currentStep) {
            document.getElementById('currentStep').textContent = progress.currentStep;
        }

        if (progress.steps && progress.steps.length > 0) {
            document.getElementById('deploymentSteps').innerHTML = progress.steps.map(step => `
                <div class="d-flex align-items-center mb-2">
                    <i class="${this.getStepStatusIcon(step.status)} ${this.getStepStatusClass(step.status)} me-2"></i>
                    <span class="${this.getStepStatusClass(step.status)}">${step.name}</span>
                </div>
            `).join('');
        }

        if (progress.status === 'Completed') this.deploymentCompleted(true);
        else if (progress.status === 'Failed') this.deploymentCompleted(false);
    },

    deploymentCompleted: function (success) {
        if (this.progressInterval) {
            clearInterval(this.progressInterval);
            this.progressInterval = null;
        }

        document.getElementById('cancelDeployment').style.display = 'none';

        if (success) {
            this.showNotification('success', 'Deploy با موفقیت تکمیل شد!');
            setTimeout(() => {
                bootstrap.Modal.getInstance(document.getElementById('progressModal'))?.hide();
                this.refreshServers();
            }, 3000);
        } else {
            this.showNotification('error', 'Deploy با شکست مواجه شد');
        }

        this.currentJobId = null;
    },

    cancelDeployment: function () {
        if (!this.currentJobId) return;
        if (!confirm('آیا از لغو Deploy اطمینان دارید؟')) return;

        fetch(`/RemoteServer/CancelDeployment/${this.currentJobId}`, { method: 'POST' })
            .then(r => r.json())
            .then(result => {
                if (result.success) {
                    this.showNotification('info', 'Deploy لغو شد');
                    bootstrap.Modal.getInstance(document.getElementById('progressModal'))?.hide();
                }
            });
    },

    manageWorkers: function (serverId, action) {
        const actionText = { start: 'راه‌اندازی', stop: 'توقف', restart: 'راه‌اندازی مجدد', remove: 'حذف' };
        if (!confirm(`آیا از ${actionText[action]} تمام Workerهای این سرور اطمینان دارید؟`)) return;

        fetch('/RemoteServer/ManageWorkers', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: `serverId=${encodeURIComponent(serverId)}&action=${encodeURIComponent(action)}`
        })
            .then(r => r.json())
            .then(result => {
                if (result.success) {
                    this.showNotification('success', result.message);
                    this.refreshServers();
                } else {
                    this.showNotification('error', result.message);
                }
            })
            .catch(() => this.showNotification('error', 'خطا در انجام عملیات'));
    },

    refreshServers: function () {
        location.reload();
    },

    refreshServerStatuses: function () {
        // Placeholder for individual server status polling if needed later
    },

    updateServerStatus: function (data) {
        const row = document.querySelector(`tr[data-server-id="${data.serverId}"]`);
        if (!row) return;

        const statusCell = row.querySelector('.server-status');
        if (statusCell) {
            statusCell.className = `badge server-status ${this.getStatusBadgeClass(data.status)}`;
            statusCell.textContent = this.getStatusText(data.status);
        }

        if (data.activeWorkers !== undefined) {
            const wc = row.querySelector('.worker-count');
            if (wc) wc.textContent = data.activeWorkers;
        }
    },

    updateServerRow: function (serverId, data) {
        if (data.success) {
            this.updateServerStatus({ serverId, status: 'Online' });
        }
    },

    getStepStatusIcon: function (status) {
        switch (status) {
            case 'Completed': return 'bi bi-check-circle-fill';
            case 'Running': return 'bi bi-arrow-repeat';
            case 'Failed': return 'bi bi-x-circle-fill';
            case 'Skipped': return 'bi bi-dash-circle';
            default: return 'bi bi-clock';
        }
    },

    getStepStatusClass: function (status) {
        switch (status) {
            case 'Completed': return 'text-success';
            case 'Running': return 'text-primary';
            case 'Failed': return 'text-danger';
            case 'Skipped': return 'text-secondary';
            default: return 'text-muted';
        }
    },

    getStatusBadgeClass: function (status) {
        switch (status) {
            case 'Online': return 'bg-success';
            case 'Offline': return 'bg-secondary';
            case 'Deploying': return 'bg-warning';
            case 'Error': return 'bg-danger';
            case 'Maintenance': return 'bg-info';
            default: return 'bg-light text-dark';
        }
    },

    getStatusText: function (status) {
        switch (status) {
            case 'Online': return 'آنلاین';
            case 'Offline': return 'آفلاین';
            case 'Deploying': return 'در حال Deploy';
            case 'Error': return 'خطا';
            case 'Maintenance': return 'تعمیر';
            default: return 'نامشخص';
        }
    },

    showNotification: function (type, message) {
        if (window.IvaScanner && window.IvaScanner.showNotification) {
            window.IvaScanner.showNotification(message, type);
            return;
        }

        const alertClass = { success: 'alert-success', error: 'alert-danger', warning: 'alert-warning', info: 'alert-info' }[type] || 'alert-info';
        const notification = document.createElement('div');
        notification.className = `alert ${alertClass} alert-dismissible fade show position-fixed`;
        notification.style.top = '20px';
        notification.style.left = '20px';
        notification.style.zIndex = '9999';
        notification.style.minWidth = '300px';
        notification.innerHTML = `${message}<button type="button" class="btn-close" data-bs-dismiss="alert"></button>`;

        document.body.appendChild(notification);
        setTimeout(() => bootstrap.Alert.getOrCreateInstance(notification).close(), 5000);
    }
};
