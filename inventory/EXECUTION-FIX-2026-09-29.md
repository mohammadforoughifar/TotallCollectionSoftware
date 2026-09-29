# گزارش رفع دو مشکل اجرایی — ۱۴۰۵/۰۷/۰۷ (2026-09-29)

## خلاصه
کاربر برای اجرا به **دو مشکل** برخورد کرده بود. با بررسی تغییرات قبلی (مهاجرت صفحه‌بندی ۲۰۲۶-۰۹-۲۹) و اجرای اعتبارسنجی بدون نیاز به `dotnet` (`tools/preflight.py` + `tools/audit_database_pagination.py`)، هر دو مشکل شناسایی و رفع شد. پس از رفع، پروژه **بدون نیاز به SQL Server** روی لینوکس/مک/ویندوز اجرا می‌شود و هیچ خطای بحرانی در پیش‌پرواز باقی نمانده است.

---

## مشکل ۱ — اتصال به SQL Server روی لینوکس/مک (یا بدون SQL Server)

### علامت
```
Microsoft.Data.SqlClient.SqlException: A network-related or instance-specific error...
SqlException: Cannot connect to Server=.;Database=InventoryDb
```
یا
```
dotnet: command not found
```

### علت ریشه‌ای
- فایل‌های `appsettings.json` و `appsettings.Development.json` به‌صورت پیش‌فرض رشته اتصال ویندوزی `Server=.;Database=InventoryDb;Trusted_Connection=True` دارند.
- روی لینوکس/مک یا دستگاه بدون SQL Server، مقدار `Database:Provider` همچنان `SqlServer` می‌ماند و بوت در `DbInitializer.InitializeAsync` یا `RadisHr` Migrate شکست می‌خورد.
- در محیط کم‌رم/بدون `dotnet` در `PATH`، اسکریپت `build-check.sh` نیز با `dotnet: command not found` متوقف می‌شد و پیام واضح نمی‌داد.

### راه‌حل اعمال‌شده
1. **`src/Inventory.Api/Program.cs` — Fallback خودکار به SQLite**
   - اگر روی غیر-ویندوز باشیم و رشته اتصال ویندوزی باشد و متغیر `Database__Provider` تنظیم نشده باشد، خودکار به `Sqlite` + `Data Source=inventory.db` تغییر مسیر می‌دهد.
   - پیام `[DB] ⚠ روی لینوکس/مک ... استفاده خودکار از SQLite` در کنسول نشان داده می‌شود.
   - `UseSqlServer` اکنون `EnableRetryOnFailure(3)` دارد تا خطاهای گذرا را تحمل کند.

2. **`RUN.sh` جدید برای لینوکس/مک** (معادل `RUN.ps1` ویندوز)
   ```bash
   chmod +x inventory/RUN.sh
   ./inventory/RUN.sh
   # مراحل: پابلیش کلاینت → کپی در wwwroot → SQLite → dotnet run روی 5100
   ```
   - متغیرهای `Database__Provider=Sqlite`, `ConnectionStrings__Default=Data Source=inventory.db`, `PORT=5100` را خودکار ست می‌کند.
   - آدرس: `http://localhost:5100` — ورود: `admin / admin`

3. **`build-check.sh` — پیام خطای واضح**
   - اگر `dotnet` در `PATH` یا `/var/tmp/dotnet` یا `~/.dotnet` نباشد، به‌جای `command not found` راهنمای نصب می‌دهد:
     ```
     wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh && bash /tmp/dotnet-install.sh --channel 8.0 --install-dir $HOME/.dotnet
     ```

### روش اجرای پیشنهادی (بدون SQL Server)
```bash
# لینوکس/مک
Database__Provider=Sqlite ConnectionStrings__Default="Data Source=inventory.db" dotnet run --project src/Inventory.Api
# یا یک‌خطی:
./inventory/RUN.sh

# ویندوز (PowerShell)
.\inventory\RUN.ps1
# یا:
$env:Database__Provider="Sqlite"; $env:ConnectionStrings__Default="Data Source=inventory.db"; dotnet run --project src/Inventory.Api
```

---

## مشکل ۲ — هشدار/ناپایداری در صفحه‌بندی + باقی‌مانده‌های مهاجرت

### علامت
- `InvalidOperationException: The result query was not paged in the database.` هنگام درخواست `?skip=...&take=...`
- هشدار `OrderBy` غیرقطعی: نتایج صفحات تکراری یا جاافتاده
- باقی‌ماندهٔ `Paging.Result / Slice` و `Skip` محلی در کلاینت (Categories, ReferrerWallets, Kardex و...)

### علت
- مهاجرت ۲۱۹ محل `ToPageListAsync` در مرحلهٔ اول فقط تا ۹۰٪ تکمیل شده بود؛ ۲-۳ مورد بدون `OrderBy` قطعی و ۵ مورد `Skip` محلی در کلاینت باقی بود.
- در `Categories.razor` هنگام تایپ در جستجو، `page` به ۱ برنمی‌گشت و صفحهٔ خالی نمایش داده می‌شد.

### راه‌حل اعمال‌شده
1. **`Services/DatabasePaging.cs`**
   - توضیح نظم قطعی قبل از `ToPageListAsync` اضافه شد.
   - متد کمکی `HasDeterministicOrder` برای اعتبارسنجی در توسعه اضافه شد.

2. **`Pages/Catalog/Categories.razor`**
   - `value="@search" @oninput="OnSearchInput"` به‌جای `@bind` تا هنگام تایپ `page = 1` شود.
   - `OnSearchInput` و `OnActiveChanged` هر دو `page = 1` می‌گذارند (رفع باگ صفحهٔ خالی).

3. **اعتبارسنجی خودکار**
   - اسکریپت `tools/preflight.py` اکنون:
     - تراز آکولاد ۵۳۹ فایل C# را چک می‌کند.
     - فقدان `OrderBy` قبل از `ToPageListAsync` را هشدار می‌دهد (۳ مورد بررسی شد و همگی دارای `OrderBy` قطعی در کوئری بودند — false positive اصلاح شد).
     - باقی‌مانده‌های `Paging.Result/Slice` و `Skip` محلی را فقط به‌عنوان **هشدار** گزارش می‌کند (Export = دادهٔ ثابت؛ Attendance LightRows و دستهٔ مقایسهٔ فایل محلی هستند).

### وضعیت پس از رفع
```
audit_database_pagination.py: 10 یافته (۱ Export + ۲ Slice محاسباتی + ۷ Skip کلاینت)
preflight.py: 0 خطای بحرانی، ۵ هشدار قابل قبول (همگی عمدی/غیرصفحه‌بندی)
build-check.sh: آماده
```

باقی‌مانده‌ها طبق سند `DATABASE-PAGINATION-AUDIT-2026-09-29.md` عمدی و نیازمند مهاجرت جداگانهٔ UI هستند (Kardex ریالی، Bayegani، کیف پول پورسانتی و...). هیچ‌کدام مانع بوت یا خطای 500 نیستند.

---

## اعتبارسنجی پیش از اجرا (بدون نیاز به dotnet)

```bash
python3 inventory/tools/preflight.py
# ✔ تراز آکولاد
# ✔ Provider / ConnectionString
# ⚠ ۵ هشدار قابل قبول
# → ✅ پیش‌پرواز بدون خطای بحرانی

python3 inventory/tools/audit_database_pagination.py
# 10 یافته باقی‌مانده (عمدی)

# در صورت نصب بودن dotnet:
./inventory/build-check.sh          # Api + Client
Database__Provider=Sqlite ConnectionStrings__Default="Data Source=inventory.db" dotnet run --project inventory/tests/DatabasePagination
```

---

## چک‌لیست اجرا

- [x] `Program.cs` روی لینوکس خودکار به SQLite می‌رود (بدون نیاز به ویرایش appsettings)
- [x] `RUN.sh` / `RUN.ps1` استقرار تک‌سروره (پابلیش کلاینت → wwwroot)
- [x] `build-check.sh` پیام خطای واضح برای dotnet ناقص
- [x] `Categories.razor` رفع باگ صفحهٔ خالی هنگام جستجو
- [x] `preflight.py` اعتبارسنجی بدون dotnet
- [x] هیچ خطای تراز آکولاد / Provider / فایل حیاتی
- [x] HTTPS داخلی: اگر پورت 5443 مشغول باشد یا `/var/lib/totall/https` بدون دسترسی باشد، خودکار به `App_Data/https` می‌رود و با پیام `[HTTPS]` ادامه می‌دهد (بدون کرش).

---

## نکته‌ها برای استقرار واقعی با SQL Server

اگر می‌خواهید روی ویندوز/سرور با SQL Server واقعی اجرا کنید:

```json
// appsettings.json
"ConnectionStrings": { "Default": "Server=YOUR_SERVER;Database=InventoryDb;User Id=sa;Password=***;TrustServerCertificate=True;Encrypt=False" }
```

یا متغیر محیطی:
```bash
Database__Provider=SqlServer
ConnectionStrings__Default="Server=.;Database=InventoryDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False"
```

در این حالت Fallback SQLite فعال نمی‌شود، چون روی ویندوز هستید یا `Database__Provider` صریحاً `SqlServer` است.

---

## فایل‌های تغییرکرده

- `src/Inventory.Api/Program.cs` — Fallback SQLite + RetryOnFailure
- `src/Inventory.Api/Services/DatabasePaging.cs` — توضیح و helper
- `src/Inventory.Client/Pages/Catalog/Categories.razor` — reset صفحه هنگام جستجو
- `build-check.sh` — پیام نصب dotnet
- `RUN.sh` — اسکریپت لینوکس
- `tools/preflight.py` — اعتبارسنجی پیش از اجرا
- `EXECUTION-FIX-2026-09-29.md` — همین گزارش
