#!/usr/bin/env bash
# آماده‌سازی یک‌بارهٔ مینی‌پروژهٔ پشتیبان:
#   ۱) ساخت ابزار امضا (LicenseTool) در حالت Release
#   ۲) کپی presets.json کنار فایل اجرایی ابزار (ابزار آن را فقط کنار خودش می‌جوید)
#   ۳) ساخت پنل
set -euo pipefail
cd "$(dirname "$0")"

INVENTORY="$(cd ../inventory && pwd)"
TOOL_DIR="$INVENTORY/tools/LicenseTool"

echo "== ۱/۳ ساخت LicenseTool =="
dotnet build "$TOOL_DIR/LicenseTool.csproj" -c Release -v:q

echo "== ۲/۳ کپی presets.json کنار ابزار =="
cp -f "$TOOL_DIR/presets.json" "$TOOL_DIR/bin/Release/net8.0/presets.json"

echo "== ۳/۳ ساخت پنل =="
dotnet build SupportPanel.csproj -c Release -v:q

cat <<'MSG'

آماده است. گام بعد:

  ۱) اگر جفت‌کلید ندارید بسازید (بیرون مخزن؛ گذرواژهٔ کلید را خودتان می‌دهید):
     LICENSETOOL_KEY_PASSWORD='<رمز-کلید>' dotnet ../inventory/tools/LicenseTool/bin/Release/net8.0/LicenseTool.dll \
        keygen --key-id <key-id> --out-dir <پوشهٔ-کلیدها>

  ۲) همین دو مسیر را در appsettings.json (یا env) بگذارید:
     Signer:PrivateKeyPath   Signer:PublicKeyPath   Signer:KeyId

  ۳) اجرا:  ./run.sh        (یا در ویندوز: run.cmd)
MSG
