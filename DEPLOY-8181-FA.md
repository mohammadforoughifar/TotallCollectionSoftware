# استقرار روی سرور ۱۹۲.۱۶۸.۳۰.۱۰۴:۸۱۸۱ — خلاصهٔ تغییرات و تحویل

تاریخ: ۱۴۰۵/۰۷/۲۱ (2026-10-13) — شاخه: `main`

## ۱) فایل تحویلی (پابلیش)

`TotallPublish-192.168.30.104-8181.7z`  (حجم ≈ ۶۹ مگابایت — self-contained وینx64)

داخلش: `Inventory.Api.exe` + کل وابستگی‌ها (+ نیتیوهای Skia/QuestPDF/SqlClient) + پوشهٔ `wwwroot`
(کلاینت Blazor منتشرشده) + `start.bat` + `stop.bat` + `run-server.bat` + `appsettings.Production.json`
+ `README-استقرار-فارسی.txt`.

روش استفاده روی سرور: کل محتوا را داخل یک پوشه (مثلاً `D:\TotallPublish`) باز کنید → `start.bat`
(یا برای دیدن لاگ زنده: `run-server.bat`) → مرورگر روی <http://192.168.30.104:8181> باز می‌شود.
توقف: `stop.bat`.

## ۲) ریشهٔ مشکل قبلی «پس از زدن رمز، ارتباط با سرور برقرار نشد»

سه ایراد هم‌زمان وجود داشت و هر سه برطرف شد:

1. **پورت سرور نادیده گرفته می‌شد**: در `Inventory.Api/Program.cs` پورتِ Kestrel از متغیر `PORT`
   خوانده می‌شد و مقدار `ASPNETCORE_URLS=http://192.168.30.104:8181` (که `start.bat`/`run-server.bat`
   ست می‌کنند) هیچ اثری نداشت؛ سرور در واقع روی `5100` گوش می‌داد و مرورگر روی ۸۱۸۱ چیزی پیدا نمی‌کرد.
   → تابع `ResolveHttpEndpoint()` اضافه شد: `--urls` ← `ASPNETCORE_URLS` ← `DOTNET_URLS` ← `PORT` ← پیش‌فرض 5100،
   و هم بایندینگ Kestrel و هم `app.Run(...)` از همان استفاده می‌کنند (`ListenAnyIP` تا اگر کارت شبکهٔ آن آی‌پی
   بالا نبود، راه‌اندازی خطا ندهد).
2. **آدرس ذخیره‌شدهٔ کهنهٔ `localhost:5100` در مرورگر**: در `wwwroot/index.html` هر مقدار موجود در
   `localStorage['apiBaseUrl']` بی‌بررسی استفاده می‌شد؛ اگر یک‌بار برنامه روی خودِ سرور با localhost باز شده
   بود، همان آدرس روی کامپیوتر کاربران استفاده می‌شد و درخواست‌ها به کامپیوتر خودشان می‌رفت.
   → نشانی محلی روی دستگاه غیرمحلی «نامعتبر» اعلام می‌شود و آدرس پیش‌فرض سرور
   (`http://192.168.30.104:8181`) جایگزین شده است.
3. **انتخاب خودکار آدرس در زمان بالا آمدن کلاینت**: تابع جدید `inventoryResolveApiBase`
   سه گزینه (هم‌مبدأ / ذخیره‌شده / آدرس پیش‌فرض سرور) را **با یک درخواست واقعی** به `/api/health`
   آزمایش و اولین گزینهٔ پاسخ‌ده را انتخاب و ذخیره می‌کند؛ پس اگر آدرسی اشتباه بود، خودکار اصلاح می‌شود.

به‌علاوه: endpoint عمومی **`GET /api/health`** اضافه شد — با باز کردن
<http://192.168.30.104:8181/api/health> در مرورگر سرور، سلامت سرور و مقادیر فعال (محیط، دیتابیس، آدرس)
به‌صورت JSON دیده می‌شود؛ ابزار سریع عیب‌یابی.

## ۳) تغییر درخواستی کد ۳۰ → ۱۰ (مدت گارانتی در چاپ فیش تحویل)

`inventory/src/Inventory.Client/Pages/Repairs/RepairDeliveryReceiptPrint.cs` (بخش «شرایط گارانتی و ضوابط ترخیص»):
«قطعات تعویضی و خدمات فنی انجام‌شده دارای **۳۰ روز** مهلت تست…» → «… دارای **۱۰ روز** مهلت تست…».
(این متن همانی است که در «عملیات ← تعمیرات ← چاپ فیش تحویل» چاپ می‌شود؛ تنها محل این متن در کد همین فایل بود.)

## ۴) یک رفع اضافی مرتبط با ورود (مهم)

در `Data/DbInitializer.cs` شرط ساخت کاربر پیش‌فرض مدیر `if (!db.Users.Any())` بود؛ اما سیدر هوش مصنوعی
کاربر `aria.ai` را **پیش از** آن می‌سازد، پس روی یک دیتابیس تازه این شرط هرگز برقرار نمی‌شد و هیچ کاربری
برای ورود ساخته نمی‌شد (نصب تازه = قفل‌شدن). شرط اصلاح شد تا اگر کاربری با نام `admin` یا نقشی با مقدار
`Admin` وجود نداشت، مدیر پیش‌فرض `admin / admin` ساخته شود — **رمز را بلافاصله تغییر دهید.**

## ۵) آزمون‌های انجام‌شده (واقعی، نه فقط بیلد)

- سرور روی `http://0.0.0.0:8181` بالا آمد و `GET /` → 200 (صفحهٔ `index.html` با کد جدید)،
  `/_framework/blazor.webassembly.js` → 200، مسیرهای SPA → 200.
- `GET /api/health` → `200` با `{"ok":true,…}`.
- `POST /api/auth/login` با `admin/admin` روی دیتابیس تازه → **HTTP 200 و توکن JWT** (زنجیرهٔ ورود سالم).
- منطق انتخاب آدرس در `index.html` با چهار سناریو تست شد: (۱) نشانی کهنهٔ localhost روی مرورگر کاربر →
  انتخاب درست آدرس سرور، (۲) کلاینت روی وب‌سرور دیگر و API روی ۸۱۸۱ → تغییر خودکار به آدرس API،
  (۳) اجرای محلی → localhost، (۴) سرور خاموش → برگشت به حدس اول (پیام خطای روشن).

## ۶) نکات عملیاتی سرور

- فایروال: `netsh advfirewall firewall add rule name="Totall 8181" dir=in action=allow protocol=TCP localport=8181`
- دیتابیس: `Server=192.168.30.104;Database=InventoryDb;User Id=sa;Password=…;TrustServerCertificate=True;Encrypt=False`
  (کاربر `sa` باید مجوز ساخت دیتابیس داشته باشد؛ در اولین اجرا جداول خودکار ساخته/به‌روزرسانی می‌شوند).
- HTTPS داخلی (اختیاری، برای اعلان سیستمی): `https://192.168.30.104:5443` + نصب گواهی از
  `http://192.168.30.104:8181/https-setup`.
- اگر روی مرورگر کاربر پیام قدیمی ماند، یک‌بار «داده‌های سایت» همان دامنه پاک شود.
- بازتولید این پابلیش روی ویندوز: `publish-win-server.bat` (کلاینت ← `wwwroot` سرور ← پابلیش تک‌فایل API).

## ۷) تغییرات مخزن در این مرحله

| فایل | تغییر |
|---|---|
| `inventory/src/Inventory.Api/Program.cs` | `ResolveHttpEndpoint()` + بایندینگ/`app.Run` بر اساس `ASPNETCORE_URLS` + endpoint `/api/health` |
| `inventory/src/Inventory.Client/wwwroot/index.html` | `DEFAULT_API_BASE`، اعتبارسنجی نشانی ذخیره‌شده، `inventoryResolveApiBase` (آزمایش واقعی سه گزینه) |
| `inventory/src/Inventory.Client/Program.cs` | استفاده از `inventoryResolveApiBase` با مقدار پیش‌فرض `http://192.168.30.104:8181` |
| `inventory/src/Inventory.Client/Pages/Repairs/RepairDeliveryReceiptPrint.cs` | گارانتی: ۳۰ روز → **۱۰ روز** |
| `inventory/src/Inventory.Api/Data/DbInitializer.cs` | ساخت کاربر پیش‌فرض مدیر روی دیتابیس تازهٔ بدون کاربر admin |
| `inventory/src/Inventory.Api/appsettings.Production.json` | تنظیمات محیط عملیاتی (دیتابیس/HTTPS) — جدید |
| `run-server.bat` / `start.bat` / `stop.bat` / `publish-win-server.bat` / `inventory/start.bat` | آدرس و رشتهٔ اتصال یکدست روی ۱۹۲.۱۶۸.۳۰.۱۰۴:۸۱۸۱ + کپی خودکار batها در پابلیش |
| `inventory/packages/TaxCollectData.Library.0.0.27.nupkg` | بستهٔ SDK مودیان ۰.۰.۲۷ که در مخزن نبود (بیلد بدون آن ممکن نبود) — جدید |
| `.gitignore` | استثنا برای ماندن nupkg مودیان در مخزن |
