# آزمون‌های پیام‌رسان

گزارش نسخهٔ فعلی: [شمارنده، ارتباط زنده و فایل‌ها](../../BUGFIX-CHAT-REALTIME-FILES.md).
گزارش قبلی: [جست‌وجوی کاربران](../../BUGFIX-CHAT-SEARCH.md).

## آزمون‌های مستقل C#

پیش‌نیاز: .NET 8 SDK. از ریشهٔ مخزن:

```bash
dotnet restore tests/chat/Inventory.Chat.Tests/Inventory.Chat.Tests.csproj --configfile inventory/NuGet.config
dotnet test tests/chat/Inventory.Chat.Tests/Inventory.Chat.Tests.csproj -c Release --no-restore
```

۵۷ آزمون؛ SQLite مستقل در حافظه و پوشهٔ فایل موقت ساخته و پس از تست پاک می‌شود. آزمون SQL Server فقط اسکریپت تولید می‌کند و به سرور متصل نمی‌شود. چهار آزمون تازهٔ ذخیره‌سازی، ریشهٔ پیکربندی‌پذیر `Files:ChatRoot`، بازگشت به مسیر جایگزین و رد مسیر ناشناس را می‌سنجند (شرح: بخش ۵ گزارش BUGFIX-CHAT-REALTIME-FILES.md).

برای محیط کم‌حافظه می‌توان فقط در فرمان آزمون از این گزینه‌ها استفاده کرد؛ تنظیمات پروژه نیازی به تغییر ندارد:

```bash
dotnet test tests/chat/Inventory.Chat.Tests/Inventory.Chat.Tests.csproj -c Release --no-restore -m:1 -p:RunAnalyzers=false -p:UseSharedCompilation=false
```

## آزمون‌های HTTP، سوکت و مرورگر

**این آزمون‌ها کاربر، گفتگو، گروه و فایل می‌سازند. فقط روی دیتابیس و پوشهٔ فایل دورریختنی اجرا کنید؛ نه نصب عملیاتی.** آزمون فایل یک فایل ۳۲ MiB را هم واقعاً آپلود/دانلود می‌کند.

Client را Publish و همراه API روی یک مبدأ اجرا کنید (روش تک‌سروره در `DEPLOY.md`). سپس:

```bash
python3 -m pip install requests websockets playwright
python3 -m playwright install chromium
# در لینوکس ممکن است playwright install-deps chromium نیز لازم باشد.
CHAT_TEST_DISPOSABLE=1 python3 tests/chat/realtime_files_test.py
CHAT_TEST_DISPOSABLE=1 python3 tests/chat/browser_realtime_files_test.py
CHAT_TEST_DISPOSABLE=1 python3 tests/chat/browser_search_test.py
CHAT_TEST_DISPOSABLE=1 python3 tests/chat/archive_check.py   # بررسی صفحهٔ آرشیو اسناد (/doc-archive) و پنهان‌شدن منو بر پایهٔ نقش
CHAT_TEST_DISPOSABLE=1 python3 tests/chat/browser_pwa_download_test.py  # ۹ سناریوی PWA: دانلود بدون تب جدید + گارد سرویس‌ورکر برای /api
```

### چرا سوئیت PWA جدا است؟
سوئیت‌های مرورگر دیگر سرویس‌ورکر را مسدود می‌کنند (`service_workers="block"`) تا پایدار بمانند؛ اما حفرهٔ «باز شدن تب دوم با شِل برنامه و دانلود فقط بعد از Ctrl+Shift+R» فقط با سرویس‌ورکر فعال دیده می‌شود. سوئیت PWA همین حالت را پوشش می‌دهد: دانلود و پیش‌نمایش پیوست در همان صفحه/تب، و اینکه `service-worker.published.js` هرگز مسیرهای `/api`، `/hubs` و URLهای دارای `access_token` را با `index.html` کش‌شده جواب نمی‌دهد.

شبیه‌سازی «استقرار دوم» برای خطای «فایل پیوست روی سرور یافت نشد.»: یک کپی از پوشهٔ Publish شده را در پوشهٔ تازه بریزید (`App_Data` را حذف کنید) و با همان دیتابیس روی پورت دیگر بالا بیاورید؛ دانلود پیوستِ ساخته‌شده در نمونهٔ اول با ۴۰۴ شکست می‌خورد. سپس با `Files__ChatRoot=<مسیر واقعی فایل‌ها>` دوباره بالا بیاورید تا دانلود و آپلود متقابل برقرار شود (لاگ `[ChatFiles] ... آماده است` مسیر مؤثر را نشان می‌دهد).

در PowerShell، قبل از اجرای اسکریپت‌ها:

```powershell
$env:CHAT_TEST_DISPOSABLE = "1"
```

متغیرهای اختیاری:

- `CHAT_TEST_BASE_URL`: مبدأ مشترک API و رابط؛ پیش‌فرض `http://127.0.0.1:5100`.
- `CHAT_TEST_USERNAME` و `CHAT_TEST_PASSWORD`: حساب مدیرِ محیط تست؛ پیش‌فرض حساب دمو پروژه.

پوشش:

| اسکریپت | سناریو |
|---|---:|
| `realtime_files_test.py` | ۱۱ سناریوی HTTP/SignalR |
| `browser_realtime_files_test.py` | ۱۳ سناریوی مرورگر با چند حساب |
| `browser_search_test.py` | ۱۱ سناریوی مرورگر جست‌وجو |
| `chat_module_test.py` (قبلی) | ۳۱ بررسی API؛ آن هم فقط روی محیط دورریختنی |

آزمون مرورگر برای ویدیو از canvas مصنوعی WebM می‌سازد؛ به دوربین یا میکروفون دسترسی نمی‌گیرد. آزمون‌ها نشست‌ها و نام‌های کاربری مستقل می‌سازند و اطلاعات ورود واقعی لازم ندارند.
