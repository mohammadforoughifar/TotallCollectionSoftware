"""Render the letter print PREVIEW window exactly as app-shell.js builds it.

Every user-visible string (button labels, status texts, which buttons are shown) and the
colours/metrics are parsed out of the real app-shell.js, so this picture cannot drift from
the shipped code. The page inside the viewer is the real company letterhead PDF from the
repo with representative letter text composed on top.

usage: render_preview.py OUT.png [ready|printing]
"""
import io, re, sys
from PIL import Image, ImageDraw, ImageFont
import arabic_reshaper
from bidi.algorithm import get_display
import pypdfium2 as pdfium

REPO = '/home/user/TotallCollectionSoftware'
JS = REPO + '/TotallCollectionSoftware/inventory/src/Inventory.Client/wwwroot/js/app-shell.js'
FONT_R = REPO + '/TotallCollectionSoftware/inventory/src/Inventory.Api/Resources/fonts/Vazirmatn-Regular.ttf'
FONT_B = REPO + '/TotallCollectionSoftware/inventory/src/Inventory.Api/Resources/fonts/Vazirmatn-Bold.ttf'
PDF = REPO + '/TotallCollectionSoftware/aria_company1005_letterhead.pdf'

src = io.open(JS, encoding='utf-8').read()
nocomment = lambda s: re.sub(r'^\s*//.*$', '', s, flags=re.M)

body = re.search(r"function ltxPrintShellMarkup\(\) \{(.*?)\n\}", src, re.S).group(1)
html = "".join(re.findall(r"'((?:[^'\\]|\\.)*)'", body))
BUTTONS = [(bid, label, 'primary' in attrs)
           for bid, attrs, label in re.findall(r'<button id="([^"]+)"([^>]*)>([^<]*)</button>', html)]

ready_src = src[src.index('window.ltxPrintReady'):src.index('window.ltxPrintFail')]
READY_STATUS = re.search(r"ltxSetPrintStatus\(session, '([^']*)'\)", ready_src).group(1)
SHOWN = set(re.findall(r"ltxShowPrintControl\(session, '(\w+)', true\)", ready_src))
assert '.print()' not in nocomment(ready_src), 'REGRESSION: ltxPrintReady prints by itself'

trig_src = src[src.index('function ltxTriggerPrint'):src.index('window.ltxPrintBegin')]
PRINTING_STATUS = re.search(r"ltxSetPrintStatus\(session, '([^']*)'\)", trig_src).group(1)

STATE = (sys.argv[2] if len(sys.argv) > 2 else 'ready')
STATUS = READY_STATUS if STATE == 'ready' else PRINTING_STATUS
DISABLED = {'ltx-print-now'} if STATE == 'printing' else set()

def visible(bid):
    short = bid.replace('ltx-print-', '')
    return short in SHOWN or short == 'close'

FILE_NAME = 'letter-127-A4-\u0628\u0627-\u0647\u0627\u0645\u0634.pdf'

S = 2
W, H = 1180, 812
TITLEBAR, BAR = 40, 58

INK, BTN_BG, BTN_INK = (30, 41, 59), (226, 232, 240), (15, 23, 42)
PRIMARY, LINE, WHITE = (15, 118, 110), (203, 213, 225), (255, 255, 255)
VIEWER = (82, 86, 89)

f_status = ImageFont.truetype(FONT_R, 14 * S)
f_btn = ImageFont.truetype(FONT_R, 14 * S)
f_title = ImageFont.truetype(FONT_R, 13 * S)

rtl = lambda t: get_display(arabic_reshaper.reshape(t))
blend = lambda c, a: tuple(int(x * a + 255 * (1 - a)) for x in c)

img = Image.new('RGB', (W * S, H * S), (222, 225, 230))
d = ImageDraw.Draw(img)

for i, c in enumerate([(255, 95, 86), (255, 189, 46), (39, 201, 63)]):
    cx = (18 + i * 20) * S
    d.ellipse([cx - 6 * S, TITLEBAR * S // 2 - 6 * S, cx + 6 * S, TITLEBAR * S // 2 + 6 * S], fill=c)
d.text((W * S / 2, TITLEBAR / 2 * S), rtl(FILE_NAME), font=f_title, fill=(60, 64, 67), anchor='mm')

top = TITLEBAR * S
bar_b = top + BAR * S
d.rectangle([0, top, W * S, bar_b], fill=WHITE)
d.line([0, bar_b, W * S, bar_b], fill=LINE, width=S)

pad, gap, cy = 14 * S, 8 * S, (top + bar_b) / 2
vis = [(bid, label, prim) for bid, label, prim in BUTTONS if visible(bid)]
widths = [d.textlength(rtl(l), font=f_btn) + 24 * S for _, l, _ in vis]
x = pad
for (bid, label, prim), bw in zip(reversed(vis), reversed(widths)):
    bh = 37 * S
    box = [x, cy - bh / 2, x + bw, cy + bh / 2]
    off = bid in DISABLED
    fill = PRIMARY if prim else BTN_BG
    d.rounded_rectangle(box, radius=6 * S, fill=blend(fill, .6) if off else fill)
    ink = WHITE if prim else BTN_INK
    d.text(((box[0] + box[2]) / 2, cy), rtl(label), font=f_btn,
           fill=blend(ink, .6) if off else ink, anchor='mm')
    x += bw + gap
d.text((W * S - pad, cy), rtl(STATUS), font=f_status, fill=INK, anchor='rm')

vt = bar_b + S
d.rectangle([0, vt, W * S, H * S], fill=VIEWER)

page = pdfium.PdfDocument(PDF)[0]
avail_h = (H * S - vt) - 36 * S
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

R = PW * 0.90
L = PW * 0.10
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
py = int(vt + 18 * S)
d.rectangle([px + 3 * S, py + 4 * S, px + pg.width + 3 * S, py + pg.height + 4 * S], fill=(54, 57, 60))
img.paste(pg, (px, py))

out = img.resize((W, H), Image.LANCZOS)
out.save(sys.argv[1], quality=95)
print('state=' + STATE + '  buttons=' + str([l for _, l, _ in vis]))
print('status=' + STATUS)
print('saved ' + sys.argv[1])
