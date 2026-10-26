# دستور کار ↔ نامه: گیرندگان همیشه به نامه دسترسی دارند

تاریخ: ۱۴۰۵/۰۸/۰۴

## خواستهٔ کارفرما
> اگر گیرندگانِ دستور کار آزادانه انتخاب یا بعداً اضافه شدند و در گردش آن نامه نیستند، باید نامه به آن‌ها ارجاع شود تا از فهرست دستور کار بتوانند نامه را باز کنند.

## وضعیت قبلی
- فقط در **ثبت** دستور کار از روی نامه، افراد جدید به گردش نامه اضافه می‌شدند (`AddNewLetterWorkOrderAssigneesToFlowAsync`).
- در **ویرایش دستور کار** (افزودن گیرنده پیش از رویت) و در موارد دیگر، ردیف ارجاع ساخته نمی‌شد ⇒ گیرنده روی لینک «نامه» خطای «شما در گردش این نامه نیستید» می‌گرفت.

## تغییرات (سمت سرور — `Controllers/ItAssets/WorkOrdersController.cs`)

### ۱) هستهٔ مشترک
- متد تازهٔ `EnsureLetterFlowForUsersAsync(sourceId, userIds, senderUserId, title, number)`:
  برای هر کاربری که در جدول `Erjas` این نامه نیست، یک ردیف ارجاع (`Type="ارجاع"`, `ShowMassage=true`, `AmalgarId=1`) می‌سازد و تعداد افزوده‌شده‌ها را برمی‌گرداند. **idempotent** است (ردیف تکراری نمی‌سازد) و فقط روی `LetterSources` با `SourceType` ۱..۳ (نامهٔ داخلی/صادره/وارده) عمل می‌کند.
- متد `SyncOrderAssigneesToLetterFlowAsync(order, userIds)`: مبدأ را از **خودِ رکورد** (`order.SourceModule`/`order.SourceId`) می‌خواند، نه از کلاینت.

### ۲) مسیرهایی که حالا همگام می‌شوند
| مسیر | قبلاً | حالا |
|---|---|---|
| ثبت دستور کار از نامه (`POST /api/workorders`) | ✅ | ✅ (همان رفتار + بدون تغییر منطق مجوز) |
| **ویرایش دستور کار** (`PUT /api/workorders/{id}`) | ❌ | ✅ فقط برای **گیرندگان تازه‌اضافه‌شده** (`addedUserIds`) |
| **افزودن نفر به گردش** (`POST /api/workorders/{id}/assignees`) | ✅ ولی با خطای احتمالی ۴۰۰ | ✅ پایدار، مبدأ از رکورد، بدون بلاک‌کردن عملیات |
| **زیر-دستور (ارجاع زنجیره‌ای)** | ❌ | ✅ مبدأ نامه از دستور والد **به ارث می‌رسد** و گیرندگان زیر-دستور هم به گردش نامه اضافه می‌شوند |

نکتهٔ مجوز: بررسی «سازنده باید ایجادکننده یا گیرندهٔ نامه باشد» فقط در مسیر ثبتِ صریح از نامه اعمال می‌شود؛ برای زیر-دستور یا ویرایش (که پیوند از قبل برقرار است) دوباره اعمال نمی‌شود تا ارجاع زنجیره‌ای قدیمی نشکند.

### ۳) اصلاح دادهٔ قبلی (بدون مهاجرت دستی)
- endpoint جدید `POST /api/workorders/letter-flow/sync`: روی همهٔ دستورکارهای متصل به نامه که کاربر در آن‌ها گیرنده یا دستوردهنده است اجرا می‌شود و ردیف‌های ارجاع ناقص را idempotent می‌سازد.
- کلاینت (`Pages/ItAssets/WorkOrders.razor`) بعد از بارگذاری فهرست، همین endpoint را یک‌بار در پس‌زمینه صدا می‌زند ⇒ کاربران فعلی بدون کار اضافی دسترسی لازم را می‌گیرند.
- با افزودن ردیف، رویدادهای زندهٔ `letters` / `incoming-letters` / `outgoing-letters` هم منتشر می‌شود تا شمارندهٔ نخواندهٔ کارتابل نامه‌ها تازه شود.

## راهنما
`Pages/ItAssets/WorkOrdersGuide.razor` — بخش «گیرندگان و زیر-دستور» یک یادداشت تازه گرفت: ساخت خودکار ارجاع برای گیرندگانِ خارج از گردش نامه و اصلاح خودکار دسترسی‌های قبلی.

## فایل‌های تغییریافته
- `inventory/src/Inventory.Api/Controllers/ItAssets/WorkOrdersController.cs`
- `inventory/src/Inventory.Client/Pages/ItAssets/WorkOrders.razor`
- `inventory/src/Inventory.Client/Pages/ItAssets/WorkOrdersGuide.razor`

---

# پیوست: اعمال یکسان برای هر سه نوع نامه (داخلی، صادره، وارده)

تاریخ: ۱۴۰۵/۰۸/۰۴

## چرا این مرحله لازم بود
ساخت ردیف ارجاع از قبل برای هر سه نوع نامه انجام می‌شد (`LetterSources.SourceType` = ۱ داخلی، ۲ صادره، ۳ وارده)،
اما **باز کردن** نامه برای کسی که فقط از طریق دستور کار به گردش اضافه شده بود، در هر سه نوع با ۴۰۳ بسته می‌شد؛
چون endpointهای جزئیات، مجوز عمومی کارتابل (`InnerLetters.Read` / `OutgoingLetters.Read` / `IncomingLetters.Read`)
را می‌خواستند و «حاضر بودن در گردش» را به‌عنوان مجوز جایگزین قبول نمی‌کردند.

## تغییرات سمت سرور — قاعدهٔ «حاضر در گردش = مجاز به دیدن»
| نامه | endpoint | قبل | حالا |
|---|---|---|---|
| داخلی | `GET api/letters/{id}` | فقط مجوز Read | ایجادکننده یا دارندهٔ ارجاع هم مجاز |
| داخلی | `GET api/letters/{id}/gardesh` | فقط Read | گردش برای عضو گردش هم باز می‌شود |
| داخلی | `GET api/letters/{id}/attachments` و `.../download` | Read + گردش | گردش به‌تنهایی کافی است |
| داخلی | `GET api/letters/{id}/print` | فقط Read | عضو گردش هم می‌تواند چاپ بگیرد |
| صادره | `GET api/outgoing-letters/{id}` | فقط dبیرخانه/مدیر/Read | ایجادکننده، امضاکننده و دارندهٔ ارجاع هم مجاز |
| صادره | `.../gardesh` و `.../attachments` | Read | عضو گردش کافی است |
| وارده | `GET api/incoming-letters/{id}` | فقط Read | `CanAccessLetterAsync` (ایجادکننده/عضو گردش/مدیر) هم کافی است |
| وارده | `.../{id}/gardesh` و پیوست‌ها | Read | عضو گردش کافی است |

## تغییرات سمت کلاینت
- `LetterCartable.razor` (داخلی)، `OutgoingLetterCartable.razor` (صادره)، `IncomingLetterCartable.razor` (وارده):
  وقتی صفحه با `?open={id}` باز می‌شود، خطای بارگذاری فهرست بی‌صدا رد می‌شود تا کاربری که فقط از طریق دستور کار
  به گردش نامه اضافه شده، بدون پیام خطا مستقیماً نامه را ببیند.
- لینک «نامه» در فهرست دستور کار برای هر سه نوع، مسیر درست را می‌سازد (`/letters`، `/outgoing-letters`، `/incoming-letters` + `?open=`).

## فایل‌های تغییریافتهٔ این مرحله
- `Inventory.Api/Controllers/Office/InnerLettersController.cs`
- `Inventory.Api/Controllers/Office/IncomingLettersController.cs`
- `Inventory.Api/Controllers/Office/OutgoingLettersController.cs`
- `Inventory.Client/Pages/Office/Letters/LetterCartable.razor`
- `Inventory.Client/Pages/Office/Letters/Outgoing/OutgoingLetterCartable.razor`
- `Inventory.Client/Pages/Office/Letters/Incoming/IncomingLetterCartable.razor`
- `Inventory.Client/Pages/ItAssets/WorkOrdersGuide.razor`
