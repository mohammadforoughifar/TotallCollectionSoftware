# چاپ صورتجلسه روی سربرگ شرکت — قالب Stimulsoft (MRT)

> **وضعیت تأیید:** این تغییرات **کامپایل نشده‌اند** (سندباکس `dotnet` ندارد). پیش از اجرا
> `dotnet build` لازم است. پیش‌نمایش‌ها شبیه‌سازی دقیق هندسه با فونت و سربرگ واقعی‌اند، نه
> خروجی QuestPDF.

---

## ۱. خواسته

- صورتجلسه روی سربرگ **همان شرکتی** چاپ شود که صورتجلسه برایش ثبت شده است.
- **عنوان، شماره صورتجلسه، تاریخ جلسه، پیوست، حاضرین، غایبین** همیشه روی کاغذ دیده شوند؛
  شماره/تاریخ/پیوست **روی برچسب‌های چاپ‌شدهٔ سربرگ** («شماره»، «تاریخ»، «پیوست») بنشینند.
- عنوان زیر مستطیل/روبان آبی سربرگ و **راست‌چین** باشد (نه وسط‌چین).
- بندها **فهرست شماره‌دار ساده** باشند، نه جدول.
- بلوک امضا برای **امضای دستی روی برگه** جا داشته باشد (کادر بزرگ‌تر) و متن «در انتظار امضا»
  روی کاغذ نیاید.
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
  `Number, Title, MeetingDate, Attendees, Absentees, Items, CompanyName, Status, Attachment`.
- اجزای صفحه (هر کدام کادر مستقل، فونت `Vazirmatn` / `Vazirmatn-Bold`، `RightToLeft=True`).
  مختصات = point از گوشهٔ بالا-چپ کاغذ:

| جزء | عبارت | A4 (top / left / عرض / ارتفاع) | A5 |
| --- | --- | --- | --- |
| `NumberValue` | `{DataSource1.Number}` | ۵۹٫۳ / ۱۴٫۴ / ۸۲٫۱ / ۱۴٫۴ — متن **سفید** | ۴۰٫۵ / ۷٫۲ / ۵۹ / ۱۳ |
| `MeetingDateValue` | `{DataSource1.MeetingDate}` | ۷۷٫۳ / ۱۴٫۴ / ۸۲٫۱ / ۱۴٫۴ — سفید | ۵۳٫۳ / ۷٫۲ / ۵۹ / ۱۳ |
| `AttachmentValue` | `{DataSource1.Attachment}` | ۹۶٫۰ / ۱۴٫۴ / ۸۲٫۱ / ۱۴٫۴ — سفید، ۱۲pt | ۶۶٫۵ / ۷٫۲ / ۵۹ / ۱۳ |
| `TitleValue` | `{DataSource1.Title}` | ۱۹۳ / ۲۸٫۱ / ۵۲۹٫۲ / ۳۹ — **راست‌چین** | ۱۳۹ / ۲۸٫۱ / ۳۴۵٫۶ / ۲۵ |
| `AttendeesValue` + `AttendeesCaption` | `{DataSource1.Attendees}` + «:حاضرین» | ۲۳۲ / ۲۸٫۱ + ۴۷۸٫۱ | ۱۶۴ / ۲۸٫۱ + ۳۲۶٫۲ |
| `AbsenteesValue` + `AbsenteesCaption` | `{DataSource1.Absentees}` + «:غایبین» | ۲۵۶ / ۲۸٫۱ + ۴۷۸٫۱ | ۱۸۲ / ۲۸٫۱ + ۳۲۶٫۲ |
| `ItemsCaption` سپس `ItemsValue` | `{DataSource1.Items}` + «:بندها و تصمیمات» | ۲۸۰ سپس ۳۰۶ | ۲۰۰ سپس ۲۲۲ |

**سه قاعدهٔ هندسی (تأیید کارفرما):**

1. **متا روی سربرگ:** شماره/تاریخ/پیوست در همان کادرهای سفیدی می‌نشینند که برچسب «شماره /
   تاریخ / پیوست» روی سربرگ ۱۰۰۳ چاپ شده است (همان مختصاتی که در قالب نامهٔ صادره/داخلی هم
   به‌کار رفته). برچسب فارسی‌شان از متن صورتجلسه حذف شده، چون روی کاغذ هست. مقدارها با متن
   سفید و در لایهٔ `Foreground` (مختصات مطلق) چاپ می‌شوند.
2. **عنوان:** ۹pt پایین‌تر از پایین‌ترین پیکسل آبیِ سربرگ (۱۸۴pt در A4) و **راست‌چین**؛
   «عنوان از سمت راست شروع شود، وسط صفحه نباشد».
3. **بدنه:** از ۲۳۲pt در A4 (۱۶۴pt در A5) شروع می‌شود؛ فاصلهٔ هر بخش تا بخش بعدی از دل
   همین جدول خوانده می‌شود (۲ تا ۲۴pt).

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
   (`ClientRectangle`, `Font`, `TextBrush`, `RightToLeft`) و جای هر بخش را به چیدمان
   QuestPDF می‌دهد؛ هیچ عدد ثابتی برای جای این بخش‌ها در کد نیست. برچسب‌ها هم از خودِ قالب
   خوانده می‌شوند (`:حاضرین` → `حاضرین:`). سه کادر شماره/تاریخ/پیوست با
   `BuildHeaderMetaRows` + `DrawHeaderMeta` در لایهٔ `Foreground` و با مختصات مطلق چاپ
   می‌شوند (ستون مقدار ۴pt پیش از برچسبِ چاپ‌شده تمام می‌شود).
   تراز افقی را خودِ چاپ تعیین می‌کند (راست‌چین)، چون در Stimulsoft با `RightToLeft` تراز
   آینه می‌شود و با آنچه روی کاغذ می‌بینیم یکی نیست.
5. **پیوست:** صورتجلسه فیلد پیوست ندارد؛ `GeneratePdfAsync` تعداد
   `AppAttachments` با `Module="MeetingMinutes"` و `RefId=Id` را می‌شمارد و «دارد/ندارد»
   را در کادر پیوست سربرگ می‌گذارد (همان قاعدهٔ `AttachmentCount` فهرست صورتجلسه‌ها).
6. **صفحهٔ دوم:** ناحیهٔ سربرگ (`BandBottom + 24`) در `page.Header()` رزرو می‌شود تا
   ادامهٔ صورتجلسهٔ بلند زیر نوار آبی سربرگ شروع شود، نه روی آن.
7. **بندها** فهرست شماره‌دار ساده‌اند (بدون جدول): شمارهٔ بند در ستون ۱۵pt و متن جاری
   (شرح + «مسئول/پیگیری» خاکستری + وضعیت رنگی). **امضاها** بعد از بندها می‌آیند:
   در A4 سه کادر ۱۶۸×۵۲pt در هر ردیف و در A5 دو کادر ۴۴pt؛ کل بلوک `ShowEntire` است تا
   نصفه به صفحهٔ بعد نرود. اگر کاربر در پنجرهٔ `canvas` امضا کرده باشد تصویر PNG او
   (ستون `MeetingMinutesParticipants.SignatureData`) داخل کادر چاپ می‌شود، وگرنه کادر
   خالی برای امضای دستی؛ «در انتظار امضا» چاپ نمی‌شود و فقط تاریخ امضای ثبت‌شده می‌ماند.
7. **پابرگ:** «صفحه X از Y» فقط در صورتجلسهٔ چندصفحه‌ای.
8. **مسیرهای جانشین:** نبودِ قالب → چیدمان قبلی کد؛ نبودِ سربرگ → همان چیدمان با صفحهٔ سفید؛
   خطای جدول/امضا → نسخهٔ ساده (`BuildFallbackPdf`). هیچ‌کدام چاپ را با خطا متوقف نمی‌کنند.

`Controllers/Office/MeetingMinutesController.cs`

```
GET api/office/meeting-minutes/{id}/print            → A4
GET api/office/meeting-minutes/{id}/print?size=A5    → A5
```

## ۴. تست‌ها

`inventory/tests/OutgoingLetterhead/test_minutes_template.py` — ۲۶ تست:

- وجود قالب برای ۱۰۰۳ (و ۳) در هر دو اندازه و **یکسان‌بودن بایت‌به‌بایت** دو درخت.
- `PaperSize`/`PageWidth`/`PageHeight`/`Margins` درست، `Watermark.ImageBytes` موجود.
- `DataSource1` با نُه ستون، و شش جزء الزامی با عبارت دقیق `{DataSource1.*}`.
- جای شماره/تاریخ/پیوست دقیقاً روی برچسب‌های سربرگ (متن سفید، پیوست ۱۲pt) و نبودِ برچسب
  تکراری برایشان؛ عنوان ۱۹۳pt (A5: ۱۳۹pt) راست‌چین و بدنه از ۲۳۲pt (A5: ۱۶۴pt).
- همهٔ متن‌ها RTL و داخل صفحه.
- سرویس: شرکت از `m.CompanyId` (و نه از درخواست)، سربرگ اول از قالب، پشتیبانی A4/A5،
  متا در لایهٔ `Foreground`، شمارش پیوست از `AppAttachments`، فهرست بدون جدول،
  بلوک امضای ۵۲/۴۴pt و `ShowEntire`، نبودِ «در انتظار امضا» در مسیر قالب، مسیرهای جانشین،
  و اینکه `MeetingMinutesDesignLayout` هرگز استثنا نمی‌دهد.
- یکتایی نام اعضای کلاس `MeetingMinutesDesignLayout` (خطای واقعی بیلد `CS0102`: property
  `Number` و متد کمکی `Number` هم‌نام بودند؛ متد به `ParseNumber` تغییر نام داد).

اجرای کل مجموعه: `cd inventory/tests/OutgoingLetterhead && python3 -m unittest discover -p "test_*.py"`
→ **۱۳۹ تست سبز** (پس از این تغییر).

## ۵. پیش‌نمایش

پیش‌نمایش زندهٔ همین چیدمان (HTML، با سربرگ و فونت واقعی) در جلسهٔ کار نشان داده شد؛ فایل‌های
قالب هم با `python3 tools/build_minutes_mrt.py` از نو ساخته می‌شوند (idempotent) — سه نسخه:
`src/…`, `inventory/src/…` و `Report/meetingminutes/…` (نسخهٔ طراح Stimulsoft).

## ۵-۱. شمارهٔ صفحه

به‌درخواست کارفرما («شماره صفحه را حذف کن») صورتجلسه **شمارهٔ صفحه چاپ نمی‌کند**:
نه در مسیر قالب (`Minutes-{A4,A5}.mrt`) و نه در مسیر قدیمیِ بدون قالب. در مسیر قدیمی،
پابرگ فقط نام شرکت را نشان می‌دهد.

این حذف یک خطای واقعی چاپ را هم برطرف کرد: پابرگ با جزء پویای QuestPDF ساخته می‌شد و اگر
محتوایش در فضای در دسترس آن کادر جا نمی‌شد، QuestPDF کل سند را با
«Dynamic component generated content that does not fit on a single page» رد می‌کرد
(در A5 دیده شد). اکنون هیچ `Dynamic` در سرویس چاپ نیست.

جزئیات کامل: `OUTGOING-PRINT-REPORT-FOLDER-FA.md` §۴-۱۳.

## ۶. آنچه انجام نشده

- `dotnet build` / اجرای واقعی چاپ: در این محیط امکان‌پذیر نبود.
- قالب شرکت‌های دیگر (۱۰۰۵/۱/۲): درخواست فقط ۱۰۰۳ بود؛ برای هر شرکت جدید، پوشهٔ
  `Companies/{id}` را از ۱۰۰۳ کپی کنید و فقط `Watermark.ImageBytes` را با سربرگ آن شرکت
  عوض کنید (یا شناسه را در `COMPANIES` اسکریپت بگذارید و دوباره اجرا کنید).
