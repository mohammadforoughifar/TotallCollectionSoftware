// نسخهٔ سبک و فقط-اینترفیس از IMessengerService برای هارنس آزمون.
// پیاده‌سازی اصلی (MessengerService) به سرویس‌های هوش مصنوعی وابسته است و برای
// آزمون «اتصال انبوهٔ بله» لازم نیست؛ اینجا فقط قرارداد لازم برای کنترلر آزمون می‌آید.
//
// نکته: امضای اینترفیس باید دقیقاً با Services/System/MessengerService.cs یکی باشد.
// هر تغییری در قرارداد اصلی باید در این فایل بازتاب یابد.

using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>نتیجهٔ ارسال یک اعلان به پیام‌رسان‌ها (برای نمایش وضعیت در «ارسال آزمایشی»).</summary>
public record MessengerSendResult(bool BaleSent, string? BaleError, bool EitaaSent, string? EitaaError, bool Linked)
{
    public bool AnySent => BaleSent || EitaaSent;
}

public interface IMessengerService
{
    /// <summary>ارسال پیام به کاربر در بله و ایتا (هر کدام که متصل است). خطاها برگردانده می‌شوند و بی‌صدا نمی‌مانند.</summary>
    Task<MessengerSendResult> SendToUserAsync(int userId, string title, string? body);
    /// <summary>ارسال فایل اکسل به بله کاربر لینک‌شده (ایتا فایل نمی‌گیرد).</summary>
    Task<bool> SendExcelToUserAsync(int userId, string caption, byte[] bytes, string fileName);

    /// <summary>همگام‌سازی دستی بله (دکمه‌ی صفحه‌ی کاربران): خواندن آپدیت‌ها، پاسخ به /start و تطبیق شماره‌ها.</summary>
    Task<(int matched, string message)> SyncBaleAsync();

    /// <summary>یک دور خواندن آپدیت‌های بله از offset داده‌شده — خروجی: تعداد اتصال‌های جدید و بزرگ‌ترین update_id.</summary>
    Task<(int linked, long maxUpdateId, string note)> PollBaleAsync(long offset);

    /// <summary>ارسال پیام آزمایشی به کاربر جاری — خروجی: وضعیت هر پیام‌رسان برای نمایش به کاربر/مدیر.</summary>
    Task<MessengerSendResult> SendTestAsync(int userId);

    /// <summary>اعتبارسنجی initData برنامک ایتا و اتصال کاربر (با کد یکبارمصرف) — شناسه چت ایتا ذخیره می‌شود.</summary>
    Task<(bool ok, string message)> LinkEitaaAsync(string code, string initData);

    /// <summary>
    /// آیا شمارهٔ موبایل کاربر به یک اکانت بله تعلق دارد؟ (از مسیر سرویس سفیر).
    /// برای اتصال انبوهٔ کاربران بدون نیاز به اقدام خودِ کاربر استفاده می‌شود.
    /// </summary>
    Task<bool> IsBalePhoneRegisteredAsync(string phone);
}
