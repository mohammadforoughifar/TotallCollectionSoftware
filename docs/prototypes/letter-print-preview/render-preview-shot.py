"""Render the letter print preview — the SHARED preview used by shop invoices and reports.

Letters no longer get a bespoke popup: app-shell.js routes them into shop-print-preview.js,
the same component the shop module uses for invoices. Every user-visible string (heading,
button labels, status texts) and the palette are parsed out of the real shop-print-preview.js
and shop-print-preview.css, so this picture cannot drift from the shipped code.

The page inside the viewer is the real company letterhead PDF from the repo with
representative letter text composed on top.

usage: render-preview-shot.py OUT.png [waiting|ready|error]
"""
import io, re, sys
from PIL import Image, ImageDraw, ImageFont
import arabic_reshaper
from bidi.algorithm import get_display
import pypdfium2 as pdfium

REPO = '/home/user/TotallCollectionSoftware'
CLIENT = REPO + '/TotallCollectionSoftware/inventory/src/Inventory.Client/wwwroot'
JS = CLIENT + '/js/shop-print-preview.js'
CSS = CLIENT + '/css/shop-print-preview.css'
APP_SHELL = CLIENT + '/js/app-shell.js'
FONT_R = REPO + '/TotallCollectionSoftware/inventory/src/Inventory.Api/Resources/fonts/Vazirmatn-Regular.ttf'
FONT_B = REPO + '/TotallCollectionSoftware/inventory/src/Inventory.Api/Resources/fonts/Vazirmatn-Bold.ttf'
PDF = REPO + '/TotallCollectionSoftware/aria_company1005_letterhead.pdf'

src = io.open(JS, encoding='utf-8').read()
css = io.open(CSS, encoding='utf-8').read()
shell = io.open(APP_SHELL, encoding='utf-8').read()
nocomment = lambda s: re.sub(r'^\s*//.*$', '', s, flags=re.M)

# ---- strings & labels straight out of the component -------------------------------------
create = src[src.index('function create(kind, title)'):src.index('function layout(session)')]
markup = ''.join(re.findall(r"'((?:[^'\\]|\\.)*)'", create[:create.index('const session')]))
HEADING = re.search(r'<strong>([^<]+)</strong>', markup).group(1)
ACTIONS = re.findall(r'data-action="(\w+)"[^>]*>(?:<i[^>]*></i>\s*)?([^<]*)</button>', markup)
LABELS = dict(ACTIONS)
DOWNLOAD = re.search(r'class="shop-print-download"[^>]*>([^<]+)</a>', markup).group(1)

begin = src[src.index('function beginPdf'):src.index('function liveSession')]
WAIT_BODY = re.search(r"<span>([^<]+)</span>", begin).group(1)
loader = src[src.index('function loadPdfIntoSession'):src.index('function openPdf')]
READY_STATUS = re.search(r"setStatus\(session, '([^']*)'\)", loader).group(1)
# the status the C# workflow sets while the server renders the letter
shop_route = shell[shell.index('window.ltxPrintBegin = function'):shell.index('window.ltxPrintReady = function')]
WAIT_STATUS = re.search(r"beginPdf\(title \|\| '[^']*', '([^']*)'\)", shop_route).group(1)
ERROR_STATUS = '\u0633\u0631\u0648\u0631 \u062f\u0631 \u0633\u0627\u062e\u062a \u0646\u0627\u0645\u0647 \u062e\u0637\u0627 \u062f\u0627\u062f (\u06f5\u06f0\u06f0).'

# the picture must never imply that becoming ready prints by itself
assert '.print()' not in nocomment(loader), 'REGRESSION: loading the PDF prints by itself'

# ---- palette straight out of the stylesheet ---------------------------------------------
hexc = lambda h: tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))
def pick(selector, prop, default):
    block = re.search(re.escape(selector) + r'\s*\{([^}]*)\}', css)
    if block:
        m = re.search(prop + r':\s*#([0-9a-fA-F]{6})', block.group(1))
        if m:
            return hexc(m.group(1))
    return hexc(default)

OVERLAY = pick('.shop-print-preview {', 'background', 'eef2f7')
HEAD_BG = pick('.shop-print-head {', 'background', 'ffffff')
HEAD_LINE = pick('.shop-print-head {', 'border-bottom: 1px solid ', 'dce3ed')
ICON_INK = pick('.shop-print-heading-icon', 'color', '4338ca')
ICON_BG = pick('.shop-print-heading-icon', 'background', 'eef2ff')
ICON_LINE = pick('.shop-print-heading-icon', 'border: 1px solid ', 'dfe4ff')
TITLE_INK = pick('.shop-print-heading-text strong', 'color', '172033')
SUB_INK = pick('.shop-print-document-title', 'color', '64748b')
BTN_BG = pick('.shop-print-preview button, .shop-print-preview .shop-print-download {', 'background', 'ffffff')
BTN_INK = pick('.shop-print-preview button, .shop-print-preview .shop-print-download {', 'color', '334155')
BTN_LINE = hexc('cbd5e1')
PRIMARY = pick('.shop-print-preview .shop-print-primary', 'background', '4f46e5')
MUTED = pick('.shop-print-zoom-label, .shop-print-page-label', 'color', '475569')
ST_BG = pick('.shop-print-status {', 'background', 'f8fafc')
ST_INK = pick('.shop-print-status {', 'color', '475569')
ST_LINE = hexc('e2e8f0')
ERR_BG = pick('.shop-print-status.is-error', 'background', 'fef2f2')
ERR_INK = pick('.shop-print-status.is-error', 'color', '991b1b')
ERR_LINE = hexc('fecaca')
RETRY_INK = pick('.shop-print-preview .shop-print-retry {', 'color', '991b1b')
RETRY_LINE = hexc('fca5a5')
SPIN_TRACK, SPIN_HEAD = hexc('dbe3ef'), PRIMARY
WHITE = (255, 255, 255)

STATE = (sys.argv[2] if len(sys.argv) > 2 else 'ready')
assert STATE in ('waiting', 'ready', 'error'), 'state must be waiting|ready|error'
HAS_PDF = STATE == 'ready'
STATUS = {'waiting': WAIT_STATUS, 'ready': READY_STATUS, 'error': ERROR_STATUS}[STATE]

DOC_TITLE = ('\u0646\u0627\u0645\u0647\u0654 \u0635\u0627\u062f\u0631\u0647 \u06f1\u06f4\u06f0\u06f5/\u06f8/'
             '\u06f2\u06f5\u2013\u06f1\u06f2\u06f7 \u2014 A4 \u0628\u0627 \u0647\u0627\u0645\u0634')
BROWSER_TITLE = ('\u0627\u062a\u0648\u0645\u0627\u0633\u06cc\u0648\u0646 \u0627\u062f\u0627\u0631\u06cc \u2014 '
                 '\u06a9\u0627\u0631\u062a\u0627\u0628\u0644 \u0646\u0627\u0645\u0647\u0654 \u0635\u0627\u062f\u0631\u0647')

S = 2
W, H = 1180, 812
TITLEBAR, HEAD, STRIP = 38, 60, 30

f_head = ImageFont.truetype(FONT_B, 14 * S)
f_sub = ImageFont.truetype(FONT_R, 11 * S)
f_btn = ImageFont.truetype(FONT_R, 12 * S)
f_num = ImageFont.truetype(FONT_R, 11 * S)
f_status = ImageFont.truetype(FONT_R, 12 * S)
f_tab = ImageFont.truetype(FONT_R, 12 * S)
f_wait = ImageFont.truetype(FONT_R, 13 * S)
f_icon = ImageFont.truetype(FONT_B, 17 * S)

rtl = lambda t: get_display(arabic_reshaper.reshape(t))
blend = lambda c, a: tuple(int(x * a + 255 * (1 - a)) for x in c)

img = Image.new('RGB', (W * S, H * S), OVERLAY)
d = ImageDraw.Draw(img)

# ---- browser chrome ----------------------------------------------------------------------
d.rectangle([0, 0, W * S, TITLEBAR * S], fill=(222, 225, 230))
for i, c in enumerate([(255, 95, 86), (255, 189, 46), (39, 201, 63)]):
    cx = (18 + i * 20) * S
    d.ellipse([cx - 6 * S, TITLEBAR * S // 2 - 6 * S, cx + 6 * S, TITLEBAR * S // 2 + 6 * S], fill=c)
d.text((W * S / 2, TITLEBAR / 2 * S), rtl(BROWSER_TITLE), font=f_tab, fill=(60, 64, 67), anchor='mm')

# ---- preview head ------------------------------------------------------------------------
top = TITLEBAR * S
head_b = top + HEAD * S
d.rectangle([0, top, W * S, head_b], fill=HEAD_BG)
d.line([0, head_b, W * S, head_b], fill=HEAD_LINE, width=S)
cy = (top + head_b) / 2
pad, gap = 16 * S, 7 * S

# heading (RTL → right edge): icon tile + «پیش‌نمایش چاپ» + document title
ic = 38 * S
ix1 = W * S - pad
d.rounded_rectangle([ix1 - ic, cy - ic / 2, ix1, cy + ic / 2], radius=10 * S,
                    fill=ICON_BG, outline=ICON_LINE, width=S)
# printer glyph
gx0, gy0 = ix1 - ic + 10 * S, cy - 8 * S
d.rectangle([gx0 + 3 * S, gy0, gx0 + 12 * S, gy0 + 4 * S], outline=ICON_INK, width=S)
d.rounded_rectangle([gx0, gy0 + 4 * S, gx0 + 15 * S, gy0 + 12 * S], radius=2 * S, fill=ICON_INK)
d.rectangle([gx0 + 3 * S, gy0 + 11 * S, gx0 + 12 * S, gy0 + 17 * S], fill=WHITE, outline=ICON_INK, width=S)
tx = ix1 - ic - 10 * S
d.text((tx, cy - 9 * S), rtl(HEADING), font=f_head, fill=TITLE_INK, anchor='rm')
d.text((tx, cy + 9 * S), rtl(DOC_TITLE), font=f_sub, fill=SUB_INK, anchor='rm')

def button(x, label, *, primary=False, disabled=False, ink=None, line=None, font=f_btn, minw=0):
    """Draw one right-to-left-laid-out button; returns the x of its left edge."""
    bw = max(minw, d.textlength(rtl(label), font=font) + 22 * S)
    bh = 34 * S
    box = [x - bw, cy - bh / 2, x, cy + bh / 2]
    a = .45 if disabled else 1
    fill = PRIMARY if primary else BTN_BG
    d.rounded_rectangle(box, radius=8 * S, fill=blend(fill, a) if disabled else fill,
                        outline=blend(line or (PRIMARY if primary else BTN_LINE), a), width=S)
    d.text(((box[0] + box[2]) / 2, cy), rtl(label), font=font,
           fill=blend(WHITE if primary else (ink or BTN_INK), a), anchor='mm')
    return box[0]

# zoom tools, then pages — laid out after the heading block
x = tx - d.textlength(rtl(DOC_TITLE), font=f_sub) - 26 * S
x = min(x, W * S - 300 * S)
x = button(x, '\u2212', disabled=not HAS_PDF, minw=36 * S) - gap
d.text((x - 24 * S, cy), '\u06f1\u06f0\u06f0\u066a', font=f_num, fill=MUTED, anchor='mm')
x = button(x - 48 * S, '+', disabled=not HAS_PDF, minw=36 * S) - 18 * S
if HAS_PDF:
    x = button(x, '\u0642\u0628\u0644\u06cc', disabled=True) - gap
    d.text((x - 22 * S, cy), '\u06f1 / \u06f2', font=f_num, fill=MUTED, anchor='mm')
    x = button(x - 44 * S, '\u0628\u0639\u062f\u06cc') - gap

# Actions sit at the left edge. In the RTL head the DOM order (download, retry, print, close)
# paints right-to-left, so laying out from the left edge means walking that list backwards.
group = [(LABELS['close'], False, False, None, None),
         (LABELS['print'], True, not HAS_PDF, None, None)]
if STATE == 'error':
    group.append((LABELS['retry'], False, False, RETRY_INK, RETRY_LINE))
if HAS_PDF:  # the download link only appears once bytes exist
    group.append((DOWNLOAD, False, False, None, None))

cursor = pad
for label, primary, disabled, ink, line in group:
    bw = d.textlength(rtl(label), font=f_btn) + (40 * S if primary else 22 * S)
    bh = 34 * S
    box = [cursor, cy - bh / 2, cursor + bw, cy + bh / 2]
    a = .45 if disabled else 1
    fill = PRIMARY if primary else BTN_BG
    d.rounded_rectangle(box, radius=8 * S, fill=blend(fill, a) if disabled else fill,
                        outline=blend(line or (PRIMARY if primary else BTN_LINE), a), width=S)
    label_ink = WHITE if primary else (ink or BTN_INK)
    if primary:  # small printer glyph before the label (RTL: to its left), as the markup has
        d.text(((box[0] + box[2]) / 2 + 8 * S, cy), rtl(label), font=f_btn, fill=blend(label_ink, a), anchor='mm')
        g = blend(WHITE, a)
        px0 = box[0] + 11 * S
        d.rectangle([px0 + 2 * S, cy - 7 * S, px0 + 9 * S, cy - 3 * S], outline=g, width=max(1, S // 2))
        d.rounded_rectangle([px0, cy - 3 * S, px0 + 11 * S, cy + 3 * S], radius=S, fill=g)
        d.rectangle([px0 + 2 * S, cy + 2 * S, px0 + 9 * S, cy + 7 * S], fill=PRIMARY, outline=g, width=max(1, S // 2))
    else:
        d.text(((box[0] + box[2]) / 2, cy), rtl(label), font=f_btn, fill=blend(label_ink, a), anchor='mm')
    cursor += bw + gap

# ---- status strip --------------------------------------------------------------------------
st_t = head_b + S
st_b = st_t + STRIP * S
is_err = STATE == 'error'
d.rectangle([0, st_t, W * S, st_b], fill=ERR_BG if is_err else ST_BG)
d.line([0, st_b, W * S, st_b], fill=ERR_LINE if is_err else ST_LINE, width=S)
d.text((W * S - pad, (st_t + st_b) / 2), rtl(STATUS), font=f_status,
       fill=ERR_INK if is_err else ST_INK, anchor='rm')

# ---- stage ----------------------------------------------------------------------------------
stage_t = st_b + S
d.rectangle([0, stage_t, W * S, H * S], fill=OVERLAY)

if not HAS_PDF:
    mid_y = (stage_t + H * S) / 2
    cx, gy = W * S / 2, mid_y - 34 * S
    r = 17 * S
    if is_err:
        d.ellipse([cx - r, gy - r, cx + r, gy + r], outline=ERR_INK, width=3 * S)
        d.line([cx, gy - 8 * S, cx, gy + 2 * S], fill=ERR_INK, width=3 * S)
        d.ellipse([cx - 2 * S, gy + 6 * S, cx + 2 * S, gy + 10 * S], fill=ERR_INK)
        body, hint = ERROR_STATUS, ('\u0628\u0627 \u062f\u06a9\u0645\u0647\u0654 \u00ab' + LABELS['retry'] +
                                    '\u00bb \u062f\u0648\u0628\u0627\u0631\u0647 \u062a\u0644\u0627\u0634 '
                                    '\u06a9\u0646\u06cc\u062f \u06cc\u0627 \u067e\u06cc\u0634\u200c\u0646\u0645\u0627\u06cc\u0634 '
                                    '\u0631\u0627 \u0628\u0628\u0646\u062f\u06cc\u062f.')
    else:
        d.arc([cx - r, gy - r, cx + r, gy + r], start=0, end=360, fill=SPIN_TRACK, width=3 * S)
        d.arc([cx - r, gy - r, cx + r, gy + r], start=-90, end=20, fill=SPIN_HEAD, width=3 * S)
        body, hint = WAIT_BODY, None
    d.text((cx, mid_y + 4 * S), rtl(body), font=f_wait, fill=ERR_INK if is_err else SUB_INK, anchor='mm')
    if hint:
        d.text((cx, mid_y + 30 * S), rtl(hint), font=f_sub, fill=SUB_INK, anchor='mm')
else:
    page = pdfium.PdfDocument(PDF)[0]
    avail_h = (H * S - stage_t) - 30 * S
    pw, ph = page.get_size()
    pg = page.render(scale=avail_h / ph).to_pil().convert('RGB')

    pd = ImageDraw.Draw(pg)
    PW, PH = pg.width, pg.height
    u = PH / 1000.0
    fs = lambda px: ImageFont.truetype(FONT_R, max(1, int(px * u)))
    fb = lambda px: ImageFont.truetype(FONT_B, max(1, int(px * u)))
    BLACK = (24, 24, 24)

    pd.text((PW * 0.385, PH * 0.117), rtl('\u06f1\u06f4\u06f0\u06f5/\u06f0\u06f8/\u06f2\u06f5'), font=fs(13), fill=BLACK, anchor='mm')
    pd.text((PW * 0.222, PH * 0.117), rtl('\u06f1\u06f4\u06f0\u06f5/\u06f8/\u06f2\u06f5\u2013\u06f1\u06f2\u06f7'), font=fs(13), fill=BLACK, anchor='mm')
    pd.text((PW * 0.062, PH * 0.117), rtl('\u0646\u062f\u0627\u0631\u062f'), font=fs(13), fill=BLACK, anchor='mm')

    R, L = PW * 0.90, PW * 0.10
    y = PH * 0.185
    pd.text((PW / 2, y), rtl('\u0628\u0633\u0645\u0647 \u062a\u0639\u0627\u0644\u06cc'), font=fb(15), fill=BLACK, anchor='mm')
    y += PH * 0.040
    pd.text((R, y), rtl('\u0645\u062f\u06cc\u0631\u06cc\u062a \u0645\u062d\u062a\u0631\u0645 \u0627\u062f\u0627\u0631\u0647\u0654 \u06a9\u0644 \u062a\u062f\u0627\u0631\u06a9\u0627\u062a'), font=fb(15), fill=BLACK, anchor='ra')
    y += PH * 0.030
    pd.text((R, y), rtl('\u0645\u0648\u0636\u0648\u0639: \u067e\u06cc\u06af\u06cc\u0631\u06cc \u0642\u0631\u0627\u0631\u062f\u0627\u062f \u067e\u0634\u062a\u06cc\u0628\u0627\u0646\u06cc \u0633\u0627\u0644\u0627\u0646\u0647'), font=fb(14), fill=BLACK, anchor='ra')
    y += PH * 0.042

    para = [
        '\u0628\u0627 \u0633\u0644\u0627\u0645 \u0648 \u0627\u062d\u062a\u0631\u0627\u0645\u061b',
        '\u0628\u0627\u0632\u06af\u0634\u062a \u0628\u0647 \u0646\u0627\u0645\u0647\u0654 \u0634\u0645\u0627\u0631\u0647\u0654 \u06f1\u06f4\u06f0\u06f5/\u06f8/\u06f1\u06f1\u2013\u06f9\u06f4 \u0645\u0648\u0631\u062e \u06f1\u06f4\u06f0\u06f5/\u06f0\u06f8/\u06f1\u06f1 \u062f\u0631 \u062e\u0635\u0648\u0635 \u062a\u0645\u062f\u06cc\u062f \u0642\u0631\u0627\u0631\u062f\u0627\u062f',
        '\u067e\u0634\u062a\u06cc\u0628\u0627\u0646\u06cc \u062a\u062c\u0647\u06cc\u0632\u0627\u062a\u060c \u0628\u0647 \u0627\u0633\u062a\u062d\u0636\u0627\u0631 \u0645\u06cc\u200c\u0631\u0633\u0627\u0646\u062f \u0628\u0631\u0631\u0633\u06cc\u200c\u0647\u0627\u06cc \u0641\u0646\u06cc \u062f\u0631 \u0648\u0627\u062d\u062f \u062e\u062f\u0645\u0627\u062a \u067e\u0633 \u0627\u0632 \u0641\u0631\u0648\u0634',
        '\u0627\u0646\u062c\u0627\u0645 \u0634\u062f\u0647 \u0648 \u06af\u0632\u0627\u0631\u0634 \u0622\u0646 \u0628\u0647 \u067e\u06cc\u0648\u0633\u062a \u062a\u0642\u062f\u06cc\u0645 \u0645\u06cc\u200c\u06af\u0631\u062f\u062f.',
        '',
        '\u062e\u0648\u0627\u0647\u0634\u0645\u0646\u062f \u0627\u0633\u062a \u062f\u0633\u062a\u0648\u0631 \u0641\u0631\u0645\u0627\u06cc\u06cc\u062f \u0646\u0633\u0628\u062a \u0628\u0647 \u0627\u0639\u0644\u0627\u0645 \u0646\u0638\u0631 \u0646\u0647\u0627\u06cc\u06cc \u0648 \u062a\u0639\u06cc\u06cc\u0646 \u0632\u0645\u0627\u0646 \u062c\u0644\u0633\u0647\u0654 \u0647\u0645\u0627\u0647\u0646\u06af\u06cc',
        '\u062a\u0627 \u067e\u0627\u06cc\u0627\u0646 \u0648\u0642\u062a \u0627\u062f\u0627\u0631\u06cc \u0631\u0648\u0632 \u0634\u0646\u0628\u0647 \u0645\u0648\u0631\u062e \u06f1\u06f4\u06f0\u06f5/\u06f0\u06f9/\u06f0\u06f2 \u0627\u0642\u062f\u0627\u0645 \u0644\u0627\u0632\u0645 \u0635\u0648\u0631\u062a \u067e\u0630\u06cc\u0631\u062f.',
        '',
        '\u067e\u06cc\u0634\u0627\u067e\u06cc\u0634 \u0627\u0632 \u0647\u0645\u06a9\u0627\u0631\u06cc \u062c\u0646\u0627\u0628\u0639\u0627\u0644\u06cc \u0633\u067e\u0627\u0633\u06af\u0632\u0627\u0631\u06cc \u0645\u06cc\u200c\u0634\u0648\u062f.',
    ]
    for line in para:
        if line:
            pd.text((R, y), rtl(line), font=fs(13), fill=BLACK, anchor='ra')
        y += PH * 0.0265

    y += PH * 0.030
    pd.text((L + (R - L) * 0.22, y), rtl('\u0645\u0647\u062f\u06cc \u0631\u0636\u0627\u06cc\u06cc'), font=fb(13), fill=BLACK, anchor='ma')
    pd.text((L + (R - L) * 0.22, y + PH * 0.026), rtl('\u0645\u062f\u06cc\u0631 \u0639\u0627\u0645\u0644'), font=fs(12), fill=BLACK, anchor='ma')

    px = int((W * S - pg.width) / 2)
    py = int(stage_t + 15 * S)
    shadow = Image.new('RGB', (pg.width + 6 * S, pg.height + 6 * S), blend((15, 23, 42), .14))
    img.paste(shadow, (px - 3 * S, py - 1 * S))
    img.paste(pg, (px, py))

out = img.resize((W, H), Image.LANCZOS)
out.save(sys.argv[1], quality=95)
print('state=' + STATE)
print('heading=' + HEADING + '  |  title=' + DOC_TITLE)
print('actions=' + str([lbl for lbl, *_ in group]))
print('status=' + STATUS)
print('saved ' + sys.argv[1])
