"""استخراج متن پی‌دی‌اف: لایهٔ متنی (pypdf) و در صورت اسکن‌بودن، رندر صفحه + OCR (pypdfium2)."""
from __future__ import annotations

import io
import logging
import time
from typing import Callable

import pypdf
import pypdfium2 as pdfium

from .config import settings
from .preprocess import meaningful_chars

log = logging.getLogger("ocr.pdf")


def _text_layer(data: bytes) -> str:
    """لایهٔ متنی پی‌دی‌اف دیجیتال را برمی‌گرداند (خالی اگر اسکن است)."""
    parts = []
    try:
        reader = pypdf.PdfReader(io.BytesIO(data))
        for page in reader.pages:
            try:
                parts.append(page.extract_text() or "")
            except Exception:
                pass
    except Exception as e:
        log.warning("خواندن لایهٔ متنی ممکن نشد: %s", e)
    return "\n".join(parts).strip()


def ocr_pdf(data: bytes, recognize: Callable[[bytes, str], tuple[str, float]],
            lang: str | None = None, max_pages: int | None = None,
            min_text_chars: int | None = None) -> dict:
    """پی‌دی‌اف → متن. خروجی: {method, text, pages(ocr شده)}"""
    max_pages = max_pages or settings.MAX_PDF_PAGES
    min_text_chars = min_text_chars or settings.MIN_TEXT_LAYER_CHARS
    t0 = time.time()

    text_layer = _text_layer(data)
    if meaningful_chars(text_layer) >= min_text_chars:
        return {
            "method": "pdf-text",
            "text": text_layer,
            "ocrPages": [],
            "durationMs": int((time.time() - t0) * 1000),
        }

    # پی‌دی‌اف اسکن‌شده: صفحات را به تصویر تبدیل و OCR کن
    ocr_pages: list[int] = []
    chunks: list[str] = []
    pdf = pdfium.PdfDocument(data)
    total = len(pdf)
    limit = min(total, max_pages)
    log.info("PDF اسکن‌شده است (لایهٔ متن ناکافی): %d صفحه، OCR تا %d صفحه", total, limit)
    for i in range(limit):
        page = pdf[i]
        scale = 200 / 72  # 200 DPI
        bitmap = page.render(scale=scale)
        pil = bitmap.to_pil()
        buf = io.BytesIO()
        pil.save(buf, format="PNG")
        try:
            text, _ = recognize(buf.getvalue(), lang)
        except Exception:
            # اگر صفحه‌ای خراب بود ادامه می‌دهیم
            text = ""
        if meaningful_chars(text) > 3:
            chunks.append(text)
            ocr_pages.append(i + 1)
        bitmap.close()
        page.close()

    if total > limit:
        log.info("تعداد صفحات پی‌دی‌اف بیش از حد مجاز بود؛ %d صفحهٔ اول OCR شد.", limit)

    return {
        "method": "pdf-ocr",
        "text": "\n\n".join(c for c in chunks if c.strip()).strip(),
        "ocrPages": ocr_pages,
        "totalPages": total,
        "durationMs": int((time.time() - t0) * 1000),
    }
