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
                self.assertIn("LetterPrintTemplate.Spacer(col", outgoing)
                # شمارهٔ صفحه به‌درخواست کارفرما از هر سه چاپ حذف شده است.
                self.assertNotIn("PageNumberFooter", outgoing)
                self.assertNotIn("PageNumberFooter", template)
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

    def test_outgoing_info_block_drops_the_title_and_receiver_labels(self):
        """بلوک اطلاعات ۱۰۰۳: گیرنده بدون برچسب در بالا، بعد «موضوع:» و عنوان.

        کارفرما خواست «عنوان نامه:» و «گیرنده نامه:» چاپ نشوند، گیرنده در خط اول و
        بدون برچسب بیاید و زیرش «موضوع:» با عنوان نامه روبه‌رویش بنشیند.
        """
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                template = self.template(tree)
                design = outgoing.split("private byte[] BuildDesignContentPdf(", 1)[1]
                design = design.split("private static float FitFontSize", 1)[0]
                # هیچ‌کدام از این دو برچسب دیگر در چاپ ۱۰۰۳ نیست.
                self.assertNotIn('new("عنوان نامه"', design)
                self.assertNotIn('new("گیرنده نامه"', design)
                # گیرنده: ردیف اول، بدون برچسب.
                self.assertIn("new(string.Empty, receiverLine, HideLabel: true)", design)
                # موضوع با عنوان نامه، پشت سر گیرنده.
                self.assertIn('new("موضوع", title, BoldValue: true)', design)
                receiver_at = design.index("new(string.Empty, receiverLine, HideLabel: true)")
                subject_at = design.index('new("موضوع", title, BoldValue: true)')
                self.assertLess(receiver_at, subject_at, "گیرنده باید بالای «موضوع» باشد")
                # قالب مشترک باید ردیف بدون برچسب را پشتیبانی کند.
                self.assertIn("bool HideLabel = false", template)
                self.assertIn("if (row.HideLabel || labelWidth <= 0f) return;", template)
                self.assertIn("var labelled = visible.Where(row => !row.HideLabel).ToList();", template)

    def test_info_block_is_a_two_column_grid_with_a_hanging_indent(self):
        """«گیرنده نامه: جناب آقای …» با فاصله، و خط دوم زیر مقدار (نه زیر برچسب)."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                template = self.template(tree)
                self.assertIn("line.RelativeItem().AlignTop().AlignRight()", template)
                self.assertIn("line.ConstantItem(labelGap);", template)
                # دونقطه از کمک‌تابع مشترک می‌آید تا همیشه سمت چپ کلمه («موضوع:») بنشیند.
                self.assertIn(".Text(RtlLabel(row.Label))", template)
                # برچسب چسبیده به لبهٔ راست متن (مثل نامهٔ داخلی)، نه شناور وسط صفحه.
                self.assertIn("line.ConstantItem(labelWidth).AlignTop().AlignRight()", template)
                # ردیف‌های خالی (مثل رونوشت بدون داده) اصلاً چاپ نمی‌شوند.
                self.assertIn("rows.Where(row => !string.IsNullOrWhiteSpace(row.Value))", template)
                # برچسب و مقدار هم‌اندازه‌اند: یک fontSize برای هر دو.
                self.assertIn(".FontSize(fontSize)", template)
                self.assertNotIn("baselineShift", template)

    def test_outgoing_has_no_separator_line_but_keeps_the_spacing(self):
        """خط جداکنندهٔ خاکستری از چاپ نامهٔ صادره برداشته شد (درخواست کارفرما).

        نامهٔ داخلی همان خط را از قالب مشترک می‌گیرد و دست‌نخورده است؛ در نامهٔ صادره
        فقط فاصلهٔ خالی (`Spacer`) می‌ماند تا متن به «موضوع» نچسبد.
        """
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                content = outgoing.split("page.Content().Column(col =>", 1)[1]
                content = content.split("    }\n\n    /// <summary>", 1)[0]
                self.assertNotIn("LetterPrintTemplate.Separator(col", content,
                                 "خط جداکننده نباید در چاپ ۱۰۰۳ رسم شود")
                self.assertNotIn("LineHorizontal(", content,
                                 "خط جداکننده نباید دستی هم رسم شود")
                self.assertIn("LetterPrintTemplate.Spacer(col, isA5 ? 10f : 12f)", content,
                              "فاصلهٔ خالی جای خط جداکننده")
                # نامهٔ داخلی خط جداکنندهٔ خودش را نگه می‌دارد.
                self.assertIn("LetterPrintTemplate.Separator(c)", self.inner(tree))

    def test_no_page_numbers_are_printed(self):
        """شمارهٔ صفحه (صفحه X از Y) در هیچ‌کدام از چاپ‌ها وجود ندارد.

        درخواست کارفرما: «شماره صفحه را حذف کن، نیازی نیست شماره صفحه داشته باشد.»
        حذف آن یک فایدهٔ جانبی هم دارد: پابرگ پویا در A5 باعث `DocumentLayoutException`
        («Dynamic component generated content that does not fit on a single page») می‌شد.
        """
        for tree in TREES:
            with self.subTest(tree=tree.name):
                template = self.template(tree)
                outgoing = self.outgoing(tree)
                inner = self.inner(tree)
                self.assertNotIn("PageNumberFooter", template)
                self.assertNotIn("IDynamicComponent", template)
                for name, source in (("نامهٔ صادره", outgoing), ("نامهٔ داخلی", inner)):
                    self.assertNotIn("CurrentPageNumber()", source, name)
                    self.assertNotIn("TotalPages()", source, name)
                    self.assertNotIn(".Dynamic(", source, name)
                    # متن شمارهٔ صفحه (نه واژهٔ «صفحه» در توضیحات)
                    self.assertNotIn('Span("صفحه ")', source, name)
                    self.assertNotIn('Text($"صفحه ', source, name)
                    self.assertNotIn('" از "', source, name)

    def test_second_page_repeats_only_number_date_and_attachment(self):
        """عنوان و گیرنده در صفحهٔ دوم تکرار نمی‌شوند؛ شماره/تاریخ/پیوست تکرار می‌شوند."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                header = outgoing.split("private void AddDesignMetaGrid(", 1)[1]
                header = header.split("private static float FitFontSize", 1)[0]
                self.assertIn("page.Foreground().Element(fg => AddDesignMetaGrid(fg, design, number, date,", outgoing,
                              "لایهٔ سراسری = تکرار خودکار روی همهٔ صفحه‌ها")
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
                # رونوشت دیگر ردیفِ بلوک اطلاعات نیست؛ بلوک جداگانه است.
                self.assertNotIn('new("رونوشت"', outgoing)
                self.assertIn("LetterPrintTemplate.CopyToBlock(", outgoing)

    def test_copy_to_goes_after_the_text_and_before_the_signatures(self):
        """«رونوشت» زیر متن و بالای امضا می‌آید (برای همهٔ شرکت‌ها) و هر رونوشت خط خودش را دارد."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                template = self.template(tree)

                # قالب مشترک: هر رونوشت یک آیتم مستقل است تا فهرست بلند به خط بعد برود.
                block = template.split("internal static void CopyToBlock(", 1)[1].split("\n    }", 1)[0]
                self.assertIn("values.Item().Text(text =>", block)
                self.assertIn("for (var index = 0; index < visible.Count; index++)", block)
                self.assertIn("visible.Count", block)
                # مقدار در ستون خودش می‌شکند (تورفتگی آویزان)، برچسب جدا و راست‌چین.
                self.assertIn("line.RelativeItem().AlignTop().AlignRight().Column(values =>", block)
                self.assertIn("line.ConstantItem(labelWidth).AlignTop().AlignRight()", block)
                self.assertIn('var label = RtlLabel("رونوشت");', block)

                # مسیر طرح: تابع دنباله = رونوشت ← امضا.
                design = outgoing.split("private byte[] BuildDesignContentPdf(", 1)[1]
                design = design.split("private static float FitFontSize", 1)[0]
                self.assertIn("void RenderTail(ColumnDescriptor target)", design)
                tail = design.split("void RenderTail(ColumnDescriptor target)", 1)[1].split("}", 1)[0]
                self.assertLess(tail.index("RenderCopyTo(target)"), tail.index("RenderSigners(target)"),
                                "رونوشت باید قبل از امضا چاپ شود")
                # گروه «آخرین پاراگراف + رونوشت + امضا»؛ ولی فهرست بلند از گروه جدا می‌شود
                # تا ارتفاع گروه از صفحه بیشتر نشود.
                self.assertIn("keepWithTrailingText: copySplitsFreely ? null : RenderTail", design)
                self.assertIn("const int CopyGroupLimit = 6;", design)
                self.assertIn("if (copyNames.Count > CopyGroupLimit) RenderCopyTo(col);", design)
                self.assertIn("RenderSigners(col);", design)

                # مسیر کلاسیک: هم‌ترتیب و با همان بلوک مشترک.
                classic = outgoing.split("void AddSubjectAndFollowingContent(", 1)[1]
                classic = classic.split("// QuestPDF starts page.Header()", 1)[0]
                self.assertLess(classic.index("LetterPrintTemplate.CopyToBlock("),
                                classic.index("AddSignerBlock(col);"),
                                "در مسیر کلاسیک هم رونوشت باید قبل از امضا بیاید")

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
                # مختصات مطلق صفحه، نه داخل حاشیه‌ها (وگرنه در A5 مقدارها روی برچسب می‌افتند).
                self.assertIn("host.AlignLeft().AlignTop()", outgoing)
                self.assertIn("var safeLeft = Math.Max(fields.Min(box => box.Left), _designPrint.MetaSafeLeft);", outgoing)

    def test_each_meta_value_sits_on_its_own_template_row(self):
        """«پیوست» باید روی خط برچسب خودش بنشیند؛ ردیف‌های پیوسته فاصلهٔ قالب را حذف می‌کردند.

        کادرهای شماره/تاریخ/پیوست در قالب ۱۰۰۳ فاصلهٔ عمودی دارند (A4: ۶۰٫۵ / ۷۷ / ۹۶pt).
        اگر به‌صورت یک جدول پیوسته چیده شوند، خطا جمع می‌شود و «پیوست» تا ۶٫۷pt بالاتر
        از برچسبش می‌افتد. اکنون هر مقدار با Top خودش در لایهٔ سراسری چاپ می‌شود.
        """
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                grid = outgoing.split("private void AddDesignMetaGrid(", 1)[1]
                grid = grid.split("private static float FitFontSize", 1)[0]
                # هر مقدار با Top خودش: فاصلهٔ خالی تا آن Top، بعد یک ردیف به ارتفاع کادرِ قالب.
                self.assertIn("Math.Max(0f, field.Top)", grid)
                self.assertIn("column.Item().Height(gap)", grid)
                self.assertIn("column.Item().Height(rowHeight)", grid)
                self.assertNotIn(".Table(", grid,
                                 "ردیف‌های پیوستهٔ جدول فاصلهٔ عمودی قالب را حذف می‌کنند")

                # قالب: مرکز کادر مقدار باید نزدیک مرکز برچسب چاپ‌شده باشد.
                import xml.etree.ElementTree as ET
                for size, margin, label_centers in (
                        ("A4", 0.39, {"number": 66.5, "date": 84.5, "attachment": 103.2}),
                        ("A5", 0.39, {"number": 46.9, "date": 59.5, "attachment": 72.7})):
                    path = (tree / "Inventory.Api" / "Reports" / "OutgoingLetters" / "Companies"
                            / "1003" / f"Outgoing-{size}.mrt")
                    root = ET.parse(path).getroot()
                    centers = {}
                    for element in root.iter():
                        if element.get("type") != "Text":
                            continue
                        text = "".join(e.text or "" for e in element if e.tag == "Text")
                        for token, slot in (("LetterNumber", "number"), ("DateShams", "date"),
                                            ("Peyvast", "attachment")):
                            if f"DataSource1.{token}" in text:
                                rect = next((e.text or "" for e in element
                                             if e.tag == "ClientRectangle"), "")
                                _, y, _, h = [float(v) for v in rect.split(",")[:4]]
                                centers[slot] = (margin + y) * 72 + h * 72 / 2
                    self.assertEqual(len(centers), 3, f"{size}: سه مقدار قالب پیدا نشد")
                    for slot, center in centers.items():
                        self.assertAlmostEqual(center, label_centers[slot], delta=1.5,
                                               msg=f"{size}/{slot}: مقدار روی خط برچسب نیست")

    def test_a4_and_a5_scale_like_the_inner_letter(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                self.assertIn("page.Size(isA5 ? PageSizes.A5 : PageSizes.A4);", outgoing)
                self.assertIn("var likeInnerLetter = isA5 ? 9f : 11f;", outgoing)
                self.assertIn("var bodyFontSize = body.FontSize > 0 ? Math.Min(likeInnerLetter, body.FontSize) : likeInnerLetter;", outgoing)
                self.assertIn("page.MarginBottom(footerReserve);", outgoing)

    def test_content_starts_below_the_blue_band_not_below_the_whole_art(self):
        """متن از پایین «نوار آبی» شروع می‌شود (درخواست کارفرما)، نه از پایین کل گرافیک.

        قبلاً `HeaderArtBottom` (۲۳۲pt در A4) ملاک بود و متن بی‌دلیل پایین می‌افتاد؛
        اختلاف اندازه‌گیری‌شده تا پایین نوار ۱۱۹pt بود.
        """
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                layout = (office(tree) / "Outgoing" / "HomepaLetterheadLayout.cs").read_text(encoding="utf-8")
                self.assertIn("internal static float BandBottom(bool isA5) => isA5 ? 79.4f : 112.8f;", layout)
                # فاصلهٔ شروع متن برای A4 و A5 یکی است: ۹۷pt (درخواست کارفرما)
                self.assertIn("internal static float ContentTop(bool isA5) => BandBottom(isA5) + ContentStartGap;",
                              layout)
                self.assertIn("internal const float ContentStartGap = 97f;", layout)
                self.assertIn("Math.Max(HomepaLetterheadLayout.ContentTop(isA5), metaBottom + 6f)", outgoing)
                # مسیر بدون سربرگ همان رفتار قبلی را نگه می‌دارد.
                self.assertIn("Math.Max(artBottom + artGap, metaBottom + 6f)", outgoing)

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
