#!/usr/bin/env bash
# =============================================================================
#  gen-codeowners.sh — تولید .github/CODEOWNERS از tools/ownership-map.csv
#
#  چرا از CSV تولید می‌شود و دستی نوشته نمی‌شود:
#    ۳۷ ماژول × ۴ مسیر = ~۱۳۰ قاعده. وقتی ماژول جدیدی اضافه شود،
#    این اسکریپت را دوباره اجرا می‌کنید و CODEOWNERS خودش به‌روز می‌شود.
#
#  روش کار:
#    1) ستون owner_github را در tools/ownership-map.csv پر کنید (مثلاً @Lak-dev)
#    2) ./tools/gen-codeowners.sh
#    3) ماژول‌هایی که مالک ندارند «کامنت‌شده» نوشته می‌شوند تا فایل همیشه
#       معتبر بماند و گیت‌هاب خطای «owner نامعتبر» ندهد.
# =============================================================================
set -euo pipefail
cd "$(dirname "$0")/.."

CSV="tools/ownership-map.csv"
OUT=".github/CODEOWNERS"
# مالک پیش‌فرض برای فایل‌های زیرساختی مشترک (یک هندل واقعی شناخته‌شده)
DEFAULT_OWNER="${DEFAULT_OWNER:-@mohammadforoughifar}"

[ -f "$CSV" ] || { echo "❌ $CSV پیدا نشد"; exit 1; }
mkdir -p .github

{
cat <<HEADER
# =============================================================================
#  CODEOWNERS — مالکیت ماژول‌ها
#
#  این فایل «خودکار» تولید شده است. دستی ویرایشش نکنید.
#  منبع حقیقت: tools/ownership-map.csv
#  تولید مجدد:  ./tools/gen-codeowners.sh
#
#  اثر: روی هر PR، گیت‌هاب خودش reviewer مربوط به همان ماژول را assign می‌کند.
#  اگر branch protection فعال باشد، بدون تأیید آن مالک امکان merge نیست.
#  ⚠️ روی مخزن عمومی حتی با پلن Free هم CODEOWNERS کار می‌کند.
# =============================================================================

# --- زیرساخت مشترک (همهٔ تغییرات اینجا نیازمند بازبینی ارشد است) ------------
/inventory/src/Inventory.Api/Program.cs                    $DEFAULT_OWNER
/inventory/src/Inventory.Api/appsettings*.json             $DEFAULT_OWNER
/inventory/src/Inventory.Api/Data/                         $DEFAULT_OWNER
/inventory/src/Inventory.Api/Migrations/                   $DEFAULT_OWNER
/inventory/src/Inventory.Shared/                           $DEFAULT_OWNER
/inventory/src/Inventory.Api/Services/Core/                $DEFAULT_OWNER
/inventory/src/Inventory.Api/Services/System/              $DEFAULT_OWNER
/inventory/Inventory.sln                                   $DEFAULT_OWNER
/inventory/Directory.Build.props                           $DEFAULT_OWNER
/inventory/Directory.Build.targets                         $DEFAULT_OWNER
/.github/                                                  $DEFAULT_OWNER
/tools/                                                    $DEFAULT_OWNER
/global.json                                               $DEFAULT_OWNER

HEADER

filled=0; pending=0
tail -n +2 "$CSV" | while IFS=, read -r mod svc ctl ent pages lines pgcount owner; do
  [ -z "$mod" ] && continue
  paths=""
  for d in "$svc" "$ctl" "$ent" "$pages"; do [ -n "$d" ] && paths="$paths $d"; done
  [ -z "$paths" ] && continue
  if [ -n "$owner" ]; then
    echo "# --- ماژول $mod  (${lines} خط کد، ${pgcount} صفحه) ---"
    for d in $paths; do printf '%-58s %s\n' "/$d/" "$owner"; done
    echo
  else
    echo "# ⏳ ماژول $mod  (${lines} خط، ${pgcount} صفحه) — مالک تعیین نشده."
    echo "#    ستون owner_github را در tools/ownership-map.csv پر کنید و دوباره اجرا کنید."
    for d in $paths; do echo "# /$d/   @TODO-$mod"; done
    echo
  fi
done

cat <<'FOOTER'
# --- زیرپروژه‌های جدا ----------------------------------------------------------
# ⏳ مالک تعیین نشده — برای فعال‌کردن، هندل واقعی بگذارید و کامنت را بردارید
# /android/                                                 @android-owner
# /ocr-service/                                             @ocr-owner

# --- قاعدهٔ پایانی: هرچیز بی‌مالک به مالک پیش‌فرض می‌رسد -----------------------
*                                                           @mohammadforoughifar
FOOTER
} > "$OUT"

filled=$(tail -n +2 "$CSV" | awk -F, '$8!=""' | wc -l | tr -d ' ')
pending=$(tail -n +2 "$CSV" | awk -F, '$8==""' | wc -l | tr -d ' ')
echo "✅ $OUT ساخته شد"
echo "   ماژول با مالک تعیین‌شده : $filled"
echo "   ماژول در انتظار مالک     : $pending   (به‌صورت کامنت نوشته شدند)"
echo
echo "   گام بعدی: ستون owner_github را در $CSV پر کنید، سپس دوباره اجرا کنید."
