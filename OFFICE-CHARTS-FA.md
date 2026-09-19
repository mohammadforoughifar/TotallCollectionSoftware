# چارت‌های اتوماسیون اداری (داخلی + صادره + وارده)

دستهٔ جدید **«اتوماسیون اداری»** به کاتالوگ ویجت‌های داشبورد شخصی اضافه شد.
کاربر در صفحهٔ «داشبورد من» → افزودن ویجت، این دسته را می‌بیند (مشروط به مجوز ماژول `InnerLetters`).

## ویجت‌های اضافه‌شده

| کلید | عنوان | نوع | چارت‌های مجاز | ابعاد |
|---|---|---|---|---|
| `off-letters-count` | نامه‌های ثبت‌شده | KPI | — | 3×2 |
| `off-letters-by-type` | نامه‌ها به تفکیک نوع | Chart | دایره‌ای، دونات، میله‌ای | 4×3 |
| `off-letters-trend` | روند نامه‌ها (۱۲ ماه، ۳ سری) | Chart | خطی، میله‌ای | 6×3 |
| `off-letters-by-status` | وضعیت نامه‌ها | Chart | دونات، دایره‌ای، میله‌ای | 4×3 |
| `off-letters-by-urgency` | فوریت نامه‌ها | Chart | دونات، دایره‌ای، میله‌ای | 4×3 |
| `off-erja-unread` | ارجاع‌های خوانده‌نشده (کاربر جاری) | KPI | — | 3×2 |

همه با بازهٔ زمانی (`HasRange`) کار می‌کنند و drilldown آن‌ها به `letters` می‌رود.

## فایل‌های تغییریافته
- `src/Inventory.Api/Services/Dashboards/WidgetCatalog.cs` — تعریف ۶ ویجت.
- `src/Inventory.Api/Services/Dashboards/WidgetDataService.cs` — ۶ `case` + کمکی‌های
  `LettersIn(from,to)`، `LetterTypeFa`، `OutStatusFa`، `UrgencyFa`.

## نکتهٔ فنی
همهٔ کوئری‌ها روی جدول مشترک **`LetterSources`** ریشه می‌گیرند و با نویگیشن‌های یک‌به‌یک
(`InnerLetter` / `OutgoingLetter` / `IncomingLetter`) هر سه نوع را پوشش می‌دهند.
از `Select().Concat()` استفاده نشده، چون EF Core آن را ترجمه نمی‌کند.

تاریخ ثبت هر نوع: داخلی و صادره = `DateSabt`، وارده = `DateErsal`.
فوریت در داخلی/صادره رشته و در وارده عدد است؛ هر دو به متن فارسی یکسان نرمال می‌شوند.

## نتایج تست زنده (دیتابیس تست: ۱۲ نامه، ۱۳ ارجاع)

**نامه‌ها به تفکیک نوع** (دایره‌ای): داخلی ۴ | صادره ۳ | وارده ۵
**روند نامه‌ها** (خطی، شهریور ۱۴۰۵): داخلی ۴ | صادره ۳ | وارده ۵
**وضعیت نامه‌ها**: ثبت‌شده ۴ | در جریان ۴ | پیش‌نویس ۱ | در گردش تایید ۱ | صادر شده ۱ | بایگانی‌شده ۱
**فوریت نامه‌ها**: عادی ۶ | فوری ۴ | خیلی فوری ۱ | آنی ۱
**نامه‌های ثبت‌شده** (KPI): ۱۲ نامه
**ارجاع‌های خوانده‌نشده** (KPI): ۷ مورد

بیلد: `Build succeeded — 0 Error`.

---

# رفع خطای «Invalid object name 'IncomingLetters'»

## علت
موجودیت `IncomingLetter` **بعد از** مایگریشن `20260911100108_SquashedInitial` به پروژه اضافه شده
و هیچ مایگریشنی جدول `IncomingLetters` را نمی‌سازد. (در همان مایگریشن `LetterSources`،
`InnerLetters` و `OutgoingLetters` ساخته می‌شوند — ولی `IncomingLetters` نه.)

روی SQLite مشکلی دیده نمی‌شد چون `EnsureCreated()` جدول را از روی مدل می‌سازد؛
اما روی **SQL Server** که `Migrate()` اجرا می‌شود، جدول ساخته نمی‌شود و نتیجه خطای
`Invalid object name 'IncomingLetters'` در کارتابل وارده، گزارش «همه نامه‌ها» و
ویجت‌های چارت اتوماسیون اداری است.

## راه‌حل
فایل خودترمیم `src/Inventory.Api/Data/IncomingLetterSchemaV1.cs` اضافه شد
(هم‌الگو با `WorkOrderSchemaV1` / `OfficeEmailSchemaV1`):
- نسخهٔ **SQL Server**: `IF OBJECT_ID(...) IS NULL CREATE TABLE ...` + کلید خارجی به
  `LetterSources` با `ON DELETE CASCADE` + ۵ ایندکس + ترمیم ستون‌های جامانده با `COL_LENGTH`.
- نسخهٔ **SQLite**: `CREATE TABLE IF NOT EXISTS ...` + همان ایندکس‌ها.
- `EnsureOnceAsync` و `TableExistsAsync` برای فراخوانی ایمن و راستی‌آزمایی.

فراخوانی در دو نقطه:
1. `Data/DbInitializer.cs` — هنگام بالا آمدن برنامه، همراه با لاگ صریح
   `[DB] ✔ جدول نامه وارده (IncomingLetters) آماده است.`
2. `Controllers/Dashboards/DashboardsController.cs` → `EnsureDashDbAsync()` — خودترمیمی
   هنگام باز کردن داشبورد.

همچنین در `WidgetDataService` پیام خطا برای `Invalid object name` / `no such table`
به یک راهنمای فارسی قابل‌فهم تبدیل شد، به‌جای نمایش خطای خام SQL.

## تست انجام‌شده
جدول `IncomingLetters` **عمداً حذف شد** (پس از پشتیبان‌گیری از ۵ ردیف)، سرویس ری‌استارت شد:
- لاگ: `[DB] ✔ جدول نامه وارده (IncomingLetters) آماده است.` ← جدول خودکار بازساخته شد
- ستون‌های بازساخته‌شده دقیقاً منطبق با موجودیت (۱۹ ستون)
- پس از بازگرداندن داده، همهٔ ۶ ویجت و گزارش `letters-all` با `error: None` پاسخ دادند.

## فایل‌های تغییریافته (این مرحله)
- `Data/IncomingLetterSchemaV1.cs` (جدید)
- `Data/DbInitializer.cs`
- `Controllers/Dashboards/DashboardsController.cs`
- `Services/Dashboards/WidgetDataService.cs`

---

# رفع مشکل درگ‌اند‌دراپ (جابه‌جا نشدن ویجت‌ها در داشبورد)

## علت
کد درگ‌اند‌دراپ **کامل و درست** بود (`dashboard.js` رویدادها را می‌بندد، `DashWidgetCard`
صفت‌های `draggable` و `data-wid` را دارد، و `OnReorder` / `OnResize` با `[JSInvokable]`
آمادهٔ فراخوانی‌اند). مشکل فقط **زمان‌بندی اتصال** بود:

```csharp
if (firstRender && Auth.CanSee("MyDashboards"))
    await JS.InvokeVoidAsync("appDash.bind", "dashGrid", selfRef);
```

`bind` فقط یک‌بار و در **اولین رندر** اجرا می‌شد. اما عنصر `<div id="dashGrid">`
داخل شرط `@if (widgets.Count == 0) {...} else {...}` است و در اولین رندر هنوز
وجود ندارد (داده‌ها با `await` بعداً می‌آیند). در نتیجه:

```js
const root = document.getElementById(rootId);
if (!root) return;   // بی‌صدا خارج می‌شد — هیچ رویدادی بسته نمی‌شد
```

چون `bind` هیچ مقداری برنمی‌گرداند، سمت Blazor هم متوجه شکست نمی‌شد و دیگر
هرگز تلاش مجدد نمی‌کرد. نتیجه: کارت‌ها `draggable="true"` بودند ولی هیچ
listener‌ای وجود نداشت، پس درگ هیچ اثری نداشت.

## راه‌حل
1. **`dashboard.js`** — `bind` حالا نتیجه برمی‌گرداند:
   `false` اگر گرید در DOM نباشد، `true` در صورت موفقیت (و `true` برای بایند تکراری).
2. **`MyDashboards.razor`** — به‌جای تلاش یک‌باره، تا موفق شدن در هر رندر دوباره تلاش می‌کند:
   ```csharp
   if (widgets.Count == 0) { gridBound = false; return; }
   if (gridBound) return;
   gridBound = await JS.InvokeAsync<bool>("appDash.bind", "dashGrid", selfRef);
   ```
   ریست شدن `gridBound` هنگام خالی شدن داشبورد لازم است، چون Blazor در آن حالت
   خود عنصر گرید را از DOM حذف می‌کند و عنصر بعدی تازه است.
3. **`index.html`** — نسخهٔ اسکریپت از `?v=1` به `?v=2` رفت تا کش مرورگر
   نسخهٔ قدیمی را تحویل ندهد.

## نکتهٔ کاربردی
درگ فقط در **حالت «طراحی داشبورد»** فعال است (`draggable="@(Editing ? "true" : "false")"`).
برای جابه‌جایی: دکمهٔ «طراحی داشبورد» → کارت را از دستگیرهٔ ⋮⋮ بکشید → «ذخیرهٔ چیدمان».
تغییر اندازه هم با کشیدن گوشهٔ کارت انجام می‌شود.

## فایل‌های تغییریافته (این مرحله)
- `src/Inventory.Client/wwwroot/js/dashboard.js`
- `src/Inventory.Client/Pages/Dashboards/MyDashboards.razor`
- `src/Inventory.Client/wwwroot/index.html`

بیلد کلاینت: `Build succeeded — 0 Error`.

---

# ویجت‌های اختصاصی نامه صادره و نامه داخلی

پیش‌تر ویجت‌ها «هر سه نوع با هم» را نشان می‌دادند. حالا برای هر کدام از
**نامه صادره** و **نامه داخلی** ویجت‌های اختصاصی اضافه شد
(جمعاً **۱۸ ویجت** در دستهٔ «اتوماسیون اداری»).

## نامه صادره

| کلید | عنوان | نوع | توضیح |
|---|---|---|---|
| `off-out-count` | نامه‌های صادره | KPI | تعداد در بازه + مقایسه با بازهٔ قبل |
| `off-out-pending` | صادره در انتظار صدور | KPI | پیش‌نویس یا در گردش تایید |
| `off-out-by-status` | وضعیت نامه‌های صادره | Chart | دونات/دایره‌ای/میله‌ای |
| `off-out-by-method` | روش ارسال صادره | Chart | پست، ایمیل، پیک، نمابر … |
| `off-out-trend` | روند نامه‌های صادره | Chart | دو سری: ثبت‌شده در برابر صادرشده |
| `off-out-top-receivers` | گیرندگان پرمکاتبه | Table | با گزینهٔ «تعداد ردیف» |

## نامه داخلی

| کلید | عنوان | نوع | توضیح |
|---|---|---|---|
| `off-inner-count` | نامه‌های داخلی | KPI | تعداد در بازه + مقایسه |
| `off-inner-overdue` | نامه‌های داخلی معوق | KPI | مهلت پاسخ گذشته و بی‌پاسخ |
| `off-inner-by-urgency` | فوریت نامه‌های داخلی | Chart | عادی/فوری/آنی |
| `off-inner-by-conf` | محرمانگی نامه‌های داخلی | Chart | عادی/محرمانه/سری |
| `off-inner-trend` | روند نامه‌های داخلی | Chart | ۱۲ ماه شمسی |
| `off-inner-top-senders` | پرمکاتبه‌ترین ثبت‌کنندگان | Table | با گزینهٔ «تعداد ردیف» |

## نکتهٔ فنی
کمکی‌های `OutLetters()` / `OutLettersIn()` و `InnerLetters()` / `InnerLettersIn()` اضافه شدند.
همه از مسیر `LetterSources` با شرط `!x.IsDelete` عبور می‌کنند تا **حذف منطقی** رعایت شود —
یعنی مستقیم روی `_db.OutgoingLetters` کوئری نمی‌زنیم.

در `off-out-trend` دو سری جداست: گروه‌بندی روی `DateSabt` (ثبت) و روی `DateSadere` (صدور)،
که اختلاف بین ثبت و صدور واقعی را نشان می‌دهد.

## نتایج تست زنده

```
off-out-count          KPI=۳ نامه
off-out-pending        KPI=۲ نامه
off-out-by-status      پیش‌نویس ۱ | در گردش تایید ۱ | صادر شده ۱
off-out-by-method      ایمیل ۱ | پست ۱ | پیک ۱
off-out-trend          ثبت‌شده: شهریور ۳  |  صادرشده: شهریور ۱
off-out-top-receivers  شرکت طرف قرارداد ۱/۲/۳ — هرکدام ۱
off-inner-count        KPI=۴ نامه
off-inner-overdue      KPI=۱ نامه
off-inner-by-urgency   عادی ۲ | فوری ۲
off-inner-by-conf      عادی ۲ | محرمانه ۲
off-inner-trend        شهریور ۴
off-inner-top-senders  کاربر ۱ — ۴ نامه
```

هر ۱۲ ویجت با `error: None`. کاتالوگ ۱۸ ویجت را در دستهٔ «اتوماسیون اداری» برمی‌گرداند.
بیلد: `Build succeeded — 0 Error`.
