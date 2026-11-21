# پنل پشتیبانی (`support-panel`)

پنل مستقل تیم پشتیبانی برای **مدیریت مشتریان، کاربران پنل، ممیزی و پشتیبان‌گیری**.
کنار پوشهٔ `inventory` است، پایگاه‌دادهٔ **محلی خودش** را دارد و هیچ ارتباطی با
`InventoryDb` مشتری ندارد.

> ⚠️ موتور لایسنس از این پنل و از نرم‌افزار حذف شده است؛ این پنل دیگر لایسنس صادر،
> امضا یا تحویل نمی‌کند و هیچ ارجاعی به `inventory/src/Inventory.Licensing` یا
> `inventory/tools/LicenseTool` ندارد. جزئیات: `docs/LICENSE-REMOVED-FA.md`.

## ساختار پروژه

```
support-panel/
├── Program.cs               راه‌اندازی: DI، کوکی احراز هویت، میان‌افزار تعویض اجباری گذرواژه، Migration + Seed
├── PanelOptions.cs          پیکربندی (رشتهٔ اتصال، پوشهٔ کاری، SMTP، حالت پیش‌نمایش)
├── appsettings.json         ConnectionStrings:Panel = Data Source=data/support.db  + Panel:*
│
├── Data/                    ◀ بک‌اند: مدل داده
│   ├── Enums.cs             نقش‌ها (SupportRole)
│   ├── Entities.cs          ۶ موجودیت: Customer، ContactPerson، SupportUser، AuditLog، MailTemplate، Setting
│   ├── SupportDbContext.cs  نگاشت/ایندکس/قید
│   └── Migrations/          مایگریشن‌های موجود (جدول‌های قدیمی لایسنس در DB باقی می‌مانند)
│
├── Services/                ◀ بک‌اند: منطق دامنه
│   ├── SupportAuth.cs       PBKDF2، قفل پس از ۵ تلاش، نقش‌ها
│   ├── AuditChain.cs        ممیزی فقط‌افزودنی با زنجیرهٔ هش
│   ├── BackupService.cs     پشتیبان/بازگردانی پایگاه‌داده
│   ├── SupportSettings.cs   SMTP (رمز فقط از متغیر محیطی) و قالب ایمیل پاسخ
│   ├── JalaliDate.cs        تاریخ شمسی و ارقام فارسی
│   ├── Ui.cs                کمک‌های نمایشی
│   └── SeedService.cs       Migration + کاربر آغازین
│
├── Pages/                   ◀ فرانت‌اند: Razor Pages (RTL فارسی)
│   ├── Shared/_Layout.cshtml   نوار ناوبری بر اساس نقش
│   ├── Index.cshtml            داشبورد (مشتریان، تماس‌ها، کاربران، آخرین ممیزی)
│   ├── Login / Logout / ChangePassword
│   ├── Customers/              فهرست، ثبت/ویرایش، ادغام
│   ├── Settings/               SMTP + قالب ایمیل، کاربران پنل
│   ├── Audit.cshtml            ممیزی
│   └── Backup.cshtml           پشتیبان/بازگردانی
│
├── wwwroot/                 site.css، jdatepicker.js، password-eye.js (بدون CDN)
├── docs/sql/                اسکریپت اختیاری حذف جدول‌های قدیمی لایسنس
└── prepare/run/publish      اسکریپت‌های آماده‌سازی، اجرا و انتشار
```

## اجرا

```bash
./prepare.sh          # یا در ویندوز: prepare.cmd
./run.sh              # http://127.0.0.1:5399
```

- در اولین اجرا کاربر `admin` ساخته می‌شود و گذرواژه‌اش **فقط یک‌بار روی کنسول**
  چاپ می‌شود؛ در اولین ورود، تعویض آن اجباری است.
- پایگاه‌دادهٔ پیش‌فرض: SQLite در `data/support.db`. برای SQL Server فقط
  `ConnectionStrings:Panel` را عوض کنید (یا `ConnectionStrings__Panel`).
- رمز SMTP در پایگاه‌داده ذخیره نمی‌شود؛ آن را در متغیر محیطی
  `SUPPORTPANEL_SMTP_PASSWORD` بگذارید.

## انتشار

```bash
./publish.sh                 # چارچوب‌وابسته (نیازمند .NET 8 Runtime)
./publish.sh self-contained   # با Runtime داخل بسته
```

## وضعیت بیلد

```
dotnet build SupportPanel.csproj -c Release   ✅
```
