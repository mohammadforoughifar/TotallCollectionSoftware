# فهرست دقیق اکشن‌های مجاز در حالت فقط‌خواندنی

> **سند مرجع بازبینی چهارم — مورد ۴ («تطبیق کاتالوگ»).**
> این فایل از کد تولید شده است، نه نوشتهٔ دستی. منبع: پیمایش `src/Inventory.Api` +
> ویژگی `[AllowInReadOnly]` + نگاشت `LicenseModuleRegistry`.
> تست `tests/LicensingDb` بخش ۱۸ همین فهرست را با Reflection روی اسمبلی واقعی مقایسه می‌کند،
> پس اگر کسی بی‌صدا اکشنی را باز کند، تست قرمز می‌شود.

---

## ۱) حساب‌وکتاب عددها — ۷۳ و ۷۰ و ۶۸ و ۶۲

| عدد | معنا | منبع |
|---|---|---|
| **۹۸۰** | کل اکشن‌های HTTP در ۱۲۶ کنترلر | فهرست‌برداری کامل (Stage ۲) |
| **۷۳** | تعداد **ویژگی** `[AllowInReadOnly]` پیش از بازبینی چهارم | شمارش ویژگی روی اکشن‌ها |
| **۷۰** | همان ۷۳، بعد از حذف تکرارها | ۷۳ − ۳ (سه جفت کنترلر هم‌نام) |
| **۶۸** | شمارش دستیِ دسته‌بندی‌شده (۴۲ + ۲۰ + ۲ + ۴) | ⚠️ سند قبلی |
| **۶۲** | ویژگی `[AllowInReadOnly]` **امروز** | این فایل |
| **۵۹** | همان ۶۲ بعد از حذف تکرارها | ۵۹ = ۶۲ − ۳ |

### چرا ۷۳ شد ۷۰؟

سه جفت کنترلر **هم‌نام** در دو namespace وجود دارد؛ هر جفت دو ویژگی جدا دارد ولی یک نام
کوتاه می‌سازد:

- `Inventory.Api.Controllers.Core.AuthController` و `Inventory.Api.Modules.RadisHr.Controllers.AuthController`
  ⇒ `AuthController.Login` و `AuthController.ChangePassword` هر کدام **×۲**
- دو `FilesController` ⇒ `FilesController.Upload` **×۲**

۷۳ − ۳ = **۷۰** ✔ همان «۷۰ یکتا»ی گزارش قبلی.

### چرا ۷۰ شد ۶۲؟

بازبینی چهارم، **۱۱ اکشن** `AiController` را از این فهرست بیرون برد (مورد ۳):
`briefing/send-now`, `digest/send-now`, `feedback/submit`, `letters/draft`,
`letters/{id}/summarize`, `letters/{id}/suggest-referral`, `letters/{id}/extract-tasks`,
`letters/{id}/categorize`, `letters/{id}/draft-reply`, `letters/suggest-receivers`, `minutes/draft`.

۷۰ − ۱۱ = **۵۹** یکتا / **۶۲** خام ✔

### پس آن ۶۸ از کجا آمده بود؟

۶۸ = ۴۲ (زیرساخت) + ۲۰ (POST خواندنی) + ۲ (لایسنس) + ۴ (داشبورد). این یک **شمارش دستی
دسته‌بندی‌شده** بود، نه شمارش مکانیکی؛ و در آن، **دو ردیف جا افتاده بود** (یکی از دو
`AuthController`ِ هم‌نام و یکی از ردیف‌های دستهٔ لایسنس). اختلاف ۷۰ − ۶۸ = ۲ دقیقاً همان دو است.
عددِ معتبر همان چیزی است که این فایل و تست با Reflection می‌شمارند — نه شمارش دستی.

> 💡 درسِ این مورد: هر عددی که «دستی» ساخته شود بالاخره یک ردیف جا می‌گذارد. راه‌حل دائمی،
> همین فایلِ تولیدشده + تست قفل‌کننده است.

---

## ۲) ترکیب فعلی (62 ویژگی، 59 یکتا)

| دسته | تعداد |
|---|---|
| همیشه‌فعال | 32 |
| خواندنی با بدنهٔ POST | 28 |
| دستیار | 2 |
| **جمع** | **62** |

| فعل | تعداد |
|---|---|
| `Post` | 55 |
| `Delete` | 4 |
| `Put` | 3 |

**هیچ ویژگی روی `GET` نیست (۰ مورد).** طبیعی است: `GET` هرگز بسته نمی‌شود؛ این ویژگی
فقط جایی معنا دارد که «فعل» برای تشخیص نوشتن کافی نیست.

---

## ۳) فهرست کامل — `Controller.Action`

| # | دسته | `Controller.Action` | فعل | مسیر | ماژول | چرا در حالت فقط‌خواندنی مجاز است |
|---|---|---|---|---|---|---|
| 1 | خواندنی با بدنهٔ POST | `ConfirmDownloadDtoController.ConfirmDownload` | Post | `{id:int}/confirm-download` | — | خواندنی/محاسباتی؛ ردیف دادهٔ کسب‌وکار نمی‌نویسد. |
| 2 | خواندنی با بدنهٔ POST | `ConnectionDtoController.TestConnection` | Post | `connection/test` | — | خواندنی/محاسباتی؛ ردیف دادهٔ کسب‌وکار نمی‌نویسد. |
| 3 | خواندنی با بدنهٔ POST | `DocCollabController.DuplicateCheck` | Post | `duplicate-check` | DocArchive | بررسی تکراری‌بودن سند — نتیجه برمی‌گرداند. |
| 4 | خواندنی با بدنهٔ POST | `DocFavoritesController.Status` | Post | `favorites/status` | DocArchive | خواندن وضعیت «موردعلاقه». |
| 5 | خواندنی با بدنهٔ POST | `DocSearchController.AdvancedSearch` | Post | `search` | DocArchive | جست‌وجوی پیشرفته در آرشیو — خواندنی. |
| 6 | خواندنی با بدنهٔ POST | `DocSearchController.SearchPage` | Post | `search-page` | DocArchive | صفحهٔ نتایج جست‌وجو — خواندنی. |
| 7 | خواندنی با بدنهٔ POST | `DocSemanticController.SemanticSearch` | Post | `semantic-search` | DocArchive | جست‌وجوی معنایی — خواندنی. |
| 8 | خواندنی با بدنهٔ POST | `DocSemanticController.Suggest` | Post | `suggest` | DocArchive | پیشنهاد مرتبط‌ها از اسناد موجود — خواندنی. |
| 9 | خواندنی با بدنهٔ POST | `DocSettingsController.RunOnce` | Post | `code-numbering/run-once` | DocArchive | اجرای یک‌بارهٔ شماره‌گذاری کدها — تنظیمات آرشیو. |
| 10 | خواندنی با بدنهٔ POST | `EmailController.Test` | Post | `test` | Letters | ایمیل آزمون به خودِ کاربر برای بررسی SMTP. |
| 11 | خواندنی با بدنهٔ POST | `FaComController.CheckAnniversaries` | Post | `anniversaries/check` | FahamCom | زمان‌بندی تبریک سالگرد — دادهٔ تازهٔ کسب‌وکار نمی‌سازد. |
| 12 | خواندنی با بدنهٔ POST | `FaComController.CheckBirthdays` | Post | `birthdays/check` | FahamCom | زمان‌بندی تبریک تولد — همان. |
| 13 | خواندنی با بدنهٔ POST | `FaComController.CheckDue` | Post | `announcements/check-due` | FahamCom | بررسی اطلاعیه‌های سررسیدشده — همان. |
| 14 | خواندنی با بدنهٔ POST | `FaLmsController.Suggest` | Post | `perf/suggest` | FahamLms | پیشنهاد محتوای آموزشی — خواندنی. |
| 15 | خواندنی با بدنهٔ POST | `HrCoreController.CheckAlerts` | Post | `contract-alerts/check` | HrCore | بررسی هشدارهای قرارداد — خواندنی/زمان‌بندی. |
| 16 | خواندنی با بدنهٔ POST | `HrCoreController.RunReport` | Post | `reports/run` | HrCore | اجرای گزارش پرسنلی موجود — خواندنی. |
| 17 | خواندنی با بدنهٔ POST | `LinkRequestController.RunExpiryCheck` | Post | `/api/doc-archive/expiry-check` | — | خواندنی/محاسباتی؛ ردیف دادهٔ کسب‌وکار نمی‌نویسد. |
| 18 | خواندنی با بدنهٔ POST | `MoadianController.PreviewSendInvoiceAsync` | Post | `PreviewSendInvoiceAsync` | Moadian | پیش‌نمایش صورتحساب مودیان — بدون ارسال (ارسال واقعی متریک جدا دارد و بسته است). |
| 19 | خواندنی با بدنهٔ POST | `OfficeMachineAgentController.Printers` | Post | `printers` | OfficeAssets | فهرست چاپگرهای دستگاه — خواندنی. |
| 20 | خواندنی با بدنهٔ POST | `OfficeMachinesController.TestSnmp` | Post | `snmp/test` | OfficeAssets | خواندنی/محاسباتی؛ ردیف دادهٔ کسب‌وکار نمی‌نویسد. |
| 21 | خواندنی با بدنهٔ POST | `OrdersController.PreviewChequeReturn` | Post | `{id:int}/cheque-return-preview` | Sales | پیش‌نمایش برگشت چک — بدون ثبت. |
| 22 | خواندنی با بدنهٔ POST | `ReportStudioController.Preview` | Post | `preview` | Reports | پیش‌نمایش گزارش — بدون ذخیره. |
| 23 | خواندنی با بدنهٔ POST | `ReportStudioController.Run` | Post | `reports/{id:int}/run` | Reports | اجرای گزارش موجود — خروجی موقت. |
| 24 | خواندنی با بدنهٔ POST | `SaveDtoController.TestConnection` | Post | `{id:int}/test` | — | خواندنی/محاسباتی؛ ردیف دادهٔ کسب‌وکار نمی‌نویسد. |
| 25 | خواندنی با بدنهٔ POST | `ShopPricingController.Preview` | Post | `preview` | Catalog | پیش‌نمایش قیمت‌گذاری — بدون ذخیره. |
| 26 | خواندنی با بدنهٔ POST | `TestInputController.TestFormula` | Post | `items/test` | — | خواندنی/محاسباتی؛ ردیف دادهٔ کسب‌وکار نمی‌نویسد. |
| 27 | خواندنی با بدنهٔ POST | `VapidApplyInputController.PushStatus` | Post | `push-status` | — | خواندنی/محاسباتی؛ ردیف دادهٔ کسب‌وکار نمی‌نویسد. |
| 28 | خواندنی با بدنهٔ POST | `VapidApplyInputController.TestPush` | Post | `test-push` | — | خواندنی/محاسباتی؛ ردیف دادهٔ کسب‌وکار نمی‌نویسد. |
| 29 | دستیار | `AiController.Chat` | Post | `chat` | Ai | گفتگوی دستیار: تنها کانالِ توضیح «چرا ثبت بسته است». در حالت فقط‌خواندنی هیچ تماس با مدل بیرونی زده نمی‌شود و همهٔ ابزارهای نوشتاری خاموش‌اند (AI-TOOLS-FA.md). |
| 30 | دستیار | `AiController.DeleteConversation` | Delete | `conversations/{id:int}` | Ai | حذف حافظهٔ گفتگوی خودِ کاربر؛ دادهٔ کسب‌وکار نیست. |
| 31 | همیشه‌فعال | `AuthController.ChangePassword` | Post | `change-password` | Core | امنیت حساب کاربر مستقل از لایسنس است؛ بستنش کاربر با رمز افشاشده را گیر می‌اندازد. |
| 32 | همیشه‌فعال | `AuthController.ChangePassword` | Post | `change-password` | Core | امنیت حساب کاربر مستقل از لایسنس است؛ بستنش کاربر با رمز افشاشده را گیر می‌اندازد. |
| 33 | همیشه‌فعال | `AuthController.Login` | Post | `login` | Core | ورود باید همیشه کار کند؛ وگرنه مدیر نمی‌تواند فایل لایسنس را بارگذاری کند. |
| 34 | همیشه‌فعال | `AuthController.Login` | Post | `login` | Core | ورود باید همیشه کار کند؛ وگرنه مدیر نمی‌تواند فایل لایسنس را بارگذاری کند. |
| 35 | همیشه‌فعال | `AuthController.ResetPassword` | Post | `reset-password` | Core | بازیابی رمز — همان استدلال امنیتی؛ بدون آن کاربر قفل‌شده بیرون می‌ماند. |
| 36 | همیشه‌فعال | `AuthController.UploadUserPhoto` | Post | `users/photo/{id:int}` | Core | تصویر پروفایل؛ دادهٔ کسب‌وکار نیست. |
| 37 | همیشه‌فعال | `AuthController.UploadUserSignature` | Post | `users/signature/{id:int}` | Core | امضای شخصی کاربر؛ بدون آن نامهٔ قبلاً ثبت‌شده قابل امضا نمی‌ماند. |
| 38 | همیشه‌فعال | `BackupController.Retry` | Post | `jobs/{id}/retry` | Backup | تلاش دوبارهٔ پشتیبان‌گیری ناموفق — همان دلیل. |
| 39 | همیشه‌فعال | `BackupController.Run` | Post | `run` | Backup | پشتیبان‌گیری دستی — «هیچ داده‌ای نباید قفل شود»؛ بستنش تنها راه خروج داده‌ها را می‌بندد. |
| 40 | همیشه‌فعال | `BackupController.SaveSettings` | Put | `settings` | Backup | تنظیمات مقصد پشتیبان — زیرمجموعهٔ «دسترسی به دادهٔ خودت». |
| 41 | همیشه‌فعال | `BackupController.Test` | Post | `test` | Backup | آزمایش مقصد پشتیبان؛ دادهٔ کسب‌وکار نمی‌نویسد. |
| 42 | همیشه‌فعال | `BackupSessionController.Close` | Post | `close` | Backup | بستن همان نشست. |
| 43 | همیشه‌فعال | `BackupSessionController.Heartbeat` | Post | `heartbeat` | Backup | ضربان نشست پشتیبان‌گیری؛ بدون آن نشست بسته و نسخهٔ پشتیبان ناقص می‌شود. |
| 44 | همیشه‌فعال | `CentralBackupController.DeleteCompany` | Delete | `companies/{id:int}` | Backup | **استثنای صریح** — تنظیمات زیرساخت، نه دادهٔ کسب‌وکار؛ حذف سهمیه آزاد نمی‌کند. توضیح کامل در §۳ پایین. |
| 45 | همیشه‌فعال | `CentralBackupController.DeleteReceipt` | Delete | `receipts/{id:int}` | Backup | **استثنای صریح** — رکورد رسید زیرساختی؛ همان استدلال §۳. |
| 46 | همیشه‌فعال | `CentralBackupController.RotateKey` | Post | `companies/{id:int}/rotate-key` | Backup | **استثنای صریح امنیتی** — چرخش کلید API. بستنش یعنی کلیدِ لو‌رفته تا خرید لایسنس معتبر می‌ماند. توضیح کامل در §۳. |
| 47 | همیشه‌فعال | `CentralBackupController.SaveCompany` | Post | `companies` | Backup | ثبت/ویرایش شرکت راه‌دور — تنظیمات زیرساخت. |
| 48 | همیشه‌فعال | `CentralBackupController.Upload` | Post | `upload` | Backup | دریافت نسخهٔ پشتیبان از شرکت راه‌دور — عملِ «بازگرداندن داده»، نه ساختن دادهٔ تازه. |
| 49 | همیشه‌فعال | `CompanyContextController.SetUserCompanies` | Put | `users/{userId:int}` | Core | تعیین شرکت‌های مجاز کاربر — زیرساخت دسترسی. |
| 50 | همیشه‌فعال | `DashboardController.HardwareCheck` | Post | `hardware-check` | Dashboard | یک دور بررسی سخت‌افزار؛ فقط آمار را تازه می‌کند. |
| 51 | همیشه‌فعال | `DashboardsController.Create` | Post | `—` | Dashboard | داشبورد شخصی — چیدمان نمایش روی داده‌های موجود. |
| 52 | همیشه‌فعال | `DashboardsController.Delete` | Delete | `{id:int}` | Dashboard | حذف داشبورد شخصی. |
| 53 | همیشه‌فعال | `DashboardsController.SetDefault` | Post | `{id:int}/default` | Dashboard | انتخاب داشبورد پیش‌فرض. |
| 54 | همیشه‌فعال | `DashboardsController.Update` | Put | `{id:int}` | Dashboard | ویرایش داشبورد شخصی. |
| 55 | همیشه‌فعال | `FilesController.Upload` | Post | `upload/{module}` | Core | بارگذاری فایل ضمیمه؛ خودش دادهٔ کسب‌وکار نمی‌سازد. |
| 56 | همیشه‌فعال | `FilesController.Upload` | Post | `—` | Core | بارگذاری فایل ضمیمه؛ خودش دادهٔ کسب‌وکار نمی‌سازد. |
| 57 | همیشه‌فعال | `HttpsController.ExportPfx` | Post | `pfx` | Core | استخراج گواهی HTTPS — زیرساخت سرور. |
| 58 | همیشه‌فعال | `HttpsController.Regenerate` | Post | `regenerate` | Core | تولید دوبارهٔ گواهی خودامضا — بستنش دسترسی به سامانه را می‌شکند. |
| 59 | همیشه‌فعال | `LicenseController.SettleReservation` | Post | `reservations/{id:long}/settle` | Licensing | تسویهٔ دستی رزرو سهمیه توسط مدیر لایسنس — ابزار نگه‌داری خودِ لایسنس. |
| 60 | همیشه‌فعال | `NotificationsController.TestMessenger` | Post | `test-messenger` | Cartable | پیام آزمون به خودِ کاربر. |
| 61 | همیشه‌فعال | `UserProfileController.VerifyEmailOtp` | Post | `email/verify` | Core | تأیید ایمیل حساب — بخشی از ورود. |
| 62 | همیشه‌فعال | `UserProfileController.VerifyMobileOtp` | Post | `mobile/verify` | Core | تأیید موبایل حساب — بخشی از ورود. |

---

## ۴) دو موردی که بازبینی صریحاً پرسیده بود

### `CentralBackupController.DeleteCompany` و `DeleteReceipt`

**عمداً مجاز مانده‌اند.**

1. **دادهٔ کسب‌وکار نیستند، تنظیمات زیرساخت‌اند.** این رکوردها در دیتابیسِ مرکزیِ
   پشتیبان‌گیری‌اند، نه در دادهٔ عملیاتی مشتری.
2. **حذف، سهمیه آزاد نمی‌کند.** سقف روی «ایجاد» است و رکورد حذف‌شده هم شمرده می‌شود
   (`IgnoreQueryFilters`) ⇒ ابزار دور زدن لایسنس نیست. (مورد ۷ همین را تقویت کرد: مبنا
   `CreatedAt` است، پس حذف و ایجاد مکرر هم سقف را باز نمی‌کند.)
3. **بستنش فقط خرابی را نگه می‌دارد.** سناریو: رکورد شرکت با کد اشتباه ثبت شده و لایسنس
   منقضی است ⇒ تا خرید لایسنس، رکورد خراب می‌ماند.
4. **مرز:** این استثنا فقط «پاک‌کردن رکورد زیرساختی» است. هیچ اکشنی که *دادهٔ کسب‌وکار
   تازه* بسازد در این فهرست نیست.

### `CentralBackupController.RotateKey`

**عمداً مجاز مانده — دلیلش امنیتی است.**

چرخش کلید، پاسخ به «کلید API لو رفته». اگر در حالت بدون لایسنس بسته شود، کلید افشاشده
تا زمان خرید لایسنس معتبر می‌ماند؛ یعنی **قفل، مشتری را در معرض ریسک امنیتی نگه می‌دارد** —
دقیقاً برخلاف اصل «قفل نباید به مشتری آسیب بزند». کلید تازه هیچ متریکی را مصرف نمی‌کند و
رکورد دادهٔ کسب‌وکار نمی‌سازد.

---

## ۵) قواعدی که این فهرست را می‌بندند

- **فعل کافی نیست** — هر `POST/PUT/PATCH/DELETE` در `Missing` و `Invalid` بسته است، مگر
  اکشنی که در جدول بالا آمده باشد.
- **اکشن تازه بسته به‌دنیا می‌آید** (fail-closed): نبودِ ویژگی = بسته.
- **یکسان‌بودن پاسخ:** `403` + کد `LICENSE_READONLY` در بدنه + هدر `X-License-ReadOnly: 1`.
- **Grace محلی حذف شد** (مورد ۱): پنجرهٔ موقت فقط با «لایسنس پُل» امضاشده ممکن است، و آن هم
  یک سند `Valid` است — نه یک حالت سوم. تست بخش ۱۸ همین را اثبات می‌کند: حتی وقتی سرویس
  می‌گوید «در Grace هستم»، `POST` ساده همچنان ۴۰۳ می‌خورد.
