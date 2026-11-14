# پنل پشتیبانی لایسنس — راهنمای بهره‌برداری (OPERATIONS-FA)

> مخاطب: اپراتور/مدیر ماشین پشتیبانی. مسیرها نسبی به `TotallCollectionSoftware/inventory/` هستند.
> **هیچ‌جا کلید خصوصی، گذرواژه یا رمز SMTP نوشته نمی‌شود** — همه از env یا پرسش تعاملی می‌آیند.

---

## ۱) پیش‌نیاز

- .NET 8 SDK (برای ساخت) یا Runtime (برای اجرا).
- ماشین ویندوز/لینوکس پشتیبانی، **بدون نیاز به اینترنت** در زمان اجرا.
- جفت‌کلید تولیدی + گذرواژهٔ آن (خارج از مخزن).

## ۲) ساخت

```bash
cd TotallCollectionSoftware/inventory
dotnet build tools/LicenseTool/LicenseTool.csproj -c Release
dotnet build support-panel/SupportPanel.csproj -c Release
```

> ⚠️ **گام الزامی:** `presets.json` را کنار فایل اجرایی ابزار کپی کنید. ابزار آن را فقط کنار خودش می‌جوید و گزینهٔ CLI ندارد:
> ```bash
> cp tools/LicenseTool/presets.json tools/LicenseTool/bin/Release/net8.0/
> ```
> اگر این کار نشود و پیش‌نویس «بستهٔ آماده» داشته باشد، پنل خطای صریح می‌دهد و امضا نمی‌کند.

## ۲-ب) مینی‌پروژهٔ `support-panel` (روش پیشنهادی برای سیستم پشتیبان)

برای اجرا روی سیستم پشتیبان، پروژهٔ سبک `TotallCollectionSoftware/support-panel/` ساخته شده است
(همسایهٔ `inventory`): کد فرم‌ها را از همین‌جا می‌خواند، ولی پیکربندی، رشتهٔ اتصال محلی و اسکریپت‌های
اجرا/انتشار خودش را دارد.

```bash
cd TotallCollectionSoftware/support-panel
./prepare.sh          # ساخت LicenseTool + کپی presets.json + ساخت پنل  (ویندوز: prepare.cmd)
./run.sh              # http://127.0.0.1:5399                            (ویندوز: run.cmd)
./publish.sh          # ساخت بستهٔ قابل‌حمل در out/ برای کپی روی ماشین پشتیبان
```

- رشتهٔ اتصال پیش‌فرض **محلی** است: `ConnectionStrings:Panel = Data Source=data/support.db`
  (فایل در `support-panel/data/support.db`). برای SQL Server فقط همان خط را عوض کنید یا
  `ConnectionStrings__Panel` را در env بگذارید.
- `Signer:PrivateKeyPath` / `PublicKeyPath` / `KeyId` در `appsettings.json` همان پروژه یا با env.
- مستند کامل: `support-panel/README-FA.md`.

## ۳) متغیرهای محیطی

| متغیر | الزام | توضیح |
|---|---|---|
| `Signer__ToolPath` | ✔ | مسیر `LicenseTool.dll` |
| `Signer__PrivateKeyPath` | ✔ | مسیر کلید خصوصی روی دیسک (بیرون مخزن، دسترسی محدود) |
| `Signer__PublicKeyPath` | ✔ | مسیر کلید عمومی برای بازبینی خودکار |
| `Signer__KeyId` | ✔ | شناسهٔ کلید در لایسنس |
| `Signer__WorkFolder` | ✔ (توصیهٔ جدی) | پوشهٔ کاری؛ نبودِ آن ⇒ پوشهٔ موقت سیستم |
| `Signer__PresetsPath` | — | مسیر `presets.json` برای seed پیش‌تنظیم‌ها در DB |
| `Signer__DatabaseConnection` | — | خالی ⇒ SQLite در `{WorkFolder}/support.db`؛ با `Server=…` ⇒ SQL Server |
| `ConnectionStrings__Panel` | — | رشتهٔ اتصال از پیکربندی (در مینی‌پروژه پیش‌فرض `Data Source=data/support.db`) |
| `Signer__TimeoutSeconds` | — | مهلت اجرای ابزار (۵–۶۰۰، پیش‌فرض ۶۰) |
| `LICENSETOOL_KEY_PASSWORD` | فقط لحظهٔ امضا | پنل خودش از فرم می‌گیرد و فقط به فرزند می‌دهد؛ برای اجرای دستی ابزار لازم است |
| `LICENSESIGNER_SMTP_PASSWORD` | — | رمز SMTP؛ نبودِ آن ⇒ ارسال غیرفعال با پیام روشن |
| `ASPNETCORE_URLS` | ✔ | مثل `http://127.0.0.1:5299` (فقط localhost) |
| `ASPNETCORE_ENVIRONMENT` | — | `Production` روی ماشین واقعی |

نمونهٔ اجرا (لینوکس):

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://127.0.0.1:5299
export Signer__ToolPath="$PWD/tools/LicenseTool/bin/Release/net8.0/LicenseTool.dll"
export Signer__PrivateKeyPath="/opt/license-keys/prod.private.pem"
export Signer__PublicKeyPath="/opt/license-keys/prod.pem"
export Signer__KeyId="prod-2026a"
export Signer__WorkFolder="/var/lib/license-signer"
export Signer__PresetsPath="$PWD/tools/LicenseTool/presets.json"
dotnet support-panel/bin/Release/net8.0/SupportPanel.dll
```

## ۴) اولین اجرا

1. Migration خودکار اجرا می‌شود (`InitialCreate`).
2. کاربر `admin` ساخته و **گذرواژهٔ آغازین یک‌بار روی کنسول** چاپ می‌شود؛ **در لاگ نیست**. آن را همان لحظه بردارید.
3. اولین ورود ⇒ صفحهٔ «تعویض گذرواژه»؛ تا تعویض نشود هیچ مسیر دیگری باز نمی‌شود.
4. پیش‌تنظیم‌ها از `presets.json` و قالب ایمیل `delivery-template` seed می‌شوند.
5. کاربران دیگر را از `تنظیمات → کاربران` بسازید و نقش بدهید (Operator/Signer/Admin).

## ۵) کار روزمره

| کار | مسیر |
|---|---|
| ثبت درخواست | `/Requests/Ingest` (JSON، `.eml`، متن، دستی) |
| بررسی و گذار | `/Requests/Details` |
| تأیید هویت (اجباری قبل از تأیید) | `/Requests/Identity` |
| پیش‌نویس امضا | `/Requests/Draft` («پیش‌نمایش ابزار» بدون امضا) |
| امضا | `/Requests/Sign` (Key ID + گذرواژهٔ کلید + تأیید صریح) |
| تحویل | `/Requests/Deliver` (دانلود / `.eml` / SMTP / دستی) |
| هشدار انقضا | `/Renewals`, `/Licenses` |
| ممیزی | `/Audit` («بررسی زنجیره») |
| پشتیبان | `/Backup` |

## ۶) قواعد کاری که نباید دور بزنید

- «صادرشده» فقط با امضا. تغییر دستی وضعیت ممکن نیست.
- امضا فقط از «تأییدشده» و با نقش Signer/Admin.
- لایسنس تغییرناپذیر است؛ اصلاح = **سریال تازه** و `Superseded` کردن قبلی.
- ابطال آفلاین نداریم: فقط یادداشت ابطال + انقضا.
- رمز کلید را در UI تایپ می‌کنید؛ در DB، لاگ یا فایل نمی‌نشیند.

## ۷) پشتیبان‌گیری

- `/Backup` ⇒ «ساخت پشتیبان» (`PRAGMA wal_checkpoint(FULL)` سپس کپی) در `{WorkFolder}/backups/support-*.db`.
- بازگردانی: کپی فایل جاری به `support.db.before-restore`، سپس جایگزینی؛ **سرور را دوباره راه‌اندازی کنید**.
- پشتیبان **شامل کلید خصوصی نیست** (کلید بیرون از DB است) — پشتیبان کلید جداگانه و آفلاین نگه داشته شود.

## ۸) عیب‌یابی

| نشانه | علت رایج | کار |
|---|---|---|
| «ابزار آماده نیست: کلید خصوصی پیدا نشد» | `Signer__PrivateKeyPath` غلط | مسیر و دسترسی فایل |
| «ابزار ورودی را نپذیرفت (کد ۲)» | مقدار غیرمجاز در پروفایل (مثلاً `OnPremise` به‌جای `OnPrem`)، `presets.json` نبود، `requestVersion` نامعتبر | متن ابزار در همان پیام آمده |
| «presets.json کنار فایل اجرایی LicenseTool نیست…» | کپی نشده | گام ۲ |
| «SMTP تنظیم نشده است» | تنظیمات خالی یا رمز در env نبود | `تنظیمات → SMTP` + `LICENSESIGNER_SMTP_PASSWORD` |
| «زنجیرهٔ ممیزی معتبر نیست: ردیف #n…» | دست‌کاری/خرابی DB | از پشتیبان بازگردانید و رخداد را گزارش کنید |
| قفل حساب | ۵ تلاش ناموفق | `تنظیمات → کاربران → رفع قفل` |
| ورود از Host دیگر ۴۰۰ می‌دهد | `AllowedHosts` عمدی است | فقط `localhost`/`127.0.0.1` |

## ۹) آزمون دودی پس از استقرار

```bash
ADMIN_PASSWORD='<گذرواژهٔ admin>' bash test/support/e2e-fa.sh
```
۶۰ بررسی (احراز هویت، ورود درخواست، تکراری، گاردها، پیش‌نویس، امضا، تحویل، دانلود+بازبینی مستقل، ممیزی + دست‌کاری، پشتیبان، اسکن راز).
خروجی: توکار `evidence/support-panel-e2e.log`.

## ۱۰) نگهداری کلید

- کلید خصوصی: بیرون مخزن، دسترسی ۶۰۰، نسخهٔ آفلاین.
- چرخش کلید ⇒ `Signer__KeyId` جدید + انتشار کلید عمومی جدید؛ لایسنس‌های قبلی معتبر می‌مانند (بازبینی با کلید عمومی مربوط به خودشان).
- **هرگز** کلید خصوصی را در `support.db`، `backups/`، `outbox/` یا تیکت نگذارید.
