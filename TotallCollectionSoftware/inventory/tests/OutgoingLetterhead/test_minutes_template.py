"""چاپ صورتجلسه روی سربرگ «همان شرکتِ صورتجلسه» با قالب Stimulsoft (MRT).

سندباکس `dotnet` ندارد، پس PDF واقعی تولید نمی‌شود؛ این تست‌ها قرارداد را می‌سنجند:

• قالب `Reports/MeetingMinutes/Companies/{1003,3}/Minutes-{A4,A5}.mrt` وجود دارد و
  همان ساختار قالب نامهٔ صادره را دارد (StiSerializer، ReportUnit، Watermark.ImageBytes).
• هر پنج جزء الزامی روی قالب هست: عنوان، شماره صورتجلسه، تاریخ جلسه، حاضرین، غایبین.
• سرویس چاپ سربرگ را از شرکتِ خودِ صورتجلسه می‌گیرد (MeetingMinutes.CompanyId) و نه از
  شرکت فعال در درخواست؛ فقط برای صورتجلسه‌های قدیمیِ بدون شرکت، هدر X-Company-Id جایگزین است.
• هندسهٔ چاپ از خودِ MRT خوانده می‌شود (`MeetingMinutesDesignLayout`) و هر دو اندازهٔ A4/A5
  از مسیر قالب رد می‌شوند.
• هر دو درخت (src و inventory/src) یکسان‌اند.
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
CONTENT_GAP = 97.0


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
                    "CompanyName", "Status"}
        for size in SIZES:
            with self.subTest(size=size):
                root = ET.parse(template(TREES[0], "1003", size)).getroot()
                declare = root.find("./Dictionary/DataSources/DataSource1")
                self.assertIsNotNone(declare)
                self.assertEqual(declare.findtext("Alias"), "DataSource1")
                columns = {value.text.split(",")[0] for value in declare.find("Columns")}
                self.assertEqual(columns, expected)

    def test_five_mandatory_parts_are_placed_in_the_template(self):
        """عنوان، شماره صورتجلسه، تاریخ جلسه، حاضرین و غایبین — همه به‌صورت کادر مستقل."""
        required = {
            "Title": "{DataSource1.Title}",
            "Number": "{DataSource1.Number}",
            "MeetingDate": "{DataSource1.MeetingDate}",
            "Attendees": "{DataSource1.Attendees}",
            "Absentees": "{DataSource1.Absentees}",
        }
        for size in SIZES:
            with self.subTest(size=size):
                by_name = {component.tag: component for component in components(TREES[0], "1003", size)}
                for name, expression in required.items():
                    self.assertIn(f"{name}Value", by_name, f"کادر {name} در قالب نیست")
                    self.assertEqual(by_name[f"{name}Value"].findtext("Text"), expression)
                    if name != "Title":  # عنوان، سرصفحهٔ وسط‌چین است و برچسب نمی‌خواهد
                        self.assertIn(f"{name}Caption", by_name, f"برچسب {name} در قالب نیست")

    def test_content_starts_after_the_letterhead_band(self):
        """نخستین کادر چاپی درست پایین نوار سربرگ (۱۱۲٫۸ + ۹۷ برای A4، ۷۹٫۴ + ۹۷ برای A5)."""
        for size in SIZES:
            with self.subTest(size=size):
                element = next(c for c in components(TREES[0], "1003", size) if c.tag == "TitleValue")
                _, top, _, _ = rect_pt(element)
                self.assertAlmostEqual(top, BAND_BOTTOM[size] + CONTENT_GAP, places=1)

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

    def test_five_mandatory_values_are_always_written(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                service = self.service(tree)
                # چهار بخش «برچسب‌دار» از Slot می‌آیند؛ عنوان از design.Title.
                for slot in ("Number", "MeetingDate", "Attendees", "Absentees"):
                    self.assertIn(f"MeetingMinutesDesignLayout.Slot.{slot}", service)
                self.assertIn("design.Title", service)
                self.assertIn("بدون عنوان", service)          # عنوان خالی هم چاپ می‌شود
                self.assertIn('"بندها و تصمیمات"', service)    # برچسب پیش‌فرض فارسی بندها

    def test_second_page_starts_below_the_letterhead_band(self):
        """صورتجلسهٔ بلند: صفحهٔ دوم نباید زیر نوار آبی سربرگ برود."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                service = self.service(tree)
                self.assertIn("HomepaLetterheadLayout.BandBottom(isA5) + 24f", service)
                self.assertIn("page.Header().Height(headerReserve)", service)
                self.assertIn("cursor[0] = headerReserve;", service)

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
                for slot in ("Title", "Number", "MeetingDate", "Attendees", "Absentees", "Items"):
                    self.assertRegex(layout, rf"\n        {slot},?\n")
                self.assertIn("HasRequiredSlots", layout)
                self.assertIn("RightToLeft=True", layout)  # آینه‌کردن تراز در قالب RTL

    def test_layout_reader_is_mirrored_in_both_trees(self):
        first, second = (office(tree) / "MeetingMinutesDesignLayout.cs" for tree in TREES)
        self.assertEqual(first.read_bytes(), second.read_bytes())


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
