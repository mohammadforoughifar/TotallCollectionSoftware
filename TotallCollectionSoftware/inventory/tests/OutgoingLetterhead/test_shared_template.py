"""قالب مشترک چاپ نامه‌ها: نامهٔ داخلی و نامهٔ صادره از یک فایل استفاده می‌کنند.

سندباکس این پروژه `dotnet` ندارد، پس PDF واقعی ساخته نمی‌شود؛ این تست‌ها «قرارداد» کد را
می‌سنجند: هر دو مسیر چاپ از `LetterPrintTemplate` می‌خوانند، عددهای نامهٔ داخلی عوض
نشده‌اند، و خواسته‌های کارفرما (خط جداکنندهٔ نازک خاکستری، بلوک اطلاعات دوستونه، حاشیهٔ
برابر ۱۵ میلی‌متر، شمارهٔ صفحهٔ فارسی فقط در چندصفحه‌ای، ارقام فارسی، جداسازی متن ترکیبی)
در کد وجود دارند.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
# هر دو درخت چاپ: src (سرور اصلی) و inventory/src — باید یکسان رفتار کنند.
TREES = [ROOT / "src", ROOT / "inventory" / "src"]


def office(tree: Path) -> Path:
    return tree / "Inventory.Api" / "Services" / "Office"


class SharedLetterTemplateTests(unittest.TestCase):
    def template(self, tree: Path) -> str:
        return (office(tree) / "LetterPrintTemplate.cs").read_text(encoding="utf-8")

    def inner(self, tree: Path) -> str:
        return (office(tree) / "InnerLetterPdfRenderer.cs").read_text(encoding="utf-8")

    def outgoing(self, tree: Path) -> str:
        return (office(tree) / "Outgoing" / "OutgoingLetterPrintService.cs").read_text(encoding="utf-8")

    def test_inner_and_outgoing_print_through_one_shared_template(self):
        """یک قالب مشترک، نه دو نسخهٔ کپی‌شده."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                inner = self.inner(tree)
                outgoing = self.outgoing(tree)
                template = self.template(tree)
                # نامهٔ داخلی: خط جداکننده و ارقام فارسی از قالب مشترک.
                self.assertIn("LetterPrintTemplate.Separator(c)", inner)
                self.assertIn("LetterPrintTemplate.PersianDigits(", inner)
                # نامهٔ صادره: بلوک اطلاعات و خط جداکننده از همان قالب.
                self.assertIn("LetterPrintTemplate.InfoBlock(", outgoing)
                self.assertIn("LetterPrintTemplate.Separator(col", outgoing)
                self.assertIn("LetterPrintTemplate.PageNumberFooter(", outgoing)
                # نامهٔ داخلی و مسیر جدید نامهٔ صادره عدد/رنگ جداکننده را دوباره هارد‌کد
                # نمی‌کنند (مسیر کلاسیک بقیهٔ شرکت‌ها دست‌نخورده می‌ماند و جداکنندهٔ خودش را دارد).
                self.assertNotIn('LineHorizontal(0.7f).LineColor("#999999")', inner,
                                 "خط جداکننده باید فقط در قالب مشترک تعریف شود")
                design_path = outgoing.split("private byte[] BuildDesignContentPdf(", 1)[1]
                design_path = design_path.split("private static float FitFontSize", 1)[0]
                self.assertNotIn('LineHorizontal(0.7f)', design_path)
                self.assertIn("SeparatorThickness = 0.7f", template)
                self.assertIn('SeparatorColor = "#999999"', template)

    def test_shared_template_keeps_the_inner_letter_numbers(self):
        """اگر قالب مشترک تغییر کند، چاپ نامهٔ داخلی نباید عوض شود."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                template = self.template(tree)
                self.assertIn("SeparatorThickness = 0.7f", template)
                self.assertIn('SeparatorColor = "#999999"', template)
                self.assertIn("SeparatorSpacing = 5f", template)
                # همان ترتیب قبلی: PaddingVertical(5) سپس خط ۰٫۷ با رنگ #999999.
                separator = template.split("internal static void Separator(", 1)[1].split("\n\n", 1)[0]
                self.assertIn("PaddingVertical(spacing)", separator)
                self.assertIn("LineHorizontal(SeparatorThickness)", separator)
                self.assertIn("LineColor(SeparatorColor)", separator)

    def test_inner_letter_layout_is_untouched(self):
        """نامهٔ داخلی: ساختار، فونت و حاشیه‌هایش دست‌نخورده مانده است."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                inner = self.inner(tree)
                self.assertIn("void HeaderField(IContainer container, HomepaLetterheadLayout.Field field", inner)
                self.assertIn("page.Header().ContentFromLeftToRight().Height(a5 ? 112 : 160)", inner)
                self.assertIn('c.Item().Text($"عنوان: {data.Title}").Bold().AlignRight();', inner)
                self.assertIn('c.Item().Text($"گیرندگان: ', inner)
                self.assertIn('c.Item().Text($"ایجادکننده: ', inner)
                self.assertIn("content = content.PaddingTop(a5 ? 12 : 16).PaddingHorizontal(a5 ? 32 : 46);", inner)
                self.assertIn("var fontSize = a5 ? 9f : 11f;", inner)
                # نامهٔ داخلی از این پس شمارهٔ صفحه چاپ نمی‌کند (رفتار قبلی حفظ می‌شود).
                self.assertNotIn("page.Footer()", inner)

    def test_info_block_is_a_two_column_grid_with_a_hanging_indent(self):
        """«گیرنده نامه: جناب آقای …» با فاصله، و خط دوم زیر مقدار (نه زیر برچسب)."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                template = self.template(tree)
                self.assertIn("line.RelativeItem().AlignTop().AlignRight()", template)
                self.assertIn("line.ConstantItem(labelGap);", template)
                self.assertIn('$"{row.Label}:"', template)
                # ردیف‌های خالی (مثل رونوشت بدون داده) اصلاً چاپ نمی‌شوند.
                self.assertIn("rows.Where(row => !string.IsNullOrWhiteSpace(row.Value))", template)
                # برچسب و مقدار هم‌اندازه‌اند: یک fontSize برای هر دو.
                self.assertIn(".FontSize(fontSize)", template)
                self.assertNotIn("baselineShift", template)

    def test_separator_spans_the_text_column_only(self):
        """خط جداکننده داخل ستون/حاشیهٔ متن رسم می‌شود، نه به عرض کل صفحه."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                content = outgoing.split("page.Content().Column(col =>", 1)[1]
                self.assertIn("LetterPrintTemplate.Separator(col", content,
                              "خط جداکننده باید در همان ستون متن باشد")

    def test_page_number_is_persian_and_only_on_overflow(self):
        """«صفحه ۲ از ۲» فقط وقتی نامه از یک صفحه بیشتر شد، با ارقام فارسی."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                template = self.template(tree)
                footer = template.split("internal sealed class PageNumberFooter", 1)[1]
                self.assertIn("if (total <= 1 || total == int.MaxValue)", footer)
                self.assertIn("صفحه ", footer)
                self.assertIn(" از ", footer)
                self.assertIn("PersianDigits(context.PageNumber.ToString(CultureInfo.InvariantCulture))", footer)
                self.assertIn("PersianDigits(total.ToString(CultureInfo.InvariantCulture))", footer)

    def test_second_page_repeats_only_number_date_and_attachment(self):
        """عنوان و گیرنده در صفحهٔ دوم تکرار نمی‌شوند؛ شماره/تاریخ/پیوست تکرار می‌شوند."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                header = outgoing.split("private void AddDesignMetaGrid(", 1)[1]
                header = header.split("private static float FitFontSize", 1)[0]
                self.assertIn("Slot.Number", header)
                self.assertIn("Slot.Date", header)
                self.assertIn("Slot.Attachment", header)
                self.assertNotIn("Slot.Receiver", header)
                self.assertNotIn("Slot.Title", header)
                self.assertIn('("شماره", number)', header)
                self.assertIn('("تاریخ", date)', header)
                self.assertIn('("پیوست", attachment)', header)

    def test_side_margins_are_equal_and_at_least_fifteen_millimetres(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                template = self.template(tree)
                self.assertIn("SideMarginMillimeters = 15f", template)
                self.assertIn("var sideMargin = Math.Max(_designPrint.SideMargin, LetterPrintTemplate.MmToPoints(", outgoing)
                self.assertIn("sideMargin = (pageWidth - textWidth) / 2f;", outgoing)
                self.assertIn("page.MarginLeft(sideMargin);", outgoing)
                self.assertIn("page.MarginRight(sideMargin);", outgoing)
                settings = (tree / "Inventory.Api" / "appsettings.json").read_text(encoding="utf-8")
                for key in ("DesignSideMarginMm", "DesignContentTopGapMm", "DesignMetaLabels",
                            "DesignMetaSafeLeftMm", "DesignLabelGapPt"):
                    self.assertIn(f'"{key}"', settings, f"کلیف تنظیمات {key} لازم است")

    def test_body_spacing_is_readable(self):
        """فاصلهٔ پاراگراف‌ها ≥۶pt و ارتفاع خط ≈۱٫۶ (خواستهٔ کارفرما)."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                template = self.template(tree)
                self.assertIn("BodyLineHeight = 1.6f", template)
                self.assertIn("ParagraphSpacing = 6f", template)
                outgoing = self.outgoing(tree)
                self.assertIn("paragraphSpacing: LetterPrintTemplate.ParagraphSpacing", outgoing)
                self.assertIn("LetterPrintTemplate.BodyLineHeight", outgoing)
                renderer = (office(tree) / "Outgoing" / "RichLetterPdfRenderer.cs").read_text(encoding="utf-8")
                self.assertIn("float paragraphSpacing = 0f", renderer)
                self.assertIn("float BlockGap() => emitted ? paragraphSpacing : 0f;", renderer)

    def test_attachment_and_copy_to_print_only_when_they_have_data(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                # فقط «دارد»/«ندارد» — تعداد برگ جلوی پیوست نوشته نمی‌شود (درخواست کارفرما).
                self.assertIn('var attachment = attachmentCount > 0 ? "دارد" : "ندارد";', outgoing)
                self.assertNotIn('" برگ)"', outgoing)
                self.assertIn('string.Join("، ", copyNames)', outgoing)

    def test_persian_date_and_mixed_values_keep_their_order(self):
        """تاریخ شمسی با ارقام فارسی و مقدار ترکیبی با جداسازی جهت (bdi)."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                template = self.template(tree)
                self.assertIn("PersianCalendar", template)
                self.assertIn('\\u06F0', template)          # ارقام فارسی، نه عربی
                self.assertIn('"\\u2066" + value + "\\u2069"', template)   # LRI/PDI مثل <bdi>
                self.assertIn("LetterPrintTemplate.IsolateMixedText(text)", self.outgoing(tree))

    def test_meta_values_are_white_fit_and_have_optional_labels(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                template = self.template(tree)
                # رنگ مقدارها مثل قبل و مثل خودِ طرح سفید است (روی نوار آبی سربرگ).
                self.assertIn('MetaValueColor = "#FFFFFF"', template)
                self.assertIn('"MetaValueColor": "#FFFFFF"',
                              (tree / "Inventory.Api" / "appsettings.json").read_text(encoding="utf-8"))
                self.assertIn("var fontSize = FitFontSize(text, columnWidth, _designPrint.MetaFontSize);", outgoing)
                self.assertIn("return Math.Max(minimum, preferred * maxWidth / estimated);", outgoing)
                self.assertIn("DrawMetaLabels", outgoing)
                self.assertIn("var rowHeight = Math.Max(10f, field.Height);", outgoing)

    def test_a4_and_a5_scale_like_the_inner_letter(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                self.assertIn("page.Size(isA5 ? PageSizes.A5 : PageSizes.A4);", outgoing)
                self.assertIn("var likeInnerLetter = isA5 ? 9f : 11f;", outgoing)
                self.assertIn("var bodyFontSize = body.FontSize > 0 ? Math.Min(likeInnerLetter, body.FontSize) : likeInnerLetter;", outgoing)
                self.assertIn("page.MarginBottom(footerReserve);", outgoing)

    def test_content_starts_below_the_letterhead_art(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                self.assertIn("var artBottom = hasLetterhead ? HomepaLetterheadLayout.HeaderArtBottom(isA5) : 0f;", outgoing)
                self.assertIn("var artGap = Math.Max(_designPrint.ContentTopGap, _designPrint.HeaderArtGap);", outgoing)
                self.assertIn("var contentTop = Math.Max(artBottom + artGap, metaBottom + 6f);", outgoing)

    def test_settings_are_synchronised_between_the_two_trees(self):
        """هر دو درخت باید همان کلیدها و همان مقادیر را داشته باشند."""
        for key in ("DesignSideMarginMm", "DesignContentTopGapMm", "DesignMetaLabels"):
            pattern = re.compile(rf'"{key}":\s*("[^"]*"|[\d.]+)')
            values = set()
            for tree in TREES:
                settings = (tree / "Inventory.Api" / "appsettings.json").read_text(encoding="utf-8")
                match = pattern.search(settings)
                self.assertIsNotNone(match, f"{key} در {tree.name} نیست")
                values.add(match.group(1))
            self.assertEqual(len(values), 1, f"مقدار {key} در دو درخت یکسان نیست")


if __name__ == "__main__":
    unittest.main()
