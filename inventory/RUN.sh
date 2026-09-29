#!/usr/bin/env bash
# =====================================================================
# RUN.sh — اجرای کامل سامانه روی لینوکس/مک بدون نیاز به SQL Server
# معادل RUN.ps1 برای ویندوز. ۴ مرحله را خودکار انجام می‌دهد:
#   1) پابلیش کلاینت Blazor
#   2) کپی فایل‌های کلاینت در wwwroot مربوط به API (استقرار تک‌سروره)
#   3) تنظیم SQLite به جای SQL Server (بدون نیاز به نصب SQL Server)
#   4) اجرای API روی http://localhost:5100
#
# استفاده:
#   chmod +x RUN.sh
#   ./RUN.sh
#
# سپس در مرورگر: http://localhost:5100   ورود: admin / admin
# برای توقف: Ctrl+C
# =====================================================================
set -e
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

echo ""
echo "====================================================="
echo "  اجرای سامانه انبار و فروش (تک‌سروره + SQLite)"
echo "====================================================="

# بررسی dotnet
if ! command -v dotnet >/dev/null 2>&1; then
  if [ -x "/var/tmp/dotnet/dotnet" ]; then
    export PATH="/var/tmp/dotnet:$PATH"
  elif [ -x "$HOME/.dotnet/dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
  else
    echo "❌ .NET 8 SDK پیدا نشد. ابتدا نصب کنید: https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
  fi
fi
echo "✓ dotnet: $(dotnet --version)"

# 1) پابلیش کلاینت
echo ""
echo "[1/4] پابلیش کلاینت Blazor WebAssembly..."
dotnet publish src/Inventory.Client -c Release -o publish/client
if [ $? -ne 0 ]; then echo "❌ پابلیش کلاینت شکست خورد"; exit 1; fi

# 2) کپی در wwwroot
echo ""
echo "[2/4] کپی فایل‌های کلاینت در wwwroot مربوط به API..."
DEST="$SCRIPT_DIR/src/Inventory.Api/wwwroot"
rm -rf "$DEST"
mkdir -p "$DEST"
cp -R publish/client/wwwroot/* "$DEST"

# 3) تنظیم SQLite
echo ""
echo "[3/4] تنظیم SQLite به جای SQL Server..."
export Database__Provider="Sqlite"
export ConnectionStrings__Default="Data Source=inventory.db"
export ASPNETCORE_ENVIRONMENT="Production"
# غیرفعال کردن HTTPS داخلی برای تست سریع (اختیاری — اعلان اندروید روی HTTP کار نمی‌کند اما https داخلی روی 5443 می‌ماند)
# برای فعال نگه داشتن HTTPS داخلی: حذف Https__Enabled
export Https__Enabled="true"
# پورت‌ها
export PORT="5100"
export HTTPS_PORT="5443"

# 4) اجرای API
echo ""
echo "[4/4] اجرای API روی http://localhost:5100"
echo ""
echo "  در مرورگر باز کنید:  http://localhost:5100"
echo "  ورود پیش‌فرض:        admin / admin"
echo "  (برای توقف: Ctrl+C)"
echo ""
cd src/Inventory.Api
dotnet run
