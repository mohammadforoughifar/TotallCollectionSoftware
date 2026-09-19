# بهبودهای دبیرخانه نامه صادره

مستند تغییرات اعمال‌شده روی ماژول **دبیرخانه نامه صادره**
(`/outgoing-letters/dabirkhane` — کنترلر `OutgoingLettersController`).

> خروجی نهایی: بیلد کل `Inventory.sln` بدون خطا (۰ Error).

---

## ۱) امکان افزودن یک نامه به بایگانی

**مسئله:** بایگانی موجود (`LetterBayegani`) فقط نامه‌های داخلی را پوشش می‌داد و
دبیرخانه هیچ راهی برای بایگانی نامه‌های صادره نداشت.

**راهکار:** یک شاخهٔ مستقل برای دبیرخانه با `TypeBayegani = 2`:

| لایه | تغییر |
|---|---|
| موجودیت | `OutgoingLetter`: `IsArchived`، `ArchivedAt`، `ArchivedByUserId` |
| اسکیما | `Data/DabirkhaneSchemaV1.cs` (خودتعمیر، idempotent، SQLite + SQL Server) |
| سرویس | `ArchiveService`: `GetOutgoingTreeAsync`، `AddOutgoingMainCategoryAsync`، `AddOutgoingSubCategoryAsync`، `EditOutgoingFolderAsync`، `MoveOutgoingFolderAsync`، `ArchiveOutgoingLettersAsync`، `UnarchiveOutgoingLetterAsync`، `MoveArchivedLetterAsync`، `DeleteOutgoingAsync` |
| API | `GET/POST/…  api/outgoing-letters/dabirkhane/bayegani/*` |
| UI | دکمهٔ «بایگانی» روی هر نامه + مودال انتخاب پوشه (`DabirkhaneArchivePicker`) + تب «بایگانی‌شده» با نمای درختی (`DabirkhaneArchiveTree`) |

**قواعد:**
- فقط نامه‌های **امضا شده** (دارای شماره صادره) بایگانی می‌شوند.
- بایگانی شخصی نامه‌های داخلی دست‌نخورده می‌ماند — دو درخت کاملاً جدا هستند
  (`GetTreeAsync` گره‌های `TypeBayegani=2` را نادیده می‌گیرد و بالعکس).
- حذف پوشه فقط در صورت خالی بودن مجاز است؛ حذف نامه، پرچم `IsArchived` را آزاد می‌کند.

---

## ۲) چاپ در دو نسخه: با رونوشت / بدون رونوشت

- `IOutgoingLetterPrintService.GeneratePdfAsync(letterId, size, withCopy = true)`
- `GET api/outgoing-letters/{id}/print?size=A4|A5&withCopy=true|false`
- نسخهٔ **بدون رونوشت** بلوک «رونوشت» را چاپ نمی‌کند (نسخهٔ تحویل به سازمان مقصد)؛
  نسخهٔ **با رونوشت** آن را در انتهای نامه درج می‌کند (نسخهٔ بایگانی دبیرخانه).
- پاصفحهٔ هر نسخه نشانهٔ «نسخه: با رونوشت / بدون رونوشت» دارد تا دو نسخه
  در پرونده از هم قابل تشخیص باشند.
- نام فایل خروجی شامل نسخه است:
  `letter-{شماره}-{A4|A5}-{با-رونوشت|بدون-رونوشت}.pdf`
- UI: دکمهٔ «چاپ» یک مودال باز می‌کند با انتخاب سایز (A4/A5) و انتخاب نسخه.
  اگر نامه رونوشت نداشته باشد، هشدار «هر دو نسخه یکسان چاپ می‌شوند» نمایش داده می‌شود.
- ایمیل خودکار دبیرخانه همچنان نسخهٔ **با رونوشت** را می‌فرستد (رفتار قبلی حفظ شد).

---

## ۳) فیلدهای پویا بر اساس روش ارسال

یک «مشخصات روش ارسال» در `Inventory.Shared` تعریف شد تا UI و سرور هر دو از یک منبع
واحد استفاده کنند:

```csharp
var spec = LetterSendMethods.Spec("پست پیشتاز");
spec.NeedsDelivererName  // true
spec.NeedsTrackingCode   // true
```

| روش ارسال | فیلدی که دبیرخانه می‌پرسد |
|---|---|
| **ایمیل** | «ارسال به کدوم ایمیل؟» (آدرس ایمیل مقصد) + انتخاب حساب ایمیل دبیرخانه |
| **پست** / **پست پیشتاز** | «نام تحویل گیرنده» (الزامی) + کد رهگیری مرسوله (اختیاری) |
| **پیک** / **تحویل حضوری** | «نام تحویل گیرنده» (الزامی) |
| **فکس** | شماره فکس مقصد (الزامی) |
| **اتوماسیون (ECE)** | شماره ثبت مقصد (الزامی) |

- فیلدهای هر روش در مودال ثبت با **رنگ و آیکون متمایز** نمایش داده می‌شوند و با
  تغییر روش ارسال، فیلدهای نامرتبط پاک می‌شوند تا دادهٔ بی‌ربط ذخیره نشود.
- اعتبارسنجی هم در کلاینت و هم در سرور (`DabirkhaneRegisterAsync`) انجام می‌شود.
- برای روش‌های غیرایمیلی، گزینهٔ «ارسال همزمان با پست الکترونیک» حفظ شده است.

**ستون‌های جدید جدول `OutgoingLetters`:**
`DelivererName`، `TrackingCode`، `DestFax`، `IsArchived`، `ArchivedAt`، `ArchivedByUserId`

---

## ۴) نمایش گردش نامه

- دکمهٔ **«گردش»** روی هر نامه در دبیرخانه → مودال `OutgoingGardeshModal`
  (سه نما: لیستی / درختواره / فلوچارتی + امضاکنندگان + پاراف و پاسخ‌ها).
- دسترسی‌ها برای کاربر دبیرخانه باز شد:
  - `GET {id}/gardesh`
  - `GET {id}/signers` (دبیرخانه باید امضاکنندگان را هم در گردش ببیند)
  - `GET {id}` (جزئیات نامه — قبلاً با خطای «در گردش نیستید» مواجه می‌شد)
  - `GET {id}/print` (قبلاً باز بود)

---

## ۶) جستجو بین نامه‌ها

**جستجوی پیشرفته** به لیست دبیرخانه اضافه شد (`DabirkhaneSearchDto`):

| فیلتر | توضیح |
|---|---|
| عبارت متنی | عنوان، شماره صادره، اندیکاتور، سازمان مقصد، نام گیرنده، نام تحویل گیرنده، کد رهگیری، شماره فکس، ایمیل مقصد، شماره ثبت مقصد، روش ارسال، متن رونوشت، توضیح دبیرخانه |
| بازهٔ تاریخ صدور | از تاریخ / تا تاریخ |
| روش ارسال | یکی از روش‌های تعریف‌شده |
| فرستنده | ثبت‌کننده نامه |
| شرکت (سربرگ) | شرکت صادرکننده |
| سازمان مقصد | بخشی از نام |
| محرمانگی / فوریت | عادی / محرمانه / سری و عادی / فوری / آنی |
| پیوست | فقط پیوست‌دار / بدون پیوست |
| وضعیت | در انتظار ثبت / ثبت شده / بایگانی‌شده (تب‌ها) |

**نکات:**
- جستجوی شماره‌ها با **ارقام فارسی و لاتین** هر دو کار می‌کند
  (تایپ `۱۴۰۴/ص-۱۲` معادل `1404/ص-12` است).
- گزینهٔ «جستجو در همه نامه‌ها (صرف‌نظر از تب جاری)» محدودیت تب فعال را برمی‌دارد.
- شمارندهٔ فیلترهای فعال روی دکمهٔ «جستجوی پیشرفته» نمایش داده می‌شود.

---

## فهرست فایل‌ها

### جدید
- `Inventory.Api/Data/DabirkhaneSchemaV1.cs`
- `Inventory.Client/Pages/Office/Letters/Outgoing/DabirkhaneArchivePicker.razor`
- `Inventory.Client/Pages/Office/Letters/Outgoing/DabirkhaneArchiveTree.razor`

### تغییر‌یافته
| فایل | تغییر |
|---|---|
| `Inventory.Api/Entities/Office/OutgoingLetter.cs` | ستون‌های ارسال و بایگانی |
| `Inventory.Api/Data/DbInitializer.cs` | فراخوانی `DabirkhaneSchemaV1.EnsureAsync` |
| `Inventory.Api/Services/Office/ArchiveService.cs` | بایگانی دبیرخانه + تفکیک نوع |
| `Inventory.Api/Services/Office/Outgoing/OutgoingLetterService.cs` | جستجوی پیشرفته، اعتبارسنجی روش ارسال |
| `Inventory.Api/Services/Office/Outgoing/OutgoingLetterPrintService.cs` | پارامتر `withCopy` |
| `Inventory.Api/Controllers/Office/OutgoingLettersController.cs` | اندپوینت‌های جدید + دسترسی‌ها |
| `Inventory.Shared/Dtos/OutgoingLetterDtos.cs` | `LetterSendMethods.Spec`، `SendMethodSpec`، `DabirkhaneSearchDto`، DTOهای بایگانی |
| `Inventory.Shared/Dtos/LetterDtos.cs` | فیلدهای صادره در `BayeganiNodeDto` |
| `Inventory.Client/Services/OutgoingLetterServices.cs` | متدهای جدید کلاینت |
| `Inventory.Client/Pages/…/OutgoingDabirkhane.razor` | بازنویسی صفحه |

---

## پایگاه داده

ستون‌های جدید نیازی به اجرای دستی اسکریپت ندارند؛
`DabirkhaneSchemaV1.EnsureAsync` در زمان راه‌اندازی برنامه و به‌صورت
**idempotent** آن‌ها را به دیتابیس موجود اضافه می‌کند (هم SQLite و هم SQL Server)،
دقیقاً مطابق الگوی `WorkOrderSchemaV1` / `OfficeEmailSchemaV1`.
