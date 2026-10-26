# ثبت‌کنندهٔ نامهٔ وارده در گردش نامه قرار نمی‌گیرد

تاریخ: ۱۴۰۵/۰۸/۰۴

## قاعدهٔ موردنظر کارفرما
> شخصی که نامهٔ وارده را ثبت می‌کند همان مسئول دبیرخانه است و لازم نیست به‌عنوان «ایجادکننده» در گردش نامه قرار بگیرد؛ اما اگر نامه **به او ارجاع** داده شد، می‌تواند در گردش باشد.

## وضعیت پیش از این تغییر
در `IncomingLetterService.AddAsync` برای ثبت‌کننده هم یک ردیف ارجاع **خودکار** ساخته می‌شد
(`SenderUserId = ReciverUserId = ثبت‌کننده`، `Type = "گیرنده"`، `IsRead = true`).
- «دریافتی» با فیلتر `e.SenderUserId != userId` این ردیف را پنهان می‌کرد (پس در کارتابل دیده نمی‌شد)
- ولی ردیف در دیتابیس و در **درخت گردش نامه** و **فهرست گیرندگان نامه** باقی می‌ماند ⇒ ثبت‌کننده عملاً در گردش بود و در شمارش گیرندگان/گردش دیده می‌شد.

## تغییرات

### ۱) دیگر ردیف خودکار ساخته نمی‌شود — `Inventory.Api/Services/Office/IncomingLetterService.cs`
- `recivers` فقط از `dto.InitialReciverUserIds` پر می‌شود و با `recivers.Remove(userId)` ثبت‌کننده حذف می‌شود.
- ردیف‌های ساخته‌شده `IsRead = false, DateRead = null` هستند (چون همه «گیرندهٔ واقعی» هستند).
- دسترسی ثبت‌کننده به نامهٔ خودش همچنان برقرار است: تمام بررسی‌های دسترسی روی `CreateUserId` هم شرط گذاشته‌اند
  (`GetDetailAsync`، `CanAccessLetterAsync`، `ErjaService.AddErjaAsync`، `FinalizeAsync`).
- اعلان گیرندگان (`FinalizeAsync`) از قبل ثبت‌کننده را مستثنی می‌کرد ⇒ بدون تغییر.
- نتیجه: نامهٔ تازه‌ثبت‌شده بدون گیرنده، صفر ردیف ارجاع دارد؛ ثبت‌کننده آن را در «ارسالی/ثبت‌شده‌های من» و فهرست دبیرخانه می‌بیند.

### ۲) دادهٔ قدیمی بدون حذف رکورد پنهان می‌شود
- `IncomingLetterService.GetDetailAsync`: ردیف‌های `SenderUserId == ReciverUserId && Type == "گیرنده"` از `allErjas` حذف شدند ⇒ در فهرست گیرندگان و اطلاعات گردشِ جزئیات نامه دیده نمی‌شوند.
- `ErjaService.GetGardeshTreeAsync`: برای نامهٔ وارده (`isIncoming`) همان فیلتر روی `scope` اعمال شد ⇒ درخت گردش، ثبت‌کننده را به‌عنوان گیرنده نشان نمی‌دهد.
- هیچ رکوردی حذف/تغییر داده نشده است (برگشت‌پذیر و بی‌خطر برای سابقه).

### ۳) اگر نامه واقعاً به ثبت‌کننده ارجاع شود
`ErjaService.AddErjaAsync` ارجاع واقعی را با `SenderUserId = ارجاع‌دهنده` می‌سازد. چون فرستنده ≠ گیرنده،
ردیف در «دریافتی» و درخت گردش نمایش داده می‌شود — دقیقاً همان رفتار خواسته‌شده.

## راهنماها
- `Pages/Office/Guides/IncomingLetterGuide.razor`: به یادداشت «نامه‌ای که خودتان ثبت کرده‌اید در دریافتی شما نیست» اضافه شد که ثبت‌کننده در **گردش نامه و فهرست گیرندگان** هم نیست و فقط با ارجاع واقعی وارد گردش می‌شود.
- `Pages/Office/Guides/DabirkhaneGuide.razor`: همان توضیح در بخش «ثبت و ارجاع نامه وارده».

## فایل‌های تغییریافته
- `inventory/src/Inventory.Api/Services/Office/IncomingLetterService.cs`
- `inventory/src/Inventory.Api/Services/Office/ErjaService.cs`
- `inventory/src/Inventory.Client/Pages/Office/Guides/IncomingLetterGuide.razor`
- `inventory/src/Inventory.Client/Pages/Office/Guides/DabirkhaneGuide.razor`
