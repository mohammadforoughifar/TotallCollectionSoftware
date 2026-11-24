"""Verify actual PDF values, glyph bounds, untouched printed captions and lossless artwork.
Run after the .NET probe: python check_rendered.py <artifact-directory>
Dependencies: PyMuPDF, Pillow. No network, database or external mail.
"""
from pathlib import Path
import hashlib
import json
import math
import sys
import unicodedata
import pymupdf
from PIL import Image, ImageChops

ROOT = Path(sys.argv[1])
MANIFEST = json.loads((ROOT / "render-manifest.json").read_text())
ARTWORK = {r["size"]: r for r in json.loads(Path(__file__).with_name("artwork-manifest.json").read_text())["artwork"]}
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
    return ''.join(c for c in unicodedata.normalize("NFKC", text) if unicodedata.category(c) != "Cf").strip()

def compact(text):
    return ''.join(normal(text).split())

def raster(page):
    # Render the entire page once. Rendering tiny, differently clipped regions in
    # different orders can exercise MuPDF's image-scaling cache differently.
    pixmap = page.get_pixmap(matrix=pymupdf.Matrix(300/72, 300/72), alpha=False)
    return Image.frombytes("RGB", (pixmap.width, pixmap.height), pixmap.samples)

def crop(image, rectangle):
    left, top, right, bottom = rectangle
    return image.crop((math.floor(left*300/72), math.floor(top*300/72),
                       math.ceil(right*300/72), math.ceil(bottom*300/72)))

references = {size: pymupdf.open(ROOT / f"1005-{size}-art-only.pdf") for size in ("A4", "A5")}
reference_rasters = {size: raster(document[0]) for size, document in references.items()}
for item in MANIFEST:
    document = pymupdf.open(ROOT / item["file"])
    size = item["size"]
    first_boxes = {}
    all_text = []
    check(len(document) == item["pages"], item["file"] + ": manifest page count")
    for page_number, page in enumerate(document):
        label = f"{item['file']}/page-{page_number+1}"
        page_image = raster(page)
        spans = [(normal(span['text']), span['bbox']) for block in page.get_text("dict")['blocks']
                 for line in block.get('lines', []) for span in line['spans'] if normal(span['text'])]
        all_text.extend(text for text, _ in spans)
        header = [(text, box) for text, box in spans if box[0] < (200 if size == "A5" else 290)
                  and box[1] < (90 if size == "A5" else 125)]
        # Extractors can merge separate horizontal fields into ONE SPAN when their
        # font/baseline matches. Partition actual glyphs by physical MRT rectangles.
        header_chars = [char for block in page.get_text("rawdict")['blocks']
                        for line in block.get('lines', []) for span in line['spans'] for char in span['chars']
                        if normal(char['c']) and char['bbox'][0] < (200 if size == "A5" else 290)
                        and char['bbox'][1] < (90 if size == "A5" else 125)]
        assigned = set()
        for field in item['fields']:
            eps = .15
            matches = [(index, char) for index, char in enumerate(header_chars)
                       if field['left']-eps <= (char['bbox'][0]+char['bbox'][2])/2 <= field['left']+field['width']+eps
                       and field['top']-eps <= (char['bbox'][1]+char['bbox'][3])/2 <= field['top']+field['height']+eps]
            chars = [char for _, char in matches]
            check(normal(''.join(char['c'] for char in chars)) == field['value'],
                  label + '/' + field['field'] + ": complete value and digit order")
            assigned.update(index for index, _ in matches)
            check(max(char['origin'][1] for char in chars)-min(char['origin'][1] for char in chars) < .1,
                  label + '/' + field['field'] + ": one baseline only, no wrapping")
            box = (min(c['bbox'][0] for c in chars), min(c['bbox'][1] for c in chars),
                   max(c['bbox'][2] for c in chars), max(c['bbox'][3] for c in chars))
            check(box[0] >= field['left'] - eps and box[2] <= field['left'] + field['width'] + eps,
                  label + '/' + field['field'] + ": glyphs inside MRT horizontal bounds")
            check(box[1] >= field['top'] - eps and box[3] <= field['top'] + field['height'] + eps,
                  label + '/' + field['field'] + ": glyphs inside MRT vertical bounds")
            if page_number:
                check(max(abs(a-b) for a, b in zip(first_boxes[field['field']], box)) < .1,
                      label + '/' + field['field'] + ": stable continuation-page metadata")
            else:
                first_boxes[field['field']] = box
            # A full white plaque, not a text-sized fill: no blue dotted ink may remain
            # behind the black value inside its rectangle.
            plaque = crop(page_image, (field['left']+.5, field['top']+.5,
                                field['left']+field['width']-.5, field['top']+field['height']-.5))
            red, green, blue = plaque.split()
            colour_difference = ImageChops.lighter(ImageChops.difference(red, green),
                ImageChops.lighter(ImageChops.difference(red, blue), ImageChops.difference(green, blue)))
            check(colour_difference.getextrema()[1] <= 3,
                  label + '/' + field['field'] + ": dotted artwork cannot show through the value")
        check(len(assigned) == len(header_chars), label + ": every metadata glyph belongs to one of the three MRT boxes")
        for index, rectangle in enumerate(CAPTIONS[size]):
            actual = crop(page_image, rectangle)
            expected = crop(reference_rasters[size], rectangle)
            check(actual.size == expected.size and actual.tobytes() == expected.tobytes(),
                  label + f": printed caption/colon {index+1} is pixel-identical to untouched artwork")
        logo = (350, 20, 570, 118) if size == "A4" else (245, 15, 402, 84)
        check(crop(page_image, logo).tobytes() == crop(reference_rasters[size], logo).tobytes(),
              label + ": company logo/name artwork is unchanged")
        if page_number == 0:
            footer = (0, 762, 595, 842) if size == "A4" else (0, 539, 420, 595)
            check(crop(page_image, footer).tobytes() == crop(reference_rasters[size], footer).tobytes(),
                  label + ": full footer artwork is unchanged")
        images = [i for i in page.get_images(full=True) if list(i[2:4]) == ARTWORK[size]['pixels']]
        check(len(images) == 1, label + ": native 300-DPI artwork dimensions retained")
        pixmap = pymupdf.Pixmap(document, images[0][0])
        check(pixmap.n == 3 and hashlib.sha256(pixmap.samples).hexdigest() == ARTWORK[size]['pixel_sha256'],
              label + ": every artwork RGB pixel survives into the PDF without recompression loss")
        body = [(text, box) for text, box in spans if (text, box) not in header]
        check(bool(body), label + ": flowing body is present")
        body_top = 116.64 if size == "A5" else 165.6
        footer_reserve = 68 if size == "A5" else 97
        # Font ascender metrics may extend up to 1.2pt above the flow origin;
        # this still leaves over 30pt clear below the new metadata row.
        check(all(box[1] >= body_top - 1.5 for _, box in body), label + ": body stays below header artwork/values")
        check(all(box[3] <= page.rect.height - footer_reserve + 1 for _, box in body),
              label + ": body/copies/signers stay above the footer")
    text = compact(' '.join(all_text))
    check(compact("ابتدای متن آزمایشی") in text and compact("پایان متن آزمایشی") in text,
          item['file'] + ": first and final body markers retained")
    if item['signersAndCopies']:
        for expected in ("بایگانی آزمایشی", "واحد کنترل کیفیت", "امضاکننده آزمایشی ۱", "امضاکننده آزمایشی ۲", "امضاکننده آزمایشی ۳"):
            check(compact(expected) in text, item['file'] + ": copy/signer retained: " + expected)
    print(f"PASS: {item['file']} ({len(document)} pages; values, geometry, captions and native image pixels verified)")
    document.close()
for reference in references.values():
    reference.close()
print(f"ALL {checks} ARIA PDF VALUE/GEOMETRY/IMAGE ASSERTIONS PASSED across {len(MANIFEST)} letters.")
