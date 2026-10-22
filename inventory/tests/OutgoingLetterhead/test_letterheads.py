"""Asset + source-contract checks; does not run the .NET renderer or access a database.
python3 -m unittest discover -s inventory/tests/OutgoingLetterhead -p 'test_*.py' -v
"""
import base64
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

    def test_templates_travel_with_build_and_publish(self):
        project = ET.parse(API / "Inventory.Api.csproj").getroot()
        content = next(e for e in project.iter("Content") if e.get("Include") == "Reports/**/*.mrt")
        self.assertEqual(content.get("CopyToOutputDirectory"), "PreserveNewest")
        self.assertEqual(content.get("CopyToPublishDirectory"), "PreserveNewest")


if __name__ == "__main__":
    unittest.main()
