# رفع خطای بیلد: «Failed to convert … to webcil»

## متن خطا

```
Failed to convert '…\microsoft.aspnetcore.components.webassembly\8.0.30\lib\net8.0\
Microsoft.AspNetCore.Components.WebAssembly.dll' to webcil:
Could not find a part of the path
'…\inventory\src\Inventory.Client\obj\Debug\net8.0\tmp-webcil\a38fd86c…\Microsoft.AspNetCore.Components.WebAssembly.wasm'.
```

این خطا از `Microsoft.NET.Sdk.WebAssembly.Browser.targets` نسخهٔ **10.0.3** می‌آید.

## علت

1. پروژه `net8.0` است، ولی ابزارِ WebAssembly که بیلد را انجام می‌دهد از **SDK نسخهٔ ۱۰** آمده است
   (`C:\Users\…\.nuget\packages\microsoft.net.sdk.webassembly.pack\10.0.3\…`).
2. فایل `global.json` ریشه با `"rollForward": "latestMajor"` اجازه می‌دهد SDK جدیدتر (۱۰) برای این پروژه
   انتخاب شود؛ در نتیجه مرحلهٔ تبدیل اسمبلی‌ها به فرمت **webcil** با نسخهٔ net8.0 اسمبلی‌ها سازگار نیست و
   فایل موقت `tmp-webcil\*.wasm` ساخته نمی‌شود.
3. مسیر پروژه هم پرانتز و فاصله دارد (`…\Downloads\TotallCollectionSoftware-main (14)\…`) که این نوع
   مسیرهای موقت را حساس‌تر می‌کند.

## رفع انجام‌شده (در مخزن)

در `inventory/src/Inventory.Client/Inventory.Client.csproj` این ویژگی اضافه شد:

```xml
<WasmEnableWebcil>false</WasmEnableWebcil>
```

با این کار مرحلهٔ تبدیل به webcil انجام نمی‌شود و اسمبلی‌ها مثل گذشته به شکل `.dll` در خروجی می‌مانند.
رفتار برنامه تغییری نمی‌کند و این تنظیم روی SDK 8 و 10 هر دو معتبر است.

## پس از گرفتن تغییرات

```bat
cd inventory\src\Inventory.Client
rmdir /s /q obj
rmdir /s /q bin
dotnet build
```

(پاک‌کردن `obj` لازم است، چون خروجی نیمه‌کارهٔ `tmp-webcil` در آن مانده است.)

## اگر باز هم خطا داد

به ترتیب این گزینه‌ها را امتحان کنید:

**۱) پین‌کردن SDK ۸** — اگر .NET 8 SDK روی سیستم نصب است، در `global.json` ریشه مقدار
`rollForward` را از `latestMajor` به `latestFeature` تغییر دهید:

```json
{
  "sdk": { "version": "8.0.100", "rollForward": "latestFeature", "allowPrerelease": false }
}
```

**۲) مسیر بدون پرانتز** — پروژه را از `…\Downloads\TotallCollectionSoftware-main (14)\…` به مسیری ساده
مثل `C:\src\TotallCollectionSoftware` منتقل کنید (پرانتز و فاصله در مسیر، بیلد ابزارهای wasm را حساس می‌کند).

**۳) نصب دات‌نت ۸** — اگر فقط SDK ۱۰ نصب است، .NET 8 SDK (نسخهٔ 8.0.4xx) را نصب کنید؛ بیلد پروژه‌های
net8.0 با ابزار خودشان همیشه امن‌ترین حالت است.

**۴) حذف کش NuGet ابزار** (نادر): پوشهٔ `%USERPROFILE%\.nuget\packages\microsoft.net.sdk.webassembly.pack`
را پاک کنید تا نسخهٔ سازگار دوباره بازیابی شود.
