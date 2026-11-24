"""Check production PDFs: values, native pixels, undamaged labels, body/hamesh/signature flow.
Usage: python check_rendered.py <rendered-dir> [<pre-change-other-company-rendered-dir>]
Requires PyMuPDF + Pillow + fontTools. No production database, network or physical printer is used.
"""
from pathlib import Path
import hashlib
import json
import math
import re
import sys
import unicodedata
import pymupdf
from PIL import Image, ImageChops
from fontTools.ttLib import TTFont

# Vazirmatn's elongated contextual glyphs have no Unicode mapping; MuPDF reports
# their glyph IDs as characters. Resolve only unmapped IDs from the actual font's
# uniXXXX glyph names. This does not change the printed text or excuse missing glyphs.
FONT_ALIASES = {}
for style in ('Regular', 'Bold'):
    font = TTFont(Path(__file__).parents[2] / f'src/Inventory.Api/Resources/fonts/Vazirmatn-{style}.ttf')
    cmap = font.getBestCmap()
    for gid, glyph in enumerate(font.getGlyphOrder()):
        match = re.match(r'^uni([0-9A-Fa-f]{4})(?:\.|$)', glyph)
        if gid not in cmap and match:
            value = chr(int(match[1], 16))
            assert gid not in FONT_ALIASES or FONT_ALIASES[gid] == value
            FONT_ALIASES[gid] = value
    font.close()

ROOT = Path(sys.argv[1])
MANIFEST = json.loads((ROOT / "render-manifest.json").read_text())
ARTWORK = {a['size']: a for a in json.loads((Path(__file__).parents[1] / "AriaLetterhead/artwork-manifest.json").read_text())['artwork']}
CAPTIONS = {
    "A4": [(63, 100, 94, 118), (158, 100, 185, 118), (251.8, 100, 276, 118)],
    "A5": [(44.4, 70, 66, 84), (111.4, 70, 131, 84), (177.4, 70, 195, 84)],
}
checks = 0

def check(ok, message):
    global checks
    assert ok, message
    checks += 1

def normal(text):
    return ''.join(c for c in unicodedata.normalize('NFKC', text.translate(FONT_ALIASES)) if unicodedata.category(c) != 'Cf').strip()

def compact(text):
    return ''.join(normal(text).split())

def raster(page):
    pixmap = page.get_pixmap(matrix=pymupdf.Matrix(300/72, 300/72), alpha=False)
    return Image.frombytes('RGB', (pixmap.width, pixmap.height), pixmap.samples)

def crop(image, box):
    return image.crop((math.floor(box[0]*300/72), math.floor(box[1]*300/72), math.ceil(box[2]*300/72), math.ceil(box[3]*300/72)))

references = {}
for family in ('inner', 'minutes'):
    for size in ('A4', 'A5'):
        with pymupdf.open(ROOT / f'{family}-1005-{size}-art-only.pdf') as doc:
            references[family, size] = raster(doc[0])

for item in MANIFEST:
    with pymupdf.open(ROOT / item['file']) as doc:
        family, size = item['family'], item['size']
        reference = references[family, size]
        first_boxes, all_spans, other_images = {}, [], []
        check(len(doc) == item['pages'], item['file'] + ': page count')
        for page_no, page in enumerate(doc):
            label = f"{item['file']}/page-{page_no+1}"
            check(tuple(page.rect[2:]) == ((420.0, 595.0) if size == 'A5' else (595.0, 842.0)), label + ': paper dimensions')
            image = raster(page)
            spans = [(normal(s['text']), s['bbox']) for b in page.get_text('dict')['blocks']
                     for line in b.get('lines', []) for s in line['spans'] if normal(s['text'])]
            cutoff = 86 if size == 'A5' else 122
            raw = page.get_text('rawdict')
            # Restore logical RTL body lines from physical glyph positions. Extractor spans
            # alone can place a participant's numeric suffix before their name or split a
            # word at an elongated glyph. Digit runs remain LTR within the RTL sentence.
            for block in raw['blocks']:
                for line in block.get('lines', []):
                    if line['bbox'][1] < cutoff:
                        continue
                    ordered = sorted((c for s in line['spans'] for c in s['chars']),
                                     key=lambda c: -(c['bbox'][0]+c['bbox'][2])/2)
                    logical = normal(''.join(c['c'] for c in ordered))
                    logical = re.sub('[۰-۹0-9/]+', lambda m: m[0][::-1], logical)
                    all_spans.append((page_no, logical, line['bbox']))
            # PDF extractors merge adjacent fields into a span. Partition actual glyphs by rectangles.
            chars = [c for b in raw['blocks'] for line in b.get('lines', [])
                     for span in line['spans'] for c in span['chars'] if normal(c['c']) and c['bbox'][1] < cutoff]
            assigned = set()
            for field in item['fields']:
                eps = .15
                matches = [(i, c) for i, c in enumerate(chars)
                           if field['left']-eps <= (c['bbox'][0]+c['bbox'][2])/2 <= field['left']+field['width']+eps
                           and field['top']-eps <= (c['bbox'][1]+c['bbox'][3])/2 <= field['top']+field['height']+eps]
                values = [c for _, c in matches]
                check(normal(''.join(c['c'] for c in values)) == field['value'], label + '/' + field['field'] + ': complete value/digit order')
                assigned.update(i for i, _ in matches)
                check(max(c['origin'][1] for c in values)-min(c['origin'][1] for c in values) < .1,
                      label + '/' + field['field'] + ': exactly one baseline')
                bounds = (min(c['bbox'][0] for c in values), min(c['bbox'][1] for c in values),
                          max(c['bbox'][2] for c in values), max(c['bbox'][3] for c in values))
                check(bounds[0] >= field['left']-eps and bounds[2] <= field['left']+field['width']+eps,
                      label + '/' + field['field'] + ': horizontal glyph bounds')
                check(bounds[1] >= field['top']-eps and bounds[3] <= field['top']+field['height']+eps,
                      label + '/' + field['field'] + ': vertical glyph bounds')
                if page_no:
                    check(max(abs(a-b) for a,b in zip(first_boxes[field['field']], bounds)) < .1,
                          label + '/' + field['field'] + ': stable continuation position')
                else:
                    first_boxes[field['field']] = bounds
                plaque = crop(image, (field['left']+.5, field['top']+.5, field['left']+field['width']-.5, field['top']+field['height']-.5))
                r, g, b = plaque.split()
                difference = ImageChops.lighter(ImageChops.difference(r,g), ImageChops.lighter(ImageChops.difference(r,b), ImageChops.difference(g,b)))
                check(difference.getextrema()[1] <= 3, label + '/' + field['field'] + ': full white value mask')
            check(len(assigned) == len(chars), label + ': no omitted/stray metadata glyphs')
            for index, box in enumerate(CAPTIONS[size]):
                check(crop(image, box).tobytes() == crop(reference, box).tobytes(), label + f': caption/colon {index+1} pixel-identical')
            logo = (350,20,570,118) if size == 'A4' else (245,15,402,84)
            check(crop(image, logo).tobytes() == crop(reference, logo).tobytes(), label + ': untouched logo/name artwork')
            footer = (0,762,595,842) if size == 'A4' else (0,539,420,595)
            if page_no == 0 or item['repeatFooter']:
                check(crop(image, footer).tobytes() == crop(reference, footer).tobytes(), label + ': intact footer')
            else:
                check(crop(image, footer).getextrema() == ((255,255),)*3, label + ': existing minutes header-only continuation retained')
            images = [i for i in page.get_images(full=True) if list(i[2:4]) == ARTWORK[size]['pixels']]
            check(len(images) == 1, label + ': native 300-DPI artwork dimensions')
            pixmap = pymupdf.Pixmap(doc, images[0][0])
            check(pixmap.n == 3 and hashlib.sha256(pixmap.samples).hexdigest() == ARTWORK[size]['pixel_sha256'], label + ': all RGB artwork pixels retained losslessly')
            body = [(text, box) for text, box in spans if box[1] >= cutoff]
            check(bool(body), label + ': no blank overflow page')
            # Font ascenders can extend slightly above the flow origin, not into the artwork.
            check(all(box[1] >= item['bodyTop']-1.5 for _, box in body), label + ': body stays below header reserve')
            check(all(box[3] <= page.rect.height-item['footerReserve']+1 for _, box in body), label + ': body/hamesh/signatures above footer reserve')
            check(all(box[0] >= -0.1 and box[2] <= page.rect.width+.1 for _, box in body), label + ': body stays within paper horizontally')
            if item['fields']:
                check(min(b[1] for _,b in body)-max(f['top']+f['height'] for f in item['fields']) >= 4.5,
                      label + ': visible metadata/body clearance')
            for drawn in page.get_image_info():
                if [drawn['width'], drawn['height']] != ARTWORK[size]['pixels']:
                    other_images.append((page_no, drawn['bbox']))
                    check(drawn['bbox'][1] >= item['bodyTop']-1.5 and drawn['bbox'][3] <= page.rect.height-item['footerReserve']+1,
                          label + ': synthetic signature image stays in the flowing content')
        text = compact(' '.join(t for _,t,_ in all_spans))
        for value in item['expected']:
            check(compact(value) in text, item['file'] + ': content retained: ' + value)
        raw_document_text = compact(' '.join(page.get_text('text') for page in doc))
        for value in item['forbidden']:
            check(compact(value) not in text and compact(value) not in raw_document_text,
                  item['file'] + ': forbidden/disabled content absent: ' + value)
        check(bool(other_images) == item['signatureImage'], item['file'] + ': signed/unsigned image semantics retained')
        if item['people']:
            final = next((p,b) for p,t,b in all_spans if compact('پایان متن آزمایشی') in compact(t))
            for name in item['people']:
                positions = [(p,b) for p,t,b in all_spans if compact(t) == compact(name)]
                check(bool(positions), item['file'] + ': individual signature name retained: ' + name)
                check(any(p > final[0] or (p == final[0] and b[1] > final[1][3]) for p,b in positions),
                      item['file'] + ': signature follows last item: ' + name)
            for absent in item['absent']:
                check(text.count(compact(absent)) == 1, item['file'] + ': absentee listed once, never given a signature box')
        print(f"PASS: {item['file']} ({len(doc)} pages; values, geometry, captions, artwork and content)")

if len(sys.argv) > 2:
    baseline = Path(sys.argv[2])
    for old in sorted(baseline.glob('*-regression.pdf')):
        current = ROOT / old.name
        with pymupdf.open(old) as before, pymupdf.open(current) as after:
            check(len(before) == len(after), old.name + ': other-company pagination unchanged')
            for n,(a,b) in enumerate(zip(before,after)):
                check(a.get_text('text') == b.get_text('text'), old.name + f'/{n+1}: other-company text unchanged')
                check(raster(a).tobytes() == raster(b).tobytes(), old.name + f'/{n+1}: pixel-identical to pre-change renderer at 300 DPI')
        print('PASS: unchanged other-company PDF:', old.name)
print(f'ALL {checks} OFFICE PDF ASSERTIONS PASSED across {len(MANIFEST)} Aria documents.')
