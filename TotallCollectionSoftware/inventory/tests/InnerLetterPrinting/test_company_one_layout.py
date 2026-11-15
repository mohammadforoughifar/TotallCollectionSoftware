"""Company 1 MRT structure and production wiring; not a C#/QuestPDF execution test."""
import base64
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

API = Path(__file__).resolve().parents[2] / 'src/Inventory.Api'

class CompanyOneLayoutTests(unittest.TestCase):
    def test_both_templates_have_matching_distinct_value_slots(self):
        for size in ('A4', 'A5'):
            r = ET.parse(API / f'Reports/InnerLetters/Companies/1/Inner-{size}.mrt')
            fields = {}
            for e in r.iter():
                if e.findtext('Name') in ('Text1', 'Text2', 'Text3'):
                    fields[e.findtext('Name')] = tuple(float(x)*72 for x in e.findtext('ClientRectangle').split(','))
            self.assertEqual(len(fields), 3)
            self.assertLess(fields['Text1'][1], fields['Text2'][1])
            self.assertLess(fields['Text2'][1], fields['Text3'][1])
            for x,y,w,h in fields.values():
                self.assertGreaterEqual(x, 0)
                self.assertGreater(w, 0)
                self.assertGreater(h, 0)
            art = base64.b64decode(r.find('.//Watermark/ImageBytes').text)
            self.assertTrue(art.startswith(b'\x89PNG'))
            self.assertEqual(r.find('.//Pages/*/Margins').text, '0,0,0,0')

    def test_company_one_uses_shared_mrt_geometry_pipeline(self):
        loader = (API / 'Services/Office/InnerLetterheadTemplates.cs').read_text()
        renderer = (API / 'Services/Office/InnerLetterPdfRenderer.cs').read_text()
        service = (API / 'Services/Office/InnerLetterPrintService.cs').read_text()
        self.assertIn('TryLoadMetaLayout', loader)
        self.assertIn('InnerLetterheadTemplates.TryLoadMetaLayout', service)
        self.assertIn('meta, company!.Id', service)
        self.assertIn('box.Left', renderer)
        self.assertIn('box.Top', renderer)
        self.assertIn('box.Color', renderer)
        for size in ('A4', 'A5'):
            tree = ET.parse(API / f'Reports/InnerLetters/Companies/1/Inner-{size}.mrt')
            for name, expression in [('Text1', 'DateShams'), ('Text2', 'LetterNumber'), ('Text3', 'Peyvast')]:
                component = next(e for e in tree.iter() if e.findtext('Name') == name)
                self.assertIn(expression, component.findtext('Text'))
                self.assertEqual(component.findtext('TextBrush'), 'Black')

if __name__ == '__main__':
    unittest.main()
