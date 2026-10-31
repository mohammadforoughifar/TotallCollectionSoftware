"""چاپ صورتجلسه روی سربرگ «همان شرکتِ صورتجلسه» با قالب Stimulsoft (MRT).

سندباکس `dotnet` ندارد، پس PDF واقعی تولید نمی‌شود؛ این تست‌ها قرارداد را می‌سنجند:

• قالب `Reports/MeetingMinutes/Companies/{1003,3}/Minutes-{A4,A5}.mrt` وجود دارد و
  همان ساختار قالب نامهٔ صادره را دارد (StiSerializer، ReportUnit، Watermark.ImageBytes).
• هر شش جزء روی قالب هست: عنوان، شماره، تاریخ جلسه، پیوست، حاضرین، غایبین؛ شماره/تاریخ/پیوست
  در کادرهای چاپ‌شدهٔ سربرگ (بالای صفحه) می‌نشینند و بدنه از عنوان شروع می‌شود — عنوان ۱۹۳pt
  در A4 و ۱۳۹pt در A5، زیر مستطیل آبی و راست‌چین.
• سرویس چاپ سربرگ را از شرکتِ خودِ صورتجلسه می‌گیرد (MeetingMinutes.CompanyId) و نه از
  شرکت فعال در درخواست؛ فقط برای صورتجلسه‌های قدیمیِ بدون شرکت، هدر X-Company-Id جایگزین است.
• هندسهٔ چاپ از خودِ MRT خوانده می‌شود (`MeetingMinutesDesignLayout`) و هر دو اندازهٔ A4/A5
  از مسیر قالب رد می‌شوند.
• هر دو درخت (src و inventory/src) یکسان‌اند.
• شرکت ۱ (فروغ آریا) قالب مخصوص خودش را دارد: سربرگ تصویری با **نقطه‌چین**، مقدارها با متن
  تیره روی نوار سفید (`Brush=White`) و روی **خط پایهٔ برچسب‌های خودِ سربرگ** (۳۵٫۸/۶۳٫۵/۹۰٫۵pt
  در A4)؛ عنوان ۲۴۰pt (زیر الگوی خاکستری که تا ۲۳۱٫۵pt می‌آید) و بدنه از ۲۷۹pt.
"""

from __future__ import annotations

import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
TREES = [ROOT / "src", ROOT / "inventory" / "src"]
COMPANIES = ("1003", "3")
SIZES = ("A4", "A5")
PAGE_MARGIN_INCH = 0.39
BAND_BOTTOM = {"A4": 112.8, "A5": 79.4}
# جای عنوان زیر مستطیل/روبان آبی سربرگ (پایین‌ترین پیکسل آبی: ۱۸۴pt در A4 و ≈۱۳۰pt در A5).
TITLE_TOP = {"A4": 193.0, "A5": 139.0}
# شروع بدنه (حاضرین) — ۳۹pt زیر عنوان در A4 و ۲۵pt در A5.
BODY_TOP = {"A4": 232.0, "A5": 164.0}
# کادرهای شماره/تاریخ/پیوست = همان برچسب‌های چاپ‌شدهٔ سربرگ ۱۰۰۳ (مثل قالب نامهٔ صادره).
META_BOX = {
    "A4": {"Number": (14.4, 59.2776, 82.08, 14.4), "MeetingDate": (14.4, 77.2776, 82.08, 14.4),
           "Attachment": (14.4, 95.9976, 82.08, 14.4)},
    "A5": {"Number": (7.2, 40.5216, 59.04, 12.96), "MeetingDate": (7.2, 53.2512, 59.04, 12.96),
           "Attachment": (7.2, 66.4848, 59.04, 12.96)},
}


# ── شرکت ۱ (فروغ آریا): هندسه اندازه‌گیری‌شده روی تصویر سربرگ خودش ──
ARIA = "1"
# برچسب‌های چاپ‌شده روی سربرگ: «تاریخ» خط پایه ۳۵٫۸pt، «شماره» ۶۳٫۵pt، «پیوست» ۹۰٫۵pt.
# کادر مقدار روی نقطه‌چین‌ها (x ۲۰..۸۸٫۵) می‌نشیند؛ A5 × ۰٫۷۰۵.
ARIA_META_BOX = {
    "A4": {"Number": (20.0, 53.1, 68.5, 14.4), "MeetingDate": (20.0, 25.4, 68.5, 14.4),
           "Attachment": (20.0, 80.1, 68.5, 14.4)},
    "A5": {"Number": (14.1, 37.25, 48.3, 10.2), "MeetingDate": (14.1, 17.65, 48.3, 10.2),
           "Attachment": (14.1, 56.25, 48.3, 10.2)},
}
ARIA_TITLE_TOP = {"A4": 240.0, "A5": 169.1}
ARIA_BODY_TOP = {"A4": 279.0, "A5": 196.6}
ARIA_META_FONT = {"A4": "Vazirmatn,10", "A5": "Vazirmatn,7.5"}
# پایین الگوی خاکستری سربرگ شرکت ۱؛ عنوان باید زیر آن باشد.
GRAY_PATTERN_BOTTOM = 231.5
# مرز محتوا: مثلث قرمز از ۶۴۱٫۵pt شروع می‌شود ولی شیبش تا y≈۶۸۲ به ستون متن (x<۵۵۸)
# نمی‌رسد؛ پس مرز ۶۶۸pt (رزرو ۱۷۴) امن است و ۳۲pt بیشتر از مرز قبلی (۶۳۶) جا می‌دهد.
# A5 همان نسبت: مرز ۴۸۰٫۳pt (رزرو ۱۱۵).
ARIA_FOOTER_RESERVE = {"A4": 174.0, "A5": 115.0}
ARIA_HEADER_RESERVE = {"A4": 121.0, "A5": 85.0}
# حداکثر پنج کادر در ردیف؛ ارتفاع کادر یک‌ردیفی و چندردیفی (جمع‌وجور).
ARIA_SIGNATURE = {"A4": (5, 46.0, 34.0), "A5": (5, 38.0, 29.0)}


def reports(tree: Path) -> Path:
    return tree / "Inventory.Api" / "Reports"


def office(tree: Path) -> Path:
    return tree / "Inventory.Api" / "Services" / "Office"


def template(tree: Path, company: str, size: str) -> Path:
    return reports(tree) / "MeetingMinutes" / "Companies" / company / f"Minutes-{size}.mrt"


def rect_pt(component: ET.Element) -> tuple[float, float, float, float]:
    """مستطیل جزء در نقطه، نسبت به گوشهٔ بالا-چپ کاغذ (حاشیهٔ صفحه ۰٫۳۹ اینچ)."""
    x, y, w, h = [float(value) for value in component.findtext("ClientRectangle").split(",")]
    return ((PAGE_MARGIN_INCH + x) * 72, (PAGE_MARGIN_INCH + y) * 72, w * 72, h * 72)


def components(tree: Path, company: str, size: str) -> list[ET.Element]:
    root = ET.parse(template(tree, company, size)).getroot()
    return list(root.find("./Pages/Page1/Components"))


class MinutesTemplateFileTests(unittest.TestCase):
    def test_templates_exist_for_homepa_companies_in_both_sizes(self):
        for tree in TREES:
            for company in COMPANIES:
                for size in SIZES:
                    with self.subTest(tree=tree.name, company=company, size=size):
                        path = template(tree, company, size)
                        self.assertTrue(path.is_file(), f"قالب صورتجلسه نیست: {path}")

    def test_templates_are_byte_identical_between_trees(self):
        for company in COMPANIES:
            for size in SIZES:
                with self.subTest(company=company, size=size):
                    first, second = (template(tree, company, size) for tree in TREES)
                    self.assertEqual(first.read_bytes(), second.read_bytes())

    def test_page_is_the_requested_paper_with_letterhead_geometry(self):
        expected = {"A4": ("A4", "8.27", "11.69"), "A5": ("A5", "5.83", "8.27")}
        for tree in TREES:
            for size in SIZES:
                with self.subTest(tree=tree.name, size=size):
                    page = ET.parse(template(tree, "1003", size)).getroot().find("./Pages/Page1")
                    paper, width, height = expected[size]
                    self.assertEqual(page.findtext("PaperSize"), paper)
                    self.assertEqual(page.findtext("PageWidth"), width)
                    self.assertEqual(page.findtext("PageHeight"), height)
                    self.assertEqual(page.findtext("Margins"), "0.39,0.39,0.39,0.39")

    def test_letterhead_of_company_1003_is_embedded_in_the_template(self):
        """سربرگ شرکت ۱۰۰۳ داخل خودِ قالب است؛ چاپ به فایل wwwroot وابسته نیست."""
        for size in SIZES:
            with self.subTest(size=size):
                watermark = ET.parse(template(TREES[0], "1003", size)).getroot().find("./Pages/Page1/Watermark")
                self.assertIsNotNone(watermark, "Watermark (سربرگ) در قالب نیست")
                self.assertEqual(watermark.findtext("ImageBehind"), "True")
                self.assertEqual(watermark.findtext("ImageStretch"), "True")
                self.assertGreater(len(watermark.findtext("ImageBytes") or ""), 10_000)

    def test_data_source_declares_every_changeable_value(self):
        expected = {"Number", "Title", "MeetingDate", "Attendees", "Absentees", "Items",
                    "CompanyName", "Status", "Attachment"}
        for size in SIZES:
            with self.subTest(size=size):
                root = ET.parse(template(TREES[0], "1003", size)).getroot()
                declare = root.find("./Dictionary/DataSources/DataSource1")
                self.assertIsNotNone(declare)
                self.assertEqual(declare.findtext("Alias"), "DataSource1")
                columns = {value.text.split(",")[0] for value in declare.find("Columns")}
                self.assertEqual(columns, expected)

    def test_mandatory_parts_are_placed_in_the_template(self):
        """عنوان، شماره، تاریخ، پیوست، حاضرین و غایبین — همه به‌صورت کادر مستقل."""
        required = {
            "Title": "{DataSource1.Title}",
            "Number": "{DataSource1.Number}",
            "MeetingDate": "{DataSource1.MeetingDate}",
            "Attachment": "{DataSource1.Attachment}",
            "Attendees": "{DataSource1.Attendees}",
            "Absentees": "{DataSource1.Absentees}",
        }
        for size in SIZES:
            with self.subTest(size=size):
                by_name = {component.tag: component for component in components(TREES[0], "1003", size)}
                for name, expression in required.items():
                    self.assertIn(f"{name}Value", by_name, f"کادر {name} در قالب نیست")
                    self.assertEqual(by_name[f"{name}Value"].findtext("Text"), expression)
                # برچسب فقط برای بخش‌های بدنه؛ شماره/تاریخ/پیوست برچسبِ چاپ‌شدهٔ سربرگ را
                # دارند و برچسبشان نباید دوباره در متن صورتجلسه بیاید.
                for name in ("Attendees", "Absentees", "Items"):
                    self.assertIn(f"{name}Caption", by_name, f"برچسب {name} در قالب نیست")
                for name in ("Number", "MeetingDate", "Attachment"):
                    self.assertNotIn(f"{name}Caption", by_name,
                                     f"برچسب {name} نباید در متن تکرار شود")

    def test_number_date_and_attachment_sit_on_the_printed_header_labels(self):
        """شماره/تاریخ/پیوست در کادرهای سفید بالای سربرگ، با متن سفید."""
        for size in SIZES:
            with self.subTest(size=size):
                by_name = {component.tag: component for component in components(TREES[0], "1003", size)}
                for name, expected in META_BOX[size].items():
                    component = by_name[f"{name}Value"]
                    actual = rect_pt(component)
                    for got, wanted in zip(actual, expected):
                        self.assertAlmostEqual(got, wanted, places=1, msg=f"{size} {name}")
                    self.assertEqual(component.findtext("TextBrush"), "White", f"{size} {name}")
                # «پیوست» درشت‌تر از شماره/تاریخ است (مثل قالب نامهٔ صادره).
                self.assertEqual(by_name["AttachmentValue"].findtext("Font"), "Vazirmatn,12")

    def test_content_starts_after_the_letterhead_band(self):
        """عنوان زیر مستطیل/روبان آبی و بدنه (حاضرین) پایین‌تر از آن — درخواست کارفرما."""
        for size in SIZES:
            with self.subTest(size=size):
                by_name = {component.tag: component for component in components(TREES[0], "1003", size)}
                left, title_top, width, title_height = rect_pt(by_name["TitleValue"])
                _, body_top, _, _ = rect_pt(by_name["AttendeesValue"])
                self.assertAlmostEqual(title_top, TITLE_TOP[size], places=1)
                self.assertGreater(title_top, BAND_BOTTOM[size])       # پایین‌تر از نوار آبی
                self.assertAlmostEqual(body_top, BODY_TOP[size], places=1)
                self.assertAlmostEqual(title_top + title_height, BODY_TOP[size], places=1)
                # عنوان کل عرض متن را دارد تا از سمت راست شروع شود (نه وسط‌چین با عرض کم).
                self.assertAlmostEqual(left, 28.08, places=1)
                self.assertAlmostEqual(width, {"A4": 529.2, "A5": 345.6}[size], places=1)
                self.assertEqual(by_name["TitleValue"].findtext("HorAlignment"), "Right")

    def test_all_texts_are_right_to_left_and_inside_the_page(self):
        for size in SIZES:
            with self.subTest(size=size):
                page_width_pt = float(ET.parse(template(TREES[0], "1003", size)).getroot()
                                      .findtext("./Pages/Page1/PageWidth")) * 72
                for component in components(TREES[0], "1003", size):
                    options = component.findtext("TextOptions") or ""
                    self.assertIn("RightToLeft=True", options, component.tag)
                    left, _, width, _ = rect_pt(component)
                    self.assertGreaterEqual(left, 0)
                    self.assertLessEqual(left + width, page_width_pt + 1, component.tag)

    def test_wellformed_xml(self):
        for tree in TREES:
            for company in COMPANIES:
                for size in SIZES:
                    with self.subTest(tree=tree.name, company=company, size=size):
                        ET.parse(template(tree, company, size))


class MinutesPrintServiceTests(unittest.TestCase):
    def service(self, tree: Path) -> str:
        return (office(tree) / "MeetingMinutesPrintService.cs").read_text(encoding="utf-8")

    def layout(self, tree: Path) -> str:
        return (office(tree) / "MeetingMinutesDesignLayout.cs").read_text(encoding="utf-8")

    def profile(self, tree: Path) -> str:
        return (office(tree) / "MinutesLetterheadProfile.cs").read_text(encoding="utf-8")

    def test_service_reads_the_template_of_the_minutes_company(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                service = self.service(tree)
                self.assertIn('Path.Combine("Reports", "MeetingMinutes", "Companies")', service)
                self.assertIn("FindTemplate(", service)
                self.assertIn("MeetingMinutesDesignLayout.TryRead(", service)
                self.assertIn('Minutes-{size}.mrt', service)
                # شرکت از خودِ صورتجلسه.
                self.assertIn("m.CompanyId", service)
                # و فقط برای صورتجلسهٔ قدیمیِ بدون شرکت، شرکت فعال درخواست.
                self.assertIn('Request.Headers["X-Company-Id"]', service)
                # اولویت: شرکتِ خودِ صورتجلسه؛ هدر شرکت فعال فقط برای رکوردهای قدیمیِ بی‌شرکت.
                self.assertIn("companyId = m.CompanyId is > 0 ? m.CompanyId", service)

    def test_letterhead_comes_from_the_template_watermark_first(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                service = self.service(tree)
                self.assertIn("ExtractTemplateLetterhead(templatePath)", service)
                self.assertIn("letterheadPath ??= registeredLetterhead;", service)
                self.assertIn('"Watermark"', service)
                self.assertIn('"ImageBytes"', service)

    def test_both_paper_sizes_are_supported(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                service = self.service(tree)
                self.assertIn('GeneratePdfAsync(int minutesId, string size = "A4")', service)
                self.assertIn('page.Size(isA5 ? PageSizes.A5 : PageSizes.A4)', service)
                for size in SIZES:
                    self.assertIn(size, service)

    def test_mandatory_values_are_always_written(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                service = self.service(tree)
                # متای سربرگ از کادرهای خودِ قالب خوانده می‌شود.
                for box in ("design.Number", "design.MeetingDate", "design.Attachment"):
                    self.assertIn(box, service)
                # حاضرین/غایبین: برچسب + مقدار از Slot.
                for slot in ("Attendees", "Absentees"):
                    self.assertIn(f"MeetingMinutesDesignLayout.Slot.{slot}", service)
                self.assertIn("design.Title", service)
                self.assertIn("بدون عنوان", service)          # عنوان خالی هم چاپ می‌شود
                self.assertIn('"بندها و تصمیمات"', service)    # برچسب پیش‌فرض فارسی بندها

    def test_header_meta_is_drawn_as_an_absolute_layer(self):
        """شماره/تاریخ/پیوست با مختصات مطلق روی برچسب‌های سربرگ، در لایهٔ Foreground."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                service = self.service(tree)
                self.assertIn("page.Foreground()", service)
                self.assertIn("DrawHeaderMeta(host, metaRows", service)
                self.assertIn("BuildHeaderMetaRows(design, number, meetingDate, attachmentText", service)
                self.assertIn("HeaderMetaRow", service)

    def test_attachment_flag_comes_from_registered_attachments(self):
        """صورتجلسه فیلد پیوست ندارد؛ «دارد/ندارد» از AppAttachments شمرده می‌شود."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                service = self.service(tree)
                self.assertIn('a.Module == "MeetingMinutes" && a.RefId == minutesId', service)
                self.assertIn('attachmentCount > 0 ? "دارد" : "ندارد"', service)

    def test_items_are_a_numbered_plain_list_not_a_table(self):
        """درخواست کارفرما: «اون حالت جدول برای بند ها و تصمیمات را بردار»."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                service = self.service(tree)
                self.assertIn("void ItemRow(ColumnDescriptor col, MeetingMinutesItem item)", service)
                self.assertIn("مسئول: ", service)
                self.assertIn("پیگیری: ", service)
                self.assertNotIn("ItemsTable", service)   # جدول بندها برداشته شد

    def test_signature_block_is_sized_for_manual_signature(self):
        """کادرهای امضا از پروفایل طرح سربرگ: ۱۰۰۳ A4 ۳ نفر/۵۲pt و A5 ۲ نفر/۴۴pt."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                service = self.service(tree)
                profile = self.profile(tree)
                self.assertIn("var maxColumns = Math.Max(1, letterheadProfile.SignatureColumns);", service)
                self.assertIn("var perRow = Math.Clamp(people.Count, Math.Min(3, maxColumns), maxColumns);",
                              service)
                self.assertIn("var boxHeight = rowCount > 1 && letterheadProfile.SignatureCompactBoxHeight > 0f",
                              service)
                self.assertIn("ShowEntire()", service)      # هیچ ردیفی نصفه نمی‌شود
                self.assertIn("محل امضای دستی", service)     # کادر خالی برای امضای روی برگه
                # چیدمان پیش‌فرض (۱۰۰۳/۳) همان قبلی است: A4 سه‌نفره ۵۲pt، A5 دونفره ۴۴pt.
                self.assertIn("SignatureColumns: 2,", profile)
                self.assertIn("SignatureBoxHeight: 44f", profile)
                self.assertIn("SignatureBoxHeight: 52f", profile)
                self.assertIn("SignatureCellWidth: 168f,", profile)
                # ۱۰۰۳ فشرده‌سازی چندردیفی ندارد (۰) تا چیدمان تأییدشده‌اش دست‌نخورده بماند.
                self.assertIn("SignatureCompactBoxHeight: 0f,", profile)

    def test_pending_signature_text_is_not_printed_by_the_template_path(self):
        """«در انتظار امضا» مخصوص گردش دیجیتال است و روی کاغذ نمی‌آید.

        فقط مسیر قالب (BuildTemplateContentPdf) بررسی می‌شود؛ مسیر پشتیبان قدیمی دست‌نخورده است.
        """
        for tree in TREES:
            with self.subTest(tree=tree.name):
                service = self.service(tree)
                start = service.index("private byte[] BuildTemplateContentPdf(")
                end = service.index("private sealed record HeaderMetaRow(")
                template_path = service[start:end]
                self.assertNotIn('"در انتظار امضا"', template_path)
                self.assertIn("person.SignedAt != null", template_path)   # فقط تاریخ امضا

    def test_second_page_starts_below_the_letterhead_band(self):
        """صورتجلسهٔ بلند: صفحهٔ دوم نباید زیر نوار آبی سربرگ برود."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                service = self.service(tree)
                profile = self.profile(tree)
                self.assertIn("page.Header().Height(headerReserve)", service)
                # رزرو سربرگ از پروفایل طرح می‌آید؛ پیش‌فرض همان «پایین نوار آبی + ۲۴» قبلی است.
                self.assertIn("var headerReserve = hasLetterhead ? letterheadProfile.HeaderReserve : 0f;",
                              service)
                self.assertIn("HomepaLetterheadLayout.BandBottom(true) + 24f", profile)
                self.assertIn("HomepaLetterheadLayout.BandBottom(false) + 24f", profile)
                # بدنه با فاصلهٔ خودِ قالب از زیر نوار آبی شروع می‌شود.
                self.assertIn("Space(col, titleTop - headerReserve)", service)

    def test_falls_back_when_template_is_missing(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                service = self.service(tree)
                self.assertIn("design is { HasRequiredSlots: true }", service)
                self.assertIn("BuildContentPdf(m, parts, items, letterheadPath != null, companyName)", service)

    def test_layout_reader_never_throws_and_knows_the_five_slots(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                layout = self.layout(tree)
                self.assertIn("internal static MeetingMinutesDesignLayout? TryRead(string? templatePath)", layout)
                self.assertIn("catch", layout)
                for slot in ("Title", "Number", "MeetingDate", "Attachment", "Attendees",
                             "Absentees", "Items"):
                    self.assertRegex(layout, rf"\n        {slot},?\n")
                self.assertIn("HasRequiredSlots", layout)
                self.assertIn("RightToLeft=True", layout)  # آینه‌کردن تراز در قالب RTL

    def test_no_member_name_collides_in_the_layout_reader(self):
        """CS0102: دو عضو هم‌نام در یک کلاس (مثل property «Number» و متد کمکی «Number»).

        این خطای واقعیِ بیلد بود؛ چون سندباکس کامپایلر C# ندارد، این‌جا اعلان‌های سطح کلاس
        استخراج و یکتایی نامشان بررسی می‌شود.
        """
        # فقط اعلان‌های سطح کلاس: دقیقاً ۴ فاصله تودرتویی و یکی از modifierهای دسترسی.
        # هر خط حداکثر یک عضو اعلان می‌کند: property (=>/{)، متد (() یا فیلد (=/;).
        declaration = re.compile(
            r"^ {4}(?:internal|private|protected|public)\s+"
            r"(?:(?:static|readonly|const|async|override|sealed)\s+)*"
            r"[A-Za-z_][\w.<>\[\]?, ]*?\s+(?P<name>[A-Za-z_]\w*)\s*(?:\(|\{|=|;)")

        for tree in TREES:
            with self.subTest(tree=tree.name):
                names: dict[str, int] = {}
                for line in self.layout(tree).splitlines():
                    if line.lstrip().startswith(("//", "///", "*", "/*")):
                        continue
                    # اعضای enum و پارامترهای record (مثل «        Number,») اعلان کلاس نیستند.
                    if line.rstrip().endswith(","):
                        continue
                    match = declaration.match(line)
                    if match is None:
                        continue
                    name = match.group("name")
                    if name in {"float", "string", "bool", "int", "void", "Box", "Slot",
                                "MeetingMinutesDesignLayout", "XElement", "IReadOnlyList"}:
                        continue
                    names[name] = names.get(name, 0) + 1

                duplicates = {name: count for name, count in names.items() if count > 1}
                self.assertEqual(duplicates, {},
                                 f"نام‌های تکراری در کلاس (CS0102): {duplicates}")
                # خودِ متد خواندن قالب هم باید شناسایی شود تا تست بی‌اثر نباشد.
                self.assertIn("TryRead", names)

    def test_layout_reader_is_mirrored_in_both_trees(self):
        first, second = (office(tree) / "MeetingMinutesDesignLayout.cs" for tree in TREES)
        self.assertEqual(first.read_bytes(), second.read_bytes())


class CompanyOneMinutesTemplateTests(unittest.TestCase):
    """صورتجلسه روی سربرگ شرکت ۱ (فروغ آریا) — «مثل ۱۰۰۳، با هندسهٔ سربرگ خودش».

    تفاوت‌های آگاهانه با ۱۰۰۳:
    • سربرگ تصویری است و برچسب «تاریخ/شماره/پیوست» + نقطه‌چین داخل خودِ تصویر چاپ شده؛ پس
      مقدارها با متن **تیره** روی نوار **سفید** (`Brush=White`) و روی خط پایهٔ برچسب می‌نشینند.
    • عنوان پایین‌تر از ۱۰۰۳ است، چون الگوی خاکستری سربرگ تا ۲۳۱٫۵pt می‌آید.
    • مرز پابرگ ۶۳۶pt در A4 (مثلث قرمز از ۶۴۱٫۵pt) → رزرو ۲۰۶pt؛ A5: ۴۴۸٫۲ → ۱۴۷pt.
    """

    def test_template_exists_in_every_read_path(self):
        """مسیر خواندن چاپ: هر سه پوشه باید قالب شرکت ۱ را داشته باشند."""
        for tree in TREES:
            for size in SIZES:
                with self.subTest(tree=tree.name, size=size):
                    self.assertTrue(template(tree, ARIA, size).is_file())
                    self.assertTrue((ROOT / "Report" / "meetingminutes" / ARIA /
                                     f"Report_{size}_MeetingMinutes.mrt").is_file())

    def test_templates_are_identical_in_every_tree_and_copy(self):
        for size in SIZES:
            with self.subTest(size=size):
                copies = [template(tree, ARIA, size) for tree in TREES]
                copies.append(ROOT / "Report" / "meetingminutes" / ARIA /
                              f"Report_{size}_MeetingMinutes.mrt")
                first = copies[0].read_bytes()
                for copy in copies[1:]:
                    self.assertEqual(first, copy.read_bytes(), f"قالب یکسان نیست: {copy}")

    def test_letterhead_is_company_ones_own_image(self):
        """سربرگ جاسازی‌شده باید همان تصویر شرکت ۱ باشد، نه سربرگ ۱۰۰۳."""
        outgoing = reports(TREES[0]) / "OutgoingLetters" / "Companies" / ARIA / "Outgoing-A4.mrt"
        outgoing_watermark = ET.parse(outgoing).getroot().find("./Pages/Page1/Watermark")
        for tree in TREES:
            for size in SIZES:
                with self.subTest(tree=tree.name, size=size):
                    watermark = ET.parse(template(tree, ARIA, size)).getroot().find("./Pages/Page1/Watermark")
                    self.assertIsNotNone(watermark, "Watermark (سربرگ) در قالب نیست")
                    self.assertEqual(watermark.findtext("ImageBytes"), outgoing_watermark.findtext("ImageBytes"))
                    self.assertEqual(watermark.findtext("ImageBehind"), "True")   # سربرگ پشت متن
                    self.assertEqual(watermark.findtext("Enabled"), "True")

    def test_meta_values_sit_on_the_printed_dotted_rows(self):
        """تاریخ/شماره/پیوست روی خط پایهٔ برچسب‌های چاپ‌شده، با متن تیره و نوار سفید."""
        for tree in TREES:
            for size in SIZES:
                with self.subTest(tree=tree.name, size=size):
                    by_name = {component.tag: component for component in components(tree, ARIA, size)}
                    for name, expected in ARIA_META_BOX[size].items():
                        component = by_name[f"{name}Value"]
                        actual = rect_pt(component)
                        for got, wanted in zip(actual, expected):
                            self.assertAlmostEqual(got, wanted, places=1, msg=f"{size} {name}")
                        self.assertEqual(component.findtext("TextBrush"), "Black", f"{size} {name}")
                        self.assertEqual(component.findtext("Brush"), "White", f"{size} {name}")
                        self.assertEqual(component.findtext("Font"), ARIA_META_FONT[size])
                    # «پیوست» دقیقاً روی ردیف سوم (خط پایهٔ ۹۰٫۵ / A5: ۶۳٫۸) است، نه بالاتر/پایین‌تر.
                    baseline = {"A4": (25.4, 53.1, 80.1), "A5": (17.65, 37.25, 56.25)}[size]
                    tops = [rect_pt(by_name[f"{name}Value"])[1] for name in ("MeetingDate", "Number", "Attachment")]
                    for got, wanted in zip(tops, baseline):
                        self.assertAlmostEqual(got, wanted, places=1)

    def test_meta_labels_are_not_repeated_in_the_minutes_body(self):
        for size in SIZES:
            with self.subTest(size=size):
                names = {component.tag for component in components(TREES[0], ARIA, size)}
                for name in ("Number", "MeetingDate", "Attachment"):
                    self.assertNotIn(f"{name}Caption", names, f"برچسب {name} نباید تکرار شود")

    def test_title_is_below_the_gray_pattern_and_right_aligned(self):
        for size in SIZES:
            with self.subTest(size=size):
                by_name = {component.tag: component for component in components(TREES[0], ARIA, size)}
                left, title_top, width, title_height = rect_pt(by_name["TitleValue"])
                _, body_top, _, _ = rect_pt(by_name["AttendeesValue"])
                self.assertAlmostEqual(title_top, ARIA_TITLE_TOP[size], places=1)
                pattern_bottom = GRAY_PATTERN_BOTTOM * (0.705 if size == "A5" else 1.0)
                self.assertGreater(title_top, pattern_bottom)          # زیر الگوی خاکستری
                self.assertAlmostEqual(title_top + title_height, ARIA_BODY_TOP[size], places=1)
                self.assertAlmostEqual(body_top, ARIA_BODY_TOP[size], places=1)
                self.assertAlmostEqual(left, 28.08, places=1)
                self.assertAlmostEqual(width, {"A4": 529.2, "A5": 345.6}[size], places=1)
                self.assertEqual(by_name["TitleValue"].findtext("HorAlignment"), "Right")

    def test_content_stays_above_the_red_triangle(self):
        """محتوا نباید روی مثلث قرمز پایین-راست بیفتد (مرز ۶۳۶pt در A4 و ۴۴۸٫۲ در A5)."""
        limit = {"A4": 668.0, "A5": 480.3}
        page_height = {"A4": 841.89, "A5": 595.28}
        for size in SIZES:
            with self.subTest(size=size):
                by_name = {component.tag: component for component in components(TREES[0], ARIA, size)}
                _, items_top, _, items_height = rect_pt(by_name["ItemsValue"])
                self.assertLessEqual(items_top + items_height, limit[size])
                # رزرو پابرگ دقیقاً همان مرز را می‌سازد (اختلاف فقط از گردکردن point).
                self.assertLessEqual(page_height[size] - ARIA_FOOTER_RESERVE[size],
                                     limit[size] + 0.5)

    def test_profile_reserves_keep_text_off_the_footer_art(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                profile = (office(tree) / "MinutesLetterheadProfile.cs").read_text(encoding="utf-8")
                for size, reserve in ARIA_FOOTER_RESERVE.items():
                    self.assertIn(f"FooterReserve: {reserve:g}f", profile, size)
                for size, reserve in ARIA_HEADER_RESERVE.items():
                    self.assertIn(f"HeaderReserve: {reserve:g}f", profile, size)
                # تا پنج کادر امضا در ردیف + کادر جمع‌وجورتر برای حالت چندردیفی.
                self.assertIn("SignatureColumns: 5,", profile)
                self.assertIn("SignatureBoxHeight: 46f", profile)
                self.assertIn("SignatureBoxHeight: 38f", profile)
                self.assertIn("SignatureCompactBoxHeight: 34f", profile)
                self.assertIn("SignatureCompactBoxHeight: 29f", profile)

    def test_layout_reader_reads_the_white_backing_from_the_template(self):
        """قالب رنگ پس‌زمینه را اعلام می‌کند و خوانندهٔ چیدمان آن را می‌خواند."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                layout = (office(tree) / "MeetingMinutesDesignLayout.cs").read_text(encoding="utf-8")
                self.assertIn('Value(component, "Brush")', layout)
                self.assertIn("string? Fill", layout)
                service = (office(tree) / "MeetingMinutesPrintService.cs").read_text(encoding="utf-8")
                self.assertIn("cell.Background(row.Fill!", service)
                # مرزهای کاغذ شرکت ۱ از پروفایل، نه از اعداد ثابت داخل سرویس.
                self.assertIn("MinutesLetterheadProfile.For(companyId, isA5)", service)

    def test_builder_script_knows_company_one(self):
        script = (ROOT / "tools" / "build_minutes_mrt.py").read_text(encoding="utf-8")
        self.assertIn('COMPANIES = ("1003", "3", "1")', script)
        self.assertIn("ARIA_META_BOX", script)
        self.assertIn("ARIA_LAYOUT", script)
        self.assertIn('meta_fill": "White"', script)


class SignatureFollowsTheTextTests(unittest.TestCase):
    """درخواست کارفرما: «امضا باید همیشه بلافاصله بعد از پایان متن بیاید، نه در صفحهٔ دیگر.»

    خروجی واقعی صورتجلسهٔ ۲۰۳۵ روی سربرگ شرکت ۱ دو صفحه شد: متن تا y≈۵۳۷٫۷pt آمد و بلوک امضا
    (که کل بلوک یک‌جا ShowEntire بود و ≈۱۱۱pt ارتفاع داشت) با ۷pt کم‌آوردن به صفحهٔ دوم پرید.
    """

    # پایان متن در همان PDF واقعی (page 1) و مرز محتوای شرکت ۱ در A4.
    REAL_CONTENT_END = 537.7
    A4_LIMIT = 841.89 - ARIA_FOOTER_RESERVE["A4"]

    def service(self, tree: Path) -> str:
        return (office(tree) / "MeetingMinutesPrintService.cs").read_text(encoding="utf-8")

    def test_signature_block_is_not_split_off_as_one_piece(self):
        """نه کل بلوک، بلکه هر ردیف یک واحد است؛ برچسب هم به ردیف اولش چسبیده."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                service = self.service(tree)
                # بلوک بیرونی ShowEntire ندارد (همان چیزی که کل امضا را به صفحهٔ بعد می‌انداخت).
                self.assertIn("col.Item().PaddingTop(letterheadProfile.SignatureTopPadding).Column(block =>",
                              service)
                # هر ردیف جداگانه کامل می‌ماند.
                self.assertIn("block.Item().PaddingBottom(cellGap).ShowEntire().Column(rowBlock =>", service)
                # برچسب داخل ردیف اول است، پس هیچ‌وقت تنها نمی‌ماند.
                caption_at = service.index('LetterPrintTemplate.RtlLabel("محل امضای حاضرین")')
                first_row_at = service.index("if (isFirstRow)")
                self.assertGreater(caption_at, first_row_at)

    def test_compact_single_row_fits_right_after_the_real_text(self):
        """با اعداد فعلی، بلوک امضای همین صورتجلسه در همان صفحهٔ اول جا می‌شود."""
        columns, box_height, compact = ARIA_SIGNATURE["A4"]
        # ارتفاع بلوک: فاصله از متن + برچسب + ردیف (کادر، نام، تاریخ امضا) + فاصلهٔ پایانی.
        top_padding, caption_bottom, name_padding, gap = 6.0, 6.0, 3.0, 8.0
        caption = 10.5 * 1.6
        name = 8.5 * 1.6
        signed = 7.0 * 1.6
        block = top_padding + caption + caption_bottom + box_height + name_padding + name + signed + gap
        self.assertLessEqual(self.REAL_CONTENT_END + block, self.A4_LIMIT,
                             "بلوک امضا در صفحهٔ اول جا نمی‌شود → دوباره به صفحهٔ دوم می‌رود")
        # مرز قبلی (رزرو ۲۰۶ → ۶۳۶pt) برای همین بلوک کافی نبود؛ این رگرسیون نباید برگردد.
        self.assertGreater(self.REAL_CONTENT_END + block, 841.89 - 206.0)
        # دو نفر → یک ردیف سه‌ستونه (کادرها بی‌دلیل پهن نمی‌شوند، ردیف دوم هم لازم نیست).
        per_row = min(max(2, min(3, columns)), columns)
        self.assertEqual(per_row, 3)
        row_count = -(-2 // per_row)
        self.assertEqual(row_count, 1)
        self.assertLess(compact, box_height)          # کادر حالت چندردیفی جمع‌وجورتر است

    def test_triangle_never_reaches_the_text_column(self):
        """مرز ۶۶۸pt امن است: مثلث قرمز تا y≈۶۸۲ به ستون متن (x<۵۵۸) نمی‌رسد."""
        self.assertAlmostEqual(self.A4_LIMIT, 667.89, places=2)
        self.assertLess(self.A4_LIMIT, 682.0)

    def test_other_companies_keep_their_calibrated_signature_layout(self):
        """۱۰۰۳/۳ دست‌نخورده: A4 سه کادر ۱۶۸×۵۲ و A5 دو کادر ۴۴، بدون فشرده‌سازی."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                profile = (office(tree) / "MinutesLetterheadProfile.cs").read_text(encoding="utf-8")
                self.assertIn("FooterReserve: HomepaLetterheadLayout.DesignFooterReserve(true)", profile)
                self.assertIn("SignatureCellWidth: 168f,", profile)
                self.assertIn("SignatureCompactBoxHeight: 0f,", profile)
                self.assertIn("SignatureTopPadding: 10f)", profile)
                self.assertIn("SignatureTopPadding: 14f)", profile)


class MinutesPrintRouteTests(unittest.TestCase):
    def controller(self, tree: Path) -> str:
        return (tree / "Inventory.Api" / "Controllers" / "Office" / "MeetingMinutesController.cs") \
            .read_text(encoding="utf-8")

    def test_route_accepts_paper_size(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                controller = self.controller(tree)
                self.assertIn("Print(int id, [FromQuery] string? size = null)", controller)
                self.assertIn('GeneratePdfAsync(id, paper)', controller)
                self.assertIn("A5", controller)

    def test_configuration_defaults_are_declared(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                settings = (tree / "Inventory.Api" / "appsettings.json").read_text(encoding="utf-8")
                self.assertIn('"MeetingMinutes"', settings)
                self.assertIn('"Reports/MeetingMinutes/Companies"', settings)
                self.assertIn('Reporting:MeetingMinutes:TemplateRoot', self._normalise(settings) or
                              self._service_text(tree))

    def _normalise(self, settings: str) -> str:
        return "Reporting:MeetingMinutes:TemplateRoot" if '"TemplateRoot"' in settings else ""

    def _service_text(self, tree: Path) -> str:
        return (office(tree) / "MeetingMinutesPrintService.cs").read_text(encoding="utf-8")


if __name__ == "__main__":
    unittest.main(verbosity=2)
