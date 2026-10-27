"""سرویس‌های موتور OCR: tesseract (محلی) و easyocr (عصبی) با انتخاب خودکار."""
from __future__ import annotations

import logging
import os
import shutil
import subprocess
import tempfile
import threading
import time
from typing import Optional

from . import preprocess
from .config import settings

log = logging.getLogger("ocr.engine")


# --------------------------------------------------------------------------
# نگاشت زبان‌های درخواستی به کدهای هر موتور
# --------------------------------------------------------------------------
def _parse_langs(raw: Optional[str]) -> list[str]:
    parts = [(raw or settings.LANGS).replace("+", " ").replace(",", " ").split()]
    out: list[str] = []
    for grp in parts:
        for p in grp:
            p = p.strip().lower()
            if p and p not in out:
                out.append(p)
    return out or ["fa", "en"]


def _tess_lang_codes(langs: list[str]) -> list[str]:
    """کدهای tesseract: برای فارسی 'fas' استاندارد است (برخی نصب‌ها 'fa')."""
    mapping = {"fa": ["fas", "fa"], "ar": ["ara"], "en": ["eng"]}
    codes: list[str] = []
    for l in langs:
        for c in mapping.get(l, [l]):
            if c not in codes:
                codes.append(c)
    return codes


class EngineError(RuntimeError):
    """خطای قابل‌فهم برای کاربر (بدون traceback سنگین)."""


# --------------------------------------------------------------------------
# موتور tesseract
# --------------------------------------------------------------------------
class TesseractEngine:
    name = "tesseract"

    def __init__(self) -> None:
        self.exe = shutil.which("tesseract")
        if not self.exe:
            raise EngineError(
                "موتور tesseract روی این سیستم پیدا نشد. روی ویندوز نسخهٔ "
                "U.B. Mannheim را نصب کنید یا به‌جای آن از موتور easyocr "
                "(pip install -r requirements-easyocr.txt و OCR_ENGINE=easyocr) استفاده کنید."
            )
        self._langs_cache: Optional[list[str]] = None
        self._lock = threading.Lock()

    def available_langs(self) -> list[str]:
        with self._lock:
            if self._langs_cache is None:
                try:
                    r = subprocess.run(
                        [self.exe, "--list-langs"], capture_output=True, text=True, timeout=20
                    )
                    langs = [ln.strip() for ln in (r.stdout or "").splitlines() if ln.strip()]
                    self._langs_cache = langs[1:] if langs and langs[0].lower().startswith("list") else langs
                except Exception:
                    self._langs_cache = []
            return self._langs_cache

    def _resolve_langs(self, wanted: list[str]) -> list[str]:
        have = set(self.available_langs())
        resolved = []
        for code in _tess_lang_codes(wanted):
            if code in have:
                resolved.append(code)
        return resolved

    def recognize(self, img_bytes: bytes, langs: list[str], psm: int = 3) -> str:
        resolved = self._resolve_langs(langs)
        if not resolved:
            have = ", ".join(sorted(have if (have := set(self.available_langs())) else [])) or "—"
            raise EngineError(
                f"دادهٔ زبان ({', '.join(_parse_langs(None))}) روی tesseract نصب نیست. "
                f"زبان‌های موجود: {have}. بستهٔ فارسی: tesseract-ocr-fas"
            )
        # پیش‌پردازش سبک برای کیفیت بهتر روی tesseract
        img = preprocess.prepare_for_tesseract(img_bytes)
        tmp = tempfile.NamedTemporaryFile(suffix=".png", delete=False)
        try:
            img.save(tmp.name)
            tmp.close()
            cmd = [self.exe, tmp.name, "stdout", "-l", "+".join(resolved), "--psm", str(psm)]
            r = subprocess.run(cmd, capture_output=True, text=True, timeout=180)
            if r.returncode not in (0, 1):
                raise EngineError((r.stderr or r.stdout or "").strip()[:300] or "خطای ناشناختهٔ tesseract")
            return (r.stdout or "").strip()
        finally:
            tmp.close()
            try:
                os.unlink(tmp.name)
            except OSError:
                pass

    def recognize_text(self, img_bytes: bytes, langs: list[str]) -> str:
        text = self.recognize(img_bytes, langs, psm=3)
        if preprocess.meaningful_chars(text) < 10:
            alt = self.recognize(img_bytes, langs, psm=6)
            if len(alt) > len(text):
                text = alt
        return text


# --------------------------------------------------------------------------
# موتور easyocr (عصبی — بدون وابستگی سیستمی)
# --------------------------------------------------------------------------
class EasyOcrEngine:
    name = "easyocr"

    def __init__(self) -> None:
        try:
            import easyocr  # noqa: F401
        except ImportError:
            raise EngineError(
                "easyocr نصب نیست. دستور:  pip install -r requirements-easyocr.txt"
            )
        self._reader = None
        self._lock = threading.Lock()

    def _get_reader(self, langs: list[str]):
        # زبان‌های پشتیبانی‌شدهٔ easyocr: fa, en, ar
        supported = []
        for l in langs:
            if l in ("fa", "en", "ar"):
                supported.append(l)
        if not supported:
            raise EngineError("easyocr از این ترکیب زبان پشتیبانی نمی‌کند.")
        with self._lock:
            if self._reader is None:
                log.info("در حال بارگذاری مدل easyocr (%s) — بار اول کمی طول می‌کشد...", supported)
                import easyocr

                self._reader = easyocr.Reader(supported, gpu=False, verbose=False)
        return self._reader

    def recognize_text(self, img_bytes: bytes, langs: list[str]) -> str:
        reader = self._get_reader(langs)
        img = preprocess.prepare_for_easyocr(img_bytes)
        tmp = tempfile.NamedTemporaryFile(suffix=".png", delete=False)
        try:
            img.save(tmp.name)
            tmp.close()
            results = reader.readtext(tmp.name, detail=0, paragraph=True)
            return "\n".join(str(x).strip() for x in results if str(x).strip())
        finally:
            tmp.close()
            try:
                os.unlink(tmp.name)
            except OSError:
                pass


# --------------------------------------------------------------------------
# انتخاب موتور (auto | tesseract | easyocr)
# --------------------------------------------------------------------------
def create_engine():
    engine_name = settings.ENGINE
    if engine_name == "easyocr":
        return EasyOcrEngine()
    if engine_name == "tesseract":
        return TesseractEngine()

    # auto: easyocr اگر نصب باشد، وگرنه tesseract
    try:
        return EasyOcrEngine()
    except EngineError:
        try:
            return TesseractEngine()
        except EngineError as e:
            raise EngineError(
                "هیچ موتور OCR در دسترس نیست.\n"
                "گزینه ۱ (پیشنهادی ویندوز):  pip install -r requirements-easyocr.txt  و  OCR_ENGINE=easyocr\n"
                "گزینه ۲: نصب tesseract با زبان فارسی روی سیستم.\n" + str(e)
            )


class OcrEngine:
    """رپر تک‌بارهٔ موتور انتخاب‌شده با اطلاعات سلامت."""

    def __init__(self) -> None:
        self.engine = create_engine()
        self.name = self.engine.name

    def describe(self) -> dict:
        langs = self.available_langs()
        return {"engine": self.name, "languages": langs, "supportedLanguages": ["fa", "en", "ar"]}

    def available_langs(self) -> list[str]:
        if isinstance(self.engine, TesseractEngine):
            return self.engine.available_langs()
        return ["fa", "en", "ar"]  # easyocr

    def recognize_text(self, img_bytes: bytes, lang: Optional[str] = None) -> tuple[str, float]:
        t0 = time.time()
        langs = _parse_langs(lang)
        text = self.engine.recognize_text(img_bytes, langs)
        return text, time.time() - t0
