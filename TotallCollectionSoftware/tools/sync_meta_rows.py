"""هم‌ترازی «مقادیر» سربرگ با «برچسب‌های چاپ‌شده» روی تصویر سربرگ شرکت ۱۰۰۳.

منطق: در QuestPDF مقدار با `AlignMiddle` وسط کادرِ قالب می‌نشیند؛ در استیمول‌سافت هم همین‌طور.
پس کافی است «مرکز کادر» را دقیقاً روی «مرکز جوهر برچسب» بگذاریم.

خروجی این اسکریپت (همه از یک منبع، بدون عدد دستی):
  • فایل‌های MRT (هر دو درخت + پوشهٔ منبع Report)  → ClientRectangle مقدارها
  • HomepaLetterheadLayout.cs (Fallback نامهٔ داخلی) → Top = MRT − مبدأ page.Header
  • ثابت‌های تست (int64 دقیق، سه رقم اعشار)
"""

from __future__ import annotations

import re
import xml.etree.ElementTree as ET
from pathlib import Path

from PIL import Image
import numpy as np

def _project_root() -> Path:
    """ریشهٔ پروژه = نزدیک‌ترین پوشهٔ والد که `inventory/src` دارد (مستقل از محل اسکریپت)."""
    for parent in Path(__file__).resolve().parents:
        if (parent / "inventory" / "src").is_dir():
            return parent
    raise SystemExit("پوشهٔ پروژه پیدا نشد")


ROOT = _project_root()
ASSET = ROOT / "Report" / "outgoingletter" / "1003" / "assets" / "homepa_company1003_letterhead.png"
A4 = (595.28, 841.89)
A5 = (419.53, 595.28)
HEADER_ORIGIN = {"A4": 24.0, "A5": 16.0}     # HomepaLetterheadLayout: مبدأ page.Header
MARGIN_TOP_INCH = 0.39
SLOTS = {"number": "LetterNumber", "date": "DateShams", "attachment": "Peyvast"}


def label_centers() -> dict[str, float]:
    """مرکز عمودی جوهر برچسب‌های «شماره/تاریخ/پیوست» روی تصویر سربرگ (نقطه، نسبت به A4)."""
    image = Image.open(ASSET).convert("L")
    width, height = image.size
    sx, sy = width / A4[0], height / A4[1]
    array = np.asarray(image).astype(float)
    # ستون برچسب‌ها: x بین ۹۵ تا ۱۴۰pt
    patch = array[: int(125 * sy), int(95 * sx):int(140 * sx)]
    bright = (patch > 200).sum(axis=1)

    groups: list[list[int]] = []
    for row, count in enumerate(bright):
        if count >= 2:
            if groups and row - groups[-1][-1] <= 4:
                groups[-1].append(row)
            else:
                groups.append([row])
    # فقط برچسب‌ها: ارتفاع گروه بیش از ۲۰ پیکسل و بالاتر از ۱۱۰pt (نوار خاکستری پایین حذف شود)
    groups = [g for g in groups if len(g) >= 20 and g[-1] / sy < 110]
    assert len(groups) == 3, f"سه برچسب پیدا نشد: {[len(g) for g in groups]}"
    return {
        "number": (groups[0][0] + groups[0][-1]) / 2 / sy,
        "date": (groups[1][0] + groups[1][-1]) / 2 / sy,
        "attachment": (groups[2][0] + groups[2][-1]) / 2 / sy,
    }


def heights(size: str) -> dict[str, float]:
    """ارتفاع کادرهای مقدار از خود قالب (اینچ) — بدون تغییر."""
    path = (ROOT / "src" / "Inventory.Api" / "Reports" / "OutgoingLetters" / "Companies"
            / "1003" / f"Outgoing-{size}.mrt")
    root = ET.parse(path).getroot()
    page = root.find("./Pages/Page1")
    result = {}
    for key, token in SLOTS.items():
        element = next(e for e in page.iter() if e.findtext("Text") == "{DataSource1." + token + "}")
        _, _, _, height = [float(v) for v in element.findtext("ClientRectangle").split(",")]
        result[key] = height
    return result


def targets() -> dict[str, dict[str, float]]:
    centers_a4 = label_centers()
    # تصویر سربرگ با FitUnproportionally روی A5 کشیده می‌شود → مقیاس عمودی = نسبت ارتفاع‌ها
    factor_a5 = A5[1] / A4[1]
    scale = {"A4": 1.0, "A5": factor_a5}
    out = {}
    for size in ("A4", "A5"):
        hs = heights(size)
        out[size] = {}
        for key, center in centers_a4.items():
            center_pt = center * scale[size]
            # مرکز کادر == مرکز برچسب  →  y = center/72 − margin − h/2
            # چهار رقم اعشار: همان عددی که در MRT نوشته می‌شود، تا Fallback هم دقیقاً هم‌تراز بماند.
            out[size][key] = round(center_pt / 72 - MARGIN_TOP_INCH - hs[key] / 2, 4)
    return out


def apply_mrt(y_inch: dict[str, dict[str, float]]) -> list[Path]:
    files: list[Path] = []
    for tree in ("src", "inventory/src"):
        for company in ("1003", "3"):
            for size in ("A4", "A5"):
                files.append(ROOT / tree / "Inventory.Api" / "Reports" / "OutgoingLetters"
                             / "Companies" / company / f"Outgoing-{size}.mrt")
    for company in ("1003", "3"):
        for size in ("A4", "A5"):
            files.append(ROOT / "Report" / "outgoingletter" / company / f"Report_{size}_OutgoingLetter.mrt")

    changed = []
    for path in files:
        if not path.exists():
            continue
        size = "A5" if "A5" in path.name else "A4"
        text = path.read_bytes().decode("utf-8")
        for key, token in SLOTS.items():
            pattern = re.compile(r'(<Text\d+[^>]*type="Text"[^>]*>)(.*?)(</Text\d+>)', re.S)

            def fix(match, token=token, key=key, size=size):
                body = match.group(2)
                if f"{{DataSource1.{token}}}" not in body:
                    return match.group(0)
                rect = re.search(r"<ClientRectangle>([^<]+)</ClientRectangle>", body)
                if not rect:
                    return match.group(0)
                parts = rect.group(1).split(",")
                parts[1] = f"{y_inch[size][key]:.4f}".rstrip("0").rstrip(".")
                return (match.group(1)
                        + body.replace(rect.group(0),
                                       f'<ClientRectangle>{",".join(parts)}</ClientRectangle>', 1)
                        + match.group(3))

            text = pattern.sub(fix, text)
        path.write_bytes(text.encode("utf-8"))
        changed.append(path)
    return changed


def apply_sidecar(y_inch: dict[str, dict[str, float]]) -> Path:
    path = (ROOT / "inventory" / "src" / "Inventory.Api" / "Services" / "Office" / "Outgoing"
            / "HomepaLetterheadLayout.cs")
    text = path.read_text(encoding="utf-8")
    for size in ("A4", "A5"):
        for key, method in (("number", "Number"), ("date", "Date"), ("attachment", "Attachment")):
            top = (MARGIN_TOP_INCH + y_inch[size][key]) * 72 - HEADER_ORIGIN[size]
            is_a5 = size == "A5"
            pattern = re.compile(rf"(static Field {method}\(bool isA5\) => isA5\s*\n\s*\? new\([^,]+,\s*)([\d.]+)(f)", re.S)
            if is_a5:
                text_new, count = pattern.subn(lambda m: f"{m.group(1)}{top:.4f}{m.group(3)}", text)
            else:
                pattern_a4 = re.compile(rf"(static Field {method}\(bool isA5\) => isA5\s*\n\s*[^\n]*\n\s*:\s*new\([^,]+,\s*)([\d.]+)(f)", re.S)
                text_new, count = pattern_a4.subn(lambda m: f"{m.group(1)}{top:.4f}{m.group(3)}", text)
            assert count == 1, f"{size}/{method}: الگو پیدا نشد"
            text = text_new
    path.write_text(text, encoding="utf-8")
    return path


if __name__ == "__main__":
    y = targets()
    print("y (اینچ) برای مرکز کادر = مرکز برچسب:")
    for size in ("A4", "A5"):
        print(" ", size, {k: round(v, 4) for k, v in y[size].items()})
        for key in y[size]:
            center = (MARGIN_TOP_INCH + y[size][key] + heights(size)[key] / 2) * 72
            print(f"     {key:11s} center = {center:.2f} pt")
    for path in apply_mrt(y):
        print("✔", path.relative_to(ROOT))
    print("✔", apply_sidecar(y).relative_to(ROOT))
