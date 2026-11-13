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

    def test_only_company_one_uses_black_mrt_coordinates(self):
        loader = (API / 'Services/Office/InnerLetterheadTemplates.cs').read_text()
        renderer = (API / 'Services/Office/InnerLetterPdfRenderer.cs').read_text()
        service = (API / 'Services/Office/InnerLetterPrintService.cs').read_text()
        self.assertIn('if (companyId != 1) return null;', loader)
        self.assertIn('valueLayout is null ? Colors.White : Colors.Black', renderer)
        self.assertIn('valueLayout?.Date ?? HomepaLetterheadLayout.Date(a5)', renderer)
        self.assertIn('fonts, valueLayout)', service)
        self.assertIn('data.ReferralDates', renderer)

if __name__ == '__main__':
    unittest.main()
