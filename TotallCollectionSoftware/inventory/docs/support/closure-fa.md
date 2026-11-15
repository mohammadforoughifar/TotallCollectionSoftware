# بستن فاز ۵ — پنل پشتیبانی (closure-fa)

> شاخه: `lic/support-panel` · دامنه: `TotallCollectionSoftware/support-panel/` + `test/support/` + `docs/support/`
> `src/` مشتری، `InventoryDb` و منطق امضای `tools/LicenseTool` **دست‌نخورده**.

## ۱) وضعیت خواسته‌ها

| # | خواسته | وضعیت | شاهد |
|---|---|---|---|
| ۱ | برنامهٔ مستقل پشتیبانی، چرخهٔ درخواست → امضا → تحویل | ✅ | `Pages/*`, `Services/*`, E2E بخش‌های ۴–۱۱ |
| ۲ | ۱۵ موجودیت + ایندکس/قید، SQLite + EF Migration، SQL Server اختیاری | ✅ | `Data/Entities.cs`, `Data/Migrations/*`, E2E (Migration روی DB خالی) |
| ۳ | هیچ ارتباطی با InventoryDb مشتری | ✅ | بدون DbContext مشتری؛ فقط ProjectReference به کتابخانهٔ مدل |
| ۴ | چرخهٔ وضعیت New…Closed + Rejected/Cancelled/Duplicate | ✅ | `Services/Workflow.cs`, E2E ۶ |
| ۵ | انواع درخواست (NewActivation…Bridge/Other) | ✅ | `Data/Enums.cs` |
| ۶ | تشخیص تکراری (installationId + هش) با پیوند | ✅ | E2E ۵ |
| ۷ | هشدار تمدید + نمایش لایسنس‌های قبلی | ✅ | `RequestService`, `SigningRunner` |
| ۸ | IdentityCheck اجباری پیش از Approved | ✅ | E2E ۶/۷ («تأیید هویت» رد/ثبت) |
| ۹ | امضا فقط از Approved با نقش Signer؛ هر امضا ⇒ IssuedLicense + ممیزی | ✅ | E2E ۹، `AuditLogs` |
| ۱۰ | لایسنس تغییرناپذیر؛ اصلاح = سریال تازه + Superseded؛ بدون ابطال آفلاین | ✅ | `Licenses/Details` («یادداشت ابطال») |
| ۱۱ | هشدار ۶۰ روز مانده | ✅ | `Licenses/Index`, `Renewals` |
| ۱۲ | کلید خصوصی فقط روی دیسک؛ رمز کلید فقط لحظهٔ امضا؛ هرگز در DB/لاگ | ✅ | E2E ۱۴ (اسکن `support.db`/WAL ⇒ صفر) |
| ۱۳ | فقط localhost؛ احراز هویت اجباری | ✅ | E2E ۱ (302/Host⇒400) |
| ۱۴ | PBKDF2، قفل ۵ تلاش، نقش‌ها Operator/Signer/Admin | ✅ | `Services/SupportAuth.cs` |
| ۱۵ | گذرواژهٔ اولیه فقط اولین اجرا روی کنسول + تعویض اجباری | ✅ | E2E ۲ |
| ۱۶ | ورودی غیرقابل‌اعتماد: حجم/ساختار/escape، بدون اجرا | ✅ | `RequestIngest.cs`, نمایش escape در Details |
| ۱۷ | ورود JSON / .eml / متن / دستی | ✅ | E2E ۴ + `Requests/Ingest` |
| ۱۸ | IMAP/endpoint آنلاین فقط رابط `IRequestIngest` | ✅ (عمداً پیاده نشده) | `Services/RequestIngest.cs` |
| ۱۹ | خروجی: دانلود `.lic`، `.eml` با پیوست، SMTP اختیاری، قالب ایمیل | ✅ | E2E ۱۰/۱۱ |
| ۲۰ | UI فارسی RTL + تاریخ شمسی/تهران | ✅ | `wwwroot/site.css`, `Services/JalaliDate.cs` |
| ۲۱ | ۱۵ ماکاپ HTML ایستای RTL پیش از پیاده‌سازی | ✅ | `docs/support/forms/*.html` (۱۵ + index) |
| ۲۲ | پشتیبان‌گیری/بازگردانی + ممیزی زنجیره‌ای قابل‌بررسی | ✅ | E2E ۱۲/۱۳ (شامل آزمون دست‌کاری) |
| ۲۳ | اسناد DESIGN/OPERATIONS/closure + شواهد | ✅ | همین پوشه + `evidence/` |

## ۱-ب) تحویل کد

- شاخه: `main` (پوش مستقیم به درخواست کاربر)
- کامیت: `bb25d3165f61f5c043f25e7c4d182fec33fd15c5` — feat(support): پنل مستقل پشتیبانی لایسنس (فاز ۵)
- `git ls-remote origin refs/heads/main` ⇒ همان کامیت
- ۷۲ فایل تازه؛ هیچ فایل موجودی تغییر/حذف نشده (`src/` مشتری، `InventoryDb` و `tools/LicenseTool` دست‌نخورده)

## ۱-ج) مینی‌پروژهٔ اجرا روی سیستم پشتیبان

`TotallCollectionSoftware/support-panel/` — پروژهٔ مستقل همسایهٔ `inventory` با پیکربندی/رشتهٔ اتصال محلی
(`Data Source=data/support.db`)، اسکریپت‌های `prepare/run/publish` و `README-FA.md`. کد فرم‌ها تک‌نسخه از
`inventory/support-panel` خوانده می‌شود (بدون کپی منطق). آزمون سرتاسری روی همین پروژه: **۶۰/۶۰ سبز**
(شاهد: `/home/user/evidence/support-panel-mini-e2e.log`).

## ۲) آزمون‌ها

| آزمون | نتیجه |
|---|---|
| بیلد `support-panel/SupportPanel.csproj` | **۰ خطا، ۰ هشدار** |
| `test/support/e2e-fa.sh` (۶۰ بررسی) | **PASSED=60 FAILED=0** |
| بازبینی مستقل `.lic` با `LicenseTool verify --public-key --installation-id` | «شناسهٔ نصب مطابق است» |
| اسکن راز در `support.db` / `-wal` / `.lic` | صفر نتیجه |
| دست‌کاری یک ردیف ممیزی ⇒ «معتبر نیست» و پس از بازگردانی ⇒ سالم | ✅ |

**مسیر شواهد:** `/home/user/evidence/support-panel-e2e.log` (بیرون مخزن؛ شامل هیچ راز/گذرواژه‌ای نیست).

## ۳) آنچه آزمون نشده (صریح)

| مورد | چرا |
|---|---|
| اجرا روی **ویندوز** و IIS/سرویس ویندوز | محیط ساخت لینوکسی بود |
| ارسال **SMTP واقعی** | بدون سرور SMTP و رمز؛ فقط مسیر «تنظیم‌نشده» و ساخت `.eml` آزمون شد |
| SQL Server | فقط SQLite آزمون شد؛ مسیر SQL Server کد-بازبینی‌شده است، نه اجراشده |
| کلید **تولیدی** | عمداً تولید نشد؛ آزمون با جفت‌کلید آزمایشی بیرون مخزن |
| IMAP / endpoint آنلاین | خارج از دامنه؛ فقط رابط |
| بار/چندکاربره هم‌زمان | پنل تک‌اپراتور localhost فرض شده |
| بازیابی واقعی از پشتیبان در حین کار | فقط ساخت پشتیبان آزمون شد؛ بازگردانی نیازمند ری‌استارت سرویس است |

## ۴) چک‌لیست آزمون دستی (توصیه‌شده پیش از استفادهٔ واقعی)

1. اجرا روی ویندوز با مسیرهای فارسی/فاصله‌دار در `WorkFolder`.
2. یک دور کامل با کلید **تولیدی** روی یک درخواست آزمایشی و تأیید با `LicenseTool verify`.
3. تنظیم SMTP واقعی + ارسال به صندوق آزمون (بررسی MIME و پیوست `.lic`).
4. بازگردانی از پشتیبان روی نسخهٔ کپی و تأیید سالم بودن زنجیرهٔ ممیزی.
5. ورود با هر سه نقش و بررسی بسته بودن مسیرهای غیرمجاز (Settings/Audit/Backup).
6. قفل حساب با ۵ تلاش ناموفق و رفع قفل توسط مدیر.
7. بررسی `AllowedHosts` با Host دیگر ⇒ ۴۰۰.
8. اطمینان از اینکه کلید خصوصی در `backups/` و `outbox/` نیست.

## ۵) فهرست فایل‌ها

```
support-panel/
  SupportPanel.csproj  Program.cs  SignerOptions.cs  appsettings.json
  prepare.sh|.cmd  run.sh|.cmd  publish.sh  README-FA.md  .gitignore
  Data/{Enums,Entities,SupportDbContext,SupportDbContextFactory}.cs
  Data/Migrations/{20261114105814_InitialCreate(.Designer).cs,SupportDbContextModelSnapshot.cs}
  Services/{JalaliDate,Ui,AuditChain,SupportAuth,Workflow,RequestIngest,RequestService,DraftService,
            LicenseToolRunner,LicenseInspector,SigningRunner,SupportSettings,DeliveryService,
            BackupService,SeedService}.cs
  Pages/{_ViewImports,_ViewStart,Login,Logout,ChangePassword,Error,Index,Audit,Backup,Renewals}.cshtml
  Pages/Shared/_Layout.cshtml
  Pages/Requests/{Index,Ingest,Details,Identity,Draft,Sign,Deliver}.cshtml
  Pages/Customers/{Index,Edit}.cshtml
  Pages/Licenses/{Index,Details,Download}.cshtml
  Pages/Settings/{Index,Users}.cshtml
  wwwroot/site.css
inventory/test/support/e2e-fa.sh
inventory/docs/support/{DESIGN-FA.md,OPERATIONS-FA.md,closure-fa.md}
inventory/docs/support/forms/{index,01-…,15-…}.html
```

## ۶) فرض‌ها

1. پنل تک‌اپراتور روی ماشین پشتیبانی و فقط `localhost` است.
2. `presets.json` کنار فایل اجرایی `LicenseTool` کپی می‌شود (ابزار گزینهٔ CLI ندارد).
3. مقادیر enum ابزار (`OnPrem`/`Saas`, `Subscription`/`Perpetual`, `Named`/`Concurrent`/`None`, `Global`/`Modules`) مبنای فرم پیش‌نویس‌اند.
4. تاریخ «تأیید هویت» و «به‌روزرسانی تا» شمسی وارد و به UTC ذخیره می‌شود؛ نمایش همیشه شمسی/تهران.
5. `WorkFolder` در بهره‌برداری واقعی صریح تنظیم می‌شود (پیش‌فرض پوشهٔ موقت سیستم است).
6. شمارهٔ درخواست `REQ-{سال شمسی}-{۴رقم}` و یکتا در همان سال در نظر گرفته شده.
7. تکراری = همان `installationId` + همان هش محتوا؛ تغییر یک نویسه ⇒ درخواست تازه (نه تکراری).
8. DataProtection روی دیسک (`{WorkFolder}/keys`) برای ماندگاری کوکی‌ها بین ری‌استارت‌ها.
