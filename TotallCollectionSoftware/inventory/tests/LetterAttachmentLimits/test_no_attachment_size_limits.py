"""نگهبان «نبود سقف حجم پیوست» در نامهٔ داخلی، وارده و صادره.

چرا این تست هست: خواستهٔ کارفرما این بود که هنگام افزودن فایل به نامهٔ داخلی/وارده/صادره
هیچ محدودیت حجمی نباشد — نه سقف هر فایل و نه سقف مجموع پیوست‌های یک نامه. این محدودیت‌ها
در دو جا بود و اگر روزی از یک طرف برگردد، کاربر پیام «حداکثر ۲۰ مگابایت» می‌بیند در حالی
که از طرف دیگر برداشته شده است:

  • سرور: `Inventory.Api/Controllers/Office/{Incoming,Inner,Outgoing}LettersController.cs`
  • کلاینت: صفحات `.razor` نامه‌ها (مودال ثبت، کارتابل‌ها، راهنما)

همچنین سطح انتقال (Kestrel / FormOptions / IIS) باید نامحدود بماند؛ اگر کسی دوباره
`MultipartBodyLengthLimit` یا `MaxRequestBodySize` را عددی کند، خطای ۴۱۳ در آپلود فایل
بزرگ رخ می‌دهد بی‌آنکه پیام روشنی به کاربر برسد.

اجرا:
    python3 -m unittest discover -s inventory/tests/LetterAttachmentLimits -p 'test_*.py'
"""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
API_TREES = [ROOT / "inventory" / "src" / "Inventory.Api", ROOT / "src" / "Inventory.Api"]
CLIENT_TREES = [ROOT / "inventory" / "src" / "Inventory.Client", ROOT / "src" / "Inventory.Client"]

CONTROLLERS = (
    "IncomingLettersController.cs",
    "InnerLettersController.cs",
    "OutgoingLettersController.cs",
)

LETTER_PAGES = (
    "Pages/Office/Letters/Incoming/ComposeIncomingModal.razor",
    "Pages/Office/Letters/Incoming/IncomingLetterCartable.razor",
    "Pages/Office/Letters/Outgoing/ComposeOutgoingModal.razor",
    "Pages/Office/Letters/Outgoing/OutgoingLetterCartable.razor",
    "Pages/Office/Letters/ComposeLetterModal.razor",
    "Pages/Office/Letters/LetterCartable.razor",
    "Pages/Office/Letters/InnerLetterGuide.razor",
)

# نشانه‌های سقف حجم که نباید در مسیر پیوست نامه‌ها بمانند.
CAP_MARKERS = (
    "20 * 1024 * 1024",
    "100L * 1024 * 1024",
    "20L * 1024 * 1024",
    "100L * 1024 * 1024",
    "MaxFileBytes",
    "MaxTotalBytes",
    "const long MaxFile ",
    "OpenReadStream(20 * 1024 * 1024)",
    "۲۰ مگابایت",
    "۱۰۰ مگابایت",
)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class ServerHasNoAttachmentSizeLimit(unittest.TestCase):
    def test_controllers_have_no_size_cap(self):
        for tree in API_TREES:
            for name in CONTROLLERS:
                path = tree / "Controllers" / "Office" / name
                with self.subTest(file=str(path)):
                    self.assertTrue(path.exists(), f"فایل کنترلر پیدا نشد: {path}")
                    source = read(path)
                    for marker in ("20 * 1024 * 1024", "100L * 1024 * 1024"):
                        self.assertNotIn(marker, source, f"سقف حجم در {name} برگشته است: {marker}")

    def test_upload_endpoints_stay_open_but_reject_empty_files(self):
        for tree in API_TREES:
            incoming = read(tree / "Controllers" / "Office" / "IncomingLettersController.cs")
            inner = read(tree / "Controllers" / "Office" / "InnerLettersController.cs")
            outgoing = read(tree / "Controllers" / "Office" / "OutgoingLettersController.cs")
            with self.subTest(tree=tree.name):
                # آپلود بزرگ نباید به سقف پیش‌فرض Kestrel برخورد کند.
                self.assertGreaterEqual(incoming.count("[DisableRequestSizeLimit]"), 1)
                self.assertGreaterEqual(inner.count("[DisableRequestSizeLimit]"), 2)
                self.assertGreaterEqual(outgoing.count("[DisableRequestSizeLimit]"), 2)
                # فایل خالی همچنان باید رد شود — حذف سقف حجم یعنی حذف تنها اعتبارسنجی.
                for source in (incoming, inner, outgoing):
                    self.assertIn("file.Length <= 0", source)

    def test_transport_limits_remain_unlimited(self):
        for tree in API_TREES:
            program = read(tree / "Program.cs")
            with self.subTest(tree=tree.name):
                self.assertIn("MultipartBodyLengthLimit = long.MaxValue", program)
                self.assertIn("MaxRequestBodySize = null", program)


class ClientHasNoAttachmentSizeLimit(unittest.TestCase):
    def test_letter_pages_have_no_size_cap(self):
        for tree in CLIENT_TREES:
            for rel in LETTER_PAGES:
                path = tree / rel
                with self.subTest(file=str(path)):
                    self.assertTrue(path.exists(), f"صفحه پیدا نشد: {path}")
                    source = read(path)
                    for marker in CAP_MARKERS:
                        self.assertNotIn(marker, source, f"سقف حجم در {rel} مانده/برگشته است: {marker}")

    def test_email_graph_is_closed_without_caps(self):
        """انتقال پیوست‌های ایمیل به نامهٔ وارده هم باید بی‌سقف باشد."""
        for tree in CLIENT_TREES:
            path = tree / "Pages/Office/Letters/Incoming/ComposeIncomingModal.razor"
            source = read(path)
            with self.subTest(file=str(path)):
                self.assertIn("EmailAttachmentTransferService.CollectAsync(", source)
                self.assertIn("long.MaxValue, long.MaxValue)", source)

    def test_empty_file_still_rejected_on_client(self):
        for tree in CLIENT_TREES:
            for rel in (
                "Pages/Office/Letters/Incoming/IncomingLetterCartable.razor",
                "Pages/Office/Letters/Outgoing/OutgoingLetterCartable.razor",
                "Pages/Office/Letters/LetterCartable.razor",
            ):
                source = read(tree / rel)
                with self.subTest(file=str(tree / rel)):
                    self.assertIn("Size <= 0", source)


if __name__ == "__main__":
    unittest.main()
