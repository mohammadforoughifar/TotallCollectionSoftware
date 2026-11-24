"""Static Aria/1005 internal/minutes scope and native-artwork contracts; executable PDFs are separate.
Run: python -m unittest discover -s tests/AriaOfficePrinting -p 'test_*.py' -v
"""
from pathlib import Path
import base64
import hashlib
import io
import json
import sys
import unittest
import xml.etree.ElementTree as ET
from PIL import Image

INVENTORY = Path(__file__).resolve().parents[2]
PROJECT = INVENTORY.parent
API = INVENTORY / 'src/Inventory.Api'
sys.path.insert(0, str(PROJECT / 'tools'))
import aria_template_metadata as sync
import build_inner_letter_mrt as inner_builder
import build_minutes_mrt as minutes_builder

MANIFEST = json.loads(Path(__file__).with_name('template-manifest.json').read_text())
ARTWORK = {a['size']: a for a in json.loads((INVENTORY / 'tests/AriaLetterhead/artwork-manifest.json').read_text())['artwork']}

def donor(size):
    return API / f'Reports/OutgoingLetters/Companies/1005/Outgoing-{size}.mrt'

def rectangle(root, name):
    page = root.find('./Pages/Page1')
    margins = [float(v) for v in page.findtext('Margins').split(',')]
    x,y,w,h = [float(v) for v in page.findtext(f'Components/{name}/ClientRectangle').split(',')]
    return ((x+margins[0])*72, (y+margins[2])*72, w*72, h*72)

class AriaOfficeTemplates(unittest.TestCase):
    def test_all_six_runtime_and_fallback_templates_have_the_correct_native_png(self):
        for record in MANIFEST['templates']:
            with self.subTest(template=record['path']):
                root = ET.parse(PROJECT / record['path']).getroot()
                raw = base64.b64decode(root.findtext('.//Watermark/ImageBytes'), validate=True)
                art = ARTWORK[record['size']]
                self.assertEqual(hashlib.sha256(raw).hexdigest(), art['embedded_sha256'])
                image = Image.open(io.BytesIO(raw))
                self.assertEqual((image.format,image.mode,image.size), ('PNG','RGB',tuple(art['pixels'])))
                self.assertEqual(hashlib.sha256(image.tobytes()).hexdigest(), art['pixel_sha256'])
                self.assertAlmostEqual(image.info['dpi'][0], 300, delta=.01)
                self.assertEqual(image.info['srgb'], 1)
                self.assertEqual(root.findtext('./Pages/Page1/PaperSize'), record['size'])

    def test_independent_fields_match_approved_outgoing_artwork_calibration(self):
        for record in MANIFEST['templates']:
            root = ET.parse(PROJECT / record['path']).getroot()
            approved = ET.parse(donor(record['size'])).getroot()
            for name, source in sync.mapping(root).items():
                with self.subTest(template=record['path'], field=name):
                    for a,b in zip(rectangle(root,name),rectangle(approved,source)):
                        self.assertAlmostEqual(a,b,delta=.0001)
                    node = root.find('./Pages/Page1/Components/' + name)
                    reference = approved.find('./Pages/Page1/Components/' + source)
                    for attribute in sync.ATTRIBUTES:
                        if attribute != 'ClientRectangle':
                            self.assertEqual(node.findtext(attribute),reference.findtext(attribute),attribute)
                    self.assertEqual(node.findtext('CanGrow'),'False')
                    self.assertEqual(node.findtext('WordWrap'),'False')
                    self.assertEqual(node.findtext('ShrinkFontToFitMinimumSize'),'1')
                    self.assertIn('RightToLeft=' + ('True' if name=='AttachmentValue' else 'False'),node.findtext('TextOptions'))

    def test_original_body_participants_bindings_dictionary_and_all_other_xml_are_unchanged(self):
        for record in MANIFEST['templates']:
            text = (PROJECT / record['path']).read_text()
            self.assertEqual(sync.unaffected_hash(text), record['unaffected_xml_sha256'],record['path'])

    def test_correct_bindings_and_watermark_flags_survive(self):
        for record in MANIFEST['templates']:
            root = ET.parse(PROJECT / record['path']).getroot()
            expected = {'NumberValue':'LetterNumber','DateValue':'DateShams','AttachmentValue':'Peyvast'} if 'InnerLetters' in record['path'] else {
                'NumberValue':'Number','MeetingDateValue':'MeetingDate','AttachmentValue':'Attachment'}
            for name,token in expected.items():
                self.assertEqual(root.findtext(f'./Pages/Page1/Components/{name}/Text'),'{DataSource1.'+token+'}')
            for flag in ('Enabled','ImageBehind','ImageStretch'):
                self.assertEqual(root.findtext(f'.//Watermark/{flag}'),'True')

    def test_archive_fallback_copies_are_identical_to_active_minutes_templates(self):
        for size in ('A4','A5'):
            self.assertEqual((PROJECT/f'Report/meetingminutes/1005/Report_{size}_MeetingMinutes.mrt').read_bytes(),
                             (API/f'Reports/MeetingMinutes/Companies/1005/Minutes-{size}.mrt').read_bytes())

    def test_updater_is_byte_idempotent_and_rejects_unexpected_units(self):
        for record in MANIFEST['templates']:
            text=(PROJECT/record['path']).read_bytes().decode('utf-8')
            source=donor(record['size']).read_bytes().decode('utf-8')
            self.assertEqual(sync.sync_aria_metadata(text,source),text)
            with self.assertRaises(ValueError):
                sync.sync_aria_metadata(text.replace('<ReportUnit>Inches</ReportUnit>','<ReportUnit>Pixels</ReportUnit>'),source)

    def test_builders_cannot_reintroduce_old_aria_image_or_metadata(self):
        for builder in (inner_builder,minutes_builder):
            for size in ('A4','A5'):
                text=builder.build(size,'1005')
                root=ET.fromstring(text)
                self.assertEqual(text,sync.sync_aria_metadata(text,donor(size).read_text()))
                self.assertEqual(hashlib.sha256(base64.b64decode(root.findtext('.//Watermark/ImageBytes'))).hexdigest(),ARTWORK[size]['embedded_sha256'])
                for name,source in sync.mapping(root).items():
                    for actual,expected in zip(rectangle(root,name),rectangle(ET.parse(donor(size)).getroot(),source)):
                        self.assertAlmostEqual(actual,expected,delta=.0001)

    def test_other_company_generator_results_are_byte_identical(self):
        for record in MANIFEST['unchanged_other_company_builders']:
            generated=minutes_builder.build(record['size'],record['company'])
            self.assertEqual(hashlib.sha256(generated.encode()).hexdigest(),record['sha256'])

    def test_native_quality_policy_is_company_scoped_and_default_dpi_is_not_changed(self):
        loader=(API/'Services/Office/InnerLetterheadTemplates.cs').read_text()
        service=(API/'Services/Office/InnerLetterPrintService.cs').read_text()
        self.assertIn('var artworkDpi = companyId == 1005 ? 0 : dpi;',loader)
        self.assertIn('TryLoadArtwork(path, a5, artworkDpi)',loader)
        self.assertIn('DefaultArtworkDpi = 200;',loader)
        self.assertIn('TryLoadForCompany(_env.ContentRootPath, AppContext.BaseDirectory, company.Id, a5,',service)
        self.assertIn('var letterheadArt = foroughArt ?? companyArt;',service)
        minutes=(API/'Services/Office/MeetingMinutesPrintService.cs').read_text()
        self.assertIn(': templateLetterhead ?? registeredLetterhead;',minutes)
        self.assertIn('var useForoughFile = isForough && foroughLetterhead != null;',minutes)

    def test_exact_painter_is_opt_in_and_does_not_normalize_the_physical_rectangles(self):
        painter=(API/'Services/Office/HeaderMetaPainter.cs').read_text()
        exact=painter.split('internal static void DrawAbsolute',1)[1].split('private static List<Row>',1)[0]
        for requirement in ('.Width(right).Height(bottom)', 'layers.PrimaryLayer()', '.Width(cell.Width).Height(cell.Height)',
                            '.ScaleToFit()', 'DirectionFromLeftToRight()', 'DirectionFromRightToLeft()'):
            self.assertIn(requirement,exact)
        self.assertNotIn('FitFontSize(',exact)
        self.assertNotIn('GroupIntoRows(',exact)
        self.assertNotIn('MinimumColumnWidth',exact)
        renderer=(API/'Services/Office/InnerLetterPdfRenderer.cs').read_text()
        self.assertIn('if (companyId == 1005 && metaRows.Count > 0)',renderer)
        self.assertIn('HeaderMetaPainter.Draw(host, metaRows,',renderer)
        self.assertIn('var fallback = AriaLetterheadLayout.MetaFields(a5);',renderer)
        profile=(API/'Services/Office/MinutesLetterheadProfile.cs').read_text()
        self.assertEqual(profile.count('ExactMetaRectangles: true'),2)
        aria=profile.split('private static MinutesLetterheadProfile AriaArtwork(',1)[1].split('private static MinutesLetterheadProfile Aria(',1)[0]
        self.assertIn('HeaderReserve: 128f',aria)
        self.assertIn('HeaderReserve: 90f',aria)
        self.assertEqual(aria.count('ExactMetaRectangles: true'),2)

    def test_registered_attachment_and_workflow_rules_are_not_replaced(self):
        inner=(API/'Services/Office/InnerLetterPrintService.cs').read_text()
        minutes=(API/'Services/Office/MeetingMinutesPrintService.cs').read_text()
        self.assertIn('x.Module == "InnerLetters" && x.RefId == letterId',inner)
        self.assertIn('x.Type == "هامش"',inner)
        self.assertIn('a.Module == "MeetingMinutes" && a.RefId == minutesId',minutes)
        self.assertIn('await ResolveCompanyAsync(m.CompanyId)',minutes)
        self.assertIn('LetterPrintTemplate.PersianDigits(m.Id.ToString(CultureInfo.InvariantCulture))',minutes)
        self.assertIn('TryDecodeSignaturePng(p.SignatureData)',minutes)
        self.assertIn('withHeaderMeta: false',minutes)

if __name__=='__main__':
    unittest.main()
