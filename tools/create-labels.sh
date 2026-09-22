#!/usr/bin/env bash
# =============================================================================
#  create-labels.sh — ساخت برچسب‌های ماژول + وضعیت روی مخزن گیت‌هاب
#
#  چرا: با ۳۷ ماژول، «کی روی چه بخشی کار کرده» بدون فیلتر ماژول قابل پاسخ نیست.
#  برچسب module:* همان چیزی است که بورد GitHub Projects را به کد وصل می‌کند:
#  روی بورد یک فیلد می‌سازید و از برچسب PR مقدارش را می‌گیرد.
#
#  نیازمند: gh CLI و لاگین‌شده
#      gh auth login
#
#  اجرا:  ./tools/create-labels.sh            (خشک — فقط نشان می‌دهد چه می‌سازد)
#         ./tools/create-labels.sh --apply    (واقعاً می‌سازد)
# =============================================================================
set -euo pipefail
cd "$(dirname "$0")/.."

APPLY=0; [ "${1:-}" = "--apply" ] && APPLY=1
CSV="tools/ownership-map.csv"

command -v gh >/dev/null || { echo "❌ gh CLI نصب نیست.  https://cli.github.com"; exit 1; }

# نام ماژول -> رنگ. ماژول‌های هم‌خانواده رنگ نزدیک می‌گیرند تا روی بورد
# با یک نگاه دسته‌بندی‌شان پیدا باشد.
color_of() {
  case "$1" in
    Hr*|BonHr)        echo "5319e7" ;;   # بنفش — خانوادهٔ منابع انسانی
    Fa*)              echo "d4c5f9" ;;   # بنفش روشن — سامانهٔ فا
    Office|Chat|Notification) echo "0e8a16" ;;  # سبز — اتوماسیون و ارتباطات
    DocArchive|Export|Pdf|Watermark|ReportStudio|Reports) echo "1d76db" ;; # آبی — سند و گزارش
    Accounting|Invoicing|Treasury|Finance|Sales) echo "fbca04" ;;  # زرد — مالی
    Catalog|Warehousing|Stocktaking|ItAssets|Repairs|Orders|Cctv) echo "eb6420" ;; # نارنجی — انبار و اموال
    Projects)         echo "c5def5" ;;
    Dashboards|Panel|Settings) echo "bfdadc" ;;
    System|Core)      echo "333333" ;;   # خاکستری تیره — زیرساخت
    *)                echo "ededed" ;;
  esac
}

echo "=============================================================="
echo " برچسب‌های ماژول (از $CSV)"
echo "=============================================================="
cmds=()
while IFS=, read -r mod rest; do
  [ "$mod" = "module" ] && continue
  [ -z "$mod" ] && continue
  name="module:$mod"
  col="$(color_of "$mod")"
  desc="ماژول $mod — مالکیت و فیلتر بورد"
  cmds+=("gh label create \"$name\" --color \"$col\" --description \"$desc\" --force")
  printf "  %-32s #%s\n" "$name" "$col"
done < "$CSV"

echo
echo "=============================================================="
echo " برچسب‌های وضعیت و نوع کار"
echo "=============================================================="
while read -r name col desc; do
  [ -z "$name" ] && continue
  cmds+=("gh label create \"$name\" --color \"$col\" --description \"$desc\" --force")
  printf "  %-32s #%s   %s\n" "$name" "$col" "$desc"
done <<'LBL'
status:blocked            b60205  منتظر رفع مانع
status:needs-review       fef2c0  منتظر بازبینی
status:wip                ffffff  در حال انجام
type:bug                  d73a4a  خطا
type:feature              a2eeef  قابلیت جدید
type:refactor             cfd3d7  بازآرایی بدون تغییر رفتار
type:db-migration         5319e7  تغییر ساختار پایگاه داده
type:docs                 0075ca  مستندسازی
area:infrastructure       333333  زیرساخت مشترک — نیازمند بازبینی ارشد
priority:high             b60205  اولویت بالا
priority:low              d4c5f9  اولویت پایین
review:agent-generated    e4e669  کد توسط ایجنت AI تولید شده — بازبینی دقیق‌تر لازم است
LBL

echo
echo "=============================================================="
if [ "$APPLY" = "0" ]; then
  echo " حالت خشک: ${#cmds[@]} برچسب ساخته «نشد»."
  echo " برای ساخت واقعی:  ./tools/create-labels.sh --apply"
  exit 0
fi
echo " ساخت ${#cmds[@]} برچسب ..."
echo "=============================================================="
ok=0; bad=0
for c in "${cmds[@]}"; do
  if eval "$c" >/dev/null 2>&1; then ok=$((ok+1)); else bad=$((bad+1)); echo "  ⚠️  ناموفق: $c"; fi
done
echo "✅ $ok ساخته شد، $bad ناموفق."
echo
echo "گام بعدی: روی بورد GitHub Projects یک فیلد تک‌انتخابی «Module» بسازید"
echo "و مقادیرش را از همین برچسب‌های module:* پر کنید."
