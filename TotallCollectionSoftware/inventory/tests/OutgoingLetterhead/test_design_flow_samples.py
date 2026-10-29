"""تست «چند صفحه‌ای شدن» چاپ نامهٔ صادرهٔ شرکت ۱۰۰۳ با سه نمونهٔ کوتاه/متوسط/بلند.

سندباکس این پروژه `dotnet` ندارد، پس PDF واقعی ساخته نمی‌شود. این تست همان حساب‌وکتابی
را که QuestPDF در چیدمان «از دل طرح» انجام می‌دهد، روی هندسهٔ واقعی فایل طرح (MRT) و
عددهای واقعی کد بازسازی می‌کند و سه چیز را تضمین می‌کند:

1. نامهٔ کوتاه یک‌صفحه‌ای می‌ماند (ایراد کارفرما: «شکستن بی‌دلیل صفحه»).
2. نامهٔ متوسط (با فهرست چهار آیتمی) هم یک‌صفحه‌ای است.
3. نامهٔ بلند به صفحهٔ دوم می‌رود، اما بلوک امضا از متن جدا نمی‌شود و تنها نمی‌ماند.

عددها از خودِ کد و قالب خوانده می‌شوند؛ اگر روزی حاشیه‌ها یا رزرو پابرگ عوض شود، این
تست دوباره ارزیابی می‌کند و رفتار نامناسب را گزارش می‌دهد.
"""

from __future__ import annotations

import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
# هر دو درخت چاپ: src (سرور اصلی) و inventory/src — باید یکسان رفتار کنند.
TREES = [ROOT / "src", ROOT / "inventory" / "src"]


def _number(pattern: str, text: str, fallback: float | None = None) -> float:
    match = re.search(pattern, text)
    if match is None:
        if fallback is None:
            raise AssertionError(f"الگوی «{pattern}» در کد پیدا نشد")
        return fallback
    return float(match.group(1))


class TemplateGeometry:
    """مستطیل‌های فایل طرح، به point، نسبت به گوشهٔ بالا-چپ صفحه."""

    def __init__(self, tree: Path, size: str = "A4") -> None:
        path = (tree / "Inventory.Api" / "Reports" / "OutgoingLetters" / "Companies"
                / "1003" / f"Outgoing-{size}.mrt")
        root = ET.parse(path).getroot()
        self.size = size
        page = next(e for e in root.iter() if e.tag == "Pages")[0]
        unit = self._value(root, "ReportUnit") or "Inches"
        self.factor = {"inches": 72.0, "centimeters": 72 / 2.54,
                       "millimeters": 72 / 25.4}.get(unit.strip().lower(), 72.0)
        self.page_width = float(self._value(page, "PageWidth")) * self.factor
        self.page_height = float(self._value(page, "PageHeight")) * self.factor
        # حاشیه‌ها هم به واحد گزارش‌اند؛ ضریب فقط یک‌بار (در _rect) اعمال می‌شود.
        margins = [float(x) for x in (self._value(page, "Margins") or "").split(",")]
        self.margin_left, self.margin_top = margins[0], margins[2]

        components = next(e for e in page if e.tag == "Components")
        self.boxes = {}
        for component in components:
            if (self._value(component, "Enabled") or "").lower() == "false":
                continue
            text = (self._value(component, "Text") or "").strip()
            name = self._value(component, "Name") or ""
            slot = self._slot(text)
            if slot is None:
                continue
            rect = self._value(component, "ClientRectangle")
            self.boxes[slot] = (self._rect(rect), name)

    @staticmethod
    def _value(parent, name):
        for element in parent:
            if element.tag == name:
                return element.text
        return None

    @staticmethod
    def _slot(text: str):
        for token, slot in (("LetterNumber", "number"), ("DateShams", "date"),
                            ("Peyvast", "attachment"), ("GirandeAsli", "receiver"),
                            ("TitleLetter", "title"), ("TextLetter", "body"),
                            ("DataSource1.Name", "copyTo")):
            if text and token in text:
                return slot
        return None

    def _rect(self, value):
        left, top, width, height = (float(x) for x in value.split(",")[:4])
        return ((self.margin_left + left) * self.factor,
                (self.margin_top + top) * self.factor,
                width * self.factor,
                height * self.factor)


class PrintLayout:
    """همان عددهایی که `BuildDesignContentPdf` برای صفحه‌بندی استفاده می‌کند.

    از نسخهٔ «قالب مشترک» به بعد، ارتفاع بلوک اطلاعات و خط جداکننده از
    `LetterPrintTemplate` می‌آید و اندازهٔ قلم مثل نامهٔ داخلی ۱۱/۹ است؛ این کلاس همان
    محاسبه را بازسازی می‌کند تا اگر روزی عددی عوض شود، صفحه‌بندی دوباره سنجیده شود.
    """

    def __init__(self, tree: Path, size: str = "A4") -> None:
        self.tree = tree
        self.geometry = TemplateGeometry(tree, size)
        original_size = self.geometry.size
        layout = (tree / "Inventory.Api" / "Services" / "Office" / "Outgoing"
                  / "HomepaLetterheadLayout.cs").read_text(encoding="utf-8")
        self.design_footer = self._ternary(layout, "DesignFooterReserve\\(bool isA5\\)", size)
        self.art_bottom = self._ternary(layout, "HeaderArtBottom\\(bool isA5\\)", size)

        template = (tree / "Inventory.Api" / "Services" / "Office"
                    / "LetterPrintTemplate.cs").read_text(encoding="utf-8")
        self.body_line_height = _number(r"BodyLineHeight = ([\d.]+)f", template)
        self.paragraph_spacing = _number(r"ParagraphSpacing = ([\d.]+)f", template)
        self.separator_spacing = _number(r"SeparatorSpacing = ([\d.]+)f", template)
        self.min_side_margin_mm = _number(r"SideMarginMillimeters = ([\d.]+)f", template)

        settings = (tree / "Inventory.Api" / "appsettings.json").read_text(encoding="utf-8")
        self.label_gap = _number(r'"DesignLabelGapPt":\s*([\d.]+)', settings)
        self.content_gap_mm = _number(r'"DesignContentTopGapMm":\s*([\d.]+)', settings, 10.0)

        number = self.geometry.boxes["number"][0]
        date = self.geometry.boxes["date"][0]
        attachment = self.geometry.boxes["attachment"][0]
        body = self.geometry.boxes["body"][0]
        meta_bottom = max(box[1] + box[3] for box in (number, date, attachment))

        # contentTop = max(پایین هنر + فاصلهٔ تنظیمات, پایین ناحیهٔ شماره/تاریخ/پیوست + ۶).
        self.content_top = max(self.art_bottom + self.content_gap_mm * 72 / 25.4, meta_bottom + 6)
        # سقفِ «دست‌کم ۲۰۰pt متن در صفحهٔ اول» (۱۵۰ در A5).
        self.content_top = min(self.content_top,
                               self.geometry.page_height - self.design_footer
                               - (150 if size == "A5" else 200))
        # page.Footer().Height(16) داخل ناحیهٔ محتوا (بالای حاشیهٔ پایین) جا می‌گیرد.
        self.footer_height = 16
        self.available = (self.geometry.page_height - self.design_footer - self.content_top
                          - self.footer_height)

        # اندازهٔ قلم مثل نامهٔ داخلی: ۱۱pt در A4 و ۹pt در A5 (هرچه طرح کوچک‌تر گفته، ملاک است).
        design_font = self._body_font(tree, size)
        self.body_font = min(9.0 if size == "A5" else 11.0, design_font)

        # ستون متن: عرض کادر متنِ طرح، ولی نه پهن‌تر از «صفحه منهای دو حاشیهٔ ۱۵mm».
        min_side = self.min_side_margin_mm * 72 / 25.4
        self.text_width = min(body[2], self.geometry.page_width - 2 * min_side)
        self.side_margin = (self.geometry.page_width - self.text_width) / 2
        self.chars_per_line = max(20.0, self.text_width / (self.body_font * 0.5))

        # بلوک اطلاعات: عنوان/گیرنده/ایجادکننده/رونوشت (هر ردیف ۱٫۵۵em و فاصلهٔ بین ردیف‌ها).
        self.info_rows = 4
        row = self.body_font * 1.55
        self.info_height = (self.info_rows * row
                            + (self.info_rows - 1) * max(self.paragraph_spacing, self.body_font * 0.45))
        self.separator = 2 * (4 if size == "A5" else 6) + 0.7
        self.text_area = self.available - self.info_height - self.separator

        # بلوک امضا: فاصلهٔ بالای بلوک + تصویر امضا (سقف ۷۲/۵۲pt) + نام + سمت.
        self.signature_padding = 12
        self.signature_height = min(
            self.geometry.boxes.get("signature", ((0, 0, 0, 51.84 if size == "A5" else 75.6), ""))[0][3],
            40.0 if size == "A5" else 52.0)
        self.signature_block = (self.signature_padding + self.signature_height + 2
                                + (self.body_font + 1) * 1.3 + self.body_font * 1.3)

    @staticmethod
    def _ternary(source: str, method: str, size: str):
        find = re.search(method + r"[^\n]*=> isA5 \? ([\d.]+)f : ([\d.]+)f", source)
        if find is None:
            raise AssertionError(f"متد {method} به شکل سه‌گانهٔ مورد انتظار نیست")
        return float(find.group(1)) if size == "A5" else float(find.group(2))

    @staticmethod
    def _body_font(tree: Path, size: str = "A4"):
        """اندازهٔ قلم کادر متن از خود قالب (هر جزء، عنصری با type=\"Text\" است)."""
        path = (tree / "Inventory.Api" / "Reports" / "OutgoingLetters" / "Companies"
                / "1003" / f"Outgoing-{size}.mrt")
        for element in ET.parse(path).getroot().iter():
            if element.get("type") != "Text":
                continue
            text = next((e.text or "" for e in element if e.tag == "Text"), "")
            if "TextLetter" not in text:
                continue
            font = next((e.text or "" for e in element if e.tag == "Font"), "")
            for part in (piece.strip() for piece in font.split(",")):
                try:
                    return float(part)
                except ValueError:
                    continue
        raise AssertionError("اندازهٔ قلم کادر متن در قالب پیدا نشد")

    # ── شبیه‌سازی صفحه‌بندی ──
    def line_height(self) -> float:
        return self.body_font * self.body_line_height

    def paragraph(self, chars: int) -> float:
        lines = max(1, -(-chars // int(self.chars_per_line)))
        return lines * self.line_height() + self.paragraph_spacing

    def list_block(self, items: int, chars_each: int) -> float:
        # هر آیتم: خطِ خودش + فاصلهٔ ۰٫۳۵em؛ بالای فهرست ۰٫۴۵em و پایانش همان اندازه.
        item_height = self.paragraph(chars_each) + self.body_font * 0.35
        return self.body_font * 0.9 + items * item_height

    def paginate(self, blocks: list[float]) -> tuple[int, float, bool]:
        """→ (تعداد صفحه, ارتفاع پرشده در صفحهٔ آخر, آیا صفحهٔ آخر متن دارد؟)"""
        pages = 1
        used = self.info_height + self.separator      # بلوک اطلاعات فقط در صفحهٔ اول
        capacity = self.available                      # صفحهٔ اول: سربرگ تا پابرگ
        for block in blocks:
            if used + block > capacity:
                pages += 1
                used = 0.0
                capacity = self.available
            used += block

        text_on_last_page = used > 1.0
        # بلوک امضا page-break-inside: avoid است و به آخرین پاراگراف قفل می‌شود.
        if used + self.signature_block > capacity:
            pages += 1
            used = 0.0
        used += self.signature_block
        return pages, used, text_on_last_page


class DesignFlowSampleTests(unittest.TestCase):
    # سه نمونهٔ کارفرما: کوتاه، متوسط با فهرست، بلند (دو برابر متوسط).
    SAMPLES = {
        # (پاراگراف‌ها به «تعداد کاراکتر», آیتم‌های فهرست, کاراکتر هر آیتم)
        "کوتاه": ([40, 140, 110], 0, 0),
        "متوسط": ([40, 120, 110, 90], 3, 45),
        "بلند": ([40, 160, 150, 140, 160, 150, 140, 160, 150], 8, 55),
    }

    def blocks(self, layout: PrintLayout, paragraphs, items, item_chars):
        blocks = [layout.paragraph(chars) for chars in paragraphs]
        if items:
            blocks.append(layout.list_block(items, item_chars))
        return blocks

    def test_short_letters_stay_on_one_page_in_a4_and_a5(self):
        for tree in TREES:
            for size in ("A4", "A5"):
                with self.subTest(tree=tree.name, size=size):
                    layout = PrintLayout(tree, size)
                    paragraphs, items, item_chars = self.SAMPLES["کوتاه"]
                    pages, _, _ = layout.paginate(self.blocks(layout, paragraphs, items, item_chars))
                    self.assertEqual(pages, 1, f"نامهٔ کوتاه در {size} نباید دو صفحه شود")

    def test_medium_letter_with_list_fits_one_page_on_a4(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                layout = PrintLayout(tree, "A4")
                paragraphs, items, item_chars = self.SAMPLES["متوسط"]
                pages, _, _ = layout.paginate(self.blocks(layout, paragraphs, items, item_chars))
                self.assertEqual(pages, 1, "نامهٔ متوسط با فهرست باید یک صفحه بماند")

    def test_long_letter_moves_to_second_page_with_its_signature(self):
        for tree in TREES:
            for size in ("A4", "A5"):
                with self.subTest(tree=tree.name, size=size):
                    layout = PrintLayout(tree, size)
                    paragraphs, items, item_chars = self.SAMPLES["بلند"]
                    pages, _, text_before_signature = layout.paginate(
                        self.blocks(layout, paragraphs, items, item_chars))
                    self.assertGreaterEqual(pages, 2, "نامهٔ بلند باید به صفحهٔ دوم برود")
                    self.assertLessEqual(pages, 3, "نامهٔ بلند نباید بی‌دلیل سه صفحه شود")
                    self.assertTrue(text_before_signature,
                                    "بلوک امضا نباید تنها و بی‌متن در صفحهٔ آخر بیاید")

    def test_letterhead_art_and_footer_leave_a_usable_text_area(self):
        """ناحیهٔ متن باید از پایین قوس سربرگ شروع شود و بالای گرافیک پابرگ بماند."""
        for tree in TREES:
            for size in ("A4", "A5"):
                with self.subTest(tree=tree.name, size=size):
                    layout = PrintLayout(tree, size)
                    self.assertGreater(layout.content_top, layout.art_bottom)
                    self.assertGreater(layout.text_area,
                                       layout.signature_block + layout.line_height() * 6,
                                       f"ناحیهٔ متن {size} برای نامهٔ کوتاه + امضا کافی نیست")

    def test_text_column_is_centred_with_equal_margins(self):
        """حاشیهٔ چپ و راست برابر و دست‌کم ۱۵ میلی‌متر است (نه مختصات دلخواه طرح)."""
        for tree in TREES:
            for size in ("A4", "A5"):
                with self.subTest(tree=tree.name, size=size):
                    layout = PrintLayout(tree, size)
                    self.assertGreaterEqual(round(layout.side_margin, 3),
                                            round(15 * 72 / 25.4, 3),
                                            "حاشیهٔ کناری کمتر از ۱۵ میلی‌متر شده است")
                    left = layout.side_margin
                    right = layout.geometry.page_width - layout.side_margin - layout.text_width
                    self.assertAlmostEqual(left, right, places=1,
                                           msg="حاشیهٔ چپ و راست نامهٔ صادره برابر نیست")

    def test_meta_values_keep_the_15_millimetre_safe_margin(self):
        """شماره/تاریخ/پیوست با حاشیهٔ امن ۱۵mm از لبهٔ چپ چاپ می‌شوند."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                settings = (tree / "Inventory.Api" / "appsettings.json").read_text(encoding="utf-8")
                safe_mm = _number(r'"DesignMetaSafeLeftMm":\s*([\d.]+)', settings)
                self.assertGreaterEqual(safe_mm, 15)
                geometry = TemplateGeometry(tree)
                safe_pt = safe_mm * 72 / 25.4
                for slot in ("number", "date", "attachment"):
                    (left, _, width, _), _name = geometry.boxes[slot]
                    self.assertLess(left + width, 120,
                                    "کادر مقدار باید سمت چپ برچسب‌های سربرگ بماند")
                    self.assertLess(left, safe_pt,
                                    "کادر طرح به لبه می‌چسبد؛ همین‌جا حاشیهٔ امن لازم است")


if __name__ == "__main__":
    unittest.main()
