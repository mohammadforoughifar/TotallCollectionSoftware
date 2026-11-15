#!/usr/bin/env python3
"""ساخت نمونهٔ تصویر/PDF فارسی برای تست سرویس OCR (فقط توسعه).

نیازمندی:  pip install pillow
(در Pillow دارای libraqm متن منطقی مستقیم داده می‌شود؛ وگرنه arabic_reshaper/python-bidi لازم است)
خروجی:     samples/persian_sample.png , samples/scanned_persian.pdf , samples/digital_en.pdf
"""
from __future__ import annotations

import io
import os
from typing import List

from PIL import Image, ImageDraw, ImageFont, features

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "samples")
FONT = os.environ.get("VAZIR_FONT", "/home/user/.cache/ocr-font/Vazirmatn-Regular.ttf")

HAS_RAQM = features.check("raqm")

LINES = [
    "قرارداد پیمانکاری شماره ۱۴۰۵-۰۲",
    "شرکت مدیریت انبار، خرید و فروش",
    "این سند نمونه برای آزمون سامانه تشخیص متن ساخته شده است",
    "تهران - خیابان آزادی - پلاک ۱۲",
    "مبلغ قرارداد: ۹۸۵،۰۰۰،۰۰۰ ریال",
]


def _font(size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(FONT, size)


def _to_drawable(text: str) -> str:
    """برای raqm متن منطقی کافی است؛ بدون raqm باید خودمان شکل‌دهی و ترتیب بصری بدهیم."""
    if HAS_RAQM:
        return text
    import arabic_reshaper
    from bidi.algorithm import get_display

    return get_display(arabic_reshaper.reshape(text))


def render_page(draw_lines: List[str], font_size: int = 40, width: int = 1750, height: int = 660) -> Image.Image:
    img = Image.new("RGB", (width, height), "white")
    d = ImageDraw.Draw(img)
    # خط قرمز سربرگ مانند نامهٔ رسمی
    d.rectangle([0, 0, width, 6], fill=(160, 30, 30))
    y = 66
    for i, line in enumerate(draw_lines):
        text = _to_drawable(line)
        size = font_size if i != 0 else font_size + 12
        d.text((width - 70, y), text, font=_font(size), fill=(15, 15, 15), anchor="ra")
        y += size + 36
    return img


def main() -> None:
    os.makedirs(OUT, exist_ok=True)
    img = render_page(LINES)
    img.save(os.path.join(OUT, "persian_sample.png"))

    # PDF «اسکن‌شده» = فقط تصویر (بدون لایهٔ متنی)
    img.save(os.path.join(OUT, "scanned_persian.pdf"), "PDF", resolution=200)

    # PDF «دیجیتال انگلیسی» با لایهٔ متنی برای تست مسیر pdf-text
    try:
        import pypdf
        from reportlab.pdfgen import canvas  # type: ignore
    except ImportError:
        print("reportlab نصب نیست؛ نمونهٔ دیجیتال ساخته نشد (اختیاری).")
        return
    buf = io.BytesIO()
    c = canvas.Canvas(buf)
    c.drawString(80, 760, "Digital PDF text layer for testing extraction.")
    c.drawString(80, 740, "Totall OCR Service contract sample No. 1405-02.")
    c.save()
    buf.seek(0)
    with open(os.path.join(OUT, "digital_en.pdf"), "wb") as f:
        f.write(buf.read())
    print("samples ready:", sorted(os.listdir(OUT)))


if __name__ == "__main__":
    main()
