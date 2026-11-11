# نامهٔ داخلی ۱۰۰۳: متن انگلیسی و صفحه‌های خالی در چاپ

> **وضعیت:** اصلاح در کد اعمال شده است. **کامپایل نشده** (سندباکس `dotnet` ندارد)؛ پیش از
> استفاده `dotnet build` و یک چاپ آزمایشی لازم است. تست‌های پایتون این تغییر (۳۱ تست در
> `inventory/tests/InnerLetterPrinting`) سبز هستند.

---

## ۱. گزارش

خروجی چاپ نامهٔ داخلی ۱۰۰۳ (A5، بدنهٔ «گزارش کار ۱۴۰۵.۰۸.۰۶») که کارفرما فرستاد:
**۲۲ صفحه**، در حالی که نامه دو صفحه متن دارد. ایرادها:

- در چند صفحه متن انگلیسی: `Normal`، `0`، `false`، `EN-US`، `X-NONE`، `AR-SA`،
  `/* Style Definitions */`، `table.MsoNormalTable`، `{mso-style-name:"Table Normal";…}`.
- چند صفحهٔ **خالی** فقط با سربرگ و شماره/تاریخ/پیوست.

## ۲. بررسی خودِ فایل PDF

| صفحه | مشاهده |
| --- | --- |
| ۱ | متن نامه تا «بدین‌وسیله گزارش کارهای انجام‌شده …» و ته آن `Normal` / `0` / `false` |
| ۲ | ادامه: `false` / `false` / `EN-US` / `X-NONE` / `AR-SA` |
| ۳ تا ۱۹ | **هیچ متنی**؛ استریم محتوا در همه بایت‌به‌بایت یکسان (`md5 a3f6ef6083`)، فقط `Tj=15` (سه مقدار سفید سربرگ) |
| ۲۰ | `/* Style Definitions */`، `table.MsoNormalTable`، `{mso-style-name:"Table Normal";` |
| ۲۱ | `mso-style-parent`، `mso-padding-alt`، `line-height:115%` … |
| ۲۲ | متن واقعی نامه («طی جلسه امروز، طراحی نرم‌افزار سامانه مودیان …») |

یعنی بدنهٔ نامه در واقع از **صفحهٔ ۲۲** شروع می‌شود؛ ۲۰ صفحهٔ اول «سرِ سند» و فاصله‌های آن است.

## ۳. علت

بدنهٔ ذخیره‌شدهٔ نامه (`OutgoingLetters.Text`) یک **سند کامل HTML** است — خروجی Word/Outlook:

```html
<html xmlns:v=… xmlns:o=… xmlns:w=…>
<head>
  <meta name=Generator content="Microsoft Word 15 (filtered medium)">
  <!--[if gte mso 9]><xml><w:WordDocument><w:View>Normal</w:View><w:Zoom>0</w:Zoom>…</xml><![endif]-->
  <style><!-- p.MsoNormal {mso-style-name:"Normal"; mso-fareast-language:EN-US;} --></style>
</head>
<body><div class=WordSection1><p class=MsoNormal dir=RTL><span lang=FA>سلام<o:p></o:p></span></p>…
```

و تابع متنی‌سازیِ چاپ فقط تگ‌ها را حذف می‌کرد:

```csharp
// InnerLetterPdfRenderer.PlainText — قبل
var text = Regex.Replace(html, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
text = Regex.Replace(text, @"</\s*(p|div|li|h[1-6]|tr)\s*>", "\n", RegexOptions.IgnoreCase);
text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", ""));
return Regex.Replace(text, @"[^\S\r\n]+", " ").Trim();   // ← \r\n دست‌نخورده می‌ماند
```

دو نشتی، هر دو از همین‌جا:

1. **متن انگلیسی:** محتوای `<head>`، `<style>` و `<xml>` «تگ» نیست، **متن** است. با حذف تگ‌ها،
   همان `Normal` / `EN-US` / `mso-*` به‌عنوان متن نامه چاپ می‌شود. (کامنت شرطی
   `<!--[if gte mso 9]>…<![endif]-->` هم حذف نمی‌شد.)
2. **صفحه‌های خالی:** الگوی `[^\S\r\n]+` فقط فاصله/تب را جمع می‌کند و `\r\n` را نگه می‌دارد؛
   پس هر خطِ سورسِ HTML (هر بلوک CSS یک خط، با تودرتویی و تب) یک **خط چاپی** می‌شود. آن همه
   خط خالی در QuestPDF هر کدام یک line box است: صفحه‌بندی پیش می‌رود ولی چیزی روی کاغذ نمی‌آید.

چرا نامهٔ صادره سالم است: آن مسیر بدنه را از `RichLetterHtml.Sanitize`
(`src/Inventory.Shared/Security/RichLetterHtml.cs`) رد می‌کند که `style`/`script`/کامنت‌ها را
**با محتوا** حذف می‌کند. مسیر نامهٔ داخلی هیچ sanitize نداشت.

## ۴. اصلاح

فایل تازه: `src/Inventory.Api/Services/Office/OfficeHtmlText.cs` (و کپی یکسان در
`inventory/src/…`) با ترتیب:

1. `RichLetterHtml.Sanitize` (حذف script/style/iframe و برگرداندن متنِ escape‌شده)
2. حذف کامنت‌ها با محتوایشان — شامل کامنت شرطی Word
3. حذف بلوک‌های `head | style | script | title | xml | svg | math | object | iframe | form | select | button | textarea | video | audio` **با محتوایشان**
4. `br` و پایان `p/div/li/ul/ol/h*/tr/table/blockquote/…` → نشانهٔ موقت شکست خط
   (پایان `td/th` فقط فاصله؛ ردیف‌های جدول به هم نمی‌چسبند)
5. حذف بقیهٔ تگ‌ها + `HtmlDecode` دوبار (متنِ دوبار-encode)
6. **جمع‌کردن همهٔ فضای سفید سورس** + حذف رشته‌های چسبیدهٔ ماشینی (base64/دیتا-URI، ۱۸۰+ کاراکتر)
7. نشانه → خط جدید؛ `Trim` هر خط و حداکثر یک خط خالی

نکتهٔ ریز: ورودیِ **بدون تگ** (مثل متن ارجاع/هامش) «متن ساده» شناخته می‌شود و شکست‌خط‌های
خودش حفظ می‌شود؛ فقط سند HTML است که شکست‌خط‌های سورسش دور ریخته می‌شود.

`InnerLetterPdfRenderer.PlainText` حالا فقط:

```csharp
internal static string PlainText(string? html) => OfficeHtmlText.ToPlainText(html);
```

همین تابع هم برای بدنهٔ نامه و هم برای ردیف‌های هامش استفاده می‌شود.

### نتیجه روی نمونهٔ واقعی

```
قبل: ۴۱ خط، ۲۸ خط خالی، شامل "Normal" و "behavior:url"
بعد: ۵ خط، ۰ خط خالی، بدون هیچ‌کدام از mso-/EN-US/Style Definitions
```

## ۵. تست‌ها

`inventory/tests/InnerLetterPrinting/test_inner_printing.py` — ۳۱ تست (۶ تست تازه):

- الگوهای `private static readonly Regex` **از خودِ `OfficeHtmlText.cs`** خوانده می‌شوند و همان
  زنجیره روی یک بدنهٔ کامل Word اجرا می‌شود؛ اگر الگوها عوض شوند، تست همان‌ها را می‌سنجد.
- قرارداد: sanitize قبل از شکست‌های ساختاری؛ جمع‌شدن `\s+` (نه `[^\S\r\n]+`)؛ نبود رشتهٔ قدیمی
  در `PlainText`؛ یکسان بودن فایل در دو درخت.
- خروجی: فقط متن نامه؛ صفر خط خالی؛ هیچ‌کدام از `mso-`/`Normal`/`EN-US`/`Style Definitions`.
- رگرسیون: الگوریتم قبلی روی همان بدنه «Normal» و «behavior:url» و ۱۰+ خط خالی می‌سازد.
- حفظ رفتار: پاراگراف/`<br>`، موجودیت‌ها، نیم‌فاصله، جدول، متن سادهٔ چندخطی، ورودی خالی.

اجرا:

```bash
python3 inventory/tests/InnerLetterPrinting/test_inner_printing.py -v
dotnet run --project inventory/tests/InnerLetterPrinting/InnerLetterPrinting.csproj  # روی سیستم دارای .NET 8
```

## ۶. کاری که در این اصلاح انجام نشد

- **چیدمان/قالب دست نخورد** (سربرگ، شماره/تاریخ/پیوست، شروع متن) — همان تأییدهای قبلی.
- بدنهٔ ذخیره‌شده در دیتابیس **تغییر نمی‌کند**؛ فقط در زمان چاپ تمیز می‌شود. (اگر بخواهید، یک
  ابزار یک‌باره می‌تواند بدنه‌های آلودهٔ موجود را هم بازنویسی کند.)
- `dotnet build` و چاپ واقعی در این محیط انجام نشد.
