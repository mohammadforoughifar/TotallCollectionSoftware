# پنل پشتیبانی لایسنس — سند طراحی (فاز ۵)

> دامنه: برنامهٔ **مستقل** تیم پشتیبانی برای چرخهٔ کامل **درخواست → امضا → تحویل**.
> محل کد: `TotallCollectionSoftware/inventory/tools/LicenseSigner/`.
> این سند «چرا و چگونه» را می‌گوید؛ «چگونه اجرا کنیم» در `OPERATIONS-FA.md` و «چه چیزی تحویل شد» در `closure-fa.md`.

---

## ۱) تصمیم‌های بنیادی

| موضوع | تصمیم | دلیل |
|---|---|---|
| استقلال | برنامهٔ جدا از کلاینت/سرور مشتری، پروژهٔ مستقل `LicenseSigner` | فرم صدور نباید داخل نرم‌افزار مشتری برود (تصمیم صریح) |
| پایگاه‌داده | SQLite پیش‌فرض + EF Core Migration؛ SQL Server اختیاری با رشتهٔ اتصال از env | نصب بدون زیرساخت، مهاجرت‌پذیر |
| جدایی داده | **هیچ ارتباطی با InventoryDb مشتری**؛ اسکیمای مشتری تغییر نمی‌کند | مرز امنیتی و محدودهٔ کار |
| منطق امضا | بازنویسی نمی‌شود؛ `tools/LicenseTool` همان ابزار فاز ۴ صدا زده می‌شود | یک منبع حقیقت برای فرمت و امضا |
| کلید خصوصی | فقط روی دیسک؛ **هرگز** در DB، لاگ، گزارش، ایمیل؛ رمز کلید فقط در لحظهٔ امضا پرسیده می‌شود | کاهش سطح افشا |
| شبکه | فقط `localhost`؛ احراز هویت برای همهٔ مسیرها اجباری | پنل عملیات حساس است، نه سرویس عمومی |
| UI | فارسی RTL، تاریخ شمسی، ساعت تهران، بدون CDN/فونت بیرونی | اجرای آفلاین روی ماشین پشتیبانی |

---

## ۲) معماری

```
مرورگر اپراتور (localhost)
        │  کوکی licsupport (هش SHA256، SameSite=Lax)
        ▼
LicenseSigner (ASP.NET Core 8 Razor Pages)
   ├── Pages/*           ۱۵+ صفحهٔ RTL
   ├── Services/*        منطق دامنه (بدون UI)
   │     ├── Workflow          جدول گذارهای مجاز + نقش‌ها
   │     ├── RequestService    ورود/گذار/رویداد/پیوند مشتری
   │     ├── DraftService      فرم → ردیف DB → پروفایل JSON ابزار
   │     ├── SigningRunner     پروفایل → LicenseTool issue → بازبینی → IssuedLicense
   │     ├── LicenseToolRunner اجرای فرزند با مهلت، بدون shell
   │     ├── LicenseInspector  بازبینی ساختار/امضا/شناسهٔ نصب
   │     ├── DeliveryService   .eml / SMTP / تحویل دستی
   │     ├── SupportAuth       PBKDF2، قفل ۵ تلاش، نقش‌ها
   │     ├── AuditChain        ممیزی فقط‌افزودنی با زنجیرهٔ هش SHA256
   │     ├── BackupService     پشتیبان/بازگردانی SQLite
   │     └── SeedService       Migration + کاربر آغازین + پیش‌تنظیم‌ها
   └── Data/*            ۱۵ موجودیت + Migration
        │
        ├── SQLite  {WorkFolder}/support.db
        └── فرایند فرزند: dotnet LicenseTool.dll issue|verify
                 └── کلید خصوصی روی دیسک (بیرون DB)
```

### چرا اجرای ابزار در فرایند فرزند؟
۱) گذرواژهٔ کلید فقط در محیط همان فرایند قرار می‌گیرد (`LICENSETOOL_KEY_PASSWORD`)، در حافظهٔ پنل نمی‌ماند.
۲) خروج ابزار (کد ۰/۲/۳/−۲) وضعیت را بدون تفسیر مبهم مشخص می‌کند.
۳) منطق امضا تک‌نسخه می‌ماند.

---

## ۳) مدل داده (۱۵ موجودیت)

| موجودیت | جدول | نکته |
|---|---|---|
| Customer | `Customers` | `Code` یکتا، `MergedIntoId` برای ادغام |
| ContactPerson | `Contacts` | `IsPrimary` |
| Installation | `Installations` | `InstallationId` = شناسهٔ نصب مشتری (lowercase-insensitive) |
| LicenseRequest | `Requests` | `RequestNo` (`REQ-{سال شمسی}-{۴رقم}`), `Status`, `PayloadJson`, `PayloadSha256`, `DuplicateOfRequestId`, `WarningsJson` |
| RequestEvent | `RequestEvents` | خط زمانی (گذار، یادداشت، امضا، تحویل) |
| RequestAttachment | `Attachments` | `Sha256` + حجم + نام امن |
| IdentityCheck | `IdentityChecks` | `Method`, `VerifiedWith`, `VerifiedAt`, `ByName` |
| LicenseDraft | `Drafts` | `TermMode/Months/Until/UpdatesUntil`, `UserMode/UserMax/Exempt/AppliesTo`, `PresetsCsv`, `ModulesCsv`, `ProfileJson` |
| IssuedLicense | `IssuedLicenses` | `Serial`, `Sha256`, `KeyId`, `Status`, `VerifiedByPublicKey`, `VerifiedByInstallation`, `SupersededByLicenseId`, `RevocationNote` |
| Delivery | `Deliveries` | `Channel` (Download/Eml/Smtp/Manual), `Success`, `Recipient`, `Note`/`Error` |
| Preset | `Presets` | از `presets.json` ابزار seed می‌شود |
| SupportUser | `Users` | `Role` (Operator/Signer/Admin), `PasswordHash` (PBKDF2-SHA256), `FailedCount`, `LockedUntil`, `MustChangePassword` |
| AuditLog | `AuditLogs` | `PrevHash` + `Hash` (زنجیره)، فقط‌افزودنی |
| MailTemplate | `MailTemplates` | `delivery-template`، جای‌نگهدارها |
| Setting | `Settings` | کلید/مقدار (SMTP و…؛ بدون راز) |

ایندکس‌ها: یکتایی `RequestNo`, `Serial`, `Customer.Code`, `(InstallationId, PayloadSha256)` برای تشخیص تکراری، و `(InstallationId, Status)` روی لایسنس‌ها.

---

## ۴) وضعیت‌ها و گذارها

```
New ──► Reviewing ──► NeedsInfo ──► Reviewing
 │          │
 │          ├──► Approved ──(امضا)──► Issued ──► Delivered ──► Closed
 │          │            │                 └──────────────► Closed
 │          ├──► Rejected                  
 └──► Rejected / Cancelled                (Issued→Delivered فقط از مسیر تحویل)
```

| قاعده | پیاده‌سازی |
|---|---|
| «صادرشده» با تغییر دستی وضعیت ممکن نیست | `Workflow.CanTransition` گذار به `Issued` را رد می‌کند |
| `Approved` فقط با `IdentityCheck` | گذار `Reviewing→Approved` پرچم `RequiresIdentity` دارد |
| امضا فقط از `Approved` و با نقش Signer/Admin | `Workflow.CheckSign` |
| `Delivered` فقط از `Issued` | جدول گذارها |
| هر امضا ⇒ `IssuedLicense` + ممیزی | `SigningRunner.SignAsync` |
| لایسنس تغییرناپذیر | هیچ مسیر ویرایشی روی `IssuedLicenses` نیست؛ اصلاح = سریال تازه + `Superseded` |
| ابطال آفلاین نداریم | فقط `RevocationNote` + انقضا |
| هشدار ۶۰ روز | `Licenses/Index`, `Renewals` |

نقش‌ها: **Operator** (ثبت/بررسی/تحویل)، **Signer** (+امضا/تأیید)، **Admin** (تنظیمات، کاربران، ممیزی، پشتیبان، گذارهای حساس).

---

## ۵) قواعد پذیرش درخواست

- **چهار مسیر ورود:** JSON مشتری، فایل `.eml` (استخراج پیوست)، چسباندن متن، ثبت دستی.
- **IMAP و endpoint آنلاین:** فقط **رابط** `IRequestIngest` در `Services/RequestIngest.cs` تعریف شده — پیاده‌سازی نشده (خارج از دامنهٔ فاز).
- تکراری: هش SHA256 محتوا + `installationId` ⇒ ردیف جدید با `Status=Duplicate` و `DuplicateOfRequestId` به اصل.
- هشدار تمدید: اگر `installationId` لایسنس جاری داشته باشد، هشدار + فهرست لایسنس‌های قبلی.
- ورودی غیرقابل‌اعتماد: محدودیت حجم، اعتبارسنجی ساختار، نمایش escape‌شده (Razor)، **هیچ‌گاه اجرا نمی‌شود**.

---

## ۶) امنیت

| موضوع | پیاده‌سازی |
|---|---|
| گذرواژه | PBKDF2-SHA256، نمک تصادفی، تکرار بالا؛ مقایسهٔ زمان‌ثابت |
| قفل حساب | ۵ تلاش ناموفق ⇒ ۱۵ دقیقه قفل |
| گذرواژهٔ آغازین | فقط اولین اجرا، فقط روی **کنسول** سرور (نه لاگ، نه صفحه) + تعویض اجباری در اولین ورود |
| میان‌افزار تعویض اجباری | تا تعویض نشود فقط `/ChangePassword`, `/Logout`, `/site.css`, `/api/health` باز است |
| CSRF | Anti-forgery توکار Razor Pages روی همهٔ فرم‌ها؛ خروج POST |
| Host | `AllowedHosts=localhost;127.0.0.1` — Host دیگر ۴۰۰ می‌گیرد |
| کوکی | `licsupport`, HttpOnly, Lax, بدون Secure فقط چون localhost |
| کلید خصوصی | بیرون DB؛ ارجاع مسیر در `SignerOptions`؛ در UI فقط «یافت شد/نشد» |
| رمز کلید | فقط پارامتر همان اجرا؛ هیچ‌گاه ذخیره/لاگ نمی‌شود |
| رمز SMTP | فقط از env (`LICENSESIGNER_SMTP_PASSWORD`) |
| ممیزی | زنجیرهٔ هش SHA256؛ دست‌کاری یک ردیف ⇒ «معتبر نیست» (تست E2E) |
| DataProtection | کلیدها در `{WorkFolder}/keys` (برای ماندگاری کوکی بین ری‌استارت) |

**اسکن راز:** در E2E روی `support.db` و `support.db-wal` جست‌وجوی `BEGIN … PRIVATE KEY` و گذرواژه‌های آزمون ⇒ **صفر نتیجه**.

---

## ۷) پیش‌نویس و امضا

- مبنای امضا **همان `Drafts.ProfileJson` ذخیره‌شده** است، نه ورودی فرم هنگام امضا.
- `TermMode`: `months` | `until` | `perpetual` ⇒ `term.type` = `Subscription`/`Subscription`/`Perpetual`.
- استقرار: `OnPrem` | `Saas` (نام‌های enum ابزار؛ `OnPremise` غلط است).
- پیش‌نمایش: همان فرمان با نبود `--yes` ⇒ ابزار **کد خروج ۳** می‌دهد؛ هیچ فایلی ساخته نمی‌شود (تست: پوشهٔ `licenses` خالی می‌ماند).
- امضا: `--yes` ⇒ کد ۰ ⇒ بازبینی فوری با کلید عمومی + تطابق `installationId` ⇒ در صورت ناسازگاری **ذخیره نمی‌شود**.
- `presets.json` فقط کنار فایل اجرایی ابزار جست‌وجو می‌شود (ابزار گزینهٔ CLI ندارد) ⇒ پنل اگر پیش‌نویس بستهٔ آماده داشته باشد و فایل نبود، پیام راهنمای صریح می‌دهد.

---

## ۸) تحویل

| مسیر | نتیجه |
|---|---|
| دانلود | `GET /Licenses/Download?id=` — فایل خام JSON، ممیزی `DOWNLOAD` |
| پیش‌نویس ایمیل | `.eml` با `multipart/mixed`، پیوست base64، UTF-8 — در `{WorkFolder}/outbox`، **بدون ارسال** |
| SMTP | فقط اگر تنظیم و رمز در env باشد؛ نتیجه در `Deliveries` |
| دستی | ثبت گیرنده/یادداشت + `Deliveries` |

قالب ایمیل قابل ویرایش با جای‌نگهدارهای `{{Serial}} {{InstallationId}} {{CustomerName}} {{RequestNo}}`.

---

## ۹) مسیرهای فایل (پوشهٔ کاری)

```
{WorkFolder}/
├── support.db                پایگاه‌دادهٔ پنل (SQLite)
├── attachments/{RequestNo}/  پیوست‌های درخواست
├── signing/                  پروفایل/درخواست موقت امضا (پاک می‌شود)
├── licenses/{Serial}.lic     لایسنس‌های صادرشده
├── outbox/*.eml              پیش‌نویس ایمیل
├── backups/support-*.db      پشتیبان
└── keys/                     کلیدهای DataProtection
```

---

## ۱۰) Form-first

قبل از پیاده‌سازی، **۱۵ ماکاپ ایستای RTL** در `docs/support/forms/` ساخته و بازبینی شد
(`01-requests-list` … `15-settings-users` + `index.html`). هر صفحهٔ واقعی همان ساختار را دارد.

---

## ۱۱) خارج از دامنه (عمدی)

- IMAP و endpoint آنلاین دریافت درخواست (فقط رابط).
- ابطال آفلاین لایسنس (فقط یادداشت + انقضا).
- دیپلوی زنده روی سرور مشتری/عمومی، HTTPS و چندکاربرهٔ شبکه‌ای.
- تغییر در `src/` کلاینت/سرور مشتری، `InventoryDb`، یا منطق امضای ابزار.
- تولید کلید تولیدی (کلید فقط توسط تیم و بیرون مخزن).
