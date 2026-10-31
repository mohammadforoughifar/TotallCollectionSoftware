"""ساخت/به‌روزرسانی قالب MRT «صورتجلسه» برای شرکت‌های سربرگ هومپا (۱۰۰۳ و ۳).

قالب با Stimulsoft طراحی می‌شود و از قالب نامهٔ صادره به‌عنوان «اهداکنندهٔ» استفاده می‌کند تا
سربرگ جاسازی‌شده (Watermark.ImageBytes) و تنظیمات صفحه عیناً همان باشد؛ فقط اجزا عوض می‌شوند:

    عنوان صورتجلسه · شماره · تاریخ جلسه · پیوست (روی برچسب‌های چاپ‌شدهٔ سربرگ)
    حاضرین · غایبین · بندها و تصمیمات (بدنه)

تصمیم‌های چیدمان (تأیید کارفرما):
• شماره/تاریخ/پیوست **در کادرهای سفیدِ سربرگ** می‌نشینند، پس برچسب فارسی‌شان از متن حذف شده
  و مقادیر با متن سفید چاپ می‌شوند (کد آن‌ها را در لایهٔ Foreground با مختصات مطلق می‌کشد).
• عنوان صورتجلسه **زیر مستطیل/روبان آبی** و **راست‌چین** است (نه وسط‌چین): ۱۹۳pt در A4 و
  ۱۳۹pt در A5، یعنی ۹pt پایین‌تر از پایین‌ترین پیکسل آبی سربرگ (۱۸۴pt در A4).
• بدنه از حاضرین شروع می‌شود (۲۳۲pt در A4 / ۱۶۴pt در A5) و بندها فهرست شماره‌دار ساده‌اند.
• برچسب حاضرین/غایبین به عرض طبیعی خودش است (نه ستون ۱۱۰pt) تا به نام‌ها بچسبد؛ کد چاپ
  عرض را از متن می‌گیرد و این عدد فقط برای نمایش درست در Stimulsoft است.

هر مقدار یک کادر مستقل با `ClientRectangle` (اینچ، نسبت به حاشیهٔ صفحه) دارد؛ طراح می‌تواند
هر کدام را در Stimulsoft جابه‌جا کند و کد چاپ همان مختصات را می‌خواند
(`MeetingMinutesDesignLayout`).

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

COMPANIES = ("1003", "3")
SIZES = ("A4", "A5")
SOURCE = Path("src/Inventory.Api/Reports/OutgoingLetters/Companies/{company}/Outgoing-{size}.mrt")

# کاغذ: حاشیهٔ صفحه ۰٫۳۹ اینچ؛ عرض متن ۵۲۹٫۲pt در A4 و ۳۴۵٫۶pt در A5.
PAGE_MARGIN_INCH = 0.39
TEXT_LEFT = 28.08
TEXT_WIDTH = {"A4": 529.2, "A5": 345.6}

# مختصات **مطلق صفحه** (point از گوشهٔ بالا-چپ کاغذ) — همان برچسب‌های چاپ‌شدهٔ سربرگ ۱۰۰۳
# که در قالب نامهٔ صادره/داخلی هم استفاده شده‌اند (HomepaLetterheadLayout).
META_BOX = {
    "A4": {"number": (14.4, 59.2776, 82.08, 14.4),
           "date": (14.4, 77.2776, 82.08, 14.4),
           "attachment": (14.4, 95.9976, 82.08, 14.4)},
    "A5": {"number": (7.2, 40.5216, 59.04, 12.96),
           "date": (7.2, 53.2512, 59.04, 12.96),
           "attachment": (7.2, 66.4848, 59.04, 12.96)},
}
META_FONT = 10.0
ATTACHMENT_FONT = 12.0

# چیدمان بدنه: عنوان زیر روبان آبی، سپس حاضرین/غایبین/بندها.
LAYOUT = {
    "A4": {
        "title": (28.08, 193.0, 529.2, 39.0), "title_font": 14.0,
        "row": 20.0, "font": 10.5,
        "attendees": (28.08, 232.0, 445.0, 20.0), "attendees_caption": (478.08, 232.0, 79.2, 20.0),
        "absentees": (28.08, 256.0, 445.0, 20.0), "absentees_caption": (478.08, 256.0, 79.2, 20.0),
        "items_caption": (440.64, 280.0, 116.64, 20.0),
        "items": (28.08, 306.0, 529.2, 226.0),
    },
    "A5": {
        "title": (28.08, 139.0, 345.6, 25.0), "title_font": 11.5,
        "row": 16.0, "font": 9.0,
        "attendees": (28.08, 164.0, 288.0, 16.0), "attendees_caption": (326.16, 164.0, 47.52, 16.0),
        "absentees": (28.08, 182.0, 288.0, 16.0), "absentees_caption": (326.16, 182.0, 47.52, 16.0),
        "items_caption": (280.08, 200.0, 93.6, 16.0),
        "items": (28.08, 222.0, 345.6, 150.0),
    },
}


def to_inch(pt: float) -> float:
    """مختصات مطلق (point از گوشهٔ کاغذ) → اینچِ نسبی به حاشیهٔ صفحه (قالب MRT)."""
    return round(pt / 72 - PAGE_MARGIN_INCH, 7)


def component(name: str, ref: int, rect: tuple[float, float, float, float],
              text: str, font: float, bold: bool = False,
              align: str = "None", brush: str = "Black", wrap: bool = False,
              valign: str = "Center", shrink_min: int = 7) -> str:
    # ورودی‌ها مطلق (point) هستند؛ قالب اینچِ نسبی به حاشیهٔ صفحه می‌خواهد.
    x, y, width, height = (to_inch(rect[0]), to_inch(rect[1]),
                           round(rect[2] / 72, 7), round(rect[3] / 72, 7))
    font_name = "Vazirmatn-Bold" if bold else "Vazirmatn"
    options = ",,RightToLeft=True,,WordWrap=True,A=0" if wrap else ",,RightToLeft=True,,,A=0"
    return f"""        <{name} Ref="{ref}" type="Text" isKey="true">
          <Brush>Transparent</Brush>
          <ClientRectangle>{x},{y},{width},{height}</ClientRectangle>
          <Conditions isList="true" count="0" />
          <Expressions isList="true" count="0" />
          <Font>{font_name},{font:g}</Font>
          <HorAlignment>{align}</HorAlignment>
          <Margins>0,0,0,0</Margins>
          <Name>{name}</Name>
          <Page isRef="4" />
          <Parent isRef="4" />
          <Text>{text}</Text>
          <TextBrush>{brush}</TextBrush>
          <Type>Expression</Type>
          <VertAlignment>{valign}</VertAlignment>
        <CanGrow>False</CanGrow><TextOptions>{options}</TextOptions><ShrinkFontToFit>True</ShrinkFontToFit><ShrinkFontToFitMinimumSize>{shrink_min}</ShrinkFontToFitMinimumSize></{name}>"""


def build_components(size: str, start_ref: int) -> tuple[str, list[str]]:
    spec = LAYOUT[size]
    meta = META_BOX[size]
    blocks: list[str] = []
    names: list[str] = []
    ref = start_ref

    def add(name: str, rect, text, size_pt, bold=False, wrap=False, align="Right",
            brush="Black", valign="Center", shrink_min=7):
        nonlocal ref
        blocks.append(component(name, ref, rect, text, size_pt, bold, align, brush,
                                wrap=wrap, valign=valign, shrink_min=shrink_min))
        names.append(name)
        ref += 1

    # عنوان صورتجلسه: زیر مستطیل آبی سربرگ و راست‌چین (درخواست کارفرما)
    add("TitleValue", spec["title"], "{DataSource1.Title}", spec["title_font"],
        bold=True, wrap=True, align="Right")

    # شماره / تاریخ / پیوست: کادرهای سفیدِ سربرگ، متن سفید (برچسبشان روی سربرگ چاپ شده)
    add("NumberValue", meta["number"], "{DataSource1.Number}", META_FONT, align="None",
        brush="White", valign="None")
    add("MeetingDateValue", meta["date"], "{DataSource1.MeetingDate}", META_FONT, align="None",
        brush="White", valign="None")
    add("AttachmentValue", meta["attachment"], "{DataSource1.Attachment}", ATTACHMENT_FONT,
        align="None", brush="White", valign="None", shrink_min=8)

    # حاضرین / غایبین: برچسب به عرض طبیعی، چسبیده به مقدار (کد چاپ عرض را از متن می‌گیرد)
    for name, caption, token in (
        ("Attendees", ":حاضرین", "{DataSource1.Attendees}"),
        ("Absentees", ":غایبین", "{DataSource1.Absentees}"),
    ):
        add(f"{name}Value", spec[name.lower()], token, spec["font"], wrap=True)
        add(f"{name}Caption", spec[f"{name.lower()}_caption"], caption, spec["font"], bold=True, wrap=True)

    # بندها: برچسب در ردیف مستقل و سپس فهرست شماره‌دار (کد آن را در جریان صفحه می‌چیند)
    add("ItemsCaption", spec["items_caption"], ":بندها و تصمیمات", spec["font"], bold=True, wrap=True)
    add("ItemsValue", spec["items"], "{DataSource1.Items}", spec["font"], wrap=True)

    return "\n".join(blocks), names


COLUMNS = ("Number", "Title", "MeetingDate", "Attendees", "Absentees", "Items",
           "CompanyName", "Status", "Attachment")


def build(size: str, company: str) -> str:
    source = ROOT / SOURCE.as_posix().format(company=company, size=size)
    text = source.read_text(encoding="utf-8-sig")

    components, _ = build_components(size, start_ref=40)
    text = re.sub(r'<Components isList="true" count="\d+">.*?</Components>',
                  f'<Components isList="true" count="{len(re.findall(r"isKey=.true.>", components))}">'
                  f"\n{components}\n      </Components>", text, count=1, flags=re.S)

    columns = "\n".join(f"          <value>{name},System.String</value>" for name in COLUMNS)
    text = re.sub(r'<Columns isList="true" count="\d+">.*?</Columns>',
                  f'<Columns isList="true" count="{len(COLUMNS)}">\n{columns}\n        </Columns>',
                  text, count=1, flags=re.S)

    text = re.sub(r'<Name>Page1</Name>', '<Name>Page1</Name>', text)
    text = text.replace("<Key>2ef1c17fbf9d48f582c4958a96e39b51</Key>",
                        "<Key>6f2c19d1a4b34d1e9c1e5f0d7a21b8c3</Key>")
    return text


def main() -> None:
    written = 0
    for company in COMPANIES:
        for size in SIZES:
            content = build(size, company)
            targets = [
                ROOT / "src/Inventory.Api/Reports/MeetingMinutes/Companies" / company / f"Minutes-{size}.mrt",
                ROOT / "inventory/src/Inventory.Api/Reports/MeetingMinutes/Companies" / company / f"Minutes-{size}.mrt",
                ROOT / "Report/meetingminutes" / company / f"Report_{size}_MeetingMinutes.mrt",
            ]
            for target in targets:
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text(content, encoding="utf-8", newline="\r\n" if False else "\n")
                written += 1
                print("✔", target.relative_to(ROOT))
    print(f"\n{written} فایل قالب صورتجلسه ساخته/به‌روز شد.")


if __name__ == "__main__":
    main()
