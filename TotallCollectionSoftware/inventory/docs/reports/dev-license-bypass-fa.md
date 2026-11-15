# حالت توسعه: بدون خطای لایسنس

**تاریخ:** ۱۴۰۵/۰۸/۲۳ (2026-11-14) · **پایه:** `main` = `a2433d95`

## ۱) مسئله

توسعه‌دهنده روی ماشین خودش نه سند امضاشده دارد، نه فایل درخواست، نه کلید خصوصی. نتیجه روی `main`:

| نشانه | ریشه |
|---|---|
| همهٔ ماژول‌ها «غیرفعال» و ثبت‌ها ۴۰۳ `LICENSE_READONLY` | وضعیت `Missing` ⇒ دروازهٔ لایسنس نوشتن را می‌بندد |
| بنر «لایسنس را فعال کنید» در همهٔ صفحه‌ها | همان وضعیت |
| ده‌ها خطای لاگ در هر بوت | «هیچ کلید عمومی بارگذاری نشد»، `LICENSE_LOAD_FAILED` و… |
| قفل «فقط‌خواندنی» پس از دست‌زدن به ساعت VM | لنگر ساعت سه‌جایی (گام ۴-۴) |
| سقف صندلی/سهمیه در تست دستی | نبود سند ⇒ سقف صفر |

## ۲) راه‌حل

یک «دور زدن لایسنس مخصوص محیط توسعه» با سه لایهٔ ایمنی:

1. **شرط محیط:** فقط وقتی `IHostEnvironment.IsDevelopment()` درست باشد. مستقل از تنظیمات.
2. **کلید تنظیمات:** `Licensing:DevelopmentBypass` — پیش‌فرض در Development روشن؛ با `false` خاموش می‌شود
   تا مسیر واقعی لایسنس (آپلود سند، انقضا، فقط‌خواندنی) قابل تست بماند.
3. **در Production اثری ندارد:** اگر کسی همان کلید را در Production روی `true` بگذارد، در لاگ **خطا**
   ثبت می‌شود و مقدار **نادیده** گرفته می‌شود.

در حالت فعال، یک `EffectiveEntitlement` ساختگی ساخته می‌شود:

| ویژگی | مقدار |
|---|---|
| وضعیت | `Valid` |
| ماژول‌ها | همهٔ ۳۳ ماژول `Valid` و نوشتنی |
| سقف سهمیه | خالی ⇒ نامحدود |
| صندلی | `Mode = None` ⇒ بدون سقف |
| ساعت | `Normal` و `WritesBlocked=false` |
| محدودیت نسخه/آپدیت | ندارد |
| نشانهٔ شناسایی | Issue با کد `LICENSE_DEV_BYPASS` (شدت Info) + `provider = "DevelopmentBypass"` در `/api/license/status` |

یعنی در صفحهٔ «وضعیت لایسنس» چیزی پنهان نمی‌شود: وضعیت `Valid` است ولی برچسب توسعه و کد
`LICENSE_DEV_BYPASS` دیده می‌شود.

## ۳) فایل‌های تغییر‌یافته

| فایل | تغییر |
|---|---|
| `src/Inventory.Licensing/Entitlement/DevelopmentEntitlement.cs` | **جدید** — ساخت وضعیت کاملِ ساختگی (بدون وابستگی به ASP.NET) |
| `src/Inventory.Api/Licensing/DevelopmentLicenseBypass.cs` | **جدید** — تصمیم «آیا در توسعه هستیم؟» + پیام هشدار |
| `src/Inventory.Api/Licensing/LicenseStartup.cs` | ثبت تک‌نمونه + لاگ هشدار + رد کردن بررسی‌های بی‌ربط در توسعه |
| `src/Inventory.Api/Licensing/Services/EntitlementCache.cs` | پیش از هر کوئری/امضا، وضعیت ساختگی برمی‌گرداند |
| `src/Inventory.Api/Licensing/Services/ClockGuard.cs` | در توسعه `Normal` منتشر می‌کند (قفل ساعت کنار می‌رود) |
| `src/Inventory.Api/Licensing/Controllers/LicenseController.cs` | فیلد `developmentBypass` در پاسخ `status` |
| `src/Inventory.Api/appsettings.Development.json` | کلید مستندشده `Licensing:DevelopmentBypass` |
| `tests/Licensing/Program.cs` | ۱۹ بررسی تازه برای وضعیت ساختگی و تصمیم فعال‌سازی |

هیچ تغییری در موتور امضا، اسکیمای دیتابیس، سیاست‌های امنیتی یا رفتار Production انجام نشده است.

## ۴) چطور استفاده می‌شود

- **روشن (پیش‌فرض):** `dotnet run --project src/Inventory.Api` با `ASPNETCORE_ENVIRONMENT=Development`.
  هیچ کار دیگری لازم نیست؛ همه‌چیز باز است و لاگ فقط یک خط هشدار دارد.
- **خاموش (تست مسیر واقعی لایسنس):** در `appsettings.Development.json`
  `"Licensing": { "DevelopmentBypass": false }` یا متغیر محیطی
  `Licensing__DevelopmentBypass=false`.
- **روی سرور مشتری:** هیچ کاری لازم نیست؛ `ASPNETCORE_ENVIRONMENT` آن‌جا `Production` است
  (هم `RUN.ps1` و هم IIS همین را می‌گذارند) و کلید بی‌اثر است.

## ۵) تست‌ها

```
dotnet build src/Inventory.Api/Inventory.Api.csproj -c Debug     # 0 error
dotnet run  --project tests/Licensing                            # 159 + 19 بررسی
```

- بیلد کامل `Inventory.Api` با این تغییرات: **۰ خطا** (۲۴۸ اخطار، همه از قبل موجود).
- ۱۹ بررسی تازه (به‌همراه یک بررسی حروف بزرگ/کوچک) همه PASS:
  وضعیت ساختگی کامل است؛ `Development` روشن، `Development + false` خاموش،
  `Production` (با و بدون کلید) خاموش، `Staging` خاموش.

## ۶) نکته‌های صادقانه

- این حالت **عمداً** محدودیت‌های لایسنس را در توسعه حذف می‌کند؛ پس روی ماشینی که
  `ASPNETCORE_ENVIRONMENT=Development` دارد، داده‌های مشتری محافظت نمی‌شود. روی سرور مشتری
  هرگز این متغیر را Development نگذارید.
- حالت توسعه، رخداد لایسنس در `LicenseEvents` **ثبت نمی‌کند** (نه اعمال، نه رد) — لاگ تمیز می‌ماند.
- قفل ساعت در توسعه غیرفعال است؛ اگر می‌خواهید رفتار واقعی ساعت را بسنجید، ابتدا
  `DevelopmentBypass=false` کنید.
