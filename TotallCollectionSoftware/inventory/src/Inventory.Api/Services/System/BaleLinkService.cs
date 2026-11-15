using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>وضعیت نتیجهٔ اتصال یک کاربر به بله در عملیات انبوه.</summary>
public enum BaleLinkOutcome
{
    /// <summary>اتصال برقرار شد (شماره به اکانت بله تعلق دارد).</summary>
    Linked,
    /// <summary>کاربر از قبل متصل بود.</summary>
    AlreadyLinked,
    /// <summary>شمارهٔ واردشده معتبر نیست.</summary>
    InvalidNumber,
    /// <summary>این شماره اکانت بله ندارد.</summary>
    NotBaleUser,
    /// <summary>شمارهٔ کاربر ثبت نشده است.</summary>
    NoNumber,
    /// <summary>ارسال با خطا مواجه شد (نتیجهٔ نامشخص).</summary>
    Failed
}

/// <summary>نتیجهٔ اتصال یک کاربر مشخص.</summary>
public record BaleLinkUserResult(
    int UserId,
    string Username,
    string? Phone,
    BaleLinkOutcome Outcome,
    string? Detail)
{
    /// <summary>آیا پیام «اتصال برقرار شد» برای این کاربر ارسال شد؟</summary>
    public bool Linked => Outcome == BaleLinkOutcome.Linked;
}

/// <summary>خلاصهٔ عملیات اتصال انبوهٔ کاربران به بله.</summary>
public record BaleBulkLinkResult(
    int Total,
    int Linked,
    int AlreadyLinked,
    int NotBaleUser,
    int InvalidNumber,
    int NoNumber,
    int Failed,
    bool Completed,
    string Message,
    IReadOnlyList<BaleLinkUserResult> Details)
{
    /// <summary>آیا عملیات با خطای سراسری متوقف شد (مثلاً اعتبار مالی یا سقف نرخ)؟</summary>
    public bool Aborted => !Completed;
}

/// <summary>
/// ================== سرویس اتصال انبوهٔ کاربران به بله ==================
///  • کاربر نباید خودش فرایند اتصال را انجام دهد؛ مدیر سامانه از صفحهٔ «کاربران»
///    یک دکمه را می‌زند و همهٔ کاربرانی که شمارهٔ موبایلشان به یک اکانت بله تعلق دارد
///    به‌صورت خودکار متصل می‌شوند.
///  • تشخیص «شماره متعلق به اکانت بله» از پاسخ سرویس رسمی «سفیر» می‌آید:
///    موفقیت ⇒ اکانت بله دارد؛ خطای ۱۷ (NotBaleUser) ⇒ اکانت بله ندارد.
///  • نتیجهٔ هر کاربر جداگانه شمرده می‌شود تا مدیر بداند چند نفر متصل شدند،
///    چند نفر اکانت بله ندارند و چند شماره نامعتبر بوده است.
/// </summary>
public interface IBaleLinkService
{
    /// <summary>
    /// اتصال انبوهٔ همهٔ کاربران فعالِ دارای شمارهٔ موبایل به بله.
    /// کاربرانی که از قبل متصل‌اند دوباره پیام دریافت نمی‌کنند.
    /// </summary>
    /// <param name="includeAlreadyLinked">اگر true باشد، کاربران متصل هم دوباره بررسی و پیام دریافت می‌کنند.</param>
    Task<BaleBulkLinkResult> LinkAllUsersAsync(bool includeAlreadyLinked = false);
}

public class BaleLinkService : IBaleLinkService
{
    private readonly AppDbContext _db;
    private readonly IBaleSafirService _safir;
    private readonly ILogger<BaleLinkService> _log;

    public BaleLinkService(AppDbContext db, IBaleSafirService safir, ILogger<BaleLinkService> log)
    {
        _db = db;
        _safir = safir;
        _log = log;
    }

    public async Task<BaleBulkLinkResult> LinkAllUsersAsync(bool includeAlreadyLinked = false)
    {
        var details = new List<BaleLinkUserResult>();

        if (!await _safir.IsConfiguredAsync())
            return new BaleBulkLinkResult(0, 0, 0, 0, 0, 0, 0, false,
                "اتصال انبوه انجام نشد: کلید دسترسی سفیر (Api Access Key) و شناسهٔ بازو (Bot Id) در صفحهٔ «تنظیمات ← پیام‌رسان‌ها» وارد نشده است.",
                details);

        // فقط کاربران فعال؛ کاربر غیرفعال اعلان دریافت نمی‌کند.
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.Id)
            .Select(u => new { u.Id, u.Username, u.Mobile, u.BaleChatId, u.BalePhone, u.BalePhoneLinked })
            .ToListAsync();

        var linked = 0;
        var already = 0;
        var notBale = 0;
        var invalid = 0;
        var noNumber = 0;
        var failed = 0;
        var completed = true;
        var stopReason = (string?)null;

        foreach (var u in users)
        {
            // شمارهٔ نرمال‌شدهٔ قبلی (اگر قبلاً تأیید شده) — مبنای «از قبل متصل»
            var existing = NormalizeStored(u.BalePhone);

            if (string.IsNullOrWhiteSpace(u.Mobile))
            {
                noNumber++;
                details.Add(new BaleLinkUserResult(u.Id, u.Username, null, BaleLinkOutcome.NoNumber, "شمارهٔ موبایل ثبت نشده است."));
                continue;
            }

            var phone = BaleSafirService.NormalizePhone(u.Mobile);
            if (phone == null)
            {
                invalid++;
                details.Add(new BaleLinkUserResult(u.Id, u.Username, u.Mobile, BaleLinkOutcome.InvalidNumber,
                    "شمارهٔ موبایل معتبر نیست یا به قالب ایرانی نیست."));
                continue;
            }

            // اگر قبلاً متصل بوده و تکرار نخواسته — بدون پیام جدید از او رد می‌شویم.
            var wasLinked = u.BalePhoneLinked && existing != null;
            var hadChat = !string.IsNullOrWhiteSpace(u.BaleChatId);
            if (wasLinked && !includeAlreadyLinked)
            {
                already++;
                details.Add(new BaleLinkUserResult(u.Id, u.Username, phone, BaleLinkOutcome.AlreadyLinked,
                    hadChat ? "از قبل متصل (شناسهٔ چت موجود است)." : "از قبل متصل است."));
                continue;
            }

            // پیام خوش‌آمدگویی — همان پیامی که اتصال را برای کاربر محسوس می‌کند.
            var text = BuildWelcome(u.Username);
            // request_id پایدار و یکتا برای هر کاربر: اجرای دوبارهٔ عملیات پیام تکراری نمی‌فرستد.
            var requestId = $"bale-link-u{u.Id}-{phone}";

            BaleSendResult result;
            try
            {
                result = await _safir.SendTextAsync(phone, text, requestId);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "اتصال بلهٔ کاربر {UserId} ناموفق بود", u.Id);
                result = new BaleSendResult(BaleSendStatus.NetworkError, Error: ex.Message);
            }

            switch (result.Status)
            {
                case BaleSendStatus.Sent:
                    linked++;
                    await SaveLinkAsync(u.Id, phone);
                    details.Add(new BaleLinkUserResult(u.Id, u.Username, phone, BaleLinkOutcome.Linked,
                        "متصل شد؛ پیام تأیید ارسال شد."));
                    break;

                case BaleSendStatus.NotBaleUser:
                    notBale++;
                    details.Add(new BaleLinkUserResult(u.Id, u.Username, phone, BaleLinkOutcome.NotBaleUser,
                        "این شماره اکانت بله ندارد."));
                    break;

                case BaleSendStatus.InvalidPhone:
                    invalid++;
                    details.Add(new BaleLinkUserResult(u.Id, u.Username, phone, BaleLinkOutcome.InvalidNumber,
                        result.Error ?? "سفیر این شماره را نامعتبر اعلام کرد."));
                    break;

                default:
                    failed++;
                    details.Add(new BaleLinkUserResult(u.Id, u.Username, phone, BaleLinkOutcome.Failed,
                        result.Error ?? result.Describe()));
                    // خطاهای سراسری (اعتبار مالی، سقف نرخ، سقف مخاطبین) ادامهٔ کار را بی‌فایده می‌کنند.
                    if (result.Status is BaleSendStatus.PaymentRequired or BaleSendStatus.ContactLimitReached)
                    {
                        completed = false;
                        stopReason = result.Status == BaleSendStatus.PaymentRequired
                            ? "اعتبار مالی سرویس سفیر تمام شد؛ عملیات متوقف شد."
                            : "به سقف تعداد مخاطبین بازو رسیدید؛ عملیات متوقف شد.";
                    }
                    else if (result.Status == BaleSendStatus.RateLimited)
                    {
                        completed = false;
                        stopReason = "به سقف نرخ ارسال سرویس سفیر رسیدید؛ عملیات متوقف شد. لطفاً کمی بعد دوباره تلاش کنید.";
                    }
                    break;
            }

            if (!completed) break;
        }

        var summary = BuildSummary(total: users.Count, linked, already, notBale, invalid, noNumber, failed);
        var message = stopReason == null
            ? summary
            : $"{summary} — {stopReason}";

        return new BaleBulkLinkResult(users.Count, linked, already, notBale, invalid, noNumber, failed, completed, message, details);
    }

    /// <summary>ذخیرهٔ نتیجهٔ اتصال بر پایهٔ شماره برای کاربر.</summary>
    private async Task SaveLinkAsync(int userId, string phone)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return;
        user.BalePhone = phone;
        user.BalePhoneLinked = true;
        await _db.SaveChangesAsync();
    }

    private static string? NormalizeStored(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        var p = BaleSafirService.NormalizePhone(stored);
        return p ?? stored.Trim();
    }

    /// <summary>متن خوش‌آمدگویی که برای کاربر ارسال می‌شود.</summary>
    private static string BuildWelcome(string username)
        => $"✅ اتصال پیام‌رسان بله برقرار شد\n\n" +
           $"سلام {username} 👋\n" +
           $"حساب شما در سامانهٔ انبار و فروش به بله متصل شد.\n" +
           $"از این پس اعلان‌ها، یادآوری‌ها و گزارش‌های سامانه به همین شماره ارسال می‌شود.\n" +
           $"———\n📱 سامانه انبار و فروش";

    /// <summary>خلاصهٔ قابل‌نمایش نتیجهٔ عملیات.</summary>
    internal static string BuildSummary(int total, int linked, int already, int notBale, int invalid, int noNumber, int failed)
    {
        var parts = new List<string> { $"کل کاربران فعال: {total}" };
        if (linked > 0) parts.Add($"متصل‌شده: {linked}");
        if (already > 0) parts.Add($"از قبل متصل: {already}");
        if (notBale > 0) parts.Add($"بدون اکانت بله: {notBale}");
        if (invalid > 0) parts.Add($"شمارهٔ نامعتبر: {invalid}");
        if (noNumber > 0) parts.Add($"بدون شماره: {noNumber}");
        if (failed > 0) parts.Add($"ناموفق: {failed}");
        return string.Join(" • ", parts);
    }
}
