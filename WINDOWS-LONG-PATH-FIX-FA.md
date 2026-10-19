# رفع خطاهای بیلد ویندوز — محدودیت MAX_PATH (۲۶۰ کاراکتر)

## خلاصه

دو خطای زیر **یک علت مشترک** دارند و هیچ‌کدام به کد پروژه مربوط نیستند:

```
MSB4018  The "GenerateStaticWebAssetsPropsFile" task failed unexpectedly.
System.IO.DirectoryNotFoundException: Could not find a part of the path
  '...\inventory\src\Inventory.Api\obj\Debug\net8.0\staticwebassets\msbuild.Inventory.Api.Microsoft.AspNetCore.StaticWebAssets.props'

Failed to convert '...Microsoft.Extensions.DependencyInjection.Abstractions.dll' to webcil:
Could not find a part of the path
  '...\inventory\src\Inventory.Client\obj\Debug\net8.0\tmp-webcil\Microsoft.Extensions.DependencyInjection.Abstractions.wasm'
```

علت: ویندوز به‌صورت پیش‌فرض مسیرهای بلندتر از **۲۶۰ کاراکتر** را نمی‌پذیرد.
وقتی فایل‌های ساخته‌شدهٔ SDK داخل `obj` از این حد عبور کنند، `Directory.CreateDirectory`
و `FileStream` با `ERROR_PATH_NOT_FOUND` برمی‌گردند که در دات‌نت به
`DirectoryNotFoundException` ترجمه می‌شود. بنابراین MSBuild به‌جای خطای «مسیر طولانی»،
خطای «پوشه پیدا نشد» می‌دهد و تشخیص را گمراه‌کننده می‌کند.

## اندازه‌گیری مسیر شما

با مسیر

```
D:\mainProject\TotallCollectionSoftware-arena-72ffde21-totallcollectionsoftware\
TotallCollectionSoftware-arena-72ffde21-totallcollectionsoftware\
```

نام پوشهٔ تکراری ۶۴ کاراکتری، طول پایه را به **۱۴۴ کاراکتر** می‌رساند:

| فایل تولیدی SDK | طول مسیر | وضعیت |
|---|---:|---|
| `Inventory.Api\obj\Debug\net8.0\staticwebassets\msbuild.Inventory.Api.Microsoft.AspNetCore.StaticWebAssets.props` | **۲۷۰** | ❌ بیش از ۲۶۰ |
| `Inventory.Client\obj\Debug\net8.0\tmp-webcil\Microsoft.Extensions.DependencyInjection.Abstractions.wasm` | **۲۶۲** | ❌ بیش از ۲۶۰ |
| پوشهٔ `Inventory.Api\obj\Debug\net8.0\staticwebassets` | ۲۰۵ | ✅ |

اگر همان مخزن در `D:\tcs` باشد، همین مسیرها به ۱۳۲ و ۱۲۴ کاراکتر کاهش می‌یابند.

> نکته: خطاهای Razor، `CS0006` یا `CS` اصلاً اینجا مطرح نیستند. تغییرات اخیر صفحهٔ
> جزئیات صورتحساب مودیان (Razor/CSS) هم بی‌ارتباط با این خطاست.

---

## راه‌حل ۱ (پیشنهادی و بدون دسترسی ادمین) — خروجی بیلد را به مسیر کوتاه ببرید

از نسخهٔ فعلی، اگر متغیر محیطی `TCS_BUILD_ROOT` تنظیم شود، پوشه‌های `obj`/`bin`
هر پروژه زیر همان مسیر کوتاه ساخته می‌شوند (بدون این متغیر، رفتار قبلی حفظ می‌شود).

```powershell
# فقط برای همین پنجرهٔ ترمینال
$env:TCS_BUILD_ROOT = 'D:\tcs-build'
dotnet build inventory\Inventory.sln -c Debug
```

برای اینکه دائمی شود (و Visual Studio هم ببیند):

```powershell
setx TCS_BUILD_ROOT D:\tcs-build
```

سپس **Visual Studio و ترمینال را ببندید و دوباره باز کنید** (متغیرهای محیطی فقط در
پروسه‌های جدید خوانده می‌شوند).

خروجی‌ها این‌جا ساخته می‌شوند:

```
D:\tcs-build\obj\Inventory.Api\Debug\net8.0\...
D:\tcs-build\bin\Inventory.Api\Debug\net8.0\...
```

> توجه: در این حالت مسیر `publish` هم زیر `D:\tcs-build\bin\...\publish` است، نه
> `inventory\src\...\bin`. اگر اسکریپت انتشار شما مسیر ثابت دارد، یا از راه‌حل ۲
> استفاده کنید یا مسیر publish را پارامتر بدهید:
> `dotnet publish ... -o D:\publish`

### اسکریپت کمکی

```powershell
# فقط تشخیص (هیچ تغییری نمی‌دهد)
powershell -ExecutionPolicy Bypass -File inventory\tools\fix-windows-long-path.ps1

# تنظیم مسیر کوتاه + دائمی‌کردن + پاک‌سازی bin/obj
powershell -ExecutionPolicy Bypass -File inventory\tools\fix-windows-long-path.ps1 `
    -BuildRoot D:\tcs-build -Persist -Clean
```

---

## راه‌حل ۲ (ریشه‌ای، نیازمند ادمین) — فعال‌کردن Long Paths ویندوز

```powershell
# PowerShell با دسترسی Administrator
reg add "HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" /v LongPathsEnabled /t REG_DWORD /d 1 /f
git config --system core.longpaths true
```

سپس **ویندوز را ری‌استارت کنید** (یا حداقل Visual Studio را ببندید و سیستم را
دوباره راه‌اندازی کنید) و پروژه را از نو Build کنید.

یا با اسکریپت:

```powershell
powershell -ExecutionPolicy Bypass -File inventory\tools\fix-windows-long-path.ps1 -EnableLongPaths
```

> VS 2022 و `dotnet` نسخهٔ ۸/۹ هر دو long-path aware هستند؛ ولی بدون این کلید
> رجیستری، APIهای ویندوز همچنان در ۲۶۰ کاراکتر شکست می‌خورند.

---

## راه‌حل ۳ (ساده‌ترین) — مخزن را به مسیر کوتاه منتقل کنید

```
D:\src\tcs\inventory\Inventory.sln
```

با این کار طول پایه از ۱۴۴ به ۹ کاراکتر می‌رسد و همهٔ مسیرهای `obj` با فاصلهٔ
زیاد زیر ۲۶۰ می‌مانند. در این حالت نیازی به هیچ متغیر یا تنظیم رجیستری نیست.
(نام پوشهٔ تکراری `TotallCollectionSoftware-arena-...` صرفاً نتیجهٔ استخراج/کلون
در پوشهٔ همنام است و برای Git اهمیتی ندارد.)

---

## پس از هر تغییر: پاک‌سازی و بیلد مجدد

```powershell
dotnet clean inventory\Inventory.sln
Get-ChildItem inventory\src -Recurse -Directory -Include bin,obj | Remove-Item -Recurse -Force
dotnet restore inventory\Inventory.sln
dotnet build inventory\Inventory.sln -c Debug
```

اسکریپت `rebuild.ps1` در پوشهٔ `inventory` هم همین کار را انجام می‌دهد.

---

## هشدار خودکار در بیلد

از این پس اگر مسیر مخزن از ۱۳۰ کاراکتر بیشتر باشد و `TCS_BUILD_ROOT` تنظیم نشده باشد،
MSBuild پیش از بیلد این هشدار را نشان می‌دهد تا علت واقعی سریع‌تر پیدا شود:

```
⚠ هشدار MAX_PATH: مسیر مخزن ۱۵۴ کاراکتر است (...)
```

این هشدار بیلد را متوقف نمی‌کند.

---

## چه چیزی در مخزن تغییر کرد

| فایل | تغییر |
|---|---|
| `inventory/Directory.Build.props` | پشتیبانی از `TCS_BUILD_ROOT` برای انتقال `obj`/`bin` به مسیر کوتاه (فقط در صورت تنظیم متغیر) |
| `inventory/Directory.Build.targets` | هشدار زودهنگام `TcsWarnLongRepoPath` هنگام طولانی‌بودن مسیر مخزن |
| `inventory/tools/fix-windows-long-path.ps1` | اسکریپت تشخیص/تعمیر ویندوز |
| `WINDOWS-LONG-PATH-FIX-FA.md` | همین راهنما |

هیچ تغییر رفتاری در بیلد معمولی (بدون `TCS_BUILD_ROOT`) وجود ندارد.
