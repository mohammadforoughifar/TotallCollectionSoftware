# رفع خطای ساخت Inventory.Shared.dll

## علت واقعی

پیام‌های زیر در Api و Client **پیام ثانویه** هستند:

```text
Metadata file '...\Inventory.Shared\obj\Debug\net8.0\ref\Inventory.Shared.dll' could not be found
```

در سورس قبلی `PagedResult<T>` دو بار در namespace یکسان `Inventory.Shared.Dtos` تعریف شده بود:

- `Dtos/CatalogDtos.cs`
- `Dtos/ProjectDtos.cs`

خطای اول کامپایل **CS0101** بود. در نتیجه `Inventory.Shared` ساخته نمی‌شد و هر دو پروژهٔ وابسته DLL آن را پیدا نمی‌کردند. حذف کش یا کپی دستی DLL نمی‌توانست این خطای سورس را حل کند؛ فاصله و پرانتز در مسیر Downloads علت این تعریف تکراری نیست.

## اصلاح انجام‌شده

1. یک تعریف مشترک در **`src/Inventory.Shared/Dtos/PagedResult.cs`** وجود دارد.
2. فیلدهای `Items`، `Page`، `PageSize`، `PageCount` و `SumTicks` حفظ شده‌اند. `Total` و `TotalCount` دو نام سازگار برای یک مقدار هستند؛ API پروژه و API کاتالوگ هر دو با قرارداد قبلی کار می‌کنند.
3. تولید Reference Assembly استاندارد SDK دوباره فعال است.
4. Target کپی دستی DLL به `obj/.../ref` حذف شده است؛ این کپی می‌توانست نسخهٔ قدیمی را نگه دارد و خطای واقعی را پنهان کند.
5. Api و Client با `ProjectReference` عادی به Shared وابسته‌اند. HR نیز داخل همین سه پروژه است و Shared/Client جدا ندارد.

## روی ویندوز

1. Visual Studio/IIS Express و هر اجرای قبلی برنامه را ببندید.
2. **.NET 8 SDK** نصب باشد (Runtime به‌تنهایی برای ساخت کافی نیست). Visual Studio 2022 را به‌روز کنید؛ برای SDKهای 8.0.4xx از نسخهٔ 17.11 یا جدیدتر استفاده کنید.
3. در PowerShell:

```powershell
cd "C:\مسیر پروژه\inventory"
.\rebuild.ps1
```

این اسکریپت فقط `bin` و `obj` پروژه‌های سورس/تست را پاک می‌کند، Restore انجام می‌دهد، ابتدا Shared و سپس کل Solution را می‌سازد و روی نخستین خطا متوقف می‌شود. فایل دیتابیس، آپلودها و کلیدها پاک نمی‌شوند.

برای Release:

```powershell
.\rebuild.ps1 -Configuration Release
```

سپس **`Inventory.sln`** را باز کنید؛ برای اجرای تک‌سروره:

```powershell
.\deploy-single.ps1
cd src\Inventory.Api
dotnet run
```

در حالت SQLite و بدون SQL Server می‌توانید به‌جای آن `RUN.ps1` را اجرا کنید.

## اگر باز هم خطا بود

در Output مربوط به Build، **اولین خطا** را بررسی کنید، نه فقط سطرهای Metadata در Error List:

```powershell
dotnet --list-sdks
dotnet restore Inventory.sln
dotnet build src/Inventory.Shared/Inventory.Shared.csproj -c Debug --no-restore
dotnet build Inventory.sln -c Debug --no-restore
```

- خطای NuGet باید پیش از کامپایل رفع شود؛ تنظیم منبع پروژه در `NuGet.config` است.
- در Configuration Manager، گزینهٔ Build برای Shared، Api و Client فعال باشد.
- DLL را دانلود یا دستی در پوشهٔ `ref` کپی نکنید؛ SDK آن را از سورس جاری تولید می‌کند.
- اگر فقط Visual Studio خطا دارد و CLI موفق است، VS را ببندید، کش محلی `.vs` را پاک و Solution را دوباره باز کنید.

تست قرارداد صفحه‌بندی: `dotnet run --project tests/Inventory.RegressionTests`.
Workflow مخزن همین Solution را روی ویندوز و لینوکس، در مسیری دارای فاصله و پرانتز، می‌سازد و وجود DLL مرجع را بررسی می‌کند.
