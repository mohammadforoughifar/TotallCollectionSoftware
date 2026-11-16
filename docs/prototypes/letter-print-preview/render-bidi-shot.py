#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Before/after of the inner-letter body bidi fix.

Pillow has no raqm here, so Arabic shaping is done with arabic_reshaper and the
visual order with python-bidi — the same Unicode Bidi Algorithm QuestPDF applies.

  BEFORE  .AlignRight()                               -> base direction LTR
  AFTER   .AlignRight().DirectionFromRightToLeft()    -> base RTL + RLM-wrapped text

Usage: render-bidi-shot.py OUT.png
"""
import os
import re
import sys

import arabic_reshaper
from bidi.algorithm import get_display
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = os.path.normpath(os.path.join(
    HERE, "../../../TotallCollectionSoftware/inventory/src/Inventory.Api/Resources/fonts"))

RLM, LRI, PDI = "\u200F", "\u2066", "\u2069"
LATIN = re.compile(r"[A-Za-z][A-Za-z0-9_@.\-]*[A-Za-z0-9]|[A-Za-z]")

BODY = [
    "\u0628\u0627 \u0633\u0644\u0627\u0645 \u0648 \u0627\u062d\u062a\u0631\u0627\u0645\u060c",
    "\u0645\u0648\u0627\u0631\u062f \u0632\u06cc\u0631 \u062f\u0631 \u0633\u0627\u0645\u0627\u0646\u0647 \u06af\u0632\u0627\u0631\u0634 \u0634\u062f\u0647 \u0627\u0633\u062a.",
    "\u06f1 \u2014 \u067e\u0631 \u0646\u0634\u062f\u0646 \u062e\u0648\u062f\u06a9\u0627\u0631 \u0642\u06cc\u0645\u062a \u0648\u0627\u062d\u062f \u062f\u0631 \u0641\u0627\u06a9\u062a\u0648\u0631 \u0641\u0631\u0648\u0634",
    "\u06f2 \u2014 \u062e\u0637\u0627\u06cc \u0645\u0627\u0698\u0648\u0644 Workflow \u0647\u0646\u06af\u0627\u0645 \u062b\u0628\u062a \u0633\u0646\u062f.",
    "\u062e\u0648\u0627\u0647\u0634\u0645\u0646\u062f \u0627\u0633\u062a \u062f\u0633\u062a\u0648\u0631 \u0628\u0631\u0631\u0633\u06cc \u0635\u0627\u062f\u0631 \u0641\u0631\u0645\u0627\u06cc\u06cc\u062f.",
]


def rtl_paragraph(line):
    v = line.strip()
    return "" if not v else RLM + LATIN.sub(lambda m: LRI + m.group(0) + PDI, v) + RLM


def visual(line, base):
    """Shape + reorder exactly as the PDF engine would for the given base direction."""
    sim = line.replace(LRI, "").replace(PDI, "")  # python-bidi is UBA 6.2 (no isolates)
    return get_display(arabic_reshaper.reshape(sim), base_dir=base).replace(RLM, "")


def panel(draw, x, y, w, title, colour, lines, base, wrap):
    font_t = ImageFont.truetype(os.path.join(FONTS, "Vazirmatn-Bold.ttf"), 19)
    font_b = ImageFont.truetype(os.path.join(FONTS, "Vazirmatn-Regular.ttf"), 17)
    draw.rectangle([x, y, x + w, y + 46], fill=colour)
    draw.text((x + 16, y + 12), title, font=font_t, fill="#ffffff")
    draw.rectangle([x, y + 46, x + w, y + 46 + 230], fill="#ffffff", outline="#cbd5e1")
    cy = y + 70
    for raw in lines:
        text = visual(wrap(raw), base)
        tw = draw.textlength(text, font=font_b)
        draw.text((x + w - 20 - tw, cy), text, font=font_b, fill="#0f172a")  # right-aligned
        cy += 38
    return y + 46 + 230


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "inner-letter-bidi.png"
    PAD, PW = 28, 562
    W = PAD * 3 + PW * 2
    img = Image.new("RGB", (W, 372), "#f1f5f9")
    d = ImageDraw.Draw(img)
    font_h = ImageFont.truetype(os.path.join(FONTS, "Vazirmatn-Bold.ttf"), 15)

    panel(d, PAD, 56, PW, "BEFORE  —  AlignRight()  \u2192  base LTR",
          "#b91c1c", BODY, "L", lambda s: s)
    panel(d, PAD * 2 + PW, 56, PW, "AFTER  —  + DirectionFromRightToLeft() + RLM",
          "#15803d", BODY, "R", rtl_paragraph)

    d.text((PAD, 20), visual("\u0686\u0627\u067e \u0646\u0627\u0645\u0647\u0654 \u062f\u0627\u062e\u0644\u06cc \u2014 "
                             "\u062c\u0627\u0628\u0647\u200c\u062c\u0627\u06cc\u06cc \u0646\u0642\u0637\u0647 \u0648 "
                             "\u0634\u0645\u0627\u0631\u0647\u0654 \u0628\u0646\u062f", "R"),
           font=font_h, fill="#334155")
    img.save(out)
    print("wrote", out)


if __name__ == "__main__":
    main()
