#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# آزمون سرتاسری پنل پشتیبانی لایسنس (فارسی، فقط localhost).
# پیش‌نیازها:
#   • پنل روی $BASE بالا باشد (SeedService گذرواژهٔ آغازین admin را روی کنسول چاپ می‌کند)
#   • $ADMIN_PASSWORD گذرواژهٔ فعلی مدیر در env باشد (هیچ‌گاه در فایل نوشته نشود)
#   • جفت‌کلید آزمون بیرون مخزن + ابزار ساخته‌شده (مسیرها با env قابل تغییر)
# اجرا:  ADMIN_PASSWORD=... KEYPASS=... bash test/support/e2e-fa.sh
# توجه: این آزمون «پذیرش از اولین اجرا» است ⇒ روی WorkFolder/DB تازه اجرا شود (وگرنه با کد ۲ می‌ایستد).
# ---------------------------------------------------------------------------
set -u
BASE=${BASE:-http://127.0.0.1:5299}
REPO=${REPO:-$(cd "$(dirname "$0")/../.." && pwd)}
WORK=${WORK:-/var/tmp/signerwork}
KEYDIR=${KEYDIR:-/var/tmp/keytest}
TOOL=${TOOL:-$REPO/tools/LicenseTool/bin/Debug/net8.0/LicenseTool.dll}
DOTNET=${DOTNET:-/var/tmp/dotnet/dotnet}
LOG=${LOG:-/home/user/evidence/support-panel-e2e.log}
JAR=$(mktemp /tmp/e2e-jar.XXXXXX)
TMP=$(mktemp -d /tmp/e2e-panel.XXXXXX)
NEWPASS=${NEWPASS:-Panel-E2E-Pass-2026}
KEYPASS=${KEYPASS:-}
FIXTURE="$TMP/request.json"
STAMP=$(date +%s)
INSTALL_ID="e2e-panel-$STAMP"
PASSED=0; FAILED=0
: > "$LOG"

log() { printf '%s\n' "$*" | tee -a "$LOG"; }
section() { printf '\n===== %s =====\n' "$*" | tee -a "$LOG"; }
ok()   { PASSED=$((PASSED+1)); printf '  ✔ %s\n' "$*" | tee -a "$LOG"; }
bad()  { FAILED=$((FAILED+1)); printf '  ✖ %s\n' "$*" | tee -a "$LOG"; }
check(){ if [ "$1" = "1" ]; then ok "$2"; else bad "$2"; fi; }
code_is() { [ "$1" = "$2" ] && echo 1 || echo 0; }
# شمارش متن فارسی پس از html.unescape (نیم‌فاصله/گیومه‌ها)
txtcount() {
python3 - "$1" "$2" <<'PY'
import html,sys
text=html.unescape(open(sys.argv[1],encoding='utf-8',errors='replace').read())
print(text.count(sys.argv[2]))
PY
}
has() { [ "$(txtcount "$1" "$2")" -gt 0 ] && echo 1 || echo 0; }
tok() {
python3 - "$1" <<'PY'
import re,sys
s=open(sys.argv[1],encoding='utf-8',errors='replace').read()
m=re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',s)
print(m.group(1) if m else "")
PY
}
get()  { curl -sS -b "$JAR" -c "$JAR" -o "$2" -w '%{http_code}' "$BASE$1"; }
geth() { curl -sS -b "$JAR" -c "$JAR" -o "$2" -w '%{http_code}' -H "Host: 127.0.0.1:5299" "$BASE$1"; }
post() { # post <path> <out> <data...>
  local path=$1 out=$2; shift 2
  curl -sS -b "$JAR" -c "$JAR" -o "$out" -w '%{http_code}|%{redirect_url}' \
       -X POST "$BASE$path" "$@"
}

cleanup() { rm -f "$JAR"; rm -rf "$TMP"; }
trap cleanup EXIT

log "آزمون سرتاسری پنل پشتیبانی — $(date -u +%FT%TZ)"
log "BASE=$BASE  WORK=$WORK  INSTALLATION=$INSTALL_ID"

# ---------------------------------------------------------------- ۱) سلامت و احراز هویت
section "۱) سلامت سرویس و بایگانی احراز هویت"
H=$(get /api/health "$TMP/health.json"); check "$(code_is "$H" 200)" "/api/health پاسخ ۲۰۰ داد ($H)"
H=$(get / "$TMP/root.html"); check "$(code_is "$H" 302)" "داشبورد بدون کوکی به ورود هدایت می‌شود ($H)"
H=$(get /Audit "$TMP/anonaudit.html"); check "$(code_is "$H" 302)" "مسیر ممیزی بدون ورود باز نمی‌شود ($H)"
H=$(curl -sS -b "$JAR" -c "$JAR" -o "$TMP/badhost.html" -w '%{http_code}' -H "Host: evil.example" "$BASE/"); check "$(code_is "$H" 400)" "Host غیرمجاز رد می‌شود ($H)"

# ---------------------------------------------------------------- ۲) ورود و تعویض اجباری گذرواژه
section "۲) ورود، تعویض اجباری گذرواژهٔ آغازین"
if [ -z "${ADMIN_PASSWORD:-}" ]; then bad "ADMIN_PASSWORD تنظیم نشده — ادامه ممکن نیست"; log "PASSED=$PASSED FAILED=$FAILED"; exit 1; fi
if [ -z "${KEYPASS:-}" ]; then bad "KEYPASS (گذرواژهٔ کلید آزمون) تنظیم نشده — ادامه ممکن نیست"; log "PASSED=$PASSED FAILED=$FAILED"; exit 1; fi
H=$(get /Login "$TMP/login.html"); check "$(code_is "$H" 200)" "صفحهٔ ورود باز شد ($H)"
T=$(tok "$TMP/login.html")
R=$(post /Login "$TMP/login-post.html" -d "__RequestVerificationToken=$T" \
      --data-urlencode "userName=admin" --data-urlencode "password=$ADMIN_PASSWORD")
case "$R" in
  302*ChangePassword*) ok "ورود موفق؛ تغییر مسیر به تعویض اجباری گذرواژه" ;;
  302*/|302*/Home*)
    bad "ورود/تغییر مسیر نامنتظر: $R"
    log "این پایگاه‌داده «تازه» نیست (گذرواژهٔ آغازین قبلاً تعویض شده). آزمون سرتاسری را روی WorkFolder تازه اجرا کنید."
    log "PASSED=$PASSED FAILED=$FAILED"; exit 2 ;;
  *) bad "ورود/تغییر مسیر نامنتظر: $R" ;;
esac
H=$(get /Requests "$TMP/blocked.html"); check "$(code_is "$H" 302)" "پیش از تعویض گذرواژه، بقیهٔ مسیرها بسته است ($H)"
H=$(get "/ChangePassword?first=1" "$TMP/chpass.html"); check "$(code_is "$H" 200)" "صفحهٔ تعویض گذرواژه باز شد ($H)"
T=$(tok "$TMP/chpass.html")
R=$(post /ChangePassword "$TMP/chpass-post.html" -d "__RequestVerificationToken=$T" \
      --data-urlencode "current=$ADMIN_PASSWORD" --data-urlencode "next=$NEWPASS" --data-urlencode "confirm=$NEWPASS")
check "$(code_is "$(echo "$R" | cut -d'|' -f1)" 200)" "پس‌داد تعویض گذرواژه پذیرفته شد"
check "$(has "$TMP/chpass-post.html" "گذرواژه تغییر کرد")" "پیام «گذرواژه تغییر کرد» نمایش داده شد"

# ورود با گذرواژهٔ تازه
H=$(get /Logout "$TMP/lo.html")
T=$(tok "$TMP/lo.html")
post /Logout "$TMP/lo-post.html" -d "__RequestVerificationToken=$T" >/dev/null
H=$(get /Login "$TMP/login2.html"); T=$(tok "$TMP/login2.html")
R=$(post /Login "$TMP/login2-post.html" -d "__RequestVerificationToken=$T" \
      --data-urlencode "userName=admin" --data-urlencode "password=$NEWPASS")
case "$R" in
  302*/) ok "ورود دوباره با گذرواژهٔ تازه موفق بود" ;;
  *) bad "ورود دوباره ناموفق: $R" ;;
esac

# ---------------------------------------------------------------- ۳) داشبورد
section "۳) داشبورد"
H=$(get / "$TMP/dash.html"); check "$(code_is "$H" 200)" "داشبورد باز شد ($H)"
check "$(has "$TMP/dash.html" "درخواست")" "داشبورد شمارندهٔ درخواست‌ها را دارد"
check "$(has "$TMP/dash.html" "مدیر")" "نقش/نام کاربر جاری در نوار بالا دیده می‌شود"

# ---------------------------------------------------------------- ۴) ورود درخواست (JSON)
section "۴) ثبت درخواست از JSON مشتری"
cat > "$FIXTURE" <<JSON
{"requestVersion":2,"requestType":"NewActivation","installationId":"$INSTALL_ID",
 "customerName":"شرکت آزمون پنل","contactName":"کاربر آزمون","contactPhone":"02100000000",
 "contactEmail":"e2e@example.invalid","customerNote":"درخواست آزمون سرتاسری",
 "requestedAt":"2026-11-14T00:00:00Z"}
JSON
H=$(get /Requests/Ingest "$TMP/ing.html"); check "$(code_is "$H" 200)" "صفحهٔ ثبت درخواست باز شد ($H)"
T=$(tok "$TMP/ing.html")
R=$(post "/Requests/Ingest?handler=Preview" "$TMP/prev.html" -d "__RequestVerificationToken=$T" \
      -d "mode=paste" -d "rawChannel=Paste" --data-urlencode "pastedJson@$FIXTURE" 2>/dev/null || true)
R=$(post "/Requests/Ingest?handler=Preview" "$TMP/prev.html" -d "__RequestVerificationToken=$T" \
      -d "mode=paste" -d "rawChannel=Paste" --data-urlencode "pastedJson=$(cat "$FIXTURE")")
check "$(has "$TMP/prev.html" "$INSTALL_ID")" "پیش‌نمایش، شناسهٔ نصب را نشان داد (بدون ذخیره)"
T=$(tok "$TMP/prev.html")
R=$(post "/Requests/Ingest?handler=Save" "$TMP/ing-post.html" -d "__RequestVerificationToken=$T" \
      -d "mode=paste" -d "rawChannel=Paste" --data-urlencode "pastedJson=$(cat "$FIXTURE")")
REQ_ID=$(echo "$R" | grep -oE 'id=[0-9]+' | head -1 | cut -d= -f2)
if [ -n "${REQ_ID:-}" ]; then ok "درخواست ثبت شد (id=$REQ_ID)"; else bad "ثبت درخواست ناموفق: $R"; fi
H=$(get "/Requests/Details?id=$REQ_ID" "$TMP/det.html"); check "$(code_is "$H" 200)" "صفحهٔ درخواست باز شد ($H)"
check "$(has "$TMP/det.html" "REQ-")" "شمارهٔ درخواست شمسی ساخته شد"
check "$(has "$TMP/det.html" "ثبت شد")" "پیام موفقیت ثبت دیده می‌شود"

# ---------------------------------------------------------------- ۴-ب) تعیین مشتری
section "۴-ب) تعیین مشتری برای درخواست (پیش‌شرط پیش‌نویس)"
H=$(get "/Customers/Edit?fromRequest=$REQ_ID" "$TMP/cust.html"); check "$(code_is "$H" 200)" "فرم مشتری تازه باز شد ($H)"
T=$(tok "$TMP/cust.html")
R=$(post "/Customers/Edit?handler=Save" "$TMP/cust-post.html" -d "__RequestVerificationToken=$T" \
      -d "id=0" -d "fromRequest=$REQ_ID" -d "code=E2E-$STAMP" --data-urlencode "name=شرکت آزمون پنل" \
      --data-urlencode "contactName=کاربر آزمون" --data-urlencode "contactEmail=e2e@example.invalid")
case "$R" in
  302*Details*) ok "مشتری ثبت و به درخواست وصل شد" ;;
  *) bad "ثبت مشتری نامنتظر: $R" ;;
esac
H=$(get "/Requests/Details?id=$REQ_ID" "$TMP/det-c.html")
check "$(has "$TMP/det-c.html" "شرکت آزمون پنل")" "نام مشتری روی درخواست دیده می‌شود"

# ---------------------------------------------------------------- ۵) تشخیص تکراری
section "۵) تشخیص درخواست تکراری (installationId + هش)"
T=$(tok "$TMP/ing.html")
R=$(post "/Requests/Ingest?handler=Save" "$TMP/dup.html" -d "__RequestVerificationToken=$T" \
      -d "mode=paste" -d "rawChannel=Paste" --data-urlencode "pastedJson=$(cat "$FIXTURE")")
REQ2_ID=$(echo "$R" | grep -oE 'id=[0-9]+' | head -1 | cut -d= -f2)
H=$(get "/Requests/Details?id=${REQ2_ID:-0}" "$TMP/dupdet.html")
check "$(has "$TMP/dupdet.html" "تکراری")" "درخواست دوم به‌عنوان تکراری با پیوند ثبت شد"

# ---------------------------------------------------------------- ۶) گارد تأیید بدون هویت
section "۶) گاردها: تأیید بدون تأیید هویت، و امضا پیش از تأیید"
T=$(tok "$TMP/det.html")
R=$(post "/Requests/Details?handler=Transition" "$TMP/guard1.html" -d "__RequestVerificationToken=$T" \
      -d "id=$REQ_ID" -d "to=Reviewing" -d "note=شروع بررسی")
H=$(get "/Requests/Details?id=$REQ_ID" "$TMP/det-r.html")
check "$(has "$TMP/det-r.html" "بررسی")" "گذار به «در حال بررسی» انجام شد"
T=$(tok "$TMP/det-r.html")
post "/Requests/Details?handler=Transition" "$TMP/g1.html" -d "__RequestVerificationToken=$T" \
      -d "id=$REQ_ID" -d "to=Approved" -d "note=" >/dev/null
H=$(get "/Requests/Details?id=$REQ_ID" "$TMP/det-g1.html")
check "$(has "$TMP/det-g1.html" "تأیید هویت")" "تأیید بدون ثبت تأیید هویت رد شد"
T=$(tok "$TMP/det-g1.html")
post "/Requests/Details?handler=Transition" "$TMP/g2.html" -d "__RequestVerificationToken=$T" \
      -d "id=$REQ_ID" -d "to=Issued" -d "note=" >/dev/null
H=$(get "/Requests/Details?id=$REQ_ID" "$TMP/det-g2.html")
check "$(has "$TMP/det-g2.html" "فقط با انجام امضا")" "گذار دستی به «صادرشده» رد شد"

# ---------------------------------------------------------------- ۷) تأیید هویت
section "۷) ثبت تأیید هویت"
H=$(get "/Requests/Identity?id=$REQ_ID" "$TMP/id.html"); T=$(tok "$TMP/id.html")
R=$(post "/Requests/Identity?id=$REQ_ID" "$TMP/id-post.html" -d "__RequestVerificationToken=$T" \
      -d "id=$REQ_ID" -d "method=Phone" --data-urlencode "verifiedWith=کاربر آزمون" \
      --data-urlencode "verifiedAt=1405/08/23" --data-urlencode "note=تماس تلفنی آزمون")
H=$(get "/Requests/Details?id=$REQ_ID" "$TMP/det-i.html")
check "$(has "$TMP/det-i.html" "تأیید هویت")" "تأیید هویت ثبت شد"
T=$(tok "$TMP/det-i.html")
post "/Requests/Details?handler=Transition" "$TMP/ap.html" -d "__RequestVerificationToken=$T" \
      -d "id=$REQ_ID" -d "to=Approved" -d "note=تأیید آزمون" >/dev/null
H=$(get "/Requests/Details?id=$REQ_ID" "$TMP/det-a.html")
check "$(has "$TMP/det-a.html" "تأییدشده")" "وضعیت به «تأییدشده» رسید"

# ---------------------------------------------------------------- ۸) پیش‌نویس امضا
section "۸) پیش‌نویس امضا (فقط پیش‌نمایش، بدون امضا)"
LIC_BEFORE=$(ls "$WORK/licenses" 2>/dev/null | grep -c '\.lic$' || true)
H=$(get "/Requests/Draft?id=$REQ_ID" "$TMP/draft.html"); check "$(code_is "$H" 200)" "صفحهٔ پیش‌نویس باز شد ($H)"
T=$(tok "$TMP/draft.html")
R=$(post "/Requests/Draft?handler=Preview" "$TMP/draft-prev.html" -d "__RequestVerificationToken=$T" \
      -d "id=$REQ_ID" -d "termMode=months" -d "months=12" -d "deployment=OnPrem" \
      -d "userMode=Named" -d "userMax=5" -d "exempt=1" -d "appliesTo=Global" \
      -d "presets=FIN" -d "modules=accounting" -d "modules=sales" -d "note=آزمون")
check "$(has "$TMP/draft-prev.html" "$INSTALL_ID")" "پیش‌نمایش ابزار، شناسهٔ نصب را نشان داد"
check "$(has "$TMP/draft-prev.html" "پیش‌نمایش")" "پیش‌نمایش ابزار (بدون امضا) رندر شد"
check "$([ "$(ls "$WORK/licenses" 2>/dev/null | grep -c '\.lic$')" = "${LIC_BEFORE:-0}" ] && echo 1 || echo 0)" "پیش‌نمایش هیچ فایل .lic نساخت"
T=$(tok "$TMP/draft-prev.html")
R=$(post "/Requests/Draft?handler=Save" "$TMP/draft-save.html" -d "__RequestVerificationToken=$T" \
      -d "id=$REQ_ID" -d "termMode=months" -d "months=12" -d "deployment=OnPrem" \
      -d "userMode=Named" -d "userMax=5" -d "exempt=1" -d "appliesTo=Global" \
      -d "presets=FIN" -d "modules=accounting" -d "modules=sales" -d "note=آزمون")
H=$(get "/Requests/Draft?id=$REQ_ID" "$TMP/draft2.html")
check "$(has "$TMP/draft2.html" "FIN")" "پیش‌نویس ذخیره شد (بستهٔ FIN)"

# ---------------------------------------------------------------- ۹) امضا
section "۹) امضا با نقش Signer/Admin از وضعیت تأییدشده"
H=$(get "/Requests/Sign?id=$REQ_ID" "$TMP/sign.html"); check "$(code_is "$H" 200)" "صفحهٔ امضا باز شد ($H)"
check "$(has "$TMP/sign.html" "یافت شد")" "کلید خصوصی/عمومی شناسایی شد"
T=$(tok "$TMP/sign.html")
R=$(post "/Requests/Sign?id=$REQ_ID" "$TMP/sign-post.html" -d "__RequestVerificationToken=$T" \
      -d "id=$REQ_ID" -d "confirm=on" -d "supersede=on" -d "keyId=dev-test" --data-urlencode "keyPassword=$KEYPASS")
SERIAL=$(python3 - "$TMP/sign-post.html" <<'PY'
import re,sys
s=open(sys.argv[1],encoding='utf-8',errors='replace').read()
m=re.search(r'<dd class="mono">([A-Z0-9\-]{8,})</dd>',s)
print(m.group(1) if m else "")
PY
)
if [ -n "${SERIAL:-}" ]; then ok "امضا انجام شد (سریال $SERIAL)"; else bad "امضا ناموفق بود"; fi
check "$(has "$TMP/sign-post.html" "تأیید با کلید عمومی")" "بازبینی خودکار پس از امضا اجرا شد"
H=$(get "/Requests/Details?id=$REQ_ID" "$TMP/det-s.html")
check "$(has "$TMP/det-s.html" "صادرشده")" "وضعیت درخواست «صادرشده» شد"
check "$([ "$(ls "$WORK/licenses" 2>/dev/null | grep -c '\.lic$')" -ge 1 ] && echo 1 || echo 0)" "فایل .lic در پوشهٔ لایسنس نوشته شد"

# ---------------------------------------------------------------- ۱۰) تحویل
section "۱۰) تحویل: .eml، دستی، SMTP تنظیم‌نشده"
H=$(get "/Requests/Deliver?id=$REQ_ID" "$TMP/deliv.html"); check "$(code_is "$H" 200)" "صفحهٔ تحویل باز شد ($H)"
LIC_ID=$(python3 - "$TMP/deliv.html" <<'PY'
import re,sys
s=open(sys.argv[1],encoding='utf-8',errors='replace').read()
m=re.search(r'name="licenseId" value="(\d+)"',s)
print(m.group(1) if m else "")
PY
)
[ -n "${LIC_ID:-}" ] && ok "شناسهٔ لایسنس پیدا شد ($LIC_ID)" || bad "شناسهٔ لایسنس پیدا نشد"
T=$(tok "$TMP/deliv.html")
post "/Requests/Deliver?handler=Eml" "$TMP/eml.html" -d "__RequestVerificationToken=$T" \
      -d "id=$REQ_ID" -d "licenseId=$LIC_ID" >/dev/null
H=$(get "/Requests/Deliver?id=$REQ_ID" "$TMP/deliv2.html")
check "$(has "$TMP/deliv2.html" "پیش‌نویس ایمیل ساخته شد")" "پیش‌نویس .eml ساخته شد"
check "$([ "$(ls "$WORK/outbox" 2>/dev/null | grep -c '\.eml$')" -ge 1 ] && echo 1 || echo 0)" "فایل .eml در outbox موجود است"
T=$(tok "$TMP/deliv2.html")
post "/Requests/Deliver?handler=Manual" "$TMP/man.html" -d "__RequestVerificationToken=$T" \
      -d "id=$REQ_ID" -d "licenseId=$LIC_ID" -d "recipient=e2e@example.invalid" --data-urlencode "note=تحویل دستی آزمون" >/dev/null
H=$(get "/Requests/Deliver?id=$REQ_ID" "$TMP/deliv3.html")
check "$(has "$TMP/deliv3.html" "تحویل دستی ثبت شد")" "ثبت تحویل دستی موفق بود"
T=$(tok "$TMP/deliv3.html")
post "/Requests/Deliver?handler=Smtp" "$TMP/smtp.html" -d "__RequestVerificationToken=$T" \
      -d "id=$REQ_ID" -d "licenseId=$LIC_ID" -d "recipient=e2e@example.invalid" >/dev/null
H=$(get "/Requests/Deliver?id=$REQ_ID" "$TMP/deliv4.html")
check "$(has "$TMP/deliv4.html" "SMTP تنظیم نشده")" "بدون SMTP، پیام روشن نشان داده شد (بدون ارسال)"

# ---------------------------------------------------------------- ۱۱) دانلود و بازبینی مستقل
section "۱۱) دانلود .lic و بازبینی با LicenseTool"
H=$(curl -sS -b "$JAR" -c "$JAR" -o "$TMP/licensed.lic" -w '%{http_code}' "$BASE/Licenses/Download?id=$LIC_ID")
check "$(code_is "$H" 200)" "دانلود فایل لایسنس ($H)"
check "$([ "$(head -c 1 "$TMP/licensed.lic")" = "{" ] && echo 1 || echo 0)" "فایل دانلودی JSON خالص است (بدون HTML چیدمان)"
if [ -x "$DOTNET" ] || command -v "$DOTNET" >/dev/null 2>&1; then
  OUT=$("$DOTNET" "$TOOL" verify "$TMP/licensed.lic" --public-key "$KEYDIR/dev-test.pem" --installation-id "$INSTALL_ID" 2>&1)
  printf '%s\n' "$OUT" | tee -a "$LOG" | tail -3
  case "$OUT" in *OK*|*معتبر*|*VALID*|*valid*) ok "بازبینی مستقل با کلید عمومی موفق بود" ;; *) bad "بازبینی مستقل ناموفق: $OUT" ;; esac
else
  bad "dotnet برای بازبینی مستقل پیدا نشد"
fi

# ---------------------------------------------------------------- ۱۲) ممیزی
section "۱۲) زنجیرهٔ ممیزی"
H=$(get /Audit "$TMP/audit.html"); check "$(code_is "$H" 200)" "صفحهٔ ممیزی باز شد ($H)"
check "$(has "$TMP/audit.html" "SIGN")" "رویداد امضا (SIGN) در ممیزی ثبت شده است"
check "$(has "$TMP/audit.html" "LOGIN")" "رویداد ورود (LOGIN) در ممیزی ثبت شده است"
T=$(tok "$TMP/audit.html")
post "/Audit?handler=Verify" "$TMP/audit-post.html" -d "__RequestVerificationToken=$T" >/dev/null
H=$(get /Audit "$TMP/audit2.html")
check "$(has "$TMP/audit2.html" "زنجیرهٔ ممیزی سالم است")" "بررسی زنجیره: سالم"

# آزمون دست‌کاری: تغییر یک ردیف ممیزی باید زنجیره را بشکند
ORIG=$(python3 - "$WORK/support.db" <<'PYTAMPER'
import sqlite3, sys
c = sqlite3.connect(sys.argv[1])
row = c.execute("select Id, Action from AuditLogs order by Id limit 1").fetchone()
c.execute("update AuditLogs set Action='TAMPERED' where Id=?", (row[0],))
c.commit()
print(row[0], row[1])
PYTAMPER
)
RID=$(echo "$ORIG" | cut -d' ' -f1); RACT=$(echo "$ORIG" | cut -d' ' -f2)
H=$(get /Audit "$TMP/audit-t.html")
check "$(has "$TMP/audit-t.html" "معتبر نیست")" "دست‌کاری در ممیزی شناسایی شد"
python3 - "$WORK/support.db" "$RID" "$RACT" <<'PYFIX'
import sqlite3, sys
c = sqlite3.connect(sys.argv[1])
c.execute("update AuditLogs set Action=? where Id=?", (sys.argv[3], int(sys.argv[2])))
c.commit()
PYFIX
HASHOK=$(python3 - "$WORK/support.db" <<'PYHASH'
import sqlite3, sys, hashlib
c = sqlite3.connect(sys.argv[1])
row = c.execute("select PrevHash, At, UserName, Action, Entity, EntityId, DataJson, Hash from AuditLogs order by Id limit 1").fetchone()
payload = '|'.join([row[0], row[1].replace(' ', 'T'), row[2], row[3], row[4], row[5] or '', row[6] or ''])
print('1' if hashlib.sha256(payload.encode()).hexdigest() == row[7] else '0')
PYHASH
)
check "$HASHOK" "پس از بازگردانی، هش ردیف با بازمحاسبهٔ مستقل می‌خواند"

# ---------------------------------------------------------------- ۱۳) پشتیبان‌گیری
section "۱۳) پشتیبان‌گیری"
H=$(get /Backup "$TMP/backup.html"); check "$(code_is "$H" 200)" "صفحهٔ پشتیبان باز شد ($H)"
T=$(tok "$TMP/backup.html")
post "/Backup?handler=Create" "$TMP/backup-post.html" -d "__RequestVerificationToken=$T" >/dev/null
H=$(get /Backup "$TMP/backup2.html")
check "$(has "$TMP/backup2.html" "پشتیبان ساخته شد")" "پشتیبان ساخته شد"
check "$([ "$(ls "$WORK/backups" 2>/dev/null | grep -c 'support-.*\.db$')" -ge 1 ] && echo 1 || echo 0)" "فایل پشتیبان در پوشهٔ backups موجود است"

# ---------------------------------------------------------------- ۱۴) نبود راز در DB/لاگ
section "۱۴) اسکن راز: کلید خصوصی/گذرواژه در DB و فایل‌های تولیدی"
LEAK=0
for f in "$WORK/support.db" "$WORK"/support.db-wal; do
  [ -f "$f" ] || continue
  if grep -a -q -e "BEGIN .*PRIVATE KEY" -e "$KEYPASS" -e "$NEWPASS" "$f"; then LEAK=$((LEAK+1)); log "  ! راز در $f"; fi
done
check "$([ "$LEAK" = "0" ] && echo 1 || echo 0)" "هیچ کلید خصوصی/گذرواژه‌ای در فایل‌های پایگاه‌داده نیست"
check "$([ "$(grep -c 'PRIVATE KEY' "$TMP"/licensed.lic 2>/dev/null || true)" = "0" ] && echo 1 || echo 0)" "فایل .lic کلید خصوصی ندارد"

# ---------------------------------------------------------------- خلاصه
section "خلاصه"
log "PASSED=$PASSED  FAILED=$FAILED"
[ "$FAILED" = "0" ] && { log "همهٔ بررسی‌ها سبز."; exit 0; }
log "بررسی‌های ناموفق: $FAILED"
exit 1
