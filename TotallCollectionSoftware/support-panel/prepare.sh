#!/usr/bin/env bash
# آماده‌سازی یک‌بارهٔ پنل پشتیبانی (بدون موتور لایسنس/ابزار امضا).
set -euo pipefail
cd "$(dirname "$0")"

echo "== ساخت پنل =="
dotnet build SupportPanel.csproj -c Release -v:q

cat <<'MSG'

آماده است. گام بعد:

  ۱) (اختیاری) رمز SMTP را در متغیر محیطی بگذارید:
     export SUPPORTPANEL_SMTP_PASSWORD='<رمز>'

  ۲) اجرا:  ./run.sh        (یا در ویندوز: run.cmd)

  در اولین اجرا کاربر «admin» ساخته می‌شود و گذرواژه‌اش فقط یک‌بار روی کنسول چاپ
  می‌شود؛ در اولین ورود تعویض آن اجباری است.
MSG
