# چاپ صورتجلسه روی سربرگ شرکت — قالب Stimulsoft (MRT)

> **وضعیت تأیید:** این تغییرات **کامپایل نشده‌اند** (سندباکس `dotnet` ندارد). پیش از اجرا
> `dotnet build` لازم است. پیش‌نمایش‌ها شبیه‌سازی دقیق هندسه با فونت و سربرگ واقعی‌اند، نه
> خروجی QuestPDF.

---

## ۱. خواسته

- صورتجلسه روی سربرگ **همان شرکتی** چاپ شود که صورتجلسه برایش ثبت شده است.
- **عنوان، شماره صورتجلسه، تاریخ جلسه، حاضرین، غایبین** همیشه روی کاغذ دیده شوند.
- قالب با **Stimulsoft** ساخته شود، همان‌طور که برای نامهٔ داخلی و نامهٔ صادره شد.
- فایل قالب در همان مسیر قالب‌های دیگر و **با شناسهٔ شرکت** قرار بگیرد:
  `inventory/src/Inventory.Api/Reports/MeetingMinutes/Companies/1003/`

## ۲. فایل‌های قالب

| مسیر | کاربرد |
| --- | --- |
| `inventory/src/Inventory.Api/Reports/MeetingMinutes/Companies/1003/Minutes-{A4,A5}.mrt` | مسیر خواسته‌شده (پروژهٔ `inventory`) |
| `src/Inventory.Api/Reports/MeetingMinutes/Companies/1003/Minutes-{A4,A5}.mrt` | همان فایل در درخت اصلی سرور (تست‌ها هر دو درخت را می‌سنجند) |
| `.../Companies/3/…` | شناسهٔ هومپا در **نصب نمونه** (همان طرح، همان سربرگ) |
| `Report/meetingminutes/{1003,3}/…` | نسخهٔ طراح، کنار `Report/outgoingletter/…` |

ساخت/بازسازی خودکار: `python3 tools/build_minutes_mrt.py`
(اسکریپت idempotent است؛ قالب نامهٔ صادرهٔ همان شرکت و همان اندازه را می‌گیرد، سربرگ
جاسازی‌شده و تنظیمات صفحه را نگه می‌دارد و فقط اجزا و `DataSource1` را می‌نویسد.)

### ساختار قالب

- `StiSerializer` + `EngineVersion EngineV2` + `ReportUnit Inches` (مثل قالب نامه‌ها).
- `Pages/Page1`: `PaperSize` = A4/A5، `Margins 0.39,0.39,0.39,0.39`.
- `<Watermark ImageBehind=True ImageStretch=True>` با `ImageBytes` = **سربرگ شرکت ۱۰۰۳**
  (همان تصویری که در قالب نامهٔ صادره هست؛ چاپ به فایل‌های `wwwroot` وابسته نیست).
- `Dictionary/DataSources/DataSource1` با ستون‌های:
  `Number, Title, MeetingDate, Attendees, Absentees, Items, CompanyName, Status`.
- اجزای صفحه (هر کدام کادر مستقل، فونت `Vazirmatn` / `Vazirmatn-Bold`، `RightToLeft=True`):

| جزء | عبارت | A4 (top/left/عرض/ارتفاع) | A5 |
| --- | --- | --- | --- |
| `TitleValue` | `{DataSource1.Title}` | ۲۰۹٫۸ / ۲۸٫۱ / ۵۲۹٫۲ / ۳۴ | ۱۷۶٫۴ / ۲۸٫۱ / ۳۴۵٫۶ / ۲۶ |
| `NumberValue` + `NumberCaption` | `{DataSource1.Number}` + «:شماره صورتجلسه» | ۲۴۹٫۸ / ۴۱۴٫۲+۱۱۰ | ۲۰۸٫۴ / ۲۵۵٫۶+۸۶ |
| `MeetingDateValue` + `MeetingDateCaption` | `{DataSource1.MeetingDate}` + «:تاریخ جلسه» | ۲۷۱٫۸ | ۲۲۶٫۴ |
| `AttendeesValue` + `AttendeesCaption` | `{DataSource1.Attendees}` + «:حاضرین» | ۲۹۳٫۸ / ارتفاع ۶۲ | ۲۴۴٫۴ / ۵۰ |
| `AbsenteesValue` + `AbsenteesCaption` | `{DataSource1.Absentees}` + «:غایبین» | ۳۶۳٫۸ / ۳۴ | ۳۰۲٫۴ / ۲۸ |
| `ItemsCaption` سپس `ItemsValue` | `{DataSource1.Items}` + «:بندها و تصمیمات» | ۴۰۵٫۸ سپس ۴۲۸٫۸ | ۳۳۸٫۴ سپس ۳۵۷٫۴ |

مختصات = point از گوشهٔ بالا-چپ کاغذ؛ نخستین کادر روی
`BandBottom + 97 = ۲۰۹٫۸ (A4) / ۱۷۶٫۴ (A5)` است، یعنی همان قاعدهٔ سربرگ هومپا که برای
نامهٔ صادره کالیبره شد (`HomepaLetterheadLayout.ContentStartGap`).

## ۳. جریان چاپ در کد

`Services/Office/MeetingMinutesPrintService.cs`

1. **شرکت از خودِ صورتجلسه:** `MeetingMinutes.CompanyId` مرجع است. فقط اگر خالی باشد
   (رکوردهای قدیمی) شرکت فعال در هدر `X-Company-Id` جایگزین می‌شود. بنابراین صورتجلسهٔ
   شرکت ۱۰۰۳ حتی اگر کاربر در شرکت دیگری باشد، روی سربرگ ۱۰۰۳ چاپ می‌شود.
2. **قالب:** `FindTemplate(companyId, isA5)` → `Reports/MeetingMinutes/Companies/{id}/Minutes-{A4|A5}.mrt`
   (قابل تغییر با `Reporting:MeetingMinutes:TemplateRoot`)، جست‌وجو در `ContentRootPath`،
   `AppContext.BaseDirectory` و پوشهٔ جاری.
3. **سربرگ:** اول `Watermark.ImageBytes` خودِ قالب (`ExtractTemplateLetterhead`، کش در فایل
   موقت با کلید مسیر+اندازه+زمان تغییر)، بعد فایل سربرگ ثبت‌شدهٔ شرکت
   (`SystemCompanies.LetterheadFileName`). سپس `OverlayOnLetterhead` با PdfSharp.
4. **هندسه:** `Services/Office/MeetingMinutesDesignLayout.cs` قالب را می‌خواند
   (`ClientRectangle`, `Font`, `HorAlignment`, `TextBrush`, `RightToLeft`) و فاصله‌های
   عمودی/افقی را به چیدمان QuestPDF می‌دهد؛ هیچ عدد ثابتی برای جای این پنج بخش در کد نیست.
   برچسب‌ها هم از خودِ قالب خوانده می‌شوند (`:حاضرین` → `حاضرین:`).
5. **صفحهٔ دوم:** ناحیهٔ سربرگ (`BandBottom + 24`) در `page.Header()` رزرو می‌شود تا
   ادامهٔ صورتجلسهٔ بلند زیر نوار آبی سربرگ شروع شود، نه روی آن.
6. **بندها و امضاها** جاری‌اند: جدول ۵ ستونی (ردیف | شرح تصمیم | مسئول انجام | پیگیری | وضعیت)
   و سپس امضای حاضرین ۳ در هر ردیف. چون در جریان عادی صفحه است، صورتجلسهٔ بلند به صفحهٔ دوم
   می‌رود و سربرگ/عنوان در صفحهٔ ۲ تکرار نمی‌شود.
7. **پابرگ:** «صفحه X از Y» فقط در صورتجلسهٔ چندصفحه‌ای.
8. **مسیرهای جانشین:** نبودِ قالب → چیدمان قبلی کد؛ نبودِ سربرگ → همان چیدمان با صفحهٔ سفید؛
   خطای جدول/امضا → نسخهٔ ساده (`BuildFallbackPdf`). هیچ‌کدام چاپ را با خطا متوقف نمی‌کنند.

`Controllers/Office/MeetingMinutesController.cs`

```
GET api/office/meeting-minutes/{id}/print            → A4
GET api/office/meeting-minutes/{id}/print?size=A5    → A5
```

## ۴. تست‌ها

`inventory/tests/OutgoingLetterhead/test_minutes_template.py` — ۲۰ تست:

- وجود قالب برای ۱۰۰۳ (و ۳) در هر دو اندازه و **یکسان‌بودن بایت‌به‌بایت** دو درخت.
- `PaperSize`/`PageWidth`/`PageHeight`/`Margins` درست، `Watermark.ImageBytes` موجود.
- `DataSource1` با هشت ستون، و پنج جزء الزامی با عبارت دقیق `{DataSource1.*}`.
- شروع محتوا روی `BandBottom + 97` برای هر دو کاغذ؛ همهٔ متن‌ها RTL و داخل صفحه.
- سرویس: شرکت از `m.CompanyId` (و نه از درخواست)، سربرگ اول از قالب، پشتیبانی A4/A5،
  مسیرهای جانشین، و اینکه `MeetingMinutesDesignLayout` هرگز استثنا نمی‌دهد.
- یکتایی نام اعضای کلاس `MeetingMinutesDesignLayout` (خطای واقعی بیلد `CS0102`: property
  `Number` و متد کمکی `Number` هم‌نام بودند؛ متد به `ParseNumber` تغییر نام داد).

اجرای کل مجموعه: `cd inventory/tests/OutgoingLetterhead && python3 -m unittest discover -p "test_*.py"`
→ **۱۱۹ تست سبز** (۹۶ تست قبلی + ۲۳ تست جدید).

## ۵. پیش‌نمایش

- `preview/minutes.html` — خروجی A4 و A5 روی سربرگ واقعی (از `Watermark.ImageBytes`).
- `preview/minutes-mrt.html` — **فایل خام MRT** با شمارهٔ خط.
- تولید دوباره: `python3 preview/render_minutes.py`
- پیش‌نمایش زنده روی سرور محلی همین سندباکس (پورت ۸۰۹۰) سرو می‌شود.

## ۶. آنچه انجام نشده

- `dotnet build` / اجرای واقعی چاپ: در این محیط امکان‌پذیر نبود.
- قالب شرکت‌های دیگر (۱۰۰۵/۱/۲): درخواست فقط ۱۰۰۳ بود؛ برای هر شرکت جدید، پوشهٔ
  `Companies/{id}` را از ۱۰۰۳ کپی کنید و فقط `Watermark.ImageBytes` را با سربرگ آن شرکت
  عوض کنید (یا شناسه را در `COMPANIES` اسکریپت بگذارید و دوباره اجرا کنید).
