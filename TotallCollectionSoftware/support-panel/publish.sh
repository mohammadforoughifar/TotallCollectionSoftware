#!/usr/bin/env bash
# ساخت بستهٔ قابل‌حمل برای کپی روی سیستم پشتیبان.
#   ./publish.sh                → out/ (نیازمند .NET 8 Runtime روی ماشین مقصد)
#   ./publish.sh self-contained → out/ با Runtime داخلش (بدون نیاز به نصب .NET)
set -euo pipefail
cd "$(dirname "$0")"

MODE="${1:-framework}"
OUT="$(pwd)/out"
INVENTORY="$(cd ../inventory && pwd)"

rm -rf "$OUT"
if [ "$MODE" = "self-contained" ]; then
  dotnet publish SupportPanel.csproj -c Release -o "$OUT" \
    --self-contained true -r "$(dotnet --info | awk '/RID:/{print $2}')"
else
  dotnet publish SupportPanel.csproj -c Release -o "$OUT" --self-contained false
fi

# ابزار امضا هم لازم است (پنل آن را صدا می‌زند).
mkdir -p "$OUT/tools"
dotnet publish "$INVENTORY/tools/LicenseTool/LicenseTool.csproj" -c Release -o "$OUT/tools" --self-contained false
cp -f "$INVENTORY/tools/LicenseTool/presets.json" "$OUT/tools/presets.json"

cat <<MSG

بسته آماده است: $OUT
روی سیستم پشتیبان فقط این‌ها را لازم دارید:
  • out/ (کل پوشه)
  • جفت‌کلید (بیرون از out؛ مثلاً C:\\license-keys)
اجرا:
  set ASPNETCORE_URLS=http://127.0.0.1:5399        (ویندوز)
  set Signer__ToolPath=<مسیر>\\out\\tools\\LicenseTool.dll
  set Signer__PrivateKeyPath=<مسیر>\\prod.private.pem
  set Signer__PublicKeyPath=<مسیر>\\prod.pem
  set Signer__KeyId=<key-id>
  dotnet SupportPanel.dll
MSG
