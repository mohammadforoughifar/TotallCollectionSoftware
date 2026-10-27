# رفع خطاهای ساخت پروژه — نسخهٔ دوم

مبنای بسته: `TotallCollectionSoftware-docarchive-fixed.7z` قبلی؛ اصلاحات مجوزهای آرشیو حفظ شده‌اند.

این نسخه علاوه بر اصلاح آرشیو، خطاهای گزارش‌شدهٔ Razor، snapshot و کنترلر IT را در سورس اصلی رفع می‌کند. هیچ صفحه یا migration از پروژهٔ اصلی حذف یا از build مستثنا نشده است.

## علت خطاها و اصلاح دقیق

### ۱. `ItClientCompanies.razor`

عبارت زیر C# معتبر نیست، چون داخل عملگر شرطی، HTML قرار گرفته بود:

```razor
@(c.OpenCount > 0 ? <span ...>@c.OpenCount</span> : "0")
```

به `@if / else` تبدیل شده است. خطاهای عجیب indexer مانند `this[?, ..., Edit, ...]`، خطاهای `Edit`/`Rotate`/`Delete` و `c`، و بسیاری از خطاهای namespace و top-level، نتیجهٔ خراب‌شدن کد تولیدشدهٔ Razor بودند؛ خود این متدها نیاز به حذف یا بازنویسی نداشتند.

همچنین `@inject IJSRuntime JS` از داخل `@code` به ابتدای فایل، کنار سایر `@inject`ها منتقل شد. خطاهای `IAuthState`، `IToastService`، `NavigationManager`، `LayoutState` و `ElementReference` در این زنجیره، دلیل افزودن package جدید نیستند؛ importهای لازم در `_Imports.razor` موجودند.

### ۲. `ItRemoteSettings.razor`

دکمهٔ بازگشت نقل‌قول تو‌در‌تو و نامعتبر داشت:

```razor
@onclick="() => Nav.NavigateTo("/it-requests")"
```

اکنون از handler نام‌دار استفاده می‌کند:

```razor
@onclick="GoToRequests"
```

```csharp
private void GoToRequests() => Nav.NavigateTo("/it-requests");
```

این تغییر خطای `TagHelper attributes must be well-formed` را برطرف می‌کند.

### ۳. `20261001130000_ItRemoteRequestChannel.Designer.cs`

فایل designer به‌اشتباه دوباره `AppDbContextModelSnapshot : ModelSnapshot` و `BuildModel` تعریف می‌کرد؛ درحالی‌که snapshot اصلی از قبل وجود داشت. اکنون designer ادامهٔ درست کلاس migration است:

```csharp
[DbContext(typeof(AppDbContext))]
[Migration("20261001130000_ItRemoteRequestChannel")]
partial class ItRemoteRequestChannel
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        // مدل هدف موجودِ این migration
    }
}
```

snapshot اصلی نگه داشته شده و `Up`/`Down` و دستورهای SQL موجود تغییر نکرده‌اند. ثبت metadata صحیح باعث می‌شود EF این migration را با شناسهٔ درست کشف کند. اصلاح، ساخت کلاس تکراری یا پاک‌کردن migration history نیست.

### ۴. `ItRequestsController.cs`

دو ایراد C# که پس از رفع خطاهای بالاتر نمایان می‌شدند نیز اصلاح شدند:

- متغیر فیلتر از `var query = _db.ItRequests` به `IQueryable<ItRequest> query = _db.ItRequests` تغییر کرد تا نتیجهٔ `Where` به `DbSet` نسبت داده نشود.
- `throw new NotFoundException()` که نوعش تعریف نشده بود، با پاسخ صریح `NotFound(...)` جایگزین شد؛ ویرایش شرکت ناموجود باید HTTP 404 برگرداند.

## اعمال نسخهٔ جدید

راه ساده: بستهٔ کامل نسخهٔ دوم را در **پوشهٔ تازه** استخراج کنید. بسته خودش همهٔ اصلاحات آرشیو و build را دارد؛ Patch را دوباره روی آن اعمال نکنید.

اگر روی بستهٔ قبلی کار می‌کنید، Patch افزایشی `TotallCollectionSoftware-build-fix.patch` نسبت به همان بسته است. در ریشهٔ مخزن:

```bash
git apply --check TotallCollectionSoftware-build-fix.patch
git apply TotallCollectionSoftware-build-fix.patch
```

Patch افزایشی را مستقیماً روی GitHub اولیه اعمال نکنید؛ اصلاحات آرشیو قبلی باید موجود باشند. برای اعمال همهٔ تغییرات از کامیت اولیه، از بستهٔ کامل یا Patch تجمیعی نسخهٔ دوم استفاده کنید. برای پروژه‌ای که تغییر محلی دارد، ابتدا Patch را بررسی و سپس ادغام کنید.

فایل‌های `bin` و `obj` یا فایل‌های تولیدشدهٔ `*.razor.g.cs` را از نسخهٔ قدیمی کپی نکنید. پس از جایگزینی سورس، Visual Studio را ببندید، `bin`/`obj` تولیدشدهٔ پروژه‌ها را پاک کنید یا `Clean Solution` بزنید و بعد Restore/Rebuild کنید. فایل‌های C# پشتیبان مثل `...copy.cs` در پوشهٔ پروژه باقی نگذارید؛ SDK آن‌ها را نیز کامپایل می‌کند.

از ریشهٔ بسته با .NET SDK 8:

```bash
dotnet clean inventory/Inventory.sln
dotnet restore inventory/Inventory.sln
dotnet build inventory/Inventory.sln -c Debug
dotnet build inventory/Inventory.sln -c Release
dotnet run --project inventory/tests/DocArchiveSecurity/DocArchiveSecurity.csproj -c Release
```

تنظیمات واقعی سرور، connection string، دیتابیس و فایل‌های بارگذاری‌شدهٔ کاربران را با فایل‌های نمونه جایگزین نکنید. برای رفع این خطاهای کامپایل، migration تازه نسازید و دیتابیس عملیاتی را پاک نکنید. اعمال migration یا انتشار سرور را فقط بعد از پشتیبان‌گیری و تست محیط آزمایشی انجام دهید؛ اجرای برنامه ممکن است migration موجودِ اکنون قابل کشف را بررسی کند.

## اعتبارسنجی

ساخت کامل **Debug و Release، هر دو با صفر خطا** انجام شد؛ هرکدام ۴۹ هشدار موجود دارند. هر **۱۰۴ بررسی رگرسیون عبور کرد**. نتایج دقیق در `BUILD-VERIFICATION.txt` ثبت شده‌اند. پروژهٔ آزمون اکنون اسمبلی واقعی `Inventory.Api` را reference می‌کند؛ همهٔ کنترلرها و migrationها در آن کامپایل می‌شوند. UiProbe نیز همهٔ صفحات Razor، از جمله دو صفحهٔ اصلاح‌شده را شامل می‌شود. تست‌ها metadata/کشف migration، تولید آفلاین اسکریپت SQL و endpoint شرکت ناموجود/فیلترهای IT را هم بررسی می‌کنند.

ساخت در Linux با SDK 8 انجام می‌شود؛ این جای تست اجرایی کامل روی محیط واقعی Windows/SQL Server، احراز هویت HTTP/JWT، سرویس‌های خارجی، چاپ و مهاجرت دیتابیس نیست. هشدارهای قبلی پروژه جدا از خطاهای build هستند و در گزارش مشخص می‌شوند.

در محیط آزمایش کم‌حافظه، build تک‌پردازه و GC کم‌مصرف برای محدودکردن مصرف RAM استفاده شد؛ فایل‌های production برای دورزدن محدودیت حافظه حذف نشده‌اند.

## یادآوری امنیت آرشیو

مجوزهای قبلاً ذخیره‌شدهٔ `CanDownload=true` خودکار پاک نشده‌اند؛ دسترسی‌هایی که باید Read بدون دانلود باشند را بازبینی کنید. پیش‌نمایش نیز DRM نیست و ذخیرهٔ محتوای دریافت‌شده یا اسکرین‌شات را مطلقاً ناممکن نمی‌کند. سیاست اولویت فرد/گروه و استثنای مدیر واقعی در `DOCARCHIVE-READONLY-FIX-FA.md` توضیح داده شده است.
