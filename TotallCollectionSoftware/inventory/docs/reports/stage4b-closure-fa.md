# گزارش بستن گام ۴-ب (۴.۲ تا ۴.۶) — لایسنس

**تاریخ:** ۲۰ آبان ۱۴۰۵ (2026-11-14) · **شاخه:** `lic/step-4b` (بر پایهٔ `lic/step-4` = `ae3c7017`)
**آخرین کامیت این گزارش:** `71e7bc4d`

## وضعیت

| بند | موضوع | وضعیت | شاهد |
|---|---|---|---|
| ۴.۲ | `GET /api/license/request-file` نسخهٔ ۲ (engine/build/Tehran/hashes) | ✅ | `evidence/stage4b-licensingdb-part2.log` (بخش ۳۲) |
| ۴.۳ | بایند نصب: ≥۲ مؤلفهٔ ثانویه ⇒ Warning + `rehostRecommended` | ✅ | همان، بخش ۳۲ |
| ۴.۴ | لنگر ساعت سه‌جایی + فقط‌خواندنی > ۲۴ ساعت + خودترمیمی | ✅ | `evidence/stage4b-licensingdb-part3.log` (بخش ۳۳) |
| ۴.۵ | حذف `DatabaseLicenseProvider` از OnPrem + فیلد `deployment` | ✅ | `evidence/verify-ver-onprem-OnPrem.log` … (۴ لاگ) |
| ۴.۶ | رفت‌وبرگشت امضا (payload مشترک ابزار/verifier) | ✅ (از قبل) | بخش ۳۰ |

**تست‌ها:** `tests/LicensingDb` **۴۰۳ PASS**، `tests/Licensing` **۱۵۹ PASS**، `tests/E2E` **۴۳ PASS**،
کلاینت/API در Debug و Release(OnPrem)/Release(SaaS) **۰ خطا**.

## چه چیزی اضافه شد

- **۴.۲** فایل درخواست حالا `requestVersion: 2` است: `engineVersion`، `engineSchema`، `buildDate`،
  `components` (هش سه مؤلفهٔ ثانویه)، `requestedAt` به وقت تهران + `requestedAtTehranZone` + `requestedAtUtc`.
  قاعدهٔ دسترسی همان است (Admin وقتی معتبر، آزاد در Missing/Invalid) و محدودیت نرخ هم همان.
- **۴.۳** `InstallationBinding`: خط پایهٔ مؤلفه‌ها (نام ماشین، MachineGuid/`/etc/machine-id`، نام سرور SQL)
  به شکل **هش ۱۶ کاراکتری** در `LicenseInstallations.MachineFingerprint` ذخیره می‌شود. یک مؤلفهٔ عوض‌شده
  طبیعی است؛ **دو یا بیشتر** ⇒ رخداد `INSTALL_BINDING_WARNING` (یک‌بار برای هر مجموعه) + پرچم
  `rehostRecommended` در `status`. هیچ مهلت/قفل/غیرفعال‌سازی ساخته نمی‌شود و GUID نصب دست‌نخورده می‌ماند.
- **۴.۴** `ClockGuard`: بالاترین زمان معتبر در **سه ذخیره‌گاه** (LicenseRuntime در دیتابیس، فایل ProgramData،
  رجیستری HKCU — روی لینوکس فایل خانهٔ کاربر) با **AES-GCM** (محرمانه + احراز اصالت، کلید per-installation).
  لنگر = max(سه ذخیره‌گاه، بیشترین `LicenseEvent.At`، `OutgoingLetter/InnerLetter.CreatedAt`، `Invoice.DateOrder`).
  «اکنون مؤثر» = max(ساعت سیستم، لنگر). ≤ ۲۴ ساعت ⇒ `CLOCK_SET_BACK` + بنر؛ > ۲۴ ساعت ⇒
  `CLOCK_SET_BACK_BLOCK` + فقط‌خواندنی از طریق همان `LicenseAccessState.IsReadOnly` که HTTP و هاب
  استفاده می‌کنند. با رسیدن ساعت واقعی به لنگر، وضعیت **خودکار** Normal و نوشتن باز می‌شود.
  کارگر `ClockGuardWorker` هر ۱۰ دقیقه و همچنین در `status` سنجش می‌کند.
- **۴.۵** `verify-licensing.ps1` بخش «۲-ب»: پویش بایت‌های `Inventory.Api.dll` برای `DatabaseLicenseProvider`
  (OnPrem باید صفر، SaaS باید داشته باشد) + وجود فیلد `deployment` در موتور.
  سوئیچ `-AllowNoProductionKey` فقط برای بازبینی ساختاری پیش از تولید کلید (در انتشار واقعی داده نمی‌شود).
- `GET /api/license/events` (فقط مدیر) برای صفحهٔ گام ۵.

## اثبات ۴.۵ (pwsh 7.4.6 روی لینوکس)

| خروجی | انتظار | نتیجه |
|---|---|---|
| `ver-onprem` + `-Deployment OnPrem` | قبول | exit 0 ✅ |
| `ver-saas` + `-Deployment SaaS` | قبول | exit 0 ✅ |
| `ver-onprem` + `-Deployment SaaS` | رد | exit 7 ✅ («DatabaseLicenseProvider: نیست») |
| `ver-saas` + `-Deployment OnPrem` | رد | exit 7 ✅ («DatabaseLicenseProvider: هست») |

## فایل‌های تغییر‌یافته (این زیرگام)

`src/Inventory.Api/Licensing/Services/InstallationBinding.cs` (نو)، `.../ClockGuard.cs` (نو)،
`.../ClockGuardWorker.cs` (نو)، `Licensing/Infrastructure/LicenseAccessState.cs`،
`Licensing/Controllers/LicenseController.cs`، `Licensing/LicenseStartup.cs`،
`tools/verify-licensing.ps1`، `tests/LicensingDb/Program.cs` (بخش ۳۲ و ۳۳)،
`tests/Licensing/*` (بخش ۲۷)، `tests/E2E/*` (نو).

## تست جهش (فقط دو مورد جدید، طبق قاعده)

1. **ساعت:** تغییر یک بیت در ciphertext لنگر ⇒ مقدار رد می‌شود و ذخیره‌گاه بازنویسی می‌شود (بخش ۳۳).
2. **اعمال انقضا با ساعت لنگر:** ۳۰ ساعت عقب‌بردن ساعت ⇒ `WritesBlocked` و `IsReadOnly(Valid) = true`
   حتی با لایسنس معتبر (بخش ۳۳ و E2E سناریو ۵).

## فرض‌ها

- حاشیهٔ نوسان ساعت ۱۰ دقیقه و آستانهٔ قفل ۲۴ ساعت، ثابت در کد (`ClockGuard.Tolerance/BlockThreshold`).
- هش مؤلفه‌ها (نه مقدار خام) نگه‌داری می‌شود؛ مؤلفهٔ خوانده‌نشده در مقایسه شمرده نمی‌شود تا خطای خواندن،
  «جابه‌جایی نصب» گزارش نشود.
- فهرست منابع «بیشترین زمان داده» بسته است (رخداد لایسنس، نامهٔ داخلی/وارده، تاریخ فاکتور)؛ افزودن منبع تازه
  فقط با آگاه‌شدن از هزینهٔ پرس‌وجو.
- اعلان‌های ساعت بر پایهٔ «مرتبهٔ» مشکل است (نه مقدار دقیق)، تا عقب‌رفتن پیوسته، جدول رخداد را پر نکند.
- `LicenseTool` همچنان تنها ابزار رسمی امضا است؛ `tools/license-sign.py` ابزار توسعه باقی می‌ماند.
