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

    def test_content_pdf_does_not_cover_the_underlaid_letterhead(self):
        source = SERVICE.read_text()
        self.assertIn("page.PageColor(hasLetterhead ? Colors.Transparent : Colors.White);", source)
        self.assertLess(source.index("gfx.DrawImage(letterheadImg, rect)"), source.index("gfx.DrawImage(contentForm, rect)"))

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
                    expected = ((margins[0] + x) * 72 - left_shift, (margins[2] + y) * 72, width * 72, height * 72)
                    actual = [float(v.strip().removesuffix("f")) for v in variants[index].split(",")]
                    for value, target in zip(actual, expected):
                        self.assertAlmostEqual(value, target, places=3)
                    self.assertGreaterEqual(actual[0], 0)
                    self.assertLess(actual[0] + actual[2], float(page.findtext("PageWidth")) * 72 / 3,
                                    "values must stay in the physical left third, not the RTL right edge")

    def test_homepa_header_is_left_anchored_and_fields_are_independent(self):
        source = SERVICE.read_text()
        branch = source.split("else if (hasLetterhead && letterheadCompanyId == HomepaCompanyId)", 1)[1]
        branch = branch.split("else if (hasLetterhead)", 1)[0]
        self.assertIn("page.Header().ContentFromLeftToRight()", branch)
        self.assertIn(".Layers(layers =>", branch)
        for field in ("Number", "Date", "Attachment"):
            self.assertIn(f"HomepaLetterheadLayout.{field}(isA5)", branch)
        self.assertNotIn(".Column(", branch)
        self.assertIn(".Height(isA5 ? 150 : 160)", branch, "body layout must not move")

    def test_print_does_not_inject_a_closing_or_stamp_box(self):
        source = SERVICE.read_text()
        self.assertNotIn("closingText", source)
        self.assertNotIn("closingMatch", source)
        self.assertNotIn('Text("با تشکر")', source)
        self.assertNotIn('Text("مهر")', source)
        self.assertNotIn("stampHeight", source)
        self.assertIn("var bodyText = HtmlToPlainText(letter.Text);", source,
                      "authored text must not be truncated at a closing-looking line")
        self.assertIn(".Text(signer.Name)", source)
        self.assertIn(".Text(signer.Role)", source)
        self.assertIn(".Image(signer.Signature)", source)
        self.assertIn("if (signerRows.Count > 0)", source)

    def test_templates_travel_with_build_and_publish(self):
        project = ET.parse(API / "Inventory.Api.csproj").getroot()
        content = next(e for e in project.iter("Content") if e.get("Include") == "Reports/**/*.mrt")
        self.assertEqual(content.get("CopyToOutputDirectory"), "PreserveNewest")
        self.assertEqual(content.get("CopyToPublishDirectory"), "PreserveNewest")


if __name__ == "__main__":
    unittest.main()
