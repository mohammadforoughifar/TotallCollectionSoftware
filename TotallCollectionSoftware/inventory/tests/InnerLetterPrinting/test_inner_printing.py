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
                # سه کادر شماره/تاریخ/پیوست مثل قالب صادرهٔ ۱۰۰۳ (مختصات مطلق، چون حاشیهٔ
                # صفحه صفر است) تا مقادیر روی همان خط برچسب‌های چاپ‌شده بنشینند.
                self.assertEqual(root.find("./Pages/Page1/Components").get("count"), "3")
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
            components = list(root.find("./Pages/Page1/Components"))
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
                                 for c in inner.find("./Pages/Page1/Components"))
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
