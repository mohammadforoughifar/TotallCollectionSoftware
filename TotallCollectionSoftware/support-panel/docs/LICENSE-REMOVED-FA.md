# حذف موتور لایسنس از پنل پشتیبانی — گزارش تغییر

> تصمیم محصول: نرم‌افزار هیچ لایسنس و سهمیه‌ای ندارد؛ پس پنل پشتیبانی هم دیگر
> «صادرکنندهٔ لایسنس» نیست. تمام مسیر امضا، کلید و تحویل سند از این پروژه حذف شد.

## چه چیزی حذف شد

| بخش | مسیر | توضیح |
|---|---|---|
| صفحات لایسنس | `Pages/Licenses/` (Index، Details، Download) | فهرست و جزئیات سند صادرشده، دانلود `.lic` |
| صفحات درخواست | `Pages/Requests/` (Index، Ingest، Details، Draft، Identity، Sign، Deliver) | گردش کامل درخواست تا امضا و تحویل |
| تمدیدها | `Pages/Renewals.cshtml` | فهرست لایسنس‌های نزدیک انقضا |
| سرویس‌ها | `RequestService`، `RequestIngest`، `DraftService`، `DeliveryService`، `CustomerTrustService`، `SigningRunner`، `SignerKeyService`، `LicenseToolRunner`، `LicenseInspector`، `Workflow` | ثبت/تجزیه درخواست، ساخت پیش‌نویس، امضا، تحویل، مجوز اعتماد، بازرسی `.lic` |
| پیکربندی امضا | `SignerOptions.cs` | جای آن `PanelOptions.cs` آمد (اتصال، پوشهٔ کاری، SMTP، پیش‌نمایش) |
| موجودیت‌ها | `LicenseRequest`، `RequestEvent`، `RequestAttachment`، `IdentityCheck`، `LicenseDraft`، `IssuedLicense`، `Delivery`، `Preset`، `Installation` | ۹ موجودیت مربوط به لایسنس |
| شمارشی‌ها | `RequestStatus`، `RequestType`، `RequestChannel`، `IdentityMethod`، `DeliveryChannel`، `LicenseStatus` | فقط `SupportRole` ماند |
| ارجاع به موتور | `<ProjectReference ../inventory/src/Inventory.Licensing>` و دو Target کپی `LicenseTool`/`presets.json` در `SupportPanel.csproj` | این ارجاع پس از حذف کتابخانه از مخزن، بیلد پنل را کامل می‌شکست |
| اسکریپت‌ها | `prepare.*`، `run.*`، `publish.sh` | دیگر ابزار امضا نمی‌سازند/کپی نمی‌کنند |
| اسناد و ابزار | `docs/SIGNING-KEY-CUSTODY-FA.md`، سه PDF راهنمای لایسنس، `tools/make-license-guide.py`، `make-approved-request.py`، `capture-user-mode.py`، `test-single-company.py` | راهنماها و عکس‌برداری‌های مخصوص لایسنس |

> فضای‌نام پروژه هم از `LicenseSigner` به `SupportPanel` تغییر کرد (csproj، کد، صفحات و مایگریشن‌ها).

## پنل بعد از حذف چه کار می‌کند

- **مشتریان**: فهرست/جست‌وجو، ثبت و ویرایش، شخص تماس، ادغام مشتری، ممیزی تغییرات.
- **کاربران پنل**: ساخت/ویرایش/غیرفعال‌سازی، نقش‌ها، تعویض اجباری گذرواژهٔ آغازین.
- **تنظیمات**: SMTP (رمز فقط از متغیر محیطی) و قالب ایمیل پاسخ.
- **ممیزی**: زنجیرهٔ هش فقط‌افزودی.
- **پشتیبان‌گیری**.

داشبورد هم به «مشتریان فعال، شخص‌های تماس، کاربران، آخرین رخدادهای ممیزی» تغییر کرد.

## دیتابیس

- مایگریشن‌های موجود **دست‌نخورده** ماندند؛ جدول‌های قدیمی لایسنس در دیتابیس‌های
  موجود باقی می‌مانند ولی دیگر نگاشت نمی‌شوند و کد به آن‌ها کاری ندارد.
- برای پاک‌سازی کامل (اختیاری، بعد از بکاپ): `docs/sql/drop-license-tables.sql`.

## تأیید

- `dotnet build SupportPanel.csproj -c Release` روی همین مخزن **موفق** است (بدون Warning
  مربوط به تایپ‌های حذف‌شده).
- اجرای واقعی پنل با SQLite و `GET /api/health` پاسخ `{"ok":true,"app":"SupportPanel"}` داد.
