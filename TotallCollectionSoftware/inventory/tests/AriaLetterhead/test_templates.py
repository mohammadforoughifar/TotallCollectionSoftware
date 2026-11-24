"""Company 1005: full-resolution artwork and metadata contracts; no database/network.
Requires Pillow. Run from inventory: python -m unittest discover -s tests/AriaLetterhead -p 'test_*.py' -v
"""
from pathlib import Path
import base64
import hashlib
import importlib.util
import io
import json
import re
import unittest
import xml.etree.ElementTree as ET
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
API = ROOT / "src/Inventory.Api"
MANIFEST = json.loads((Path(__file__).with_name("artwork-manifest.json")).read_text())
SPEC = importlib.util.spec_from_file_location("aria_updater", ROOT / "tools/update_aria_letterhead.py")
UPDATER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(UPDATER)

def template(record):
    path = API / "Reports/OutgoingLetters/Companies/1005" / f"Outgoing-{record['size']}.mrt"
    return path, ET.parse(path).getroot()

def artwork(record):
    _, xml = template(record)
    data = base64.b64decode(xml.findtext(".//Watermark/ImageBytes"), validate=True)
    return data, Image.open(io.BytesIO(data))

def fields(xml):
    page = xml.find("./Pages/Page1")
    margins = [float(x) for x in page.findtext("Margins").split(",")]
    result = {}
    for key in ("Text1", "Text2", "Text3"):
        node = page.find("Components/" + key)
        x, y, w, h = [float(v) for v in node.findtext("ClientRectangle").split(",")]
        result[key] = ((x + margins[0]) * 72, (y + margins[2]) * 72, w * 72, h * 72, node)
    return result

class AriaTemplatesTests(unittest.TestCase):
    def test_native_resolution_is_retained_without_lossy_recompression(self):
        for record in MANIFEST["artwork"]:
            with self.subTest(size=record["size"]):
                data, image = artwork(record)
                self.assertEqual(image.format, "PNG")
                self.assertEqual(image.mode, "RGB")
                self.assertEqual(image.size, tuple(record["pixels"]))
                self.assertEqual(hashlib.sha256(data).hexdigest(), record["embedded_sha256"])
                self.assertEqual(hashlib.sha256(image.tobytes()).hexdigest(), record["pixel_sha256"])
                self.assertEqual(image.info.get("srgb"), 1)
                self.assertAlmostEqual(image.info["dpi"][0], 300, delta=.01)
                self.assertAlmostEqual(image.info["dpi"][1], 300, delta=.01)

    def test_a4_and_a5_use_their_respective_distinct_source_images(self):
        a4, a5 = MANIFEST["artwork"]
        self.assertEqual((a4["size"], a5["size"]), ("A4", "A5"))
        self.assertIn("A4", a4["source_name"])
        self.assertIn("A5", a5["source_name"])
        self.assertNotEqual(a4["source_sha256"], a5["source_sha256"])
        self.assertNotEqual(a4["embedded_sha256"], a5["embedded_sha256"])

    def test_body_signatures_dictionary_and_other_components_are_unchanged(self):
        for record in MANIFEST["artwork"]:
            path, _ = template(record)
            self.assertEqual(UPDATER.unaffected_xml_hash(path.read_text()), record["unaffected_xml_sha256"])

    def test_metadata_rectangles_match_the_new_artwork_measurements(self):
        for record in MANIFEST["artwork"]:
            _, xml = template(record)
            for key, (left, top, width, height, _) in fields(xml).items():
                with self.subTest(size=record["size"], field=key):
                    self.assertAlmostEqual(left, record["fields"][key]["left"], delta=.0001)
                    self.assertAlmostEqual(width, record["fields"][key]["width"], delta=.0001)
                    self.assertAlmostEqual(top, record["top"], delta=.0001)
                    self.assertAlmostEqual(height, record["height"], delta=.0001)
                    self.assertAlmostEqual(top + height / 2, 109.9 if record["size"] == "A4" else 77.65, delta=.01)

    def test_values_do_not_cover_the_printed_colons(self):
        colons = {"A4": {"Text1": 252.03, "Text2": 158.18, "Text3": 63.13},
                  "A5": {"Text1": 177.58, "Text2": 111.52, "Text3": 44.51}}
        for record in MANIFEST["artwork"]:
            _, xml = template(record)
            for key, (left, _, width, _, _) in fields(xml).items():
                self.assertGreater(colons[record["size"]][key] - left - width, .49)

    def test_declared_values_have_full_white_background_and_single_line_direction(self):
        for record in MANIFEST["artwork"]:
            _, xml = template(record)
            for key, (*_, node) in fields(xml).items():
                with self.subTest(size=record["size"], field=key):
                    self.assertEqual(node.findtext("Brush"), "White")
                    self.assertEqual(node.findtext("TextBrush"), "Black")
                    self.assertEqual(node.findtext("CanGrow"), "False")
                    self.assertEqual(node.findtext("CanShrink"), "False")
                    self.assertEqual(node.findtext("WordWrap"), "False")
                    self.assertEqual(node.findtext("ShrinkFontToFit"), "True")
                    self.assertEqual(node.findtext("ShrinkFontToFitMinimumSize"), "1")
                    self.assertEqual(node.findtext("HorAlignment"), "Right")
                    self.assertEqual(node.findtext("VertAlignment"), "Center")
                    self.assertIn("RightToLeft=" + ("True" if key == "Text3" else "False"), node.findtext("TextOptions"))

    def test_new_row_fits_header_and_is_clear_of_the_body(self):
        for record in MANIFEST["artwork"]:
            _, xml = template(record)
            a5 = record["size"] == "A5"
            bottom = record["top"] + record["height"]
            self.assertLess(bottom, 86 if a5 else 119)
            self.assertGreater((116.64 if a5 else 165.6) - bottom, 30)
            self.assertEqual(xml.findtext("./Pages/Page1/Watermark/ImageStretch"), "True")

    def test_fallback_geometry_matches_declared_mrt_rectangles(self):
        source = (API / "Services/Office/Outgoing/AriaLetterheadLayout.cs").read_text()
        for key, name in (("Text1", "Date"), ("Text2", "Number"), ("Text3", "Attachment")):
            method = re.search(r"static Field " + name + r"\(bool isA5\)(.*?);", source, re.S).group(1)
            variants = re.findall(r"new\(([^)]+)\)", method)
            for index, size in enumerate(("A5", "A4")):
                actual = [float(v.strip().removesuffix("f")) for v in variants[index].split(",")]
                record = next(r for r in MANIFEST["artwork"] if r["size"] == size)
                expected = [record["fields"][key]["left"], record["top"] - (16 if size == "A5" else 24),
                            record["fields"][key]["width"], record["height"]]
                for a, b in zip(actual, expected): self.assertAlmostEqual(a, b, delta=.0001)

    def test_artwork_preference_does_not_enable_a_different_body_engine(self):
        settings = (API / "appsettings.json").read_text()
        self.assertIn('"TemplateLetterheadCompanyIds": "1003,1,1005"', settings)
        self.assertIn('"DesignDrivenCompanyIds": "1003,1"', settings)
        source = (API / "Services/Office/Outgoing/OutgoingLetterPrintService.cs").read_text()
        self.assertIn('DefaultTemplateLetterheadCompanyIds = { DefaultDesignDrivenCompanyId, AriaCompanyId }', source)
        self.assertIn('"Reporting:OutgoingLetters:TemplateLetterheadCompanyIds", DefaultTemplateLetterheadCompanyIds', source)
        branch = source.split("if (isAriaLetterhead)", 1)[1].split("else if (isHomepaLetterhead)", 1)[0]
        self.assertIn("declared ?? AriaLetterheadLayout.MetaFields(isA5)", branch)
        self.assertIn("OutgoingLetterMetaPainter.Draw", branch)
        self.assertIn("field.Top - pageTopMargin", branch)
        self.assertIn(".Height(AriaLetterheadLayout.HeaderHeight(isA5))", branch)

if __name__ == "__main__":
    unittest.main()
