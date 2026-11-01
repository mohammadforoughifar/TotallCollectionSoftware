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


def members_of(source: str, type_name: str) -> set[str]:
    """عضوهای اعلام‌شدهٔ یک کلاس (همان اندازه‌ای که کد استفاده می‌کند کافی است)."""
    body = source.split(f"class {type_name}", 1)[1]
    names = set(re.findall(r"\b(?:internal|public|private|protected)\s+"
                           r"(?:static\s+|readonly\s+|const\s+|override\s+|async\s+)*"
                           r"[\w<>\[\],\.\?\(\) ]+?\s+(\w+)\s*[\(;{=]", body))
    names |= set(re.findall(r"\bconst\s+\w+\s+(\w+)", body))
    return names


class CompilationGuardTests(unittest.TestCase):
    """خطاهای واقعی کامپایل که در IDE دیده شد — تا دوباره تکرار نشوند.

    * در کنترلرها نام `File` به `ControllerBase.File(byte[], string)` اشاره می‌کند، نه
      `System.IO.File` ⇒ CS0119.
    * `LetterheadFolders(Clean(roots))` یک `IReadOnlyList<string>` را به پارامتر
      `params string?[]` می‌داد ⇒ CS1503.
    * `InnerLetterPrintService` متد `ResolveCompanyTemplatePath` را صدا می‌زد که پس از
      یکپارچه‌سازی حذف شده بود ⇒ CS0117.
    """

    def test_controllers_never_use_bare_file(self):
        pattern = re.compile(r"(?<![.\w])File\.(Exists|OpenRead|OpenWrite|ReadAll\w*|WriteAll\w*|Delete|Copy|Move)\b")
        for tree in TREES:
            controllers = tree / "Inventory.Api" / "Controllers"
            if not controllers.is_dir():
                continue
            for path in controllers.rglob("*.cs"):
                with self.subTest(file=str(path.relative_to(tree))):
                    self.assertEqual(pattern.findall(path.read_text(encoding="utf-8")), [])

    def test_params_members_are_not_called_with_a_list(self):
        source = (office(TREES[0]) / "PrintAssetResolver.cs").read_text(encoding="utf-8")
        # Clean(...) یک IReadOnlyList است؛ برای پارامتر params باید آرایه شود.
        self.assertNotIn("LetterheadFolders(Clean(roots))", source)
        self.assertIn("LetterheadFolders(Clean(roots).ToArray())", source)

    def test_every_inner_letterhead_member_call_exists(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = (office(tree) / "InnerLetterheadTemplates.cs").read_text(encoding="utf-8")
                defined = members_of(source, "InnerLetterheadTemplates")
                self.assertIn("ResolveCompanyTemplatePath", defined)
                self.assertIn("CompanyCandidates", defined)

                used = set()
                for path in (tree / "Inventory.Api").rglob("*.cs"):
                    used |= set(re.findall(r"InnerLetterheadTemplates\.(\w+)\s*\(",
                                           path.read_text(encoding="utf-8")))
                self.assertEqual(sorted(used - defined), [], "متد استفاده‌شده‌ای وجود ندارد")

    def test_every_resolver_member_call_exists(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = (office(tree) / "PrintAssetResolver.cs").read_text(encoding="utf-8")
                defined = members_of(source, "PrintAssetResolver") | {
                    "OutgoingLetters", "InnerLetters", "MeetingMinutes"}

                used = set()
                for path in (tree / "Inventory.Api").rglob("*.cs"):
                    used |= set(re.findall(r"PrintAssetResolver\.(\w+)\s*[\(;=]",
                                           path.read_text(encoding="utf-8")))
                self.assertEqual(sorted(used - defined), [], "عضو استفاده‌شده‌ای وجود ندارد")


class QuestPdfLayersTests(unittest.TestCase):
    """«The Layers component needs to have exactly one primary layer. It has none.»

    خطای واقعی چاپ نامهٔ داخلی شرکت ۱۰۰۵: سربرگش سه کادر (پیوست/شماره/تاریخ) در یک
    ردیف دارد و <see>HeaderMetaPainter</see> آن ردیف را با Layers رسم می‌کرد، بدون
    لایهٔ اصلی. لایهٔ اصلیِ خالی هم راه‌حل نیست: اندازهٔ ردیف Empty می‌شود و مقدارها
    رسم نمی‌شوند؛ پس لایهٔ اصلی باید خودِ کادر اول باشد.
    """

    def layers_files(self):
        for tree in TREES:
            office_dir = office(tree)
            for path in [office_dir / "HeaderMetaPainter.cs",
                         office_dir / "Outgoing" / "OutgoingLetterPrintService.cs"]:
                if path.exists():
                    yield path

    def test_every_layers_block_declares_a_primary_layer(self):
        for path in self.layers_files():
            with self.subTest(file=path.name):
                source = path.read_text(encoding="utf-8")
                self.assertGreater(source.count(".Layers("), 0)
                self.assertGreaterEqual(source.count("PrimaryLayer()"), source.count(".Layers("),
                                        "هر Layers نیاز به یک لایهٔ اصلی دارد")

    def test_primary_layer_is_never_left_empty(self):
        for path in self.layers_files():
            with self.subTest(file=path.name):
                source = path.read_text(encoding="utf-8")
                # لایهٔ اصلیِ خالی ⇒ ارتفاع/اندازهٔ Empty ⇒ مقدارها چاپ نمی‌شوند.
                self.assertNotIn("PrimaryLayer().Extend()", source)
                self.assertNotIn("PrimaryLayer().Height(", source)

    def test_minutes_and_inner_letter_share_the_meta_painter(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                minutes = (office(tree) / "MeetingMinutesPrintService.cs").read_text(encoding="utf-8")
                renderer = (office(tree) / "InnerLetterPdfRenderer.cs").read_text(encoding="utf-8")
                self.assertIn("HeaderMetaPainter.Draw(", minutes)
                self.assertIn("HeaderMetaPainter.Draw(", renderer)

    def test_inner_letter_print_survives_a_meta_painter_failure(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = (office(tree) / "InnerLetterPrintService.cs").read_text(encoding="utf-8")
                # شکست رسم سه مقدار سربرگ نباید کل چاپ را از کار بیندازد.
                self.assertIn("catch (Exception metaFailure) when (meta != null)", source)


if __name__ == "__main__":
    unittest.main()
