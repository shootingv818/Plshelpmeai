// Remote Server Management JavaScript
window.RemoteServerManager = {
    currentJobId: null,
    progressInterval: null,

    init: function() {
        this.bindEvents();
        this.initSignalR();
        this.startPeriodicRefresh();
    },

    bindEvents: function() {
        // Test connection buttons
        $(document).on('click', '.test-connection', function() {
            const serverId = $(this).data('server-id');
            RemoteServerManager.testConnection(serverId);
        });

        // Deploy workers buttons
        $(document).on('click', '.deploy-workers', function() {
            const serverId = $(this).data('server-id');
            RemoteServerManager.showDeployModal(serverId);
        });

        // Manage workers buttons
        $(document).on('click', '.manage-workers', function() {
            const serverId = $(this).data('server-id');
            const action = $(this).data('action');
            RemoteServerManager.manageWorkers(serverId, action);
        });

        // Deploy form submission
        $('#startDeployment').click(function() {
            RemoteServerManager.startDeployment();
        });

        // Cancel deployment
        $('#cancelDeployment').click(function() {
            RemoteServerManager.cancelDeployment();
        });

        // Refresh servers button
        $('#refresh-servers').click(function() {
            RemoteServerManager.refreshServers();
        });

        // DataTables initialization
        if ($.fn.DataTable) {
            $('#serversTable').DataTable({
                language: {
                    url: '/lib/datatables/Persian.json'
                },
                order: [[0, 'asc']],
                pageLength: 25,
                responsive: true
            });
        }
    },

    initSignalR: function() {
        // Listen for server status updates
        if (window.dashboardConnection) {
            window.dashboardConnection.on('ServerStatusUpdate', function(data) {
                RemoteServerManager.updateServerStatus(data);
            });

            window.dashboardConnection.on('DeploymentProgress', function(data) {
                RemoteServerManager.updateDeploymentProgress(data);
            });
        }
    },

    startPeriodicRefresh: function() {
        // Refresh server status every 30 seconds
        setInterval(() => {
            this.refreshServerStatuses();
        }, 30000);
    },

    testConnection: function(serverId) {
        const button = $(`.test-connection[data-server-id="${serverId}"]`);
        const originalText = button.html();
        
        button.html('<i class="fas fa-spinner fa-spin"></i> تست...').prop('disabled', true);

        $.ajax({
            url: '/RemoteServer/TestConnection',
            type: 'POST',
            data: { serverId: serverId },
            success: function(result) {
                if (result.success) {
                    RemoteServerManager.showNotification('success', 'اتصال موفق!');
                    RemoteServerManager.updateServerRow(serverId, result);
                } else {
                    RemoteServerManager.showNotification('error', 
                        `اتصال ناموفق: ${result.errorMessage}`);
                }
            },
            error: function() {
                RemoteServerManager.showNotification('error', 'خطا در تست اتصال');
            },
            complete: function() {
                button.html(originalText).prop('disabled', false);
            }
        });
    },

    showDeployModal: function(serverId) {
        $('#deployServerId').val(serverId);
        $('#deployModal').modal('show');
    },

    startDeployment: function() {
        const serverId = $('#deployServerId').val();
        const workerCount = $('#workerCount').val();
        const customConfig = $('#customConfig').val();
        const startImmediately = $('#startImmediately').is(':checked');

        let configuration = {};
        if (customConfig) {
            try {
                configuration = JSON.parse(customConfig);
            } catch (e) {
                RemoteServerManager.showNotification('error', 'فرمت JSON نامعتبر');
                return;
            }
        }

        const request = {
            serverId: serverId,
            workerCount: parseInt(workerCount),
            configuration: configuration,
            startImmediately: startImmediately
        };

        $.ajax({
            url: '/RemoteServer/Deploy',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(request),
            success: function(result) {
                if (result.success) {
                    $('#deployModal').modal('hide');
                    RemoteServerManager.currentJobId = result.jobId;
                    RemoteServerManager.showProgressModal();
                    RemoteServerManager.startProgressMonitoring();
                    RemoteServerManager.showNotification('info', 'Deploy آغاز شد...');
                } else {
                    RemoteServerManager.showNotification('error', 
                        `خطا در شروع Deploy: ${result.errorMessage}`);
                }
            },
            error: function() {
                RemoteServerManager.showNotification('error', 'خطا در ارسال درخواست Deploy');
            }
        });
    },

    showProgressModal: function() {
        $('#deployProgress').css('width', '0%').text('0%');
        $('#currentStep').text('در انتظار شروع...');
        $('#deploymentSteps').empty();
        $('#cancelDeployment').show();
        $('#progressModal').modal('show');
    },

    startProgressMonitoring: function() {
        if (this.progressInterval) {
            clearInterval(this.progressInterval);
        }

        this.progressInterval = setInterval(() => {
            if (this.currentJobId) {
                this.checkDeploymentProgress();
            }
        }, 2000); // Check every 2 seconds
    },

    checkDeploymentProgress: function() {
        $.ajax({
            url: `/RemoteServer/DeploymentProgress/${this.currentJobId}`,
            type: 'GET',
            success: function(progress) {
                RemoteServerManager.updateDeploymentProgress(progress);
            },
            error: function() {
                console.error('Error checking deployment progress');
            }
        });
    },

    updateDeploymentProgress: function(progress) {
        const percentage = Math.round(progress.progress || 0);
        
        $('#deployProgress').css('width', `${percentage}%`).text(`${percentage}%`);
        
        if (progress.currentStep) {
            $('#currentStep').text(progress.currentStep);
        }

        // Update steps list
        if (progress.steps && progress.steps.length > 0) {
            let stepsHtml = '';
            progress.steps.forEach(step => {
                const statusIcon = this.getStepStatusIcon(step.status);
                const statusClass = this.getStepStatusClass(step.status);
                
                stepsHtml += `
                    <div class="d-flex align-items-center mb-2">
                        <i class="${statusIcon} ${statusClass} mr-2"></i>
                        <span class="${statusClass}">${step.name}</span>
                    </div>
                `;
            });
            $('#deploymentSteps').html(stepsHtml);
        }

        // Check if completed or failed
        if (progress.status === 'Completed') {
            this.deploymentCompleted(true);
        } else if (progress.status === 'Failed') {
            this.deploymentCompleted(false);
        }
    },

    deploymentCompleted: function(success) {
        if (this.progressInterval) {
            clearInterval(this.progressInterval);
            this.progressInterval = null;
        }

        $('#cancelDeployment').hide();
        
        if (success) {
            RemoteServerManager.showNotification('success', 'Deploy با موفقیت تکمیل شد!');
            setTimeout(() => {
                $('#progressModal').modal('hide');
                RemoteServerManager.refreshServers();
            }, 3000);
        } else {
            RemoteServerManager.showNotification('error', 'Deploy با شکست مواجه شد');
        }

        this.currentJobId = null;
    },

    cancelDeployment: function() {
        if (!this.currentJobId) return;

        if (confirm('آیا از لغو Deploy اطمینان دارید؟')) {
            $.ajax({
                url: `/RemoteServer/CancelDeployment/${this.currentJobId}`,
                type: 'POST',
                success: function(result) {
                    if (result.success) {
                        RemoteServerManager.showNotification('info', 'Deploy لغو شد');
                        $('#progressModal').modal('hide');
                    }
                }
            });
        }
    },

    manageWorkers: function(serverId, action) {
        const actionText = {
            'start': 'راه‌اندازی',
            'stop': 'توقف',
            'restart': 'راه‌اندازی مجدد',
            'remove': 'حذف'
        };

        const confirmText = `آیا از ${actionText[action]} تمام Workerهای این سرور اطمینان دارید؟`;
        
        if (!confirm(confirmText)) return;

        $.ajax({
            url: '/RemoteServer/ManageWorkers',
            type: 'POST',
            data: {
                serverId: serverId,
                action: action
            },
            success: function(result) {
                if (result.success) {
                    RemoteServerManager.showNotification('success', result.message);
                    RemoteServerManager.refreshServers();
                } else {
                    RemoteServerManager.showNotification('error', result.message);
                }
            },
            error: function() {
                RemoteServerManager.showNotification('error', 'خطا در انجام عملیات');
            }
        });
    },

    refreshServers: function() {
        location.reload();
    },

    refreshServerStatuses: function() {
        // Get all visible server IDs and refresh their status
        $('[data-server-id]').each(function() {
            const serverId = $(this).data('server-id');
            // Optionally implement individual server status updates
        });
    },

    updateServerStatus: function(data) {
        const row = $(`tr[data-server-id="${data.serverId}"]`);
        if (row.length === 0) return;

        // Update status badge
        const statusCell = row.find('.server-status');
        statusCell.removeClass().addClass(`badge server-status ${this.getStatusBadgeClass(data.status)}`);
        statusCell.text(this.getStatusText(data.status));

        // Update worker count if provided
        if (data.activeWorkers !== undefined) {
            row.find('.worker-count').text(data.activeWorkers);
        }
    },

    updateServerRow: function(serverId, data) {
        const row = $(`tr[data-server-id="${serverId}"]`);
        if (row.length === 0) return;

        // Update status to online if connection successful
        if (data.success) {
            this.updateServerStatus({
                serverId: serverId,
                status: 'Online'
            });
        }
    },

    getStepStatusIcon: function(status) {
        switch (status) {
            case 'Completed': return 'fas fa-check-circle';
            case 'Running': return 'fas fa-spinner fa-spin';
            case 'Failed': return 'fas fa-times-circle';
            case 'Skipped': return 'fas fa-minus-circle';
            default: return 'fas fa-clock';
        }
    },

    getStepStatusClass: function(status) {
        switch (status) {
            case 'Completed': return 'text-success';
            case 'Running': return 'text-primary';
            case 'Failed': return 'text-danger';
            case 'Skipped': return 'text-secondary';
            default: return 'text-muted';
        }
    },

    getStatusBadgeClass: function(status) {
        switch (status) {
            case 'Online': return 'badge-success';
            case 'Offline': return 'badge-secondary';
            case 'Deploying': return 'badge-warning';
            case 'Error': return 'badge-danger';
            case 'Maintenance': return 'badge-info';
            default: return 'badge-light';
        }
    },

    getStatusText: function(status) {
        switch (status) {
            case 'Online': return 'آنلاین';
            case 'Offline': return 'آفلاین';
            case 'Deploying': return 'در حال Deploy';
            case 'Error': return 'خطا';
            case 'Maintenance': return 'تعمیر';
            default: return 'نامشخص';
        }
    },

    showNotification: function(type, message) {
        // Use existing notification system or create simple alert
        if (window.showNotification) {
            window.showNotification(type, message);
        } else {
            const alertClass = {
                'success': 'alert-success',
                'error': 'alert-danger',
                'warning': 'alert-warning',
                'info': 'alert-info'
            }[type] || 'alert-info';

            const notification = $(`
                <div class="alert ${alertClass} alert-dismissible fade show position-fixed" 
                     style="top: 20px; right: 20px; z-index: 9999; min-width: 300px;">
                    ${message}
                    <button type="button" class="close" data-dismiss="alert">
                        <span>&times;</span>
                    </button>
                </div>
            `);

            $('body').append(notification);

            // Auto hide after 5 seconds
            setTimeout(() => {
                notification.alert('close');
            }, 5000);
        }
    }
};

// Initialize when document is ready
$(document).ready(function() {
    if (typeof RemoteServerManager !== 'undefined') {
        // Initialize will be called from the page
    }
});

// Export for global usage
window.RemoteServerManager = RemoteServerManager;