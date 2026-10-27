# چاپ روی سربرگ شرکت دیگر — علت و رفع

تاریخ: ۱۴۰۵/۰۸/۰۵ (۲۰۲۶-۱۰-۲۷)

## چرا این اتفاق می‌افتد؟
سربرگ هیچ‌وقت «حدس» زده نمی‌شود؛ از یکی از این دو منبع خوانده می‌شود:

| نوع چاپ | منبع شرکت | فایل سربرگ |
|---|---|---|
| **نامهٔ صادره** | `OutgoingLetters.CompanyId` (شرکت ثبت‌شده روی خود نامه) | `SystemCompanies.LetterheadFileName` یا سربرگ جاسازی‌شده در `Reports/OutgoingLetters/Companies/{id}/Outgoing-A4.mrt` |
| **نامهٔ داخلی** | **شرکت فعالی که کاربر بالای صفحه انتخاب کرده** (نامهٔ داخلی ستون شرکت ندارد) | `SystemCompanies.LetterheadFileName` یا `Reports/InnerLetters/Companies/{id}/Inner-A4.mrt` |
| **صورتجلسه** | شرکت فعالِ همان درخواست | `SystemCompanies.LetterheadFileName` |

پس «سربرگ شرکت دیگر» چهار علت ممکن دارد:

### ۱) رکورد شرکت مشتری سربرگ ندارد و کد بی‌صدا سربرگ نمونه می‌گذاشت (شایع‌ترین)
در `InnerLetterPrintService` این خطوط وجود داشت:
```csharp
if (company?.Id == 1003)  // ← شناسه در کد ثابت!
if (company?.Id == 1005 || Code == "1005")  // ← آریا
if (headerPath == null && ...) headerPath = "uploads/letterheads/forough-letterhead.pdf"; // ← بی‌صدا
```
یعنی اگر شرکت مشتری سربرگ نداشت، **فایل سربرگ «فروغ»** چاپ می‌شد. اگر شناسهٔ شرکتشان هم
تصادفاً ۱۰۰۳ یا ۱۰۰۵ بود (نصب روی پایگاه‌دادهٔ نمونه)، سربرگ هومپا/آریا می‌آمد.

### ۲) شرکت فعال اشتباه انتخاب شده
کاربری که به چند شرکت دسترسی دارد، یا مدیری که ردیف «دسترسی شرکت» ندارد، ممکن است شرکت فعالش
شرکت نمونه (هومپا/آریا/فروغ) باشد. نامهٔ داخلی هم سربرگ را از همان شرکت فعال می‌گیرد.

### ۳) نامه به شرکت اشتباه وصل شده
`OutgoingLetters.CompanyId` نامه — اگر موقع ثبت، شرکت فعال یک شرکت نمونه بوده، همان ذخیره شده
است. (`AppDbContext.ResolveDefaultCompanyId()` اولین شرکت فعال را انتخاب می‌کند.)

### ۴) هدر خام `X-Company-Id` در چاپ صورتجلسه
`MeetingMinutesPrintService` مستقیماً هدر را می‌خواند و اعتبارسنجی دسترسی را دور می‌زد.

## اصلاح‌های کد (انجام‌شده)

1. **`InnerLetterheadTemplates`** — قالب سربرگ هر شرکت از پوشهٔ خودش خوانده می‌شود:
   `TryLoadForCompany(contentRoot, baseDir, companyId, a5)` و `CompanyTemplatePath(...)`.
   شناسه‌های ثابت ۱۰۰۳/۱۰۰۵ از منطق حذف شدند.
2. **`InnerLetterPrintService`** — ترتیب انتخاب سربرگ:
   1. قالب اختصاصی همان شرکت (`Reports/InnerLetters/Companies/{id}/Inner-{A4|A5}.mrt`)
   2. فایل ثبت‌شده روی رکورد شرکت (PDF/تصویر)
   3. اگر شرکت مشخص است ولی هیچ‌کدام نبود ⇒ **خطای روشن** و توقف چاپ (نه سربرگ شرکت دیگر):
      «سربرگ شرکت «X» تنظیم نشده است؛ … از مسیر «سیستم ← شرکت‌ها» فایل PDF سربرگ این شرکت را بارگذاری کنید.»
   4. `forough-letterhead.pdf` فقط در نصب تک‌شرکتیِ **بدون هیچ رکورد شرکت** (نصب‌های قدیمی).
3. **`OutgoingLetterPrintService`** — نامهٔ بدون `CompanyId` دیگر «اولین شرکت دارای سربرگ» را
   برنمی‌دارد؛ اولویت: تنها شرکت فعال ⇒ یا شرکت فعالِ همان درخواست (اعتبارسنجی‌شده). اگر چند شرکت
   فعال باشد ⇒ خطای روشن و راهنمای رفع. سربرگ پیش‌فرض فقط وقتی هیچ شرکت فعالی وجود ندارد.
4. **`MeetingMinutesPrintService`** — شرکت فعال از `ActiveCompanyMiddleware` (اعتبارسنجی‌شده) خوانده
   می‌شود، نه از هدر خام.
5. لاگ‌ها حالا «کدام شرکت، کدام فایل» را ثبت می‌کنند تا در پشتیبانی سریع مشخص شود.

## رفع عملیاتی برای همین مشتری (به ترتیب)
1. `diagnose-letterhead-company-FA.sql` را روی دیتابیس مشتری اجرا کنید (فقط SELECT).
2. خروجی بخش ۱ را ببینید: هر شرکتی که `LetterheadFileName` خالی یا حاوی `forough/homepa/aria` است،
   سربرگش اشتباه است.
3. سربرگ درست را از UI بارگذاری کنید: **سیستم ← شرکت‌ها ← انتخاب شرکت ← بارگذاری سربرگ (PDF)**.
4. شرکت‌های نمونه‌ای که مشتری به آن‌ها کاری ندارد را `IsActive = 0` کنید (بخش اصلاح‌های اسکریپت)
   تا پیش‌فرضِ کاربران بدون دسترسی، شرکت آنها نشود.
5. برای هر کاربر، شرکت درست را در «دسترسی شرکت‌های کاربر» با `IsDefault = 1` ثبت کنید.
6. نامه‌های صادرهٔ بی‌شرکت را با `UPDATE OutgoingLetters SET CompanyId = @RightCompany ...` ترمیم کنید.
7. بالای صفحه (کمبوی شرکت) شرکت درست را انتخاب و دوباره چاپ کنید.

> از این پس اگر سربرگ شرکتی تنظیم نشده باشد، به‌جای چاپ اشتباه، **خطای روشن** نمایش داده می‌شود؛
> این خطا یعنی «سربرگ فلان شرکت تنظیم نشده»، نه خرابی چاپ.

## فایل‌های تغییریافته
- `Inventory.Api/Services/Office/InnerLetterheadTemplates.cs`
- `Inventory.Api/Services/Office/InnerLetterPrintService.cs`
- `Inventory.Api/Services/Office/Outgoing/OutgoingLetterPrintService.cs`
- `Inventory.Api/Services/Office/MeetingMinutesPrintService.cs`
- `Inventory.Client/Pages/Office/Letters/InnerLetterGuide.razor`
- `Inventory.Client/Pages/Office/Guides/OutgoingLetterGuide.razor`
- `/home/user/diagnose-letterhead-company-FA.sql` (اسکریپت عیب‌یابی)
