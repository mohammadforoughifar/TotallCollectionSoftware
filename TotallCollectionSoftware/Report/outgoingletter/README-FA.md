# `Report/outgoingletter` — این پوشه چیست و چرا ویرایش آن چاپ نامه را تغییر نمی‌دهد؟

این پوشه **بستهٔ طراحی و پیش‌نمایش** سربرگ نامهٔ صادره است، نه مسیری که سرور از آن چاپ می‌کند.
هیچ جای کد (API و کلاینت) مسیر `Report/outgoingletter` را نمی‌خواند؛ نه در `Program.cs`، نه در
`OutgoingLetterPrintService` و نه در `appsettings.json`. بنابراین اگر فایل `*.mrt` یا تصویر سربرگ
را فقط در همین پوشه عوض کنید، خروجی چاپ **بدون تغییر** می‌ماند.

## محتوای پوشه

| فایل | کاربرد |
|---|---|
| `{3,1003}/Report_A4_OutgoingLetter.mrt` | پروژهٔ کامل گزارش Stimulsoft (دو منبع داده، متغیرهای امضا، کادرهای متن) — مرجع طراحی |
| `{3,1003}/Report_A5_OutgoingLetter.mrt` | همان گزارش برای کاغذ A5 |
| `{3,1003}/assets/homepa_company*_letterhead.png` | تصویر خام سربرگ هومپا (۲۴۸۱×۳۵۰۸) |
| `{3,1003}/assets/Vazirmatn-*.ttf` | فونت‌های فارسی |
| `{3,1003}/index.html` | پیش‌نمایش مرورگری چیدمان همان MRT (فقط برای بازبینی بصری) |

## مسیری که سرور واقعاً می‌خواند

```text
inventory/src/Inventory.Api/Reports/OutgoingLetters/Companies/{CompanyId}/Outgoing-{A4|A5}.mrt
```

این فایل‌ها با `Content Include="Reports/**/*.mrt"` در `Inventory.Api.csproj` کنار خروجی
Build/Publish کپی می‌شوند و مسیر جست‌وجو در `FindCompanyTemplate` (ContentRoot ← BaseDirectory ←
پوشهٔ جاری) است. ریشهٔ مسیر هم از `Reporting:Stimulsoft:TemplateRoot` خوانده می‌شود (پیش‌فرض
همان `Reports/OutgoingLetters/Companies`).

پوشهٔ دیگر `src/Inventory.Api/Reports/...` نسخهٔ قدیمی‌تر همین درخت است. ابزارهای استقرار
(`publish-server.bat`، `deploy-single.sh`، `inventory/start.bat`) روی `inventory/src` کار می‌کنند؛
پس **`src/` را ویرایش نکنید**، مگر اینکه همان را هم منتشر کنید.

## دو نکتهٔ مهم دربارهٔ محتوای MRT

۱. سرور قالب را «اجرا» نمی‌کند. از هر قالب فقط این‌ها خوانده می‌شود:
   * `Watermark/ImageBytes` → تصویر سربرگ (پس‌زمینهٔ نامه)
   * اجزای متنی `{DataSource1.LetterNumber}`، `{DataSource1.DateShams}` و `{DataSource1.Peyvast}`
     → مستطیل، رنگ (`TextBrush`)، اندازهٔ فونت و تراز مقدار «شماره/تاریخ/پیوست»

   کادرهای `GirandeAsli` / `TitleLetter` / `TextLetter` و کادرهای تصویر امضا در چاپ **اعمال
   نمی‌شوند**؛ متن نامه، موضوع، گیرنده و بلوک امضا را کد `OutgoingLetterPrintService` می‌چیند.
   یعنی تغییر جای این کادرها در MRT، چاپ را جابه‌جا نمی‌کند.

۲. شناسهٔ شرکت در دو نصب متفاوت است: هومپا در نصب اصلی `1003` و در نصب نمونه `3` است. تصویر
   سربرگ هر دو پوشه یکی است (`homepa_company3_letterhead.png` و `homepa_company1003_letterhead.png`
   بایت‌به‌بایت یکسان‌اند). چیدمان کالیبره‌شدهٔ هومپا در کد برای هر دو شناسه فعال است و با
   `Reporting:OutgoingLetters:HomepaCompanyIds` قابل تنظیم است. آریا `1005` و فروغ آریا `1` و `2`.

| شناسه | طرح سربرگ | مرجع تصویر |
|---|---|---|
| `1`, `2` | فروغ آریا (کاغذ سفید، برچسب‌های تیره) | `wwwroot/uploads/letterheads/forough-letterhead.pdf` |
| `3`, `1003` | هومپا (نوار آبی، مقدارها سفید) | `assets/homepa_company*_letterhead.png` |
| `1005` | آریا همیار ژنراتور | `aria_company1005_letterhead.pdf` |

## چرا چاپ روی طرح شرکتِ دیگر یا با چیدمان غلط انجام می‌شود؟

قالب با **شناسهٔ عددی** شرکت پیدا می‌شود (`Companies/{CompanyId}`) و این شناسه بین نصب‌ها یکی
نیست: هومپا در پایگاه دادهٔ اصلی `1003` است و در نصب نمونه `3`. جدول `SystemCompanies` شناسه را
خودکار (`IDENTITY`) می‌سازد، پس در یک نصب تازه اولین شرکت‌ها `1`، `2`، `3` می‌شوند. نتیجه:

* اگر شناسهٔ شرکت شما `1` باشد، نامه روی **طرح فروغ آریا** (`Companies/1`) چاپ می‌شود، چون همان
  پوشه تصویر و برچسب‌های فروغ را دارد — حتی اگر شرکت شما هومپا باشد.
* اگر شناسهٔ شرکت هیچ پوشه‌ای نداشته باشد، چاپ با پیام «سربرگ شرکت ... پیدا نشد» متوقف می‌شود.

راه‌حل بدون تغییر کد، در `appsettings.json`:

```json
"Reporting": {
  "OutgoingLetters": {
    "HomepaCompanyIds": "3,1003",
    "AriaCompanyIds": "1005",
    "CompanyTemplateMap": {
      "4": "1003"
    }
  }
}
```

`"4": "1003"` یعنی «شرکت با شناسهٔ ۴ با طرح/سربرگ پوشهٔ ۱۰۰۳ چاپ شود». تصویر سربرگ، مختصات
شماره/تاریخ/پیوست، رنگ آن‌ها و فاصلهٔ شروع متن همه از همان پوشه می‌آیند؛ بنابراین یک نگاشت، کل
چیدمان را درست می‌کند. اگر طرح مقصد هومپا یا آریا باشد، شناسهٔ پوشه را در
`HomepaCompanyIds` / `AriaCompanyIds` هم داشته باشید (پیش‌فرض: `3,1003` و `1005`).

شناسهٔ واقعی شرکت‌ها:

```sql
SELECT Id, Name, IsActive FROM SystemCompanies ORDER BY Id;
```

## همگام‌سازی این پوشه با مسیری که چاپ می‌شود

```bat
Report\outgoingletter\sync-to-api-templates.bat            :: هر دو درخت کد
Report\outgoingletter\sync-to-api-templates.bat inventory    :: فقط inventory\src
Report\outgoingletter\sync-to-api-templates.bat src          :: فقط src
```

اسکریپت، `Report_{A4,A5}_OutgoingLetter.mrt` را برای شرکت‌های `3` و `1003` به
`...\Reports\OutgoingLetters\Companies\{id}\Outgoing-{A4,A5}.mrt` کپی می‌کند. پس از آن
API را دوباره Build/Publish یا ری‌استارت کنید.

> پیش از اجرا از فایل مقصد نسخهٔ پشتیبان بگیرید: تصویر سربرگ داخل `Report/outgoingletter/1003`
> نسخهٔ قدیمی‌تر (۴۶۶KB) است و نسخهٔ `inventory/.../1003` جدیدتر (۴۷۳KB)؛ هر دو یک طرح و یک
> جای برچسب دارند، ولی کپی، نسخهٔ قدیمی را جای جدید می‌گذارد.

## چگونه یک تغییر طراحی را واقعاً اعمال کنیم؟

1. طرح را در همین پوشه (نسخهٔ `.mrt` و تصویر) نهایی کنید.
2. با `sync-to-api-templates.bat` (بالا) یا با کپی دستی، فایل را روی مسیر سرور بگذارید:
   ```text
   inventory/src/Inventory.Api/Reports/OutgoingLetters/Companies/{CompanyId}/Outgoing-{A4|A5}.mrt
   ```
   (برای هر دو سایز؛ و برای هومپا برای هر دو شناسهٔ `3` و `1003` اگر هر دو در پایگاه داده فعال‌اند.)
3. اگر شرکت در `SystemCompanies.LetterheadFileName` فایل ثبت‌شده دارد، آن فایل **اولویت** دارد؛
   یا فایل `wwwroot/uploads/letterheads/...` را هم به‌روز کنید یا مقدار ستون را خالی کنید تا
   سربرگ از قالب MRT خوانده شود.
4. API را دوباره Publish/اجرا کنید. کش سربرگ در `%TEMP%/totall-letterheads` با طول و تاریخ فایل
   کلید می‌شود، پس بازسازی لازم نیست؛ فقط اگر همان فایل با همان تاریخ روی سرور مانده باشد، یک بار
   فایل را لمس/جایگزین کنید.
5. برای دیدن اینکه کدام مسیر واقعاً استفاده شده، در لاگ چاپ این خطوط را ببینید:
   `SelectedCompanyId`، `RegisteredPath`، `TemplatePath`، `FinalPath`، `FromTemplate` و
   `مقادیر شماره/تاریخ/پیوست از قالب شرکت خوانده شد`.

## پیش‌نمایش `index.html`

این فایل چیدمان همان MRT را در مرورگر بازمی‌سازد و برای بازبینی جهت RTL و جای برچسب‌ها مفید است،
اما **خروجی نهایی چاپ نیست**. لینک‌های PDF قدیمی این پوشه (فایل‌های `*Preview*.pdf`) در مخزن وجود
ندارند؛ برای دیدن PDF واقعی از مسیر چاپ خود سامانه استفاده کنید (کارتابل نامهٔ صادره → چاپ A4/A5).
