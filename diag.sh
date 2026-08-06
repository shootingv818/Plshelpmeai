#!/bin/bash
# اسکریپت تشخیصی: همه‌چیز را در لاگ ذخیره می‌کند
# استفاده: bash ~/Plshelpmeai/diag.sh   سپس:  cat ~/master-diag.log

LOG="$HOME/master-diag.log"

# همه‌ی خروجی (stdout + stderr) به فایل + نمایش هم‌زمان
exec > >(tee "$LOG") 2>&1

echo "===== 1) which dotnet ====="
which dotnet || echo "dotnet NOT in PATH"

echo "===== 2) dotnet --version ====="
dotnet --version || echo "dotnet version FAILED"

echo "===== 3) رفتن به پوشه Master ====="
cd "$(dirname "${BASH_SOURCE[0]}")/Master" || { echo "cd FAILED"; exit 1; }
echo "PWD = $(pwd)"
ls *.csproj

echo "===== 4) تنظیم متغیرها ====="
export ASPNETCORE_URLS="http://0.0.0.0:5000"
export ASPNETCORE_ENVIRONMENT="Production"
echo "ASPNETCORE_URLS = $ASPNETCORE_URLS"

echo "===== 5) build ====="
dotnet build -c Debug
echo ">>> build exit code = $?"

echo "===== 6) اجرا (حداکثر 20 ثانیه، بعد خودکار متوقف) ====="
timeout 20 dotnet run --no-build
echo ">>> run exit code = $?"

echo "===== پایان تشخیص ====="
