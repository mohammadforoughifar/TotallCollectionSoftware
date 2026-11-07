"""پیش‌پردازش تصویر پیش از OCR."""
from __future__ import annotations

import io

from PIL import Image, ImageEnhance, ImageOps

MAX_SIDE = 2600          # برای ورودی‌های خیلی بزرگ (اسکن A3 و ...) کاهش می‌دهیم
TESS_SCALE_MIN_SIDE = 1400  # اگر کوچک‌تر بود بزرگ‌نمایی می‌کنیم (بهبود tesseract)


def load_image(data: bytes) -> Image.Image:
    img = Image.open(io.BytesIO(data))
    img.load()
    if img.mode not in ("RGB", "L"):
        img = img.convert("RGB")
    return img


def meaningful_chars(text: str) -> int:
    return sum(1 for ch in text if ch.isalnum())


def _resize_if_needed(img: Image.Image, target_min: int, max_side: int) -> Image.Image:
    w, h = img.size
    long_side = max(w, h)
    if long_side > max_side:
        k = max_side / long_side
        return img.resize((max(1, int(w * k)), max(1, int(h * k))), Image.LANCZOS)
    if long_side < target_min:
        k = target_min / long_side
        return img.resize((max(1, int(w * k)), max(1, int(h * k))), Image.LANCZOS)
    return img


def prepare_for_tesseract(data: bytes) -> Image.Image:
    img = load_image(data)
    img = _resize_if_needed(img, TESS_SCALE_MIN_SIDE, MAX_SIDE)
    # سیاه‌سفید + افزایش کنتراست برای tesseract
    gray = ImageOps.grayscale(img)
    gray = ImageOps.autocontrast(gray, cutoff=1)
    gray = ImageEnhance.Sharpness(gray).enhance(1.2)
    return gray


def prepare_for_easyocr(data: bytes) -> Image.Image:
    img = load_image(data)
    # فقط تصحیح اندازه؛ موتور عصبی بقیهٔ کارها را خودش انجام می‌دهد
    return _resize_if_needed(img, 0, MAX_SIDE)
