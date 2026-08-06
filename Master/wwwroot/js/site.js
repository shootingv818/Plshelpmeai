// IVA Scanner Dashboard JavaScript

// Global variables
let refreshIntervals = [];

// Initialize on document ready
document.addEventListener('DOMContentLoaded', function() {
    initializeDashboard();
});

// Initialize dashboard functionality
function initializeDashboard() {
    // Initialize tooltips
    initializeTooltips();
    
    // Initialize real-time updates
    initializeRealTimeUpdates();
    
    // Initialize search functionality
    initializeSearch();
    
    // Initialize modals
    initializeModals();
    
    // Add Persian number formatting
    formatPersianNumbers();
}

// Initialize Bootstrap tooltips
function initializeTooltips() {
    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
    var tooltipList = tooltipTriggerList.map(function (tooltipTriggerEl) {
        return new bootstrap.Tooltip(tooltipTriggerEl);
    });
}

// Initialize real-time updates
function initializeRealTimeUpdates() {
    // Clear existing intervals
    refreshIntervals.forEach(interval => clearInterval(interval));
    refreshIntervals = [];
    
    // Auto-refresh based on current page
    const controller = getCurrentController();
    
    switch (controller) {
        case 'home':
            refreshIntervals.push(setInterval(updateDashboardStats, 30000));
            break;
        case 'workers':
            refreshIntervals.push(setInterval(updateWorkersPage, 15000));
            break;
        case 'scan':
            refreshIntervals.push(setInterval(updateScanPage, 10000));
            break;
        case 'logs':
            refreshIntervals.push(setInterval(updateLogsPage, 20000));
            break;
    }
}

// Get current controller name
function getCurrentController() {
    const path = window.location.pathname;
    if (path === '/' || path.includes('/Home')) return 'home';
    if (path.includes('/Workers')) return 'workers';
    if (path.includes('/Scan')) return 'scan';
    if (path.includes('/Accounts')) return 'accounts';
    if (path.includes('/Logs')) return 'logs';
    if (path.includes('/Settings')) return 'settings';
    return 'unknown';
}

// Update dashboard statistics
function updateDashboardStats() {
    fetch('/api/status')
        .then(response => response.json())
        .then(data => {
            if (!data.error) {
                updateElement('workers-online', data.activeWorkers);
                updateElement('jobs-running', data.activeJobs);
                updateElement('tasks-pending', data.pendingTasks);
                updateElement('tasks-progress', data.inProgressTasks);
                updateElement('accounts-active', data.activeAccounts);
            }
        })
        .catch(error => console.error('Error updating dashboard stats:', error));
}

// Update workers page
function updateWorkersPage() {
    if (typeof refreshWorkers === 'function') {
        refreshWorkers();
    }
}

// Update scan page
function updateScanPage() {
    if (typeof refreshScans === 'function') {
        refreshScans();
    }
}

// Update logs page  
function updateLogsPage() {
    if (typeof refreshLogs === 'function') {
        refreshLogs();
    }
}

// Update element text content
function updateElement(id, value) {
    const element = document.getElementById(id);
    if (element) {
        element.textContent = value;
    }
}

// Initialize search functionality
function initializeSearch() {
    const searchInputs = document.querySelectorAll('[data-search-target]');
    
    searchInputs.forEach(input => {
        input.addEventListener('input', function() {
            const target = this.getAttribute('data-search-target');
            const searchTerm = this.value.toLowerCase();
            const rows = document.querySelectorAll(target + ' tr');
            
            rows.forEach(row => {
                if (row.querySelector('th')) return; // Skip header rows
                
                const text = row.textContent.toLowerCase();
                row.style.display = text.includes(searchTerm) ? '' : 'none';
            });
        });
    });
}

// Initialize modals
function initializeModals() {
    // Add loading state to form submissions
    const forms = document.querySelectorAll('form');
    
    forms.forEach(form => {
        form.addEventListener('submit', function() {
            const submitButton = this.querySelector('button[type="submit"]');
            if (submitButton) {
                submitButton.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>در حال پردازش...';
                submitButton.disabled = true;
            }
        });
    });
}

// Format Persian numbers
function formatPersianNumbers() {
    const persianNumbers = ['۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹'];
    const englishNumbers = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9'];
    
    function toPersianNumbers(str) {
        for (let i = 0; i < englishNumbers.length; i++) {
            str = str.replace(new RegExp(englishNumbers[i], 'g'), persianNumbers[i]);
        }
        return str;
    }
    
    // Apply to elements with persian-numbers class
    const elements = document.querySelectorAll('.persian-numbers');
    elements.forEach(element => {
        element.textContent = toPersianNumbers(element.textContent);
    });
}

// Notification system
function showNotification(message, type = 'info', duration = 5000) {
    const alertTypes = {
        'success': 'alert-success',
        'error': 'alert-danger',
        'warning': 'alert-warning',
        'info': 'alert-info'
    };
    
    const icons = {
        'success': 'bi-check-circle',
        'error': 'bi-exclamation-triangle',
        'warning': 'bi-exclamation-triangle',
        'info': 'bi-info-circle'
    };
    
    const alert = document.createElement('div');
    alert.className = `alert ${alertTypes[type] || alertTypes.info} alert-dismissible fade show`;
    alert.style.position = 'fixed';
    alert.style.top = '20px';
    alert.style.left = '20px';
    alert.style.zIndex = '9999';
    alert.style.minWidth = '300px';
    
    alert.innerHTML = `
        <i class="bi ${icons[type] || icons.info} me-2"></i>
        ${message}
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    `;
    
    document.body.appendChild(alert);
    
    // Auto-remove after duration
    setTimeout(() => {
        if (alert.parentNode) {
            bootstrap.Alert.getOrCreateInstance(alert).close();
        }
    }, duration);
}

// Confirmation dialogs
function confirmAction(message, callback) {
    if (confirm(message)) {
        callback();
    }
}

// Loading state management
function showLoading(element, text = 'در حال بارگذاری...') {
    if (typeof element === 'string') {
        element = document.getElementById(element);
    }
    
    if (element) {
        element.innerHTML = `
            <div class="text-center p-4">
                <div class="spinner-border text-primary mb-2"></div>
                <div>${text}</div>
            </div>
        `;
    }
}

function hideLoading(element) {
    if (typeof element === 'string') {
        element = document.getElementById(element);
    }
    
    if (element) {
        element.innerHTML = '';
    }
}

// Time formatting utilities
function formatTimeAgo(timestamp) {
    const now = new Date();
    const time = new Date(timestamp);
    const diffInSeconds = Math.floor((now - time) / 1000);
    
    if (diffInSeconds < 60) {
        return 'همین الان';
    } else if (diffInSeconds < 3600) {
        const minutes = Math.floor(diffInSeconds / 60);
        return `${minutes} دقیقه پیش`;
    } else if (diffInSeconds < 86400) {
        const hours = Math.floor(diffInSeconds / 3600);
        return `${hours} ساعت پیش`;
    } else {
        const days = Math.floor(diffInSeconds / 86400);
        return `${days} روز پیش`;
    }
}

function formatDuration(milliseconds) {
    const seconds = Math.floor(milliseconds / 1000);
    const minutes = Math.floor(seconds / 60);
    const hours = Math.floor(minutes / 60);
    const days = Math.floor(hours / 24);
    
    if (days > 0) {
        return `${days} روز`;
    } else if (hours > 0) {
        return `${hours} ساعت`;
    } else if (minutes > 0) {
        return `${minutes} دقیقه`;
    } else {
        return `${seconds} ثانیه`;
    }
}

// Progress bar utilities
function updateProgressBar(elementId, percentage, text = null) {
    const progressBar = document.querySelector(`#${elementId} .progress-bar`);
    if (progressBar) {
        progressBar.style.width = `${percentage}%`;
        progressBar.setAttribute('aria-valuenow', percentage);
        if (text) {
            progressBar.textContent = text;
        }
    }
}

// Status badge utilities
function getStatusBadge(status, type = 'worker') {
    const badges = {
        worker: {
            'Online': 'badge bg-success',
            'Working': 'badge bg-warning',
            'Offline': 'badge bg-secondary',
            'Error': 'badge bg-danger'
        },
        job: {
            'Running': 'badge bg-primary',
            'Completed': 'badge bg-success',
            'Failed': 'badge bg-danger',
            'Paused': 'badge bg-warning',
            'Cancelled': 'badge bg-secondary'
        }
    };
    
    const badgeMap = badges[type] || badges.worker;
    return badgeMap[status] || 'badge bg-info';
}

// Copy to clipboard
function copyToClipboard(text) {
    if (navigator.clipboard) {
        navigator.clipboard.writeText(text).then(() => {
            showNotification('کپی شد', 'success', 2000);
        });
    } else {
        // Fallback for older browsers
        const textArea = document.createElement('textarea');
        textArea.value = text;
        document.body.appendChild(textArea);
        textArea.select();
        document.execCommand('copy');
        document.body.removeChild(textArea);
        showNotification('کپی شد', 'success', 2000);
    }
}

// Export data utilities
function exportTableToCSV(tableId, filename = 'data.csv') {
    const table = document.getElementById(tableId);
    if (!table) return;
    
    const rows = table.querySelectorAll('tr');
    const csvContent = [];
    
    rows.forEach(row => {
        const cells = row.querySelectorAll('td, th');
        const rowData = Array.from(cells).map(cell => {
            return `"${cell.textContent.replace(/"/g, '""')}"`;
        });
        csvContent.push(rowData.join(','));
    });
    
    const blob = new Blob([csvContent.join('\n')], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    link.click();
    window.URL.revokeObjectURL(url);
}

// Theme management
function toggleTheme() {
    const body = document.body;
    const currentTheme = body.getAttribute('data-theme');
    const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
    
    body.setAttribute('data-theme', newTheme);
    localStorage.setItem('theme', newTheme);
}

function loadTheme() {
    const savedTheme = localStorage.getItem('theme') || 'light';
    document.body.setAttribute('data-theme', savedTheme);
}

// Initialize theme on page load
loadTheme();

// Cleanup on page unload
window.addEventListener('beforeunload', function() {
    refreshIntervals.forEach(interval => clearInterval(interval));
});

// Global error handler
window.addEventListener('error', function(e) {
    console.error('Global error:', e.error);
    showNotification('خطا در سیستم رخ داده است', 'error');
});

// Make functions globally available
window.IvaScanner = {
    showNotification,
    confirmAction,
    showLoading,
    hideLoading,
    formatTimeAgo,
    formatDuration,
    updateProgressBar,
    getStatusBadge,
    copyToClipboard,
    exportTableToCSV,
    toggleTheme
};