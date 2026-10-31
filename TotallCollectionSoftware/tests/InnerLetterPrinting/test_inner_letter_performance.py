"""کارایی چاپ نامهٔ داخلی — «چرا لود/چاپ نامهٔ داخلی ۱۰۰۳ سنگین بود».

قالب سربرگ هر شرکت یک فایل ~۶۲۰KB است که تصویر ۲۴۸۱×۳۵۰۸ (۴۵۶KB، ۳۰۰dpi) را به‌صورت Base64
در خود دارد. پیش از این:

1. در **هر بار چاپ**، فایل قالب دو بار خوانده و از نو XML-parse می‌شد
   (`LoadHomepa`/`TryLoadForCompany` + `…MetaLayout`) و تصویر هم هر بار Base64-decode می‌شد.
2. همان تصویر ۸٫۷ مگاپیکسلی به QuestPDF داده می‌شد و QuestPDF با تنظیم پیش‌فرض خود
   (`DocumentSettings.ImageRasterDpi = 288`) آن را به ۲۳۸۱×۳۳۶۷ تغییر اندازه و از نو JPEG
   می‌کرد (اندازه‌گیری روی همین تصویر: ~۴۲۰ms + ده‌ها مگابایت حافظهٔ موقت، در هر چاپ).
3. حجم PDF و حافظهٔ لازم برای نمایش و چاپ در مرورگر هم به همان اندازهٔ تصویر ۳۰۰dpi بود.

این تست‌ها «قرارداد» کد را می‌سنجند (سندباکس dotnet ندارد): کش نسخه‌دار، آماده‌سازی یک‌بارهٔ
تصویر، و اینکه QuestPDF دیگر در هر چاپ تصویر را دست‌کاری نکند.
"""

from __future__ import annotations

import base64
import struct
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
API = ROOT / "src" / "Inventory.Api"
OFFICE = API / "Services" / "Office"
TEMPLATE_DIR = API / "Reports" / "InnerLetters" / "Companies" / "1003"


def jpeg_size(data: bytes) -> tuple[int, int]:
    index = 2
    while index < len(data) - 9:
        if data[index] != 0xFF:
            index += 1
            continue
        marker = data[index + 1]
        if marker in (0xC0, 0xC1, 0xC2):
            height, width = struct.unpack(">HH", data[index + 5:index + 9])
            return width, height
        index += 2 + struct.unpack(">H", data[index + 2:index + 4])[0]
    return (0, 0)


class InnerLetterPerformanceTests(unittest.TestCase):
    def loader_source(self) -> str:
        return (OFFICE / "InnerLetterheadTemplates.cs").read_text(encoding="utf-8")

    def renderer_source(self) -> str:
        return (OFFICE / "InnerLetterPdfRenderer.cs").read_text(encoding="utf-8")

    def service_source(self) -> str:
        return (OFFICE / "InnerLetterPrintService.cs").read_text(encoding="utf-8")

    def test_the_template_artwork_is_really_a_300dpi_full_page_image(self):
        """پیش‌فرض‌ها را مستند می‌کند: منبع ۳۰۰dpi است و همین منبع، سنگینی را توضیح می‌دهد."""
        root = ET.parse(TEMPLATE_DIR / "Inner-A4.mrt").getroot()
        encoded = "".join(root.findtext(".//Watermark/ImageBytes").split())
        image = base64.b64decode(encoded)
        width, height = jpeg_size(image)
        self.assertEqual((width, height), (2481, 3508))
        self.assertAlmostEqual(width / 8.27, 300, delta=3)  # ≈۳۰۰dpi روی A4
        self.assertGreater(len(image), 400_000, "تصویر منبع چند صد کیلوبایت است")

    def test_artwork_and_geometry_are_cached_per_file_version(self):
        """فایل قالب یک بار در عمر پروسه خوانده می‌شود، نه در هر چاپ."""
        source = self.loader_source()
        self.assertIn("ConcurrentDictionary<string, ArtworkEntry>", source)
        self.assertIn("ConcurrentDictionary<string, LayoutEntry>", source)
        # اعتبارسنجی نسخه: اندازه و زمان تغییر فایل ⇒ تعویض قالب روی سرور خودکار دیده می‌شود.
        self.assertIn("cached.Length == info.Length && cached.WriteUtc == info.LastWriteTimeUtc", source)
        self.assertIn("info.LastWriteTimeUtc", source)
        # پارس XML و Base64 فقط داخل مسیر کش‌شده انجام می‌شود.
        self.assertEqual(source.count("XDocument.Load("), 1)
        self.assertEqual(source.count("Convert.FromBase64String("), 1)

    def test_artwork_is_prepared_once_at_a_print_ready_dpi(self):
        """یک بار کوچک‌کردن با Skia و کش نتیجه؛ نه resize/encode در هر چاپ."""
        source = self.loader_source()
        self.assertIn("internal const int DefaultArtworkDpi = 200;", source)
        self.assertIn("SKBitmap.Decode(encoded)", source)
        self.assertIn("bitmap.Resize(new SKImageInfo(targetWidth, targetHeight), SKFilterQuality.High)", source)
        self.assertIn("image.Encode(SKEncodedImageFormat.Jpeg, ArtworkJpegQuality)", source)
        # اگر تصویر از قبل کوچک باشد یا Skia در دسترس نباشد، تصویر اصلی چاپ می‌شود.
        self.assertIn("if (bitmap.Width <= targetWidth + 8) return encoded;", source)
        self.assertIn("catch\n        {\n            return encoded;", source)

    def test_questpdf_embeds_the_prepared_image_without_resizing_it_again(self):
        """بدون UseOriginalImage، QuestPDF در هر چاپ تصویر را به ۲۸۸dpi تغییر اندازه و کدگذاری می‌کرد."""
        source = self.renderer_source()
        self.assertIn("page.Background().Image(homepaBackground!).UseOriginalImage().FitUnproportionally();", source)
        self.assertNotIn(".WithRasterDpi(", source, "کوچک‌سازی باید یک بار در لودر انجام شود، نه در هر چاپ")

    def test_dpi_is_configurable_and_print_timings_are_logged(self):
        service = self.service_source()
        self.assertIn('"Reporting:InnerLetters:LetterheadDpi"', service)
        self.assertIn("InnerLetterheadTemplates.DefaultArtworkDpi", service)
        # زمان‌سنجی: کاربر/پشتیبان باید ببیند وقت کجا می‌رود.
        for field in ("TemplateMs={TemplateMs}", "RenderMs={RenderMs}", "PdfKB={PdfKB}", "ArtKB={ArtKB}"):
            self.assertIn(field, service)

    def test_second_print_does_not_reread_the_620kb_template(self):
        """مسیر چاپ هیچ‌جا خودش فایل قالب را نمی‌خواند؛ همه‌چیز از لودر کش‌شده می‌آید."""
        service = self.service_source()
        self.assertNotIn("XDocument", service)
        self.assertNotIn("FromBase64String", service)
        self.assertIn("InnerLetterheadTemplates.", service)
        # یک بار خواندن تصویر و یک بار خواندن هندسه، هر دو از کش.
        self.assertEqual(service.count("TryLoadForCompany(") + service.count("LoadHomepa("), 1)
        self.assertIn("MetaLayout(", service)


if __name__ == "__main__":
    unittest.main()
