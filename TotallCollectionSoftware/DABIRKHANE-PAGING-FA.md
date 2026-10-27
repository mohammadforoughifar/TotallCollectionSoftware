# صفحه‌بندی دبیرخانهٔ یکپارچه (صادره + وارده) — سمت سرور

تاریخ: ۱۴۰۵/۰۸/۰۴ · بر پایهٔ `main` عمومی (`e51a04b`)

## ۱) پاسخ به پرسش «آیا صفحه‌بندی دارد؟ سمت سرور است؟»

| فهرست | قبل از این تغییر |
|---|---|
| نامه‌های صادره (دبیرخانهٔ صادره) | **صفحه‌بندی نداشت** — `GetDabirkhaneAsync(BuildFilter())` بدون `skip/take`، یعنی فهرست کامل در یک پاسخ |
| نامه‌های وارده (دبیرخانهٔ یکپارچه) | **صفحه‌بندی سروری بود ولی ثابت روی صفحهٔ ۱** — `GetSentAsync/GetArchiveAsync(search, 1, 12)` و هیچ دکمهٔ صفحه در UI نبود ⇒ فقط ۱۲ ردیف اول دیده می‌شد |

زیرساخت سرور از قبل موجود بود: `GET api/outgoing-letters/dabirkhane` پارامترهای `skip`/`take` را می‌گیرد و با `Paging.ResultAsync(...)` پاسخ `{ total, items }` با `Skip/Take` سطح دیتابیس می‌دهد (بدون پارامتر ⇒ آرایهٔ کامل، سازگار با نسخهٔ قبل). یعنی «صفحه‌بندی سمت سرور» بود، فقط اتصال کلاینت/UI انجام نشده بود.

## ۲) تغییرات

### الف) نامه‌های صادره — `Pages/Office/Letters/Outgoing/OutgoingDabirkhane.razor`
- نوار صفحه‌بندی واقعی اضافه شد: «اول / قبلی / شماره‌ها / بعدی / آخر» + متن «N نامه — صفحهٔ X از Y».
- اندازهٔ صفحه: **۱۵ ردیف** (`DabirPageSize`).
- شمارندهٔ صفحات پنج‌پنجره‌ای (`DabirPageNumbers`) تا در فهرست‌های بزرگ نوار کوتاه بماند.
- تغییر تب، تغییر هر فیلتر جستجوی پیشرفته، `ClearFilters` و جستجو ⇒ بازگشت به **صفحهٔ ۱** (`ReloadFromFilterAsync`).
- اگر بعد از تغییر فیلتر صفحهٔ جاری از محدوده خارج شود، خودکار به آخرین صفحه برمی‌گردد.
- استایل از کلاس‌های موجود `letters.css` استفاده می‌کند: `.lt-pager` / `.pgbtn` / `.pginfo` (بدون CSS تازه).

### ب) سرویس کلاینت — `Services/OutgoingLetterServices.cs`
- متد تازه:
  ```csharp
  Task<PagedResult<DabirkhaneListItemDto>> GetDabirkhanePageAsync(DabirkhaneSearchDto? filter = null, int page = 1, int pageSize = 15);
  ```
  که `skip = (page-1)*pageSize` و `take = pageSize` را به همان endpoint می‌فرستد و با `ListOrPaged.GetPagedAsync` پاسخ `{ total, items }` را می‌خواند.
- ساخت رشتهٔ کوئری فیلترها به `BuildDabirkhaneQuery(filter)` منتقل شد تا فهرست کامل و صفحه‌بندی‌شده هر دو از یک منبع تغذیه شوند (بدون تکرار کد).
- `GetDabirkhaneAsync` قبلی دست‌نخورده مانده (برای مصارف دیگر) و همان آرایهٔ کامل را برمی‌گرداند.

### ج) نامه‌های وارده — `Pages/Office/Letters/Incoming/IntegratedIncomingPanel.razor`
- کنترل صفحه اضافه شد (صفحهٔ اول/قبلی/شماره/بعدی/آخر + «N نامه — صفحهٔ X از Y»).
- **صفحهٔ مستقل برای هر تب**: «ثبت‌شده‌ها» و «بایگانی‌شده‌ها» هر کدام `registeredPage` / `archivedPage` جدا دارند؛ جابه‌جایی در یکی روی دیگری اثر ندارد.
- فقط **تب فعال** با اندازهٔ صفحهٔ ۱۲ واکشی می‌شود؛ برای شمارندهٔ بالای صفحه، تب دیگر با `pageSize=1` فقط برای گرفتن `TotalCount` خوانده می‌شود (بدون هزینهٔ خواندن ردیف‌ها).
- جستجو ⇒ هر دو صفحه به ۱ برمی‌گردند؛ خروج صفحهٔ جاری از محدوده ⇒ اصلاح خودکار.
- استایل: `.iin-pager` / `.iin-mini.on` / `.iin-pginfo` درون `<style>` همان کامپوننت، هم‌خوان با ظاهر موجود.

### د) راهنما
`Pages/Office/Guides/DabirkhaneGuide.razor` در بخش «جستجو و فیلترها» یک «یادداشت» اضافه شد: صفحه‌بندی سروری هر دو فهرست، اندازهٔ صفحه (۱۵ صادره / ۱۲ وارده)، بازگشت به صفحهٔ ۱ با تغییر جستجو/فیلتر، و اینکه شمارنده‌ها کل نتایج را نشان می‌دهند نه صفحهٔ جاری.

## ۳) نکات پیاده‌سازی
- ترتیب سرور پیش از `Skip/Take` اعمال می‌شود (`OrderBy(DabirkhaneSabt).ThenByDescending(DateSadere).ThenByDescending(Id)`) ⇒ صفحه‌بندی ردیف‌ها را جابه‌جا/تکراری نمی‌کند.
- پاسخ صفحه‌بندی‌شده همیشه `{ total, items }` است؛ کلاینت با `ListOrPaged.NormalizePaged` هم شکل آرایه و هم شکل صفحه‌بندی‌شده را می‌فهمد.
- هیچ endpoint یا قرارداد API تغییر نکرد ⇒ سازگاری کامل با نسخه‌های دیگر کلاینت.

## ۴) فایل‌های تغییریافته
- `inventory/src/Inventory.Client/Pages/Office/Letters/Outgoing/OutgoingDabirkhane.razor`
- `inventory/src/Inventory.Client/Services/OutgoingLetterServices.cs`
- `inventory/src/Inventory.Client/Pages/Office/Letters/Incoming/IntegratedIncomingPanel.razor`
- `inventory/src/Inventory.Client/Pages/Office/Guides/DabirkhaneGuide.razor`
