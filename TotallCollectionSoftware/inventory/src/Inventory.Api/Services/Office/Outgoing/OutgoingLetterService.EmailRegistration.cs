using Inventory.Api.Services.Office.Email;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Office.Outgoing;

public partial class OutgoingLetterService
{
    /// <summary>ثبت دبیرخانه: شماره ثبت مقصد + روش ارسال — فقط برای نامه‌های امضا شده</summary>
    public async Task<DabirkhaneRegisterResultDto> DabirkhaneRegisterAsync(int letterId, DabirkhaneRegisterDto dto, int userId, string userName)
    {
        var letter = await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new EmailPreparationException("REGISTRATION_VALIDATION", "نامه پیدا نشد.");

        // 🔒 شرط کارفرما: نامه فقط وقتی در دبیرخانه ثبت می‌شود که امضا شده باشد (SadereNumber مقدار گرفته)
        if (string.IsNullOrWhiteSpace(letter.SadereNumber))
            throw new EmailPreparationException("REGISTRATION_VALIDATION", "این نامه هنوز امضا نشده است — فقط نامه‌های امضا شده (دارای شماره صادره) در دبیرخانه ثبت می‌شوند.");

        if (string.IsNullOrWhiteSpace(dto.SendMethod))
            throw new EmailPreparationException("REGISTRATION_VALIDATION", "روش ارسال نامه الزامی است.");

        var method = dto.SendMethod.Trim();

        // ==================== اعتبارسنجی فیلدهای وابسته به روش ارسال ====================
        // هر روش ارسال اطلاعات متفاوتی لازم دارد:
        //   • ایمیل            → کدوم ایمیل (آدرس ایمیل مقصد)
        //   • پست / پست پیشتاز  → نام تحویل گیرنده (+ کد رهگیری)
        //   • پیک / تحویل حضوری → نام تحویل گیرنده
        //   • فکس              → شماره فکس مقصد
        //   • اتوماسیون (ECE)  → شماره ثبت مقصد
        var spec = LetterSendMethods.Spec(method);

        bool byEmail = dto.SendByEmail || spec.NeedsDestEmail;
        if (byEmail && (dto.EmailRequestId == Guid.Empty || dto.EmailAccountId is not > 0))
            throw new EmailPreparationException("EMAIL_REQUEST_REQUIRED", "حساب رسمی و کد پیگیری ارسال الزامی است؛ صفحه را کامل بازخوانی کنید. هیچ ایمیلی از این درخواست ارسال نشده است.");
        if (byEmail && !await _db.OtoEmails.AnyAsync(a => a.EmailId == dto.EmailAccountId && a.IsDabirkhane))
            throw new EmailPreparationException("ACCOUNT_UNAVAILABLE", "حساب رسمی انتخاب‌شده در دسترس نیست؛ حساب شخصی برای نامهٔ دبیرخانه مجاز نیست.");
        string? destEmail = dto.DestEmail?.Trim();
        string? delivererName = dto.DelivererName?.Trim();
        string? trackingCode = dto.TrackingCode?.Trim();
        string? destFax = dto.DestFax?.Trim();
        string? destRegNumber = dto.DestRegNumber?.Trim();

        if (byEmail && string.IsNullOrWhiteSpace(destEmail))
            throw new EmailPreparationException("REGISTRATION_VALIDATION", "آدرس ایمیل مقصد را وارد کنید (مشخص کنید نامه به کدوم ایمیل ارسال شود).");

        if (byEmail && !string.IsNullOrWhiteSpace(destEmail) && !destEmail.Contains('@'))
            throw new EmailPreparationException("REGISTRATION_VALIDATION", "آدرس ایمیل مقصد معتبر نیست.");

        if (spec.NeedsDelivererName && string.IsNullOrWhiteSpace(delivererName))
            throw new EmailPreparationException("REGISTRATION_VALIDATION", "نام تحویل گیرنده را وارد کنید.");

        if (spec.TrackingCodeRequired && string.IsNullOrWhiteSpace(trackingCode))
            throw new EmailPreparationException("REGISTRATION_VALIDATION", "کد رهگیری مرسوله را وارد کنید.");

        if (spec.NeedsFax && string.IsNullOrWhiteSpace(destFax))
            throw new EmailPreparationException("REGISTRATION_VALIDATION", "شماره فکس مقصد را وارد کنید.");

        if (spec.NeedsDestRegNumber && string.IsNullOrWhiteSpace(destRegNumber))
            throw new EmailPreparationException("REGISTRATION_VALIDATION", "شماره ثبت مقصد (ECE) را وارد کنید.");

        if (byEmail && destEmail!.Length > 250)
            throw new EmailPreparationException("DEST_EMAIL_LIMIT", "فهرست ایمیل مقصد نامه باید حداکثر ۲۵۰ نویسه باشد؛ درخواست ثبت و ارسال انجام نشد.");
        if (byEmail)
        {
            // A sequential replay/conflict must not reassign the original registrar or overwrite
            // already-committed registration fields before discovering the duplicate request.
            var previous = await _db.EmailSendAttempts.AsNoTracking().SingleOrDefaultAsync(a => a.RequestId == dto.EmailRequestId);
            if (previous != null)
            {
                if (previous.RequestedByUserId != userId || previous.EmailAccountId != dto.EmailAccountId || previous.LetterSourceId != letterId
                    || previous.RequestHash != EmailService.LetterRequestFingerprint(letterId, dto, userId))
                    return new() { RegistrationSaved = null, Email = new()
                    {
                        RequestId = dto.EmailRequestId, LetterSourceId = letterId, Status = EmailDeliveryStatus.Unknown,
                        Code = "REQUEST_ID_CONFLICT", Stage = "request", ResultPersisted = false,
                        Message = "این کد پیگیری برای درخواست متفاوتی استفاده شده است؛ ثبت و ارسال تازه‌ای انجام نشد. نتیجهٔ درخواست قبلی را بررسی کنید."
                    } };
                var replay = EmailDispatchStore.ToDto(previous);
                replay.Replayed = true;
                return new() { RegistrationSaved = true, Email = replay };
            }
        }

        letter.DabirkhaneSabt = true;
        letter.DabirkhaneUserId = userId;
        letter.DateDabirkhane ??= DateTime.Now;
        letter.DestRegNumber = destRegNumber;
        letter.SendMethod = method;
        letter.DabirkhaneNote = dto.Note?.Trim();
        if (byEmail) letter.DestEmail = destEmail;

        // فیلدهای وابسته به روش ارسال — فقط مقادیر مرتبط با روش انتخابی ذخیره می‌شوند
        letter.DelivererName = spec.NeedsDelivererName ? delivererName : null;
        letter.TrackingCode = spec.NeedsTrackingCode ? trackingCode : null;
        letter.DestFax = spec.NeedsFax ? destFax : null;

        await _db.SaveChangesAsync();

        // Registration and SMTP are separate outcomes. An error cannot undo a committed registration.
        var result = new DabirkhaneRegisterResultDto { RegistrationSaved = true };
        if (byEmail)
        {
            try { result.Email = await _email.SendLetterEmailAsync(letterId, dto, userId, userName); }
            catch (EmailPreparationException ex)
            {
                result.Email = new EmailDeliveryResultDto
                {
                    RequestId = dto.EmailRequestId, LetterSourceId = letterId, Status = EmailDeliveryStatus.Failed,
                    Stage = "prepare", Code = ex.Code, Message = ex.Message, ResultPersisted = false
                };
            }
            catch (Exception ex)
            {
                _deliveryLog.LogError("Letter {LetterId}, email {RequestId}: result unavailable ({ErrorType}); no automatic resend.",
                    letterId, dto.EmailRequestId, ex.GetType().Name);
                result.Email = EmailDeliveryText.Uncertain(dto.EmailRequestId);
                result.Email.LetterSourceId = letterId;
            }
        }

        // Do not duplicate notifications on replay, or turn a post-commit notification failure into a send failure.
        if (result.Email?.Replayed != true)
        {
            try
            {
                await _notify.SendAsync(letter.CreatorUserId,
                    $"نامه صادره در دبیرخانه ثبت شد: {letter.Title}",
                    $"شماره صادره: {letter.SadereNumber} — روش ارسال: {letter.SendMethod}" +
                    (string.IsNullOrWhiteSpace(letter.DestRegNumber) ? "" : $" — شماره ثبت مقصد: {letter.DestRegNumber}") +
                    (result.Email == null ? "" : $" — ایمیل: {EmailDeliveryText.Status(result.Email.Status)} — کد پیگیری: {result.Email.RequestId}"),
                    userName, "دبیرخانه صادره", $"outgoing-letters/view/{letterId}");
            }
            catch (Exception ex)
            {
                _deliveryLog.LogWarning("Letter {LetterId}: post-registration notification failed ({ErrorType}).", letterId, ex.GetType().Name);
                result.Warning = "نامه در دبیرخانه ثبت شد، اما اعلان ایجادکننده ارسال نشد. ثبت یا ارسال ایمیل را تکرار نکنید.";
                if (result.Email != null)
                    result.Email = await _email.AddDeliveryWarningAsync(result.Email, "notify", "NOTIFICATION_WARNING", result.Warning);
            }
        }
        try { await _notify.BroadcastChangedAsync("outgoing-letters"); }
        catch (Exception ex)
        {
            _deliveryLog.LogWarning("Letter {LetterId}: post-registration broadcast failed ({ErrorType}).", letterId, ex.GetType().Name);
            result.Warning = "ثبت دبیرخانه انجام شد، اما تازه‌سازی زندهٔ فهرست‌ها با خطا مواجه شد؛ صفحه را به‌روزرسانی کنید، نه ارسال را.";
            if (result.Email != null)
                result.Email = await _email.AddDeliveryWarningAsync(result.Email, "notify", "BROADCAST_WARNING", result.Warning);
        }
        return result;
    }

}
