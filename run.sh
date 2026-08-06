#!/bin/bash
# اجرای Master server با تنظیمات درست
# استفاده: bash ~/Plshelpmeai/run.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/Master"

# ساخت آدرس با scheme به‌صورت مطمئن (بدون مشکل کپی/پیست)
SCHEME="http"
HOSTPORT="0.0.0.0:5000"
export ASPNETCORE_URLS="${SCHEME}://${HOSTPORT}"
export ASPNETCORE_ENVIRONMENT="Production"

echo "=================================================="
echo " Starting IVA Scanner Master"
echo " URL: $ASPNETCORE_URLS"
echo " Logging to: $HOME/master-run.log"
echo "=================================================="

# اجرا + ذخیره کل خروجی در فایل لاگ
dotnet run 2>&1 | tee "$HOME/master-run.log"
