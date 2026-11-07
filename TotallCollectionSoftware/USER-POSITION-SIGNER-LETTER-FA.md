# سمت‌های کاربر، نمایش سمت در کمبوی امضاکنندگان و چاپِ نام/سمت/امضا در سمت چپ نامه

تاریخ: ۱۴۰۵/۰۷/۳۰ — شاخه: `arena/a33dacf2-totallcollectionsoftware`

این سند چهار خواستهٔ مطرح‌شده را توضیح می‌دهد: ریشهٔ هر ایراد، فایل‌های تغییرکرده و راه‌حل.

---

## خلاصهٔ خواسته‌ها

1. در تعریفِ دستیِ کاربر بتوان برایش **عنوان سمت** تعریف کرد؛ هر کاربر بتواند **چند سمت فعال** داشته باشد.
2. در انتخابِ **امضاکنندگانِ نامهٔ صادره**، «نام + سمت» (فقط اگر سمت تعریف شده باشد) در کمبو باکس نمایش داده شود و سرچ هم **با نام** و هم **با سمت** کار کند.
3. **نام، سمت و امضای** امضاکننده در **سمت چپِ انتهای نامه** قرار بگیرد و **امضا روی نام و سمت** بیفتد (Overlap).
4. همهٔ موارد بالا درست اجرا شوند.

---

## ۱) تعریفِ دستیِ سمت‌های کاربر (چند سمت فعال)

### وضعیتِ قبل

جدولِ `Semats` (سمت سازمانی) در هر دو موتور پایگاه‌داده (SQLite و SQL Server) از قبل
وجود داشت و کلیدِ خارجیِ `UserId` روی آن بود؛ یعنی داده‌مدل از قبل «هر کاربر چند
سمت» را پشتیبانی می‌کرد، اما **هیچ رابطِ کاربری یا API** برای ثبتِ سمتِ کاربر وجود
نداشت و مقدارِ `Semats.UserId` همیشه `null` می‌ماند.

### راه‌حل

**ساختارِ داده (بدون تغییرِ اسکیما)**

- `Inventory.Api/Entities/System/Semat.cs` — موجود: `SematId`, `UserId` (nullable FK)،
  `Title`, `OnvanMokatebati`, `OrganizationId` (NOT NULL)، `DefaultSemat`, `IsActive`, `IsDelete`.
- `Inventory.Api/Data/AppDbContext.cs` — `DbSet<Semat> Semats` و `DbSet<Organization> Organizations`.

**سرویس (API)**

- `Inventory.Shared/Dtos/AuthDtos.cs` → `UserDto.Positions` (نوع `List<string>?`) اضافه شد.
  - `null` یعنی «فرم سمت‌ها را نفرستاده» ⇒ سمت‌های کاربر دست‌نخورده می‌مانند
    (تا فراخوانی‌های دیگر مثل تغییر رمز از پنل شخصی، سمت‌ها را پاک نکنند).
- `Inventory.Api/Services/Core/AuthService.cs`:
  - `UserPositionMapAsync(userIds)` — نگاشتِ شناسهٔ کاربر → فهرستِ عنوانِ سمت‌های فعال
    (`!IsDelete && IsActive`)، بدون تکرار.
  - `DefaultOrganizationIdAsync()` — سازمانِ پیش‌فرض (سمت‌ها به آن نسبت داده می‌شوند).
    اگر سازمانی نبود، ساخته می‌شود؛ چون `NameUniq` ایندکسِ یکتا دارد، کدِ یکتا انتخاب می‌شود.
  - `SyncUserPositionsAsync(userId, titles)` — هم‌گام‌سازی:
    - سمت‌هایی که دیگر در فهرست نیستند **soft-delete** می‌شوند (`IsDelete=true`, `IsActive=false`, `DefaultSemat=false`).
    - سمت‌های جدید ساخته می‌شوند.
    - **اولین** سمتِ فعال به‌عنوان «سمتِ پیش‌فرض» (`DefaultSemat=true`) علامت می‌خورد
      (فقط یکی؛ بقیه `false`).
  - `GetUsersAsync()` فهرستِ سمت‌های هر کاربر را در `UserDto.Positions` پر می‌کند.
  - `SaveUserAsync()` پس از ذخیرهٔ کاربر، در صورتِ `dto.Positions is not null`، سمت‌ها را هم‌گام می‌کند.

**کلاینت**

- `Inventory.Client/Pages/System/Users.razor`:
  - در فرمِ «تعریف/ویرایش کاربر» بلوکِ «سمت‌های کاربر» اضافه شد: هر سطر یک `input` برای
    عنوانِ سمت + دکمهٔ حذف + دکمهٔ «افزودن سمت».
  - ستونِ «سمت‌ها» در جدولِ کاربران، سمت‌ها را به‌صورت `badge` نشان می‌دهد.
  - در `Save()` مقدار `form.Positions = CollectPositions()` (پاک‌سازی‌شده، بدون تکرار) ارسال می‌شود.
  - در `OpenNew`/`OpenEdit` ورودی‌ها از `u.Positions` پر می‌شوند (به‌همراه یک سطرِ خالی برای افزودن).

> **نکته:** عنوانِ سمت در `Semats.Title` ذخیره می‌شود؛ `OnvanMokatebati` (عنوانِ مکاتباتی)
> اگر مقدار داشته باشد، در نمایش‌ها بر `Title` اولویت دارد.

---

## ۲) کمبو باکسِ امضاکنندگان: نام + سمت و سرچ با نام و سمت

### وضعیتِ قبل

- `OutgoingLetterService.GetAvailableSignersAsync` فقط `UserId` و `FullName` را برمی‌گرداند
  و سرچ فقط روی `Username`/`FirstName`/`LastName` بود؛ سمتِ امضاکننده هیچ‌جا دیده نمی‌شد.
- کمبو (`LetterPickCombo.razor`) هم فقط `FullName` را نمایش می‌داد و سرچ آن فقط روی نام بود.

### راه‌حل

**سرویس (API)**

- `Inventory.Shared/Dtos/LetterDtos.cs` → `LetterReciverDto.Positions` (نوع `List<string>?`) اضافه شد
  (`SematId` و `SematTitle` از قبل وجود داشتند).
- `Inventory.Api/Services/Office/Outgoing/OutgoingLetterService.cs`:
  - سرچ سمت‌محور: در `GetAvailableSignersAsync` شرطِ جستجو با
    `_db.Semats.Any(x => x.UserId == u.Id && !x.IsDelete && x.IsActive && x.Title.Contains(s))`
    کامل شد ⇒ سرچ هم با نام و هم با عنوانِ سمت کار می‌کند.
  - پس از گرفتنِ لیست، با **یک** نگاشت (بدون N+1) سمت‌های هر کاربر پر می‌شود:
    - `UserPositionMapAsync(userIds)` → همهٔ سمت‌های فعال (`Positions`)
    - `DefaultSematIdsAsync(userIds)` → سمتِ پیش‌فرضِ هر کاربر (`SematId`)
    - `SematTitleMapAsync(sematIds)` → عنوانِ سمت (`SematTitle`)
  - هنگامِ ثبتِ نامهٔ صادره، `OutgoingLetterSigner.SematId` هم ثبت می‌شود
    (هم در مسیرِ «ایجاد»، هم در مسیرِ «افزودن امضاکننده») تا سمتِ هر امضاکننده در همان
    لحظه **تثبیت** شود و بعدها تغییراتِ سمتِ کاربر روی نامه‌های قدیمی اثر نگذارد.
  - در نگاشتِ جزئیاتِ نامه، `SematTitle` در `dto.Signers` و `dto.MySigner` پر می‌شود؛
    در `GetSignersAsync` هم با یک زیرپرس‌وجوی EF مقدارِ `SematTitle` محاسبه می‌شود.

**کلاینت**

- `Inventory.Client/Pages/Office/Letters/LetterPickCombo.razor`:
  - `PositionText(u)` — اگر کاربر چند سمت داشته باشد، همه با «، » جدا می‌شوند؛
    اگر سمتی نبود رشتهٔ خالی برمی‌گردد ⇒ **فقط وقتی سمت تعریف شده باشد** نمایش داده می‌شود.
  - `MatchesUserQuery(u, q)` — سرچ با نام (`FullName`)، با `SematTitle` و با هر کدام از
    `Positions` انجام می‌شود.
  - نمایش: در فهرستِ بازشونده، کنارِ نام یک `chip` سمت با آیکونِ کیفِ‌کار؛
    در حبابک‌های انتخاب‌شده هم نام + سمت نمایش داده می‌شود.
- `Inventory.Client/wwwroot/css/letters.css` → استایلِ `.ltx .copt .cpos`
  (رنگِ کم‌رنگ، فونتِ ۱۰ پیکسل، بریدنِ متن با `ellipsis`).

**نمایش در صفحاتِ دیگر (افزوده‌شده برای هماهنگی)**

- `OutgoingLetterView.razor` — کنارِ نامِ امضاکننده، سمت نمایش داده می‌شود.
- `OutgoingLetterCartable.razor` — کنارِ نام در چیپ‌ها و در فهرستِ «وضعیت امضا»، سمت نمایش داده می‌شود.

---

## ۳) چاپ: نام + سمت + امضا در سمت چپِ انتهای نامه، با امضا روی نام و سمت

### وضعیتِ قبل

- بلوکِ امضاکنندگان به‌صورت `Row` با ۳ کارت در هر ردیف رسم می‌شد (چیدمانِ شبکه‌ای) و
  همه‌جا راست‌به‌چپ بود؛ در نتیجه امضاکنندگان در کلِ عرضِ صفحه پخش می‌شدند.
- امضا **بالای** کارت و **جدا از** نام و سمت رسم می‌شد (نه روی آن).
- اگر سمتِ ثبت‌شده نبود، عبارتِ «سمت ثبت نشده» چاپ می‌شد.

### راه‌حل (فایل: `Inventory.Api/Services/Office/Outgoing/OutgoingLetterPrintService.cs`)

تنها مسیرِ زندهٔ چاپ همین سرویسِ QuestPDF است
(`StimulsoftOutgoingLetterPrintService` ثبت نشده و استفاده نمی‌شود).

**الف) منبعِ عنوانِ سمت**

- علاوه بر `sematTitles` (نگاشتِ `SematId → عنوان` برای سمتِ ثبت‌شده روی نامه)، نگاشتِ
  `sematTitlesByUser` اضافه شد: سمتِ فعالِ فعلیِ کاربر برای نامه‌های قدیمی که `SematId`
  ندارند.
- زنجیرهٔ نمایش: `سِمتِ ثبت‌شده روی نامه` → `سمتِ فعالِ فعلیِ کاربر` → «سمت ثبت نشده».

**ب) چیدمانِ جدید**

```
page.Content()                                  ← صفحهٔ راست‌به‌چپ (ContentFromRightToLeft)
└─ Column
   └─ Row  (ShowEntire)
      ├─ RelativeItem()                          ← فضای خالی، سمتِ راست
      └─ ConstantItem(signerBlockWidth)          ← بلوک، سمتِ چپِ فیزیکی
         └─ Column
            ├─ «امضاکنندگان»
            └─ برای هر امضاکننده: AddSignerCard(...)
```

- `signerBlockWidth = 200` (A4) / `140` (A5)، `signerBlockHeight = 72` / `54`.
- دلیلِ چپ‌بودن: در صفحهٔ RTL، `Row` آفستِ X را برعکس می‌کند ⇒ **آیتمِ اول نسبی در
  سمتِ راست و آیتمِ ثابتِ بعدی در سمتِ چپ** می‌نشیند (این با سورسِ QuestPDF 2024.12.3
  راستی‌آزمایی شده است).

**ج) هم‌پوشانیِ امضا روی نام و سمت**

```csharp
container.Height(signerBlockHeight).Layers(layers =>
{
    // لایهٔ اصلی: نام و سمت — چسبیده به پایینِ بلوک
    layers.PrimaryLayer().AlignBottom().Column(textColumn => { ... });

    // لایهٔ رویی: تصویرِ امضا — دقیقاً روی نام و سمت
    layers.Layer().AlignBottom().AlignRight()
        .Width(signatureBoxWidth).Height(signatureBoxHeight)
        .Image(signer.Signature).FitArea();
});
```

- در QuestPDF ترتیبِ کشیدن = ترتیبِ لایه‌هاست: لایه‌ای که **بعد** از لایهٔ اصلی تعریف
  شود، روی آن رسم می‌شود ⇒ امضا روی نام و سمت می‌افتد.
- نام و سمت راست‌چین در انتهای بلوک رسم می‌شوند و جعبهٔ امضا (`132×40` برای A4،
  `96×30` برای A5) در همان لبهٔ راستِ بلوک می‌افتد ⇒ **هم‌پوشانی واقعی**.
- `Image(...).FitArea()` نسبتِ تصویر را حفظ می‌کند و آن را در جعبه جا می‌دهد.

> **نکتهٔ مهم برای تست:** تصویرِ امضا از فایلِ PNG/JPG کاربر خوانده می‌شود
> (`User.SignaturePath`). اگر پس‌زمینهٔ تصویرِ امضا سفید و مات نباشد، بخشی از نام و
> سمت را می‌پوشاند — این همان چیزی است که خواسته شده («امضا روی نام و سمت»).

---

## فایل‌های تغییرکرده

| فایل | تغییر |
|---|---|
| `Inventory.Shared/Dtos/AuthDtos.cs` | افزودن `UserDto.Positions` |
| `Inventory.Shared/Dtos/LetterDtos.cs` | افزودن `LetterReciverDto.Positions` |
| `Inventory.Api/Services/Core/AuthService.cs` | نگاشت/هم‌گام‌سازی سمت‌ها + پرکردن `UserDto.Positions` |
| `Inventory.Api/Services/Office/Outgoing/OutgoingLetterService.cs` | سرچ با سمت، پرکردن سمت در کمبو، ثبت `SematId` امضاکننده، `SematTitle` در DTOها |
| `Inventory.Api/Services/Office/Outgoing/OutgoingLetterPrintService.cs` | چیدمانِ چپِ نامه + هم‌پوشانیِ امضا + منبعِ عنوانِ سمت |
| `Inventory.Client/Pages/System/Users.razor` | ویرایشگرِ سمت‌ها + ستونِ «سمت‌ها» در جدول |
| `Inventory.Client/Pages/Office/Letters/LetterPickCombo.razor` | نمایش و سرچِ سمت در کمبو |
| `Inventory.Client/Pages/Office/Letters/Outgoing/OutgoingLetterView.razor` | نمایشِ سمت کنارِ نامِ امضاکننده |
| `Inventory.Client/Pages/Office/Letters/Outgoing/OutgoingLetterCartable.razor` | نمایشِ سمت در چیپ‌ها و «وضعیت امضا» |
| `Inventory.Client/wwwroot/css/letters.css` | استایلِ `.cpos` |

---

## نحوهٔ تستِ دستی

1. **تعریفِ سمت:** «مدیریت کاربران ← کاربران ← ویرایش کاربر» → در بلوکِ «سمت‌های کاربر»
   دو سمت بنویسید (مثلاً «مدیر فنی» و «سرپرست تولید») و ذخیره کنید.
   در جدولِ کاربران، دو `badge` کنارِ نامِ کاربر دیده می‌شود.
2. **کمبوی امضاکننده:** «نامه‌های صادره ← ثبت نامهٔ جدید ← بخشِ امضاکنندگان» →
   نامِ کاربر کنارِ آن عنوانِ سمت دیده می‌شود. با تایپِ «فنی» (عنوانِ سمت) هم
   پیدا می‌شود، همان‌طور که با تایپِ نام.
3. **چاپ:** نامهٔ صادره را چاپ بگیرید ⇒ بلوکِ «امضاکنندگان» در **سمت چپِ پایینِ نامه**
   است، نام و سمت در آن و تصویرِ امضا **روی** نام و سمت افتاده است.
   (برای دیدنِ هم‌پوشانی باید برای کاربر امضا تعریف شده باشد.)

---

## محدودیتِ تأیید

در این محیط امکانِ اجرای `dotnet build` وجود ندارد (SDK و پکیج‌ها در دسترس نیست)،
بنابراین تأیید **ایستا** انجام شده است:

- توازنِ آکولاد/پرانتز/براکتِ هر فایلِ تغییرکرده با نسخهٔ پیشین مقایسه شد (همه متوازن).
- هیچ نمادِ حذف‌شده‌ای باقی نمانده (`signerRows`, `AddSignerRow`, `DefaultSematIdAsync` …).
- وجودِ همهٔ نمادها و پراپرتی‌های استفاده‌شده در منبع بررسی شد
  (`Db.Semats`, `Db.Organizations`, `Semat.Title/OnvanMokatebati/DefaultSemat/IsActive/IsDelete`,
  `UserDto.Positions`, `LetterReciverDto.Positions`, `OutgoingSignerDto.SematTitle`).
- رفتارِ چیدمان بر اساسِ سورسِ QuestPDF 2024.12.3 راستی‌آزمایی شد
  (`Row` در RTL آفستِ X را برمی‌گرداند، `Layers` به ترتیبِ کد رسم می‌شود،
  `Constrained`/`AspectRatio` در RTL جابه‌جا می‌شوند، هر `IContainer` فقط یک فرزند می‌پذیرد).

**لطفاً پس از دریافتِ تغییرات، یک‌بار `dotnet build` بگیرید.**
