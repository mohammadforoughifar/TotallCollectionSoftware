"""ساخت/به‌روزرسانی قالب MRT «صورتجلسه» برای شرکت‌های سربرگ هومپا (۱۰۰۳ و ۳).

قالب با Stimulsoft طراحی می‌شود و از قالب نامهٔ صادره به‌عنوان «اهداکنندهٔ» استفاده می‌کند تا
سربرگ جاسازی‌شده (Watermark.ImageBytes) و تنظیمات صفحه عیناً همان باشد؛ فقط اجزا عوض می‌شوند:

    عنوان صورتجلسه · شماره صورتجلسه · تاریخ جلسه · حاضرین · غایبین · بندها و تصمیمات

هر مقدار یک کادر مستقل با `ClientRectangle` (اینچ، نسبت به حاشیهٔ صفحه) دارد و برچسبش هم
یک کادر جدا روی همان ردیف است؛ پس طراح می‌تواند هر کدام را در Stimulsoft جابه‌جا/تغییر دهد و
کد چاپ همان مختصات را می‌خواند (`MeetingMinutesDesignLayout`).

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

# فاصلهٔ شروع محتوا از پایین نوار آبی — همان ۹۷pt نامه‌ها (A4 و A5)
BAND_BOTTOM = {"A4": 112.8, "A5": 79.4}
CONTENT_GAP = 97.0
PAGE_MARGIN_INCH = 0.39

# اندازه‌های چیدمان هر کاغذ (نقطه)
LAYOUT = {
    "A4": {
        "title": (28.08, 34.0, 14.0),
        "row": 20.0,
        "caption": 110.0,
        "attendees_h": 62.0,
        "absentees_h": 34.0,
        "items_h": 226.0,
        "gap": 5.0,
        "font": 10.5,
        "title_font": 14.5,
    },
    "A5": {
        "title": (28.08, 26.0, 11.5),
        "row": 16.0,
        "caption": 86.0,
        "attendees_h": 50.0,
        "absentees_h": 28.0,
        "items_h": 150.0,
        "gap": 4.0,
        "font": 9.0,
        "title_font": 11.5,
    },
}


def text_inch(pt: float) -> float:
    return round(pt / 72 - PAGE_MARGIN_INCH, 4)


def component(name: str, ref: int, rect: tuple[float, float, float, float],
              text: str, font: float, bold: bool = False,
              align: str = "Right", brush: str = "Black", wrap: bool = False) -> str:
    x, y, width, height = rect
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
          <VertAlignment>Center</VertAlignment>
        <CanGrow>False</CanGrow><TextOptions>{options}</TextOptions><ShrinkFontToFit>True</ShrinkFontToFit><ShrinkFontToFitMinimumSize>7</ShrinkFontToFitMinimumSize></{name}>"""


def build_components(size: str, start_ref: int) -> tuple[str, list[str]]:
    spec = LAYOUT[size]
    _, title_h, title_font = spec["title"]
    left = 28.08
    text_width = 529.2 if size == "A4" else 345.6
    right = left + text_width
    row = spec["row"]
    caption_w = spec["caption"]
    gap = spec["gap"]
    font = spec["font"]

    y = BAND_BOTTOM[size] + CONTENT_GAP
    blocks: list[str] = []
    names: list[str] = []
    ref = start_ref

    def add(name: str, rect, text, size_pt, bold=False, wrap=False, align="Right"):
        nonlocal ref
        blocks.append(component(name, ref, rect, text, size_pt, bold, align, wrap=wrap))
        names.append(name)
        ref += 1

    # عنوان صورتجلسه — سرصفحهٔ وسط‌چین
    add("TitleValue", (text_inch(left), text_inch(y), round(text_width / 72, 4), round(title_h / 72, 4)),
        "{DataSource1.Title}", title_font, bold=True, wrap=True, align="Center")
    y += title_h + 6

    # شماره و تاریخ جلسه: برچسب سمت راست، مقدار کنارش (هم‌ردیف)
    for name, caption, token in (
        ("Number", ":شماره صورتجلسه", "{DataSource1.Number}"),
        ("MeetingDate", ":تاریخ جلسه", "{DataSource1.MeetingDate}"),
    ):
        value_width = text_width - caption_w - gap
        add(f"{name}Value", (text_inch(left), text_inch(y), round(value_width / 72, 4), round(row / 72, 4)),
            token, font)
        add(f"{name}Caption", (text_inch(right - caption_w), text_inch(y), round(caption_w / 72, 4),
                               round(row / 72, 4)), caption, font, bold=True)
        y += row + 2

    # حاضرین / غایبین: برچسب سمت راست، کادر مقدار سمت چپِ همان ردیف
    for name, caption, token, height in (
        ("Attendees", ":حاضرین", "{DataSource1.Attendees}", spec["attendees_h"]),
        ("Absentees", ":غایبین", "{DataSource1.Absentees}", spec["absentees_h"]),
    ):
        value_width = text_width - caption_w - gap
        add(f"{name}Value", (text_inch(left), text_inch(y), round(value_width / 72, 4),
                             round(height / 72, 4)), token, font, wrap=True)
        add(f"{name}Caption", (text_inch(right - caption_w), text_inch(y), round(caption_w / 72, 4),
                               round(row / 72, 4)), caption, font, bold=True)
        y += height + 8

    # بندها: برچسب در ردیف مستقل بالای جدول، چون جدول تمام عرض متن را می‌گیرد
    add("ItemsCaption", (text_inch(right - caption_w), text_inch(y), round(caption_w / 72, 4),
                         round(row / 72, 4)), ":بندها و تصمیمات", font, bold=True)
    y += row + 3
    add("ItemsValue", (text_inch(left), text_inch(y), round(text_width / 72, 4),
                       round(spec["items_h"] / 72, 4)), "{DataSource1.Items}", font, wrap=True)

    return "\n".join(blocks), names


COLUMNS = ("Number", "Title", "MeetingDate", "Attendees", "Absentees", "Items", "CompanyName", "Status")


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
