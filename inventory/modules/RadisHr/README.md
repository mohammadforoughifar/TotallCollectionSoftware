# RADIS-HR V019 — نسخهٔ .NET 8 + Blazor WebAssembly

بازنویسی کامل نرم‌افزار منابع انسانی RADIS-HR V019 (نسخهٔ قدیمی: HTML + JavaScript + localStorage)
به یک برنامهٔ استاندارد و تجاری با معماری زیر:

| لایه | فناوری |
|---|---|
| رابط کاربری | **Blazor WebAssembly (.NET 8)** — همان ظاهر و چیدمان نسخهٔ قبلی |
| ارتباط | **REST API** (JSON + JWT) |
| سرویس بک‌اند | **ASP.NET Core 8 Web API** |
| پایگاه داده | **SQL Server** — `Server=.` با احراز هویت ویندوز |
| نگاشت داده | Entity Framework Core 8 (Code-First + Migrations) |

---

## ۱. ساختار پروژه

```
radishr/
├─ RadisHr.sln
├─ src/
│  ├─ RadisHr.Shared/            # مدل‌ها، DTOها و موتورهای محاسباتی (مشترک بین کلاینت و سرور)
│  │  ├─ Models/                 # Employee, Attendance, StatutoryRules, Hse, Workflows, AppUser
│  │  ├─ Calculations/           # PayrollEngine, AttendanceEngine, PersianCalendarUtil
│  │  └─ Contracts/Dtos.cs
│  ├─ RadisHr.Api/               # وب‌سرویس REST + میزبانی فایل‌های Blazor
│  │  ├─ Controllers/            # ۱۰ کنترلر (auth, employees, payroll, attendance, ...)
│  │  ├─ Services/               # PayrollService, AttendanceService, TokenService,
│  │  │                          #  PasswordHasher, DeviceWorkbookParser
│  │  └─ Data/                   # AppDbContext, DbSeeder, Migrations
│  └─ RadisHr.Client/            # Blazor WebAssembly (SPA)
│     ├─ Pages/                  # ۱۳ صفحه، معادل ۱۳ بخش نسخهٔ قدیمی
│     ├─ Shared/                 # LoginView, RialInput, BarChart, PieChart, Toasts, ...
│     ├─ Services/               # ApiClient, AuthState, AppNav, Fa (قالب‌بندی فارسی)
│     └─ wwwroot/css/            # همان ۱۱ فایل CSS نسخهٔ قدیمی + radis-blazor.css
└─ tests/
   ├─ parity/                    # اوراکل جاوااسکریپتی (خروجی محاسبات نسخهٔ قدیمی)
   └─ RadisHr.ParityTests/       # مقایسهٔ خروجی C# با اوراکل
```

---

## ۲. اجرا روی سیستم شما (SQL Server محلی)

پیش‌نیاز: **.NET 8 SDK** و **SQL Server** روی همان ماشین (`Server=.`).

```powershell
cd radishr
dotnet restore
dotnet run --project src/RadisHr.Api
```

سپس مرورگر: <http://localhost:5080>

> ⚠️ **همیشه پروژهٔ `RadisHr.Api` را اجرا کنید، نه `RadisHr.Client`.**
> پروژهٔ Api هم سرویس REST را ارائه می‌دهد و هم فایل‌های Blazor را میزبانی می‌کند.
> اگر `RadisHr.Client` را جداگانه اجرا کنید، فقط یک سرور فایل استاتیک بالا می‌آید که
> کنترلری ندارد و هنگام ورود خطای **`405 Method Not Allowed`** با هدر `Allow: GET, HEAD` می‌گیرید.
> در Visual Studio مطمئن شوید پروژهٔ Startup روی **RadisHr.Api** تنظیم است
> (راست‌کلیک روی RadisHr.Api → Set as Startup Project).

- رشتهٔ اتصال در `src/RadisHr.Api/appsettings.json`:
  `Server=.;Database=RadisHrV019;Trusted_Connection=True;TrustServerCertificate=True`
- دیتابیس و جدول‌ها در نخستین اجرا به‌صورت خودکار با **EF Migrations** ساخته و
  با داده‌های پایه (قوانین ۱۴۰۵، واحدها، سطوح سازمانی، تعاریف HSE، کاربران، ۱۰ پرسنل نمونه) پر می‌شوند.

### اجرای نمایشی بدون SQL Server
```bash
Database__Provider=InMemory dotnet run --project src/RadisHr.Api --urls http://0.0.0.0:5080
```

### مستندات API
در حالت Development آدرس <http://localhost:5080/swagger> فهرست کامل سرویس‌ها را نشان می‌دهد.

---

## ۳. حساب‌های کاربری

رمز اولیهٔ همهٔ کاربران: **`Radis@1405`** (در نخستین ورود تغییر رمز اجباری است).

| نام کاربری | نقش | صفحهٔ خانه |
|---|---|---|
| `hr` | مالک فرآیند اداری | کارکنان |
| `ceo` | مدیرعامل | داشبورد مدیریتی |
| `guard` | مالک فرآیند نگهبانی | حضور و غیاب |
| `hse` | مسئول HSE | HSE |
| `warehouse` | انباردار | HSE |
| `production` | مدیر تولید | HSE / تولید روزانه |
| `finance` | مالی | تنخواه و مساعده |
| `accounting` | مدیر مالی حسابداری | حسابداری حقوق |

---

## ۴. پوشش ماژول‌ها (هر ۹ ماژول، کامل)

1. **حقوق و دستمزد** — محاسبهٔ ماهانه، پیش‌نمایش، فیش حقوقی قابل چاپ، خروجی CSV.
2. **حضور و غیاب** — ورود فایل دستگاه (XLSX بلوکی و لیست ترددی)، مرخصی، تردد دستی، بازبینی، گزارش روزانه.
3. **HSE** — حوادث (شماره‌گذاری `HSE-1405-0001`)، PPE، تجهیزات، کپسول آتش‌نشانی، گزارش‌ها، تنظیمات.
4. **شیفت نگهبانی** — چرخهٔ ۶ روزه (صبح/شب/استراحت) و اثر آن روی اضافه‌کار غیرمجاز.
5. **حسابداری حقوق** — اصلاحیه با دلیل اجباری، قفل ماه، تأیید و تسویه، ثبت پرداخت.
6. **ساختار و تنظیمات سازمان** — واحدها/ایستگاه‌ها، سطوح، ماتریس سازمانی، شیفت کاری، تقویم و تعطیلات.
7. **قوانین قانونی** — پارامترهای سالانه، پله‌های مالیاتی، سابقهٔ تغییرات (Audit).
8. **تحلیل مدیریتی** — داشبورد، روند چندماهه، نمودار میله‌ای/دایره‌ای.
9. **گردش کارها** — مساعده و اقساط، بایگانی، اطلاعیه‌ها و اعلان‌ها.

---

## ۵. تضمین یکسانی محاسبات (Parity)

خروجی موتور محاسباتی جدید با خروجی نسخهٔ جاوااسکریپتی به‌صورت خودکار مقایسه شده است:

```
tests/parity  →  ۲۳٬۰۸۸ مقایسه   |   ۰ اختلاف
```

رفتارهای نسخهٔ قدیمی **عیناً حفظ شده‌اند** (طبق درخواست صریح شما، اصلاح نشده‌اند):

- مقسوم‌علیه ۲۲۰ برای نرخ ساعتی (`hourly = base / 220`)
- فرمول کبیسه `year % 4 === 3`
- عدم تسهیم (proration) مزایای مسکن/خواربار/عائله‌مندی/حق اولاد
- گِرد کردن به سبک جاوااسکریپت: `Math.Floor(v + 0.5)`

اجرای تست:
```bash
dotnet run --project tests/RadisHr.ParityTests
```

---

## ۶. تنها تغییر عمدی: احراز هویت

| نسخهٔ قدیمی | نسخهٔ جدید |
|---|---|
| رمز به‌صورت متن ساده در localStorage | هش نمکی (PBKDF2) در SQL Server |
| کنترل دسترسی فقط سمت مرورگر | JWT + `[Authorize]` روی همهٔ کنترلرها |
| بدون انقضا | توکن ۱۲ ساعته + تغییر رمز اجباری در ورود اول |

سایر بخش‌ها از نظر منطق کاری و ظاهر، مطابق نسخهٔ قبلی است.

---

## ۷. فونت فارسی سامانه

فونت رابط کاربری از Tahoma به **Vazirmatn** تغییر کرده است — فونت استاندارد و مدرن فارسی با مجوز آزاد SIL OFL 1.1 (قابل استفادهٔ تجاری).

- فایل‌ها در `src/RadisHr.Client/wwwroot/fonts/` قرار دارند و **به‌صورت محلی سرو می‌شوند** (بدون CDN)، بنابراین در شبکهٔ داخلی و بدون اینترنت هم کار می‌کند.
- چهار وزن با فرمت فشردهٔ `woff2` استفاده شده: Regular 400، Medium 500، SemiBold 600، Bold 700 — مجموعاً حدود ۲۰۰ کیلوبایت.
- تعریف فونت در `wwwroot/css/fonts.css` و متغیر `--font-fa` است. **برای تغییر فونت کل نرم‌افزار فقط کافی است همین یک متغیر را عوض کنید.**
- نسخهٔ استاندارد وزیرمتن انتخاب شده، نه نسخهٔ Farsi-Digits؛ چون در این سامانه ارقام فارسی از سمت کد با `Fa.N` تولید می‌شود و فیلدهای ریالی عمداً با ارقام لاتین کار می‌کنند (`Fa.Rial`). انتخاب نسخهٔ Farsi-Digits این تفکیک را از بین می‌برد.
- برای اعداد جدول‌ها و مبالغ، `tabular-nums` فعال شده تا ارقام هم‌عرض باشند و ستون‌ها هنگام تغییر عدد جابه‌جا نشوند.
- فیش حقوقی A5 (پنجرهٔ چاپ در `radis-interop.js`) هم همین فونت را دارد و چاپ تا آمادهٔ شدن فونت صبر می‌کند (`document.fonts.ready`) تا خروجی با فونت درست چاپ شود.
- متن مجوز در `wwwroot/fonts/OFL.txt` نگهداری می‌شود.

---

## ۸. نکتهٔ فنی برای توسعهٔ آینده (مهم)

**هرگز به مجموعه‌هایی که با EF Core نگاشت شده‌اند (`OwnsMany` / `HasMany`) مقدار پیش‌فرض در property initializer ندهید.**

هنگام خواندن از پایگاه داده، EF Core ابتدا شیء را می‌سازد (پس initializer اجرا و آیتم‌های پیش‌فرض ساخته می‌شوند) و سپس ردیف‌های ذخیره‌شده را به همان لیست **اضافه** می‌کند؛ نتیجه دو برابر شدن داده است. همین اتفاق در `System.Text.Json` هنگام deserialize هم رخ می‌دهد.

الگوی درست که در `StatutoryRules` استفاده شده:

```csharp
public List<TaxBracket> TaxBrackets { get; set; } = new();        // خالی
public static List<TaxBracket> DefaultTaxBrackets() => new() { ... };  // کارخانه
public static StatutoryRules CreateDefault(int year = 1405) =>
    new() { Year = year, TaxBrackets = DefaultTaxBrackets() };
```

برای ساخت نمونهٔ دارای مقادیر پیش‌فرض از `StatutoryRules.CreateDefault()` استفاده کنید (در `DbSeeder`، کنترلر ایجاد سال جدید و آزمون parity همین کار انجام شده است).

همچنین در صفحه‌های Blazor، مجموعه‌هایی که در markup با اندیس ثابت خوانده می‌شوند (`_brackets` با ۵ سطر و `_contracts` با ۳ سطر) باید در همان field initializer پر شوند و مسیر بارگذاری داده به‌جای `return` زودهنگام، لیست را pad/truncate کند. `MainLayout` نیز کل `@Body` را در `Shared/PageBoundary.razor` می‌پیچد تا خطای رندر به‌جای صفحهٔ سفید، پیام فارسی نشان دهد.
