"""Forough outgoing MRT contract: fixed metadata boxes, no caption collision."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
TEMPLATES = ROOT / 'src/Inventory.Api/Reports/OutgoingLetters/Companies'


class MetadataTemplateTests(unittest.TestCase):
    def fields(self):
        for company in (1, 2):
            for size in ('A4', 'A5'):
                root = ET.parse(TEMPLATES / str(company) / f'Outgoing-{size}.mrt').getroot()
                page = root.find('Pages')[0]
                margins = list(map(float, page.findtext('Margins').split(',')))
                for row, column in enumerate(('DateShams', 'LetterNumber', 'Peyvast')):
                    field = next(e for e in page.find('Components') if e.findtext('Text') == '{DataSource1.' + column + '}')
                    rect = list(map(float, field.findtext('ClientRectangle').split(',')))
                    physical = ((margins[0]+rect[0])*72, (margins[2]+rect[1])*72, rect[2]*72, rect[3]*72)
                    yield company, size, row, column, field, physical

    def test_physical_boxes_match_the_artwork(self):
        for company, size, row, column, field, actual in self.fields():
            with self.subTest(company=company, size=size, column=column):
                scale = .7071 if size == 'A5' else 1
                expected = (22*scale, (28+row*27.2)*scale, 62*scale, 15*scale)
                for a, e in zip(actual, expected):
                    self.assertAlmostEqual(a, e, delta=.01)

    def test_values_end_before_printed_captions(self):
        for company, size, row, column, field, rect in self.fields():
            with self.subTest(company=company, size=size, column=column):
                scale = .7071 if size == 'A5' else 1
                self.assertGreater(rect[0], 0)
                self.assertLess(rect[0]+rect[2], 87*scale)
                self.assertLess(rect[3], 27.2*scale)

    def test_white_background_covers_dots_not_labels(self):
        for company, size, row, column, field, rect in self.fields():
            with self.subTest(company=company, size=size, column=column):
                self.assertEqual(field.findtext('Brush'), 'White')
                self.assertEqual(field.findtext('TextBrush'), 'Black')
                self.assertEqual(field.findtext('HorAlignment'), 'Right')
                self.assertEqual(field.findtext('VertAlignment'), 'Center')

    def test_one_line_no_growth_and_correct_direction(self):
        for company, size, row, column, field, rect in self.fields():
            with self.subTest(company=company, size=size, column=column):
                self.assertEqual(field.findtext('CanGrow'), 'False')
                self.assertEqual(field.findtext('ShrinkFontToFit'), 'True')
                self.assertIn('WordWrap=False', field.findtext('TextOptions'))
                self.assertIn('RightToLeft=' + str(column == 'Peyvast'), field.findtext('TextOptions'))

    def test_company_copies_have_identical_metadata(self):
        for size in ('A4','A5'):
            a = [ET.tostring(row[4]) for row in self.fields() if row[0] == 1 and row[1] == size]
            b = [ET.tostring(row[4]) for row in self.fields() if row[0] == 2 and row[1] == size]
            self.assertEqual(a, b)

    def test_active_renderer_uses_mrt_and_versions_disk_cache(self):
        source = (ROOT / 'src/Inventory.Api/Services/Office/Outgoing/OutgoingLetterPrintService.cs').read_text()
        method = source.split('private void AddArtworkMetaValues', 1)[1].split('private void AddDesignMetaGrid', 1)[0]
        self.assertIn('ReadDeclaredMetaFields', method)
        self.assertIn('OutgoingLetterMetaPainter.Draw', method)
        self.assertNotIn('.Width(geometry.ValueRight)', method)
        self.assertIn('geometry.ValueRight - geometry.ValueLeft', method)
        self.assertIn('PrintTemplateCacheStamp(row.CompanyId, size)', source)
        self.assertIn('outgoing-print-v3-mrt-meta:', source)
        self.assertIn('info.LastWriteTimeUtc.Ticks', source)


if __name__ == '__main__':
    unittest.main()
