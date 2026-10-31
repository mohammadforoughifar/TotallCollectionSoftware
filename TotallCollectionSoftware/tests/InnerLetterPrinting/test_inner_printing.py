"""Offline asset and source-contract checks, not a substitute for the .NET PDF render tests."""
import base64
import struct
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
API = ROOT / "src" / "Inventory.Api"

def jpeg_size(data: bytes) -> tuple[int, int]:
    """ابعاد JPEG از روی نشانگر SOF — بدون کتابخانهٔ بیرونی."""
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
CLIENT = ROOT / "src" / "Inventory.Client"
OFFICE = API / "Services" / "Office"


class InnerPrintingTests(unittest.TestCase):
    def test_company_1003_has_independent_a4_a5_artwork_only_templates(self):
        for size in ("A4", "A5"):
            with self.subTest(size=size):
                template = API / "Reports/InnerLetters/Companies/1003" / f"Inner-{size}.mrt"
                root = ET.parse(template).getroot()
                self.assertEqual(root.findtext("./Pages/Page1/PaperSize"), size)
                # قالب اصلی (ساخته‌شده با Stimulsoft) همهٔ کادرهای نامه را دارد:
                #   ۳ کادر شماره/تاریخ/پیوست + گیرنده + برچسب «موضوع:» + عنوان + متن + هامش
                # مختصات مطلق است چون حاشیهٔ صفحه صفر است.
                components = root.find("./Pages/Page1/Components")
                self.assertEqual(components.get("count"), str(len(list(components))))
                self.assertEqual(len(list(components)), 8)
                self.assertEqual(root.find("./Dictionary/Databases").get("count"), "0")
                self.assertEqual(root.find("./Dictionary/DataSources").get("count"), "0")
                value = root.findtext(".//Watermark/ImageBytes")
                image = base64.b64decode("".join(value.split()), validate=True)
                self.assertTrue(image.startswith(b"\xff\xd8") and image.endswith(b"\xff\xd9"))
                approved = ET.parse(API / "Reports/OutgoingLetters/Companies/1003" / f"Outgoing-{size}.mrt")
                original = base64.b64decode("".join(approved.findtext(".//Watermark/ImageBytes").split()))
                # سربرگ داخلی همان طرح تأییدشدهٔ سربرگ صادره است (بایت‌ها ممکن است دوباره فشرده
                # شده باشند، ولی تصویر یکی است). ابعاد یکی بودن را تضمین می‌کند.
                self.assertEqual(jpeg_size(image), jpeg_size(original),
                                 "سربرگ نامهٔ داخلی و صادره باید یک طرح باشند")
                self.assertNotIn("ConnectionString", template.read_text())

    # ── چیدمان نامهٔ داخلی ۱۰۰۳ = همان چیدمان نامهٔ صادره، بدون امضا ──
    # مرکز برچسب‌های چاپ‌شده روی تصویر سربرگ (اندازه‌گیری روی خودِ تصویر ۲۴۸۱×۳۵۰۸):
    # ۶۶٫۵ / ۸۴٫۵ / ۱۰۳٫۲pt در A4؛ تصویر A5 همان تصویر است و با نسبت ۰٫۷۰۷ کشیده می‌شود.
    LABEL_CENTERS = {
        "A4": {"LetterNumber": 66.5, "DateShams": 84.5, "Peyvast": 103.2},
        "A5": {"LetterNumber": 47.0, "DateShams": 59.7, "Peyvast": 72.9},
    }

    def test_meta_boxes_sit_on_the_printed_label_rows(self):
        """شماره/تاریخ/پیوست در قالب داخلی، دقیقاً مرکزِ همان برچسب‌های چاپ‌شده."""
        for size, slots in self.LABEL_CENTERS.items():
            root = ET.parse(API / "Reports/InnerLetters/Companies/1003" / f"Inner-{size}.mrt").getroot()
            page = root.find("./Pages/Page1")
            margins = [float(v) for v in (page.findtext("Margins") or "0,0,0,0").split(",")]
            self.assertEqual(margins[2], 0.0, "کادرها مطلق صفحه‌اند؛ حاشیهٔ بالا باید صفر بماند")
            found = {}
            for component in page.find("Components"):
                text = component.findtext("Text") or ""
                for slot in slots:
                    if slot in text:
                        rect = [float(v) for v in component.findtext("ClientRectangle").split(",")]
                        found[slot] = (rect[1] + rect[3] / 2.0) * 72.0  # مرکز عمودی، point
            self.assertEqual(len(found), 3, f"سه کادر شماره/تاریخ/پیوست لازم است: {size}")
            for slot, center in self.LABEL_CENTERS[size].items():
                self.assertAlmostEqual(found[slot], center, delta=1.5,
                                       msg=f"{slot} در {size} روی خط برچسب نیست")

    def test_page_direction_matches_the_outgoing_1003_layout(self):
        """چیدمان مثل نامهٔ صادرهٔ ۱۰۰۳ چپ‌به‌راست است.

        با راست‌به‌چپ، QuestPDF ترتیب فرزندان Row را آینه می‌کند: برچسب‌های
        «موضوع/ایجادکننده/تاریخ ارجاع» به لبهٔ چپ می‌رفتند و مقدارها به لبهٔ راست.
        """
        source = (OFFICE / "InnerLetterPdfRenderer.cs").read_text()
        self.assertIn("page.ContentFromLeftToRight();", source)
        self.assertNotIn("page.ContentFromRightToLeft();", source)
        outgoing = (OFFICE / "Outgoing" / "OutgoingLetterPrintService.cs").read_text()
        self.assertIn("page.ContentFromLeftToRight();", outgoing)

    def test_info_block_is_only_receiver_then_subject(self):
        """خط اول گیرنده، خط دوم «موضوع:»؛ ایجادکننده و تاریخ ارجاع چاپ نمی‌شوند."""
        source = (OFFICE / "InnerLetterPdfRenderer.cs").read_text()
        body = source.split("private static void AddHomepaBody", 1)[1].split("AddPlainBody", 1)[0]
        self.assertLess(body.index('"گیرندگان"'), body.index('"موضوع"'),
                        "گیرنده باید خط اول باشد و موضوع خط دوم")
        for removed in ('InfoRow("ایجادکننده"', 'InfoRow("تاریخ ارجاع"'):
            self.assertNotIn(removed, source, f"ردیف «{removed}» نباید در نامهٔ داخلی چاپ شود")
        # مسیر شرکت‌های دیگر هم تاریخ ارجاع چاپ نمی‌کند (فقط در مدل داده می‌ماند).
        self.assertNotIn('Text($"تاریخ ارجاع', source)

    # ── قالب اصلی Stimulsoft: تغییرات داخل .mrt است و چاپ هم از همان خوانده می‌شود ──────

    BLOCK_COMPONENTS = {
        "GirandeAsli": "گیرنده",
        "TitleLetter": "عنوان (موضوع)",
        "TextLetter": "متن نامه",
        "Hamesh": "هامش",
    }

    def inner_template(self, size: str, tree: Path | None = None):
        tree = tree or API
        return (tree / "Reports/InnerLetters/Companies/1003" / f"Inner-{size}.mrt")

    def test_stimulsoft_template_contains_the_whole_letter_block(self):
        """قالب اصلی (Stimulsoft) خودش کادرهای گیرنده/موضوع/متن/هامش را دارد."""
        for size in ("A4", "A5"):
            root = ET.parse(self.inner_template(size)).getroot()
            texts = {component.findtext("Name"): (component.findtext("Text") or "")
                     for component in root.find("./Pages/Page1/Components")}
            for token, label in self.BLOCK_COMPONENTS.items():
                self.assertTrue(any(token in text for text in texts.values()),
                                f"کادر «{label}» ({token}) در قالب {size} نیست")
            # برچسب «موضوع:» هم مثل نامهٔ صادره در قالب تعریف شده است.
            self.assertTrue(any("موضوع" in text for text in texts.values()),
                            f"برچسب «موضوع:» در قالب {size} نیست")
            # حذف‌شده‌ها نباید در قالب کادر داشته باشند.
            for removed in ("ReferralDates", "Creator", "تاریخ ارجاع", "ایجادکننده"):
                self.assertFalse(any(removed in text for text in texts.values()),
                                 f"«{removed}» نباید در قالب {size} باشد")

    def test_template_block_geometry_matches_the_printed_layout(self):
        """مختصات کادرهای بلوک اطلاعات در قالب = همان چیزی که چاپ می‌شود."""
        for size, gap, row in (("A4", 97.0, 23.05), ("A5", 97.0, 19.95)):
            root = ET.parse(self.inner_template(size)).getroot()
            boxes = {component.findtext("Name"): [float(v) for v in component.findtext("ClientRectangle").split(",")]
                     for component in root.find("./Pages/Page1/Components")}
            receiver_top = boxes["Text13"][1] * 72
            subject_top = boxes["Text15"][1] * 72
            body_top = boxes["Text16"][1] * 72
            band = 112.8 if size == "A4" else 79.4
            # شروع بلوک = پایین نوار آبی + ۹۷pt (همان عدد تأییدشدهٔ کارفرما)
            self.assertAlmostEqual(receiver_top, band + gap, delta=1.0,
                                   msg=f"گیرنده در {size} از پایین نوار آبی + ۹۷pt شروع نمی‌شود")
            # موضوع یک ردیف پایین‌تر و متن پایین‌تر از موضوع
            self.assertAlmostEqual(subject_top - receiver_top, row, delta=3.0)
            self.assertGreater(body_top, subject_top + 15.0)
            # عرض متن = عرض کاغذ منهای دو حاشیهٔ ۱۵ میلی‌متری
            page_width = {"A4": 595.28, "A5": 419.53}[size]
            self.assertAlmostEqual(boxes["Text16"][2] * 72, page_width - 2 * 42.52, delta=1.0)
            self.assertAlmostEqual((boxes["Text16"][0]) * 72, 42.52, delta=1.0)

    def test_renderer_reads_block_geometry_from_the_template(self):
        """رندر نامهٔ داخلی شروع بلوک و عرض/فاصلهٔ برچسب را از طرح می‌خواند (نه اعداد ثابت)."""
        source = (OFFICE / "InnerLetterPdfRenderer.cs").read_text()
        self.assertIn("meta?.Receiver?.Top", source)
        self.assertIn("meta?.Title?.Top", source)
        self.assertIn("designTop > 0f ? designTop : HomepaLetterheadLayout.ContentTop(a5)", source)
        self.assertIn("OutgoingLetterDesignLayout.Slot.Caption", source)
        self.assertIn("captionBox.Left - (titleBox.Left + titleBox.Width)", source)
        self.assertIn("InfoBlock(c, rows, infoFontSize", source)
        self.assertIn("labelGap, labelWidth)", source)

    def test_shared_info_block_accepts_the_template_label_width(self):
        shared = (OFFICE / "LetterPrintTemplate.cs").read_text()
        self.assertIn("float? fixedLabelWidth = null", shared)
        self.assertIn("var labelWidth = fixedLabelWidth is > 0f", shared)
        # نامهٔ صادره بدون تغییر: پارامتر اختیاری است و فراخوان فعلی دست‌نخورده می‌ماند.
        outgoing = (OFFICE / "Outgoing" / "OutgoingLetterPrintService.cs").read_text()
        self.assertIn("LetterPrintTemplate.InfoBlock(col, infoRows, infoFontSize, infoSpacing,", outgoing)

    def test_hamesh_is_printed_after_the_letter_text(self):
        source = (OFFICE / "InnerLetterPdfRenderer.cs").read_text()
        body = source.split("private static void AddHomepaBody", 1)[1].split("AddPlainBody", 1)[0]
        text_at = body.index("c.Item().Text(body)")
        hamesh_at = body.index("AddHamesh(c, data, withHamesh);")
        self.assertLess(text_at, hamesh_at, "هامش باید بعد از متن نامه بیاید")

    def test_meta_values_are_placed_next_to_the_printed_labels(self):
        """لبهٔ راستِ مقدارها ۴pt قبل از برچسب‌های چاپ‌شده و روی مرکز همان ردیف‌ها."""
        source = (OFFICE / "InnerLetterPdfRenderer.cs").read_text()
        self.assertIn("rows.Min(row => row.Left + row.Width) - 4f", source)
        self.assertIn(".PaddingLeft(Math.Max(0f, left))", source)
        # لبهٔ راستِ خودِ کادر ۴pt جلوتر از لبهٔ راستِ مقدار است (رندر ۴pt کم می‌کند).
        for size, centers, box_right in (("A4", (66.5, 84.5, 103.2), 96.48),
                                         ("A5", (47.0, 59.8, 73.0), 66.24)):
            root = ET.parse(API / "Reports/InnerLetters/Companies/1003" / f"Inner-{size}.mrt").getroot()
            # فقط سه کادر سربرگ (شماره/تاریخ/پیوست)؛ بقیهٔ کادرها مربوط به بلوک اطلاعات‌اند.
            components = [component for component in root.find("./Pages/Page1/Components")
                          if any(token in (component.findtext("Text") or "")
                                 for token in ("LetterNumber", "DateShams", "Peyvast"))]
            self.assertEqual(len(components), 3)
            for component, center in zip(components, centers):
                rect = [float(v) for v in component.findtext("ClientRectangle").split(",")]
                self.assertAlmostEqual((rect[1] + rect[3] / 2) * 72.0, center, delta=2.0)
                self.assertAlmostEqual((rect[0] + rect[2]) * 72.0, box_right, delta=2.0)

    def test_inner_template_keeps_the_outgoing_header_box_geometry(self):
        """هندسهٔ کادرهای سربرگ نامهٔ داخلی = همان کادرهای قالب نامهٔ صادرهٔ ۱۰۰۳."""
        for size in ("A4", "A5"):
            inner = ET.parse(API / "Reports/InnerLetters/Companies/1003" / f"Inner-{size}.mrt").getroot()
            outgoing = ET.parse(API / "Reports/OutgoingLetters/Companies/1003" / f"Outgoing-{size}.mrt").getroot()
            # قالب نامهٔ صادره حاشیهٔ صفحه دارد و مستطیل‌ها نسبت به آن‌اند؛ قالب داخلی
            # حاشیهٔ صفر دارد و مستطیل‌ها مطلق‌اند ⇒ بعد از جمع با حاشیه باید یکی شوند.
            margin = [float(v) for v in (outgoing.findtext("./Pages/Page1/Margins") or "0,0,0,0").split(",")]
            inner_rects = sorted(tuple(round(float(v), 4) for v in c.findtext("ClientRectangle").split(","))
                                 for c in inner.find("./Pages/Page1/Components")
                                 if any(token in (c.findtext("Text") or "")
                                        for token in ("LetterNumber", "DateShams", "Peyvast")))
            outgoing_rects = sorted(
                (round(float((c.findtext("ClientRectangle") or "").split(",")[0]) + margin[0], 4),
                 round(float((c.findtext("ClientRectangle") or "").split(",")[1]) + margin[2], 4),
                 round(float((c.findtext("ClientRectangle") or "").split(",")[2]), 4),
                 round(float((c.findtext("ClientRectangle") or "").split(",")[3]), 4))
                for c in outgoing.find("./Pages/Page1/Components")
                if (c.findtext("Text") or "").strip() in
                ("{DataSource1.LetterNumber}", "{DataSource1.DateShams}", "{DataSource1.Peyvast}"))
            self.assertEqual(inner_rects, outgoing_rects, f"کادرهای سربرگ {size} باید یکی باشند")

    def test_renderer_uses_absolute_foreground_and_starts_body_below_the_band(self):
        """مقادیر سربرگ در لایهٔ Foreground مطلق؛ متن از پایین نوار آبی + ۹۷pt شروع می‌شود."""
        source = (OFFICE / "InnerLetterPdfRenderer.cs").read_text()
        self.assertIn("page.MarginTop(0);", source)
        # مقادیر سربرگ در لایهٔ سراسری صفحه (مختصات مطلق) و با جهت چپ‌به‌راست،
        # وگرنه جهت راست‌به‌چپِ صفحه ترتیب Row را آینه می‌کرد و مقادیر از برچسب‌ها دور می‌افتادند.
        self.assertIn("page.Foreground().ContentFromLeftToRight()", source)
        self.assertIn("DrawMetaRows(host, metaRows)", source)
        self.assertIn("HomepaLetterheadLayout.ContentTop(a5)", source)
        self.assertIn("page.Header().Height(contentTop);", source)
        # کادرها با مختصات مطلق‌اند؛ کسر مبدأ page.Header همان ایراد «یک ردیف بالاتر» بود.
        self.assertNotIn("HeaderField(", source)
        self.assertNotIn("HomepaLetterheadLayout.Number(a5)", source)

    def test_inner_homepa_body_is_the_outgoing_layout_without_signatures(self):
        source = (OFFICE / "InnerLetterPdfRenderer.cs").read_text()
        self.assertIn("LetterPrintTemplate.InfoBlock(c, rows, infoFontSize", source)
        self.assertIn('new LetterPrintTemplate.InfoRow("موضوع"', source)
        self.assertIn("HideLabel: true", source)
        self.assertIn("LetterPrintTemplate.Spacer(c, a5 ? 10f : 12f);", source)
        self.assertNotIn("Separator(c)", source.split("AddHomepaBody", 1)[1].split("AddPlainBody", 1)[0],
                         "خط جداکنندهٔ خاکستری در مسیر ۱۰۰۳ برداشته شده است")
        # هیچ بخش امضا/مهر/عبارت پایانی‌ای ساخته نمی‌شود.
        for generated in ('Text("با تشکر")', 'Text("مهر")', 'Signer(', "SignatureImage"):
            self.assertNotIn(generated, source)

    # ── مسیر قالب: چاپ باید از فایل داخل مخزن (inventory/src/…) خوانده شود ──────────────

    def test_print_reads_the_repository_inventory_template_first(self):
        """اولویت مسیرها: تنظیمات → فایل داخل مخزن → ContentRoot → BaseDirectory."""
        source = (OFFICE / "InnerLetterheadTemplates.cs").read_text()
        candidates = source.split("CompanyCandidates(string contentRoot", 1)[1].split("return list.Distinct", 1)[0]
        order = [
            candidates.index("AddFolder(ConfiguredFolder("),
            candidates.index("AddFolder(RepositoryCompanyFolder("),
            candidates.index('Path.Combine(contentRoot, "Reports"'),
            candidates.index('Path.Combine(baseDirectory, "Reports"'),
        ]
        self.assertEqual(order, sorted(order), "ترتیب اولویت مسیرهای قالب به‌هم خورده است")
        # پوشهٔ داخل مخزن دقیقاً همان مسیری است که کارفرما گفته است.
        self.assertIn('"inventory", "src", "Inventory.Api"', source)
        self.assertIn('"Reports", "InnerLetters", "Companies", company', source)

    def test_repository_template_is_discoverable_from_a_build_output_folder(self):
        """از پوشهٔ خروجی بیلد (bin/Debug/...) با بالا رفتن، پوشهٔ قالب داخل مخزن پیدا می‌شود."""
        start = ROOT / "src" / "Inventory.Api" / "bin" / "Debug" / "net8.0"
        found = None
        for parent in [start, *start.parents][:12]:
            candidate = (parent / "inventory" / "src" / "Inventory.Api" / "Reports"
                         / "InnerLetters" / "Companies" / "1003")
            if candidate.is_dir():
                found = candidate
                break
        self.assertIsNotNone(found, "پوشهٔ قالب داخل مخزن باید از خروجی بیلد قابل discovery باشد")
        self.assertTrue((found / "Inner-A4.mrt").is_file())
        self.assertTrue((found / "Inner-A5.mrt").is_file())

    @staticmethod
    def repository_root() -> Path:
        """ریشهٔ مخزن، مستقل از این‌که تست از کدام درخت اجرا شود.

        پوشهٔ inventory خودش یک Inventory.sln دارد؛ پس معیار «هر دو درخت کنار هم» است.
        """
        for parent in Path(__file__).resolve().parents:
            if ((parent / "src" / "Inventory.Api").is_dir()
                    and (parent / "inventory" / "src" / "Inventory.Api").is_dir()):
                return parent
        return ROOT

    def test_both_source_trees_share_the_same_inner_template(self):
        """نسخهٔ src و نسخهٔ inventory باید یکی بمانند؛ وگرنه چاپ از یک نسخه و ویرایش روی دیگری است."""
        repo = self.repository_root()
        primary_dir = repo / "src" / "Inventory.Api"
        mirror_dir = repo / "inventory" / "src" / "Inventory.Api"
        self.assertNotEqual(primary_dir, mirror_dir)
        for size in ("A4", "A5"):
            with self.subTest(size=size):
                name = f"Inner-{size}.mrt"
                primary = primary_dir / "Reports" / "InnerLetters" / "Companies" / "1003" / name
                mirror = mirror_dir / "Reports" / "InnerLetters" / "Companies" / "1003" / name
                self.assertTrue(primary.is_file() and mirror.is_file(), f"فایل {name} در یکی از درخت‌ها نیست")
                self.assertEqual(primary.read_bytes(), mirror.read_bytes(),
                                 f"{name} در دو درخت یکی نیست")
        renderer = primary_dir / "Services" / "Office" / "InnerLetterPdfRenderer.cs"
        mirror_renderer = mirror_dir / "Services" / "Office" / "InnerLetterPdfRenderer.cs"
        self.assertEqual(renderer.read_bytes(), mirror_renderer.read_bytes(),
                         "رندر نامهٔ داخلی در دو درخت یکی نیست")

    def test_configured_template_root_wins_and_is_documented(self):
        service = (OFFICE / "InnerLetterPrintService.cs").read_text()
        self.assertIn('configuration?["Reporting:InnerLetters:TemplateRoot"]', service)
        settings = (API / "appsettings.json").read_text()
        self.assertIn('"TemplateRoot": ""', settings)
        self.assertIn('"LetterheadDpi": 200', settings)

    def test_print_log_names_the_template_file_that_was_read(self):
        service = (OFFICE / "InnerLetterPrintService.cs").read_text()
        self.assertIn("Template={Template}", service)
        self.assertIn("var templatePath =", service)
        # لاگ باید مسیر واقعیِ خوانده‌شده را ثبت کند، نه مسیر پیش‌فرض ContentRoot را.
        log = service.split("Template={Template}", 1)[1]
        self.assertIn("templatePath", log.split(");", 1)[0])
        self.assertNotIn("HomepaPath(_env.ContentRootPath, a5),\n                    loadMs", service)
    def test_service_reads_meta_geometry_from_the_inner_template(self):
        service = (OFFICE / "InnerLetterPrintService.cs").read_text()
        self.assertIn("MetaLayout(", service)
        self.assertIn("hasAttachment, fonts, meta)", service)

    def test_background_is_a_repeating_page_background_and_body_can_flow(self):
        source = (OFFICE / "InnerLetterPdfRenderer.cs").read_text()
        # تصویر سربرگ یک بار در InnerLetterheadTemplates آماده می‌شود؛ اینجا دست‌نخورده
        # در PDF گذاشته می‌شود تا هر چاپ یک بار دیگر resize/encode نشود.
        self.assertIn("page.Background().Image(homepaBackground!).UseOriginalImage().FitUnproportionally();", source)
        self.assertIn("page.Size(a5 ? PageSizes.A5 : PageSizes.A4)", source)
        self.assertIn("c.Item().Text(body).AlignRight();", source)
        self.assertNotIn(".ShowEntire(", source)
        self.assertNotIn(".ClampLines(", source)
        self.assertNotIn(".Footer()", source)
        for generated in ('Text("با تشکر")', 'Text("مهر")', 'Text("امضاکنندگان")', 'CurrentPageNumber()'):
            self.assertNotIn(generated, source)

    def test_hamesh_is_optional_end_to_end_and_excludes_private_answers(self):
        service = (OFFICE / "InnerLetterPrintService.cs").read_text()
        self.assertIn("if (withHamesh)", service)
        self.assertIn('x.Type == "هامش"', service)
        self.assertNotIn(".Answer", service)
        self.assertNotIn(".Take(", service)
        renderer = (OFFICE / "InnerLetterPdfRenderer.cs").read_text()
        # «با هامش» فقط ردیف‌های هامش را اضافه می‌کند (خروج زودهنگام در تابع مشترک).
        self.assertIn("if (!withHamesh || data.Hamesh.Count == 0) return;", renderer)
        self.assertIn("HomepaLetterheadLayout.AbsoluteAttachment(a5)", renderer)
        controller = (API / "Controllers/Office/InnerLettersController.cs").read_text()
        self.assertIn("[FromQuery] bool withHamesh = false", controller)
        self.assertIn("HttpContext.RequestAborted, withHamesh", controller)
        client = (CLIENT / "Services/LetterServices.cs").read_text()
        self.assertIn("&withHamesh=", client)
        self.assertIn("_api.GetFileAsync(path", client)

    def test_letter_and_company_access_are_preserved(self):
        service = (OFFICE / "InnerLetterPrintService.cs").read_text()
        self.assertIn("x.ReciverUserId == userId", service)
        self.assertIn("letter.CreatorUserId != userId", service)
        self.assertIn("ActiveCompanyMiddleware.ItemKey", service)
        self.assertIn("x.UserId == userId && x.Company.IsActive", service)
        transport = (CLIENT / "Services/ApiClient.cs").read_text()
        method = transport.split("public async Task<(byte[] Data, string FileName, string ContentType)> GetFileAsync", 1)[1]
        method = method.split("public async Task<T> PostFileAsync", 1)[0]
        self.assertIn("AddActiveCompanyHeaderAsync(req)", method)
        self.assertIn("AddAuth(req)", method)

    def test_homepa_fails_instead_of_using_another_company_background(self):
        service = (OFFICE / "InnerLetterPrintService.cs").read_text()
        # هر شرکتی که قالب داخلی دارد، فقط از قالب خودش چاپ می‌شود و در صورت خطا
        # به سربرگ شرکت دیگر برنمی‌گردد.
        self.assertIn("InnerLetterheadTemplates.", service)
        self.assertIn("throw new InnerLetterPrintException", service)
        # شاخهٔ سربرگ اختصاصی شرکت هرگز به سربرگ نمونهٔ شرکت دیگر برنمی‌گردد.
        # دو درخت دو نسخهٔ این سرویس دارند (۱۰۰۳ اختصاصی / هر شرکتی که قالب دارد)؛ هر دو
        # سربرگ را فقط از قالب خودِ شرکت می‌خوانند و در صورت خطا چاپ بی‌سربرگ نمی‌کنند.
        if "var companyArt =" in service:
            company_branch = service.split("var companyArt =", 1)[1].split("var header = company?", 1)[0]
            self.assertIn("InnerLetterheadTemplates.TryLoadForCompany(", company_branch)
        else:
            company_branch = service.split("if (company?.Id == 1003)", 1)[1].split("var header = company?", 1)[0]
            self.assertIn("InnerLetterheadTemplates.LoadHomepa(", company_branch)
        self.assertNotIn("forough", company_branch)
        self.assertIn("throw new InnerLetterPrintException", company_branch)
        loader = (OFFICE / "InnerLetterheadTemplates.cs").read_text()
        self.assertIn('"Reports", "InnerLetters", "Companies", "1003"', loader)
        self.assertIn('Inner-{(a5 ? "A5" : "A4")}.mrt', loader)

    def test_ui_asks_for_variant_after_either_size_click(self):
        ui = (CLIENT / "Pages/Office/Letters/LetterCartable.razor").read_text()
        # دکمه‌های چاپ: هر اندازه در دو نسخهٔ «با هامش» و «بدون هامش».
        self.assertIn("data-ltx-print-option", ui)
        self.assertIn("PrintSelected(SizeA4, false)", ui)
        self.assertIn("PrintSelected(SizeA5, true)", ui)
        self.assertIn("PrintRow(ll, SizeA4, false)", ui)
        self.assertIn("PrintRow(ll, SizeA5, true)", ui)
        self.assertIn("بدون هامش", ui)
        self.assertIn("با هامش", ui)
        method = ui.split("private async Task PrintPdf(string size, bool withHamesh)", 1)[1]
        self.assertIn("GetPrintPdfAsync(letterId, size, withHamesh, ct)", method)
        self.assertIn("PrintWorkflow.PrintPdfAsync(", method)

    def test_publish_rule_includes_inner_templates(self):
        project = ET.parse(API / "Inventory.Api.csproj")
        rule = next(c for c in project.iter("Content") if c.get("Include") == "Reports/**/*.mrt")
        self.assertEqual(rule.get("CopyToOutputDirectory"), "PreserveNewest")
        self.assertEqual(rule.get("CopyToPublishDirectory"), "PreserveNewest")


if __name__ == "__main__":
    unittest.main()
