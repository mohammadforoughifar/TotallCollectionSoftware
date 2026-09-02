# 📁 ماژول اتوماسیون اداری — نامه داخلی (کارتابل، ارجاع، پیش‌نویس، گروه‌ها)

این سند بخش **اتوماسیون اداری** است که از مخزن
[Otomation-Hemmati](https://github.com/mohammadforoughifar/Otomation-Hemmati.git)
به سورس جدید (`TotallCollectionSoftware`) منتقل شده است.

ورودیِ کار: سرویس‌ها و موجودیت‌های قدیمیِ اتوماسیون (نامه داخلی، ارجاع، پیش‌نویس، بایگانی،
گروه‌ها، عملگرها، نامه‌های مرتبط) که با معماری این پروژه (کاربر `int`، RBAC، پیوست یکپارچه،
اعلان لحظه‌ای SignalR، تاریخ شمسی) بازنویسی شده‌اند.

---

## ۱. دسترسی‌ها (RBAC)

یک ماژول دسترسی جدید به سیستم نقش‌ها اضافه شده است
(**تنظیمات → نقش‌ها و دسترسی‌ها**، دسته‌ی «اتوماسیون اداری»):

| ماژول | نام فارسی | اکشن‌ها |
|---|---|---|
| `InnerLetters` | نامه داخلی (اتوماسیون اداری) | `Create` ثبت/ارسال نامه، مدیریت پیش‌نویس و گروه‌ها · `Read` کارتابل/مشاهده/دانلود پیوست · `Erja` ارجاع نامه به دیگران · `Delete` حذف نامه و گروه (مدیرانه) |

- پرمیشن‌ها هنگام اجرای برنامه به‌صورت خودکار سید می‌شوند و به نقش **Admin** تخصیص می‌یابند.
- کاربران قدیمی با نقش `Operator` (بدون نقش RBAC): ایجاد + مشاهده + ارجاع دارند؛ حذف ندارند.
- گارد مرکزی مسیرها: مسیر `letters` بدون داشتن حداقل یک دسترسی از `InnerLetters` بسته است.
- منوی «اتوماسیون اداری» فقط برای کاربرانی که `InnerLetters` دارند نمایش داده می‌شود و
  **تعداد نامه‌های نخوانده** را به‌صورت نشان قرمز کنار آیتم نشان می‌دهد (به‌روزرسانی بلادرنگ).

## ۲. موجودیت‌ها و جداول جدید

همه در `Inventory.Api/Entities/Office/OfficeAutomation.cs` (فضای نام `Inventory.Api.Data`):

| موجودیت | جدول | توضیح |
|---|---|---|
| `LetterSource` | `LetterSources` | کلید مرجع مشترک همه‌ی انواع نامه (معادل `SourceKeyID` طرح قدیم) — `SourceType`: ۱=داخلی، ۲=صادره، ۳=وارده |
| `InnerLetter` | `InnerLetters` | نامه داخلی — شناسه‌اش همان `LetterSource` است |
| `Erja` | `Erjas` | ارجاع/گردش نامه — هر گیرنده یک رکورد دارد (`گیرنده` / `ارجاع` / `هامش`) |
| `Amalgar` | `Amalgars` | عملگر ارجاع (جهت اطلاع / اقدام / تایید و امضا / …) |
| `PishnevisLetter` | `PishnevisLetters` | پیش‌نویس نامه |
| `RelatedLetter` | `RelatedLetters` | نامه‌های مرتبط: عطف (۲) و پیرو (۱) |
| `LetterBayegani` | `LetterBayeganis` | بایگانی درختی (ساختار جدول آماده؛ فعلاً از `Erja.IsBayegani` استفاده می‌شود) |
| `LetterGroup` / `LetterGroupMember` | `LetterGroups` / `LetterGroupMembers` | گروه‌های گیرندگان (پورت جدول `Groups` طرح قدیم) |

ستون‌های `SematId` برای فاز «چارت سازمانی» به‌صورت nullable آماده‌اند.

مایگریشن: `20260831103000_AddOfficeAutomation` (اجرای خودکار هنگام بالا آمدن برنامه).

## ۳. شماره‌گذاری نامه

```
شماره اندیکاتور = «سال شمسی/شماره ترتیبی»   مانند   1404/12
```
شمارنده در هر سال شمسی از ۱ شروع می‌شود.

## ۴. API — `api/letters`

| متد | مسیر | توضیح |
|---|---|---|
| GET | `api/letters/inbox?search=&unreadOnly=` | صندوق وارده |
| GET | `api/letters/sent?search=` | ارسالی‌ها |
| GET | `api/letters/archive?search=` | بایگانی‌شده‌ها |
| GET | `api/letters/stats` | شمارنده‌ها (نخوانده، کل، ارسالی، پیش‌نویس، نزدیک مهلت) |
| GET | `api/letters/{id}` | جزئیات نامه (فقط فرستنده/گیرندگان/مدیر) |
| POST | `api/letters` | ثبت و ارسال نامه |
| PUT | `api/letters/{id}` | ویرایش نامه (فقط تا قبل از خوانده‌شدن توسط هر گیرنده) |
| DELETE | `api/letters/{id}` | حذف نرم |
| GET | `api/letters/pick?search=` | انتخاب نامه برای عطف/پیرو |
| GET | `api/letters/{id}/gardesh` | درخت گردش نامه |
| POST | `api/letters/erja` | ارجاع/هامش به کاربران یا گروه‌ها |
| POST | `api/letters/erja/{erjaId}/answer` | ثبت پاسخ + تایید/رد |
| POST | `api/letters/erja/{erjaId}/read` | ثبت خوانده‌شدن |
| POST | `api/letters/erja/{erjaId}/neshan` | نشان‌کردن/برداشتن نشان |
| POST | `api/letters/erja/{erjaId}/bayegani` | بایگانی / خروج از بایگانی |
| GET | `api/letters/amalgars` | فهرست عملگرها |
| GET/POST/DELETE | `api/letters/pishnevis[/{id}]` | پیش‌نویس‌ها |
| GET | `api/letters/recivers` | کاربران فعال برای انتخاب گیرنده |
| GET/POST/DELETE | `api/letters/groups[/{id}]` | گروه‌های گیرندگان |
| GET/POST | `api/letters/{id}/attachments` | پیوست‌های نامه (هر فایل ≤ ۲۰ مگابایت) |
| GET/POST | `api/letters/pishnevis/{id}/attachments` | پیوست‌های پیش‌نویس |
| GET | `api/letters/attachments/{attId}/download` | دانلود پیوست |
| DELETE | `api/letters/attachments/{attId}` | حذف پیوست (فقط بارگذارنده یا مدیر) |

## ۵. صفحه‌ی کارتابل — `/letters`

- **پوشه‌ها:** وارده · ارسالی · پیش‌نویس · نشان‌شده · بایگانی
- **فیلترها:** همه / نشان‌شده / مهلت‌دار / پیوست‌دار + جستجو + صفحه‌بندی
- **نمایش سه‌ستونه:** ریل پوشه‌ها · لیست نامه‌ها · پنل خواندن نامه
- **عملیات هر نامه:** مشاهده، پاسخ (متنی / تایید / رد)، ارجاع یا هامش، پیگیری گردش،
  چاپ، بایگانی/خروج از بایگانی، ویرایش (قبل از خوانده‌شدن)، حذف
- **فرم ایجاد نامه (شیت کناری):**
  - سه نوع گیرنده: **گیرنده** (الزامی)، **ارجاع** (جهت اقدام)، **هامش** (رونوشت)
  - انتخاب به‌صورت **انفرادی یا گروهی**
  - فوریت (عادی/فوری/آنی) و محرمانگی (عادی/محرمانه/سری)
  - ادیتور متن غنی
  - پیوست با دراپ‌زون (هر فایل حداکثر ۲۰ مگابایت، فایل خالی رد می‌شود)
  - عطف/پیرو با انتخاب از نامه‌های دریافتی و ارسالی
  - ذخیره‌ی پیش‌نویس، ادامه‌ی پیش‌نویس (پیوست‌های پیش‌نویس به نامه منتقل می‌شود)
- **گردش نامه (Gardesh):** نمایش درختیِ کل مسیر ارجاع‌ها با وضعیت خواندن/پاسخ/تایید و
  مخفی‌ماندن پاسخ‌های خصوصی از افراد غیرمرتبط.

## ۶. اعلان‌ها

هر رخداد (نامه جدید، ارجاع جدید، پاسخ به ارجاع) با `INotifyService` اعلان شخصی می‌فرستد و
با `datachanged` روی کلید `letters` کارتابل‌ها و شمارنده‌ی منو را بلادرنگ به‌روز می‌کند.

## ۷. فایل‌های افزوده/تغییریافته

**API**
- `Entities/Office/OfficeAutomation.cs` (جدید)
- `Services/Office/InnerLetterService.cs` · `ErjaService.cs` · `PishnevisService.cs` · `LetterGroupService.cs` (جدید)
- `Controllers/Office/InnerLettersController.cs` (جدید)
- `Data/AppDbContext.cs` (DbSetها و پیکربندی رابطه‌ها)
- `Data/RbacSeeder.cs` (ماژول `InnerLetters`)
- `Data/DbInitializer.cs` (عملگرهای پیش‌فرض ارجاع + داده‌ی دمو)
- `Program.cs` (ثبت سرویس‌ها)
- `Migrations/20260831103000_AddOfficeAutomation.*` و به‌روزرسانی `AppDbContextModelSnapshot`

**Shared**
- `Dtos/LetterDtos.cs` (جدید)

**Client**
- `Services/LetterServices.cs` (جدید)
- `Pages/Office/Letters/`: `LetterCartable.razor` (`/letters`)، `LetterView.razor` (`/letters/view/{id}`)،
  `ComposeLetterModal.razor`، `ErjaSheet.razor`، `GardeshModal.razor`، `LetterPickCombo.razor`
- `wwwroot/css/letters.css` + لینک آن در `index.html` (کلاینت و API) و تابع `ltxPrint`
- `Layout/NavMenu.razor` (گروه منوی جدید + نشان نخوانده)
- `Layout/MainLayout.razor` (شمارنده‌ی نامه‌های نخوانده + گارد مسیر)
- `Pages/Settings/Roles.razor` (دسته و ترجمه‌ی دسترسی‌های جدید)
- `Program.cs` (ثبت `ILetterService`)
- `Extensions/PersianExtensions.cs` (بازیابی `ToFaDate` / `FaDigits` / `HoursFa` / `TimeFa`)

## ۸. اجرا

1. برنامه را اجرا کنید — دیتابیس به‌صورت خودکار مایگریت می‌شود (`RUN.ps1` یا `dotnet run`).
2. با کاربر Admin وارد شوید؛ دسترسی‌های ماژول به‌صورت پیش‌فرض داده شده‌اند.
3. برای سایر کاربران: **تنظیمات → نقش‌ها و دسترسی‌ها → اتوماسیون اداری**.

## ۹. موارد آماده برای فاز بعد

- **چارت سازمانی / سمت‌ها (`Semat`):** ستون‌های `SematId` در همه‌ی جدول‌ها آماده است.
- **بایگانی درختی:** جدول `LetterBayeganis` آماده است؛ فعلاً بایگانی با `Erja.IsBayegani` انجام می‌شود.
- **نامه صادره و وارده:** با `LetterSource.SourceType` (۲ و ۳) بدون تغییر ساختار قابل افزودن است.
- **مدیریت گروه‌های گیرندگان:** API کامل است (`api/letters/groups`)؛ رابط کاربری آن بعداً اضافه می‌شود.

---

# 📤 فاز دوم — نامه صادره، دبیرخانه و چاپ روی سربرگ (به‌روزرسانی ۱۴۰۵/۰۶/۱۱)

## ۱. تغییرات این فاز (طبق درخواست کارفرما)

| # | درخواست | پیاده‌سازی |
|---|---------|-----------|
| ۱ | ثبت نامه صادره حداقل یک امضا کننده داشته باشد | اعتبارسنجی در سرور (`OutgoingLetterService.Add/Edit`) + فرم کلاینت (خطای «نامه صادره باید حداقل یک امضا کننده داشته باشد») |
| ۲ | حذف بخش نامه صادره قدیم | `Letter_Sadere` (موجودیت، سرویس، کنترلر، DTO) و لینک منوی «نامه صادره (قدیم)» به‌طور کامل حذف شد |
| ۳ | دبیرخانه نامه صادره — فقط نامه‌های امضا شده (`SadereNumber` دار) | صفحه `/outgoing-letters/dabirkhane` + API `api/outgoing-letters/dabirkhane` — تا وقتی همه امضا نکرده‌اند نامه وارد دبیرخانه نمی‌شود |
| ۴ | ثبت شماره مقصد در دبیرخانه | فیلد `DestRegNumber` (شماره ثبت دبیرخانه سازمان مقصد) در فرم «ثبت و ارسال» |
| ۵ | نمایش نامه | صفحه اختصاصی `/outgoing-letters/view/{id}` — متن کامل، امضا کنندگان، پیوست‌ها، وضعیت دبیرخانه + دکمه چاپ |
| ۶ | روش ارسال نامه از دبیرخانه | فیلد `SendMethod`: پست / پست پیشتاز / پیک / ایمیل / فکس / تحویل حضوری / اتوماسیون (ECE) |
| ۷ | چاپ A4 و A5 روی سربرگ شرکت (PDF از مسیر روت API) | `GET api/outgoing-letters/{id}/print?size=A4|A5` — QuestPDF (متن فارسی RTL) + PDFsharp (قرار دادن روی سربرگ) |

## ۲. سربرگ شرکت (چاپ)

- در **اطلاعات پایه → کمپانی** برای هر شرکت فیلد جدید «**فایل سربرگ (PDF)**» اضافه شد
  (`SystemCompanies.LetterheadFileName`).
- فایل PDF سربرگ باید در **مسیر روت API** قرار گیرد (روت پروژه در توسعه یا کنار فایل اجرایی در انتشار).
- هنگام ایجاد نامه صادره، «شرکت صادرکننده (سربرگ چاپ)» انتخاب می‌شود (`OutgoingLetters.CompanyId`)؛
  چاپ همان نامه روی سربرگ همان شرکت انجام می‌شود.
- اگر شرکت/سربرگ تعریف نشده باشد، PDF نامه بدون پس‌زمینه تولید می‌شود.

## ۳. ستون‌های جدید (مایگریشن `20260902100000_AddOutgoingDabirkhaneAndLetterhead`)

| جدول | ستون | توضیح |
|---|---|---|
| `OutgoingLetters` | `CompanyId` | شرکت صادرکننده — سربرگ چاپ |
| `OutgoingLetters` | `DabirkhaneSabt` | ثبت شده در دبیرخانه؟ |
| `OutgoingLetters` | `DabirkhaneUserId` · `DateDabirkhane` | کاربر و تاریخ ثبت دبیرخانه |
| `OutgoingLetters` | `DestRegNumber` | **شماره ثبت مقصد** |
| `OutgoingLetters` | `SendMethod` | **روش ارسال** از دبیرخانه |
| `OutgoingLetters` | `DabirkhaneNote` | توضیح دبیرخانه |
| `SystemCompanies` | `LetterheadFileName` | نام فایل PDF سربرگ در مسیر روت API |

## ۴. API های جدید — `api/outgoing-letters`

| متد | مسیر | توضیح |
|---|---|---|
| GET | `dabirkhane?search=&registeredOnly=` | لیست دبیرخانه (فقط نامه‌های امضا شده) |
| GET | `dabirkhane/stats` | آمار: در انتظار ثبت / ثبت شده |
| POST | `{id}/dabirkhane` | ثبت و ارسال: `{ destRegNumber, sendMethod, note }` |
| GET | `companies` | شرکت‌های فعال برای انتخاب سربرگ |
| GET | `{id}/print?size=A4یاA5` | PDF نامه روی سربرگ شرکت |

## ۵. دسترسی جدید (RBAC)

- `OutgoingLetters.Dabirkhane` — «دبیرخانه نامه صادره 📮»: مشاهده دبیرخانه، ثبت شماره مقصد و روش ارسال.
  (به‌صورت خودکار سید و به نقش Admin داده می‌شود.)

## ۶. پکیج جدید

- `PDFsharp 6.1.1` در `Inventory.Api` — برای قرار دادن صفحات نامه روی PDF سربرگ.

---

# 📥 فاز سوم — پورت تغییرات «نامه داخلی» از Otomation-Hemmati (به‌روزرسانی ۱۴۰۵/۰۶/۱۱)

تغییرات جدید نامه داخلی از ریپوی `mohammadforoughifar/Otomation-Hemmati` (شاخه `main`،
کامیت `13606bc` — ۲۰۲۶/۰۹/۰۲) روی این ریپو اعمال شد. ساختار پوشه‌بندی این پروژه
(`Entities/Office`, `Services/Office`, `Controllers/Office`, `Pages/Office/Letters`) حفظ شده و
تغییرات اسکیمایی به‌صورت **سه مایگریشن مجزا** اضافه شده‌اند.

## ۱. ساختار شماره اندیکاتور (`LetterStrature`)

| مایگریشن | `20260902110000_AddLetterStrature` |
|---|---|
| جدول جدید | `LetterStratures` (`StratureId`, `TypeForm`, `TypeStrature`) + ایندکس روی `TypeForm` |

- هر ردیف یک جزء ساختار است؛ **ترتیب ردیف‌ها ترتیب اجزا را تعیین می‌کند**.
- `TypeForm`: ۱=نامه داخلی، ۲=صادره، ۳=وارده.
- `TypeStrature`: `واحد` | `شماره` | `سال` — وجود «شماره» الزامی است.
- خروجی نمونه با ترتیب واحد/شماره/سال: `MQ/1/1405`.
- جزء «واحد» تا پورت‌نشدن چارت سازمانی از `appsettings.json → Letters:UnitCode` خوانده می‌شود
  (پیش‌فرض `MQ`). بعد از پیاده‌سازی چارت، همان‌جا با `GetOrganizationNameUniqAsync(sematId)`
  جایگزین می‌شود — امضای متد از الان `sematId` را می‌پذیرد.
- `DbInitializer` ساختار پیش‌فرض (واحد/شماره/سال) را سید می‌کند و شماره‌ی نمایشی نامه‌های
  قدیمی را **یک‌بار و به‌صورت idempotent** با ساختار جدید بازسازی می‌کند
  (شماره‌ی ترتیبی `Number` دست‌نخورده می‌ماند).

> ⚠️ اگر ساختاری تعریف نشده باشد، `InnerLetterService` به فرمت قبلی این پروژه
> (`سال/شماره` مثل `1404/12`) برمی‌گردد تا شماره‌ی نامه‌های موجود بی‌پیکربندی تغییر نکند.

## ۲. نشان (ستاره) سمت ارسالی

| مایگریشن | `20260902120000_AddLetterNeshan` |
|---|---|
| ستون جدید | `InnerLetters.IsNeshan` (`bit`, NOT NULL, default `false`) |

- تا پیش از این فقط گیرنده می‌توانست نامه را نشان کند (روی `Erja.IsNeshan`).
- اکنون فرستنده هم می‌تواند نامه‌ی ارسالی خودش را نشان کند (روی خود نامه).

| متد | مسیر | توضیح |
|---|---|---|
| POST | `api/letters/{id}/neshan` | نشان/برداشتن نشان نامه ارسالی توسط فرستنده |

پوشه‌ی «نشان‌شده‌ها» اکنون اجتماع دریافتی‌های نشان‌شده و ارسالی‌های نشان‌شده است.

## ۳. بایگانی درختی نامه‌ها

| مایگریشن | `20260902130000_AddBayeganiLetterId` |
|---|---|
| ستون جدید | `LetterBayeganis.LetterId` (`int`, NULL) — برای بایگانی نامه ارسالی توسط فرستنده که ارجاعی ندارد |

سرویس جدید `Services/Office/ArchiveService.cs` (`IArchiveService`) — پورت `ArchiveServices` کارفرما:

| متد | مسیر | توضیح |
|---|---|---|
| GET | `bayegani/tree` | درخت کامل بایگانی کاربر (پوشه‌ها + نامه‌ها) |
| POST | `bayegani/main-category` | ایجاد دسته اصلی (ریشه) |
| POST | `bayegani/sub-category` | ایجاد زیرپوشه |
| PUT | `bayegani/folder/{id}` | ویرایش عنوان پوشه |
| POST | `bayegani/move-folder/{id}?newParentId=` | جابجایی پوشه (با جلوگیری از حلقه) |
| POST | `bayegani/letters` | بایگانی یک یا چند نامه (`ErjaIds` و/یا `LetterIds`) |
| POST | `bayegani/move-letter/{id}?newParentId=` | جابجایی نامه بایگانی‌شده |
| DELETE | `bayegani/{id}` | حذف پوشه خالی / خروج نامه از بایگانی (Soft Delete) |
| POST | `bayegani/unarchive/{erjaId}` | خروج نامه دریافتی از بایگانی |
| POST | `bayegani/unarchive-letter/{letterId}` | خروج نامه ارسالی از بایگانی |

- نامه‌های ارسالیِ بایگانی‌شده دیگر در پوشه‌ی «ارسالی» فهرست نمی‌شوند (فقط در درخت بایگانی).
- مهاجرت تنبل: نامه‌هایی که با toggle قدیمی بایگانی شده‌اند ولی رکورد درختی ندارند،
  در اولین بازکردن درخت به ریشه منتقل می‌شوند.
- اجزای UI جدید: `BayeganiTree.razor`, `BayeganiFolderPickerModal.razor`,
  `LetterConfirmModal.razor`, `PishnevisModal.razor`.

## ۴. پیوست نامه

- **قاعده مصوب:** فقط **فرستنده** نامه می‌تواند پیوست اضافه کند
  (پیش‌تر: هر فرد در گردش یا مدیر).
- افزودن/حذف پیوست از داخل صفحه‌ی خواندن نامه (فقط فرستنده).
- **رفع باگ:** لینک دانلود پیوست (`<a href>`) هدر `Authorization` نمی‌فرستاد و `401` می‌گرفت.
  اکنون توکن با `?access_token=` ارسال می‌شود و `JwtBearerEvents.OnMessageReceived`
  آن را فقط برای مسیرهای شامل `/download` می‌پذیرد.
- ذخیره‌سازی پیوست **مثل قبل داخل دیتابیس** (ستون `Data`) باقی مانده است —
  قابلیت «ذخیره رمزنگاری‌شده روی دیسک» ریپوی مرجع طبق تصمیم، پورت **نشده**
  (ستون‌های `StoredFileName`/`Size` و `LetterAttachmentStorage` اضافه نشده‌اند).

## ۵. مواردی که عمداً از مرجع پورت نشد (جلوگیری از پسرفت)

ریپوی مرجع در این نقاط **قدیمی‌تر** از این ریپو است؛ نسخه‌ی اصلاح‌شده‌ی این پروژه حفظ شد:

| نقطه | نسخه‌ی مرجع | وضعیت در این ریپو |
|---|---|---|
| `ErjaService` | فقط نامه داخلی | پشتیبانی نامه داخلی **+ صادره** حفظ شد |
| `InnerLetterService.NextNumberAsync` | آخرین نامه بر اساس `Id` | بیشترین شماره در سال شمسی جاری بین نامه‌های غیرحذفی |
| `InnerLetterService.EditAsync` | بعد از خوانده‌شدن حتی ادمین هم نمی‌تواند ویرایش کند | ادمین می‌تواند ویرایش کند |
| `InnerLetterService.EditAsync` | بارگذاری زیردرخت ارجاع‌ها داخل حلقه (N+1) | یک‌بار بیرون حلقه |
| `AppAttachment.FilePath` | تغییر نام به `StoredFileName` | `FilePath` بدون تغییر (۳ کنترلر دیگر به آن وابسته‌اند) |
