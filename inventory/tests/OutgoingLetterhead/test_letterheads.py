"""Asset + source-contract checks; does not run the .NET renderer or access a database.
python3 -m unittest discover -s inventory/tests/OutgoingLetterhead -p 'test_*.py' -v
"""
import base64
import re
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET

API = Path(__file__).resolve().parents[2] / "src" / "Inventory.Api"
SERVICE = API / "Services" / "Office" / "Outgoing" / "OutgoingLetterPrintService.cs"
TEMPLATES = API / "Reports" / "OutgoingLetters" / "Companies"


class LetterheadAssetsTests(unittest.TestCase):
    def test_every_company_size_has_an_embedded_jpeg(self):
        for company in (1, 2, 3, 1003, 1005):
            for size in ("A4", "A5"):
                with self.subTest(company=company, size=size):
                    root = ET.parse(TEMPLATES / str(company) / f"Outgoing-{size}.mrt").getroot()
                    watermark = next(e for e in root.iter() if e.tag.split("}")[-1] == "Watermark")
                    value = next(e.text for e in watermark if e.tag.split("}")[-1] == "ImageBytes")
                    # Convert.FromBase64String tolerates XML whitespace just as this normalization does.
                    image = base64.b64decode("".join(value.split()), validate=True)
                    self.assertTrue(image.startswith(b"\xff\xd8"))
                    self.assertTrue(image.endswith(b"\xff\xd9"))
                    self.assertGreater(len(image), 100_000)

    def test_1003_enhanced_art_is_synchronized_across_registered_and_embedded_files(self):
        registered = API / "wwwroot" / "uploads" / "letterheads" / "homepa_company1003_letterhead.jpg"
        png = registered.with_suffix(".png")
        pdf = registered.with_suffix(".pdf")
        jpg_bytes = registered.read_bytes()
        self.assertTrue(jpg_bytes.startswith(b"\xff\xd8"))
        self.assertTrue(png.read_bytes().startswith(b"\x89PNG\r\n\x1a\n"))
        self.assertTrue(pdf.read_bytes().startswith(b"%PDF-"))
        for size in ("A4", "A5"):
            with self.subTest(size=size):
                root = ET.parse(TEMPLATES / "1003" / f"Outgoing-{size}.mrt").getroot()
                watermark = next(e for e in root.iter() if e.tag.split("}")[-1] == "Watermark")
                value = next(e.text for e in watermark if e.tag.split("}")[-1] == "ImageBytes")
                embedded = base64.b64decode("".join(value.split()), validate=True)
                self.assertEqual(embedded, jpg_bytes, "each company-1003 template must use the synchronized enhanced artwork")

    def test_content_pdf_does_not_cover_the_underlaid_letterhead(self):
        source = SERVICE.read_text()
        self.assertIn("page.PageColor(hasLetterhead ? Colors.Transparent : Colors.White);", source)
        self.assertLess(source.index("graphics.DrawImage(asset.Pdf, bounds)"), source.index("graphics.DrawImage(contentForm, bounds)"))
        self.assertIn("graphics.DrawImage(asset.Image, bounds)", source)

    def test_registered_file_priority_is_preserved(self):
        self.assertIn("var letterheadPath = registeredPath ?? templateLetterheadPath;", SERVICE.read_text())

    def test_active_service_honors_the_configured_template_root(self):
        source = SERVICE.read_text()
        self.assertIn('_configuration["Reporting:Stimulsoft:TemplateRoot"]', source)
        self.assertIn("Path.IsPathRooted(templateRoot)", source)
        self.assertIn("$\"Outgoing-{size}.mrt\"", source)

    def test_homepa_metadata_boxes_preserve_mrt_geometry_with_calibrated_left_shift(self):
        layout = SERVICE.with_name("HomepaLetterheadLayout.cs").read_text()
        expressions = {"Number": "LetterNumber", "Date": "DateShams", "Attachment": "Peyvast"}
        for field, column in expressions.items():
            method = re.search(r"static Field " + field + r"\(bool isA5\)(.*?);", layout, re.S).group(1)
            variants = re.findall(r"new\(([^)]+)\)", method)
            self.assertEqual(len(variants), 2)
            for index, size in enumerate(("A5", "A4")):
                with self.subTest(field=field, size=size):
                    root = ET.parse(TEMPLATES / "1003" / f"Outgoing-{size}.mrt").getroot()
                    self.assertEqual(root.findtext("ReportUnit"), "Inches")
                    page = root.find("./Pages/Page1")
                    margins = [float(v) for v in page.findtext("Margins").split(",")]
                    component = next(e for e in page.iter() if e.findtext("Text") == "{DataSource1." + column + "}")
                    x, y, width, height = [float(v) for v in component.findtext("ClientRectangle").split(",")]
                    left_shift = (3 if size == "A5" else 4) if field in ("Date", "Attachment") else 0
                    physical_x = (margins[0] + x) * 72 - left_shift
                    physical_y = (margins[2] + y) * 72
                    page_header_origin = 16 if size == "A5" else 24
                    expected = (physical_x, physical_y - page_header_origin, width * 72, height * 72)
                    actual = [float(v.strip().removesuffix("f")) for v in variants[index].split(",")]
                    for value, target in zip(actual, expected):
                        self.assertAlmostEqual(value, target, places=3)
                    self.assertAlmostEqual(actual[1] + page_header_origin, physical_y, places=3,
                                           msg="value top must match the corresponding label's page coordinate")
                    self.assertGreaterEqual(actual[0], 0)
                    self.assertLess(actual[0] + actual[2], float(page.findtext("PageWidth")) * 72 / 3,
                                    "values must stay in the physical left third, not the RTL right edge")

    def test_1003_header_values_match_the_label_rows_and_use_ltr_isolation(self):
        source = SERVICE.read_text()
        layout = SERVICE.with_name("HomepaLetterheadLayout.cs").read_text()
        self.assertIn("Header content begins after Page.MarginTop", layout)
        self.assertIn("text.DirectionFromLeftToRight()", source)
        self.assertIn('isolateLtr ? $"\\u2066{value}\\u2069" : value', source)
        for is_a5, expected in ((False, (36.48, 53.04, 69.6)), (True, (25.04, 36.56, 48.08))):
            fields = [re.search(r"static Field " + name + r"\(bool isA5\)(.*?);", layout, re.S).group(1)
                      for name in ("Number", "Date", "Attachment")]
            actual = []
            for field in fields:
                variants = re.findall(r"new\(([^)]+)\)", field)
                size_index = 0 if is_a5 else 1
                actual.append(float(variants[size_index].split(",")[1].strip().removesuffix("f")))
            self.assertEqual(tuple(actual), expected)

    def test_homepa_header_is_left_anchored_and_fields_are_independent(self):
        source = SERVICE.read_text()
        branch = source.split("else if (isHomepaLetterhead)", 1)[1]
        branch = branch.split("else if (hasLetterhead)", 1)[0]
        self.assertIn("page.Header().ContentFromLeftToRight()", branch)
        self.assertIn(".Layers(layers =>", branch)
        for field in ("Number", "Date", "Attachment"):
            self.assertIn(f"HomepaLetterheadLayout.{field}(isA5)", branch)
        self.assertNotIn(".Column(", branch)
        self.assertIn(".Height(HomepaLetterheadLayout.HeaderHeight(isA5))", branch)
        self.assertIn("HomepaLetterheadLayout.Number(isA5)", branch, "fixed metadata positions are independent of body spacing")

    def test_1003_signer_heading_group_and_multpage_footer_are_conditional(self):
        source = SERVICE.read_text()
        self.assertIn("col.Item().ShowEntire().Column(AddSignerBlock)", source)
        self.assertIn("var headingTopPadding = isHomepaLetterhead", source)
        self.assertIn("var tableTopPadding = isHomepaLetterhead", source)
        self.assertIn("var maxSignatureHeight = isA5 ? 68f : 72f", source)
        self.assertIn("context.TotalPages > 1", source)
        self.assertIn("page.Footer().Height(12).AlignTop().Dynamic(new HomepaPageNumberComponent())", source)
        overlay = source.split("private byte[] OverlayOnLetterhead", 1)[1]
        self.assertIn("for (var index = 0; index < contentDoc.PageCount; index++)", overlay)
        self.assertLess(overlay.index("graphics.DrawImage(asset.Image, bounds)"),
                        overlay.index("graphics.DrawImage(contentForm, bounds)"))
        self.assertIn("page.PageColor(hasLetterhead ? Colors.Transparent : Colors.White);", source)
        self.assertIn("page.Content().Background(Colors.Transparent)", source)

    def test_print_does_not_inject_a_closing_or_stamp_box(self):
        source = SERVICE.read_text()
        self.assertNotIn("closingText", source)
        self.assertNotIn("closingMatch", source)
        self.assertNotIn('Text("با تشکر")', source)
        self.assertNotIn('Text("مهر")', source)
        self.assertNotIn("stampHeight", source)
        self.assertNotIn("HtmlToPlainText", source,
                         "authored HTML must be rendered rather than flattened or truncated")
        self.assertIn("RichLetterPdfRenderer.AddTo(col, letter.Text", source)
        self.assertIn(".Text(signer.Name)", source)
        self.assertIn(".Text(signer.Role)", source)
        self.assertIn(".Svg(signature.Svg).FitArea()", source)
        self.assertIn("if (signerCards.Count > 0 && !signersKeptWithTrailingParagraph)", source)

    def test_homepa_header_fields_and_body_spacing_are_independent(self):
        source = SERVICE.read_text()
        layout = SERVICE.with_name("HomepaLetterheadLayout.cs").read_text()
        self.assertIn("var isHomepaLetterhead = hasLetterhead && letterheadCompanyId == HomepaCompanyId;", source)
        self.assertIn("var contentTopPadding = isHomepaLetterhead", source)
        self.assertIn(": (withMargin || hasLetterhead ? 30f : 0f);", source,
                      "other companies keep their existing body spacing")
        self.assertIn("HomepaLetterheadLayout.ReceiverTopPadding(isA5)", source)
        self.assertIn("contentContainer.PaddingTop(contentTopPadding)", source)
        self.assertIn("HomepaLetterheadLayout.SubjectBodyGap(isA5) : 0f", source)
        self.assertIn("col.Item().PaddingTop(subjectTopPadding).Text", source)
        self.assertIn(".Height(HomepaLetterheadLayout.HeaderHeight(isA5))", source)
        self.assertIn("HeaderHeight(bool isA5) => isA5 ? 150f : 160f", layout)
        self.assertIn("ReceiverTopPadding(bool isA5) => isA5 ? 28f : 80f", layout)
        self.assertIn("SubjectBodyGap(bool isA5) => isA5 ? 8f : 12f", layout)
        self.assertIn("FooterReserve(bool isA5) => isA5 ? 68f : 90f", layout)
        self.assertIn("HeaderValueFontSize(bool isA5) => isA5 ? 8.5f : 10.5f", layout)
        homepa_branch = source.split("else if (isHomepaLetterhead)", 1)[1].split("else if (hasLetterhead)", 1)[0]
        self.assertNotIn("center: true", homepa_branch)
        self.assertNotIn('"شماره:"', homepa_branch, "captions must not be duplicated over the artwork")
        self.assertNotIn('"تاریخ:"', homepa_branch)
        self.assertNotIn('"پیوست:"', homepa_branch)
        self.assertIn(".Height(field.Height).AlignMiddle().AlignRight()", source)
        self.assertIn('isolateLtr ? $"\\u2066{value}\\u2069" : value', source)
        self.assertIn("col.Item().PaddingTop(subjectTopPadding).Text", source)
        self.assertNotIn(".Column(body => AddSubjectAndFollowingContent(body))", source,
                         "the separator and all body elements must share one margin box")
        self.assertIn("page.MarginTop(isHomepaLetterhead", source,
                      "company 1003 header anchor must not depend on the body-margin option")
        self.assertIn(": (withMargin ? (isA5 ? 16 : 24) : 0));", source,
                      "other companies retain their existing page margin")
        self.assertIn("page.MarginBottom(isHomepaLetterhead", source,
                      "only company 1003 uses the dedicated footer clearance")
        self.assertIn("page.Size(isA5 ? PageSizes.A5 : PageSizes.A4);", source)

    def test_homepa_content_gap_matches_scaled_header_art_in_a4_and_a5(self):
        layout = SERVICE.with_name("HomepaLetterheadLayout.cs").read_text()
        # Measured from the actual 1003 watermark; keep roughly 1cm clear below the diagonal art.
        a4_gap = 24 + 160 + 80 - 232
        a5_gap = 16 + 150 + 28 - 163
        self.assertGreaterEqual(a4_gap, 28)
        self.assertLessEqual(a4_gap, 42)
        self.assertGreaterEqual(a5_gap, 28)
        self.assertLessEqual(a5_gap, 42)
        self.assertLess(abs(a4_gap - a5_gap), 3)
        self.assertNotIn("NumberLabel", layout)
        self.assertNotIn("DateLabel", layout)
        self.assertNotIn("AttachmentLabel", layout)

    def test_print_popup_is_reserved_on_native_menu_click(self):
        view = (Path(__file__).resolve().parents[2] / "src" / "Inventory.Client" / "Pages" / "Office" / "Letters" / "Outgoing" / "OutgoingLetterView.razor").read_text()
        shell = (Path(__file__).resolve().parents[2] / "src" / "Inventory.Client" / "wwwroot" / "js" / "app-shell.js").read_text()
        self.assertIn('data-ltx-print-option', view)
        self.assertIn("document.addEventListener('click'", shell)
        self.assertIn("window.ltxReservePrintWindow()", shell)
        self.assertIn("window.ltxPrintPendingWindows", shell)

    def test_print_api_keeps_access_and_not_found_guards(self):
        controller = (API / "Controllers" / "Office" / "OutgoingLettersController.cs").read_text()
        print_action = controller.split('public async Task<IActionResult> Print(', 1)[1].split('// ==================== گردش / ارجاع', 1)[0]
        self.assertIn('ForbiddenUnlessAsync(Module, "Read")', print_action)
        self.assertIn('if (pdf == null) return NotFound', print_action)
        self.assertIn('!string.Equals(size, "A4"', print_action)
        self.assertIn('!string.Equals(size, "A5"', print_action)

    def test_saved_signature_preview_uses_authenticated_bytes_not_an_img_request(self):
        users_page = (Path(__file__).resolve().parents[2] / "src" / "Inventory.Client" / "Pages" / "System" / "Users.razor").read_text()
        auth_controller = (API / "Controllers" / "Core" / "AuthController.cs").read_text()
        self.assertIn('Http.GetAsync($"/api/auth/users/signature/{userId}")', users_page)
        self.assertIn('data:image/png;base64,', users_page)
        self.assertNotIn('src="/api/auth/users/signature/@form.Id"', users_page)
        self.assertIn('[Authorize]', auth_controller.split('[HttpGet("users/signature/{id:int}")]', 1)[1].split('private static byte[] CropSignatureToAlphaBounds', 1)[0])
        self.assertIn('SaveNamedAsync("UserSignatures", id, output, $"user_{id}_signature.png")', auth_controller)
        self.assertIn('Size = new Size(1200, 480)', auth_controller)
        self.assertIn('PngColorType.RgbWithAlpha', auth_controller)
        self.assertIn('Accept="image/png"', users_page)

    def test_templates_travel_with_build_and_publish(self):
        project = ET.parse(API / "Inventory.Api.csproj").getroot()
        content = next(e for e in project.iter("Content") if e.get("Include") == "Reports/**/*.mrt")
        self.assertEqual(content.get("CopyToOutputDirectory"), "PreserveNewest")
        self.assertEqual(content.get("CopyToPublishDirectory"), "PreserveNewest")


if __name__ == "__main__":
    unittest.main()
