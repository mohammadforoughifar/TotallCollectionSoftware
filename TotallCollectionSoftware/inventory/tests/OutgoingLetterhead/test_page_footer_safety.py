"""«صفحه X از Y» نباید هرگز چاپ را متوقف کند.

خطای واقعیِ گزارش‌شده در چاپ A5:

    ساخت PDF نامه ناموفق بود (DocumentLayoutException):
    Dynamic component generated content that does not fit on a single page.

منشأ این استثنا خودِ QuestPDF است: در `DynamicHost.Measure` اگر محتوایی که جزء پویا ساخته
در فضای در دسترس جا نشود (Wrap/PartialRender)، کل سند رد می‌شود. پابرگِ «صفحه ۱ از ۲» یک
جزء پویا است؛ اگر کادر پابرگ در کاغذ کوچک‌تر یک خط جا نداشته باشد، نامه چاپ نمی‌شد.

دو لایه محافظ در کد هست و این تست‌ها هر دو را می‌سنجند:

1. خودِ جزء (`LetterPrintTemplate.PageNumberFooter`): پیش از ساختن متن، فضای در دسترس
   (`context.AvailableSize`) بررسی می‌شود؛ اگر یک خط جا نشود هیچ چاپ نمی‌شود، و اگر عرض کم
   باشد قلم کوچک می‌شود تا متن هرگز به خط دوم نشکند.
2. سرویس چاپ (`OutgoingLetterPrintService` و `MeetingMinutesPrintService`): اگر باز هم
   `DocumentLayoutException` رخ داد، همان سند یک بار دیگر بدون شمارهٔ صفحه ساخته می‌شود تا
   خودِ نامه/صورتجلسه از دست نرود.
"""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
TREES = [ROOT / "src", ROOT / "inventory" / "src"]


def office(tree: Path) -> Path:
    return tree / "Inventory.Api" / "Services" / "Office"


class PageNumberFooterTests(unittest.TestCase):
    def template(self, tree: Path) -> str:
        return (office(tree) / "LetterPrintTemplate.cs").read_text(encoding="utf-8")

    def footer(self, tree: Path) -> str:
        source = self.template(tree)
        start = source.index("internal sealed class PageNumberFooter")
        return source[start:]

    def test_footer_checks_available_space_before_drawing(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                footer = self.footer(tree)
                self.assertIn("context.AvailableSize", footer)
                self.assertIn("return NoContent(context);", footer)
                # ارتفاع/عرضِ کمتر از یک خط ⇒ هیچ چاپی (نه استثنا)
                self.assertIn("available.Height < fontSize * 1.8f", footer)
                self.assertIn("available.Width < fontSize * 6f", footer)

    def test_empty_footer_is_one_shared_helper(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                footer = self.footer(tree)
                self.assertEqual(footer.count("private static DynamicComponentComposeResult NoContent"),
                                 1)
                self.assertEqual(footer.count("context.CreateElement(_ => { })"), 1,
                                 "همهٔ حالت‌های «چیزی چاپ نشود» باید از یک نقطه باشند")

    def test_long_label_shrinks_instead_of_wrapping(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                footer = self.footer(tree)
                self.assertIn("fontSize = Math.Max(5f, fontSize * available.Width / estimated);", footer)
                self.assertIn(".FontSize(fontSize)", footer)
                # ارتفاع خط کوچک‌تر از ۱٫۲ قبلی: کمتر احتمال شکستن در کادر باریک
                self.assertIn(".LineHeight(1.1f)", footer)

    def test_single_page_documents_print_no_footer(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                footer = self.footer(tree)
                self.assertIn("if (total <= 1 || total == int.MaxValue)", footer)


class ServicesRetryWithoutPageNumbersTests(unittest.TestCase):
    def service(self, tree: Path, name: str) -> str:
        return (office(tree) / name).read_text(encoding="utf-8")

    def test_outgoing_letter_retries_without_footer(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = self.service(tree, "Outgoing/OutgoingLetterPrintService.cs")
                self.assertIn("Document ComposeDocument(bool withPageNumbers)", source)
                self.assertIn("if (withPageNumbers)", source)
                self.assertIn("catch (DocumentLayoutException", source)
                self.assertIn("ComposeDocument(true).GeneratePdf()", source)
                self.assertIn("ComposeDocument(false).GeneratePdf()", source)
                # پیام هشدار باید در لاگ بماند تا علت دیده شود
                self.assertIn("شمارهٔ صفحه در پابرگ جا نشد", source)
                self.assertIn("using QuestPDF.Drawing.Exceptions;", source)

    def test_meeting_minutes_retries_without_footer(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = self.service(tree, "MeetingMinutesPrintService.cs")
                self.assertIn("Document ComposeDocument(bool withPageNumbers)", source)
                self.assertIn("if (withPageNumbers)", source)
                self.assertIn("catch (DocumentLayoutException", source)
                self.assertIn("ComposeDocument(false).GeneratePdf()", source)
                self.assertIn("using QuestPDF.Drawing.Exceptions;", source)

    def test_shared_template_is_identical_in_both_trees(self):
        first, second = (office(tree) / "LetterPrintTemplate.cs" for tree in TREES)
        self.assertEqual(first.read_bytes(), second.read_bytes())


if __name__ == "__main__":
    unittest.main(verbosity=2)
