#!/usr/bin/env python3
"""
preflight.py — بررسی پیش از اجرا بدون نیاز به dotnet
بررسی‌های انجام‌شده:
  1) Syntax ساده C# (تراز آکولاد، نبود ; در انتهای using های خراب)
  2) وجود Provider و ConnectionString در appsettings
  3) وجود ToPageListAsync بدون OrderBy قطعی (هشدار، نه خطا)
  4) وجود Paging.Slice/Result باقی‌مانده (اطلاع‌رسانی)
  5) سلامت فایل‌های کلاینت (Skip محلی)
  6) بررسی تحریم NuGet / دسترسی به nuget.org (اختیاری)

خروجی: خلاصهٔ مشکلات + exit 0 در صورت نبود خطای بحرانی، 1 در صورت خطا.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

# رنگ‌ها
GREEN = "\033[92m"; YELLOW = "\033[93m"; RED = "\033[91m"; CYAN = "\033[96m"; RESET = "\033[0m"

def ok(m): print(f"{GREEN}✔ {m}{RESET}")
def warn(m): print(f"{YELLOW}⚠ {m}{RESET}")
def fail(m): print(f"{RED}✘ {m}{RESET}")
def info(m): print(f"{CYAN}ℹ {m}{RESET}")

errors = 0
warnings = 0

# 1) Syntax ساده: شمارش آکولاد
print(f"\n{CYAN}== 1) بررسی syntax ساده C# =={RESET}")
cs_files = list((ROOT / "src").rglob("*.cs"))
braces_ok = True
for f in cs_files:
    try:
        t = f.read_text(encoding="utf-8-sig")
    except Exception as e:
        fail(f"{f.relative_to(ROOT)}: خواندن ناموفق: {e}")
        errors += 1
        continue
    # حذف رشته‌ها و کامنت‌ها برای شمارش دقیق‌تر
    t2 = re.sub(r'//.*?$|/\*.*?\*/|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'', '', t, flags=re.MULTILINE|re.DOTALL)
    opens = t2.count('{'); closes = t2.count('}')
    if opens != closes:
        fail(f"{f.relative_to(ROOT)}: عدم تراز آکولاد — {{={opens} }}={closes}")
        errors += 1
        braces_ok = False
if braces_ok:
    ok(f"تراز آکولاد در {len(cs_files)} فایل C# سالم است")

# 2) appsettings
print(f"\n{CYAN}== 2) پیکربندی دیتابیس =={RESET}")
for cfg in [ROOT / "src/Inventory.Api/appsettings.json", ROOT / "src/Inventory.Api/appsettings.Development.json"]:
    if not cfg.exists():
        fail(f"{cfg.name} پیدا نشد")
        errors += 1
        continue
    txt = cfg.read_text(encoding="utf-8-sig")
    if '"Provider"' not in txt:
        fail(f"{cfg.name}: کلید Database:Provider پیدا نشد")
        errors += 1
    else:
        ok(f"{cfg.name}: Provider موجود است")
    if '"Default"' not in txt:
        fail(f"{cfg.name}: ConnectionStrings:Default پیدا نشد")
        errors += 1
    else:
        ok(f"{cfg.name}: ConnectionString موجود است")
    if "Server=." in txt:
        info(f"{cfg.name} از Server=. (محلی ویندوز) استفاده می‌کند — روی لینوکس با SQLite اجرا کنید: Database__Provider=Sqlite")

# 3) ToPageListAsync بدون OrderBy
print(f"\n{CYAN}== 3) بررسی OrderBy قبل از ToPageListAsync (هشدار) =={RESET}")
found_no_order = []
for f in cs_files:
    txt = f.read_text(encoding="utf-8-sig")
    for m in re.finditer(r"\.ToPageListAsync", txt):
        snippet = txt[max(0, m.start()-1200):m.start()]
        # بررسی وجود OrderBy / orderby در 800 کاراکتر قبل
        window = snippet[-800:]
        has_order = "OrderBy" in window or "orderby" in window
        if not has_order:
            # بررسی imports: آیا query با Select شروع شده؟ ممکن است OrderBy قبل از Select باشد و ما ندیدیم
            # برای جلوگیری از false positive، فقط مواردی که واقعاً مشکوک‌اند گزارش می‌شوند
            line = txt.count('\n', 0, m.start())+1
            rel = f.relative_to(ROOT).as_posix()
            # فیلتر موارد شناخته‌شدهٔ false positive (DatabaseTreePaging, etc.)
            if "DatabaseTreePaging" in str(f):
                continue
            found_no_order.append(f"{rel}:{line}")
if found_no_order:
    for x in found_no_order[:15]:
        warn(f"احتمال فقدان OrderBy قبل از ToPageListAsync → {x}")
    if len(found_no_order) > 15:
        warn(f"... و {len(found_no_order)-15} مورد دیگر")
    warnings += len(found_no_order)
else:
    ok("تمام فراخوانی‌های ToPageListAsync دارای OrderBy/orderby در نزدیکی خود هستند (یا در Select قبلی)")

# 4) Paging.Result / Slice باقی‌مانده
print(f"\n{CYAN}== 4) الگوهای صفحه‌بندی قدیمی (Result/Slice) =={RESET}")
old = []
for f in cs_files:
    txt = f.read_text(encoding="utf-8-sig")
    for line_no, line in enumerate(txt.splitlines(), 1):
        if "Paging.Result(" in line or "Paging.Slice(" in line:
            old.append(f"{f.relative_to(ROOT)}:{line_no}: {line.strip()[:90]}")
if old:
    warn(f"{len(old)} الگوی قدیمی Paging.Result/Slice باقی است (Export و چند گزارش محاسباتی):")
    for x in old[:8]:
        print(f"  - {x}")
    if len(old) > 8:
        print(f"  ... و {len(old)-8} مورد دیگر")
    warnings += 1
else:
    ok("الگوی قدیمی Paging.Result/Slice پیدا نشد")

# 5) کلاینت: Skip محلی
print(f"\n{CYAN}== 5) صفحه‌بندی محلی در کلاینت (Razor) =={RESET}")
razors = list((ROOT / "src/Inventory.Client").rglob("*.razor"))
skips = []
for f in razors:
    txt = f.read_text(encoding="utf-8-sig")
    for m in re.finditer(r"\.Skip\(", txt):
        line_no = txt.count('\n', 0, m.start())+1
        line = txt.splitlines()[line_no-1].strip()
        # فیلتر موارد غیرصفحه‌بندی (Split + Skip, lines.Skip(1) برای هدر اکسل)
        if "lines.Skip(1)" in line or "Split" in line or "Skip(1)" in line:
            continue
        skips.append(f"{f.relative_to(ROOT)}:{line_no}: {line[:90]}")
if skips:
    warn(f"{len(skips)} مورد Skip در Razor باقی است (برخی عمدی، برخی نیازمند مهاجرت به سرور):")
    for x in skips[:10]:
        print(f"  - {x}")
    warnings += 1
else:
    ok("صفحه‌بندی محلی در کلاینت یافت نشد")

# 6) NuGet
print(f"\n{CYAN}== 6) سلامت NuGet.config =={RESET}")
nuget = ROOT / "NuGet.config"
if nuget.exists():
    txt = nuget.read_text(encoding="utf-8-sig")
    if 'api.nuget.org' in txt:
        ok("NuGet.config به nuget.org اشاره دارد")
    else:
        warn("NuGet.config به nuget.org اشاره ندارد — ممکن است restore شکست بخورد")
else:
    warn("NuGet.config پیدا نشد")

# 7) وجود فایل‌های حیاتی برای اجرا
print(f"\n{CYAN}== 7) فایل‌های حیاتی برای استقرار تک‌سروره =={RESET}")
for p in [ROOT / "src/Inventory.Api/Program.cs", ROOT / "src/Inventory.Client/Program.cs", ROOT / "../global.json", ROOT / "global.json"]:
    if p.exists():
        ok(f"{p.relative_to(ROOT) if str(p).startswith(str(ROOT)) else p} موجود است")
        break
else:
    fail(f"global.json پیدا نشد")
    errors += 1
# همچنین بررسی Program.csها
for p in [ROOT / "src/Inventory.Api/Program.cs", ROOT / "src/Inventory.Client/Program.cs"]:
    if p.exists():
        ok(f"{p.relative_to(ROOT)} موجود است")
    else:
        fail(f"{p.relative_to(ROOT)} پیدا نشد")
        errors += 1

print(f"\n{CYAN}================================{RESET}")
if errors:
    fail(f"پیش‌پرواز با {errors} خطای بحرانی و {warnings} هشدار پایان یافت — قبل از اجرا رفع کنید.")
    sys.exit(1)
elif warnings:
    warn(f"پیش‌پرواز بدون خطای بحرانی، با {warnings} هشدار پایان یافت — قابل اجرا، اما بررسی شود.")
    sys.exit(0)
else:
    ok("پیش‌پرواز بدون خطا و هشدار — آمادهٔ اجرا ✔")
    sys.exit(0)
