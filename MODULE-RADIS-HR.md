# منابع انسانی RADIS-HR — ماژول داخلی Inventory

منبع اولیه: `RadisHr-V019-dotnet8.rar` از مخزن `mohammadforoughifar/HR`.

## ادغام بومی (۲۰۲۶-۰۹-۰۷)

HR دیگر یک برنامه/SPA مستقل نیست. مانند مدیریت پروژه، صفحات و سرویس‌هایش در سه پروژهٔ اصلی کامپایل می‌شوند:

| لایه | مسیر سورس |
|---|---|
| مدل‌ها و موتور حقوق/کارکرد | `inventory/src/Inventory.Shared/RadisHr` |
| کنترلرها | `inventory/src/Inventory.Api/Controllers/RadisHr` |
| DbContext، Seed و Migration | `inventory/src/Inventory.Api/Data/RadisHr` |
| سرویس‌های بک‌اند | `inventory/src/Inventory.Api/Services/RadisHr` |
| صفحات | `inventory/src/Inventory.Client/Pages/RadisHr` |
| آداپتورهای کلاینت | `inventory/src/Inventory.Client/Services/RadisHr` |
| کنترل‌های مشترک | `inventory/src/Inventory.Client/Shared/RadisHr` |

- **یک Client، یک Router و یک منوی اصلی:** همهٔ ۱۳ صفحه داخل قالب Inventory نمایش داده می‌شوند، نه iframe، تب/برنامهٔ دیگر یا بارگذاری مجدد.
- **یک ورود و یک نشست:** HR مستقیماً `IAuthState` برنامه را مصرف می‌کند. هیچ `radisHrSession`، ورود، تغییر رمز یا JWT مستقل HR وجود ندارد. خطای 401 در درخواست عادی، آپلود یا دانلود، همان نشست اصلی را پایان می‌دهد.
- **یک Publish:** فقط `Inventory.Client` پابلیش می‌شود؛ فایل‌های HR همراه آن هستند. پروژه‌ها و Solution مستقل HR حذف شده‌اند.
- **مجوز مشترک:** `RadisHr.Access` مثل نسخهٔ قبلی مجوز **کامل** HR (از جمله حقوق، فایل‌ها و عملیات مدیریتی) است. سمت سرور و کلاینت کنترل می‌شود؛ Admin دسترسی دارد و برای دیگران باید صریحاً از تنظیمات نقش‌ها واگذار شود. داشتن نقش قدیمی Operator به‌تنهایی این مجوز را نمی‌دهد. نقش‌های ساختگی HR به JWT هسته اضافه نمی‌شوند.
- **عدم تداخل ظاهر:** CSS فقط زیر `.radis-hr` اعمال می‌شود؛ سایدبار، مدیریت پروژه، فرم‌های سایر بخش‌ها و اندازهٔ کاغذ چاپ آن‌ها تغییر نمی‌کنند. فونت‌های موجود Inventory و JS چاپ/ورودی ریالی استفاده می‌شوند.
- **بدون وابستگی ICU:** نمایش ارقام فارسی HR با تنظیم `InvariantGlobalization=true` کلاینت سازگار است.

## صفحه‌ها و مسیرها

تمام گزینه‌ها مستقیماً در منوی **منابع انسانی** هستند:

| بخش | مسیر |
|---|---|
| داشبورد | `/hr` |
| پرسنل | `/hr/employees` |
| ثبت/ویرایش اطلاعات و قرارداد | `/hr/employee-entry` |
| حضور و غیاب و ورود فایل دستگاه | `/hr/attendance` |
| حقوق و دستمزد و فیش | `/hr/payroll` |
| الزامات سالانه حقوق | `/hr/statutory-rules` |
| ساختار سازمانی و ماتریس پرسنل | `/hr/organization-structure` |
| تقویم و ساعات کاری | `/hr/organization-settings` |
| ایمنی، حوادث و PPE | `/hr/hse` |
| مساعده و اقساط | `/hr/finance` |
| حسابداری و بایگانی حقوق | `/hr/accounting` |
| گزارش روزانه حضور پرسنل | `/hr/production-daily` |
| اطلاعیه‌ها و ممیزی | `/hr/notices` |

نشانی‌های قدیمی `/radis-hr/...` نیز همان کامپوننت‌های داخلی Inventory را باز می‌کنند. بخش قبلی مرخصی/ماموریت، ورود و خروج شخصی و تقویم کاری هسته حذف نشده است.

API اصلی: **`/api/hr/...`**؛ مثال: `/api/hr/employees`.
APIهای قبلی **`/radis-hr/api/...`** alias همان کنترلرها با همان مجوز هستند. ورود HR قدیمی در هیچ‌کدام منتشر نمی‌شود؛ ورود فقط `/api/auth/login` است.

پاسخ آپلود فایل اکنون `uid` را صریحاً برمی‌گرداند تا پیوست قرارداد، HSE و اطلاعیه لینک صحیح داشته باشند. دانلود اطلاعیه با هدر Authorization انجام می‌شود، نه لینک بدون توکن یا JWT در query string.

## دیتابیس و حفظ داده

- هر دو DbContext از `ConnectionStrings:Default` استفاده می‌کنند.
- نام کلاس‌های مدل، namespaceهای مدل/بک‌اند، نام جدول‌ها، شناسه و محتوای Migrationهای SQL Server حفظ شده‌اند؛ موتورهای محاسباتی بدون تغییر منتقل شده‌اند.
- تاریخچهٔ SQL Server ماژول همچنان `__EFMigrationsHistory_RadisHr` است. جدول کاربران قدیمی `RadisHrUsers` حفظ می‌شود، اما ورود مستقلی روی آن فعال نیست و حساب/رمز پیش‌فرض جدیدی برای HR ساخته نمی‌شود.
- SQLite از مدل همان provider برای ساخت جدول‌های HR کنار جدول‌های هسته استفاده می‌کند؛ Migration مخصوص SQL Server روی SQLite اجرا نمی‌شود. اجرای مجدد جدول‌ها و داده‌ها را پاک نمی‌کند. اگر ساختار HR در SQLite از اجرای قدیمی ناقص مانده باشد، خطای روشن همراه نام جدول‌های مفقود می‌دهد، نه حذف خودکار اطلاعات.
- پرسنل نمونه فقط با `Database:SeedDemoData=true` ساخته می‌شوند؛ تنظیم پیش‌فرض false است.
- اسکریپت‌های استقرار فقط فایل‌های تولیدشدهٔ Blazor و SPA قدیمی را پاک می‌کنند؛ `uploads` و `SecureFiles` و کلیدهای رمزنگاری حفظ می‌شوند.

**پیش از ارتقای دیتابیس عملیاتی، از دیتابیس و فایل‌های رمزنگاری/کلیدها بکاپ بگیرید.**

## ساخت و اجرا

Solution فعال فقط `inventory/Inventory.sln` است. در Visual Studio پروژهٔ Startup را روی `Inventory.Api` قرار دهید.

```powershell
cd inventory
.\rebuild.ps1
.\deploy-single.ps1
cd src\Inventory.Api
dotnet run
```

برای اجرای محلی با SQLite: `inventory/RUN.ps1`.
در لینوکس: `cd inventory && ./deploy-single.sh` و سپس اجرای `src/Inventory.Api`.

پس از ورود به Inventory، گزینه‌های HR داخل همان منوی منابع انسانی هستند. کاربران دارای JWT قدیمی پس از تغییر مجوز باید یک بار خارج و دوباره وارد شوند.

## تست‌ها

از پوشهٔ `inventory` با **.NET 8 SDK**:

```bash
dotnet build Inventory.sln
dotnet run --project tests/Inventory.RegressionTests
dotnet run --project tests/RadisHr.ParityTests
dotnet run --project tests/Inventory.HrIntegrationTests
python ../tests/hr/test_structure.py
node ../tests/hr/interop_test.cjs
```

- Regression: قرارداد صفحه‌بندی، فارسی بدون ICU، مسیرها، نشست مشترک، تعویض کاربر، 401/403، آپلود و دانلود.
- Parity: اوراکل اصلی و ۲۳٬۰۸۸ مقایسهٔ حقوق و کارکرد.
- Integration: مسیرهای MVC، RBAC، تولید SQL مایگریشن بدون نیاز به سرور SQL، جدول‌های مشترک SQLite و حفظ داده پس از اجرای مجدد.
- CI ویندوز و لینوکس: Debug/Release، پوشهٔ دارای فاصله/پرانتز، وجود `ref/Inventory.Shared.dll` و Publish کلاینت یکپارچه.

رفع خطای اصلی Metadata در [راهنمای ساخت](inventory/docs/BUILD-TROUBLESHOOTING.md) توضیح داده شده است.
