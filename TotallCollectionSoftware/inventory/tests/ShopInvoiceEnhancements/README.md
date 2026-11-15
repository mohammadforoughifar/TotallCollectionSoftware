# آزمون تغییرات فاکتور فروشگاهی

در محیط دارای .NET 8 و دسترسی به وابستگی‌های پروژه:

```sh
dotnet run --project tests/ShopInvoiceEnhancements/ShopInvoiceEnhancements.csproj
dotnet run --project tests/SaleChequeCharge/SaleChequeCharge.csproj
dotnet run --project tests/PrintProbe/PrintProbe.csproj
```

آزمون نخست از SQLite در حافظه و اسکریپت تولیدشدهٔ migration برای SQL Server استفاده می‌کند. موارد اصلی: ارتقای تکرارپذیر و داده‌های قدیمی، درصد و مبنای پورسانت، محرمانگی، نرخ ناقص، ویرایش مقدار، بهای برآوردی، چک‌های دستی نامساوی، گردش مشتری، بازه‌ها، برگشت و جلوگیری از ثبت چک کمتر از آستانهٔ تشخیص. اجرای اسکریپت migration روی SQL Server واقعی و آزمون Blazor/API واقعی همچنان جداگانه لازم است.

آزمون‌های Python در همین پوشه قرارداد سیم‌کشی کد را می‌سنجند؛ جایگزین آزمون اجرایی EF، API یا کامپایل Razor نیستند.
