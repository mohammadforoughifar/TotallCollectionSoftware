# حذف کامل موتور لایسنس — گزارش تغییر

> تصمیم محصول: این نرم‌افزار **هیچ لایسنس و هیچ محدودیت سهمیه/صندلی‌ای ندارد**.
> کل موتور لایسنس از مخزن حذف شد؛ سامانه بدون فایل لایسنس، بدون کلید امضا و
> بدون دروازهٔ فقط‌خواندنی بالا می‌آید.

## چه چیزی حذف شد

| بخش | مسیر | توضیح |
|---|---|---|
| کتابخانهٔ موتور | `src/Inventory.Licensing/` | ۲۶ فایل: مدل سند، امضا/اعتبارسنجی، سهمیه، صندلی، دوره‌های شمسی، `Keys/` (کلیدهای عمومی و manifest) |
| زیرسیستم در API | `src/Inventory.Api/Licensing/` | ۴۳ فایل: دروازه‌ها، فیلترها، سرویس‌ها، کنترلر، کش، پاک‌کنندهٔ نشست، رزرو سهمیه |
| موجودیت‌ها | `src/Inventory.Api/Entities/Licensing/` | ۱۱ جدول `License*` |
| اسکیمای دستی | `Data/LicenseSchemaV1.cs`، `Data/QuotaCountingSchemaV1.cs` | ساخت جدول/ستون سهمیه |
| ابزارها | `tools/LicenseTool`، `LicenseCheck`، `RootKeyTool`، `license-sign.py`، `verify-licensing.ps1`، `replace-product-key.py`، `publish-customer.ps1` | صدور/بررسی لایسنس و جاسازی کلید |
| تست‌ها | `tests/Licensing`، `LicensingDb`، `LicensingUpgrade`، `E2E`، `test/support/feature-guard`، `test/licenses/` | همه لایسنس‌محور بودند |
| کلاینت | `Services/LicensingApi.cs`، `LicenseErrorText.cs`، `LicenseStatusSummary.cs`، `LicenseMenuMap.cs`، `Shared/LicenseBanner.razor`، `Pages/Settings/LicenseStatus.razor` | صفحه/بنر/سرویس وضعیت لایسنس |
| اسناد | ۱۵ فایل `*LICENS*` در `docs/` | مستندهای طراحی و عملیات لایسنس |

جمع: **۱۱۳ فایل حذف شد**.

## چه چیزی در کد تغییر کرد

- `Program.cs`: حذف `AddLicensing`، `InitializeLicensingAsync`، `LicenseGateMiddleware`،
  `SeatGateMiddleware`، `HubLicenseFilter`، پارامتر `ILicenseService` در `/api/health` و
  بلوک `licensing` در پاسخ سلامت.
- `AppDbContext`: حذف ۱۱ `DbSet` و `ConfigureLicensing`.
- `DbInitializer`: حذف فراخوانی‌های `LicenseSchemaV1` و `QuotaCountingSchemaV1`.
- `ApiControllerBase` / `BaseApiController`: حذف propertyهای `Licenses` و `LicenseModule`
  و رسیدگی ویژه به `LicenseQuotaException`؛ `[AllowInReadOnly]` از همهٔ اکشن‌ها حذف شد.
- `InvoicingService`: صدور فاکتور دیگر سهمیه مصرف نمی‌کند (ثبت مستقیم در تراکنش).
- `AuthService` / `AuthController`: حذف صندلی از ورود/خروج و سقف «کاربر نام‌دار».
- `MoadianController`: ارسال مستقیم به مودیان بدون رزرو/تسویهٔ سهمیه.
- نامه‌های داخلی/وارده/صادره و صورت‌جلسه: ثبت بدون گارد سهمیه.
- `AiAgentService`: حذف حالت «فقط‌خواندنی»؛ ابزارهای نوشتاری همیشه مجازند.
- `TehranClock`: بازنویسی مستقل (بدون `PersianPeriodKey`) — همان ساعت دیوارِ تهران
  با `PersianCalendar` برای مقایسهٔ ماه شمسی.
- `ProgramDataLayout`: از پوشهٔ Licensing به `Infrastructure/` منتقل شد (لازم برای
  پوشه‌های security/projects/logs) و مسیر License از آن حذف شد.
- `appsettings*.json`: حذف کامل بخش `Licensing` (شامل `SigningKeyId` و اثر انگشت).
- `Inventory.sln` / `Inventory.Api.csproj`: حذف پروژهٔ `Inventory.Licensing`.
- `tools/update/Update-App.ps1`: پیش‌فرض دروازهٔ لایسنس `Off` شد.
- کلاینت: حذف آیتم منو و بنر «وضعیت لایسنس».

## تنها بازماندهٔ عمدی

ستون `Users.LicenseSeatExempt` در دیتابیس نصب‌های موجود باقی می‌ماند، ولی دیگر هیچ
کاری نمی‌کند. نام پراپرتی به `SessionSeatExempt` تغییر کرد و با
`[Column("LicenseSeatExempt")]` به همان ستون نگاشت شده است تا **هیچ مایگریشنی لازم نباشد**.

جدول‌های `License*` در دیتابیس‌های قدیمی هم دست‌نخورده می‌مانند (کد دیگر به آن‌ها
کار ندارد). اگر می‌خواهید پاک شوند، پیش از حذف نسخهٔ پشتیبان بگیرید و سپس:

```sql
-- فقط برای پاک‌سازی کامل (اختیاری، غیرقابل بازگشت)
DROP TABLE IF EXISTS LicenseUsageLedger, LicenseUsageBaselines, LicenseUsages,
    LicenseItemRows, LicenseGrants, LicenseInstallations, LicenseReservations,
    LicenseGrandfatherBaselines, LicenseSessions, LicenseRuntime, LicenseEvents;
```

## پیامدهای عملی

1. دیگر هیچ مرحلهٔ «انتشار کلید» و «تأیید جاسازی کلید» در خط بیلد/نشر نیست.
2. `run-server.bat` و `appsettings.Production.json` دیگر به کلید یا شناسهٔ کلید کاری ندارند.
3. فایل‌های لایسنس قبلی (`.lic`) روی نصب‌های موجود بی‌اثر و بی‌خطرند.
4. `/api/health` دیگر فیلد `licensing` ندارد؛ `Update-App.ps1` هم دیگر به آن تکیه نمی‌کند.
