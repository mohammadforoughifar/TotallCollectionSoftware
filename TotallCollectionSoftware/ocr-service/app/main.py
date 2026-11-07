"""Totall OCR Service — سرویس مستقل استخراج متن (OCR) برای برنامهٔ Totall.

اجرا:
    uvicorn app.main:app --host 127.0.0.1 --port 8765
یا:
    run_windows.bat
"""
from __future__ import annotations

import logging
import os
import time

from fastapi import FastAPI, File, Form, Header, HTTPException, UploadFile
from fastapi.responses import JSONResponse

from . import office, pdf_text
from .config import settings
from .engines import EngineError, OcrEngine
from .preprocess import meaningful_chars

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
log = logging.getLogger("ocr.main")

VERSION = "1.0.0"

app = FastAPI(
    title="Totall OCR Service",
    description="سرویس استخراج متن تصویر و PDF اسکن‌شده (فارسی/انگلیسی) برای آرشیو اسناد",
    version=VERSION,
)


def _check_key(x_ocr_key: str | None = None, authorization: str | None = None) -> None:
    if not settings.API_KEY:
        return
    provided = (x_ocr_key or "").strip()
    if not provided and authorization and authorization.lower().startswith("bearer "):
        provided = authorization[7:].strip()
    if provided != settings.API_KEY:
        raise HTTPException(status_code=401, detail="کلید API سرویس OCR نامعتبر است.")


# موتور در اولین درخواست ساخته می‌شود تا راه‌اندازی سرویس سریع بماند
_engine: OcrEngine | None = None
_engine_error: str | None = None
_engine_retry_after: float = 0.0  # پس از نصب/رفع مشکل، خودکار دوباره تلاش کند


def _get_engine() -> OcrEngine:
    global _engine, _engine_error, _engine_retry_after
    if _engine is not None:
        return _engine
    if _engine_error and time.time() < _engine_retry_after:
        raise HTTPException(status_code=503, detail=_engine_error)
    try:
        _engine = OcrEngine()
        _engine_error = None
        log.info("موتور OCR فعال شد: %s", _engine.name)
    except EngineError as e:
        _engine_error = str(e)
        _engine_retry_after = time.time() + 60  # یک دقیقه بعد دوباره تلاش کن
        raise HTTPException(status_code=503, detail=_engine_error)
    return _engine


@app.get("/health")
def health():
    engine: str | None = None
    langs: list[str] = []
    ready = False
    try:
        e = _get_engine()
        engine, langs = e.name, e.available_langs()
        ready = True
    except HTTPException:
        pass
    return {
        "status": "ok" if ready else "degraded",
        "service": "totall-ocr",
        "version": VERSION,
        "engine": engine,
        "languages": langs,
        "requestedLanguages": settings.LANGS,
    }


@app.get("/api/ocr/info")
def info(x_ocr_key: str | None = Header(default=None), authorization: str | None = Header(default=None)):
    _check_key(x_ocr_key, authorization)
    e = _get_engine()
    return {
        "service": "totall-ocr",
        "version": VERSION,
        "engine": e.name,
        "availableLanguages": e.available_langs(),
        "requestedLanguages": settings.LANGS,
        "maxPdfPages": settings.MAX_PDF_PAGES,
    }


@app.post("/api/ocr/image")
async def ocr_image(
    file: UploadFile = File(...),
    lang: str | None = Form(default=None),
    x_ocr_key: str | None = Header(default=None),
    authorization: str | None = Header(default=None),
):
    _check_key(x_ocr_key, authorization)
    data = await file.read()
    if not data:
        raise HTTPException(status_code=400, detail="فایل خالی است.")
    if len(data) > settings.MAX_UPLOAD_MB * 1024 * 1024:
        raise HTTPException(status_code=413, detail=f"حجم فایل بیش از {settings.MAX_UPLOAD_MB} مگابایت است.")
    t0 = time.time()
    try:
        engine = _get_engine()
        text, dur = engine.recognize_text(data, lang)
    except EngineError as e:
        return JSONResponse(status_code=200, content={
            "success": False, "engine": engine_name(), "method": "image",
            "text": "", "characterCount": 0, "error": str(e), "durationMs": 0,
        })
    duration_ms = int((time.time() - t0) * 1000)
    log.info("OCR image: %d بایت -> %d نویسه (%.1fs)", len(data), meaningful_chars(text), duration_ms / 1000)
    return {
        "success": True, "engine": engine.name, "method": "image", "text": text,
        "characterCount": meaningful_chars(text), "durationMs": duration_ms, "error": None,
    }


@app.post("/api/ocr/pdf")
async def ocr_pdf(
    file: UploadFile = File(...),
    lang: str | None = Form(default=None),
    maxPages: int | None = Form(default=None),
    minTextChars: int | None = Form(default=None),
    x_ocr_key: str | None = Header(default=None),
    authorization: str | None = Header(default=None),
):
    _check_key(x_ocr_key, authorization)
    data = await file.read()
    if not data:
        raise HTTPException(status_code=400, detail="فایل خالی است.")
    if len(data) > settings.MAX_UPLOAD_MB * 1024 * 1024:
        raise HTTPException(status_code=413, detail=f"حجم فایل بیش از {settings.MAX_UPLOAD_MB} مگابایت است.")
    t0 = time.time()
    try:
        engine = _get_engine()
        res = pdf_text.ocr_pdf(
            data,
            recognize=lambda b, l: engine.recognize_text(b, l),
            lang=lang, max_pages=maxPages, min_text_chars=minTextChars,
        )
    except EngineError as e:
        return JSONResponse(status_code=200, content={
            "success": False, "engine": engine_name(), "method": "pdf",
            "text": "", "characterCount": 0, "error": str(e),
            "ocrPages": [], "durationMs": 0,
        })
    duration_ms = max(int((time.time() - t0) * 1000), res.get("durationMs", 0))
    log.info("OCR pdf: روش=%s صفحات OCR=%s (%.1fs)",
             res["method"], res.get("ocrPages", []), duration_ms / 1000)
    return {
        "success": True, "engine": engine.name, "method": res["method"],
        "text": res["text"], "characterCount": meaningful_chars(res["text"]),
        "ocrPages": res.get("ocrPages", []), "totalPages": res.get("totalPages"),
        "durationMs": duration_ms, "error": None,
    }


@app.post("/api/ocr/office")
async def ocr_office(
    file: UploadFile = File(...),
    x_ocr_key: str | None = Header(default=None),
    authorization: str | None = Header(default=None),
):
    """استخراج متن کامل فایل‌های Word (.docx) و Excel (.xlsx) — جدول‌ها و همهٔ شیت‌ها."""
    _check_key(x_ocr_key, authorization)
    data = await file.read()
    if not data:
        raise HTTPException(status_code=400, detail="فایل خالی است.")
    if len(data) > settings.MAX_UPLOAD_MB * 1024 * 1024:
        raise HTTPException(status_code=413, detail=f"حجم فایل بیش از {settings.MAX_UPLOAD_MB} مگابایت است.")
    ext = os.path.splitext(file.filename or "")[1]
    t0 = time.time()
    text, kind = office.extract_office(data, ext)
    if not kind:
        return JSONResponse(status_code=200, content={
            "success": False, "engine": "office", "method": "office",
            "text": "", "characterCount": 0,
            "error": ("پشتیبانی فقط از فرمت‌های جدید آفیس (.docx و .xlsx). "
                      "فایل‌های قدیمی .doc/.xls را در Word/Excel با «Save As» به docx/xlsx تبدیل کنید."),
            "durationMs": 0,
        })
    duration_ms = int(time.time() - t0)
    return {
        "success": True, "engine": "office", "method": kind.lower(),
        "kind": kind, "text": text,
        "characterCount": meaningful_chars(text), "durationMs": duration_ms, "error": None,
    }


def engine_name() -> str | None:
    global _engine
    return _engine.name if _engine else None
