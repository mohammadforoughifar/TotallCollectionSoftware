"""ساخت/به‌روزرسانی قالب MRT «نامهٔ داخلی» برای شرکت‌هایی که سربرگشان از دلِ طرح چاپ می‌شود.

شرکت ۱۰۰۵ (آریا همیار ژنراتور) تا پیش از این قالب نامهٔ داخلی‌اش تصویر سربرگ **شرکت ۱
(فروغ آریا)** را در خود داشت و چیدمان اجزا هم چیدمان شرکت ۱ بود؛ نتیجه: نامهٔ داخلی ۱۰۰۵ با
سربرگ شرکت دیگر چاپ می‌شد. این ابزار همان قالب را از «قالب نامهٔ صادرهٔ همان شرکت» بازمی‌سازد
(دقیقاً مثل ابزار صورتجلسه) تا:

• هنر سربرگ (Watermark.ImageBytes) همان هنر خودِ ۱۰۰۵ باشد؛
• شماره/تاریخ/پیوست روی خط‌چین‌های چاپ‌شدهٔ همان هنر بنشینند (یک ردیف افقی، مقدار راست‌چین
  و پلاک سفید زیر مقدار)؛
• ساختار مثل نامهٔ داخلی ۱۰۰۳ باشد: گیرنده، «موضوع:» و عنوان، متن، بدون امضا و بدون شمارهٔ صفحه.

اعداد با اندازه‌گیری پیکسلی روی خودِ هنر سربرگ ۱۰۰۵ به‌دست آمده‌اند (نقطه، از گوشهٔ بالا-چپ):
    خط نقطه‌چین  y ≈ ۹۷٫۶ (A4) و ۶۹٫۰ (A5)
    خط‌چین‌ها تا x = ۶۴٫۳ / ۱۵۶٫۷ / ۲۵۲٫۱  →  لبهٔ راست کادرها ۶۲ / ۱۵۳٫۵ / ۲۴۹٫۵
    نوار تماس سربرگ تا y ≈ ۱۰۹pt  →  گیرنده از ۱۳۳pt
    نوار تیره/زرد پایین کاغذ از y ≈ ۷۶۲pt  →  مرز محتوا ۷۴۵pt
A5 همان نسبت ۰٫۷۰۷ است.

اجرای دوباره بی‌خطر است (idempotent): خروجی همیشه از روی قالب نامهٔ صادره بازسازی می‌شود.
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

COMPANIES = ("1005",)
SIZES = ("A4", "A5")
SOURCE = "src/Inventory.Api/Reports/OutgoingLetters/Companies/{company}/Outgoing-{size}.mrt"
PAGE_MARGIN_INCH = 0.39

# ── کادرهای شماره / تاریخ / پیوست: یک ردیف افقی روی سه نقطه‌چین سربرگ ──
# لبهٔ راست کادر = انتهای خط‌چین (۲pt سمت چپِ دونقطهٔ برچسب).
META_BOX = {
    "A4": {
        "attachment": (21.0, 90.8, 41.0, 13.7),
        "number": (110.0, 90.8, 43.5, 13.7),
        "date": (202.0, 90.8, 47.5, 13.7),
    },
    "A5": {
        "attachment": (14.8, 64.2, 29.0, 9.7),
        "number": (77.7, 64.2, 30.8, 9.7),
        "date": (142.8, 64.2, 33.6, 9.7),
    },
}
META_FONT = {"A4": "Vazirmatn,9,Bold", "A5": "Vazirmatn,6.5,Bold"}

# ── بلوک گیرنده/موضوع/متن (نقطه، مطلق صفحه) ──
# عرض کادر موضوع طوری است که لبهٔ راستش ۷pt پیش از برچسب «موضوع:» تمام شود؛ کد چاپ همین
# فاصله را از قالب می‌خواند (InnerLetterPdfRenderer → InfoBlock).
LAYOUT = {
    "A4": {
        "receiver": (20.88, 133.0, 547.2, 21.6),
        "subject": (20.88, 161.0, 471.9, 21.6),
        "subject_caption": (500.0, 161.0, 67.2, 21.6),
        "subject_font": 12.0,
        "body": (20.88, 193.0, 547.2, 373.0),
        "body_font": 11.5,
    },
    "A5": {
        "receiver": (14.7, 94.0, 381.6, 15.5),
        "subject": (14.7, 113.8, 336.3, 15.5),
        "subject_caption": (358.0, 113.8, 47.5, 15.5),
        "subject_font": 9.0,
        "body": (14.7, 136.5, 381.6, 265.0),
        "body_font": 9.0,
    },
}

# نام کادرها همان چیزی است که `OutgoingLetterDesignLayout` از روی متن تشخیص می‌دهد.
COLUMNS = ("LetterNumber", "DateShams", "Peyvast", "GirandeAsli", "TitleLetter", "TextLetter",
           "Name", "Desc")


def to_inch(pt: float, margin: float = PAGE_MARGIN_INCH) -> float:
    """مختصات مطلق (point از گوشهٔ کاغذ) → اینچِ نسبی به حاشیهٔ صفحه (قالب MRT)."""
    return round(pt / 72 - margin, 7)


def component(name: str, ref: int, rect: tuple[float, float, float, float], text: str,
              font: str, align: str = "Right", brush: str = "Black", fill: str = "Transparent",
              page_ref: int = 3, wrap: bool = True) -> str:
    x, y, width, height = (to_inch(rect[0]), to_inch(rect[1]),
                           round(rect[2] / 72, 7), round(rect[3] / 72, 7))
    options = ",,RightToLeft=True,,WordWrap=True,A=0" if wrap else ",,RightToLeft=True,,,A=0"
    return f"""        <{name} Ref="{ref}" type="Text" isKey="true">
          <Brush>{fill}</Brush>
          <ClientRectangle>{x},{y},{width},{height}</ClientRectangle>
          <Conditions isList="true" count="0" />
          <Expressions isList="true" count="0" />
          <Font>{font}</Font>
          <HorAlignment>{align}</HorAlignment>
          <Margins>0,0,0,0</Margins>
          <Name>{name}</Name>
          <Page isRef="{page_ref}" />
          <Parent isRef="{page_ref}" />
          <Text>{text}</Text>
          <TextBrush>{brush}</TextBrush>
          <Type>Expression</Type>
          <VertAlignment>Center</VertAlignment>
        <CanGrow>False</CanGrow><TextOptions>{options}</TextOptions><ShrinkFontToFit>True</ShrinkFontToFit><ShrinkFontToFitMinimumSize>7</ShrinkFontToFitMinimumSize></{name}>"""


def build_components(size: str, page_ref: int, start_ref: int = 4) -> str:
    meta = META_BOX[size]
    layout = LAYOUT[size]
    font = META_FONT[size]
    blocks: list[str] = []
    ref = start_ref

    def add(name: str, rect, text: str, font_name: str, align: str = "Right",
            brush: str = "Black", fill: str = "Transparent") -> None:
        nonlocal ref
        blocks.append(component(name, ref, rect, text, font_name, align=align, brush=brush,
                                fill=fill, page_ref=page_ref))
        ref += 1

    # شماره / تاریخ / پیوست — روی خط‌چین‌های خودِ سربرگ ۱۰۰۵، با پلاک سفید پشت مقدار.
    add("AttachmentValue", meta["attachment"], "{DataSource1.Peyvast}", font, fill="White")
    add("NumberValue", meta["number"], "{DataSource1.LetterNumber}", font, fill="White")
    add("DateValue", meta["date"], "{DataSource1.DateShams}", font, fill="White")

    # گیرنده (بدون برچسب) → «موضوع:» و عنوان → متن نامه. بدون امضا و بدون شمارهٔ صفحه.
    add("ReceiverValue", layout["receiver"], "{DataSource1.GirandeAsli}",
        f"Vazirmatn,{layout['subject_font']:g}")
    add("SubjectValue", layout["subject"], "{DataSource1.TitleLetter}",
        f"Vazirmatn,{layout['subject_font']:g},Bold")
    add("SubjectCaption", layout["subject_caption"], ":موضوع",
        f"Vazirmatn,{layout['subject_font']:g},Bold")
    add("BodyValue", layout["body"], "{DataSource1.TextLetter}",
        f"Vazirmatn,{layout['body_font']:g}")
    return "\n".join(blocks)


def ensure_watermark_flags(text: str) -> str:
    """سربرگ جاسازی‌شده باید «پشت» متن و فعال باشد (قالب فعلی ۱۰۰۵ این پرچم‌ها را ندارد)."""
    start = text.find("<Watermark")
    end = text.find("</Watermark>", start)
    if start < 0 or end < 0:
        return text
    block = text[start:end]
    missing = [flag for flag in ("<Enabled>True</Enabled>", "<ImageBehind>True</ImageBehind>",
                                 "<ImageTransparency>0</ImageTransparency>")
               if flag not in block]
    if not missing:
        return text
    line_end = block.index("\n") + 1
    inserted = "".join(f"        {flag}\n" for flag in missing)
    return text[:start] + block[:line_end] + inserted + block[line_end:] + text[end:]


def page_ref_of(text: str) -> int:
    match = re.search(r'<Page1 Ref="(\d+)"', text)
    return int(match.group(1)) if match else 3


def build(size: str, company: str) -> str:
    source = ROOT / SOURCE.format(company=company, size=size)
    text = source.read_text(encoding="utf-8-sig")
    page_ref = page_ref_of(text)

    components = build_components(size, page_ref)
    count = len(re.findall(r'isKey="true">', components))
    text = re.sub(r'<Components isList="true" count="\d+">.*?</Components>',
                  f'<Components isList="true" count="{count}">\n{components}\n      </Components>',
                  text, count=1, flags=re.S)

    columns = "\n".join(f"          <value>{name},System.String</value>" for name in COLUMNS)
    text = re.sub(r'<Columns isList="true" count="\d+">.*?</Columns>',
                  f'<Columns isList="true" count="{len(COLUMNS)}">\n{columns}\n        </Columns>',
                  text, count=1, flags=re.S)

    # نام و کلید گزارش تا با قالب نامهٔ صادره اشتباه گرفته نشود.
    text = re.sub(r"<ReportName>[^<]*</ReportName>",
                  "<ReportName>InnerLetter</ReportName>", text, count=1)
    text = text.replace("<Key>2ef1c17fbf9d48f582c4958a96e39b51</Key>",
                        "<Key>4c7a91d2b6e34f0d9a15c83e2f60b7d4</Key>")
    return ensure_watermark_flags(text)


def main(argv: list[str] | None = None) -> None:
    requested = [argument for argument in (argv or []) if argument.strip()]
    companies = tuple(company for company in COMPANIES if not requested or company in requested)
    if not companies:
        raise SystemExit(f"شرکت شناخته‌شده نیست: {', '.join(requested)}")

    written = 0
    for company in companies:
        for size in SIZES:
            content = build(size, company)
            for tree in ("src", "inventory/src"):
                target = (ROOT / tree / "Inventory.Api" / "Reports" / "InnerLetters" /
                          "Companies" / company / f"Inner-{size}.mrt")
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text(content, encoding="utf-8", newline="\n")
                written += 1
                print("✔", target.relative_to(ROOT))
    print(f"\n{written} فایل قالب نامهٔ داخلی ساخته/به‌روز شد.")


if __name__ == "__main__":
    import sys

    main(sys.argv[1:])
