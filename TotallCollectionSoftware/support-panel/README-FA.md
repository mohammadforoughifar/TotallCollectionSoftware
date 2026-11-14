# مینی‌پروژهٔ پشتیبانی لایسنس (`support-panel`)

پنل مستقل تیم پشتیبانی برای **دیدن درخواست مشتری → تأیید → امضا → تحویل فایل لایسنس**.
کنار پوشهٔ `inventory` است، پایگاه‌دادهٔ **محلی خودش** را دارد و هیچ ارتباطی با `InventoryDb` مشتری ندارد.

- کد **بک‌اند و فرانت‌اند کاملِ پنل داخل همین پروژه است** (پروژهٔ خودکفای ASP.NET Core 8).
- تنها وابستگی‌های بیرونی عمدی: کتابخانهٔ `inventory/src/Inventory.Licensing` (مدل/اعتبارسنجی لایسنس و فهرست ماژول‌ها)
  و ابزار خط‌فرمان `inventory/tools/LicenseTool` (تنها مسیر امضا/بازبینی فایل `.lic`).

## ساختار پروژه (بک‌اند + فرانت‌اند)

```
support-panel/
├── Program.cs               راه‌اندازی: DI، کوکی احراز هویت، میان‌افزار تعویض اجباری گذرواژه، Migration + Seed
├── SignerOptions.cs         پیکربندی (کلیدها، پوشهٔ کاری، رشتهٔ اتصال)
├── appsettings.json         ConnectionStrings:Panel = Data Source=data/support.db  + Signer:*
│
├── Data/                    ◀ بک‌اند: مدل داده
│   ├── Enums.cs             وضعیت‌ها، انواع درخواست، نقش‌ها، کانال تحویل
│   ├── Entities.cs          ۱۵ موجودیت (Customer … AuditLog)
│   ├── SupportDbContext.cs  نگاشت/ایندکس/قید
│   └── Migrations/          InitialCreate (SQLite / SQL Server)
│
├── Services/                ◀ بک‌اند: منطق دامنه
│   ├── Workflow.cs          جدول گذارهای مجاز + نقش‌ها
│   ├── RequestService.cs    ثبت درخواست، گذار وضعیت، رویداد، پیوند مشتری
│   ├── RequestIngest.cs     تجزیهٔ JSON/.eml/متن + تشخیص تکراری + رابط IRequestIngest
│   ├── DraftService.cs      فرم → ردیف DB → پروفایل JSON ابزار
│   ├── SigningRunner.cs     پیش‌نمایش/امضا با LicenseTool + بازبینی خودکار
│   ├── LicenseToolRunner.cs اجرای فرزند با مهلت (بدون shell)
│   ├── LicenseInspector.cs  بازبینی ساختار/امضا/شناسهٔ نصب
│   ├── DeliveryService.cs   .lic / .eml با پیوست / SMTP / تحویل دستی
│   ├── SupportAuth.cs       PBKDF2، قفل ۵ تلاش، نقش‌ها
│   ├── AuditChain.cs        ممیزی فقط‌افزودنی با زنجیرهٔ هش
│   ├── BackupService.cs     پشتیبان/بازگردانی SQLite
│   └── SeedService.cs       Migration + کاربر آغازین + پیش‌تنظیم‌ها
│
├── Pages/                   ◀ فرانت‌اند: Razor Pages (RTL فارسی)
│   ├── Shared/_Layout.cshtml   نوار ناوبری بر اساس نقش
│   ├── Login/Logout/ChangePassword
│   ├── Index (داشبورد) · Requests/* (Index, Ingest, Details, Identity, Draft, Sign, Deliver)
│   ├── Customers/* · Licenses/* · Settings/* · Renewals · Audit · Backup
│   └── wwwroot/site.css        استایل RTL بدون CDN/فونت بیرونی
│
├── prepare.sh|.cmd · run.sh|.cmd · publish.sh   اسکریپت‌های اجرا/انتشار
└── data/                    (ساخته می‌شود) پوشهٔ کاری: DB، پیوست‌ها، لایسنس‌ها، outbox، پشتیبان
```

معماری: **Razor Pages با هندلرهای سرور** — هر فرم یک صفحهٔ `.cshtml` است که هندلرهای `OnPost*` آن
مستقیماً سرویس‌های بک‌اند را صدا می‌زنند (بدون API واسط برای UI). برای مصرف ماشینی،
`GET /api/health` هم موجود است.
- کلید خصوصی و گذرواژه‌ها **هیچ‌جا در این پروژه، DB یا لاگ ذخیره نمی‌شوند**.

---

## ۱) پیش‌نیاز

- .NET 8 SDK (برای ساخت) یا .NET 8 Runtime/ASP.NET Runtime (برای اجرای بستهٔ publish).
- جفت‌کلید لایسنس + گذرواژهٔ کلید (بیرون از مخزن).
- ویندوز یا لینوکس؛ فقط `localhost`.

## ۲) راه‌اندازی در سه گام

```bash
cd TotallCollectionSoftware/support-panel

# گام ۱ — ساخت ابزار امضا + کپی presets.json + ساخت پنل
./prepare.sh            # ویندوز: prepare.cmd
```

```bash
# گام ۲ — جفت‌کلید (اگر ندارید؛ بیرون مخزن)
export LICENSETOOL_KEY_PASSWORD='<رمز-کلید>'
dotnet ../inventory/tools/LicenseTool/bin/Release/net8.0/LicenseTool.dll \
  keygen --key-id <key-id> --out-dir /opt/license-keys
unset LICENSETOOL_KEY_PASSWORD
```

سپس سه مسیر را در `appsettings.json` (یا با env) بگذارید:

```json
"Signer": {
  "PrivateKeyPath": "/opt/license-keys/<key-id>.private.pem",
  "PublicKeyPath":  "/opt/license-keys/<key-id>.pem",
  "KeyId": "<key-id>"
}
```

```bash
# گام ۳ — اجرا
./run.sh                # ویندوز: run.cmd   →  http://127.0.0.1:5399
```

اولین اجرا: کاربر `admin` ساخته می‌شود و **گذرواژهٔ آغازین فقط یک‌بار روی کنسول** چاپ می‌شود
(در لاگ نیست). اولین ورود، تعویض گذرواژه اجباری است؛ تا تعویض نشود هیچ صفحهٔ دیگری باز نمی‌شود.

## ۳) پایگاه‌داده و رشتهٔ اتصال (محلی، بعداً قابل تغییر)

پیش‌فرض در `appsettings.json`:

```json
"ConnectionStrings": { "Panel": "Data Source=data/support.db" }
```

- مسیر نسبی، نسبت به پوشهٔ همین پروژه باز می‌شود؛ فایل در `support-panel/data/support.db` ساخته می‌شود.
- برای **SQL Server** فقط همین مقدار را عوض کنید (یا env بدهید):

```bash
export ConnectionStrings__Panel='Server=.;Database=LicenseSupport;Trusted_Connection=True;TrustServerCertificate=True'
```

اولویت: `Signer__DatabaseConnection` (env) ← `ConnectionStrings__Panel` ← `Signer:DatabaseConnection` ← `ConnectionStrings:Panel` ← SQLite پیش‌فرض.
Migration در اولین اجرا خودکار اعمال می‌شود.

## ۴) پوشهٔ کاری (`data/`)

```
support-panel/data/
├── support.db                پایگاه‌دادهٔ پنل
├── attachments/{RequestNo}/  پیوست‌های درخواست
├── signing/                  فایل‌های موقت امضا (خودکار پاک می‌شوند)
├── licenses/{Serial}.lic     لایسنس‌های صادرشده
├── outbox/*.eml              پیش‌نویس ایمیل تحویل
├── backups/                  پشتیبان‌ها
└── keys/                     کلیدهای DataProtection (کوکی)
```

کلید **خصوصی** جای دیگری است و این‌جا نمی‌آید.

## ۵) چرخهٔ کار روزمره

1. **دریافت درخواست:** مشتری از `تنظیمات → وضعیت لایسنس → دریافت فایل درخواست لایسنس` فایل JSON می‌گیرد و می‌فرستد.
2. **ثبت:** `ثبت درخواست` → مسیر «فایل JSON یا .eml» (یا «ثبت دستی») → پیش‌نمایش → ثبت.
3. **مشتری:** در صفحهٔ درخواست «تعیین مشتری».
4. **بررسی:** «شروع بررسی» (هشدار تکراری/تمدید را ببینید).
5. **تأیید هویت:** `تأیید هویت` (بدون آن، تأیید درخواست ممکن نیست).
6. **پیش‌نویس:** `پیش‌نویس امضا` → مدت/کاربر/بسته‌ها/ماژول‌ها → «پیش‌نمایش ابزار (بدون امضا)».
7. **امضا:** `امضا` → Key ID + گذرواژهٔ کلید + تیک تأیید.
8. **تحویل:** `تحویل` → دانلود `.lic` یا `.eml` با پیوست یا SMTP یا ثبت دستی.
9. **سمت مشتری:** بارگذاری فایل لایسنس در همان صفحهٔ وضعیت لایسنس (اعمال بدون ری‌استارت).

قواعد ثابت: «صادرشده» فقط با امضا · «تأییدشده» فقط با تأیید هویت · لایسنس تغییرناپذیر (اصلاح = سریال تازه) · ابطال آفلاین نداریم (فقط یادداشت + انقضا).

## ۶) بردن روی سیستم پشتیبان (بدون کد منبع)

```bash
./publish.sh                  # out/ : نیازمند ASP.NET 8 Runtime روی مقصد
./publish.sh self-contained   # out/ : Runtime داخل بسته
```

سپس پوشهٔ `out/` را کپی کنید و روی ماشین مقصد:

```bat
set ASPNETCORE_URLS=http://127.0.0.1:5399
set Signer__ToolPath=C:\support\out\tools\LicenseTool.dll
set Signer__PrivateKeyPath=C:\license-keys\<key-id>.private.pem
set Signer__PublicKeyPath=C:\license-keys\<key-id>.pem
set Signer__KeyId=<key-id>
dotnet SupportPanel.dll
```

ابزار امضا و `presets.json` داخل `out/tools/` هستند (پنل آن‌ها را صدا می‌زند).

## ۷) آزمون دودی

```bash
ADMIN_PASSWORD='<گذرواژهٔ admin>' KEYPASS='<گذرواژهٔ کلید>' \
BASE=http://127.0.0.1:5399 WORK=./data \
bash ../inventory/test/support/e2e-fa.sh
```

۶۰ بررسی؛ باید روی **پایگاه‌دادهٔ تازه** اجرا شود (روی DB استفاده‌شده با کد ۲ می‌ایستد).

## ۸) امنیت — چیزهایی که نباید تغییر کنند

- فقط `localhost` (Host دیگر ۴۰۰ می‌گیرد). برای دسترسی شبکه‌ای، پنل را عمومی نکنید؛ از تونل امن استفاده کنید.
- کلید خصوصی بیرون مخزن با دسترسی محدود (`chmod 600`)، نسخهٔ آفلاین داشته باشید.
- گذرواژهٔ کلید را در `appsettings.json` **ننویسید**؛ هنگام امضا در فرم وارد می‌شود.
- رمز SMTP فقط از env (`LICENSESIGNER_SMTP_PASSWORD`).
- پوشهٔ `data/` و `out/` و کلیدها را کامیت نکنید (`.gitignore` تنظیم است).
- هر تغییر مهم: `ممیزی → بررسی زنجیره` و `پشتیبان → ساخت پشتیبان`.

## ۹) عیب‌یابی سریع

| نشانه | کار |
|---|---|
| «ابزار آماده نیست: کلید خصوصی پیدا نشد» | `Signer:PrivateKeyPath` + دسترسی فایل |
| «presets.json کنار فایل اجرایی LicenseTool نیست…» | `prepare.sh` را اجرا کنید (یا فایل را کنار `LicenseTool.dll` کپی کنید) |
| «ابزار ورودی را نپذیرفت (کد ۲)» | متن ابزار در همان پیام است؛ معمولاً presets یا مقدار غیرمجاز (مثلاً `OnPremise` به‌جای `OnPrem`) |
| «SMTP تنظیم نشده است» | `تنظیمات → SMTP` + `LICENSESIGNER_SMTP_PASSWORD` |
| ورود از آدرس دیگر ۴۰۰ | `AllowedHosts` عمدی است؛ فقط `127.0.0.1`/`localhost` |
| قفل حساب | ۵ تلاش ناموفق ⇒ `تنظیمات → کاربران → رفع قفل` |

مرجع کامل: `inventory/docs/support/OPERATIONS-FA.md` و طراحی در `inventory/docs/support/DESIGN-FA.md`.
