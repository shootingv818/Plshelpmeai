#!/bin/bash
# تشخیص وضعیت نصب dotnet
# استفاده: bash ~/Plshelpmeai/dotnet-info.sh
LOG="$HOME/dotnet-info.log"
{
echo "===== which dotnet ====="
which dotnet
echo ""
echo "===== readlink -f (مسیر واقعی) ====="
readlink -f /usr/bin/dotnet
echo ""
echo "===== ls -la /usr/bin/dotnet ====="
ls -la /usr/bin/dotnet
echo ""
echo "===== DOTNET_ROOT env ====="
echo "DOTNET_ROOT=[$DOTNET_ROOT]"
echo ""
echo "===== محتوای /usr/share/dotnet ====="
ls -la /usr/share/dotnet 2>&1
echo ""
echo "===== /usr/share/dotnet/sdk ====="
ls /usr/share/dotnet/sdk 2>&1
echo ""
echo "===== /usr/share/dotnet/shared ====="
ls /usr/share/dotnet/shared 2>&1
echo ""
echo "===== ~/.dotnet ====="
ls -la "$HOME/.dotnet" 2>&1
echo ""
echo "===== ~/.dotnet/sdk ====="
ls "$HOME/.dotnet/sdk" 2>&1
echo ""
echo "===== جستجوی همه‌ی پوشه‌های sdk مربوط به dotnet ====="
find / -maxdepth 6 -type d -name sdk -path "*dotnet*" 2>/dev/null
echo ""
echo "===== dotnet --info (مستقیم) ====="
dotnet --info 2>&1
echo ">>> exit code = $?"
echo ""
echo "===== فضای دیسک ====="
df -h /
} > "$LOG" 2>&1
cat "$LOG"
