#!/usr/bin/env bash
# ساخت بستهٔ قابل‌حمل برای کپی روی سیستم پشتیبان.
#   ./publish.sh                → out/ (نیازمند .NET 8 Runtime روی ماشین مقصد)
#   ./publish.sh self-contained → out/ با Runtime داخلش (بدون نیاز به نصب .NET)
set -euo pipefail
cd "$(dirname "$0")"

MODE="${1:-framework}"
OUT="$(pwd)/out"

rm -rf "$OUT"
if [ "$MODE" = "self-contained" ]; then
  dotnet publish SupportPanel.csproj -c Release -o "$OUT" \
    --self-contained true -r "$(dotnet --info | awk '/RID:/{print $2}')"
else
  dotnet publish SupportPanel.csproj -c Release -o "$OUT" --self-contained false
fi

cat <<MSG

بسته آماده است: $OUT
اجرا روی سیستم پشتیبان:
  set ASPNETCORE_URLS=http://127.0.0.1:5399        (ویندوز)
  dotnet SupportPanel.dll
MSG
