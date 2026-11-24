"""Check the actual rendered PDF glyph bounds, not just MRT/source rectangles.
Run after the .NET renderer probe: python check_rendered.py <artifact-directory>
Dependency: PyMuPDF. No network/database/SMTP access.
"""
from pathlib import Path
import sys, json, unicodedata
import pymupdf

root = Path(sys.argv[1])
manifest = json.loads((root / 'render-manifest.json').read_text())
checks = 0

def check(ok, message):
    global checks
    assert ok, message
    checks += 1

def normalized(text):
    return ''.join(c for c in unicodedata.normalize('NFKC', text) if unicodedata.category(c) != 'Cf').strip()

for item in manifest:
    doc = pymupdf.open(root / item['file'])
    scale = .7071 if item['size'] == 'A5' else 1
    first_boxes = {}
    for page_number, page in enumerate(doc):
        lines = [line for block in page.get_text('dict')['blocks'] for line in block.get('lines', [])]
        lines = [(normalized(''.join(s['text'] for s in line['spans'])), line['bbox']) for line in lines]
        header = [(text, box) for text, box in lines if text and box[0] < 90*scale and box[1] < 100*scale]
        check(len(header) == 3, f"{item['file']}/{page_number}: exactly three header value lines, no wraps")
        for field in item['fields']:
            matching = [box for text, box in header if text == field['value']]
            check(len(matching) == 1, f"{item['file']}/{page_number}/{field['column']}: full value/order retained")
            box = matching[0]
            eps = .4
            check(box[0] >= field['left']-eps and box[2] <= field['left']+field['width']+eps,
                  f"{item['file']}/{page_number}/{field['column']}: glyphs inside MRT horizontal bounds")
            check(box[1] >= field['top']-eps and box[3] <= field['top']+field['height']+eps,
                  f"{item['file']}/{page_number}/{field['column']}: glyphs inside MRT vertical bounds")
            check(box[2] < 87*scale, f"{item['file']}/{page_number}/{field['column']}: no caption collision")
            if page_number:
                check(max(abs(a-b) for a,b in zip(first_boxes[field['column']], box)) < .4,
                      f"{item['file']}/{page_number}/{field['column']}: stable metadata on continuation page")
            else:
                first_boxes[field['column']] = box
    print('PASS:', item['file'], f'({len(doc)} pages; all glyph bounds/values verified)')
print(f'ALL {checks} PDF VALUE/GEOMETRY ASSERTIONS PASSED across {len(manifest)} actual production-rendered PDFs.')
