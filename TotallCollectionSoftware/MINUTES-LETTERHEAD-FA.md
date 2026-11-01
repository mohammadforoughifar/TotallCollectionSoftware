# چاپ صورتجلسه روی سربرگ شرکت (۱۰۰۵) — چرا سربرگ می‌افتاد و چه شد

## نشانه

«صورتجلسه روی سربرگ شرکت ۱۰۰۵ چاپ نمی‌شود» — PDF سالم بالا می‌آمد، ولی برگه **بدون**
سربرگ ۱۰۰۵ بود و در لاگ سرور هم هیچ خطای مشخصی نبود.

## سه مسیر بی‌صدایی که سربرگ را می‌انداختند

هر سه در `Inventory.Api/Services/Office/MeetingMinutesPrintService.cs` بودند:

1. **شکست ساخت محتوا با چیدمان قالب**
   ```csharp
   catch
   {
       contentPdf = BuildFallbackPdf(m, parts, items, companyName);
       letterheadPath = null;      // ← سربرگ همین‌جا دور ریخته می‌شد
   }
   ```
   اگر QuestPDF در `GeneratePdf()` جا کم می‌آورد (بلوک امضا/متن بلند)، استثنا گرفته می‌شد،
   سند جانشین ساخته می‌شد **و سربرگ هم پاک می‌شد** — نتیجه: برگهٔ سادهٔ بدون سربرگ.

2. **شکست کشیدن روی سربرگ**
   ```csharp
   catch { return BuildContentPdf(m, parts, items, false, companyName); }
   ```
   `reserveLetterhead: false` یعنی حاشیهٔ سربرگ صفر و رنگ صفحه سفید.

3. **صفحهٔ سفیدِ مات روی سربرگ**
   `BuildContentPdf` با `reserveLetterhead: true` رنگ صفحه را شفاف نمی‌کرد؛ QuestPDF پیش‌فرض
   صفحه را **سفیدِ مات** می‌کند، بعد آن صفحه روی تصویر سربرگ کشیده می‌شد و سربرگ کامل پوشیده
   می‌شد. فقط `BuildTemplateContentPdf` این را داشت:
   `page.PageColor(hasLetterhead ? Colors.Transparent : Colors.White)`.

   یعنی هر بار که چیدمان قالب «کامل» تشخیص داده نمی‌شد (یا مسیر جانشین فعال می‌شد)، PDF
   درست بود ولی **سربرگ ناپیدا**.

## اصلاح

| مسیر | رفتار جدید |
| --- | --- |
| ساخت محتوا با قالب | استثنا ⇒ لاگ `LogWarning` با `MinutesId/CompanyId/Template/HasLetterhead` و تلاش دوم با همان قالب و **همان سربرگ**، فقط بدون لایهٔ کادرهای شماره/تاریخ/پیوست (`withHeaderMeta: false`) |
| تلاش دوم هم نشد | چیدمان کلاسیک (`BuildContentPdf`) روی **همان سربرگ** |
| چیدمان کلاسیک | `page.PageColor(reserveLetterhead ? Colors.Transparent : Colors.White)` |
| نسخهٔ ساده (`BuildFallbackPdf`) | پارامتر `withLetterhead`؛ شفاف + حاشیهٔ بالای ۱۵۰pt و بدون هدرِ خودش |
| کشیدن روی سربرگ | فقط اگر خودِ فایل سربرگ خراب باشد `LogError` می‌شود و نسخهٔ بدون سربرگ برمی‌گردد |
| تصویر جاسازی‌شده در MRT | با `PrintAssetResolver.IsRenderable` بررسی می‌شود؛ اگر خراب بود، فایل ثبت‌شدهٔ همان شرکت جای آن می‌نشیند |
| هیچ سربرگی نبود | `LogWarning`: «صورتجلسه {Id} شرکت {CompanyId} بدون سربرگ چاپ می‌شود» |

هیچ‌جای سرویس دیگر `letterheadPath = null;` وجود ندارد؛ سربرگ فقط وقتی کنار گذاشته می‌شود که
مرحلهٔ کشیدن روی آن با خطا شکست بخورد — و آن هم لاگ دارد.

## شرکتِ چاپ (چرا ممکن است سربرگ شرکت دیگری بیاید)

`GeneratePdfAsync` حالا زنجیرهٔ صریح دارد (`ResolveCompanyAsync`):

1. `MeetingMinutes.CompanyId` — مرجع اصلی (`MinutesCompanySource.Record`)
2. شرکت فعالِ درخواست از `ActiveCompanyMiddleware` (`ActiveContext`)
3. هدر خام `X-Company-Id` (`Header`) — فقط برای صورتجلسه‌های قدیمیِ بدون شرکت
4. هیچ (`None`) ⇒ لاگ هشدار و چاپ بدون سربرگ

اگر شرکت از رکورد نیاید، در لاگ نوشته می‌شود که چرا سربرگ از شرکت فعال گرفته شده است.

## تشخیص بدون لاگ سرور

```http
GET /api/print-assets/diagnose?minutesId=123&size=A4
```

خروجی تازه:

- `minutes.recordCompanyId` / `minutes.headerCompanyId` / `minutes.effectiveCompanyId`
- `minutes.source` — همان یکی از چهار حالت بالا با متن فارسی
- `templates.minutes.resolved` و `templates.minutes.watermarkBytes`
  (برای ۱۰۰۵ باید `133673` باشد؛ صفر یعنی قالبِ در حال استفاده سربرگ جاسازی‌شده ندارد)
- `letterhead.candidates` — همهٔ مسیرهایی که برای فایل ثبت‌شدهٔ شرکت بررسی می‌شود

اگر `recordCompanyId` خالی بود، همان صورتجلسه شرکت ثبت‌شده ندارد؛ با ویرایش صورتجلسه در
شرکت ۱۰۰۵ یا ست‌کردن `MeetingMinutes.CompanyId = 1005` مشکل ریشه‌ای حل می‌شود.

## یادآوری استقرار

`start.bat` همیشه `deploy-single.ps1` را صدا می‌زند و برنامه از `inventory\publish\app` اجرا
می‌شود. یعنی **نسخهٔ کهنهٔ MRT داخل `publish` بر فایل ویرایش‌شدهٔ مخزن اولویت دارد**؛ اگر
`templates.minutes.resolved` مسیر `publish\app\...` را نشان داد و `watermarkBytes` صفر بود،
باید دوباره publish شود.

## تست‌ها

`inventory/tests/OutgoingLetterhead` — کلاس تازهٔ `MinutesLetterheadIsNeverSilentlyDropped`:

- هیچ `letterheadPath = null;` در سرویس نیست
- هر پنج پیام لاگ مسیرهای شکست موجودند
- سه `PageColor` درست (قالب/کلاسیک/ساده)
- `withHeaderMeta: false` و `withLetterhead` سر جای خود هستند

اجرا:

```powershell
python -m unittest discover -s inventory/tests/OutgoingLetterhead -p "test_*.py"
```
