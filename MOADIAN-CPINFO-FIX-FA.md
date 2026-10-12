# رفع خطای 404 ماژول مودیان — Route گمشده به‌علاوه تکمیل ثبت شرکت (SaveCpInfo)

تاریخ: 2026-10-12 — مرجع: کامیت‌های قبلی `5c89561` (کلاینت PWA + پیاده‌سازی سرور) و اینجا.

## علت خطا

خطاهای کاربر هنگام ثبت شرکت و نمایش واحدهای سنجش:

```
بدنه‌ی پاسخ خالی بود. (HTTP 404 — http://localhost:5100/Moadian/SaveCpInfo)
بدنه‌ی پاسخ خالی بود. (HTTP 404 — http://localhost:5100/Moadian/GetAllUnitOfMeasurementAsync?...)
```

**جداول و سرویس‌ها کامل بودند** (`CPInfos` در مگراسیون `AddMoadianV1` ساخته شده؛ `DbInitializer` هنگام اجرا اعمال می‌کند؛ `IConnectService` در DI ثبت است) — مشکل سرِ **Routing** بود:

کلاینت PWA (`MoadianService.cs`) با فرض رویت MVC کلاسیک، همهٔ endpointها را زیر پیشوند `Moadian/` می‌خواند،
اما `MoadianController` نه `[ApiController]` داشت و نه `[Route(...)]`. در ASP.NET Core برای کنترلرِ attribute-routedِ
بدون route در سطح کلاس، الگوهای اکشن **به‌صورت مطلق** مپ می‌شوند:

| واقعی (قبل از این کامیت) | انتظار کلاینت |
|---|---|
| `/GetAllUnitOfMeasurementAsync` | `/Moadian/GetAllUnitOfMeasurementAsync` ← 404 با بدنهٔ خالی |
| `/SaveCpInfo` | `/Moadian/SaveCpInfo` ← 404 با بدنهٔ خالی |

پس **این رفع، کل رابط‌های کاربری مودیان را زنده می‌کند** (اتصال، کالا، مشتری، سال مالی، فاکتور، واحد سنجش، ...).

## تغییرات این کامیت (روی تیپ main)

### ۱) `MoadianController.cs` — ریشهٔ خطا
```csharp
[ApiController]
[Route("Moadian")]
public class MoadianController(...)
```
- `[Route("Moadian")]`: اکشن‌ها زیر `/Moadian/{action}` می‌نشینند — دقیقاً همان آدرسی که کلاینت می‌خواند.
- `[ApiController]`: بدنهٔ JSON اکشن‌های POST به‌درستی bind می‌شود (بدون آن مدل‌های پیچیده از body خوانده نمی‌شدند).

### ۲) `ConnectService.SaveCpInfoAsync` — تکمیل منطق ثبت شرکت
- قبلاً فقط Insert بود و ویرایش شرکت موجود → شرکت جدیدِ تکراری ساخته می‌شد.
- اکنون: `cpinfoid = 0` ← ثبت جدید | `cpinfoid > 0` ← ویرایش همان رکورد.
- اعتبارسنجی: نام فارسی و شناسهٔ ملی الزامی + جلوگیری از شناسهٔ ملی تکراری (به‌جز خودِ رکورد در ویرایش).

## تست سریع (بعد از اجرای API روی ۵۱۰۰)

```bash
curl "http://localhost:5100/Moadian/GetAllUnitOfMeasurementAsync?searchTerm=&page=1&pageSize=10"

curl "http://localhost:5100/Moadian/GetCpInfos"

curl -X POST "http://localhost:5100/Moadian/SaveCpInfo" \
  -H "Content-Type: application/json" \
  -d "{\"cpinfoid\":0,\"persianName\":\"شرکت تست\",\"nationalid\":\"14000000001\",\"economicNumber\":\"\"}"
```

پاسخ‌ها قالب استاندارد `ApiModel` دارند (`success/status/message/data`).
**مگراسیون جدید لازم نیست** — اسکیمای دیتابیس تغییر نکرده است.

---

## لایهٔ دوم مشکل: `Invalid object name 'CPInfos'`

بعد از رفع 404، سرور در ثبت شرکت با این خطا برگشت:

```
خطا در سرور: Invalid object name 'CPInfos'.
```

### ریشه (دقیق)
`DbInitializer.MigrateSqlServer` وقتی اجرای مایگریشن با خطای «شیءِ تکراری در دیتابیس»
مواجه شود (فیلتر خطاها: کدهای 2714/2705/…)، مایگریشن جاری را **رزمیج (stamp) در
`__EFMigrationsHistory` می‌کند و ادامه می‌دهد**. چون `AddMoadianV1` ابتدا
`AddColumn(Users.Email)` را صدا می‌زند و چند ستونِ دیگر هم دارد (PostalCode روی Parties،
Transcript روی ChatMessages و…)، روی دیتابیس‌هایی که این ستون‌ها را *از قبل* داشتند،
مایگریشن در همان قدم اول می‌خورد و کل تراکنش رول‌بک می‌شود؛ بعد به‌اشتباه «اعمال‌شده»
ثبت می‌گردد. نتیجه: جدول‌های مودیان (`CPInfos` و ۷ جدول دیگر) **هرگز ساخته نشده‌اند**
و EF دیگر سراغشان نمی‌آید.

### رفع
- **خودتعمیر خودکار:** `Data/MoadianSchemaV1.cs` — مطابق الگوی بقیهٔ `*SchemaV1*ها،
  هر ۸ جدول مودیان را به‌صورت idempotent می‌سازد (SQL Server + SQLite) و در
  `DbInitializer` هر دو مسیر provider را پوشش می‌دهد. روی دیتابیسی که مایگریشن کامل
  اجرا شده، هیچ کاری انجام نمی‌دهد. → **با اجرای مجدد API مشکل رفع می‌شود.**
- **رفع فوری دستی (بدون استقرار):** `inventory/sql/moadian-repair.sql` را روی
  دیتابیس اجرا کنید (پایانی با ۸ سطر تأیید می‌گیرد).

---

## ممیزی کامل ثبت‌های ماژول (2026-10-12) — رفع ۴ ناشایست + فیلد نوع شرکت

بازبینی دوطرفهٔ «کلاینت PWA ↔ کنترلر ↔ سرویس↔ دیتابیس» همهٔ فرم‌ها:

### یافته‌ها و رفع‌ها
| # | مشکل | رفع |
|---|---|---|
| 1 | ثبت کالا **بدون واحد سنجش** → `FirstOrDefault(...).Name` نال می‌شد → کالا با 500 شکست می‌خورد (فرم دراپ‌داون واحد را اختیاری دارد) | `InsertGoodOrServiceAsync`/`UpdateGoodOrServiceAsync` نال‌ایمن شد + فallback به نام واحدِ VM |
| 2 | در **ویرایش** کالا، واحد سنجش اصلاً در ستون ذخیره نمی‌شد (داده گم می‌شد) | همان متد اکنون `data.UnitOfMeasurement` را هم می‌نویسد |
| 3 | `GetIsConnectToMoadian` روی `FilePrivatekey.FileName` بدون نال‌چک → 500 | گارد با پیام فارسی 400 |
| 4 | خطای 400 ثبت کالا عمومی بود («درخواست نامعتبر است») | پیام دقیق فارسی + clamp نرخ ارزش‌افزوده بین ۰–۱۰۰ پیش از تبدیل به byte |

### فیلد «نوع شرکت — حقیقی/حقوقی»
- `CpInfo.Type` و `VMShowCpinfoByCompany.Type` اضافه شد (سازگار با عقب: خالی ⇒ «حقیقی»).
- `MoadianSchemaV1` ستون را هم در CREATE تازه و هم با ALTER idempotent روی دیتابیس‌های موجود می‌سازد (SQL Server + SQLite)؛ `moadian-repair.sql` هم همین را دارد.
- UI: فرم «ثبت شرکت جدید» الان **دراپ‌داون نوع** دارد (با پیشنهاد خودکار از طول شناسه: ۱۱ رقم ⇒ حقوقی، ۱۰ رقم ⇒ حقیقی)؛ بار شرکت و هدر کارت اتصال نشان حقیقی/حقوقی را کنار نام نشان می‌دهند.

### تست
`inventory/tools/moadian-smoke-test.ps1` — `pwsh ./tools/moadian-smoke-test.ps1`
همهٔ ثبت‌ها/ویرایش‌ها را با دادهٔ تصادفی یکتا روی API زنده چک می‌کند (شرکت، سال مالی،
واحد، کالا با و بدون واحد، مشتری، صورتحساب و گارد کلید) و در پایان PASS/FAIL می‌دهد.

