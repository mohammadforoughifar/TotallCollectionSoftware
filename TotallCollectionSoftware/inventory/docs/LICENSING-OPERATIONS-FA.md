# راهنمای بهره‌برداری لایسنس (فارسی)

> مخاطب: تیم پشتیبانی/نصب. همهٔ دستورها بدون ویرایش سورس قابل اجرا هستند.
> ⚠️ در هیچ گزارش/تیکت/چت، **مقدار** کلید خصوصی یا توکن نگذارید؛ فقط مسیر فایل.

## ۱) نقشهٔ سریع

| کار | ابزار | خروجی |
|---|---|---|
| ساخت کلید امضا | `tools/LicenseTool keygen` | `*.private.pem` (رمزدار) + `*.pem` عمومی |
| صدور لایسنس | `tools/LicenseTool issue` | فایل `.lic` امضاشده |
| لایسنس «پل» ۳۰ روزه | `tools/LicenseTool bridge --days 30` | `.lic` با ۳۳ ماژول |
| بازبینی سند | `tools/LicenseTool preview` / `verify` | جدول ماژول‌ها و وضعیت امضا |
| بررسی نصب | `tools/LicenseCheck --keys/--deployment/--prepare` | کد خروج ۰/۷ |
| به‌روزرسانی سرور | `tools/update/update.bat` | `ProgramData\Totall\logs\update-*.log` |
| بررسی بستهٔ منتشرشده | `tools/verify-licensing.ps1` | کد خروج ۰–۷ |

## ۲) چرخهٔ فعال‌سازی روی سرور مشتری

1. مدیر وارد `settings/license` می‌شود و **«دریافت فایل درخواست»** را می‌زند.
2. فایل `license-request.json` برای پشتیبانی فرستاده می‌شود (شامل `installationId`، نسخهٔ موتور،
   تاریخ تهران، و **هش** سه مؤلفهٔ ماشین — نه مقدار خام).
3. پشتیبانی: `LicenseTool issue --profile FIN --request <file> --private-key <key> --out customer.lic --yes`
4. مدیر فایل `.lic` را در همان صفحه آپلود می‌کند (چند فایل هم‌زمان مجاز؛ نتیجهٔ هر فایل جداست).
5. بررسی: وضعیت `Valid`، ماژول‌های خریداری‌شده باز، `canWrite = true`.

## ۳) کدهای خطا و رفتار درست

| کد | HTTP | معنا | کار درست |
|---|---|---|---|
| `LICENSE_READONLY` | 403 | نوشتن بسته (بدون لایسنس / نامعتبر / ساعت عقب) | بررسی `settings/license`؛ ساعت سرور را درست کنید |
| `MODULE_DISABLED` | 403 | ماژول در لایسنس نیست | فروش/ارتقای بسته، سپس آپلود سند تازه |
| `QUOTA_EXCEEDED` | 429 | سقف دوره پر شده | تا دورهٔ بعد یا ارتقای سقف؛ رزروهای معلق را تسویه کنید |
| `SEAT_FULL` / `LICENSE_SEAT_FULL` | 403 | ظرفیت کاربران پر است | غیرفعال‌کردن حساب یا افزایش صندلی |
| `SEAT_ENDED` / `LICENSE_SEAT_ENDED` | 401 | نشست کاربر پایان یافته | ورود دوبارهٔ همان کاربر (خروج از حساب لازم نیست) |
| `LICENSE_SESSION_ENDED` | 401 | توکن بدون `sid` بعد از پنجرهٔ ۱۳ ساعته | ورود دوباره |
| `CLOCK_SET_BACK` | – | ساعت ≤ ۲۴ ساعت عقب | تنظیم NTP؛ نوشتن باز است |
| `CLOCK_SET_BACK_BLOCK` | – | ساعت > ۲۴ ساعت عقب | ساعت را درست کنید؛ قفل **خودکار** باز می‌شود |
| `INSTALL_BINDING_WARNING` | – | ≥۲ مؤلفهٔ ثانویه عوض شده | اگر سرور جابه‌جا شده، لایسنس را دوباره ببندید |
| `LICENSE_SIGNATURE_INVALID` | 400 | امضا/ساختار خراب | فایل را دوباره از پشتیبانی بگیرید |

## ۴) کدهای خروج `Update-App.ps1`

| کد | معنا | تغییر انجام‌شده؟ |
|---|---|---|
| 0 | موفق | بله |
| 10 | بستهٔ نصب ناقص (بدون `package-info.json`/فایل اجرایی) | هیچ |
| 20 | دروازهٔ لایسنس رد شد | هیچ |
| 30 | پیش‌بررسی (مسیر/فضای دیسک/قابل‌نوشتن‌بودن) | هیچ |
| 40 | پشتیبان‌گیری شکست خورد | هیچ |
| 50 | توقف سرویس شکست خورد | `app_offline.htm` گذاشته شد |
| 60 | کپی فایل‌ها شکست خورد | برنامه از پشتیبان قابل بازگردانی |
| 70 | `--update-database` شکست خورد | برنامه از پشتیبان برگشت؛ دیتابیس فقط با `-RestoreDbOnFailure` |
| 80 | بررسی سلامت ناموفق | برنامه از پشتیبان برگشت |
| 90 / 91 / 95 | پارامتر / اجرای هم‌زمان / خطای غیرمنتظره | هیچ |

اجرا:
```bat
tools\update\update.bat D:\pkg\2026-11-14 C:\inetpub\Totall -HealthUrl http://localhost/api/health
```
پیش از اجرا: `appsettings.json` مشتری **بازنویسی نمی‌شود**؛ `App_Data`، `License`، `logs`، `uploads` دست‌نخورده‌اند.

## ۵) چک‌لیست انتشار

- [ ] `publish-server.bat onprem` (یا `saas`) با موفقیت تمام شد
- [ ] `tools\verify-licensing.ps1 -AppPath <publish> -Deployment OnPrem` ⇒ کد خروج ۰
- [ ] `package-info.json` در بسته هست و نسخه/حالت درست است
- [ ] `tools\LicenseCheck` در بسته هست، `LicenseTool` **نیست**
- [ ] پشتیبان دیتابیس تازه + تست بازگردانی روی محیط آزمایش
- [ ] `Update-App.ps1` روی محیط آزمایش با `-DryRun` و سپس واقعی
- [ ] `settings/license` بعد از به‌روزرسانی: `Valid` و `canWrite = true`
- [ ] لاگ `update-*.log` بدون `[ERROR]`

## ۶) کارهایی که باید دستی روی ویندوز تست شوند

این موارد در محیط توسعهٔ لینوکس قابل آزمون نبودند و در چک‌لیست دستی هستند:
ACL با `icacls` روی `ProgramData\Totall\{security,License,projects,logs}`، توقف/شروع App Pool با
`appcmd`/`WebAdministration`، اجرای اسکریپت با **Windows PowerShell 5.1** (نه فقط ۷)، و
`BACKUP DATABASE`/`RESTORE` روی نمونهٔ واقعی SQL Server.
