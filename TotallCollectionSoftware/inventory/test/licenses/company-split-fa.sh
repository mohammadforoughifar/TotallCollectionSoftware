#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# آزمون «لایسنس به‌ازای هر شرکت» (چند شرکت فعال روی یک نصب)
#
# جریان کاری که می‌سنجد:
#   هر شرکت فعال ادمین خودش را دارد → ادمین همان شرکت درخواست می‌فرستد →
#   پشتیبان سند را امضا می‌کند → ادمین شرکت بارگذاری می‌کند → نقش/کاربر می‌سازد →
#   دسترسی کاربران به‌اندازهٔ ماژول‌های همان سند است.
#
# چه چیزی را ثابت می‌کند؟
#   ۱) سند شرکت الف فقط ماژول‌های شرکت الف را می‌دهد؛ ماژول سند شرکت ب به او نمی‌رسد.
#   ۲) سقف صندلی هر شرکت = کمینهٔ سقف سند و سقف همان شرکت.
#   ۳) شرکت بدون سند، «بدون لایسنس» است (نه «همه‌چیز مجاز»).
#   ۴) سند بدون فهرست شرکت (فرمت قدیمی) به هیچ شرکتی نسبت داده نمی‌شود و هشدار می‌دهد.
#
# اجرا:  KEYDIR=/var/tmp/keytest bash test/licenses/company-split-fa.sh
# ---------------------------------------------------------------------------
set -u

REPO=${REPO:-$(cd "$(dirname "$0")/../.." && pwd)}
DOTNET=${DOTNET:-/var/tmp/dotnet/dotnet}
KEYDIR=${KEYDIR:-/var/tmp/keytest}
PRIV=${PRIV:-$KEYDIR/dev-test.private.pem}
PUB=${PUB:-$KEYDIR/dev-test.pem}
TOOL=${TOOL:-$REPO/tools/LicenseTool/bin/Debug/net8.0/LicenseTool.dll}
[ -f "$TOOL" ] || TOOL=$REPO/tools/LicenseTool/bin/Release/net8.0/LicenseTool.dll

TMP=$(mktemp -d /tmp/lic-company-split.XXXXXX)
LOG=${LOG:-$TMP/split.log}
PASSED=0; FAILED=0
log()  { printf '%s\n' "$*" | tee -a "$LOG"; }
ok()   { PASSED=$((PASSED+1)); printf '  ✔ %s\n' "$*" | tee -a "$LOG"; }
bad()  { FAILED=$((FAILED+1)); printf '  ✖ %s\n' "$*" | tee -a "$LOG"; }
has()  { if printf '%s' "$1" | grep -qF -- "$2"; then ok "$3"; else bad "$3 (نبود: $2)"; fi; }
hasnt(){ if printf '%s' "$1" | grep -qF -- "$2"; then bad "$3 (نباید بود: $2)"; else ok "$3"; fi; }

[ -f "$TOOL" ] || { echo "✖ ابزار نیست: $TOOL" >&2; exit 2; }
[ -f "$PRIV" ] || { echo "✖ کلید خصوصی آزمون نیست: $PRIV" >&2; exit 2; }

# گذرواژهٔ کلید آزمون: از env یا از فایل محلی .keypass (هرگز در لاگ چاپ نمی‌شود)
if [ -z "${LICENSETOOL_KEY_PASSWORD:-}" ] && [ -f "$KEYDIR/.keypass" ]; then
    LICENSETOOL_KEY_PASSWORD="$(cat "$KEYDIR/.keypass")"
    export LICENSETOOL_KEY_PASSWORD
fi

INST="TEST-MULTI-$$"

cat > "$TMP/a.json" <<'JSON'
{ "customer": { "id": "CUST-A", "name": "شرکت الف" }, "deployment": "OnPrem",
  "items": [ { "id": "base", "scope": ["sales","hr_fa"], "term": {"type":"Subscription","months":12},
               "users": {"mode":"Named","max":10} } ],
  "companies": [ { "companyId": "C-A", "name": "شرکت الف", "maxUsers": 4, "modules": ["sales"] } ] }
JSON

cat > "$TMP/b.json" <<'JSON'
{ "customer": { "id": "CUST-B", "name": "شرکت ب" }, "deployment": "OnPrem",
  "items": [ { "id": "base", "scope": ["sales","hr_fa","docarchive"], "term": {"type":"Subscription","months":12},
               "users": {"mode":"Named","max":8} } ],
  "companies": [ { "companyId": "C-B", "name": "شرکت ب" } ] }
JSON

cat > "$TMP/old.json" <<'JSON'
{ "customer": { "id": "CUST-OLD", "name": "مشتری قدیمی" }, "deployment": "OnPrem",
  "items": [ { "id": "base", "scope": ["sales"], "term": {"type":"Subscription","months":12},
               "users": {"mode":"Named","max":5} } ] }
JSON

issue() {
    local out
    out=$("$DOTNET" "$TOOL" issue --profile "$1" --installation-id "$INST" \
            --private-key "$PRIV" --out "$2" --yes 2>&1)
    if [ ! -f "$2" ]; then
        printf '  ✖ صدور نشد: %s\n    %s\n' "$1" "$(printf '%s' "$out" | tail -3)" | tee -a "$LOG" >&2
        return 1
    fi
}

log "===== صدور سه سند (الف، ب، و یک سند قدیمی بدون شرکت) ====="
issue "$TMP/a.json" "$TMP/a.lic"
issue "$TMP/b.json" "$TMP/b.lic"
issue "$TMP/old.json" "$TMP/old.lic"
if [ -f "$TMP/a.lic" ] && [ -f "$TMP/b.lic" ] && [ -f "$TMP/old.lic" ]; then
    ok "سه سند امضا شد"
else
    bad "صدور سند ناموفق بود"
fi

log ""
log "===== ۱) نمای شرکت‌ها ====="
OUT=$("$DOTNET" "$TOOL" companies "$TMP/a.lic" "$TMP/b.lic" --public-key "$PUB" 2>&1)
printf '%s\n' "$OUT" | sed 's/^/    /' | tee -a "$LOG" >/dev/null
has   "$OUT" "شرکت‌های دارای لایسنس: 2" "دو شرکت مستقل شناخته شد"
has   "$OUT" "C-A"                        "شرکت الف در گزارش هست"
has   "$OUT" "C-B"                        "شرکت ب در گزارش هست"

# بخش مربوط به هر شرکت را جدا می‌کنیم تا ادعاها دقیق باشند.
A_BLOCK=$(printf '%s\n' "$OUT" | awk '/^■ C-A/{f=1} /^■ C-B/{f=0} f')
B_BLOCK=$(printf '%s\n' "$OUT" | awk '/^■ C-B/{f=1} f')

log ""
log "===== ۲) جدایی ماژول‌ها ====="
has   "$A_BLOCK" "فروش و خرید"        "شرکت الف ماژول فروش را دارد"
hasnt "$A_BLOCK" "منابع انسانی"       "شرکت الف ماژول منابع انسانی شرکت ب را ندارد"
has   "$B_BLOCK" "منابع انسانی"       "شرکت ب ماژول منابع انسانی را دارد"
has   "$B_BLOCK" "آرشیو اسناد"        "شرکت ب ماژول آرشیو را دارد"

log ""
log "===== ۳) سقف صندلی هر شرکت ====="
has "$A_BLOCK" "صندلی: 4"  "سقف شرکت الف از سقف سند (10) کوچک‌تر است: 4"
has "$B_BLOCK" "صندلی: 8"  "شرکت ب سقف خودش (8) را می‌گیرد"

log ""
log "===== ۴) شرکت بدون سند ====="
OUT3=$("$DOTNET" "$TOOL" companies "$TMP/a.lic" --public-key "$PUB" 2>&1)
hasnt "$OUT3" "C-B" "وقتی سند شرکت ب روی نصب نیست، شرکت ب لایسنس ندارد"
# بررسی مستقیم پیام نداشتن لایسنس برای شرکتی که سندش نیست:
has "$OUT3" "شرکت‌های دارای لایسنس: 1" "فقط یک شرکت لایسنس دارد"

log ""
log "===== ۵) سند قدیمی بدون فهرست شرکت ====="
OUT4=$("$DOTNET" "$TOOL" companies "$TMP/old.lic" --public-key "$PUB" 2>&1)
printf '%s\n' "$OUT4" | sed 's/^/    /' >> "$LOG"
has "$OUT4" "سند بدون فهرست شرکت" "سند بی‌شرکت به هیچ شرکتی نسبت داده نشد"
has "$OUT4" "شرکت‌های دارای لایسنس: 0" "شرکت تازه‌ای از سند بی‌شرکت ساخته نشد"

log ""
log "──────────────────────────────────────────────"
log "  نتیجه: $PASSED موفق، $FAILED ناموفق"
log "  لاگ: $LOG"
log "──────────────────────────────────────────────"
[ "$FAILED" -eq 0 ] || exit 1
