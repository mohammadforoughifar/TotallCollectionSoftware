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
