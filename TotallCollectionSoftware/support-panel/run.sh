#!/usr/bin/env bash
# اجرای پنل پشتیبانی روی همین ماشین (فقط localhost).
set -euo pipefail
cd "$(dirname "$0")"

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5399}"

# مسیرهای پیش‌فرض اگر در appsettings/env تعیین نشده باشند:
export Signer__ToolPath="${Signer__ToolPath:-$(cd ../inventory && pwd)/tools/LicenseTool/bin/Release/net8.0/LicenseTool.dll}"
export Signer__PresetsPath="${Signer__PresetsPath:-$(cd ../inventory && pwd)/tools/LicenseTool/presets.json}"

echo "پنل پشتیبانی: $ASPNETCORE_URLS  (پوشهٔ کاری: $(pwd)/data)"
exec dotnet run --project SupportPanel.csproj -c Release --no-launch-profile
