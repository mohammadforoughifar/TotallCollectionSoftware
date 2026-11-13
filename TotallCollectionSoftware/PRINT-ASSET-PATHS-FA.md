# فایل‌های ورودی چاپ: هر چاپ از کدام مسیر خوانده می‌شود؟

این یادداشت پاسخ کوتاهِ همین سؤال است برای هر سه چاپ دفترنامه (نامهٔ داخلی، نامهٔ صادره،
صورتجلسه). منبعِ حقیقتِ کد: `inventory/src/Inventory.Api/Services/Office/PrintAssetResolver.cs`.

پیش از این هر سرویس قاعدهٔ خودش را داشت؛ نتیجه‌اش این بود که فایل‌های یک شرکت (مثلاً ۱۰۰۵) در
یک چاپ پیدا می‌شد و در دو چاپ دیگر نه. اکنون **یک قاعده** داریم.

---

## ۱) قالب MRT (فایل طرح Stimulsoft)

ترتیب بررسی، برای هر سه چاپ یکسان است:

| # | محل |
|---|---|
| ۱ | مسیر تنظیمات: `Reporting:OutgoingLetters:TemplateRoot` / `Reporting:InnerLetters:TemplateRoot` / `Reporting:MeetingMinutes:TemplateRoot` |
| ۲ | **پوشهٔ داخل مخزن** — `…/inventory/src/Inventory.Api/Reports/{نوع}/Companies/{شرکت}/`، بعد `…/src/Inventory.Api/Reports/…`، بعد `…/Reports/…` |
| ۳ | `ContentRoot` (اجرای `dotnet run` از پوشهٔ پروژه) |
| ۴ | `BaseDirectory` (نسخهٔ Publish) |
| ۵ | پوشهٔ جاری (`Directory.GetCurrentDirectory()`) |
| ۶ | آخرین شانس: پوشهٔ طراحی `Report/{innerletters\|outgoingletter\|meetingminutes}/{شرکت}/` |

پوشهٔ مخزن عمداً **پیش از** خروجی بیلد بررسی می‌شود: قالبی که با Stimulsoft روی فایل مخزن
ذخیره می‌شود، بی‌درنگ در چاپ دیده می‌شود و منتظر Publish کامل نمی‌ماند.

نام فایل استاندارد: `Inner-A4.mrt` / `Inner-A5.mrt` / `Outgoing-A4.mrt` / `Outgoing-A5.mrt` /
`Minutes-A4.mrt` / `Minutes-A5.mrt`. اگر طراح نام دیگری گذاشته باشد، همان پوشه‌ها با هر فایلی که
اندازه (`A4`/`A5`) در نامش باشد بررسی می‌شوند (برای نامهٔ صادره).

## ۲) فایل سربرگ

مقدار `SystemCompanies.LetterheadFileName` می‌تواند مسیر نسبی کامل (`uploads/letterheads/x.pdf`)،
یا فقط نام فایل (`x.pdf`) باشد. ترتیب بررسی:

| # | محل |
|---|---|
| ۱ | `{root}/{همان مقدار}` |
| ۲ | `{root}/uploads/{بدون پیشوند uploads}` |
| ۳ | `{root}/uploads/letterheads/{نام فایل}` |
| ۴ | `{root}/wwwroot/uploads/letterheads/{نام فایل}` |
| ۵ | کپی داخل مخزن: `…/inventory/src/Inventory.Api/wwwroot/uploads/letterheads/{نام}` و `…/src/…` |
| ۶ | اسکن پوشه با **نامِ نرمال‌شده**: ي→ی، ك→ک، ة→ه و حذف نیم‌فاصله، فاصله، `-`، `_`، `.`، `()` |

`{root}`ها: پوشهٔ جاری، `ContentRoot`، `BaseDirectory`.

فقط `.pdf` / `.png` / `.jpg` / `.jpeg` قابل چاپ‌اند (`IsRenderable`).

## ۳) اولویت سربرگ در هر چاپ

| چاپ | اولویت |
|---|---|
| نامهٔ داخلی | ۱) تصویر جاسازی‌شده در همان MRT (`Watermark.ImageBytes`) → ۲) فایل ثبت‌شدهٔ شرکت |
| صورتجلسه | ۱) تصویر جاسازی‌شده در همان MRT → ۲) فایل ثبت‌شدهٔ شرکت |
| نامهٔ صادره | ۱) فایل ثبت‌شدهٔ شرکت → ۲) تصویر جاسازی‌شده در MRT |

استثناهای نامهٔ صادره:

* `Reporting:OutgoingLetters:PreferTemplateLetterhead=true` → طرح قالب بر فایل ثبت‌شده اولویت می‌گیرد.
* `Reporting:OutgoingLetters:TemplateLetterheadCompanyIds` (پیش‌فرض `1003`) → برای شرکت‌های این
  فهرست همیشه طرح قالب ملاک است.
* **گارد «سربرگ شرکت دیگر»**: اگر فایل ثبت‌شده از خانوادهٔ شرکت دیگری باشد (`homepa` = ۳/۱۰۰۳،
  `aria` = ۱۰۰۵، `forough` = ۱) و قالب سربرگ درست را داشته باشد، فایل ثبت‌شده نادیده گرفته می‌شود.
  این همان خطای «نامهٔ شرکت ۱۰۰۵ با سربرگ شرکت ۱ چاپ شد» بود.

## ۴) رسم سربرگ روی کاغذ

`PrintAssetResolver.LetterheadRect(pageW, pageH, assetW, assetH)`: اگر نسبت ابعاد فایل با کاغذ
یکی باشد (اختلاف ≤ ۰٫۵٪) همان کل صفحه رسم می‌شود. اختلاف بین ۰٫۵٪ و ۳٪ (مثل
`aria-1005-letterhead.pdf` که ۸۶۰٫۶pt ارتفاع دارد به‌جای ۸۴۱٫۹pt) با **حفظ نسبت** و لنگر بالا رسم
می‌شود: عرض دقیقاً برابر صفحه می‌ماند تا ردیف شماره/تاریخ/پیوست روی نقطه‌چین جابه‌جا نشود و
اضافهٔ عمودی از پایین بریده می‌شود. اختلاف بیش از ۳٪ (کاغذ دیگر، مثل Letter) همان کشیدن کامل قبلی است.

## ۵) وضعیت شرکت ۱۰۰۵

| چیز | مسیر |
|---|---|
| قالب نامهٔ داخلی | `inventory/src/Inventory.Api/Reports/InnerLetters/Companies/1005/Inner-{A4,A5}.mrt` |
| قالب صورتجلسه | `inventory/src/Inventory.Api/Reports/MeetingMinutes/Companies/1005/Minutes-{A4,A5}.mrt` |
| قالب نامهٔ صادره | `inventory/src/Inventory.Api/Reports/OutgoingLetters/Companies/1005/Outgoing-{A4,A5}.mrt` |
| سربرگ | `inventory/src/Inventory.Api/wwwroot/uploads/letterheads/aria-1005-letterhead.pdf` (۵۹۵٫۴۴×۸۶۰٫۶۰pt) |

هر شش قالب ۱۰۰۵ همان یک طرح سربرگ آریا را جاسازی کرده‌اند (`Watermark.ImageBytes` = ۱۳۳٫۶۷۳ بایت،
۱۸۲۰×۲۵۷۴، md5 `dd955f5ed9…`)، پس چاپ حتی بدون فایل PDF سربرگ هم روی طرح درست انجام می‌شود.

## ۶) چطور روی سرور بررسی کنیم

```
GET /api/print-assets/diagnose?companyId=1005&size=A4
```

خروجی: ریشه‌های اجرا، `letterhead.registeredValue` / `resolved` / `renderable` / `candidates[{path,exists}]`
و برای هر سه چاپ `{expectedFile, resolved, found, watermarkBytes, candidates}` به‌همراه `hint` فارسی.

در لاگ هم هر سه سرویس صریح می‌نویسند: مسیر قالب انتخاب‌شده، منبع سربرگ
(«قالب MRT» / «فایل ثبت‌شدهٔ شرکت» / «هیچ») و فهرست مسیرهای بررسی‌شده.

> وضعیت: کد کامپایل نشده است (dotnet در محیط توسعهٔ ما نیست) — `dotnet build` لازم است.
