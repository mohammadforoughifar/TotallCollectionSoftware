"""پیکربندی سرویس OCR — از متغیرهای محیطی خوانده می‌شود."""
import os


def _b(name: str, default: str) -> str:
    return os.environ.get(name, default).strip() or default


def _i(name: str, default: int) -> int:
    try:
        return int(os.environ.get(name, default))
    except (TypeError, ValueError):
        return default


class Settings:
    """تمام تنظیمات؛ با نام‌های معادل در run_windows.bat مستند شده‌اند."""

    HOST: str = _b("OCR_HOST", "127.0.0.1")            # 0.0.0.0 اگر اپ روی ماشین دیگر است
    PORT: int = _i("OCR_PORT", 8765)

    # موتور: auto | tesseract | easyocr
    # auto = easyocr در صورت نصب بودن، وگرنه tesseract
    ENGINE: str = _b("OCR_ENGINE", "auto").lower()

    # زبان‌ها: fa=en -> فارسی + انگلیسی | fa | fa+en+ar (عربی)
    LANGS: str = _b("OCR_LANGS", "fa+en")

    # حداکثر صفحه برای OCR پی‌دی‌اف اسکن‌شده
    MAX_PDF_PAGES: int = _i("OCR_MAX_PDF_PAGES", 8)

    # حداقل حرف معنی‌دار لایهٔ متنی پی‌دی‌اف تا «دیجیتال» تلقی شود
    MIN_TEXT_LAYER_CHARS: int = _i("OCR_MIN_TEXT_LAYER_CHARS", 30)

    # کلید اختیاری؛ اگر خالی بود سرویس بدون کلید می‌پذیرد.
    # برنامهٔ اصلی باید همان مقدار را در OcrService:ApiKey بگذارد.
    API_KEY: str = _b("OCR_API_KEY", "")

    # حداکثر اندازهٔ آپلود (بایت) — پیش‌فرض ۵۰ مگابایت
    MAX_UPLOAD_MB: int = _i("OCR_MAX_UPLOAD_MB", 50)


settings = Settings()
