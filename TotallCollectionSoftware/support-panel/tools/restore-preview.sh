#!/usr/bin/env bash
# بازسازی سریع محیط پیش‌نمایش پنل (پس از ریست سندباکس).
# استفاده:  bash tools/restore-preview.sh build     ⇒ فقط ساخت
#           bash tools/restore-preview.sh run       ⇒ ساخت + اجرا روی 0.0.0.0:5399
set -euo pipefail

ROOT=/home/user/work/repo/TotallCollectionSoftware
SDK=/var/tmp/dotnet
OUT=/var/tmp/panel-out
KEYS=/var/tmp/keytest
DATA="$ROOT/support-panel/data"
LOG=/var/tmp/panel.log

export DOTNET_ROOT=$SDK
export PATH=$SDK:$PATH
export DOTNET_CLI_HOME=/var/tmp/dotnet-home
export NUGET_PACKAGES=/var/tmp/nuget
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

if [ ! -x "$SDK/dotnet" ]; then
  echo "== نصب SDK 8 در $SDK"
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$SDK" --no-path >/dev/null
fi

echo "== ساخت LicenseTool + presets"
dotnet build "$ROOT/inventory/tools/LicenseTool/LicenseTool.csproj" -c Release \
  -m:1 -p:UseSharedCompilation=false -p:RunAnalyzers=false -p:DebugType=none -v:q
cp -f "$ROOT/inventory/tools/LicenseTool/presets.json" \
      "$ROOT/inventory/tools/LicenseTool/bin/Release/net8.0/presets.json"

echo "== publish پنل در $OUT"
rm -rf "$OUT"
dotnet publish "$ROOT/support-panel/SupportPanel.csproj" -c Release -o "$OUT" \
  -m:1 -p:UseSharedCompilation=false -p:RunAnalyzers=false -p:DebugType=none -v:q

# گذرواژهٔ کلید آزمون: از env یا تولید تصادفی و نگهداری محلی (هرگز چاپ/کامیت نمی‌شود)
mkdir -p "$KEYS"; chmod 700 "$KEYS" 2>/dev/null || true
PP="$KEYS/.keypass"
if [ -f "$KEYS/dev-test.private.pem" ] && [ ! -f "$PP" ] && [ -z "${LICENSETOOL_KEY_PASSWORD:-}" ]; then
  echo "✗ کلید آزمون موجود است ولی گذرواژهٔ آن ذخیره نشده؛ LICENSETOOL_KEY_PASSWORD را ست کنید یا $KEYS را پاک کنید." >&2
  exit 3
fi
if [ -z "${LICENSETOOL_KEY_PASSWORD:-}" ]; then
  if [ -f "$PP" ]; then LICENSETOOL_KEY_PASSWORD="$(cat "$PP")"
  else LICENSETOOL_KEY_PASSWORD="$(openssl rand -hex 16)"; printf '%s' "$LICENSETOOL_KEY_PASSWORD" > "$PP"; chmod 600 "$PP"; fi
fi
export LICENSETOOL_KEY_PASSWORD

if [ ! -f "$KEYS/dev-test.private.pem" ]; then
  echo "== ساخت جفت‌کلید آزمون (فقط محلی)"
  LICENSETOOL_KEY_PASSWORD="$LICENSETOOL_KEY_PASSWORD" \
    dotnet "$ROOT/inventory/tools/LicenseTool/bin/Release/net8.0/LicenseTool.dll" \
    keygen --key-id dev-test --out-dir "$KEYS" >/dev/null
fi

# خروجی‌های بیلد را از ورک‌اسپیس پاک کن (حجم پایین بماند)
rm -rf "$ROOT/support-panel/bin" "$ROOT/support-panel/obj" \
       "$ROOT/inventory/tools/LicenseTool/bin" "$ROOT/inventory/tools/LicenseTool/obj" \
       "$ROOT/inventory/src/Inventory.Licensing/bin" "$ROOT/inventory/src/Inventory.Licensing/obj"

if [ "${1:-build}" = "run" ]; then
  mkdir -p "$DATA"
  echo "== اجرا روی http://0.0.0.0:5399 (log: $LOG)"
  cd "$OUT"
  exec env ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:5399 \
    Signer__PreviewMode=true \
    Signer__KeyId=dev-test \
    Signer__PrivateKeyPath="$KEYS/dev-test.private.pem" \
    Signer__PublicKeyPath="$KEYS/dev-test.pem" \
    Signer__WorkFolder="$DATA" \
    Signer__DatabaseConnection="Data Source=$DATA/support.db" \
    LICENSETOOL_KEY_PASSWORD="$LICENSETOOL_KEY_PASSWORD" \
    dotnet SupportPanel.dll
fi
echo "== آماده است: $OUT"
