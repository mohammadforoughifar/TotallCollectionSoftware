#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# آزمون «شرکت‌های مجاز داخل لایسنس» — موتور لایسنس + ابزار صدور + گزارش نصب.
#
# چه چیزی را ثابت می‌کند؟
#   ۱) شرکت‌ها در سند امضاشده می‌نشینند و بعد از امضا دست‌نخورده خوانده می‌شوند.
#   ۲) اعتبارسنجی، ترکیب‌های خطرناک را رد می‌کند:
#        • شرکتی با ماژولی که در سند فروخته نشده        ⇒ COMPANY_MODULE_OUT_OF_SCOPE
#        • جمع سقف کاربران شرکت‌ها بیشتر از سقف سند      ⇒ COMPANY_USERS_EXCEED_SEATS
#        • سقف کاربر برای بعضی شرکت‌ها و بی‌سقف برای بقیه ⇒ COMPANY_USERS_PARTIAL
#   ۳) سند بدون شرکت (سازگاری عقب‌رو) هیچ خطای شرکتی نمی‌دهد.
#   ۴) دست‌کاری بایت‌های payload (مثلاً افزودن شرکت) امضا را می‌شکند.
#
# پیش‌نیاز: جفت‌کلید آزمون بیرون مخزن (KEYDIR) + ابزارهای ساخته‌شده.
# اجرا:  KEYPASS=... KEYDIR=/var/tmp/keytest bash test/licenses/companies-fa.sh
# ---------------------------------------------------------------------------
set -u

REPO=${REPO:-$(cd "$(dirname "$0")/../.." && pwd)}
DOTNET=${DOTNET:-/var/tmp/dotnet/dotnet}
KEYDIR=${KEYDIR:-/var/tmp/keytest}
PRIV=${PRIV:-}
[ -z "$PRIV" ] && for c in "$KEYDIR/dev-test.private.pem" "$KEYDIR/dev-2026a.private.pem"; do
    [ -f "$c" ] && PRIV="$c" && break
done
PUB=${PUB:-}
[ -z "$PUB" ] && for c in "$KEYDIR/dev-test.pem" "$KEYDIR/dev-2026a.pem"; do
    [ -f "$c" ] && PUB="$c" && break
done

TOOL=${TOOL:-$REPO/tools/LicenseTool/bin/Debug/net8.0/LicenseTool.dll}
[ -f "$TOOL" ] || TOOL=$REPO/tools/LicenseTool/bin/Release/net8.0/LicenseTool.dll

TMP=$(mktemp -d /tmp/lic-companies.XXXXXX)
LOG=${LOG:-$TMP/companies.log}
PASSED=0; FAILED=0

log()  { printf '%s\n' "$*" | tee -a "$LOG"; }
ok()   { PASSED=$((PASSED+1)); printf '  ✔ %s\n' "$*" | tee -a "$LOG"; }
bad()  { FAILED=$((FAILED+1)); printf '  ✖ %s\n' "$*" | tee -a "$LOG"; }
has()  { if printf '%s' "$1" | grep -qF -- "$2"; then ok "$3"; else bad "$3 (نبود: $2)"; fi; }
hasnt(){ if printf '%s' "$1" | grep -qF -- "$2"; then bad "$3 (نباید بود: $2)"; else ok "$3"; fi; }

if [ ! -f "$TOOL" ]; then echo "✖ ابزار پیدا نشد: $TOOL" >&2; exit 2; fi
if [ -z "$PRIV" ] || [ ! -f "$PRIV" ]; then echo "✖ کلید خصوصی آزمون پیدا نشد (KEYDIR=$KEYDIR)" >&2; exit 2; fi
if [ -z "$PUB" ] || [ ! -f "$PUB" ]; then echo "✖ کلید عمومی آزمون پیدا نشد (KEYDIR=$KEYDIR)" >&2; exit 2; fi

# گذرواژهٔ کلید آزمون: از env یا فایل محلی .keypass (هیچ‌گاه چاپ نمی‌شود)
if [ -z "${LICENSETOOL_KEY_PASSWORD:-}" ] && [ -f "$KEYDIR/.keypass" ]; then
    LICENSETOOL_KEY_PASSWORD="$(cat "$KEYDIR/.keypass")"
    export LICENSETOOL_KEY_PASSWORD
fi

run() { "$DOTNET" "$TOOL" "$@" 2>&1; }

INST="TEST-COMPANIES-$$"
issue() {  # issue <profile> <out.lic>  → چاپ خروجی ابزار (و شکست اگر فایل ساخته نشد)
    local out
    out=$(run issue --profile "$1" --installation-id "$INST" --private-key "$PRIV" --out "$2" --yes)
    if [ ! -f "$2" ]; then
        printf '  ✖ صدور نشد (%s):\n    %s\n' "$1" "$(printf '%s' "$out" | tail -3)" | tee -a "$LOG" >&2
    fi
    printf '%s' "$out"
}

cat > "$TMP/good.json" <<'JSON'
{
  "customer": { "id": "DEMO-CO", "name": "شرکت نمونه" },
  "deployment": "OnPrem",
  "items": [
    { "id": "base", "scope": ["sales", "hr_fa"], "term": { "type": "Subscription", "months": 12 },
      "users": { "mode": "Named", "max": 8, "appliesTo": "Global", "exempt": 1, "exemptUsers": ["admin"] } }
  ],
  "companies": [
    { "companyId": "C-1001", "name": "شرکت الف", "maxUsers": 5, "modules": ["sales"] },
    { "companyId": "C-1002", "name": "شرکت ب", "maxUsers": 3 }
  ]
}
JSON

cat > "$TMP/bad-scope.json" <<'JSON'
{
  "customer": { "id": "DEMO-CO", "name": "شرکت نمونه" },
  "deployment": "OnPrem",
  "items": [
    { "id": "base", "scope": ["sales"], "term": { "type": "Subscription", "months": 12 },
      "users": { "mode": "Named", "max": 8 } }
  ],
  "companies": [ { "companyId": "C-1", "name": "الف", "maxUsers": 5, "modules": ["sales", "docarchive"] } ]
}
JSON

cat > "$TMP/bad-seats.json" <<'JSON'
{
  "customer": { "id": "DEMO-CO", "name": "شرکت نمونه" },
  "deployment": "OnPrem",
  "items": [
    { "id": "base", "scope": ["sales"], "term": { "type": "Subscription", "months": 12 },
      "users": { "mode": "Named", "max": 8 } }
  ],
  "companies": [
    { "companyId": "C-1", "name": "الف", "maxUsers": 5 },
    { "companyId": "C-2", "name": "ب", "maxUsers": 4 },
    { "companyId": "C-3", "name": "ج" }
  ]
}
JSON

cat > "$TMP/no-companies.json" <<'JSON'
{
  "customer": { "id": "DEMO-CO", "name": "شرکت نمونه" },
  "deployment": "OnPrem",
  "items": [
    { "id": "base", "scope": ["sales"], "term": { "type": "Subscription", "months": 12 },
      "users": { "mode": "Named", "max": 5 } }
  ]
}
JSON

log "===== ۱) سند سالم با دو شرکت ====="
OUT=$(issue "$TMP/good.json" "$TMP/good.lic")
has "$OUT" "شرکت‌های مجاز (2)" "پیش‌نمایش دو شرکت را نشان می‌دهد"
has "$OUT" "LIC-" "لایسنس صادر شد"
VER=$(run verify "$TMP/good.lic" --installation-id "$INST" --public-key "$PUB")
has   "$VER" "امضا معتبر است"        "امضای سند سالم تأیید شد"
has   "$VER" "شرکت‌های مجاز: 2"       "شرکت‌ها بعد از امضا سالم خوانده می‌شوند"
has   "$VER" "C-1001"                 "شناسهٔ شرکت اول در سند هست"
has   "$VER" "C-1002"                 "شناسهٔ شرکت دوم در سند هست"
hasnt "$VER" "COMPANY_"               "سند سالم هیچ خطای شرکتی ندارد"

log ""
log "===== ۲) شرکت با ماژول فروخته‌نشده ====="
issue "$TMP/bad-scope.json" "$TMP/bad-scope.lic" >/dev/null
VER=$(run verify "$TMP/bad-scope.lic" --installation-id "$INST" --public-key "$PUB")
has "$VER" "COMPANY_MODULE_OUT_OF_SCOPE" "ماژول خارج از scope رد شد"

log ""
log "===== ۳) جمع سقف کاربران شرکت‌ها بیشتر از سقف سند ====="
issue "$TMP/bad-seats.json" "$TMP/bad-seats.lic" >/dev/null
VER=$(run verify "$TMP/bad-seats.lic" --installation-id "$INST" --public-key "$PUB")
has "$VER" "COMPANY_USERS_EXCEED_SEATS" "جمع ۹ صندلی در سقف ۸ رد شد"
has "$VER" "COMPANY_USERS_PARTIAL"      "شرکت بدون سقف هشدار گرفت"

log ""
log "===== ۴) سازگاری عقب‌رو: سند بدون شرکت ====="
issue "$TMP/no-companies.json" "$TMP/no-companies.lic" >/dev/null
VER=$(run verify "$TMP/no-companies.lic" --installation-id "$INST" --public-key "$PUB")
has   "$VER" "امضا معتبر است"       "سند بدون شرکت معتبر است"
has   "$VER" "محدودیت شرکت ندارد"   "گزارش وضعیت می‌گوید محدودیتی ندارد"
hasnt "$VER" "COMPANY_"             "هیچ خطای شرکتی تولید نشد"

log ""
log "===== ۵) دست‌کاری payload (افزودن شرکت جعلی) باید امضا را بشکند ====="
python3 - "$TMP/good.lic" "$TMP/tampered.lic" <<'PY'
import base64, json, sys
src, dst = sys.argv[1], sys.argv[2]
env = json.load(open(src, encoding='utf-8'))
payload = json.loads(base64.b64decode(env['payload']))
payload['companies'].append({"companyId": "C-9999", "name": "شرکت جعلی"})
env['payload'] = base64.b64encode(json.dumps(payload, ensure_ascii=False).encode()).decode()
json.dump(env, open(dst, 'w', encoding='utf-8'), ensure_ascii=False)
PY
VER=$(run verify "$TMP/tampered.lic" --installation-id "$INST" --public-key "$PUB")
has "$VER" "نامعتبر" "شرکت جعلی امضا را شکست"

log ""
log "──────────────────────────────────────────────"
log "  نتیجه: $PASSED موفق، $FAILED ناموفق"
log "  لاگ: $LOG"
log "──────────────────────────────────────────────"
[ "$FAILED" -eq 0 ] || exit 1
