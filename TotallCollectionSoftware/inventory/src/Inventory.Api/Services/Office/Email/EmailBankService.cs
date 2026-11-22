using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Office.Email;

// ============================================================
//  بانک ایمیل مخاطبین دبیرخانه
//  • کاربر دبیرخانه در فرم «دبیرخانه یکپارچه» آدرس را از فهرست پیشنهاد انتخاب
//    می‌کند (اول آدرس‌های همان سازمان گیرنده، بعد کل بانک).
//  • هر آدرسی که انتخاب/استفاده شود خودکار در بانک می‌ماند.
//  • آدرس تکراری ردیف دوم نمی‌سازد؛ فقط UseCount/LastUsedAt به‌روز و
//    Duplicate=true برمی‌گردد تا رابط کاربری پیام «قبلاً ثبت شده» بدهد.
// ============================================================

public interface IEmailBankService
{
    /// <summary>پیشنهاد آدرس‌ها برای فرم ارسال — اول سازمان گیرنده، بعد کل بانک</summary>
    Task<EmailBankSuggestDto> SuggestAsync(string? term, string? organization, int take = 12);

    /// <summary>افزودن/به‌خاطر‌سپاری یک آدرس (تکراری ⇒ Duplicate=true و بدون ردیف جدید)</summary>
    Task<EmailBankSaveResultDto> RememberAsync(string email, string? organization, string? personName,
        int? userId, string? userName, bool markUsed = true);

    /// <summary>فهرست صفحه‌بندی‌شدهٔ بانک (صفحهٔ مدیریت)</summary>
    Task<PagedResult<EmailBankEntryDto>> ListAsync(string? search, bool? isActive, int page, int pageSize);

    /// <summary>ثبت/ویرایش دستی ردیف بانک</summary>
    Task<EmailBankSaveResultDto> SaveAsync(SaveEmailBankEntryDto dto, int? userId, string? userName);

    Task DeleteAsync(int id);

    /// <summary>ادغام دو ردیف تکراری: شمار استفاده و آخرین استفاده به ردیف باقی‌مانده منتقل و دیگری حذف می‌شود</summary>
    Task<bool> MergeAsync(int keepId, int removeId);

    /// <summary>آیا این آدرس از قبل در بانک هست؟ (برای پیام تکراری در فرم)</summary>
    Task<EmailBankEntryDto?> FindAsync(string email);
}

public class EmailBankService : IEmailBankService
{
    private readonly AppDbContext _db;

    public EmailBankService(AppDbContext db) => _db = db;

    // ==================== کمکی ====================

    /// <summary>پاک‌سازی آدرس: حذف فاصله‌های اضافی و یکسان‌سازی حروف</summary>
    private static string Clean(string? email) => (email ?? "").Trim().Trim('،', ';', ',');

    /// <summary>اعتبارسنجی سبک — نه جایگزین اعتبارسنجی DTO، بلکه محافظ پیش از ذخیره</summary>
    private static bool LooksLikeEmail(string email)
        => email.Contains('@') && email.IndexOf('@') > 0 && email.LastIndexOf('.') > email.IndexOf('@') + 1;

    private static EmailBankEntryDto Map(OtoEmailBankEntry e) => new()
    {
        Id = e.Id,
        Email = e.Email,
        Organization = e.Organization,
        PersonName = e.PersonName,
        Note = e.Note,
        IsActive = e.IsActive,
        CreatedAt = e.CreatedAt,
        LastUsedAt = e.LastUsedAt,
        UseCount = e.UseCount,
        CreatedBy = e.CreatedByName
    };

    // ==================== جست‌وجو / پیشنهاد ====================

    public async Task<EmailBankEntryDto?> FindAsync(string email)
    {
        var clean = Clean(email);
        if (clean.Length == 0) return null;
        var row = await _db.OtoEmailBank.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Email.ToLower() == clean.ToLower());
        return row is null ? null : Map(row);
    }

    public async Task<EmailBankSuggestDto> SuggestAsync(string? term, string? organization, int take = 12)
    {
        take = Math.Clamp(take, 1, 50);
        var result = new EmailBankSuggestDto();
        var q = term?.Trim() ?? "";
        var org = organization?.Trim() ?? "";

        // ۱) آدرس‌های همان سازمان گیرنده — مستقل از عبارت جست‌وجو، تا کاربر
        //    با انتخاب سازمان همهٔ آدرس‌های ثبت‌شدهٔ آن را ببیند.
        if (org.Length > 0)
        {
            result.OrganizationMatches = await _db.OtoEmailBank.AsNoTracking()
                .Where(e => e.IsActive && e.Organization != null && e.Organization.ToLower() == org.ToLower())
                .OrderByDescending(e => e.UseCount).ThenByDescending(e => e.LastUsedAt)
                .Take(take)
                .Select(e => Map(e))
                .ToListAsync();
        }

        // ۲) بقیهٔ آدرس‌های بانک: اگر عبارتی تایپ نشده، پرکاربردترین‌ها پیشنهاد می‌شوند
        //    تا با باز‌کردن کادر ایمیل، بانک خالی به‌نظر نرسد.
        {
            var query = _db.OtoEmailBank.AsNoTracking().Where(e => e.IsActive);
            if (q.Length > 0)
            {
                var lower = q.ToLower();
                query = query.Where(e => e.Email.ToLower().Contains(lower)
                    || (e.Organization != null && e.Organization.ToLower().Contains(lower))
                    || (e.PersonName != null && e.PersonName.ToLower().Contains(lower))
                    || (e.Note != null && e.Note.ToLower().Contains(lower)));
            }
            var takenIds = result.OrganizationMatches.Select(x => x.Id).ToHashSet();
            result.OtherMatches = (await query
                    .OrderByDescending(e => e.UseCount).ThenByDescending(e => e.LastUsedAt)
                    .Take(take + takenIds.Count)
                    .Select(e => Map(e))
                    .ToListAsync())
                .Where(e => !takenIds.Contains(e.Id))
                .Take(take)
                .ToList();
        }

        return result;
    }

    // ==================== ثبت/به‌خاطر‌سپاری ====================

    public async Task<EmailBankSaveResultDto> RememberAsync(string email, string? organization, string? personName,
        int? userId, string? userName, bool markUsed = true)
    {
        var clean = Clean(email);
        if (!LooksLikeEmail(clean))
            return new EmailBankSaveResultDto { Message = "آدرس ایمیل معتبر نیست." };

        var existing = await _db.OtoEmailBank.FirstOrDefaultAsync(e => e.Email.ToLower() == clean.ToLower());
        if (existing is not null)
        {
            if (markUsed)
            {
                existing.UseCount += 1;
                existing.LastUsedAt = Services.Core.TehranClock.Now();
                if (!string.IsNullOrWhiteSpace(organization) && string.IsNullOrWhiteSpace(existing.Organization))
                    existing.Organization = organization.Trim();
                if (!string.IsNullOrWhiteSpace(personName) && string.IsNullOrWhiteSpace(existing.PersonName))
                    existing.PersonName = personName.Trim();
                await _db.SaveChangesAsync();
            }
            return new EmailBankSaveResultDto
            {
                Id = existing.Id,
                Created = false,
                Duplicate = true,
                Message = $"این آدرس قبلاً در بانک ثبت شده است{(string.IsNullOrWhiteSpace(existing.Organization) ? "" : $" — {existing.Organization}")}.",
                Entry = Map(existing)
            };
        }

        var row = new OtoEmailBankEntry
        {
            Email = clean,
            Organization = string.IsNullOrWhiteSpace(organization) ? null : organization.Trim(),
            PersonName = string.IsNullOrWhiteSpace(personName) ? null : personName.Trim(),
            IsActive = true,
            CreatedAt = Services.Core.TehranClock.Now(),
            LastUsedAt = markUsed ? Services.Core.TehranClock.Now() : null,
            UseCount = markUsed ? 1 : 0,
            CreatedByUserId = userId,
            CreatedByName = string.IsNullOrWhiteSpace(userName) ? null : userName.Trim()
        };
        _db.OtoEmailBank.Add(row);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // رقابت هم‌زمان: ردیف بین بررسی و درج توسط کاربر دیگری ساخته شد
            _db.Entry(row).State = EntityState.Detached;
            var again = await _db.OtoEmailBank.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Email.ToLower() == clean.ToLower());
            if (again is null) throw;
            return new EmailBankSaveResultDto
            {
                Id = again.Id, Created = false, Duplicate = true,
                Message = "این آدرس قبلاً در بانک ثبت شده است.",
                Entry = Map(again)
            };
        }

        return new EmailBankSaveResultDto
        {
            Id = row.Id, Created = true, Duplicate = false,
            Message = "آدرس در بانک ایمیل مخاطبین ذخیره شد.",
            Entry = Map(row)
        };
    }

    // ==================== مدیریت (صفحهٔ بانک) ====================

    public async Task<PagedResult<EmailBankEntryDto>> ListAsync(string? search, bool? isActive, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = _db.OtoEmailBank.AsNoTracking().AsQueryable();

        var s = search?.Trim() ?? "";
        if (s.Length > 0)
        {
            var lower = s.ToLower();
            q = q.Where(e => e.Email.ToLower().Contains(lower)
                || (e.Organization != null && e.Organization.ToLower().Contains(lower))
                || (e.PersonName != null && e.PersonName.ToLower().Contains(lower))
                || (e.Note != null && e.Note.ToLower().Contains(lower)));
        }
        if (isActive.HasValue) q = q.Where(e => e.IsActive == isActive.Value);

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(e => e.UseCount).ThenByDescending(e => e.LastUsedAt).ThenBy(e => e.Email)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => Map(e))
            .ToListAsync();

        return new PagedResult<EmailBankEntryDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    public async Task<EmailBankSaveResultDto> SaveAsync(SaveEmailBankEntryDto dto, int? userId, string? userName)
    {
        var clean = Clean(dto.Email);
        if (!LooksLikeEmail(clean))
            return new EmailBankSaveResultDto { Message = "آدرس ایمیل معتبر نیست." };

        var duplicate = await _db.OtoEmailBank
            .FirstOrDefaultAsync(e => e.Email.ToLower() == clean.ToLower() && e.Id != dto.Id);

        if (dto.Id > 0)
        {
            var row = await _db.OtoEmailBank.FirstOrDefaultAsync(e => e.Id == dto.Id)
                ?? throw new Exception("ردیف بانک ایمیل پیدا نشد.");
            if (duplicate is not null)
                return new EmailBankSaveResultDto
                {
                    Id = duplicate.Id, Duplicate = true,
                    Message = $"این آدرس قبلاً برای «{duplicate.Organization ?? "بدون سازمان"}» ثبت شده است — ردیف تکراری مجاز نیست.",
                    Entry = Map(duplicate)
                };

            row.Email = clean;
            row.Organization = string.IsNullOrWhiteSpace(dto.Organization) ? null : dto.Organization.Trim();
            row.PersonName = string.IsNullOrWhiteSpace(dto.PersonName) ? null : dto.PersonName.Trim();
            row.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            row.IsActive = dto.IsActive;
            await _db.SaveChangesAsync();

            return new EmailBankSaveResultDto
            {
                Id = row.Id, Created = false, Duplicate = false,
                Message = "ردیف بانک ایمیل ویرایش شد.", Entry = Map(row)
            };
        }

        if (duplicate is not null)
            return new EmailBankSaveResultDto
            {
                Id = duplicate.Id, Duplicate = true,
                Message = $"این آدرس قبلاً برای «{duplicate.Organization ?? "بدون سازمان"}» ثبت شده است.",
                Entry = Map(duplicate)
            };

        var created = await RememberAsync(clean, dto.Organization, dto.PersonName, userId, userName, markUsed: false);
        if (dto.Note is { Length: > 0 } && created.Id > 0)
        {
            var row = await _db.OtoEmailBank.FirstOrDefaultAsync(e => e.Id == created.Id);
            if (row is not null) { row.Note = dto.Note.Trim(); await _db.SaveChangesAsync(); }
        }
        return created;
    }

    public async Task DeleteAsync(int id)
    {
        var row = await _db.OtoEmailBank.FirstOrDefaultAsync(e => e.Id == id)
            ?? throw new Exception("ردیف بانک ایمیل پیدا نشد.");
        _db.OtoEmailBank.Remove(row);
        await _db.SaveChangesAsync();
    }

    public async Task<bool> MergeAsync(int keepId, int removeId)
    {
        if (keepId == removeId || keepId <= 0 || removeId <= 0) return false;
        var keep = await _db.OtoEmailBank.FirstOrDefaultAsync(e => e.Id == keepId);
        var remove = await _db.OtoEmailBank.FirstOrDefaultAsync(e => e.Id == removeId);
        if (keep is null || remove is null) return false;

        keep.UseCount += remove.UseCount;
        if (remove.LastUsedAt.HasValue && (keep.LastUsedAt is null || remove.LastUsedAt > keep.LastUsedAt))
            keep.LastUsedAt = remove.LastUsedAt;
        if (string.IsNullOrWhiteSpace(keep.Organization)) keep.Organization = remove.Organization;
        if (string.IsNullOrWhiteSpace(keep.PersonName)) keep.PersonName = remove.PersonName;
        if (string.IsNullOrWhiteSpace(keep.Note)) keep.Note = remove.Note;

        _db.OtoEmailBank.Remove(remove);
        await _db.SaveChangesAsync();
        return true;
    }
}
