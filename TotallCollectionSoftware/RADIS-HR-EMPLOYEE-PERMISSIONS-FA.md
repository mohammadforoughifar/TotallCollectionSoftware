# تفکیک «افزودن» و «ویرایش» پرسنل در منابع انسانی بن‌سازه

تاریخ: ۱۴۰۵/۰۸/۰۴

## پاسخ کوتاه
قبلاً **خیر** — فرم پرسنل بن‌سازه با یک مجوز (`RadisHr.EmployeeEntry`) هم ثبت و هم ویرایش می‌کرد.
اکنون **بله** — دو مجوز مستقل در «تنظیمات ← نقش‌ها و دسترسی‌ها ← منابع انسانی بن‌سازه» اضافه شد:

| مجوز | برچسب فارسی | اثر |
|---|---|---|
| `RadisHr.EmployeeCreate` | افزودن پرسنل جدید | ثبت پرسنل جدید + ورود گروهی (ردیف‌های جدید) |
| `RadisHr.EmployeeUpdate` | ویرایش پروندهٔ پرسنلی | ویرایش پرونده + بازنویسی ردیف‌های موجود در ورود گروهی |
| `RadisHr.EmployeeEntry` | ورود اطلاعات (قدیمی) | هم‌ارز هر دو مجوز بالا — برای سازگاری حفظ شد |
| `RadisHr.Access` | دسترسی کل ماژول | هر دو |

## ۱) نقش‌ها و مجوزها (سمت سرور)
`Inventory.Api/Data/RbacSeeder.cs`
- دو اکشن `EmployeeCreate` و `EmployeeUpdate` به ماژول `RadisHr` اضافه شد؛ چون seeder برای دیتابیس‌های موجود هم اجرا می‌شود، مجوزها به‌صورت خودکار ساخته می‌شوند (بدون مهاجرت دستی).
- **مهاجرت دسترسی:** هر نقشی که از قبل `RadisHr.EmployeeEntry` داشت، به‌طور خودکار هر دو مجوز جدید را هم می‌گیرد ⇒ با ارتقا دسترسی هیچ کاربری قطع نمی‌شود؛ از این پس می‌توان آن‌ها را جدا کرد.
- مجوزهای جدید خودکار به نقش `Admin` هم داده می‌شوند (رفتار موجود seeder).

## ۲) اعمال واقعی در API (نه فقط ظاهری)
`Inventory.Api/Modules/RadisHr/Controllers/EmployeesController.cs`
- `POST api/employees` (ایجاد): نیازمند `EmployeeCreate` (یا `EmployeeEntry` / `Access` / نقش‌های `hr,ceo`) ⇒ در غیر این صورت **۴۰۳** با پیام فارسی.
- `PUT api/employees/{id}` (ویرایش): نیازمند `EmployeeUpdate`.
- `POST api/employees/import`: نیازمند `EmployeeCreate`؛ و اگر `Overwrite=true` باشد نیازمند `EmployeeUpdate` هم است.

## ۳) رابط کاربری
- `Pages/BonHr/EmployeeEntryBon.razor`
  - دکمهٔ **«پرسنل جدید»** و **«ورود گروهی»** فقط با `EmployeeCreate`.
  - دکمهٔ **«ویرایش پرونده»** فقط با `EmployeeUpdate`.
  - اگر کاربر مجوز عمل جاری را نداشته باشد، فرم باز نمی‌شود و پیام روشن با نام مجوز لازم نمایش داده می‌شود.
  - چک‌باکس «بازنویسی پرونده‌های موجود» بدون `EmployeeUpdate` غیرفعال است و ورود گروهی فقط ردیف جدید ثبت می‌کند.
- `Pages/BonHr/EmployeesBon.razor`
  - دکمهٔ «پرسنل جدید» با `EmployeeCreate`؛ دکمه‌های ویرایش (ردیف و مودال) با `EmployeeUpdate`.
- `Pages/BonHr/BonHrGuard.razor`: پارامتر جدید `AlsoPermissions` — بخش «ورود اطلاعات» با هر یک از دو مجوز باز می‌شود.
- `Layout/NavMenu.razor`: آیتم منوی «ورود اطلاعات» برای دارندهٔ هر یک از دو مجوز هم نمایش داده می‌شود (`CanSeeBonHrSection`).
- `Pages/Settings/Roles.razor`: برچسب فارسی سه مجوز برای انتخاب در درخت دسترسی‌ها.

## ۴) سناریوهای پیشنهادی
- **کارشناس ثبت:** `RadisHr.EmployeeCreate` + `RadisHr.Employees` (بدون ویرایش).
- **مسئول اصلاح پرونده:** `RadisHr.EmployeeUpdate` + `RadisHr.Employees`.
- **مدیر منابع انسانی:** `RadisHr.Access` یا هر دو مجوز.

## ۵) فایل‌های تغییریافته
- `inventory/src/Inventory.Api/Data/RbacSeeder.cs`
- `inventory/src/Inventory.Api/Modules/RadisHr/Controllers/EmployeesController.cs`
- `inventory/src/Inventory.Client/Pages/BonHr/EmployeeEntryBon.razor`
- `inventory/src/Inventory.Client/Pages/BonHr/EmployeesBon.razor`
- `inventory/src/Inventory.Client/Pages/BonHr/BonHrGuard.razor`
- `inventory/src/Inventory.Client/Layout/NavMenu.razor`
- `inventory/src/Inventory.Client/Pages/Settings/Roles.razor`
