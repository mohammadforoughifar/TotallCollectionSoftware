# قابلیت «داشبورد من» — داشبورد شخصی قابل طراحی توسط کاربر

تاریخ: ۱۴۰۵/۰۶/۲۵ (2026-09-16) — شاخه: `main` (بدون کامیت، آمادهٔ بازبینی)

## ۱) هدف

هر کاربر بتواند داشبورد شخصی خودش را از **داده‌هایی که مجوز دیدنشان را دارد** بسازد،
چیدمان کند، ذخیره کند و هر روز باز کند؛ و بعداً ویجت اضافه/کم/جابه‌جا کند.
هر کاربر می‌تواند چند داشبورد داشته باشد و یکی را «پیش‌فرض» کند.

## ۲) معماری

| لایه | فایل/مکان | توضیح |
|---|---|---|
| دیتابیس | `Data/DashboardSchemaV1.cs` | ساخت خودکار و idempotent دو جدول `UserDashboards` و `UserDashWidgets` برای **SQLite و SQL Server** (بدون EF Migration، مطابق الگوی `WorkOrderSchemaV1`) |
| موجودیت | `Entities/Dashboards/UserDashboardEntities.cs` | `UserDashboard` + `UserDashWidget` |
| DTO | `Inventory.Shared/Dtos/DashboardDtos.cs` | `WidgetDefDto`, `WidgetCatalogDto`, `WidgetDataDto`, `DashSeriesDto`, `DashRowDto`, `DashListItemDto`, `UserDashboardDto`, `UserDashWidgetDto` + enumهای `DashWidgetKind`, `DashChartType`, `DashRange` |
| کاتالوگ ویجت | `Services/Dashboards/WidgetCatalog.cs` | ۳۰ ویجت در ۱۰ دسته، هرکدام با `Module` (کلید مجوز)، `Kind`، اندازهٔ پیشنهادی و گزینه‌های پیکربندی |
| محاسبهٔ داده | `Services/Dashboards/WidgetDataService.cs` | پیاده‌سازی کوئری همهٔ ۳۰ کلید؛ سطل‌بندی ماهانه بر اساس **تقویم شمسی** (`PersianDate`)، مقایسه با بازهٔ قبل، ارقام فارسی (`Fa.Digits`) |
| API | `Controllers/Dashboards/DashboardsController.cs` | CRUD + دادهٔ ویجت، مشتق از `ControllerBase` با `[Authorize]` (نه `RbacControllerBase` چون آن پایه نقش را به Admin/Operator محدود می‌کند) |
| کلاینت | `Services/DashboardClient.cs`, `Pages/Dashboards/MyDashboards.razor`, `Shared/DashWidgetCard.razor`, `wwwroot/js/dashboard.js` | صفحهٔ جدید منو، گرید ۱۲ستونه، drag & drop، resize، تنظیمات ویجت، Chart.js (از قبل در `index.html` bundle است) |
| منو/مسیر | `Layout/NavMenu.razor`, `Layout/MainLayout.razor`, `Services/ModuleAccess.cs` | آیتم «داشبورد من» در گروه داشبورد + RouteGuard |
| مجوز | `Data/RbacSeeder.cs`, `Pages/Settings/Roles.razor`, `Services/Core/AuthService.cs` | ماژول `MyDashboards` با دو اقدام `View` و `Design`؛ به همهٔ نقش‌های فعال داده می‌شود |

## ۳) endpoints

| متد | مسیر | مجوز | توضیح |
|---|---|---|---|
| GET | `/api/my-dashboards/widget-catalog` | `MyDashboards.View` | کاتالوگ ویجت‌های **مجازِ همین کاربر** + `canDesign` |
| GET | `/api/my-dashboards/widget-data/{key}?range=&limit=` | `MyDashboards.View` + مجوز ماژول خود ویجت | دادهٔ ویجت؛ بررسی مجدد سمت سرور |
| GET | `/api/my-dashboards` | `MyDashboards.View` | داشبوردهای کاربر به‌همراه ویجت‌ها |
| POST | `/api/my-dashboards` | `MyDashboards.Design` | ساخت داشبورد (با ویجت‌ها) |
| PUT | `/api/my-dashboards/{id}` | `MyDashboards.Design` + مالکیت | ویرایش نام/پیش‌فرض + جایگزینی کامل چیدمان |
| DELETE | `/api/my-dashboards/{id}` | `MyDashboards.Design` + مالکیت | حذف داشبورد و ویجت‌هایش |
| POST | `/api/my-dashboards/{id}/default` | `MyDashboards.Design` + مالکیت | ست‌کردن داشبورد پیش‌فرض |

### امنیت (مهم)
- چیدمان ذخیره‌شده **فقط UI** است؛ هیچ اعتمادی به آن نمی‌شود.
- `widget-data` قبل از محاسبه، ماژول ویجت را با مجوزهای واقعی کاربر چک می‌کند و در صورت نبودِ مجوز `403` می‌دهد.
- داشبوردها per-user هستند؛ کاربر دیگر حتی با دانستن `id` نمی‌تواند بخواند/ویرایش کند (`404`).
- نقش‌های قدیمی (legacy) که در جدول RBAC ردیف ندارند، در `AuthService` و `DashboardsController` با fallback یکسان پوشش داده شدند تا ماژول‌های شخصی (`MyCartable`, `MyArchive`, `MyDashboards`) از دست نروند.

## ۴) کاتالوگ ویجت‌ها (۳۰ ویجت / ۱۰ دسته)

### آرشیو اسناد
- `doc-expiring` — مدارک رو به انقضا (جدول) ماژول: `DocArchive`

### انبارداری
- `inv-stock-value` — ارزش موجودی (KPI) ماژول: `InvDocs`
- `inv-doc-count` — اسناد انبار (KPI) ماژول: `InvDocs`
- `inv-doc-trend` — روند اسناد انبار (نمودار) ماژول: `InvDocs`

### حسابداری
- `acc-voucher-count` — اسناد حسابداری (KPI) ماژول: `AccVouchers`
- `acc-voucher-trend` — روند صدور سند (نمودار) ماژول: `AccVouchers`

### خزانه‌داری
- `trs-payment-period` — پرداختی خزانه (KPI) ماژول: `TrsVouchers`
- `trs-cheques-due` — چک‌های سررسیدنزدیک (جدول) ماژول: `TrsCheques`
- `trs-cheques-amount` — چک‌های نزد ما (KPI) ماژول: `TrsCheques`
- `trs-receipt-period` — دریافتی خزانه (KPI) ماژول: `TrsVouchers`
- `trs-flow-trend` — روند دریافت و پرداخت (نمودار) ماژول: `TrsVouchers`

### دستور کار
- `wo-open` — دستور کارهای باز (KPI) ماژول: `WorkOrders`
- `wo-overdue` — دستور کارهای معوق (KPI) ماژول: `WorkOrders`
- `wo-by-status` — وضعیت دستور کارها (نمودار) ماژول: `WorkOrders`

### فروش و فاکتور
- `fac-top-parties` — برترین طرف حساب‌ها (جدول) ماژول: `FacInvoices`
- `fac-invoice-count` — تعداد فاکتور (KPI) ماژول: `FacInvoices`
- `fac-purchase-period` — جمع خرید (KPI) ماژول: `FacInvoices`
- `fac-sales-period` — جمع فروش (KPI) ماژول: `FacInvoices`
- `fac-sales-trend` — روند فروش (نمودار) ماژول: `FacInvoices`
- `fac-sales-today` — فروش امروز (KPI) ماژول: `FacInvoices`
- `fac-sales-vs-purchase` — فروش در برابر خرید (نمودار) ماژول: `FacInvoices`
- `fac-top-products` — کالاهای پرفروش (جدول) ماژول: `FacInvoices`

### کالا و موجودی
- `prod-reorder-count` — اقلام زیر نقطه سفارش (KPI) ماژول: `Products`
- `prod-reorder-list` — فهرست اقلام رو به اتمام (جدول) ماژول: `Products`
- `prod-stock-by-warehouse` — موجودی به تفکیک انبار (نمودار) ماژول: `Products`

### مالی
- `exp-by-category` — هزینه به تفکیک دسته (نمودار) ماژول: `Expenses`
- `exp-period` — هزینه‌ها (KPI) ماژول: `Expenses`

### مدیریت سیستم
- `it-open` — درخواست‌های IT باز (KPI) ماژول: `ItRequests`

### منابع انسانی
- `hr-employees` — پرسنل فعال (KPI) ماژول: `HrCore`
- `fa-att-today` — ترددهای امروز (KPI) ماژول: `FaAtt`

## ۵) تست‌های انجام‌شده (اجرای واقعی API روی SQLite با دادهٔ نمونه)

| سناریو | نتیجه |
|---|---|
| بیلد `Inventory.Api` و `Inventory.Client` | ✅ Build succeeded / 0 Error |
| seed مجوزها | ✅ ۳۳۸ مجوز / ۶۱ ماژول؛ جدول `UserDashboards` خودکار ساخته شد |
| کاتالوگ برای Admin | ✅ `canDesign=true`، ۳۰ ویجت، ۱۰ دسته |
| کاتالوگ برای کاربر محدود (نقش Employee بدون مجوز ماژول‌های داده) | ✅ `canDesign=true`، ۰ ویجت (نه ۴۰۳) |
| دادهٔ ویجت برای کاربر بدون مجوز ماژول | ✅ HTTP 403 |
| بدون توکن | ✅ HTTP 401 |
| کلید ویجت نامعتبر | ✅ HTTP 404 + پیام فارسی |
| CRUD کامل (ساخت، پیش‌فرض، ویرایش نام، جایگزینی چیدمان، حذف) | ✅ همه ۲۰۰/۲۰۱ و round-trip درست |
| جداسازی داده بین کاربران | ✅ کاربر دیگر ۰ داشبورد می‌بیند و PUT روی داشبورد غیرخودش ۴۰۴ |
| دادهٔ همهٔ ۳۰ ویجت | ✅ ۰ خطا (KPI/نمودار/جدول/فهرست) |
| جاروی نهایی: ۳۰ ویجت × ۶ بازهٔ زمانی (امروز/هفته/ماه/فصل/سال/همه) | ✅ ۱۸۰ درخواست، ۰ خطا |
| `cfg` (مثل limit)، `chartType` و `range` از کوئری‌استرینگ | ✅ اعمال می‌شوند |
| بازهٔ «همه» | ✅ مقایسهٔ «نسبت به بازهٔ قبل» حذف شد (بی‌معنی بود) |

نمونهٔ صحت محاسبات (دادهٔ تست دستی):
- `fac-sales-today` = ۲۵٬۰۰۰٬۰۰۰ ریال؛ `fac-sales-period` (این ماه) = ۵۵٬۰۰۰٬۰۰۰ با «+۸۳٪ نسبت به بازهٔ قبل»
- `fac-sales-trend` → تیر ۹M / مرداد ۳۰M / شهریور ۵۵M (سطل شمسی درست)
- `trs-cheques-amount` = ۲۱٬۶۰۰٬۰۰۰ (۳ چک وصول‌نشده)؛ `trs-cheques-due` با لحن danger/warning بر اساس سررسید
- `exp-period` = ۳٬۱۵۰٬۰۰۰ با «+۳۷٪»؛ `prod-reorder-list` با لحن danger برای موجودی صفر

## ۶) باگ‌هایی که حین پیاده‌سازی پیدا و رفع شد

1. **`RbacSeeder` (باگ قدیمی و نهفته):** بلوک firstSeed برای نقش‌های Operator/Accountant بدون بررسی
   مجوزهای موجود، `RolePermission` اضافه می‌کرد → خطای EF
   «another instance with the same key {'RoleId','PermissionId'} is already being tracked».
   این باگ فقط بعد از بازگردانی کاتالوگ مجوزها (تسک ممیزی) فعال شد. رفع: فیلتر با `operatorHas`/`accountantHas`.
2. **جمع `decimal` در SQLite:** `SumAsync(x => (decimal?)x.Field)` روی SQLite خطا می‌دهد
   («SQLite cannot apply aggregate operator 'Sum' on expressions of type 'decimal'»).
   رفع: جمع به‌صورت `(double)` در پایگاه‌داده و تبدیل به `decimal` در حافظه — روی SQL Server هم درست کار می‌کند.
   (۱۵ ویجت درگیر بودند.)
3. **ناسازگاری سطل شمسی با گروه‌بندی میلادی:** `MonthSeries` کلید `(Year, Month)` **شمسی** را با
   خروجی `GroupBy(Date.Year, Date.Month)` **میلادی** مقایسه می‌کرد → همهٔ نمودارهای روند صفر می‌شدند.
   رفع: گروه‌بندی روزانهٔ میلادی + تبدیل هر روز به شمسی با `JalaliRow(...)` و جمع در سطل.

## ۷) اجرا / تست دستی

```bash
export Database__Provider=Sqlite
export ConnectionStrings__Default="Data Source=/path/to/dev.db"
export Database__SeedDemoData=true
export ASPNETCORE_URLS=http://0.0.0.0:5100
dotnet run --project inventory/src/Inventory.Api/Inventory.Api.csproj
```
در اپ: منو ← «داشبورد من» ← «طراحی داشبورد» ← «افزودن ویجت» ← کشیدن/تغییر اندازه ← «ذخیرهٔ چیدمان».

> نکته: دادهٔ نمونهٔ Seeder فقط محصولات/انبار/اشخاص را می‌سازد؛ برای دیدن اعداد مالی باید
> فاکتور/سند خزانه/هزینه ثبت شود.
