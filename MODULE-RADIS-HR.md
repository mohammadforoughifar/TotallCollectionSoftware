# ماژول جامع منابع انسانی RADIS-HR V019

این ماژول از فایل `RadisHr-V019-dotnet8.rar` مخزن
[`mohammadforoughifar/HR`](https://github.com/mohammadforoughifar/HR.git) به برنامه جامع منتقل شده است.

## مدل ادغام

- **یک Host و یک آدرس:** ماژول زیر مسیر `/radis-hr/` توسط `Inventory.Api` سرو می‌شود.
- **یک ورود:** کلاینت RADIS-HR نشست `authSession` برنامه اصلی و همان JWT را مصرف می‌کند؛ صفحه ورود دوم نمایش داده نمی‌شود.
- **یک دیتابیس:** هر دو DbContext از `ConnectionStrings:Default` استفاده می‌کنند.
- **عدم تداخل Migration:** تاریخچه Migration ماژول در جدول `__EFMigrationsHistory_RadisHr` نگهداری می‌شود.
- **عدم تداخل کاربران:** تنها نگاشت فنی جدول `AppUser` ماژول به `RadisHrUsers` تغییر کرده است. خود کلاس موجودیت هیچ تغییری نکرده است.
- **مسیر API:** مسیرهای اصلی کنترلرها بدون ویرایش فایل کنترلر، در Host با پیشوند `/radis-hr` منتشر می‌شوند؛ نمونه: `/radis-hr/api/employees`.
- **RBAC:** مجوز `RadisHr.Access` به نقش Admin خودکار داده می‌شود و از صفحه نقش‌ها قابل واگذاری است.
- بخش قبلی منابع انسانی (مرخصی/ماموریت و حضور و غیاب) طبق تصمیم پروژه حذف یا تغییر نکرده و کنار این ماژول باقی مانده است.

## تضمین حفظ سورس اصلی

فایل‌های زیر عیناً از آرشیو منتقل شده‌اند و در آن‌ها تغییری داده نشده است:

- تمام موجودیت‌ها، DTOها و موتورهای محاسباتی: `inventory/modules/RadisHr/src/RadisHr.Shared`
- تمام کنترلرها: `inventory/modules/RadisHr/src/RadisHr.Api/Controllers`
- تمام سرویس‌های بک‌اند: `inventory/modules/RadisHr/src/RadisHr.Api/Services`
- تست parity و فایل expected اصلی: `inventory/modules/RadisHr/tests`

تغییرات سازگاری فقط در لایه Composition/Hosting، نگاشت نام جدول کاربران، تنظیمات بدون رمز و SSO کلاینت انجام شده‌اند. کد Host در
`inventory/src/Inventory.Api/Infrastructure/RadisHrIntegration.cs` قرار دارد.

## اجرا

```powershell
cd inventory
.\deploy-single.ps1
cd src\Inventory.Api
dotnet run
```

یا روی لینوکس:

```bash
cd inventory
./deploy-single.sh
cd src/Inventory.Api
dotnet run
```

سپس:

1. برنامه اصلی: `http://localhost:5100`
2. ورود با حساب برنامه اصلی
3. منوی **منابع انسانی ← سامانه جامع RADIS-HR**
4. مسیر مستقیم: `http://localhost:5100/radis-hr/`

> پس از نصب این نسخه، کاربران دارای نشست قدیمی باید یک بار خارج و دوباره وارد شوند تا claim مجوز `RadisHr.Access` داخل JWT جدید قرار گیرد.

## اجزای منتقل‌شده

- پرسنل و قراردادها
- حضور و غیاب، ورود XLSX، مرخصی/ماموریت و تردد دستی
- حقوق و دستمزد و فیش حقوقی
- حسابداری حقوق، مساعده، اقساط، بایگانی و پرداخت
- قوانین قانونی سالانه و Audit
- HSE، حوادث، PPE و کپسول آتش‌نشانی
- ساختار سازمانی، ایستگاه‌ها، تقویم و شیفت نگهبانی
- داشبورد و تحلیل مدیریتی
- اطلاعیه‌ها، اعلان‌ها و فایل‌ها

## نکات دیتابیس

هنگام شروع Host، Migration و Seed اصلی RADIS-HR به‌طور خودکار اجرا می‌شود. جدول‌های ماژول همان نام‌های اصلی را دارند؛ فقط جدول `Users` ماژول برای جلوگیری از برخورد با جدول کاربران هسته، `RadisHrUsers` نام‌گذاری شده است. حساب‌های seed قدیمی RADIS-HR برای سازگاری داده‌ای ایجاد می‌شوند، اما ورود UI فقط از حساب و JWT برنامه جامع انجام می‌شود.
