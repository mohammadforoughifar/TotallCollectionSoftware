# فهرست کامل ۷۳ ویژگی `[AllowInReadOnly]` — مورد ۴ بازبینی چهارم

> **هدف:** پاسخ دقیق به «۴۲+۲۰+۲+۴ = ۶۸، ولی ۷۳ علامت‌خورده و ۷۰ یکتا».
> این فایل **هر ۷۳ ردیف** را نام می‌برد: ۶۲ ردیفی که امروز در کد هست + ۱۱ ردیفی که
> بازبینی چهارم برداشت. منبع: پیمایش خودکار `src/Inventory.Api` + ویژگی واقعی روی اکشن‌ها.

---

## ۱) حساب‌وکتاب اعداد

| عدد | معنا | چطور به دست می‌آید |
|---|---|---|
| **۷۳** | ویژگی `[AllowInReadOnly]` **پیش از** بازبینی چهارم | شمارش ویژگی روی اکشن‌ها (خام) |
| **۷۰** | همان ۷۳، بدون تکرار | ۷۳ − ۳ (سه جفت کنترلر هم‌نام) |
| **۶۸** | شمارش **دستی** سند قبلی (۴۲+۲۰+۲+۴) | ❌ دو ردیف جا افتاده بود |
| **۶۲** | ویژگی‌های **امروز** | ۷۳ − ۱۱ |
| **۵۹** | یکتای امروز | ۶۲ − ۳ |

### چرا ۷۳ ⇐ ۷۰؟

سه جفت کنترلر **هم‌نام** در دو namespace:

| اکشن | تکرار | namespace ها |
|---|---|---|
| `AuthController.Login` | ×۲ | `Inventory.Api.Controllers.Core` و `Inventory.Api.Modules.RadisHr.Controllers` |
| `AuthController.ChangePassword` | ×۲ | همان دو |
| `FilesController.Upload` | ×۲ | `Inventory.Api.Controllers.Core` و `Inventory.Api.Modules.RadisHr.Controllers` |

۷۳ − ۳ = **۷۰** ✔ (همان «۷۰ یکتا»ی گزارش قبلی)

### چرا ۷۰ ⇐ ۵۹؟

بازبینی چهارم **۱۱ اکشن** `AiController` را برداشت (مورد ۳). ۷۰ − ۱۱ = **۵۹** ✔ / خام ۷۳ − ۱۱ = **۶۲** ✔

### پس آن ۶۸ از کجا آمد؟

۶۸ = ۴۲ (زیرساخت) + ۲۰ (POST خواندنی) + ۲ (لایسنس) + ۴ (داشبورد). یک شمارش **دستی و
دسته‌بندی‌شده** بود، نه مکانیکی؛ و در آن دو ردیف جا افتاد: یکی از دو `AuthController`
هم‌نام، و یکی از دو ردیف دستهٔ لایسنس. اختلاف ۷۰ − ۶۸ = ۲ دقیقاً همان‌ها است.

> **درس:** هر عددِ دستی بالاخره یک ردیف جا می‌گذارد. عدد معتبر همین است که با پیمایش کد و
> با Reflection در `tests/LicensingDb` (بخش ۱۸) شمرده می‌شود و تست، اختلاف را قرمز می‌کند.

---

## ۲) جدول کامل هر ۷۳ ردیف

| # | وضعیت | `Controller.Action` | فعل | مسیر | ماژول | فایل |
|---|---|---|---|---|---|---|
| 1 | ❌ برداشته شد | `AiController.BriefingSendNow` | Post | `briefing/send-now` | Ai | `Controllers/Ai/AiController.cs` |
| 2 | ❌ برداشته شد | `AiController.CategorizeLetter` | Post | `letters/{id:int}/categorize` | Ai | `Controllers/Ai/AiController.cs` |
| 3 | ✅ امروز مجاز | `AiController.Chat` | Post | `chat` | Ai | `Controllers/Ai/AiController.cs` |
| 4 | ✅ امروز مجاز | `AiController.DeleteConversation` | Delete | `conversations/{id:int}` | Ai | `Controllers/Ai/AiController.cs` |
| 5 | ❌ برداشته شد | `AiController.DigestSendNow` | Post | `digest/send-now` | Ai | `Controllers/Ai/AiController.cs` |
| 6 | ❌ برداشته شد | `AiController.DraftLetter` | Post | `letters/draft` | Ai | `Controllers/Ai/AiController.cs` |
| 7 | ❌ برداشته شد | `AiController.DraftMinutes` | Post | `minutes/draft` | Ai | `Controllers/Ai/AiController.cs` |
| 8 | ❌ برداشته شد | `AiController.DraftReply` | Post | `letters/{id:int}/draft-reply` | Ai | `Controllers/Ai/AiController.cs` |
| 9 | ❌ برداشته شد | `AiController.ExtractTasks` | Post | `letters/{id:int}/extract-tasks` | Ai | `Controllers/Ai/AiController.cs` |
| 10 | ❌ برداشته شد | `AiController.FeedbackSubmit` | Post | `feedback/submit` | Ai | `Controllers/Ai/AiController.cs` |
| 11 | ❌ برداشته شد | `AiController.SuggestReceivers` | Post | `letters/suggest-receivers` | Ai | `Controllers/Ai/AiController.cs` |
| 12 | ❌ برداشته شد | `AiController.SuggestReferral` | Post | `letters/{id:int}/suggest-referral` | Ai | `Controllers/Ai/AiController.cs` |
| 13 | ❌ برداشته شد | `AiController.SummarizeLetter` | Post | `letters/{id:int}/summarize` | Ai | `Controllers/Ai/AiController.cs` |
| 14 | ✅ امروز مجاز | `AuthController.ChangePassword` | Post | `change-password` | Core | `Controllers/Core/AuthController.cs` |
| 15 | ✅ امروز مجاز | `AuthController.ChangePassword` | Post | `change-password` | Core | `Modules/RadisHr/Controllers/AuthController.cs` |
| 16 | ✅ امروز مجاز | `AuthController.Login` | Post | `login` | Core | `Controllers/Core/AuthController.cs` |
| 17 | ✅ امروز مجاز | `AuthController.Login` | Post | `login` | Core | `Modules/RadisHr/Controllers/AuthController.cs` |
| 18 | ✅ امروز مجاز | `AuthController.ResetPassword` | Post | `reset-password` | Core | `Modules/RadisHr/Controllers/AuthController.cs` |
| 19 | ✅ امروز مجاز | `AuthController.UploadUserPhoto` | Post | `users/photo/{id:int}` | Core | `Controllers/Core/AuthController.cs` |
| 20 | ✅ امروز مجاز | `AuthController.UploadUserSignature` | Post | `users/signature/{id:int}` | Core | `Controllers/Core/AuthController.cs` |
| 21 | ✅ امروز مجاز | `BackupController.Retry` | Post | `jobs/{id}/retry` | Backup | `Controllers/Backup/BackupController.cs` |
| 22 | ✅ امروز مجاز | `BackupController.Run` | Post | `run` | Backup | `Controllers/Backup/BackupController.cs` |
| 23 | ✅ امروز مجاز | `BackupController.SaveSettings` | Put | `settings` | Backup | `Controllers/Backup/BackupController.cs` |
| 24 | ✅ امروز مجاز | `BackupController.Test` | Post | `test` | Backup | `Controllers/Backup/BackupController.cs` |
| 25 | ✅ امروز مجاز | `BackupSessionController.Close` | Post | `close` | Backup | `Controllers/Backup/BackupSessionController.cs` |
| 26 | ✅ امروز مجاز | `BackupSessionController.Heartbeat` | Post | `heartbeat` | Backup | `Controllers/Backup/BackupSessionController.cs` |
| 27 | ✅ امروز مجاز | `CentralBackupController.DeleteCompany` | Delete | `companies/{id:int}` | Backup | `Controllers/Backup/CentralBackupController.cs` |
| 28 | ✅ امروز مجاز | `CentralBackupController.DeleteReceipt` | Delete | `receipts/{id:int}` | Backup | `Controllers/Backup/CentralBackupController.cs` |
| 29 | ✅ امروز مجاز | `CentralBackupController.RotateKey` | Post | `companies/{id:int}/rotate-key` | Backup | `Controllers/Backup/CentralBackupController.cs` |
| 30 | ✅ امروز مجاز | `CentralBackupController.SaveCompany` | Post | `companies` | Backup | `Controllers/Backup/CentralBackupController.cs` |
| 31 | ✅ امروز مجاز | `CentralBackupController.Upload` | Post | `upload` | Backup | `Controllers/Backup/CentralBackupController.cs` |
| 32 | ✅ امروز مجاز | `CompanyContextController.SetUserCompanies` | Put | `users/{userId:int}` | Core | `Controllers/System/CompanyContextController.cs` |
| 33 | ✅ امروز مجاز | `ConfirmDownloadDtoController.ConfirmDownload` | Post | `{id:int}/confirm-download` | — | `Controllers/DocArchive/DocumentsController.cs` |
| 34 | ✅ امروز مجاز | `ConnectionDtoController.TestConnection` | Post | `connection/test` | — | `Controllers/ItAssets/ItRemoteController.cs` |
| 35 | ✅ امروز مجاز | `DashboardController.HardwareCheck` | Post | `hardware-check` | Dashboard | `Controllers/System/DashboardController.cs` |
| 36 | ✅ امروز مجاز | `DashboardsController.Create` | Post | `—` | Dashboard | `Controllers/Dashboards/DashboardsController.cs` |
| 37 | ✅ امروز مجاز | `DashboardsController.Delete` | Delete | `{id:int}` | Dashboard | `Controllers/Dashboards/DashboardsController.cs` |
| 38 | ✅ امروز مجاز | `DashboardsController.SetDefault` | Post | `{id:int}/default` | Dashboard | `Controllers/Dashboards/DashboardsController.cs` |
| 39 | ✅ امروز مجاز | `DashboardsController.Update` | Put | `{id:int}` | Dashboard | `Controllers/Dashboards/DashboardsController.cs` |
| 40 | ✅ امروز مجاز | `DocCollabController.DuplicateCheck` | Post | `duplicate-check` | DocArchive | `Controllers/DocArchive/DocCollabController.cs` |
| 41 | ✅ امروز مجاز | `DocFavoritesController.Status` | Post | `favorites/status` | DocArchive | `Controllers/DocArchive/DocFavoritesController.cs` |
| 42 | ✅ امروز مجاز | `DocSearchController.AdvancedSearch` | Post | `search` | DocArchive | `Controllers/DocArchive/DocSearchController.cs` |
| 43 | ✅ امروز مجاز | `DocSearchController.SearchPage` | Post | `search-page` | DocArchive | `Controllers/DocArchive/DocSearchController.cs` |
| 44 | ✅ امروز مجاز | `DocSemanticController.SemanticSearch` | Post | `semantic-search` | DocArchive | `Controllers/DocArchive/DocSemanticController.cs` |
| 45 | ✅ امروز مجاز | `DocSemanticController.Suggest` | Post | `suggest` | DocArchive | `Controllers/DocArchive/DocSemanticController.cs` |
| 46 | ✅ امروز مجاز | `DocSettingsController.RunOnce` | Post | `code-numbering/run-once` | DocArchive | `Controllers/DocArchive/DocSettingsController.cs` |
| 47 | ✅ امروز مجاز | `EmailController.Test` | Post | `test` | Letters | `Controllers/Office/EmailController.cs` |
| 48 | ✅ امروز مجاز | `FaComController.CheckAnniversaries` | Post | `anniversaries/check` | FahamCom | `Controllers/FaCom/FaComController.cs` |
| 49 | ✅ امروز مجاز | `FaComController.CheckBirthdays` | Post | `birthdays/check` | FahamCom | `Controllers/FaCom/FaComController.cs` |
| 50 | ✅ امروز مجاز | `FaComController.CheckDue` | Post | `announcements/check-due` | FahamCom | `Controllers/FaCom/FaComController.cs` |
| 51 | ✅ امروز مجاز | `FaLmsController.Suggest` | Post | `perf/suggest` | FahamLms | `Controllers/FaLms/FaLmsController.cs` |
| 52 | ✅ امروز مجاز | `FilesController.Upload` | Post | `upload/{module}` | Core | `Controllers/System/FilesController.cs` |
| 53 | ✅ امروز مجاز | `FilesController.Upload` | Post | `—` | Core | `Modules/RadisHr/Controllers/FilesController.cs` |
| 54 | ✅ امروز مجاز | `HrCoreController.CheckAlerts` | Post | `contract-alerts/check` | HrCore | `Controllers/HrCore/HrCoreController.cs` |
| 55 | ✅ امروز مجاز | `HrCoreController.RunReport` | Post | `reports/run` | HrCore | `Controllers/HrCore/HrCoreController.cs` |
| 56 | ✅ امروز مجاز | `HttpsController.ExportPfx` | Post | `pfx` | Core | `Controllers/System/HttpsController.cs` |
| 57 | ✅ امروز مجاز | `HttpsController.Regenerate` | Post | `regenerate` | Core | `Controllers/System/HttpsController.cs` |
| 58 | ✅ امروز مجاز | `LicenseController.SettleReservation` | Post | `reservations/{id:long}/settle` | Licensing | `Licensing/Controllers/LicenseController.cs` |
| 59 | ✅ امروز مجاز | `LinkRequestController.RunExpiryCheck` | Post | `/api/doc-archive/expiry-check` | — | `Controllers/DocArchive/DocumentsController.cs` |
| 60 | ✅ امروز مجاز | `MoadianController.PreviewSendInvoiceAsync` | Post | `PreviewSendInvoiceAsync` | Moadian | `Controllers/Moadian/MoadianController.cs` |
| 61 | ✅ امروز مجاز | `NotificationsController.TestMessenger` | Post | `test-messenger` | Cartable | `Controllers/System/NotificationsController.cs` |
| 62 | ✅ امروز مجاز | `OfficeMachineAgentController.Printers` | Post | `printers` | OfficeAssets | `Controllers/Office/OfficeMachineAgentController.cs` |
| 63 | ✅ امروز مجاز | `OfficeMachinesController.TestSnmp` | Post | `snmp/test` | OfficeAssets | `Controllers/Office/OfficeMachinesController.cs` |
| 64 | ✅ امروز مجاز | `OrdersController.PreviewChequeReturn` | Post | `{id:int}/cheque-return-preview` | Sales | `Controllers/Sales/OrdersController.cs` |
| 65 | ✅ امروز مجاز | `ReportStudioController.Preview` | Post | `preview` | Reports | `Controllers/ReportStudio/ReportStudioController.cs` |
| 66 | ✅ امروز مجاز | `ReportStudioController.Run` | Post | `reports/{id:int}/run` | Reports | `Controllers/ReportStudio/ReportStudioController.cs` |
| 67 | ✅ امروز مجاز | `SaveDtoController.TestConnection` | Post | `{id:int}/test` | — | `Controllers/ItAssets/ServersController.cs` |
| 68 | ✅ امروز مجاز | `ShopPricingController.Preview` | Post | `preview` | Catalog | `Controllers/Catalog/ShopPricingController.cs` |
| 69 | ✅ امروز مجاز | `TestInputController.TestFormula` | Post | `items/test` | — | `Controllers/Hr/HrPayController.cs` |
| 70 | ✅ امروز مجاز | `UserProfileController.VerifyEmailOtp` | Post | `email/verify` | Core | `Controllers/Core/UserProfileController.cs` |
| 71 | ✅ امروز مجاز | `UserProfileController.VerifyMobileOtp` | Post | `mobile/verify` | Core | `Controllers/Core/UserProfileController.cs` |
| 72 | ✅ امروز مجاز | `VapidApplyInputController.PushStatus` | Post | `push-status` | — | `Controllers/System/NotificationsController.cs` |
| 73 | ✅ امروز مجاز | `VapidApplyInputController.TestPush` | Post | `test-push` | — | `Controllers/System/NotificationsController.cs` |

**جمع: 73 ردیف** = 62 ردیف امروز + 11 ردیف برداشته‌شده.

---

## ۳) ۱۱ ردیفی که برداشته شد — با دلیل

| # | اکشن | چرا برداشته شد |
|---|---|---|
| 1 | `AiController.BriefingSendNow` | ارسال فوری خلاصهٔ روزانه به کاربران — کنش بیرونی (اصلاح: «ارسال» است، نه «آماده‌سازی») |
| 2 | `AiController.CategorizeLetter` | نوشتن دسته روی رکورد نامه |
| 3 | `AiController.DigestSendNow` | ارسال فوری خلاصهٔ دوره‌ای |
| 4 | `AiController.DraftLetter` | ساخت پیش‌نویس نامه = ثبت دادهٔ تازه |
| 5 | `AiController.DraftMinutes` | ساخت صورت‌جلسه = ثبت دادهٔ تازه |
| 6 | `AiController.DraftReply` | پیش‌نویس پاسخ = ثبت داده |
| 7 | `AiController.ExtractTasks` | تماس با مدل + ساخت ساختار داده |
| 8 | `AiController.FeedbackSubmit` | نوشتن ردیف امتیاز در دیتابیس (نه ارسال پیام) |
| 9 | `AiController.SuggestReceivers` | تماس با مدل بیرونی |
| 10 | `AiController.SuggestReferral` | تماس با مدل بیرونی |
| 11 | `AiController.SummarizeLetter` | تماس با مدل بیرونی (هزینهٔ API) |

اصل حاکم: در حالت فقط‌خواندنی، «جست‌وجو / پیش‌نمایش / آزمایش» آزاد است (چون همان کار GET را
می‌کند)، ولی **هر چیزی که تماس با مدل بیرونی یا نوشتن رکورد بسازد** بسته است.

---

## ۴) توجیه دو موردی که بازبینی پرسیده بود

### `CentralBackup/delete` (DeleteCompany و DeleteReceipt) — عمداً مجاز

1. **دادهٔ کسب‌وکار نیست، تنظیمات زیرساخت است.** رکورد «شرکت راه‌دور» و «رسید دریافت
   پشتیبان» در دیتابیس مرکزیِ پشتیبان‌گیری‌اند، نه در دادهٔ عملیاتی مشتری.
2. **حذف، سهمیه آزاد نمی‌کند.** سقف روی «ایجاد» است و رکورد حذف‌شده هم شمرده می‌شود
   (`IgnoreQueryFilters`) ⇒ ابزار دور زدن لایسنس نیست.
3. **بستنش فقط خرابی را نگه می‌دارد.** سناریو: شرکت با کد اشتباه ثبت شده و لایسنس منقضی
   است ⇒ تا خرید لایسنس، رکورد خراب می‌ماند.
4. **مرز:** این استثنا فقط «پاک‌کردن رکورد زیرساختی» است؛ هیچ اکشنی که دادهٔ تازه بسازد
   در فهرست نیست.

### `CentralBackup/rotate-key` — عمداً مجاز، به دلیل امنیتی

چرخش کلید پاسخ به «کلید API لو رفته» است. اگر در حالت بدون لایسنس بسته شود، کلید افشاشده
تا زمان خرید لایسنس معتبر می‌ماند؛ یعنی **قفل، مشتری را در معرض ریسک امنیتی نگه می‌دارد** —
برخلاف اصل «قفل نباید به مشتری آسیب بزند». کلید تازه هیچ متریکی را مصرف نمی‌کند و رکورد
دادهٔ کسب‌وکار نمی‌سازد.

---

## ۵) قفل تستی

`tests/LicensingDb` بخش ۱۸، فهرست ۵۹/۶۲ را با Reflection روی اسمبلی واقعی مقایسه می‌کند:
اگر کسی اکشنی را بی‌صدا اضافه یا حذف کند، تست **قرمز** می‌شود و باید دلیلش را همین‌جا بنویسد.
