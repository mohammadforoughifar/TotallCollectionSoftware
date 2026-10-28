# ریشه‌یابی «چاپ نامهٔ صادره درست انجام نمی‌شود» — مسیر `Report/outgoingletter`

تاریخ بررسی: ۱۴۰۵/۰۸/۰۶ (2026-10-28) — دامنه: نامهٔ صادره (`OutgoingLetters`) روی سربرگ شرکت، A4 و A5.

## ۱) پاسخ کوتاه: مشکل خودِ `Report/outgoingletter` این است که «مسیر اجرا» نیست

هیچ‌جای کد این پوشه را نمی‌خواند. جست‌وجوی کل مخزن برای `Report/outgoingletter` در کد
(`*.cs`, `*.razor`, `*.json`, `*.csproj`) هیچ نتیجه‌ای ندارد؛ تنها ارجاع‌ها به مسیر دیگری است:

```text
inventory/src/Inventory.Api/Reports/OutgoingLetters/Companies/{CompanyId}/Outgoing-{A4|A5}.mrt
```

* این مسیر در `Inventory.Api.csproj` با `Content Include="Reports/**/*.mrt"` کنار خروجی
  Build/Publish کپی می‌شود و `FindCompanyTemplate` آن را از ContentRoot / BaseDirectory /
  پوشهٔ جاری می‌جوید (ریشه از `Reporting:Stimulsoft:TemplateRoot`).
* پس هر ویرایش در `Report/outgoingletter/**` (طرح MRT، تصویر سربرگ، فونت) **بی‌اثر** است.
* `Report/outgoingletter/{3,1003}/index.html` هم پیش‌نمایش مرورگری است، نه خروجی چاپ؛ سه لینک
  PDF آن (`Report_A5_OutgoingLetter_Preview.pdf`، `Report_A4_OutgoingLetter_Preview.pdf`،
  `Preview_OutgoingLetter_Company*_A5_A4.pdf`) در مخزن وجود ندارند و ۴۰۴ می‌دهند.

## ۲) حتی با ویرایش مسیر درست، بخشی از MRT اصلاً اعمال نمی‌شود

سرور قالب Stimulsoft را «اجرا» نمی‌کند؛ چاپ با QuestPDF ساخته می‌شود. از هر MRT فقط این‌ها خوانده
می‌شد: `Watermark/ImageBytes` (تصویر سربرگ). کادرهای متنی قالب — `GirandeAsli`، `TitleLetter`،
`TextLetter`، جای تصویر امضاها — در چاپ اعمال نمی‌شدند و چیدمان متن/موضوع/گیرنده/امضا را کد
`OutgoingLetterPrintService` می‌ساخت. بنابراین «جابه‌جا کردن کادرها در قالب» انتظار درستی نداشته.

## ۳) باگ واقعی چاپ: چیدمان فقط برای دو شناسه کالیبره شده بود

در `OutgoingLetterPrintService` (نسخهٔ `inventory/src`) شاخه‌های چیدمان چنین بودند:

```csharp
var isHomepaLetterhead = hasLetterhead && letterheadCompanyId == HomepaCompanyId;   // 1003
var isAriaLetterhead   = hasLetterhead && letterheadCompanyId == AriaCompanyId;     // 1005
```

هر شرکت دیگری به شاخهٔ عمومی می‌افتاد که مقدارها را **سفید** و با فاصله‌های ثابت می‌نوشت.
پیامد اندازه‌گیری‌شده روی تصویر سربرگ‌های موجود در مخزن:

| شرکت | طرح تصویر | رفتار قبلی کد | نتیجه |
|---|---|---|---|
| `1003` | هومپا (نوار آبی) | شاخهٔ کالیبره‌شدهٔ هومپا | درست |
| `3` | **همان طرح هومپا** (تصویر `homepa_company3_letterhead.png` بایت‌به‌بایت همان `…1003…png` است) | شاخهٔ عمومی: مقدار سفید، `PaddingTop 60` | مقدارها ~۲۱pt **پایین‌تر از برچسب‌ها** (برچسب‌های تصویر در ۶۲٫۶/۸۰٫۴/۹۹٫۴pt هستند و مقدارها روی ۸۴/۱۰۲/۱۲۰ می‌افتادند) و متن نامه از ۲۱۴pt شروع می‌شد در حالی که طرح تا ۲۳۲pt ادامه دارد؛ یعنی متن روی تزئینات پایین نوار |
| `1`, `2` | فروغ آریا (کاغذ سفید، برچسب‌های تیره) | شاخهٔ عمومی: مقدار **سفید** روی کاغذ سفید | شماره/تاریخ/پیوست عملاً نامرئی |
| `1005` | آریا همیار ژنراتور | شاخهٔ کالیبره‌شدهٔ آریا | درست |

قالب هر شرکت خودش دقیقاً گفته مقدارها کجا و با چه رنگی باشند:

```xml
<!-- Companies/3/Outgoing-A4.mrt (طرح هومپا) -->
<ClientRectangle>-0.19,0.45,1.14,0.2</ClientRectangle><TextBrush>White</TextBrush>   <!-- شماره -->
<!-- Companies/1/Outgoing-A4.mrt (طرح فروغ) -->
<ClientRectangle>0,0.3,0.9,0.2</ClientRectangle><TextBrush>Black</TextBrush>          <!-- شماره -->
```

کد این اطلاعات را نادیده می‌گرفت. ضمناً دو درخت کد موازی وجود دارد و اسکریپت‌های استقرار با هم
اختلاف دارند:

| اسکریپت | درختی که منتشر می‌کند |
|---|---|
| `publish-server.bat`، `deploy-single.ps1`، `rebuild.ps1` (ریشه) | `src/Inventory.Api` (نسل قدیمی چاپ) |
| `inventory/publish-server.bat`، `inventory/deploy-single.ps1`، `inventory/start.bat` | `inventory/src/Inventory.Api` (نسل جاری) |

## ۴) چه چیزی اصلاح شد

### ۴-۱. مقدارهای شماره/تاریخ/پیوست از قالب خود شرکت خوانده می‌شوند
فایل جدید: `Services/Office/Outgoing/LetterheadMetaFields.cs` (در هر دو درخت `inventory/src` و `src`)

* سه جزء متنی قالب را با عبارت‌های `{DataSource1.LetterNumber}`، `{DataSource1.DateShams}` و
  `{DataSource1.Peyvast}` پیدا می‌کند و از هر کدام می‌خواند: `ClientRectangle` (+ `Margins` صفحه
  و `ReportUnit`)، `TextBrush` (رنگ)، `Font` (اندازه و توپُر بودن) و `HorAlignment`.
* محاسبهٔ مختصات: `(Margin + ClientRectangle) × UnitToPoints` — همان قراردادی که مختصات
  کالیبره‌شدهٔ هومپا/آریا از آن ساخته شده بود. برای هومپا خروجی این محاسبه **عیناً** همان اعداد
  `HomepaLetterheadLayout` است (۱۴٫۴/۶۰٫۴۸ …)، پس رفتار ۱۰۰۳ تغییر نمی‌کند.
* اگر قالب موجود نباشد یا این سه جزء را اعلام نکند، `null` برمی‌گردد و چیدمان قبلی دست‌نخورده
  اجرا می‌شود (بدون رگرسیون برای شرکت‌هایی که فقط فایل سربرگ ثبت‌شده دارند).

### ۴-۲. شاخهٔ عمومی هدر از همین اعلان‌ها استفاده می‌کند
در `BuildContentPdf`، شاخهٔ `else if (hasLetterhead)`:

```csharp
var declared = ReadDeclaredMetaFields(FindCompanyTemplate(...));
if (declared != null) { /* AddDeclaredField: مستطیل/رنگ/فونت/تراز از قالب */ }
else { /* چیدمان قبلی، بدون تغییر */ }
```

`pageTopMargin` هم به‌صورت متغیر مشترک با `page.MarginTop` محاسبه می‌شود، چون `page.Header()`
بعد از حاشیهٔ بالا شروع می‌شود و مختصات قالب «مطلق» است. مقادیر منفی (مستطیلی که کمی بالاتر از
حاشیه است، مثل تاریخ در قالب شرکت ۱) با `Math.Max(0, …)` مهار می‌شوند تا QuestPDF خطا ندهد.
نتیجهٔ عملی: `1`, `2` سیاه و روی خط‌چین خودشان، `1005` و `1003` مثل قبل.

### ۴-۳. شناسهٔ هومپا فقط ۱۰۰۳ نیست
```csharp
private static readonly int[] DefaultHomepaCompanyIds = { 3, HomepaCompanyId };
private static readonly int[] DefaultAriaCompanyIds   = { AriaCompanyId };
```
و قابل تنظیم بدون بیلد، در `appsettings.json`:

```json
"Reporting": {
  "OutgoingLetters": {
    "HomepaCompanyIds": "3,1003",
    "AriaCompanyIds": "1005"
  }
}
```

چون `Reports/OutgoingLetters/Companies/3` و `/1003` هر دو همان تصویر هومپا را دارند، شرکت ۳ با
همان چیدمان کالیبره‌شده (مقدار سفید روی نوار، فاصلهٔ ۸۰/۲۸ برای شروع متن، رزرو پانوشت، شمارهٔ
صفحه، پس‌زمینهٔ شفاف) چاپ می‌شود.

### ۴-۴. پوشهٔ `Report/outgoingletter`
* `Report/outgoingletter/README-FA.md` اضافه شد: این پوشه بستهٔ طراحی/پیش‌نمایش است، مسیر اجرا
  کجاست، هر شناسه چه طرحی دارد و مراحل اعمال یک تغییر طراحی چیست.
* لینک‌های PDF شکسته از `index.html` حذف و به یک یادداشت جایگزین شد؛ عنوان صفحهٔ ۱۰۰۳ هم که
  اشتباهاً «قالب‌های شرکت ۳» بود اصلاح شد.
* خود فایل‌های MRT و تصویرهای این پوشه دست‌نخورده‌اند.

نکته: `Report/outgoingletter/1003/*.mrt` با نسخهٔ **قدیمی‌تر** `src/Inventory.Api/Reports/.../1003`
یکی است (۴۶۶٬۶۴۵ بایت تصویر) و با `inventory/.../1003` (۴۷۳٬۴۹۳ بایت) یکی نیست؛
`Report/outgoingletter/3/*` برابر `inventory/.../3` است. یعنی این پوشه آینهٔ دو درخت مختلف است.

### ۴-۵. ریشهٔ سوم (مهم‌ترین علت «طرح شرکت دیگر»): شناسهٔ عددی شرکت

قالب با شمارهٔ پوشه پیدا می‌شود (`Companies/{CompanyId}`) و این شماره بین نصب‌ها یکی نیست.
`SystemCompanies` شناسه را خودکار می‌سازد، پس در نصب تازه شرکت‌ها `1`,`2`,`3` می‌شوند و پوشه‌های
موجود این طرح‌ها را دارند:

| پوشه | طرح داخل تصویر |
|---|---|
| `1`, `2` | فروغ آریا |
| `3` | هومپا |
| `1003` | هومپا (نسخهٔ دیگر تصویر، همان جای برچسب) |
| `1005` | آریا همیار ژنراتور |

یعنی اگر شرکتِ واقعیِ شما هومپا باشد ولی شناسه‌اش `1` باشد، نامه روی **سربرگ فروغ آریا** چاپ
می‌شود و هیچ خطایی هم دیده نمی‌شود؛ و اگر شناسه `4` یا `6` باشد، چاپ با «سربرگ پیدا نشد» متوقف
می‌شود. آزمون سریع (`simulated search order`) روی هر شناسه این را نشان می‌دهد؛ برای `999` هیچ
قالبی پیدا نمی‌شود.

### اصلاح
* `FindCompanyTemplate` اکنون اول «طرح نگاشت‌شده» و بعد پوشهٔ هم‌نام خود شرکت را می‌جوید:

```csharp
foreach (var designId in DesignCompanyIds(companyId)) { ... }
```

* نگاشت در `appsettings.json` و بدون بیلد:

```json
"Reporting": { "OutgoingLetters": { "CompanyTemplateMap": { "4": "1003" } } }
```

* انتخاب چیدمان (ارتفاع هدر، شروع بدنه، رزرو پانوشت، رنگ/جای مقدارها) هم از **طرح** پیروی می‌کند،
  نه شناسهٔ خام: `designCompanyId = TemplateDesignId(letterheadCompanyId)`. پس یک نگاشت، تمام
  رفتار چاپ را یک‌جا درست می‌کند. بدون نگاشت، رفتار دقیقاً مثل قبل است.
* هنگام استفاده از نگاشت، یک خط `LogInformation` مسیر و طرح انتخابی را ثبت می‌کند.

### ۴-۶. ابزار همگام‌سازی برای خودِ پوشهٔ `Report/outgoingletter`

`Report/outgoingletter/sync-to-api-templates.bat` فایل‌های `Report_{A4,A5}_OutgoingLetter.mrt`
را برای شرکت‌های `3` و `1003` به مسیر واقعی چاپ در هر دو درخت کد کپی می‌کند
(`sync-to-api-templates.bat inventory|src` برای یکی از دو درخت). قبلاً برای اعمال یک تغییر طراحی
باید مسیرها را دستی می‌شناختید؛ اکنون یک فرمان کافی است.

## ۵) تست‌ها و بررسی‌هایی که انجام شد

```bash
python3 -m unittest discover -s inventory/tests/OutgoingLetterhead -p 'test_*.py' -v   # ۳۴ تست، OK
```

تست‌های تازه (در همان فایل) این‌ها را قفل می‌کنند:

* هر ده قالب (`1`,`2`,`3`,`1003`,`1005` × `A4`,`A5`) هر سه مقدار را اعلام می‌کنند.
* رنگ اعلام‌شده با طرح تصویر هم‌خوان است: هومپا (`3`,`1003`) سفید، فروغ/آریا (`1`,`2`,`1005`) سیاه.
* هندسهٔ شرکت `3` و `1003` یکسان است.
* اعداد `HomepaLetterheadLayout` با اعلام‌های MRT قابل‌قبول یکسان می‌مانند (جلوگیری از انحراف).
* رندر مقادیر از قالب (و نه حدس سفید) در سورس سرویس وصل شده است.
* چیدمان از «طرح» پیروی می‌کند و جست‌وجوی قالب، اول پوشهٔ نگاشت‌شده را می‌بیند.
* `CompanyTemplateMap` از `appsettings.json` خوانده می‌شود.
* هر پوشهٔ قالب هر دو سایز را دارد، و اسکریپت/فایل‌های همگام‌سازی پوشهٔ `Report/outgoingletter` موجودند.

تصویر سربرگ‌های همهٔ شرکت‌ها جداگانه decode و پیکسل‌به‌پیکسل اندازه‌گیری شد (جای برچسب‌ها و
رنگ متن) تا اعداد بالا از روی تصویر واقعی به دست بیاید، نه از حافظه.

**محدودیت این محیط:** SDK دات‌نت و بسته‌های NuGet (QuestPDF/PDFsharp/Stimulsoft/EF Core) در
این سندباکس موجود نیست، بنابراین `dotnet build` اجرا نشد. تغییرها به‌صورت گرامری با پارسر C#
بررسی شد (بدون خطای نحوی) و منطق خواندن MRT با یک بازسازی مستقل در پایتون روی همان فایل‌ها
آزموده شد؛ اما کامپایل و چاپ واقعی باید روی ماشین شما انجام شود:

```powershell
dotnet build inventory/Inventory.sln -c Debug
dotnet build Inventory.sln -c Debug            # اگر از درخت ریشه استفاده می‌کنید
```

## ۶) اگر بعد از این تغییرها هنوز چاپ درست نبود، به ترتیب این‌ها را ببینید

0. **شناسهٔ شرکت را با پوشهٔ قالب مقایسه کنید** (پیش از هر چیز):
   ```sql
   SELECT Id, Name, IsActive FROM SystemCompanies ORDER BY Id;
   ```
   اگر شناسه در `{1,2,3,1003,1005}` نیست، یا طرحش با طرح شرکت شما فرق دارد، در
   `Reporting:OutgoingLetters:CompanyTemplateMap` نگاشتش کنید (مثال: `"4": "1003"`).
1. **کدام درخت ساخته/منتشر شده؟** اگر با `publish-server.bat` ریشه منتشر می‌کنید، `src/` را
   منتشر می‌کنید. هر دو درخت اصلاح شده‌اند، ولی فایل‌های منتشرشده روی سرور باید تازه باشند.
2. **لاگ چاپ** (`OutgoingLetterPrintService`) را بخوانید:
   `LetterCompanyId`، `SelectedCompanyId`، `RegisteredPath`، `TemplatePath`، `FinalPath`،
   `FromTemplate` و خط «مقادیر شماره/تاریخ/پیوست از قالب شرکت خوانده شد».
   اگر `FinalPath` روی `wwwroot/uploads/letterheads/...` است، **فایل ثبت‌شده بر قالب MRT اولویت
   دارد**؛ برای استفاده از طرح داخل MRT باید `SystemCompanies.LetterheadFileName` خالی شود.
3. **قالب روی سرور هست؟** پوشهٔ `Reports/OutgoingLetters/Companies/{id}` باید کنار فایل اجرایی
   API باشد (csproj آن را کپی می‌کند؛ انتقال دستی فایل‌ها کافی نیست).
4. **کش:** تصویر سربرگ در `%TEMP%/totall-letterheads` و اعلان‌های مقدارها در حافظه تا ۱ ساعت کش
   می‌شوند؛ کلید کش شامل طول و زمان تغییر فایل است، پس جایگزینی فایل کافی است، ولی اگر همان فایل
   با همان تاریخ روی سرور مانده باشد، یک بار لمسی/جایگزینی لازم است.
5. **گیت‌های چاپ** (اگر پیام خطا می‌گیرید، نه خروجی غلط): مجوز دبیرخانه/ایجادکننده/دریافت‌کننده،
   کامل بودن امضاها، و وجود `Resources/fonts/Vazirmatn-*.ttf` کنار API.

## ۷) پیشنهادهای بعدی (انجام نشده)

* یکی از دو درخت کد (`src/` یا `inventory/src/`) حذف/آرشیو شود تا اسکریپت‌های استقرار نتوانند
  نسخهٔ قدیمی را منتشر کنند.
* چیدمان متن/موضوع/گیرنده هم از قالب خوانده شود (همان کاری که برای سه مقدار انجام شد) تا کل طرح
  MRT مرجع چاپ باشد.
* اگر می‌خواهید `Report/outgoingletter` واقعاً مرجع باشد، `Reporting:Stimulsoft:TemplateRoot` را
  به مسیر مطلق همان پوشه تنظیم کنید (یا فایل‌ها را به مسیر اجرا کپی کنید) — با یک مرجع، نه دو.
