# گزارش عیب‌یابی و پیاده‌سازی نامه صادره

## بخش ۱: باگ‌های شناسایی‌شده در ماژول موجود (نامه داخلی)

بر اساس بررسی جداول، مدل‌ها و سرویس‌های ارسالی (InnerLetterService, ErjaService, PishnevisService, LetterGroupService):

### ۱. شماره‌گذاری اندیکاتور (NextNumberAsync) در InnerLetterService
- **مشکل**: فقط آخرین نامه بر اساس `Id` بررسی می‌شد، فیلتر `IsDelete` و `Source.IsDelete` نداشت، و سال شمسی فقط از آخرین نامه استخراج می‌شد. اگر نامه‌های سال جاری حذف شده بودند یا ترتیب `Id` با تاریخ ثبت همخوان نبود، شماره تکراری یا نادرست تولید می‌شد. همچنین در محیط چندکاربره Race Condition داشت.
- **رفع**: بازنویسی شد تا بیشترین `Number` در سال شمسی جاری بین نامه‌های غیرحذفی محاسبه شود. فیلتر `IsDelete` اضافه شد و برای کمک به ایندکس، ابتدا از `startOfYear` میلادی فیلتر می‌شود سپس در حافظه سال شمسی دقیق محاسبه می‌شود.
- **فایل**: `Services/Office/InnerLetterService.cs`

### ۲. CheckGroupIdsAsync در LetterGroupService
- **مشکل**: وقتی لیست گروه‌ها خالی بود `false` برمی‌گرداند، در حالی که معنای منطقی «هیچ گروهی برای بررسی نیست → معتبر» است. این باعث می‌شد در فراخوان‌های بدون گروه، خطای نادرست بدهد (هرچند در سرویس داخلی با `Count>0` محافظت شده بود).
- **رفع**: برای لیست خالی یا null، `true` برگردانده می‌شود.
- **فایل**: `Services/Office/LetterGroupService.cs`

### ۳. ویرایش نامه (EditAsync) — ناسازگاری دسترسی ادمین
- **مشکل**: کامنت کنترلر می‌گوید «مدیر: همیشه» می‌تواند ویرایش کند، اما کد حتی ادمین را بعد از خوانده‌شدن مسدود می‌کرد. در حالی که `DeleteAsync` به ادمین اجازه حذف بعد از خوانده‌شدن می‌داد — ناسازگاری.
- **رفع**: شرط `anyRead` فقط برای کاربران عادی اعمال می‌شود؛ ادمین می‌تواند حتی بعد از خوانده‌شدن ویرایش کند.
- **فایل**: `Services/Office/InnerLetterService.cs`

### ۴. همگام‌سازی ارجاع‌ها در EditAsync — لود مکرر از دیتابیس
- **مشکل**: برای هر گیرنده حذف‌شده، تمام زیردرخت‌های گردش دوباره از دیتابیس لود می‌شد (`await _db.Erjas.Where(...).ToListAsync()` داخل حلقه).
- **رفع**: یک بار خارج از حلقه همه زیردرخت‌ها لود می‌شوند (`allSubErjas`) و سپس در حافظه پردازش می‌شوند.
- **فایل**: `Services/Office/InnerLetterService.cs`

### ۵. اعتبارسنجی ParentErjaId در ErjaService.AddErjaAsync
- **مشکل**: `ParentErjaId` بدون اعتبارسنجی پذیرفته می‌شد؛ کاربر می‌توانست ارجاع والد از نامه‌ای دیگر را تزریق کند و درخت گردش خراب شود.
- **رفع**: بررسی می‌شود که والد وجود داشته باشد، حذف نشده باشد و متعلق به همان `SourceId` باشد.
- **فایل**: `Services/Office/ErjaService.cs`

### ۶. پشتیبانی ErjaService فقط از نامه داخلی
- **مشکل**: سرویس ارجاع فقط `InnerLetters` را چک می‌کرد؛ برای فاز صادره قابل استفاده نبود. همچنین `AnswerAsync` و `GetGardeshTreeAsync` فقط داخلی را پشتیبانی می‌کردند.
- **رفع**: سرویس بازنویسی شد تا `LetterSources` را با `Include` برای هر دو نوع (داخلی و صادره) چک کند. لینک نوتیفیکیشن و Broadcast بر اساس نوع نامه تعیین می‌شود.
- **فایل**: `Services/Office/ErjaService.cs`

### ۷. امنیت پیوست‌ها — نام ماژول
- **مشکل**: در `InnerLettersController` ثابت `Module = "InnerLetters"` برای پیوست استفاده می‌شد اما در برخی جاها `"Pishnevis"` هاردکد بود؛ برای صادره نیز باید ماژول جدا باشد.
- **رفع**: در کنترلر صادره دو ثابت `AttachmentModule = "OutgoingLetters"` و `PishnevisAttachmentModule = "OutgoingPishnevis"` تعریف شد و همه‌جا استفاده شد.

## بخش ۲: پیاده‌سازی نامه صادره (Outgoing Letters)

### ساختار پوشه‌بندی تمیز (درخواست کارفرما)
```
Entities/Office/
  OfficeAutomation.cs          (موجود - LetterSource, InnerLetter, Erja, ...)
  OutgoingLetter.cs            (جدید - OutgoingLetter + OutgoingPishnevisLetter)

Services/Office/
  InnerLetterService.cs
  ErjaService.cs
  LetterGroupService.cs
  PishnevisService.cs
  Outgoing/
    OutgoingLetterService.cs       (جدید - سرویس اصلی صادره)
    OutgoingPishnevisService.cs    (جدید - پیش‌نویس صادره)

Controllers/Office/
  InnerLettersController.cs
  OutgoingLettersController.cs     (جدید)

Shared/Dtos/
  LetterDtos.cs
  OutgoingLetterDtos.cs            (جدید)

Client/Services/
  LetterServices.cs
  OutgoingLetterServices.cs        (جدید)

Client/Pages/Office/Letters/
  (موجود)
  Outgoing/
    OutgoingLetterCartable.razor       (جدید - کارتابل کامل)
    ComposeOutgoingModal.razor         (جدید - فرم ایجاد/ویرایش)
```

### مدل‌ها (Entities)

#### OutgoingLetter
- کلید مشترک با `LetterSource` (Id دستی، SourceType=2)
- شماره‌گذاری مجزا بر اساس سال شمسی: `1404/ص-12`
- فیلدهای اصلی: Title, Text, DateSabt, Mahramanegi, Foriat
- فیلدهای صادره:
  - ReceiverOrganization (الزامی)
  - ReceiverName, ReceiverTitle, ReceiverAddress (اختیاری)
  - CopyTo (رونوشت متن آزاد)
  - ExternalRefNumber (شماره مرجع خارجی)
  - Status: 0=پیش‌نویس، 1=در گردش، 2=تایید شده، 3=صادر شده
- IsDelete, Creator, Source

#### OutgoingPishnevisLetter
- مشابه PishnevisLetter داخلی اما با ReceiverOrganization/Name/Title

#### به‌روزرسانی LetterSource
- اضافه شدن `OutgoingLetter?` navigation

### AppDbContext
- DbSet<OutgoingLetter>, DbSet<OutgoingPishnevisLetter>
- OnModelCreating: رابطه One-to-One با LetterSource (Cascade)، ایندکس‌ها روی Number, DateSabt, CreatorUserId, ReceiverOrganization

### DTOs (OutgoingLetterDtos.cs)
- AddOutgoingLetterDto: با اعتبارسنجی Required برای Title و ReceiverOrganization
- EditOutgoingLetterDto
- OutgoingLetterListItemDto: شامل StatusTitle، ReceiverOrganization، HasAttachment
- OutgoingLetterDetailDto: شامل گیرنده بیرونی + گیرندگان داخلی + RelatedLetters + MyErja
- OutgoingPishnevisDto
- OutgoingLetterCartableStatsDto
- OutgoingLetterPickDto: برای انتخاب عطف/پیرو (شامل هر دو نوع داخلی و صادره)

### سرویس‌ها

#### IOutgoingPishnevisService / OutgoingPishnevisService
- GetAll, GetById, Add, Edit, Delete
- جستجو در Title و ReceiverOrganization
- مرتب‌سازی IsNeshan desc

#### IOutgoingLetterService / OutgoingLetterService
- **NextNumberAsync**: نسخه اصلاح‌شده (فیلتر IsDelete، Max در سال جاری)
- **AddOutgoingLetterAsync**:
  - اعتبارسنجی عنوان و سازمان مقصد
  - باز کردن گروه‌ها به کاربران (Groups → Users)
  - حذف فرستنده از گیرندگان
  - تراکنش: LetterSource (Type=2) + OutgoingLetter + Erjas + RelatedLetters + انتقال پیوست پیش‌نویس
  - Status اولیه: اگر گردش داخلی دارد → 1 (در گردش) وگرنه 2 (تایید شده)
  - نوتیفیکیشن به گیرندگان داخلی + Broadcast
- **GetInboxAsync / GetArchiveAsync / GetSentAsync**: مشابه داخلی اما با فیلتر OutgoingLetter
- **GetDetailAsync**: کنترل دسترسی (فرستنده/گیرنده/ادمین)، CanEdit = isAdmin || (isMine && !anyRead)
- **GetStatsAsync**: InboxUnread, InboxTotal, SentTotal, PishnevisTotal, DeadlineSoon
- **PickListAsync**: از LetterSources هر دو نوع داخلی و صادره (برای عطف/پیرو بین ماژولی)
- **DeleteAsync**: ادمین می‌تواند صادر شده را حذف کند، کاربر عادی نه؛ چک anyRead برای عادی
- **EditAsync**: مشابه داخلی با رفع باگ لود مکرر، همگام‌سازی ارجاع‌های سطح اول + زیردرخت‌ها، همگام‌سازی RelatedLetters
- **UpdateStatusAsync**: تغییر وضعیت صدور (0-3) فقط توسط فرستنده یا ادمین

### کنترلر (OutgoingLettersController)
- Route: `api/outgoing-letters`
- RBAC Module: `OutgoingLetters` (Create, Read, Erja, Delete)
- Endpoints:
  - GET inbox, archive, sent, stats, {id}, pick, {id}/gardesh, amalgars, recivers, groups
  - POST /, erja, erja/{id}/answer, erja/{id}/read, erja/{id}/neshan, erja/{id}/bayegani, pishnevis, {id}/status
  - PUT {id}
  - DELETE {id}, pishnevis/{id}, attachments/{id}
  - Attachments: GET {id}/attachments, POST {id}/attachments, GET attachments/{attId}/download, etc.
- امنیت: InFlowAsync (فرستنده یا گیرنده در گردش یا ادمین)

### RBAC
- ماژول جدید `OutgoingLetters` با اکشن‌های Create, Read, Erja, Delete به RbacSeeder اضافه شد
- به‌صورت خودکار به نقش Admin داده می‌شود

### مایگریشن
- `20260901000000_AddOutgoingLetters`: ایجاد جداول OutgoingLetters و OutgoingPishnevisLetters با FK به LetterSources و Users + ایندکس‌ها
- ModelSnapshot به‌روزرسانی شد

### کلاینت (Blazor WASM)

#### OutgoingLetterServices.cs
- IOutgoingLetterService / OutgoingLetterService
- متدهای کارتابل، گردش، پیش‌نویس، گیرندگان، گروه‌ها، پیوست‌ها
- UploadCoreAsync با Bearer token

#### ComposeOutgoingModal.razor
- فرم ایجاد صادره با گیرنده بیرونی الزامی
- سه کمبوباکس گیرنده داخلی (LetterPickCombo) با پشتیبانی گروهی
- فوریت/محرمانگی رادیویی
- ادیتور غنی (rteSetHtml/rteGetHtml)
- پیوست دراپ‌زون (۲۰MB/فایل، بدون محدودیت تعداد)
- عطف/پیرو با انتخاب از PickList (نمایش نوع صادره/داخلی)
- ذخیره پیش‌نویس و ارسال

#### OutgoingLetterCartable.razor
- ریل پوشه‌ها: تایید (inbox)، صادره (sent)، پیش‌نویس، نشان‌شده، بایگانی
- هدر با جستجو و چیپ فیلتر (همه، خوانده‌نشده، نشان‌شده، پیوست‌دار)
- لیست با گروه‌بندی روز، نمایش شماره اندیکاتور، وضعیت صدور، فوریت، محرمانگی
- منوی سه‌نقطه: ویرایش، علامت صادر شده، ارجاع/هامش، نشان‌کردن، گردش، چاپ، بایگانی
- صفحه‌بندی ۱۵تایی
- ریدر: مشخصات فرستنده، گیرنده بیرونی (سازمان، نام، سمت، آدرس، رونوشت)، متن، پیوست‌ها، دستور ارجاع، پاسخ، اکشن‌ها (تایید/رد/پاسخ متنی/پیگیری گردش/ویرایش/علامت صادر شده)
- سایدبار: مشخصات نامه صادره، نامه‌های مرتبط، گیرندگان داخلی
- مودال پاسخ
- Realtime: گوش دادن به DataChanged با scope `outgoing-letters`

#### NavMenu.razor
- اضافه شدن لینک «کارتابل نامه صادره» در گروه اتوماسیون اداری
- شرط نمایش: HasModule("OutgoingLetters")
- گروه اتوماسیون اکنون با هر یک از دو ماژول (InnerLetters یا OutgoingLetters) نمایش داده می‌شود

### بهبودهای خوانایی و تمیزی کد
- کامنت‌های فارسی برای هر کلاس/متد
- نام‌گذاری واضح (ReceiverOrganization به‌جای گیرنده مبهم)
- تفکیک مسئولیت: هر سرویس فقط یک کار (Single Responsibility)
- استفاده از `HashSet` برای جلوگیری از تکرار گیرندگان
- تراکنش صریح برای عملیات چندجدولی
- اعتبارسنجی‌های ورودی با پیام فارسی
- عدم تکرار کد: ErjaService برای هر دو نوع نامه استفاده می‌شود

## نحوه تست
1. اجرای API: باید مایگریشن `AddOutgoingLetters` اعمال شود (SqlServer: `dotnet ef database update` یا در حالت Sqlite: حذف `inventory.db` و اجرای مجدد)
2. ورود با admin/admin
3. رفتن به «اتوماسیون اداری → کارتابل نامه صادره»
4. ایجاد نامه صادره جدید با سازمان مقصد
5. بررسی کارتابل ارسالی، ویرایش قبل از خوانده‌شدن، ارجاع داخلی، پاسخ، بایگانی، پیوست
6. بررسی نوتیفیکیشن‌ها و بروزرسانی زنده

## فایل‌های تغییر یافته / جدید
- جدید: Entities/Office/OutgoingLetter.cs
- ویرایش: Entities/Office/OfficeAutomation.cs (افزودن OutgoingLetter navigation)
- ویرایش: Data/AppDbContext.cs (DbSet و OnModelCreating)
- جدید: Migrations/20260901000000_AddOutgoingLetters.cs
- ویرایش: Migrations/AppDbContextModelSnapshot.cs
- جدید: Shared/Dtos/OutgoingLetterDtos.cs
- ویرایش: Services/Office/InnerLetterService.cs (رفع باگ شماره‌گذاری، ادمین، لود مکرر)
- ویرایش: Services/Office/LetterGroupService.cs (رفع باگ CheckGroupIds)
- ویرایش: Services/Office/ErjaService.cs (اعتبارسنجی ParentErjaId، پشتیبانی صادره)
- جدید: Services/Office/Outgoing/OutgoingPishnevisService.cs
- جدید: Services/Office/Outgoing/OutgoingLetterService.cs
- جدید: Controllers/Office/OutgoingLettersController.cs
- ویرایش: Data/RbacSeeder.cs (ماژول OutgoingLetters)
- ویرایش: Program.cs (ثبت سرویس‌های صادره)
- جدید: Client/Services/OutgoingLetterServices.cs
- ویرایش: Client/Program.cs (ثبت سرویس کلاینت)
- ویرایش: Client/Layout/NavMenu.razor (لینک صادره)
- جدید: Client/Pages/Office/Letters/Outgoing/ComposeOutgoingModal.razor
- جدید: Client/Pages/Office/Letters/Outgoing/OutgoingLetterCartable.razor
