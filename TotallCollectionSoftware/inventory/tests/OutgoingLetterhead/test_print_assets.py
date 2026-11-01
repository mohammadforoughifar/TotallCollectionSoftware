"""چاپ شرکت ۱۰۰۵: «چرا روی سربرگ خودش چاپ نمی‌شود؟» — قرارداد مسیر فایل‌ها.

گزارش کارفرما: صورتجلسهٔ ۱۰۰۵ بدون سربرگ چاپ می‌شد، نامهٔ داخلی‌اش خطا می‌داد و نامهٔ صادره‌اش
هم چاپ نمی‌شد. علت در «پیدا نشدن فایل‌های ورودی چاپ» بود و سه سرویس سه قاعدهٔ متفاوت داشتند:

* نامهٔ صادره نام فایلِ بدون مسیر («aria-1005-letterhead.pdf») را داخل
  `wwwroot/uploads/letterheads` می‌گشت، ولی نامهٔ داخلی و صورتجلسه فقط
  `wwwroot/<همان متن>` را ⇒ سربرگ ثبت‌شده پیدا نمی‌شد.
* نامهٔ داخلی پوشهٔ داخل مخزن (`inventory/src/Inventory.Api/Reports/…`) را می‌گشت، ولی
  نامهٔ صادره و صورتجلسه فقط ContentRoot/BaseDirectory ⇒ قالب تازهٔ `Companies/1005` تا اولین
  Publish کامل «وجود نداشت» و چاپ با خطای «سربرگ پیدا نشد» متوقف می‌شد.

این تست‌ها همان دو قاعده را می‌سنجند (منبعِ حقیقت: `PrintAssetResolver`).
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
TREES = [ROOT / "src", ROOT / "inventory" / "src"]
COMPANY = "1005"


def office(tree: Path) -> Path:
    return tree / "Inventory.Api" / "Services" / "Office"


def reports(tree: Path) -> Path:
    return tree / "Inventory.Api" / "Reports"


class ResolverSourceContractTests(unittest.TestCase):
    """هر سه سرویس چاپ باید از یک قاعدهٔ مشترک استفاده کنند."""

    def test_both_trees_carry_the_shared_resolver(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                path = office(tree) / "PrintAssetResolver.cs"
                self.assertTrue(path.is_file(), f"PrintAssetResolver.cs نیست: {path}")
                source = path.read_text(encoding="utf-8")
                for marker in (
                    "TemplateFolders(",
                    "TemplateCandidates(",
                    "LetterheadCandidates(",
                    "internal static string? Letterhead(",
                    "LetterheadRect(",
                ):
                    self.assertIn(marker, source, marker)

    def test_resolver_searches_the_repository_tree_before_build_outputs(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = (office(tree) / "PrintAssetResolver.cs").read_text(encoding="utf-8")
                order = [
                    source.index("// ۱) مسیر صریح تنظیمات"),
                    source.index("// ۲) خودِ مخزن سورس"),
                    source.index("// ۳) ریشه‌های اجرا"),
                ]
                self.assertEqual(order, sorted(order))
                self.assertIn('"inventory", "src", "Inventory.Api", "Reports", kind, "Companies", company',
                              source)

    def test_letterhead_resolver_accepts_a_bare_file_name(self):
        """مقدار ثبت‌شده ممکن است فقط نام فایل باشد؛ فایل در uploads/letterheads است."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = (office(tree) / "PrintAssetResolver.cs").read_text(encoding="utf-8")
                self.assertIn('Path.Combine(root, "uploads", "letterheads", name)', source)
                self.assertIn('Path.Combine(root, "wwwroot", "uploads", "letterheads", name)', source)
                self.assertIn('clean.StartsWith("uploads/"', source)

    def test_letterhead_resolver_ignores_persian_letter_variants(self):
        """ي/ك عربی، نیم‌فاصله و فاصله در نام فایل نباید باعث «پیدا نشد» شود."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = (office(tree) / "PrintAssetResolver.cs").read_text(encoding="utf-8")
                self.assertIn("private static string Normalize(string fileName)", source)
                self.assertIn("case '\\u064A':", source)      # ي
                self.assertIn("case '\\u0643':", source)      # ك
                self.assertIn("case '\\u200C':", source)      # نیم‌فاصله

    def test_every_print_service_delegates_to_the_resolver(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                inner = (office(tree) / "InnerLetterPrintService.cs").read_text(encoding="utf-8")
                minutes = (office(tree) / "MeetingMinutesPrintService.cs").read_text(encoding="utf-8")
                outgoing = (office(tree) / "Outgoing" / "OutgoingLetterPrintService.cs").read_text(encoding="utf-8")

                self.assertIn("PrintAssetResolver.Letterhead(", inner)
                self.assertIn("PrintAssetResolver.Letterhead(", minutes)
                self.assertIn("PrintAssetResolver.Letterhead(", outgoing)

                self.assertIn("PrintAssetResolver.TemplateCandidates(", minutes)
                self.assertIn("PrintAssetResolver.TemplateFolders(", outgoing)

    def test_missing_files_are_logged_with_the_searched_paths(self):
        """«چاپ نشد» نباید بی‌سرنخ باشد؛ مسیرهای بررسی‌شده در لاگ/پیام خطا می‌آیند."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                inner = (office(tree) / "InnerLetterPrintService.cs").read_text(encoding="utf-8")
                minutes = (office(tree) / "MeetingMinutesPrintService.cs").read_text(encoding="utf-8")
                outgoing = (office(tree) / "Outgoing" / "OutgoingLetterPrintService.cs").read_text(encoding="utf-8")
                for source in (inner, minutes, outgoing):
                    self.assertIn("PrintAssetResolver.Describe(", source)
                # نامهٔ داخلی علت واقعی استثنا را هم در پیام کاربر می‌آورد.
                self.assertIn("علت: {ex.Message}", inner)


class Company1005AssetTests(unittest.TestCase):
    """فایل‌های ۱۰۰۵ باید دقیقاً در همان مسیرهایی باشند که چاپ می‌گردد."""

    def test_company_1005_templates_exist_in_both_trees(self):
        expected = {
            "OutgoingLetters": ("Outgoing-A4.mrt", "Outgoing-A5.mrt"),
            "InnerLetters": ("Inner-A4.mrt", "Inner-A5.mrt"),
            "MeetingMinutes": ("Minutes-A4.mrt", "Minutes-A5.mrt"),
        }
        for tree in TREES:
            for kind, files in expected.items():
                for name in files:
                    with self.subTest(tree=tree.name, kind=kind, file=name):
                        path = reports(tree) / kind / "Companies" / COMPANY / name
                        self.assertTrue(path.is_file(), f"قالب ۱۰۰۵ نیست: {path}")

    def test_company_1005_letterhead_file_is_where_the_resolver_looks(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                path = (tree / "Inventory.Api" / "wwwroot" / "uploads" / "letterheads"
                        / "aria-1005-letterhead.pdf")
                self.assertTrue(path.is_file(), f"فایل سربرگ ۱۰۰۵ نیست: {path}")

    def test_design_folder_mirror_also_exists_for_minutes_and_outgoing(self):
        """پوشهٔ طراحی (`Report/…`) آخرین جای جست‌وجوی resolver است."""
        self.assertTrue((ROOT / "Report" / "meetingminutes" / COMPANY).is_dir())
        self.assertTrue((ROOT / "Report" / "outgoingletter").is_dir())


class PrintAssetsDiagnosticsTests(unittest.TestCase):
    """نقطهٔ پایانی تشخیص باید در هر دو درخت باشد (کاربر بدون لاگ هم ببیند چه خبر است)."""

    def test_controller_reports_paths_for_all_three_prints(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                path = tree / "Inventory.Api" / "Controllers" / "Office" / "PrintAssetsController.cs"
                self.assertTrue(path.is_file(), f"کنترلر تشخیص نیست: {path}")
                source = path.read_text(encoding="utf-8")
                self.assertIn('[Route("api/print-assets")]', source)
                self.assertIn('public async Task<ActionResult> Diagnose(', source)
                for kind in ("OutgoingLetters", "InnerLetters", "MeetingMinutes"):
                    self.assertIn(f"PrintAssetResolver.{kind}", source)
                self.assertIn("letterhead", source)
                self.assertIn("candidates", source)


class LetterheadOverlayTests(unittest.TestCase):
    """سربرگِ با نسبت نامناسب نباید کشیده شود؛ وگرنه نقطه‌چین جابه‌جا می‌شود."""

    def test_overlay_keeps_the_artwork_aspect(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                for relative in ("InnerLetterPrintService.cs", "MeetingMinutesPrintService.cs",
                                 "Outgoing/OutgoingLetterPrintService.cs"):
                    source = (office(tree) / relative).read_text(encoding="utf-8")
                    self.assertIn("ArtRect(", source, relative)
                    self.assertIn("PrintAssetResolver.LetterheadRect(", source, relative)

    def test_rect_rule_keeps_exact_paper_untouched(self):
        """اختلاف صفر (سربرگ A4 واقعی) → همان کل صفحه؛ اختلاف ۲٪ → حفظ نسبت."""
        source = (office(TREES[0]) / "PrintAssetResolver.cs").read_text(encoding="utf-8")
        body = source.split("internal static (double X, double Y, double Width, double Height) LetterheadRect(",
                            1)[1].split("// ==================== پیام خطا", 1)[0]
        self.assertIn("drift <= 0.005", body)
        self.assertIn("drift > 0.03", body)


class ForeignLetterheadGuardTests(unittest.TestCase):
    """رکورد شرکت نباید سربرگ شرکت دیگری را به چاپ بدهد.

    خطای گزارش‌شدهٔ «نامهٔ شرکت ۱۰۰۵ با سربرگ شرکت ۱ چاپ می‌شود» از این‌جا می‌آمد که
    `SystemCompanies.LetterheadFileName` می‌توانست به فایل شرکت دیگری اشاره کند و کد
    فقط «وجود فایل» را می‌سنجید.
    """

    def test_outgoing_rejects_a_registered_file_owned_by_another_company(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = (office(tree) / "Outgoing" / "OutgoingLetterPrintService.cs").read_text(encoding="utf-8")
                self.assertIn("RegisteredLetterheadBelongsToAnotherCompany(", source)
                # خانواده‌های شناخته‌شده: homepa = ۳/۱۰۰۳، aria = ۱۰۰۵، forough = شرکت ۱
                self.assertIn('name.Contains("homepa"', source)
                self.assertIn('name.Contains("aria"', source)
                self.assertIn('name.Contains("forough"', source)
                # گارد پیش از انتخاب نهایی فایل اجرا می‌شود
                self.assertLess(source.index("RegisteredLetterheadBelongsToAnotherCompany(selectedCompanyId"),
                                source.index("var letterheadPath = preferTemplate"))

    def test_company_1005_letterhead_is_recognised_as_its_own(self):
        """نام فایل Aria برای ۱۰۰۵ «بیگانه» تشخیص داده نمی‌شود."""
        letterheads = ROOT / "inventory" / "src" / "Inventory.Api" / "wwwroot" / "uploads" / "letterheads"
        aria = [path.name.lower() for path in letterheads.glob("aria*")]
        self.assertTrue(aria, "فایل سربرگ شرکت ۱۰۰۵ باید موجود باشد")
        self.assertTrue(all("homepa" not in name and "forough" not in name for name in aria))


if __name__ == "__main__":
    unittest.main()
