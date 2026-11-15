"""هم‌ترازی مقادیر سربرگ شرکت ۱۰۰۵ با خط‌چین‌های هنر خودش — از یک منبع.

منبع اعداد: `Services/Office/Outgoing/AriaLetterheadLayout.cs` (همان ثابت‌هایی که چاپ
می‌خواند). این ابزار همان اعداد را داخل قالب Stimulsoft بنویسد تا طراح در Designer همان
چیزی را ببیند که چاپ می‌شود و هر جابه‌جایی در قالب، از مسیر «مقادیر اعلام‌شدهٔ قالب»
(`LetterheadMetaFields`) مستقیماً روی چاپ بیاید.

نکتهٔ مختصات:
  • `AriaLetterheadLayout.Field.Left` مطلق است (از گوشهٔ چپ کاغذ).
  • `Field.Top` نسبت به مبدأ `page.Header` است (۲۴pt در A4 و ۱۶pt در A5)؛ برای MRT باید
    مبدأ اضافه شود: top_absolute = Field.Top + origin.
  • MRT مختصات را نسبت به حاشیهٔ صفحه (۰٫۳۹ اینچ) ذخیره می‌کند.

ظاهر مقادیر هم یک‌جا تنظیم می‌شود: قلم توپُر با اندازهٔ `FieldFontSize`، متن مشکی
(`ValueColor`) و «پلاک سفید» پشت مقدار (`Brush=White`) تا خط‌چین از میان رقم‌ها رد نشود.
"""

from __future__ import annotations

import re
import xml.etree.ElementTree as ET
from pathlib import Path

COMPANY = "1005"
SIZES = ("A4", "A5")
TREES = ("inventory/src",)
PAGE_MARGIN_INCH = 0.39
HEADER_ORIGIN = {"A4": 24.0, "A5": 16.0}      # همان pageTopMargin در OutgoingLetterPrintService
SLOTS = {"date": "DateShams", "number": "LetterNumber", "attachment": "Peyvast"}


def project_root() -> Path:
    for parent in Path(__file__).resolve().parents:
        if (parent / "inventory" / "src").is_dir():
            return parent
    raise SystemExit("پوشهٔ پروژه پیدا نشد")


ROOT = project_root()
LAYOUT = ROOT / "inventory/src/Inventory.Api/Services/Office/Outgoing/AriaLetterheadLayout.cs"


def layout_source() -> str:
    return LAYOUT.read_text(encoding="utf-8")


def field_sizes(source: str) -> dict[str, float]:
    """اندازهٔ قلم هر کاغذ: A5 و A4 (همان ترتیبی که در AriaLetterheadLayout نوشته شده)."""
    match = re.search(r"FieldFontSize\(bool isA5\) => isA5 \? ([\d.]+)f : ([\d.]+)f", source)
    if not match:
        raise SystemExit("FieldFontSize در AriaLetterheadLayout پیدا نشد")
    return {"A5": float(match.group(1)), "A4": float(match.group(2))}


def value_color(source: str) -> str:
    """رنگ متنِ اعلام‌شده در AriaLetterheadLayout، به نام رنگ Stimulsoft.

    قالب‌های این پروژه رنگ را با نام می‌نویسند («Black»/«White») و خوانندهٔ C# هم همان را
    به هگز تبدیل می‌کند؛ پس طرح و چاپ یک رنگ دارند.
    """
    match = re.search(r'ValueColor => "([^"]+)"', source)
    color = (match.group(1) if match else "#000000").upper()
    return {"#000000": "Black", "#FFFFFF": "White"}.get(color, color)


def fields(source: str) -> dict[str, dict[str, tuple[float, float, float, float]]]:
    """(A4, A5) برای هر مقدار: (left, top, width, height) در نقطه، مطلق صفحه."""
    result: dict[str, dict[str, tuple[float, float, float, float]]] = {"A4": {}, "A5": {}}
    for name in ("Date", "Number", "Attachment"):
        block = re.search(rf"internal static Field {name}\(bool isA5\) => isA5\s*\n?\s*"
                          r"\? new\(([\d.]+)f, ([\d.]+)f, ([\d.]+)f, ([\d.]+)f\)\s*\n?\s*"
                          r": new\(([\d.]+)f, ([\d.]+)f, ([\d.]+)f, ([\d.]+)f\)", source)
        if not block:
            raise SystemExit(f"مختصات {name} در AriaLetterheadLayout پیدا نشد")
        numbers = [float(value) for value in block.groups()]
        a5, a4 = numbers[:4], numbers[4:]
        slot = name.lower()
        result["A5"][slot] = (a5[0], a5[1] + HEADER_ORIGIN["A5"], a5[2], a5[3])
        result["A4"][slot] = (a4[0], a4[1] + HEADER_ORIGIN["A4"], a4[2], a4[3])
    return result


def to_rect_inch(box: tuple[float, float, float, float]) -> str:
    left, top, width, height = box
    x = round(left / 72 - PAGE_MARGIN_INCH, 4)
    y = round(top / 72 - PAGE_MARGIN_INCH, 4)
    return f"{x},{y},{round(width / 72, 4)},{round(height / 72, 4)}"


def patch_template(text: str, boxes: dict[str, tuple[float, float, float, float]],
                   font: str, color: str) -> str:
    for slot, token in SLOTS.items():
        expression = "{DataSource1." + token + "}"
        # جزء متنی که همین عبارت را دارد (به نام کادر وابسته نیستیم).
        block = re.search(r'(<(\w+) Ref="\d+" type="Text"[^>]*>(?:(?!</\2>).)*?'
                          + re.escape(expression) + r'(?:(?!</\2>).)*?</\2>)', text, re.S)
        if not block:
            raise SystemExit(f"کادر {token} در قالب پیدا نشد")
        updated = block.group(1)
        updated = re.sub(r"<ClientRectangle>[^<]*</ClientRectangle>",
                         f"<ClientRectangle>{to_rect_inch(boxes[slot])}</ClientRectangle>", updated, count=1)
        updated = re.sub(r"<Font>[^<]*</Font>", f"<Font>{font}</Font>", updated, count=1)
        updated = re.sub(r"<TextBrush>[^<]*</TextBrush>", f"<TextBrush>{color}</TextBrush>", updated, count=1)
        updated = re.sub(r"<Brush>[^<]*</Brush>", "<Brush>White</Brush>", updated, count=1)
        updated = re.sub(r"<HorAlignment>[^<]*</HorAlignment>", "<HorAlignment>Right</HorAlignment>", updated, count=1)
        updated = re.sub(r"<VertAlignment>[^<]*</VertAlignment>", "<VertAlignment>Center</VertAlignment>", updated, count=1)
        text = text[:block.start(1)] + updated + text[block.end(1):]
    return text


def main() -> None:
    source = layout_source()
    boxes = fields(source)
    sizes = field_sizes(source)
    color = value_color(source)
    written = 0
    for size in SIZES:
        font = f"Vazirmatn,{sizes[size]:g},Bold"
        for tree in TREES:
            path = (ROOT / tree / "Inventory.Api" / "Reports" / "OutgoingLetters" / "Companies"
                    / COMPANY / f"Outgoing-{size}.mrt")
            text = path.read_text(encoding="utf-8-sig")
            path.write_text(patch_template(text, boxes[size], font, color),
                            encoding="utf-8", newline="\n")
            written += 1
            print("✔", path.relative_to(ROOT))
    print(f"\n{written} فایل قالب نامهٔ صادره هم‌تراز شد "
          f"(قلم {sizes['A4']:g}pt در A4 و {sizes['A5']:g}pt در A5، رنگ {color}).")


if __name__ == "__main__":
    main()
