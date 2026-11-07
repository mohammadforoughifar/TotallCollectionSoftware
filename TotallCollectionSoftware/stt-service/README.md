# سرویس محلی «تبدیل ویس به متن» (STT)

این سرویس، پیام‌های صوتی چت را به **متن فارسی** تبدیل می‌کند و دکمهٔ «تبدیل به متن» سامانه را
فعال می‌کند. کاملاً **رایگان، بدون کلید API و بدون نیاز به اینترنت** (بعد از یک‌بار دانلود مدل)
و روی همان سروری که برنامه روی آن است اجرا می‌شود. نیازی به کارت گرافیک ندارد.

- موتور: `faster-whisper` (نسخهٔ بهینهٔ Whisper از OpenAI، اجرا با CTranslate2)
- قرارداد: دقیقاً مثل OpenAI → `POST /v1/audio/transcriptions` با پارامترهای `file`, `model`, `language`, `response_format`
- پورت پیش‌فرض: **۹۰۰۰** روی `127.0.0.1` (فقط همان سرور)

---

## ۱) راه‌اندازی سریع (دو دقیقه + یک‌بار دانلود مدل)

**ویندوز:** فایل `run_windows.bat` را دوبار کلیک کنید (خودش محیط پایتون می‌سازد و بقیه را نصب می‌کند).

**لینوکس/مک:**

```bash
cd stt-service
chmod +x run.sh
./run.sh
```

اولین اجرا مدل را دانلود می‌کند (مدل `small` حدود **۵۰۰ مگابایت**) و در پوشهٔ `stt-service/models`
ذخیره می‌شود؛ از آن به بعد اجرا در چند ثانیه انجام می‌شود و اینترنت لازم نیست.

بعد از اجرا، این آدرس باید جواب بدهد:

```
http://127.0.0.1:9000/health
```

## ۲) تنظیم سامانه (یک‌بار)

در `inventory/src/Inventory.Api/appsettings.json` (یا بهتر: با متغیر محیطی):

```jsonc
"Ai": {
  "TranscriptionModel": "small",                     // همان مقدار --model سرویس
  "TranscriptionBaseUrl": "http://127.0.0.1:9000/v1", // آدرس همین سرویس
  "TranscriptionApiKey": "",                          // اگر STT_API_KEY ست کردید، همان را بگذارید
  "TranscriptionLanguage": "fa",
  "TranscriptionMaxMegabytes": 24
}
```

معادل متغیر محیطی (ویندوز/لینوکس):

```
Ai__TranscriptionModel=small
Ai__TranscriptionBaseUrl=http://127.0.0.1:9000/v1
```

> در نسخهٔ جدید همین مقادیر به‌صورت پیش‌فرض تنظیم شده‌اند؛ اگر سرویس را روی همین سرور اجرا
> کنید، کار دیگری لازم نیست. (اگر نمی‌خواهید این قابلیت فعال باشد، هر دو مقدار را خالی بگذارید.)

سپس برنامه را یک‌بار **Restart** کنید. در چت، زیر هر پیام صوتی دکمهٔ **«تبدیل به متن»** ظاهر
می‌شود؛ با کلیک، متن زیر همان پیام می‌آید و برای همهٔ اعضای گفتگو هم زنده به‌روز می‌شود.

## ۳) انتخاب مدل (کیفیت ↔ سرعت)

| مدل | حجم دانلود | حافظهٔ لازم | سرعت روی CPU | کیفیت فارسی |
|---|---|---|---|---|
| `tiny` | ~۷۵ مگ | ~۵۰۰ مگ | خیلی سریع (چند ثانیه) | ضعیف — فقط برای تست |
| `base` | ~۱۴۵ مگ | ~۷۰۰ مگ | سریع | قابل قبول برای جملهٔ ساده |
| **`small`** (پیشنهادی) | ~۵۰۰ مگ | ~۱ گیگ | متوسط | خوب |
| `medium` | ~۱.۵ گیگ | ~۲.۵ گیگ | کند | خیلی خوب |
| `large-v3` / `large-v3-turbo` | ~۳ گیگ | ~۴ گیگ | بسیار کند روی CPU | بهترین |

نکته: روی سرورهای ضعیف، `small` با `--compute-type int8` (پیش‌فرض) بهترین تعادل است. اگر
تبدیل طول کشید و پیام «زمان‌بر شد» گرفتید، مدل کوچک‌تر بردارید یا در `appsettings.json` مقدار
`Ai:TimeoutSeconds` را بیشتر کنید (پیش‌فرض ۱۸۰ ثانیه).

## ۴) گزینه‌های اجرا

```
python stt_server.py --help

  --model small            نام مدل یا مسیر مدل محلی
  --model-dir ./models     محل ذخیرهٔ مدل (برای کپی کردن روی سرور بدون اینترنت)
  --host 127.0.0.1         اگر API روی ماشین دیگری است: 0.0.0.0
  --port 9000
  --device auto|cpu|cuda   cuda = کارت گرافیک NVIDIA
  --compute-type int8|float16
  --language fa            خالی = تشخیص خودکار
  --initial-prompt "…"      متن راهنمای فارسی برای بهترشدن املا (پیش‌فرض فعال؛ خاموش: "")
  --no-vad                 حذف‌کنندهٔ سکوت را خاموش می‌کند
  --api-key <secret>       احراز هویت Bearer (برای وقتی روی شبکه باز می‌شود)
  --preload                مدل را همان اول بارگذاری کن (پیشنهاد می‌شود)
  --dump-dir ./dumps       ذخیرهٔ درخواست‌های خام برای اشکال‌زدایی
```

> نکته: خروجی مدل‌های کوچک (`tiny`/`base`) برای فارسی ضعیف است؛ پیشنهاد ما `small` است. اگر
> سرور ≥۸ گیگابایت رم دارد، `medium` دقت فارسی را محسوس بهتر می‌کند (فقط با تغییر `--model` یا
> متغیر `STT_MODEL` در اسکریپت اجرا).

نمونهٔ اجرای دقیق‌تر:

```bash
python stt_server.py --model medium --host 0.0.0.0 --port 9000 --language fa --preload --api-key 1234
```

## ۵) اجرای دائمی (اختیاری)

**ویندوز:** با Task Scheduler یک تسک «At startup» بسازید که `run_windows.bat` را اجرا کند،
یا از NSSM برای ساخت سرویس ویندوزی استفاده کنید:

```
nssm install TotallSTT "C:\...\stt-service\.venv\Scripts\python.exe" "C:\...\stt-service\stt_server.py"
nssm set TotallSTT AppDirectory "C:\...\stt-service"
nssm start TotallSTT
```

**لینوکس (systemd):**

```ini
# /etc/systemd/system/totall-stt.service
[Unit]
Description=Totall STT Service
After=network.target

[Service]
WorkingDirectory=/opt/totall/stt-service
ExecStart=/opt/totall/stt-service/.venv/bin/python stt_server.py --model small --preload
Restart=always
User=www-data

[Install]
WantedBy=multi-user.target
```

## ۶) اگر سرویس محلی نخواستید (جایگزین‌ها)

| سرویس | `TranscriptionModel` | `TranscriptionBaseUrl` | `TranscriptionApiKey` |
|---|---|---|---|
| OpenAI | `whisper-1` یا `gpt-4o-transcribe` | `https://api.openai.com/v1` | `sk-...` |
| Groq | `whisper-large-v3` | `https://api.groq.com/openai/v1` | کلید Groq |
| speaches / faster-whisper-server | نام مدل آن سرویس | `http://127.0.0.1:8000/v1` | — |

> ⚠️ Ollama فعلاً endpoint رونویسی ندارد؛ برای همین `TranscriptionBaseUrl` از `BaseUrl`
> دستیار جداست و می‌تواند به سرویس دیگری برود.

## ۷) رفع اشکال

| نشانه | علت / راه‌حل |
|---|---|
| پیام «ارتباط … برقرار نشد: Connection refused» | سرویس STT اجرا نیست → `run_windows.bat` / `run.sh` را اجرا کنید |
| می‌خواهید ببینید سامانه دقیقاً چه چیزی می‌فرستد | سرویس را با `--dump-dir ./dumps` اجرا کنید؛ هر درخواست خام ذخیره می‌شود |
| پیام «سرویس … در این آدرس پیدا نشد (404)» | `TranscriptionBaseUrl` باید به `/v1` ختم شود |
| «کلید سرویس نامعتبر است» | `--api-key` سرویس با `Ai:TranscriptionApiKey` یکی نیست |
| «زمان‌بر شد» | مدل سنگین است → `base`/`small` یا افزایش `Ai:TimeoutSeconds` |
| تبدیل از ماشین دیگری جواب نمی‌دهد | `--host 0.0.0.0` و باز کردن پورت در فایروال |
| اولین اجرا خطای دانلود مدل می‌دهد | اینترنت سرور را بررسی کنید، یا پوشهٔ `models` را از ماشین دیگری کپی کنید |
| متن خالی برمی‌گردد | فایل بی‌صدا یا خیلی کوتاه است؛ دوباره ضبط کنید |

## ۸) تست دستی (اختیاری)

```bash
curl -F "file=@sample.webm" -F "model=small" -F "language=fa" \
     http://127.0.0.1:9000/v1/audio/transcriptions
# → {"text":"سلام، این یک پیام صوتی آزمایشی است."}
```

## فایل‌ها

| فایل | توضیح |
|---|---|
| `stt_server.py` | سرویس (بدون وابستگی به فریم‌ورک؛ فقط faster-whisper) |
| `requirements.txt` | وابستگی‌ها |
| `run_windows.bat` / `run.sh` | اجرای آمادهٔ ویندوز / لینوکس (ساخت محیط + نصب + اجرا) |
| `models/` (خودکار ساخته می‌شود) | مدل‌های دانلودشده — قابل کپی روی سرور بدون اینترنت |
