#!/usr/bin/env bash
# =============================================================================
#  install-hooks.sh — هر عضو تیم «یک بار» این را اجرا می‌کند
#
#  کارها:
#    1) هویت git را می‌پرسد و تنظیم می‌کند (ریشهٔ اصلی مشکل attribution)
#    2) هویت فعلی را با ۲۱ هویت عمومیِ تاریخچهٔ مخزن چک می‌کند و هشدار می‌دهد
#    3) قلاب‌ها را با core.hooksPath فعال می‌کند (چون .git/hooks ورژن‌کنترل نمی‌شود)
#    4) اگر tools/team-members.txt خالی است، قالبش را می‌سازد
# =============================================================================
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$(pwd)"
MEMBERS="tools/team-members.txt"

echo "=============================================================="
echo " نصب قلاب‌های تیمی — $ROOT"
echo "=============================================================="

# --------------------------------------------------------------- گام ۱ -------
cur_name="$(git config user.name  || true)"
cur_mail="$(git config user.email || true)"
echo
echo "هویت فعلی git در این مخزن:"
echo "    user.name  = ${cur_name:-<تنظیم نشده>}"
echo "    user.email = ${cur_mail:-<تنظیم نشده>}"

# الگوهای عمومی که در تاریخچهٔ این مخزن پیدا شده‌اند (نه یک انسان مشخص)
GENERIC='(arena|agent|bot|dev@|team|@local|@invalid|noreply\.github\.com)'
if [ -z "$cur_name" ] || [ -z "$cur_mail" ] || printf '%s %s' "$cur_name" "$cur_mail" | grep -qEi "$GENERIC"; then
  echo
  echo "⚠️  این هویت «یک انسان مشخص» نیست. همین باعث شده تاریخچهٔ مخزن"
  echo "    نتواند بگوید چه کسی روی چه ماژولی کار کرده."
  echo
  read -r -p "نام کامل شما (مثلاً «Ali Lak»): " new_name
  read -r -p "ایمیل شما: " new_mail
  [ -n "$new_name" ] && [ -n "$new_mail" ] || { echo "❌ هر دو فیلد لازم است."; exit 1; }
  git config user.name  "$new_name"
  git config user.email "$new_mail"
  cur_name="$new_name"; cur_mail="$new_mail"
  echo "✅ تنظیم شد: $cur_name <$cur_mail>"
else
  echo "✅ هویت شما مشخص است."
fi

# --------------------------------------------------------------- گام ۲ -------
echo
if [ ! -f "$MEMBERS" ]; then
  cat > "$MEMBERS" <<EOF
# =============================================================================
#  اعضای تیم — منبع حقیقت برای attribution
#
#  قالب هر خط:   نام کامل <ایمیل>   @هندل-گیت‌هاب
#  خط‌های با # و خط‌های خالی نادیده گرفته می‌شوند.
#
#  این فهرست دو کار می‌کند:
#    1) قلاب commit-msg اجازه می‌دهد commit ثبت شود
#    2) ستون owner_github در tools/ownership-map.csv باید از همین هندل‌ها باشد
#
#  ⚠️ ایمیل اینجا باید «دقیقاً» همان باشد که در git config user.email دارید.
# =============================================================================
$cur_name <$cur_mail>   @CHANGE-ME
EOF
  echo "✅ $MEMBERS ساخته شد — بقیهٔ اعضای تیم را اضافه کنید:"
  echo "     nano $MEMBERS"
else
  if grep -qxF "$cur_name <$cur_mail>" <(sed -E 's/[[:space:]]+@.*$//' "$MEMBERS" | grep -v '^#' | grep -v '^\s*$'); then
    echo "✅ نام شما در $MEMBERS ثبت است."
  else
    echo "⚠️  هویت شما در $MEMBERS نیست. این خط را به آن فایل اضافه کنید:"
    echo "       $cur_name <$cur_mail>   @هندل-گیت‌هاب-شما"
    echo "    تا وقتی اضافه نشده، commit‌هایتان فقط با تریلر Requested-by پذیرفته می‌شوند."
  fi
fi

# --------------------------------------------------------------- گام ۳ -------
echo
chmod +x .githooks/* 2>/dev/null || true
git config core.hooksPath .githooks
echo "✅ core.hooksPath = $(git config core.hooksPath)"
echo "   فعال: $(ls .githooks | tr '\n' ' ')"

# --------------------------------------------------------------- گام ۴ -------
echo
cat <<'DONE'
==============================================================
 تمام. حالا این قوانین خودکار اعمال می‌شوند:

   • نام برنچ باید  <ماژول>/<کلیدکار>-<توضیح>  باشد
   • هر commit باید هویت یک انسان مشخص را داشته باشد
     (یا نویسنده عضو تیم، یا تریلر Requested-by)
   • push مستقیم به main بسته است — فقط از راه PR

 گام‌های بعدی برای مسئول تیم:
   1) tools/team-members.txt   را با همهٔ اعضا پر کنید
   2) tools/ownership-map.csv  → ستون owner_github را پر کنید
   3) ./tools/gen-codeowners.sh
   4) تنظیم branch protection روی main در Settings گیت‌هاب
   5) ./tools/create-labels.sh  (با gh CLI)
==============================================================
DONE
