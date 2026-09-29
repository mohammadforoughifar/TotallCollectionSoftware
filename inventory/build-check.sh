#!/usr/bin/env bash
# بیلد تأییدی در محیط کم‌رم — تک‌هسته، بدون آنالایزر (فقط برای صحت‌سنجی کامپایل)
# استفاده: ./build-check.sh [Api|Client|Shared|All]
# نکته اصلاح‌شده: اگر dotnet در PATH نبود، از $HOME/.dotnet هم جستجو می‌کند و در صورت نبود
# پیام خطای واضح با راهنمای نصب می‌دهد (مشکل شایع: «dotnet: command not found» روی لینوکس).
set -e
# تلاش برای یافتن dotnet در مسیرهای رایج (self-contained install / global)
if ! command -v dotnet >/dev/null 2>&1; then
  for cand in "/var/tmp/dotnet" "$HOME/.dotnet" "/usr/share/dotnet"; do
    if [ -x "$cand/dotnet" ]; then export PATH="$cand:$PATH"; break; fi
  done
fi
if ! command -v dotnet >/dev/null 2>&1; then
  echo "❌ .NET 8 SDK پیدا نشد." >&2
  echo "   راه حل لینوکس (بدون نیاز به apt):" >&2
  echo "     wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh && bash /tmp/dotnet-install.sh --channel 8.0 --install-dir \$HOME/.dotnet" >&2
  echo "     export PATH=\$HOME/.dotnet:\$PATH && ./build-check.sh" >&2
  echo "   یا نسخه ویندوز: https://dotnet.microsoft.com/download/dotnet/8.0" >&2
  exit 1
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_gcServer=0 DOTNET_GCHeapHardLimit=0x30000000 MSBUILDDISABLENODEREUSE=1
cd "$(dirname "$0")"
TARGET="${1:-All}"
FLAGS=(-c Debug -m:1 -p:MaxCpuCount=1 -p:RunAnalyzers=false -p:EnableNETAnalyzers=false)
case "$TARGET" in
  Shared) dotnet build src/Inventory.Shared/Inventory.Shared.csproj --no-restore "${FLAGS[@]}";;
  Api)    dotnet build src/Inventory.Api/Inventory.Api.csproj --no-restore "${FLAGS[@]}";;
  Client) dotnet build src/Inventory.Client/Inventory.Client.csproj --no-restore "${FLAGS[@]}";;
  All)
    dotnet build src/Inventory.Api/Inventory.Api.csproj --no-restore "${FLAGS[@]}"
    dotnet build src/Inventory.Client/Inventory.Client.csproj --no-restore "${FLAGS[@]}"
    ;;
esac
echo "✅ بیلد موفق: $TARGET"
