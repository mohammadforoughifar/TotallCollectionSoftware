"""«چاپ آماده نشد: بیش از ۹۰ ثانیه طول کشید» — قراردادهای سرعت و بازیابی چاپ.

گزارش کارفرما: چاپ نامهٔ صادره پس از ۹۰ ثانیه با پیام «ساخت نسخهٔ چاپ بیش از ۹۰ ثانیه
طول کشید» شکست خورد. سرور رندر را رها نمی‌کند، ولی هیچ‌کدام از این‌ها وجود نداشت:

* کش پایدار: خروجی ساخته‌شده فقط ۹۰ ثانیه در حافظه می‌ماند؛ اگر بودجهٔ کلاینت تمام شود،
  «تلاش مجدد» همان رندر سنگین را از صفر تکرار می‌کرد.
* تلاش خودکار: خطا بلافاصله به کاربر نشان داده می‌شد، در حالی که رندر سرور در حال
  تکمیل بود.

این تست‌ها همان دو قرارداد را می‌سنجند.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
TREES = [ROOT / "src", ROOT / "inventory" / "src"]


def service(tree: Path) -> Path:
    return tree / "Inventory.Api" / "Services" / "Office" / "Outgoing" / "OutgoingLetterPrintService.cs"


def workflow(tree: Path) -> Path:
    return tree / "Inventory.Client" / "Services" / "LetterPrintWorkflow.cs"


class ServerPrintCacheTests(unittest.TestCase):
    """خروجی چاپ باید پایدار کش شود تا «تلاش مجدد» فوری باشد."""

    def test_render_cache_lives_long_enough_for_a_retry(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = service(tree).read_text(encoding="utf-8")
                match = re.search(r"RenderedPdfLifetime = TimeSpan\.From(\w+)\((\d+)\)", source)
                self.assertIsNotNone(match)
                unit, value = match.group(1), int(match.group(2))
                seconds = value * {"Seconds": 1, "Minutes": 60, "Hours": 3600}[unit]
                self.assertGreaterEqual(seconds, 600, "کش باید دست‌کم ۱۰ دقیقه زنده بماند")

    def test_rendered_pdf_is_written_to_a_persistent_cache(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = service(tree).read_text(encoding="utf-8")
                self.assertIn("TryReadPrintCache(cacheKey)", source)
                self.assertIn("WritePrintCache(cacheKey, pdf)", source)
                # نوشتن اتمیک: خواننده نباید فایل نیمه‌نوشته ببیند.
                self.assertIn('var temporary = path + ".tmp";', source)
                self.assertIn("File.Move(temporary, path, overwrite: true);", source)
                # سقف تعداد فایل تا پوشهٔ موقت رشد نکند.
                self.assertIn("PrintCacheMaxFiles", source)
                self.assertIn("PrunePrintCache();", source)

    def test_disk_cache_hit_happens_before_any_rendering(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = service(tree).read_text(encoding="utf-8")
                hit = source.index("TryReadPrintCache(cacheKey)")
                render = source.index("RentRenderedPdf(cacheKey)")
                self.assertLess(hit, render, "کش دیسک باید پیش از شروع رندر بررسی شود")
                self.assertIn("Stage=print-cache-disk-hit", source)

    def test_slow_render_is_summarised_in_one_log_line(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = service(tree).read_text(encoding="utf-8")
                self.assertIn("Stage=print-complete", source)
                self.assertIn("OutgoingLetterPrintTiming SLOW", source)

    def test_template_lookup_is_memoised_within_the_request(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = service(tree).read_text(encoding="utf-8")
                # پوشه‌ها و XML قالب در یک درخواست چند بار خوانده می‌شد.
                self.assertIn("_requestTemplatePaths", source)
                self.assertIn("FindCompanyTemplateCore(companyId, size)", source)


class ClientAutoRetryTests(unittest.TestCase):
    """تایم‌اوت اول نباید به کاربر نشان داده شود؛ سرور در حال تکمیل رندر است."""

    def test_first_attempt_gets_more_than_ninety_seconds(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = workflow(tree).read_text(encoding="utf-8")
                match = re.search(r"FirstAttemptTimeout = TimeSpan\.FromSeconds\((\d+)\)", source)
                self.assertIsNotNone(match)
                self.assertGreater(int(match.group(1)), 90)

    def test_timeout_of_the_first_attempt_triggers_one_automatic_retry(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = workflow(tree).read_text(encoding="utf-8")
                self.assertIn("AutoRetryTimeout", source)
                self.assertIn("cancelledByBudget && attemptNumber == 1", source)
                self.assertIn("auto-retry-requested", source)
                # تلاش خودکار پیش از ساخت پیام خطا و پیش از نمایش آن انجام می‌شود.
                auto = source.index("cancelledByBudget && attemptNumber == 1")
                message = source.index('var message = cancelledByBudget')
                self.assertLess(auto, message)

    def test_auto_retry_uses_the_longest_budget(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                source = workflow(tree).read_text(encoding="utf-8")
                first = int(re.search(r"FirstAttemptTimeout = TimeSpan\.FromSeconds\((\d+)\)", source).group(1))
                auto = int(re.search(r"AutoRetryTimeout = TimeSpan\.FromSeconds\((\d+)\)", source).group(1))
                manual = int(re.search(r"(?<!Auto)RetryTimeout = TimeSpan\.FromSeconds\((\d+)\)", source).group(1))
                self.assertGreaterEqual(auto, first)
                self.assertGreater(auto, manual)


if __name__ == "__main__":
    unittest.main()
