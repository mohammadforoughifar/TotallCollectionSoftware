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
| صادره | `GET api/outgoing-letters/{id}` | فقط دبیرخانه/مدیر/Read | ایجادکننده، امضاکننده و دارندهٔ ارجاع هم مجاز |
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

---

# پیوست ۲: اعلان «نامهٔ جدید در کارتابل شما» برای افراد تازه‌اضافه‌شده

تاریخ: ۱۴۰۵/۰۸/۰۴

## قاعده
هر فردی که **در همان لحظه** به گردش نامه اضافه می‌شود (یعنی برایش ردیف ارجاع ساخته می‌شود)، یک اعلان با عنوان
**«نامهٔ جدید در کارتابل شما 📨»** می‌گیرد. افرادی که از قبل در گردش نامه بوده‌اند **اعلان تکراری نمی‌گیرند**
و خودِ فرستنده/دستوردهنده هم اعلان نمی‌گیرد.

## محتوای اعلان
- **بدنه:** «{شمارهٔ نامه} — «{عنوان نامه}» — دسترسی از طریق دستورکار «{عنوان دستور}» ({شمارهٔ دستور})»
- **فرم:** نامه داخلی / نامه صادره / نامه وارده (بر اساس نوع واقعی نامه)
- **از طرف:** کاربری که ارجاع را ثبت کرده (دستوردهنده / افزایندهٔ نفر)
- **لینک:** مسیر کارتابل همان نوع نامه + `?open={شناسه}` ⇒ `/letters`، `/outgoing-letters`، `/incoming-letters`

## جایگاه پیاده‌سازی
داخل `EnsureLetterFlowForUsersAsync` در `Controllers/ItAssets/WorkOrdersController.cs` و درست پس از ساخت
ردیف‌های `Erjas`، متد `NotifyNewLetterAccessAsync(...)` فقط برای فهرست `newTargets` (افراد تازه‌افزوده‌شده)
فراخوانی می‌شود. بنابراین **همهٔ مسیرها** به‌طور خودکار پوشش داده می‌شوند:

1. ثبت دستور کار از روی نامه (`POST api/workorders`)
2. ویرایش دستور کار و افزودن گیرندهٔ تازه (`PUT api/workorders/{id}`)
3. افزودن نفر به گردش (`POST api/workorders/{id}/assignees`)
4. زیر-دستور زنجیره‌ای (نامه از والد به ارث می‌رسد)
5. همگام‌سازی دادهٔ قبلی (`POST api/workorders/letter-flow/sync`)

## هم‌زمانی
پس از ساخت ارجاع، رویدادهای زندهٔ `letters` / `incoming-letters` / `outgoing-letters` منتشر می‌شود تا کارتابل باز
کاربر و شمارندهٔ نخوانده، بدون رفرش تازه شوند.

---

# پیوست ۳ — اعلان «پیوست جدید» برای رویت‌کرده‌های گردش

## قاعده
هرگاه به نامه‌ای (داخلی، صادره یا وارده) پیوست تازه‌ای اضافه شود، اعلان «پیوست جدید برای نامه» فقط برای
افرادی ارسال می‌شود که **هم در گردش آن نامه هستند** و **هم نامه را رویت کرده‌اند**.

## گیرندگان اعلان
| نوع نامه | «حاضر در گردش» | «رویت‌کرده» |
|---|---|---|
| داخلی | ردیف‌های `Erja` فعال (ایجادکننده هم اگر ردیف ارجاع داشته باشد) | `Erja.IsRead == true` |
| صادره | ردیف‌های `Erja` فعال + `OutgoingLetterSigners` + ایجادکننده | `Erja.IsRead == true` یا `OutgoingLetterSigner.IsRead == true` |
| وارده | ردیف‌های `Erja` فعال | `Erja.IsRead == true` |

- بارگذارندهٔ فایل (`uploaderUserId`) از فهرست گیرندگان حذف می‌شود.
- افرادی که نامه را نخوانده‌اند اعلان جداگانه نمی‌گیرند؛ نامهٔ نخوانده از قبل در کارتابل/شمارندهٔ آن‌ها برجسته است.

## محتوای اعلان
- **عنوان:** «پیوست جدید برای نامه 📎»
- **بدنه:** `{شمارهٔ نامه} — «{عنوان نامه}» — فایل: {نام فایل}`
- **فرم:** نامه داخلی / نامه صادره / نامه وارده
- **از طرف:** نام بارگذارندهٔ فایل
- **لینک:** `/letters` ، `/outgoing-letters` ، `/incoming-letters` + `?open={شناسه}`

## جایگاه پیاده‌سازی
- `Services/Office/LetterAttachmentNotifier.cs` → `LetterAttachmentNotifier.NotifyNewAttachmentAsync(...)`
  (منبع واحد قاعده؛ هر سه نوع نامه از همین متد استفاده می‌کنند).
- فراخوانی در `UploadAttachment` (یعنی `POST {id}/attachments`) هر سه کنترلر
  `InnerLettersController`، `OutgoingLettersController` و `IncomingLettersController`
  و درست پس از `SaveChangesAsync`؛ خطای اعلان در `try/catch` گرفته می‌شود تا آپلود هرگز به‌خاطر اعلان شکست نخورد.

## راهنماها
این رفتار در `InnerLetterGuide.razor`، `OutgoingLetterGuide.razor` و `IncomingLetterGuide.razor`
(بخش «اعلان پیوست جدید») و همچنین `ItAssets/WorkOrdersGuide.razor` مستند شده است.
