# رفع خطای بیلد ویژوال استودیو — «Could not find a part of the path» (MAX_PATH ویندوز) — ۱۴۰۵/۰۷/۲۹ (2026-10-21)

## علامت
باز کردن راه‌حل (`inventory/Inventory.sln`) و بیلد اولیه در ویژوال استودیو ۲۰۲۲، سه خطای
زیر را می‌دهد:

```
Could not find a part of the path
'C:\Users\...\Downloads\TotallCollectionSoftware-arena-...\TotallCollectionSoftware-arena-...\inventory\src\Agent.Console\obj\Debug\net8.0\Agent.Console.GeneratedMSBuildEditorConfig.editorconfig'
```
(برای `Agent.Console`، `Inventory.Shared` و `Inventory.Client`)

## ریشهٔ مشکل — طول مسیر بیشتر از سقف ۲۶۰ کاراکتری ویندوز
این خطا ربطی به تغییرات اخیر (سربرگ نامه صادره) ندارد؛ مشکل **طول مسیر** است:

- مخزن از آرشیو ZIP برنچ در `Downloads` استخراج شده و چون GitHub خودش یک پوشهٔ هم‌نام
  داخل ZIP می‌گذارد، مسیر پایه دوبرابر شده است:
  `...\Downloads\TotallCollectionSoftware-arena-3ce0c943-totallcollectionsoftware\TotallCollectionSoftware-arena-3ce0c943-totallcollectionsoftware\`
  = **۱۶۰ کاراکتر** فقط برای پوشهٔ پایه!
- سقف مجاز مسیر در ویندوز (بدون فعال‌سازی LongPathsEnabled) **۲۵۹/۲۶۰ کاراکتر** است.
- طول دقیق مسیرهای خطادار: `Agent.Console` = **۲۶۱**، `Inventory.Shared` = **۲۶۷**،
  `Inventory.Client` = **۲۶۷** کاراکتر — همه بالای سقف؛ به همین دلیل MSBuild نمی‌تواند
  پوشهٔ `obj\Debug\net8.0` را بسازد و پیام «Could not find a part of the path» می‌دهد.
- هر پروژهٔ دیگری هم که بعداً به `obj` یا `bin` چیزی بنویسد (مثل کپی قالب‌های MRT و
  سربرگ‌ها در `Inventory.Api`) در همین شرایط شکست می‌خورد.

## اصلاح (در سورس مخزن)
فایل `Directory.Build.props` از `inventory/` به **ریشهٔ مخزن** منتقل شد و این تنظیم به آن
اضافه شد:

```xml
<BaseIntermediateOutputPath Condition="'$(BaseIntermediateOutputPath)' == ''">$(MSBuildThisFileDirectory).o\$(MSBuildProjectName)\</BaseIntermediateOutputPath>
<BaseOutputPath        Condition="'$(BaseOutputPath)' == ''">$(MSBuildThisFileDirectory).b\$(MSBuildProjectName)\</BaseOutputPath>
```

یعنی پوشه‌های `obj` و `bin` همهٔ پروژه‌ها به دو پوشهٔ کوتاه **`.o`** و **`.b`** در ریشهٔ
مخزن منتقل می‌شود (با زیرپوشهٔ نام هر پروژه تا هیچ دو پروژه‌ای مشترک نداشته باشند).

### نتیجه
| سناریو | طول مسیر قبل | بعد |
|---|---|---|
| `Agent.Console` (obj editorconfig) | ۲۶۱ ✗ | ۲۴۶ ✓ |
| `Inventory.Shared` (obj editorconfig) | ۲۶۷ ✗ | ۲۵۲ ✓ |
| `Inventory.Client` (obj editorconfig) | ۲۶۷ ✗ | ۲۵۲ ✓ |
| کپی قالب MRT در خروجی Api | ۲۶۱ ✗ | ۲۴۵ ✓ |
| کپی سربرگ در خروجی Api | ۲۶۷ ✗ | ۲۵۲ ✓ |

حتی در بدترین محل ممکن (همان پوشهٔ دوبل Downloads) طولانی‌ترین مسیر بیلد ~۲۵۶ کاراکتر
می‌ماند و بیلد کامل (بازیابی NuGet، بیلد، تست، پابلیش) درست کار می‌کند.

### سازگاری
- تنظیمات قبلی `inventory/Directory.Build.props` (زبان C# 12، `ProduceReferenceAssembly=false`
  برای رفع CS0006، بدون PDB در Release) عیناً حفظ شده — فقط محل فایل به ریشه منتقل شده
  چون MSBuild فقط **نزدیک‌ترین** `Directory.Build.props` را می‌خواند و اگر در inventory/ می‌ماند
  تنظیم ریشه‌ای نادیده گرفته می‌شد.
- `inventory/Directory.Build.targets` (رفع CS0006 اسمبلی ref) سر جای خودش مانده و
  چون از `$(IntermediateOutputPath)` استفاده می‌کند، با مسیر جدید هم درست کار می‌کند.
- `inventory/NuGet.config` و `inventory/Inventory.sln` بدون تغییر.
- اسکریپت‌های استقرار (`publish-win-server.bat`، `run-server.bat`، `start.bat`، `stop.bat`)
  خروجی پابلیش را با `-o` صریح می‌گیرند و به `bin\Debug/Release` ارجاعی ندارند — بدون تغییر.
- `.gitignore` با `.o/` و `.b/` بروزرسانی شد.

## نکات برای سیستم شما
1. **بعد از گرفتن این نسخه، یک‌بار Clean کامل کنید**: پوشه‌های `obj` و `bin` قدیمی داخل
   `inventory/src/*` و `inventory/tests/*` را (در صورت وجود) حذف کنید تا VS از کش قدیمی
   استفاده نکند.
2. بهتر است مخزن را با **`git clone`** بگیرید نه ZIP، و در مسیر کوتاه مثل `C:\TCS` قرار دهید.
   اگر ZIP گرفتید، فقط پوشهٔ داخل آن را استخراج کنید (نه پوشه داخل پوشه).
3. راه‌حل قطعی سیستمی (اختیاری): فعال‌سازی مسیرهای طولانی در ویندوز —
   در رجیستری `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem` مقدار
   `LongPathsEnabled` را `1` کنید (سپس ویندوز را ری‌استارت کنید).
