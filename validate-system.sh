#!/bin/bash

# IVA Scanner System Validation Script
# این اسکریپت سیستم را بدون build کردن validate می‌کند

set -e

echo "🔍 IVA Scanner System Validation"
echo "================================="

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    local status=$1
    local message=$2
    if [ "$status" = "OK" ]; then
        echo -e "${GREEN}✅ $message${NC}"
    elif [ "$status" = "WARNING" ]; then
        echo -e "${YELLOW}⚠️  $message${NC}"
    elif [ "$status" = "ERROR" ]; then
        echo -e "${RED}❌ $message${NC}"
    elif [ "$status" = "INFO" ]; then
        echo -e "${BLUE}ℹ️  $message${NC}"
    fi
}

# Initialize counters
CHECKS_TOTAL=0
CHECKS_PASSED=0
CHECKS_WARNINGS=0
CHECKS_FAILED=0

# Function to run check
run_check() {
    local check_name=$1
    local check_command=$2
    
    CHECKS_TOTAL=$((CHECKS_TOTAL + 1))
    echo -n "Checking $check_name... "
    
    if eval "$check_command" > /dev/null 2>&1; then
        print_status "OK" "$check_name"
        CHECKS_PASSED=$((CHECKS_PASSED + 1))
        return 0
    else
        print_status "ERROR" "$check_name failed"
        CHECKS_FAILED=$((CHECKS_FAILED + 1))
        return 1
    fi
}

echo ""
print_status "INFO" "1. بررسی ساختار پروژه"
echo "-------------------------------"

# Check solution file
run_check "Solution file exists" "test -f IvaScanner.sln"

# Check project directories
run_check "Master project directory" "test -d Master"
run_check "Worker project directory" "test -d Worker" 
run_check "Core project directory" "test -d IvaScanner.Core"

# Check project files
run_check "Master project file" "test -f Master/IvaScanner.Master.csproj"
run_check "Worker project file" "test -f Worker/IvaScanner.Worker.csproj"
run_check "Core project file" "test -f IvaScanner.Core/IvaScanner.Core.csproj"

echo ""
print_status "INFO" "2. بررسی فایل‌های کلیدی Master"
echo "-------------------------------"

# Master key files
run_check "Master Program.cs" "test -f Master/Program.cs"
run_check "Master DbContext" "test -f Master/Data/MasterDbContext.cs"
run_check "Master Controllers directory" "test -d Master/Controllers"
run_check "Master Services directory" "test -d Master/Services"
run_check "Master Views directory" "test -d Master/Views"

# Check specific controllers
run_check "Home Controller" "test -f Master/Controllers/HomeController.cs"
run_check "API Controller" "test -f Master/Controllers/ApiController.cs"
run_check "Workers Controller" "test -f Master/Controllers/WorkersController.cs"
run_check "Scan Controller" "test -f Master/Controllers/ScanController.cs"

# Check services
run_check "Worker Service" "test -f Master/Services/WorkerService.cs"
run_check "Task Distribution Service" "test -f Master/Services/TaskDistributionService.cs"
run_check "Scan Orchestrator" "test -f Master/Services/ScanOrchestrator.cs"
run_check "Error Handling Service" "test -f Master/Services/ErrorHandlingService.cs"

# Check SignalR
run_check "SignalR Dashboard Hub" "test -f Master/Hubs/DashboardHub.cs"

# Check Middleware
run_check "Exception Handler Middleware" "test -f Master/Middleware/GlobalExceptionHandlerMiddleware.cs"

echo ""
print_status "INFO" "3. بررسی فایل‌های کلیدی Worker"
echo "-------------------------------"

# Worker key files
run_check "Worker Program.cs" "test -f Worker/Program.cs"
run_check "Worker Services directory" "test -d Worker/Services"
run_check "Worker Configuration directory" "test -d Worker/Configuration"

# Check worker services
run_check "Worker State Manager" "test -f Worker/Services/IWorkerStateManager.cs"
run_check "Master API Client" "test -f Worker/Services/IMasterApiClient.cs"
run_check "Task Executor" "test -f Worker/Services/ITaskExecutor.cs"
run_check "IVA Worker Client" "test -f Worker/Services/IIvaWorkerClient.cs"

# Check deployment files
run_check "Worker startup script" "test -f Worker/start-worker.sh"
run_check "Worker install script" "test -f Worker/install-service.sh"
run_check "Worker systemd service" "test -f Worker/iva-worker.service"

echo ""
print_status "INFO" "4. بررسی فایل‌های Configuration"
echo "-------------------------------"

# Configuration files
run_check "Master appsettings.json" "test -f Master/appsettings.json"
run_check "Master appsettings.Development.json" "test -f Master/appsettings.Development.json"
run_check "Worker appsettings.json" "test -f Worker/appsettings.json"
run_check "Worker Configuration class" "test -f Worker/Configuration/WorkerConfiguration.cs"

echo ""
print_status "INFO" "5. بررسی Models و DTOs"
echo "-------------------------------"

# Core models
run_check "IVA Models" "test -f IvaScanner.Core/Models/IvaModels.cs"
run_check "Master Models" "test -f IvaScanner.Core/Models/MasterModels.cs"

echo ""
print_status "INFO" "6. بررسی UI و Assets"
echo "-------------------------------"

# UI files
run_check "Layout template" "test -f Master/Views/Shared/_Layout.cshtml"
run_check "Home Index view" "test -f Master/Views/Home/Index.cshtml"
run_check "Workers Index view" "test -f Master/Views/Workers/Index.cshtml"
run_check "Scan views directory" "test -d Master/Views/Scan"

# Static files
run_check "CSS files" "test -f Master/wwwroot/css/site.css"
run_check "JavaScript files" "test -f Master/wwwroot/js/site.js"
run_check "SignalR Dashboard JS" "test -f Master/wwwroot/js/signalr-dashboard.js"

echo ""
print_status "INFO" "7. بررسی Documentation"
echo "-------------------------------"

# Documentation files
run_check "Main README" "test -f README.md"
run_check "Test Plan" "test -f TEST_PLAN.md"
run_check "Deployment Guide" "test -f DEPLOYMENT_GUIDE.md"
run_check "Worker README" "test -f Worker/README.md"

echo ""
print_status "INFO" "8. بررسی Syntax کلیدی"
echo "-------------------------------"

# Basic syntax checks
check_json_syntax() {
    local file=$1
    python3 -m json.tool "$file" > /dev/null 2>&1
}

run_check "Master appsettings.json syntax" "check_json_syntax Master/appsettings.json"
run_check "Worker appsettings.json syntax" "check_json_syntax Worker/appsettings.json"

# Check for common C# syntax issues
check_csharp_basic() {
    local file=$1
    # Check for basic bracket matching
    if [ -f "$file" ]; then
        local open_braces=$(grep -o '{' "$file" | wc -l)
        local close_braces=$(grep -o '}' "$file" | wc -l)
        [ "$open_braces" -eq "$close_braces" ]
    else
        return 1
    fi
}

run_check "Master Program.cs basic syntax" "check_csharp_basic Master/Program.cs"
run_check "Worker Program.cs basic syntax" "check_csharp_basic Worker/Program.cs"

echo ""
print_status "INFO" "9. بررسی پیکربندی Docker (اختیاری)"
echo "-------------------------------"

# Docker files (optional)
if [ -f "Dockerfile" ]; then
    run_check "Dockerfile exists" "test -f Dockerfile"
else
    print_status "WARNING" "Dockerfile not found (optional)"
    CHECKS_WARNINGS=$((CHECKS_WARNINGS + 1))
fi

if [ -f "docker-compose.yml" ]; then
    run_check "Docker Compose file" "test -f docker-compose.yml"
else
    print_status "WARNING" "docker-compose.yml not found (optional)"
    CHECKS_WARNINGS=$((CHECKS_WARNINGS + 1))
fi

echo ""
print_status "INFO" "10. بررسی Scripts اجرایی"
echo "-------------------------------"

# Check execute permissions
run_check "Worker start script executable" "test -x Worker/start-worker.sh"
run_check "Worker install script executable" "test -x Worker/install-service.sh"
run_check "System validation script executable" "test -x validate-system.sh"

echo ""
print_status "INFO" "11. بررسی Dependencies و Packages"
echo "-------------------------------"

# Check for critical dependencies in project files
check_dependency() {
    local project_file=$1
    local dependency=$2
    grep -q "$dependency" "$project_file"
}

run_check "Master EF dependency" "check_dependency Master/IvaScanner.Master.csproj Microsoft.EntityFrameworkCore"
run_check "Master SignalR dependency" "check_dependency Master/IvaScanner.Master.csproj Microsoft.AspNetCore.SignalR"
run_check "Worker Hosting dependency" "check_dependency Worker/IvaScanner.Worker.csproj Microsoft.Extensions.Hosting"

echo ""
print_status "INFO" "12. تحلیل نهایی"
echo "================"

# Final summary
echo "📊 خلاصه نتایج:"
echo "   کل چک‌ها: $CHECKS_TOTAL"
echo "   موفق: $CHECKS_PASSED"
echo "   هشدار: $CHECKS_WARNINGS"
echo "   ناموفق: $CHECKS_FAILED"
echo ""

# Calculate success rate
if [ $CHECKS_TOTAL -gt 0 ]; then
    SUCCESS_RATE=$(( (CHECKS_PASSED * 100) / CHECKS_TOTAL ))
    echo "نرخ موفقیت: ${SUCCESS_RATE}%"
    
    if [ $SUCCESS_RATE -ge 95 ]; then
        print_status "OK" "سیستم آماده برای deployment است! 🚀"
        exit 0
    elif [ $SUCCESS_RATE -ge 85 ]; then
        print_status "WARNING" "سیستم تقریباً آماده است، چند مسئله جزئی وجود دارد"
        exit 1
    elif [ $SUCCESS_RATE -ge 70 ]; then
        print_status "WARNING" "سیستم نیاز به رفع برخی مسائل دارد"
        exit 2
    else
        print_status "ERROR" "سیستم نیاز به بازبینی اساسی دارد"
        exit 3
    fi
else
    print_status "ERROR" "هیچ چکی اجرا نشد!"
    exit 4
fi