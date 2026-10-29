"""اعمال تغییرات چیدمان روی «قالب اصلی MRT» شرکت‌های سربرگ هومپا (۱۰۰۳ و ۳).

چه چیزی را عوض می‌کند؟
  ۱) برچسب «:گیرنده نامه» در قالب خالی می‌شود (گیرنده بدون برچسب چاپ شود).
  ۲) برچسب «:عنوان نامه» به «:موضوع» تغییر می‌کند و روی ردیف موضوع می‌نشیند.
  ۳) گیرنده، موضوع و متن با همان اعدادِ کد چاپ جابه‌جا می‌شوند
     (`HomepaLetterheadLayout.ContentTop` = پایین نوار آبی + فاصلهٔ تأییدشدهٔ کارفرما).
  ۴) کادر «رونوشت» از پایین صفحه به زیر متن و بالای کادرهای امضا منتقل و کوتاه می‌شود
     تا با تصویرهای امضا تلاقی نکند.

همهٔ اعداد از یک منبع می‌آیند (ثابت‌های C#) و روی هر ۱۲ فایل MRT نوشته می‌شوند؛
اجرای دوباره بی‌خطر است (idempotent).
"""

from __future__ import annotations

import re
from pathlib import Path

ROOT = None
for parent in Path(__file__).resolve().parents:
    if (parent / "inventory" / "src").is_dir():
        ROOT = parent
        break
if ROOT is None:
    raise SystemExit("پوشهٔ پروژه پیدا نشد")

LAYOUT = ROOT / "src" / "Inventory.Api" / "Services" / "Office" / "Outgoing" / "HomepaLetterheadLayout.cs"
COMPANIES = ("1003", "3")
SIZES = ("A4", "A5")

# فونت متن/بلوک اطلاعات در کد چاپ
BODY_FONT = {"A4": 11.0, "A5": 9.0}
# Spacer(col, isA5 ? 10f : 12f)
SPACER = {"A4": 12.0, "A5": 10.0}
LINE_HEIGHT = 1.55
PARAGRAPH_SPACING = 6.0


def content_top() -> dict[str, float]:
    """پایین نوار آبی + فاصلهٔ شروع متن، از خودِ کلاس چیدمان."""
    source = LAYOUT.read_text(encoding="utf-8")
    band = re.search(r"BandBottom\(bool isA5\) => isA5 \? ([\d.]+)f : ([\d.]+)f", source)
    gap = re.search(r"ContentStartGap = ([\d.]+)f", source)
    if band is None or gap is None:
        raise SystemExit("BandBottom/ContentStartGap در HomepaLetterheadLayout پیدا نشد")
    offset = float(gap.group(1))
    return {
        "A5": float(band.group(1)) + offset,
        "A4": float(band.group(2)) + offset,
    }


def rectangles(size: str) -> dict[str, tuple[float, float, float, float]]:
    """مختصات هدف (x, y, w, h) به اینچ، برای هر جزء قالب."""
    margin = 0.39
    top_pt = content_top()[size]
    font = BODY_FONT[size]
    row = font * LINE_HEIGHT
    spacing = max(PARAGRAPH_SPACING, font * 0.45)

    receiver_top = top_pt
    subject_top = receiver_top + row + spacing
    body_top = subject_top + row + SPACER[size]
    to_inch = lambda pt: round(pt / 72 - margin, 4)  # noqa: E731

    if size == "A4":
        geometry = {
            "receiver": (-0.04, to_inch(receiver_top), 6.0, 0.28),
            "subject": (-0.04, to_inch(subject_top), 6.0, 0.30),
            "body": (0.01, to_inch(body_top), 7.35, round(7.0 - to_inch(body_top), 4)),
            "body_off": (-0.04, to_inch(body_top), 7.45, round(7.0 - to_inch(body_top), 4)),
            "copy": (4.86, 7.02, 2.5, 0.60),
            "receiver_caption": (6.06, to_inch(receiver_top), 1.35, 0.28),
            "subject_caption": (6.06, to_inch(subject_top), 1.35, 0.30),
        }
    else:
        geometry = {
            "receiver": (-0.39, to_inch(receiver_top), 4.08, 0.22),
            "subject": (-0.39, to_inch(subject_top), 4.08, 0.22),
            "body": (-0.29, to_inch(body_top), 4.8, round(4.46 - to_inch(body_top), 4)),
            "body_off": (-0.29, to_inch(body_top), 4.9, round(4.46 - to_inch(body_top), 4)),
            "copy": (2.71, 4.09, 1.9, 0.58),
            "receiver_caption": (3.81, to_inch(receiver_top), 1.0, 0.22),
            "subject_caption": (3.81, to_inch(subject_top), 1.0, 0.22),
        }
    return geometry


def component_block(text: str, name: str) -> tuple[int, int]:
    """بازهٔ یک جزء (مثلاً Text8) در متن خام MRT."""
    match = re.search(r"<" + name + r" [^>]*>", text)
    if match is None:
        raise SystemExit(f"جزء {name} پیدا نشد")
    close = text.index("</" + name + ">", match.start())
    return match.start(), close + len("</" + name + ">") + 1


def patch(path: Path, size: str) -> list[str]:
    text = path.read_text(encoding="utf-8-sig")
    geometry = rectangles(size)
    changes: list[str] = []

    targets = {
        "Text8": "receiver",
        "Text5": "subject",
        "Text7": "body",
        "Text6": "body_off",
        "Text11": "copy",
        "Text18": "receiver_caption",
        "Text19": "subject_caption",
    }
    for name, key in targets.items():
        start, end = component_block(text, name)
        block = text[start:end]
        x, y, w, h = geometry[key]
        rect = f"{x},{y},{w},{h}"
        new_block, count = re.subn(r"<ClientRectangle>[^<]*</ClientRectangle>",
                                   f"<ClientRectangle>{rect}</ClientRectangle>", block, count=1)
        if count != 1:
            raise SystemExit(f"{path.name}:{name} مستطیل ندارد")
        if new_block != block:
            changes.append(f"{name} → {rect}")
        text = text[:start] + new_block + text[end:]

    # ۱) برچسب گیرنده حذف (خالی)  ۲) برچسب عنوان → موضوع
    if ":گیرنده نامه" in text:
        text = text.replace("<Text>:گیرنده نامه</Text>", "<Text></Text>")
        changes.append("برچسب «گیرنده نامه» حذف شد")
    if ":عنوان نامه" in text:
        text = text.replace("<Text>:عنوان نامه</Text>", "<Text>:موضوع</Text>")
        changes.append("برچسب «عنوان نامه» → «موضوع»")

    path.write_text(text, encoding="utf-8")
    return changes


def main() -> None:
    total = 0
    for company in COMPANIES:
        for size in SIZES:
            relative = Path("src/Inventory.Api/Reports/OutgoingLetters/Companies") / company / f"Outgoing-{size}.mrt"
            targets = [
                ROOT / relative,
                ROOT / "inventory" / relative,
                ROOT / "Report" / "outgoingletter" / company / f"Report_{size}_OutgoingLetter.mrt",
            ]
            for target in targets:
                if not target.exists():
                    print(f"… پیدا نشد: {target.relative_to(ROOT)}")
                    continue
                changes = patch(target, size)
                total += 1
                print(f"✔ {target.relative_to(ROOT)}  ({len(changes)} تغییر)")
    print(f"\n{total} فایل MRT به‌روز شد. contentTop = {content_top()}")


if __name__ == "__main__":
    main()
