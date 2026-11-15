using Inventory.Api.Services.Moadian;
using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Metrics;
using Inventory.Licensing.Modules;
using Inventory.Shared.Dtos.Moadian;

namespace Inventory.Api.Licensing.Services;

/// <summary>سرنوشت یک ارسال دارای سهمیه.</summary>
public enum QuotaSendOutcome
{
    /// <summary>سقف پر بود؛ <b>هیچ تماسی</b> با بیرون انجام نشد.</summary>
    Rejected = 0,

    /// <summary>ارسال انجام شد و مصرف تأیید شد.</summary>
    Sent = 1,

    /// <summary>ارسال قطعاً شکست خورد؛ واحد رزروشده بازگردانده شد.</summary>
    Failed = 2,

    /// <summary>نتیجه نامعلوم است (احتمالاً پس از ارسال قطع شد)؛ واحد بازنگشت و ردیف Pending شد.</summary>
    Pending = 3
}

/// <summary>
/// هماهنگ‌کنندهٔ «رزرو ← تماس ← تسویه» برای عملیات شبکه‌ایِ سهمیه‌دار (ارسال به مودیان).
///
/// <para>
/// <b>چرا این کلاس جدا شد؟</b> سه دلیل مشخص:
/// <list type="number">
///   <item>ترتیب درست (رزرو اتمیک قبل از تماس، تماس بیرون از تراکنش) باید در یک جا
///   باشد، نه پراکنده در کنترلر — وگرنه نفر بعدی دوباره «اول بفرست، بعد بشمار» می‌نویسد.</item>
///   <item>سه سرنوشت (موفق / شکست قطعی / نامعلوم) باید صریح مدل شوند؛ قبلاً همه در یک
///   شاخهٔ «ناموفق» قاطی می‌شدند و نتیجه‌اش این بود که Timeout پس از ارسال، سهمیه را
///   بی‌صدا می‌خورد.</item>
///   <item>قابل تست با Mock: تابع ارسال به‌صورت پارامتر می‌آید، پس تست می‌تواند ۵۰ درخواست
///   هم‌زمان روی «آخرین واحد» بفرستد و <b>شمارش واقعی تماس‌ها</b> را بسنجد.</item>
/// </list>
/// </para>
/// </summary>
public sealed class QuotaSendCoordinator
{
    private readonly QuotaService _quota;
    private readonly ILogger<QuotaSendCoordinator> _log;

    public QuotaSendCoordinator(QuotaService quota, ILogger<QuotaSendCoordinator> log)
    {
        _quota = quota;
        _log = log;
    }

    /// <summary>کد وضعیت 408 — «سرور در مهلت پاسخ نداد» (نتیجه نامعلوم).</summary>
    public const int StatusRequestTimeout = 408;

    /// <summary>کد وضعیت 429 — «درخواست پردازش نشد» (قطعی: انجام نشد).</summary>
    public const int StatusTooManyRequests = 429;

    /// <summary>
    /// یکسان‌سازی تصمیم بر پایهٔ کد وضعیت — یک‌جا و قابل تست.
    ///
    /// <list type="bullet">
    ///   <item><b>408</b> ⇒ <c>DeliveryUncertain = true</c> (نامعلوم). سرور نگفته «رد شد»؛
    ///   گفته «در مهلت دریافت/پاسخ ندادم». درخواست ممکن است در صف پردازش باشد.</item>
    ///   <item><b>429</b> ⇒ <c>DeliveryUncertain = false</c> (قطعی). سامانه صریحاً گفته
    ///   «پردازش نکردم، زیادی درخواست دادی»؛ سهمیه باید برگردد.</item>
    /// </list>
    ///
    /// <para>
    /// این قاعده بر پرچمی که لایهٔ پایین ست کرده اولویت دارد، چون آن لایه به «بستهٔ ارسال
    /// ساخته شد یا نه» نگاه می‌کند و ممکن است در ۴۲۹ هم بسته ساخته باشد.
    /// </para>
    /// </summary>
    public static void NormalizeByStatusCode(ApiModel? model)
    {
        if (model is null || model.Success) return;

        if (model.Status == StatusTooManyRequests)
        {
            model.DeliveryUncertain = false;
            return;
        }

        if (model.Status == StatusRequestTimeout)
            model.DeliveryUncertain = true;
    }

    /// <summary>
    /// ارسال با رزرو سهمیه.
    /// </summary>
    /// <param name="entitlement">لایسنس مؤثر (پیش از فراخوانی یک‌بار خوانده می‌شود).</param>
    /// <param name="send">تابع تماس با بیرون — <b>هیچ</b> تراکنش دیتابیسی نباید باز باشد.</param>
    public async Task<(ApiModel Result, QuotaSendOutcome Outcome)> SendAsync(
        EffectiveEntitlement entitlement,
        Func<CancellationToken, Task<ApiModel>> send,
        string module = ModuleCodes.Moadian,
        string metric = MetricCodes.MoadianInvoiceSend,
        string entityType = "MoadianInvoice",
        string? entityId = null,
        CancellationToken cancellationToken = default)
    {
        // ---------- ۱) رزرو اتمیک در تراکنش جدا و کوتاه ----------
        var reserve = await _quota.ReserveAsync(entitlement, module, metric, 1, entityType, entityId,
            cancellationToken);

        if (!reserve.Result.Allowed)
        {
            _log.LogWarning("[License] ارسال مودیان پیش از تماس رد شد (سقف پر است): {Message}", reserve.Result.Message);
            return (new ApiModel { Success = false, Status = 403, Message = reserve.Result.Message },
                QuotaSendOutcome.Rejected);
        }

        // شناسهٔ ردیف‌های رزرو — تا تسویه بداند کدام ردیف را ببندد.
        var reservationIds = reserve.ReservationIds;

        // ---------- ۲) تماس با بیرون — هیچ تراکنشی باز نیست ----------
        ApiModel result;
        try
        {
            result = await send(cancellationToken);
        }
        catch (Exception ex)
        {
            // استثنای رهاشده یعنی نمی‌دانیم درخواست رسیده یا نه ⇒ محافظه‌کارانه «نامعلوم».
            result = new ApiModel
            {
                Success = false,
                Status = 500,
                Message = "ارسال به سامانهٔ مودیان با خطا پایان یافت: " + ex.Message,
                DeliveryUncertain = true
            };
        }

        // ---------- ۲.۵) بازتنظیم دقیق بر پایهٔ کد وضعیت ----------
        //
        // دو کد HTTP معنايی دارند که نباید به حدسِ لایهٔ پایین واگذار شود (بازبینی چهارم):
        //   • 408 Request Timeout ⇒ سرور درخواست را در مهلت مقرر دریافت **نکرد**؛ در عمل
        //     یعنی «نمی‌دانیم رسیده یا نه» ⇒ نامعلوم ⇒ Pending (سهمیه برنمی‌گردد).
        //   • 429 Too Many Requests ⇒ درخواست **پردازش نشد** (سهمیهٔ ارسال سامانه پر بود)
        //     ⇒ قطعیِ «انجام نشد» ⇒ بازگشت سهمیه با LICENSE_QUOTA_REFUND_TECHNICAL.
        // اگر لایهٔ پایین برعکس این‌ها را ست کرده باشد، همین‌جا اصلاح می‌شود؛ مرجع، کد است.
        NormalizeByStatusCode(result);

        // ---------- ۳) تسویه ----------
        if (result.Success)
        {
            await _quota.ConfirmAsync(entitlement, module, metric, entityType, entityId, reservationIds,
                cancellationToken);

            if (reserve.Result.WarningMessage is { } warning)
                _log.LogWarning("[License] هشدار سهمیهٔ مودیان: {Warning} ({Used}/{Limit})",
                    warning, reserve.Result.Used, reserve.Result.Limit);

            return (result, QuotaSendOutcome.Sent);
        }

        if (result.DeliveryUncertain == true)
        {
            // ممکن است انجام شده باشد ⇒ نه بازگشت، نه تکرار خودکار؛ فقط علامت.
            await _quota.MarkPendingAsync(entitlement, module, metric, entityType, entityId,
                note: result.Message, reservationIds: reservationIds, cancellationToken: cancellationToken);

            result.Message = string.IsNullOrWhiteSpace(result.Message)
                ? "نتیجهٔ ارسال نامعلوم است؛ مصرف سهمیه بازگردانده نشد و برای بررسی دستی علامت‌گذاری شد."
                : result.Message + " | نتیجهٔ ارسال نامعلوم است: مصرف سهمیه بازگردانده نشد و پرونده برای بررسی دستی علامت‌گذاری شد.";

            return (result, QuotaSendOutcome.Pending);
        }

        await _quota.RefundTechnicalAsync(entitlement, module, metric, 1, entityType, entityId,
            reservationIds, cancellationToken);
        return (result, QuotaSendOutcome.Failed);
    }
}
