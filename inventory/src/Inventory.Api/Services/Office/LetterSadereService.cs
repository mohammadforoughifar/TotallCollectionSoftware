using System.Globalization;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

// ============================================================
//  سرویس نامه صادره — برگرفته از سرویس ارسالی کارفرما (Letter_Sadere_Servises)
//  با رفع باگ‌ها و تطبیق با معماری این پروژه:
//
//  🔴 باگ‌های فایل ارسالی که رفع شد:
//  1. MaxNumber() به اشتباه از InnerLetter می‌خواند → اصلاح به Letter_Saderes
//  2. Delete_Letter_SadereAsync فاقد await روی SaveChangesAsync → اصلاح شد
//  3. CreatorUserId از نوع Guid بود → این پروژه int استفاده می‌کند
//  4. SystemLog در این پروژه وجود ندارد → استفاده از AuditLogFilter سراسری
//  5. Validate() فیلدهای خالی را رد نمی‌کرد → اصلاح شد
//  6. شماره‌گذاری: سال شمسی/شماره ترتیبی (مانند InnerLetter)
// ============================================================

public class LetterSadereService : ILetterSadereService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;

    public LetterSadereService(AppDbContext db, INotifyService notify)
    {
        _db = db;
        _notify = notify;
    }

    // ==================== شماره‌گذاری ====================

    /// <summary>شماره ترتیبی بعدی برای سال جاری شمسی</summary>
    private async Task<int> NextNumberAsync()
    {
        var pc = new PersianCalendar();
        int currentYear = pc.GetYear(DateTime.Now);

        var last = await _db.Letter_Saderes
            .OrderByDescending(s => s.SadereLetterId)
            .Select(s => new { s.Number, s.DateSabt })
            .FirstOrDefaultAsync();

        if (last == null) return 1;

        int lastYear = pc.GetYear(last.DateSabt);
        return lastYear < currentYear ? 1 : last.Number + 1;
    }

    /// <summary>ساخت شماره اندیکاتور: «سال شمسی/شماره»</summary>
    private static string BuildLetterNumber(int number)
    {
        var pc = new PersianCalendar();
        return $"{pc.GetYear(DateTime.Now)}/{number}";
    }

    // ==================== اعتبارسنجی ====================

    private static bool Validate(AddLetterSadereDto dto, out string error)
    {
        error = "";

        if (string.IsNullOrWhiteSpace(dto.Title))
        {
            error = "عنوان نامه الزامی است.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(dto.Text))
        {
            error = "متن نامه الزامی است.";
            return false;
        }

        if (dto.Foriat < 1 || dto.Foriat > 3)
        {
            error = "مقدار فوریت معتبر نیست (1=عادی، 2=فوری، 3=آنی).";
            return false;
        }

        if (dto.Mahramangi < 1 || dto.Mahramangi > 3)
        {
            error = "مقدار محرمانگی معتبر نیست (1=عادی، 2=محرمانه، 3=سری).";
            return false;
        }

        if (dto.MarjeErsalId <= 0)
        {
            error = "مرجع ارسال الزامی است.";
            return false;
        }

        return true;
    }

    // ==================== ایجاد ====================

    public async Task<ApiResult<int>> CreateAsync(AddLetterSadereDto dto, int creatorUserId, string creatorName)
    {
        var result = new ApiResult<int>();

        try
        {
            if (!Validate(dto, out var error))
            {
                result.Success = false;
                result.StatusCode = 400;
                result.Message = error;
                return result;
            }

            var number = await NextNumberAsync();
            var letterNumber = BuildLetterNumber(number);

            // ۱. ساخت LetterSource با SourceType=2 (نامه صادره)
            var source = new LetterSource
            {
                SourceType = 2,
                IsDelete = false
            };
            _db.LetterSources.Add(source);
            await _db.SaveChangesAsync();

            // ۲. ساخت نامه صادره با Id = همان Id سورس
            var letter = new Letter_Sadere
            {
                SadereLetterId = source.Id,
                Foriat = dto.Foriat,
                Mahramangi = dto.Mahramangi,
                CreatorUserId = creatorUserId,
                CreatorSematId = dto.CreatorSematId,
                LetterNumber = letterNumber,
                Number = number,
                Title = dto.Title.Trim(),
                Text = dto.Text?.Trim(),
                DateErsal = dto.DateErsal,
                IsSent = dto.IsSent,
                MarjeErsalId = dto.MarjeErsalId,
                NumberSabtMaghsad = dto.NumberSabtMaghsad,
                GirandeAsli = dto.GirandeAsli?.Trim(),
                TransferName = dto.TransferName?.Trim(),
                IsArchived = false,
                IsDeleted = false,
                DateSabt = DateTime.Now
            };

            _db.Letter_Saderes.Add(letter);
            await _db.SaveChangesAsync();

            // ۳. نوتیفیکیشن
            await _notify.NotifyAsync(new[] { creatorUserId },
                "نامه صادره",
                $"نامه صادره «{letter.Title}» با شماره {letterNumber} ثبت شد.",
                "",
                "");

            result.Success = true;
            result.StatusCode = 200;
            result.Data = letter.SadereLetterId;
            result.Message = $"نامه صادره با شماره {letterNumber} با موفقیت ثبت شد.";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.StatusCode = 500;
            result.Message = $"خطا در ثبت نامه صادره: {ex.Message}";
            return result;
        }
    }

    // ==================== ویرایش ====================

    public async Task<ApiResult> EditAsync(int id, EditLetterSadereDto dto, int userId, bool isAdmin)
    {
        var result = new ApiResult();

        try
        {
            var letter = await _db.Letter_Saderes
                .Include(s => s.Source)
                .FirstOrDefaultAsync(s => s.SadereLetterId == id && !s.IsDeleted);

            if (letter == null)
            {
                result.Success = false;
                result.StatusCode = 404;
                result.Message = "نامه صادره یافت نشد.";
                return result;
            }

            // فقط نویسنده یا ادمین می‌تواند ویرایش کند
            if (letter.CreatorUserId != userId && !isAdmin)
            {
                result.Success = false;
                result.StatusCode = 403;
                result.Message = "شما مجوز ویرایش این نامه را ندارید.";
                return result;
            }

            // نامه ارسال‌شده قابل ویرایش نیست
            if (letter.IsSent && !isAdmin)
            {
                result.Success = false;
                result.StatusCode = 400;
                result.Message = "نامه ارسال‌شده قابل ویرایش نیست.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(dto.Title))
            {
                result.Success = false;
                result.StatusCode = 400;
                result.Message = "عنوان نامه الزامی است.";
                return result;
            }

            letter.Title = dto.Title.Trim();
            letter.Text = dto.Text?.Trim();
            letter.Foriat = dto.Foriat;
            letter.Mahramangi = dto.Mahramangi;
            letter.DateErsal = dto.DateErsal;
            letter.NumberSabtMaghsad = dto.NumberSabtMaghsad;
            letter.GirandeAsli = dto.GirandeAsli?.Trim();
            letter.TransferName = dto.TransferName?.Trim();
            letter.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync();

            result.Success = true;
            result.StatusCode = 200;
            result.Message = "نامه صادره با موفقیت ویرایش شد.";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.StatusCode = 500;
            result.Message = $"خطا در ویرایش نامه صادره: {ex.Message}";
            return result;
        }
    }

    // ==================== حذف منطقی ====================

    public async Task<ApiResult> DeleteAsync(int id, int userId, bool isAdmin)
    {
        var result = new ApiResult();

        try
        {
            var letter = await _db.Letter_Saderes
                .Include(s => s.Source)
                .FirstOrDefaultAsync(s => s.SadereLetterId == id && !s.IsDeleted);

            if (letter == null)
            {
                result.Success = false;
                result.StatusCode = 404;
                result.Message = "نامه صادره یافت نشد.";
                return result;
            }

            if (letter.CreatorUserId != userId && !isAdmin)
            {
                result.Success = false;
                result.StatusCode = 403;
                result.Message = "شما مجوز حذف این نامه را ندارید.";
                return result;
            }

            letter.IsDeleted = true;
            letter.Source.IsDelete = true;
            letter.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync();

            result.Success = true;
            result.StatusCode = 200;
            result.Message = "نامه صادره با موفقیت حذف شد.";
            return result;
        }
        catch
        {
            result.Success = false;
            result.StatusCode = 500;
            result.Message = "خطا در حذف نامه صادره.";
            return result;
        }
    }

    // ==================== لیست ====================

    public async Task<List<LetterSadereListItemDto>> GetListAsync(string? search, bool? archived)
    {
        var query = _db.Letter_Saderes
            .Include(s => s.CreatorUser)
            .Where(s => !s.IsDeleted)
            .AsQueryable();

        if (archived.HasValue)
            query = query.Where(s => s.IsArchived == archived.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(x =>
                x.Title.Contains(s) ||
                x.LetterNumber.Contains(s) ||
                (x.GirandeAsli != null && x.GirandeAsli.Contains(s)));
        }

        return await query
            .OrderByDescending(s => s.SadereLetterId)
            .Select(s => new LetterSadereListItemDto
            {
                Id = s.SadereLetterId,
                LetterNumber = s.LetterNumber,
                Title = s.Title,
                GirandeAsli = s.GirandeAsli,
                Foriat = s.Foriat,
                Mahramangi = s.Mahramangi,
                IsSent = s.IsSent,
                DateErsal = s.DateErsal,
                DateSabt = s.DateSabt,
                CreatorName = s.CreatorUser == null ? "" :
                    (string.IsNullOrWhiteSpace(s.CreatorUser.FirstName) && string.IsNullOrWhiteSpace(s.CreatorUser.LastName))
                        ? s.CreatorUser.Username
                        : $"{s.CreatorUser.FirstName} {s.CreatorUser.LastName}".Trim(),
                IsArchived = s.IsArchived
            })
            .ToListAsync();
    }

    // =================== جزئیات ====================

    public async Task<LetterSadereDetailDto?> GetDetailAsync(int id)
    {
        return await _db.Letter_Saderes
            .Include(s => s.CreatorUser)
            .Where(s => s.SadereLetterId == id && !s.IsDeleted)
            .Select(s => new LetterSadereDetailDto
            {
                Id = s.SadereLetterId,
                LetterNumber = s.LetterNumber,
                Title = s.Title,
                Text = s.Text,
                Foriat = s.Foriat,
                Mahramangi = s.Mahramangi,
                DateErsal = s.DateErsal,
                IsSent = s.IsSent,
                MarjeErsalId = s.MarjeErsalId,
                NumberSabtMaghsad = s.NumberSabtMaghsad,
                GirandeAsli = s.GirandeAsli,
                TransferName = s.TransferName,
                IsArchived = s.IsArchived,
                DateSabt = s.DateSabt,
                CreatorUserId = s.CreatorUserId,
                CreatorName = s.CreatorUser == null ? "" :
                    (string.IsNullOrWhiteSpace(s.CreatorUser.FirstName) && string.IsNullOrWhiteSpace(s.CreatorUser.LastName))
                        ? s.CreatorUser.Username
                        : $"{s.CreatorUser.FirstName} {s.CreatorUser.LastName}".Trim()
            })
            .FirstOrDefaultAsync();
    }
}