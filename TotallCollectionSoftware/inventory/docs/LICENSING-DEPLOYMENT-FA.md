# حالت استقرار: SaaS و OnPrem

این سند دو بستهٔ انتشار و راه جدا نگه‌داشتن‌شان را توضیح می‌دهد. هدف: بستهٔ اشتباه
منتشر نشود، و اگر شد، همان لحظهٔ انتشار گرفته شود.

## ۱. تفاوت واقعی دو بسته: یک ثابت کامپایل

| | OnPrem (نصب روی سرور مشتری) | SaaS (میزبانی‌شده) |
|---|---|---|
| ثابت کامپایل | — | `SAAS` |
| `DatabaseLicenseProvider` | **کامپایل نمی‌شود** (`#if SAAS`) | هست |
| منبع لایسنس | فایل امضاشده روی دیسک | جدول‌های لایسنس در دیتابیس |
| Publish Profile | `Properties/PublishProfiles/OnPrem.pubxml` | `Properties/PublishProfiles/SaaS.pubxml` |

هر دو خطای ممکن بی‌صدا است:

- بستهٔ **OnPrem** با `SAAS` ⇒ سامانه سراغ جدول‌های لایسنس می‌رود که روی نصب مشتری نیست.
- بستهٔ **SaaS** بدون `SAAS` ⇒ Provider دیتابیسی وجود ندارد و ردیف لایسنس نادیده می‌ماند.

## ۲. انتشار

```bat
publish-server.bat onprem      :: پیش‌فرض
publish-server.bat saas
```

اسکریپت، Publish Profile متناظر را پاس می‌دهد و در پایان
`verify-licensing.ps1 -Deployment <SaaS|OnPrem>` را اجرا می‌کند.

## ۳. بررسی خودکار (کد خروج ۷)

```powershell
powershell -File tools\verify-licensing.ps1 -AppPath .\deploy\inventory-server -Deployment SaaS
```

یا مستقیم:

```
LicenseCheck --deployment --app <پوشهٔ خروجی> [--expect saas|onprem]
```

تشخیص، از **متادیتای** `Inventory.Api.dll` است (`PEReader`/`MetadataReader`)، بدون
اجرای کد و بدون بارگذاری وابستگی‌ها؛ وجود نوع
`Inventory.Api.Licensing.Providers.DatabaseLicenseProvider` یعنی بیلد SaaS است.
اگر با `--expect` نخواند ⇒ کد خروج **۷** و انتشار متوقف می‌شود.

## ۴. کلید پیامک (کاوه‌نگار) بیرون از گیت

`KavenegarSms:ApiKey` در `appsettings.json` **خالی** است. منبع کلید:

1. متغیر محیطی `KavenegarSms__ApiKey` (اولویت اول، روی سرور توصیه می‌شود)
2. `appsettings.Local.json` در پوشهٔ اجرا (در `.gitignore` است)

اگر کلید خالی باشد، ارسال پیامک **غیرفعال** می‌شود و در شروع برنامه یک هشدار روشن
در لاگ ثبت می‌گردد (`[SMS] کلید KavenegarSms:ApiKey تنظیم نشده است`). این خطا نیست و
هیچ مسیر کسب‌وکاری نمی‌شکند؛ فقط پیامکی ارسال نمی‌شود.

> ⚠️ اگر کلیدی در نسخه‌های قبلی داخل `appsettings.json` بوده، فقط از فایل حذف شدن
> آن را از تاریخچهٔ گیت پاک نمی‌کند: باید در پنل کاوه‌نگار **باطل/چرخش** شود.
