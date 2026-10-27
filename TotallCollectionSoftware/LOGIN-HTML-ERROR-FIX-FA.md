# رفع صفحهٔ قرمز حاوی سورس HTML در صفحهٔ ورود — ۱۴۰۵/۰۷/۲۹ (2026-10-21)

## علامت
با زدن دکمهٔ ورود، به‌جای پیام کوتاه خطا، یک باکس قرمز عظیم باز می‌شد که **سورس کامل
صفحهٔ index.html** (شامل توابع JS) داخلش نمایش داده می‌شد.

## ریشهٔ دقیق — زنجیرهٔ سه‌مرحله‌ای

۱. درخواست ورود (`POST api/auth/login`) به سروری می‌رسد که پاسخ **HTML** برمی‌گرداند نه
   JSON — این وقتی رخ می‌دهد که درخواست به endpoint درست نخورد و یکی از این‌ها پاسخ بدهد:
   - فالبک SPA سرور (`MapFallback`) که برای هر مسیر بدون endpoint، پوستهٔ index.html را برمی‌گرداند؛
   - صفحهٔ خطای IIS/پروکسی (۵۰۲/۵۰۳) وقتی سرویس API بالا نیست؛
   - آدرس API ذخیره‌شدهٔ مرورگر (localStorage) به سرور/پورت اشتباهی اشاره کند.

۲. در `ApiClient.SendAsync` وقتی پاسخ JSON نباشد، پیام خطا **برابر کل بدنهٔ پاسخ** می‌شد:
   `msg = text;` → کل سورس HTML داخل `ApiException` می‌رفت.

۳. `LoginView.razor` هم `ex.Message` را مستقیم در باکس قرمز نشان می‌دهد → همان صفحهٔ عظیم.

نکته: تغییرات چاپ قبلی علت این مشکل نبودند؛ توابع چاپ فقط چون داخل index.html هستند در
آن dump دیده می‌شدند.

## اصلاحات

### ۱) سرور — `Inventory.Api/Program.cs`
مسیرهای `/api/*` دیگر هرگز پوستهٔ SPA را نمی‌گیرند؛ اگر درخواست API با هیچ endpoint ای
مطابقت نداشت، پاسخ **404 JSON** با پیام شفاف برمی‌گردد:
```csharp
app.MapFallback("{*path:nonfile}", ctx =>
{
    if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        return ctx.Response.WriteAsync("{\"message\": \"این مسیر در API یافت نشد؛ ...\"}");
    }
    return ServeSpaIndexAsync(ctx, clientRoot);
}).AllowAnonymous();
```

### ۲) کلاینت — `Inventory.Client/Services/ApiClient.cs`
- تابع کمکی `DescribeBody`: اگر بدنهٔ پاسخ HTML بود → پیام کوتاه راهنما
  («پاسخ سرور به‌جای داده (JSON) یک صفحهٔ HTML بود؛ احتمالاً نشانی سرور API اشتباه است
  یا سرویس API در دسترس نیست.»)؛ سایر متن‌های غیر JSON به ۲۰۰ کاراکتر خلاصه می‌شوند.
- هر دو مسیر خطا (کد غیرموفق و کد ۲۰۰ با بدنهٔ غیر JSON) از همین helper استفاده می‌کنند.
- همان اصلاح در سرویس‌هایی که بدنهٔ خام را dump می‌کردند:
  `DevTeamService.EnsureOk`، و سه متد دانلود پیوست در
  `IncomingLetterServices` / `LetterServices` / `OutgoingLetterServices`.

## نتیجه برای کاربر
- دیگر هیچ‌وقت سورس HTML در باکس خطا نمی‌بینید؛ پیام‌ها کوتاه و قابل‌فهم‌اند و
  آدرس دقیق درخواست + کد HTTP را هم نشان می‌دهند تا علت مشخص شود.
- اگر بعد از این نسخه پیام «صفحهٔ HTML به‌جای JSON» دیدید، یعنی درخواست به سرور درستی
  نمی‌رسد: در `F12 → Network` آدرس درخواست login را چک کنید و `api/health` سرور را
  مستقیم در مرورگر باز کنید (باید JSON بدهد).

## فایل‌ها
- `Inventory.Api/Program.cs`
- `Inventory.Client/Services/ApiClient.cs`
- `Inventory.Client/Services/DevTeamService.cs`
- `Inventory.Client/Services/IncomingLetterServices.cs`
- `Inventory.Client/Services/LetterServices.cs`
- `Inventory.Client/Services/OutgoingLetterServices.cs`
