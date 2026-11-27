# بیلد کلاینت Blazor در ویندوز (.NET 8)

`Inventory.Client` برای `net8.0` است. اگر خطای `DirectoryNotFoundException` برای
`obj\Debug\net8.0\fx\Inventory.Client\_framework\Microsoft.Extensions.Configuration.FileExtensions.dll` دیدید،
این یک **مسیر تولیدی در زمان بیلد** است، نه فایلی که باید دستی داخل سورس کپی شود.
در گزارش، مسیر مقصد ۲۶۰ کاراکتر است و تسک از WebAssembly Pack نسخهٔ ۱۱ اجرا می‌شود.

## تغییرات این نسخه

- سه `global.json` (ریشهٔ بسته، `TotallCollectionSoftware` و `inventory`) همگی `version: 8.0.100`، `rollForward: major` و `allowPrerelease: false` دارند. بر خلاف `latestMajor`، اگر SDK ۸ سازگار موجود باشد همان خانواده ترجیح داده می‌شود. اگر نباشد، انتخاب نسخهٔ پایدار بالاتر مجاز است؛ روی سیستم فقط با `11.0.100` همان SDK انتخاب می‌شود. این تنظیم **TargetFramework را تغییر نمی‌دهد** و SDK نصب نمی‌کند.
- `inventory/Directory.Build.props` فقط برای `Inventory.Client` در ویندوز، مسیر پیش‌فرض موقت را به `%LOCALAPPDATA%\TCS\obj\<checkout-key>\` می‌برد. کلید از مسیر کامل پروژه به دست می‌آید تا نسخه‌های جداگانه روی یکدیگر ننویسند. `Restore` و `Build` از همان مسیر استفاده می‌کنند. `bin`، مسیر انتشار و فایل‌های کاربر جابه‌جا نمی‌شوند.
- برای حفظ فرمت بوت قبلی، `WasmEnableWebcil=false` باقی مانده است. حذف DLLها، ویرایش فایل‌های `.targets` در NuGet یا تغییر رجیستری لازم نیست.

## بررسی در یک کپی تازه

آرشیو را مستقیم در پوشه‌ای کوتاه مانند `C:\src\TCS` استخراج کنید. در پوشهٔ Solution مرجع (`TotallCollectionSoftware\inventory`) اجرا کنید:

```powershell
dotnet --list-sdks
dotnet --version
dotnet restore .\src\Inventory.Client\Inventory.Client.csproj
dotnet build .\src\Inventory.Client\Inventory.Client.csproj -c Debug --no-restore
```

در نصب فقط با SDK `11.0.100`، خروجی `dotnet --version` از داخل همین پوشه باید `11.0.100` باشد؛ در نصب آزمایشی دارای هر دو SDK، `8.0.428` انتخاب شد. سپس `Inventory.sln` **همین پوشه** را در Visual Studio باز و Rebuild کنید.

**NETSDK1141 هنگام باز کردن بستهٔ قبلی:** این خطا پیش از کامپایل می‌گوید resolver نتوانسته درخواست `global.json` را با SDKهای قابل استفاده برآورده کند. فایل گزارش‌شده در `C:\Program Files\dotnet\sdk\...\targets` را ویرایش نکنید. Visual Studio را ببندید، بستهٔ جدید را در پوشهٔ تازه استخراج کنید و Solution همان نسخه را باز کنید. اگر فقط VS خطا دارد ولی CLI موفق است، سازگاری نسخهٔ Visual Studio با SDK نصب‌شده را بررسی کنید. برای بیلد با نصب استاندارد SDK ۸ نیز می‌توانید آن را **کنار** ۱۱ نصب کنید (`winget install --id Microsoft.DotNet.SDK.8 --exact --architecture x64`).

**اجرا جدا از بیلد است:** `Inventory.Api` همچنان `net8.0` است. اگر بعد از بیلد، اجرای برنامهٔ framework-dependent خطای نبود `Microsoft.AspNetCore.App 8.0.x` داد، `dotnet --list-runtimes` را بررسی و **ASP.NET Core Runtime 8 x64** (یا SDK ۸ که آن را شامل می‌شود) نصب کنید. برای رفع خطای انتخاب SDK، دیتابیس و تنظیمات آن را تغییر ندهید.

برای مشاهدهٔ مسیرهای واقعی فایل‌های موقت در نسخه‌های تست‌شده:

```powershell
dotnet msbuild .\src\Inventory.Client\Inventory.Client.csproj -getProperty:BaseIntermediateOutputPath,MSBuildProjectExtensionsPath,IntermediateOutputPath
```

در صورت به‌روزرسانی روی یک کپی قدیمی، اول IDE را ببندید؛ فقط `bin` و `obj` تولیدیِ خود پروژهٔ کلاینت را در صورت نیاز پاک کنید، نه سورس، تنظیمات اتصال، آپلودها یا دیتابیس را. پوشهٔ کش NuGet را پاک نکنید.
