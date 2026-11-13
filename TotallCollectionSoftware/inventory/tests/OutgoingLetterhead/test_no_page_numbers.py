"""هیچ چاپی شمارهٔ صفحه ندارد.

درخواست کارفرما: «شماره صفحه را حذف کن. نیازی نیست شماره صفحه داشته باشد.»

پیش از این، پابرگ «صفحه X از Y» به سه شکل در چاپ بود: مسیر طرح نامهٔ صادره (جزء پویا)،
مسیر کلاسیک نامهٔ صادره (Text با `CurrentPageNumber`)، و چاپ صورتجلسه. همین جزء پویا در
A5 باعث خطای `DocumentLayoutException` می‌شد («Dynamic component generated content that
does not fit on a single page»).

اکنون:
• کلاس `PageNumberFooter` از قالب مشترک حذف شده است.
• نه نامهٔ صادره (هر دو مسیر) و نه صورتجلسه هیچ شمارهٔ صفحه‌ای چاپ نمی‌کنند.
• هیچ `Dynamic` در سرویس‌های چاپ نمانده است (پس آن استثنا دیگر ممکن نیست).
• سطر «نام شرکت» در پابرگ مسیر قدیمی صورتجلسه باقی است (شمارهٔ صفحه نبود، حذف نشد).
"""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
TREES = [ROOT / "src", ROOT / "inventory" / "src"]


def office(tree: Path) -> Path:
    return tree / "Inventory.Api" / "Services" / "Office"


class NoPageNumbersTests(unittest.TestCase):
    def test_shared_template_has_no_page_number_component(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                template = (office(tree) / "LetterPrintTemplate.cs").read_text(encoding="utf-8")
                self.assertNotIn("PageNumberFooter", template)
                self.assertNotIn("IDynamicComponent", template)
                self.assertNotIn("CurrentPageNumber", template)
                self.assertNotIn("TotalPages", template)
                # قالب مشترک باید همچنان کارش را بکند (بلوک اطلاعات، رونوشت، ارقام فارسی)
                for member in ("InfoBlock", "CopyToBlock", "Spacer", "PersianDigits"):
                    self.assertIn(member, template)

    def test_outgoing_letters_never_print_page_numbers(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = (office(tree) / "Outgoing" / "OutgoingLetterPrintService.cs").read_text(
                    encoding="utf-8")
                self.assertNotIn("PageNumberFooter", source)
                self.assertNotIn("CurrentPageNumber", source)
                self.assertNotIn("TotalPages", source)
                self.assertNotIn(".Dynamic(", source)
                self.assertNotIn('Span("صفحه ")', source)
                # هر دو مسیر چاپ (طرح ۱۰۰۳ و کلاسیک) دست‌نخورده باید بسازند
                self.assertIn("private byte[] BuildDesignContentPdf(", source)
                self.assertIn("return document.GeneratePdf();", source)

    def test_meeting_minutes_never_print_page_numbers(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = (office(tree) / "MeetingMinutesPrintService.cs").read_text(encoding="utf-8")
                self.assertNotIn("PageNumberFooter", source)
                self.assertNotIn("CurrentPageNumber", source)
                self.assertNotIn("TotalPages", source)
                self.assertNotIn(".Dynamic(", source)
                self.assertNotIn('Span("صفحه ")', source)
                self.assertIn("private byte[] BuildTemplateContentPdf(", source)
                self.assertIn("var doc = Document.Create(container =>", source)

    def test_inner_letters_still_have_no_footer(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                inner = (office(tree) / "InnerLetterPdfRenderer.cs").read_text(encoding="utf-8")
                self.assertNotIn("page.Footer()", inner)

    def test_no_dynamic_component_remains_in_print_services(self):
        """بدون `Dynamic`، خطای «does not fit on a single page» دیگر ممکن نیست."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                for name in ("Outgoing/OutgoingLetterPrintService.cs",
                             "MeetingMinutesPrintService.cs",
                             "LetterPrintTemplate.cs"):
                    source = (office(tree) / name).read_text(encoding="utf-8")
                    self.assertNotIn(".Dynamic(", source, name)

    def test_all_three_sources_are_mirrored(self):
        for name in ("LetterPrintTemplate.cs",
                     "Outgoing/OutgoingLetterPrintService.cs",
                     "MeetingMinutesPrintService.cs"):
            with self.subTest(file=name):
                first, second = ((office(tree) / name).read_bytes() for tree in TREES)
                self.assertEqual(first, second)


if __name__ == "__main__":
    unittest.main(verbosity=2)
