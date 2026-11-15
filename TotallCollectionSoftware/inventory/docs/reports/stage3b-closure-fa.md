# گام ۳-ب — گزارش بسته‌شدن

شاخهٔ `lic/step-3b` · تاریخ 2026-11-14 · شاهدها: `docs/reports/evidence/stage3b-*.log`

## وضعیت

| مورد | وضعیت | شاهد |
|---|---|---|
| جدول `LicenseRuntime` (افزایشی، SQL Server + SQLite) | ✅ | `Data/LicenseSchemaV1.cs`, `Entities/Licensing/LicenseRuntime.cs` |
| ثبت `build.firstStart` در اولین استارت (با `ClockAnchor`) | ✅ | `Licensing/Services/LicenseRuntimeState.cs` |
| پذیرش توکن بدون `sid` فقط در پنجرهٔ ۱۳ ساعته | ✅ | `Licensing/Services/SeatService.cs` (شاخهٔ `sessionId` خالی) |
| رد بعد از پنجره با `LICENSE_SESSION_ENDED` + HTTP 401 | ✅ | همان + `SeatGrant.StatusFor` |
| ساخت `security`/`License`/`projects`/`logs` و بررسی نوشتن | ✅ | `Licensing/ProgramDataLayout.cs` |
| حذف فال‌بک بی‌صدا به `App_Data` | ✅ | `Licensing/LicensePaths.cs` |
| تست‌ها | ✅ | ۳۴۶ بررسی در `tests/LicensingDb` (بخش ۲۹)، ۱۲۹ در `tests/Licensing` |

## تصمیم‌ها و فرض‌ها

1. پنجرهٔ ۱۳ ساعت = حداکثر عمر توکن (۱۲ ساعت) + ۱ ساعت حاشیه؛ از «اولین استارت همین بیلد» شمرده می‌شود، نه از نصب لایسنس.
2. هیچ endpoint/تنظیماتی برای تمدید یا ریست پنجره ساخته نشد (خواستهٔ صریح: مشتری مهلت خودساخته ندارد).
3. مسیر پیش‌فرض ProgramData: `%ProgramData%\Totall\{security,projects,logs}` و `%ProgramData%\TotallCollection\License` (سازگار با نصب‌های موجود)؛ قابل تغییر با `TOTALL_DATA_ROOT` یا `Totall:DataRoot`.
4. مسئولیت ACL به گام ۶ منتقل شد؛ این‌جا فقط «قابل‌نوشتن بودن» سنجیده می‌شود.

## فایل‌های تغییرکرده

`Entities/Licensing/LicenseRuntime.cs` (نو) · `Licensing/ProgramDataLayout.cs` (نو) · `Licensing/Services/LicenseRuntimeState.cs` (نو) · `Data/AppDbContext.cs` · `Data/LicenseSchemaV1.cs` · `Licensing/Services/SeatService.cs` · `Licensing/LicensePaths.cs` · `Licensing/LicenseStartup.cs` · `Program.cs` · `tests/LicensingDb/Program.cs`

## آنچه روی ویندوز باید دستی آزموده شود

- ACL واقعی IIS (هویت App Pool) و رفتار `ProgramData` وقتی پوشه با دسترسی اشتباه از قبل وجود دارد.
- رفتار توکن قدیمی روی یک نصب واقعی با توکن صادرشده توسط نسخهٔ قبلی (اینجا فقط با ساعت لنگر و sid=null شبیه‌سازی شد).
