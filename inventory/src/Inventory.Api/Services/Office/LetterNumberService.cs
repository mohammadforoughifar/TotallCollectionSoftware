using System.Globalization;
using System.Text;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

// ============================================================
//  سرویس ساختار شماره نامه (اندیکاتور)
//  ------------------------------------------------------------
//  ترتیب اجزا، جداکننده، تعداد ارقام، دوره‌ی شمارنده و کدهای سازمانی
//  از تنظیمات خوانده می‌شوند. همان formatter برای پیش‌نمایش، نامه‌های
//  موجود و نامه‌های جدید استفاده می‌شود تا خروجی‌ها با هم اختلاف نکنند.
// ============================================================

public interface ILetterNumberService
{
    Task<LetterNumberSettingDto> GetAsync(int sourceType = 1, int userId = 0);
    Task<LetterNumberSettingDto> SaveAsync(LetterNumberSettingDto dto, int userId = 0);
    Task<(int number, string letterNumber)> NextAsync(int creatorUserId, DateTime date, int sourceType = 1);
    Task<string> PreviewAsync(LetterNumberSettingDto dto, int userId);
    List<LetterNumberPartDto> AvailableParts();
    Task<LetterNumberLookupsDto> LookupsAsync();
}

public class LetterNumberService : ILetterNumberService
{
    private readonly AppDbContext _db;

    private static readonly (string Code, string Title)[] Parts =
    {
        ("prefix",  "پیشوند ثابت"),
        ("year",    "سال شمسی"),
        ("month",   "ماه شمسی"),
        ("day",     "روز شمسی"),
        ("dept",    "کد واحد/دپارتمان"),
        ("company", "کد شرکت"),
        ("user",    "شناسه کاربر ثبت‌کننده"),
        ("serial",  "شماره ترتیبی (سریال)"),
        ("text1",   "متن دلخواه ۱"),
        ("text2",   "متن دلخواه ۲"),
        ("suffix",  "پسوند ثابت")
    };

    private static readonly HashSet<string> ValidPartCodes =
        Parts.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);

    public LetterNumberService(AppDbContext db) => _db = db;

    public List<LetterNumberPartDto> AvailableParts() =>
        Parts.Select(p => new LetterNumberPartDto { Code = p.Code, Title = p.Title }).ToList();

    /// <summary>
    /// واحدها و شرکت‌ها از «شناسنامه سیستم» خوانده می‌شوند؛ کد انتخابیِ این
    /// رکوردها در بخش dept/company شماره درج می‌شود.
    /// </summary>
    public async Task<LetterNumberLookupsDto> LookupsAsync() => new()
    {
        Departments = await _db.SystemDepartments.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new OrgCodeItemDto { Id = d.Id, Name = d.Name, Code = d.Code })
            .ToListAsync(),

        Companies = await _db.SystemCompanies.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new OrgCodeItemDto { Id = c.Id, Name = c.Name, Code = c.Code })
            .ToListAsync(),

        Parts = AvailableParts()
    };

    // ==================== خواندن / ذخیره ====================

    private static int EnsureSupportedSourceType(int sourceType)
    {
        // این شاخه فعلاً فقط موجودیت نامه داخلی دارد؛ نگهداری تنظیمات بی‌مصرف برای
        // صادره/وارده باعث می‌شود کاربر تصور کند ساختار روی آن‌ها اعمال شده است.
        if (sourceType != 1)
            throw new Exception("در نسخه فعلی، ساختار شماره فقط برای نامه داخلی قابل تنظیم است.");
        return sourceType;
    }

    /// <summary>
    /// اگر هنوز رکوردی ذخیره نشده باشد، تنظیمات پیش‌فرض را بدون نوشتن در دیتابیس
    /// برمی‌گرداند؛ بنابراین درخواست GET هیچ اثر جانبی ندارد.
    /// </summary>
    private async Task<LetterNumberSetting> EntityAsync(int sourceType)
    {
        sourceType = EnsureSupportedSourceType(sourceType);
        return await _db.LetterNumberSettings.FirstOrDefaultAsync(x => x.SourceType == sourceType)
               ?? new LetterNumberSetting { SourceType = sourceType };
    }

    public async Task<LetterNumberSettingDto> GetAsync(int sourceType = 1, int userId = 0)
    {
        var e = await EntityAsync(sourceType);
        var dto = ToDto(e);
        dto.NextSerial = await NextSerialAsync(e, DateTime.Now);
        var codes = await ResolveUserCodesAsync(userId, e.PartsOrder);
        dto.Preview = Format(e, dto.NextSerial, DateTime.Now, userId, codes.DeptCode, codes.CompanyCode);
        return dto;
    }

    public async Task<LetterNumberSettingDto> SaveAsync(LetterNumberSettingDto dto, int userId = 0)
    {
        var sourceType = EnsureSupportedSourceType(dto.SourceType);
        var normalizedParts = NormalizeParts(dto.Parts, requireSerial: true);

        if (dto.YearDigits is not (2 or 4))
            throw new Exception("تعداد ارقام سال باید ۲ یا ۴ باشد.");
        if (dto.SerialDigits is < 0 or > 10)
            throw new Exception("تعداد ارقام سریال باید بین ۰ تا ۱۰ باشد.");
        if (dto.StartNumber < 1)
            throw new Exception("شماره شروع باید حداقل ۱ باشد.");
        if (dto.Step < 1)
            throw new Exception("گام افزایش شمارنده باید حداقل ۱ باشد.");

        var separator = dto.Separator ?? "";
        if (separator.Length > 5)
            throw new Exception("جداکننده نمی‌تواند بیشتر از ۵ نویسه باشد.");

        var resetPolicy = NormalizeResetPolicy(dto.ResetPolicy);
        var prefix = NormalizeText(dto.Prefix, 30, "پیشوند");
        var suffix = NormalizeText(dto.Suffix, 30, "پسوند");
        var text1 = NormalizeText(dto.Text1, 30, "متن دلخواه ۱");
        var text2 = NormalizeText(dto.Text2, 30, "متن دلخواه ۲");
        var deptCode = NormalizeText(dto.DefaultDeptCode, 30, "کد واحد");
        var companyCode = NormalizeText(dto.DefaultCompanyCode, 100, "کد شرکت");

        await using var tx = await _db.Database.BeginTransactionAsync();
        var e = await EntityAsync(sourceType);
        if (e.Id == 0) _db.LetterNumberSettings.Add(e);

        e.PartsOrder = string.Join(",", normalizedParts);
        e.Separator = separator;
        e.Prefix = prefix;
        e.Suffix = suffix;
        e.Text1 = text1;
        e.Text2 = text2;
        e.YearDigits = dto.YearDigits;
        e.SerialDigits = dto.SerialDigits;
        e.StartNumber = dto.StartNumber;
        e.Step = dto.Step;
        e.ResetPolicy = resetPolicy;
        e.UsePersianDigits = dto.UsePersianDigits;
        e.DefaultDeptCode = deptCode;
        e.DefaultCompanyCode = companyCode;
        e.UpdatedAt = DateTime.Now;

        // ترتیب/فرمت تازه بلافاصله روی نامه‌های موجود هم اعمال می‌شود. شماره ترتیبی
        // هر نامه ثابت می‌ماند و فقط نمایش LetterNumber با ساختار جدید بازسازی می‌شود.
        await ReapplyToExistingLettersAsync(e);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return await GetAsync(sourceType, userId);
    }

    private static string NormalizeResetPolicy(string? value) =>
        (value ?? "Yearly").Trim().ToLowerInvariant() switch
        {
            "yearly" => "Yearly",
            "monthly" => "Monthly",
            "never" => "Never",
            _ => throw new Exception("قاعده‌ی ریست شمارنده باید سالانه، ماهانه یا هرگز باشد.")
        };

    private static string? NormalizeText(string? value, int maxLength, string title)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new Exception($"{title} نمی‌تواند بیشتر از {maxLength} نویسه باشد.");
        return normalized;
    }

    private static List<string> NormalizeParts(IEnumerable<string>? values, bool requireSerial)
    {
        var parts = (values ?? Array.Empty<string>())
            .Select(p => (p ?? "").Trim().ToLowerInvariant())
            .ToList();

        if (parts.Count == 0)
            throw new Exception("حداقل یک جزء برای ساختار شماره نامه انتخاب کنید.");
        if (parts.Any(p => !ValidPartCodes.Contains(p)))
            throw new Exception("ساختار شماره شامل جزء نامعتبر است.");
        if (parts.Distinct(StringComparer.Ordinal).Count() != parts.Count)
            throw new Exception("هر جزء فقط یک‌بار می‌تواند در ساختار شماره قرار بگیرد.");
        if (requireSerial && !parts.Contains("serial", StringComparer.Ordinal))
            throw new Exception("جزء «شماره ترتیبی (سریال)» الزامی است — بدون آن شماره‌ها تکراری می‌شوند.");

        return parts;
    }

    private static LetterNumberSettingDto ToDto(LetterNumberSetting e) => new()
    {
        SourceType = e.SourceType,
        Parts = (e.PartsOrder ?? "year,serial")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList(),
        Separator = e.Separator,
        Prefix = e.Prefix,
        Suffix = e.Suffix,
        Text1 = e.Text1,
        Text2 = e.Text2,
        YearDigits = e.YearDigits,
        SerialDigits = e.SerialDigits,
        StartNumber = e.StartNumber,
        Step = e.Step,
        ResetPolicy = e.ResetPolicy,
        UsePersianDigits = e.UsePersianDigits,
        DefaultDeptCode = e.DefaultDeptCode,
        DefaultCompanyCode = e.DefaultCompanyCode
    };

    // ==================== شماره‌گذاری ====================

    /// <summary>
    /// بیشترین شماره همان دوره مبناست؛ اتکا به آخرین Id در صورت ورود داده قدیمی یا
    /// حذف نامه نتیجه نادرست می‌داد. نامه حذف‌شده هم عمداً رزرو باقی می‌ماند.
    /// </summary>
    private async Task<int> NextSerialAsync(LetterNumberSetting cfg, DateTime date)
    {
        IQueryable<InnerLetter> query = _db.InnerLetters.AsNoTracking();
        var pc = new PersianCalendar();

        if (cfg.ResetPolicy == "Yearly")
        {
            var year = pc.GetYear(date);
            var from = pc.ToDateTime(year, 1, 1, 0, 0, 0, 0);
            var to = pc.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0);
            query = query.Where(l => l.DateSabt >= from && l.DateSabt < to);
        }
        else if (cfg.ResetPolicy == "Monthly")
        {
            var year = pc.GetYear(date);
            var month = pc.GetMonth(date);
            var from = pc.ToDateTime(year, month, 1, 0, 0, 0, 0);
            var nextYear = month == 12 ? year + 1 : year;
            var nextMonth = month == 12 ? 1 : month + 1;
            var to = pc.ToDateTime(nextYear, nextMonth, 1, 0, 0, 0, 0);
            query = query.Where(l => l.DateSabt >= from && l.DateSabt < to);
        }

        var max = await query.MaxAsync(l => (int?)l.Number);
        if (!max.HasValue) return cfg.StartNumber;
        if (max.Value > int.MaxValue - cfg.Step)
            throw new Exception("شمارنده‌ی نامه به بیشترین مقدار مجاز رسیده است.");
        return max.Value + cfg.Step;
    }

    public async Task<(int number, string letterNumber)> NextAsync(
        int creatorUserId, DateTime date, int sourceType = 1)
    {
        var cfg = await EntityAsync(sourceType);
        var serial = await NextSerialAsync(cfg, date);
        var codes = await ResolveUserCodesAsync(creatorUserId, cfg.PartsOrder);
        return (serial, Format(cfg, serial, date, creatorUserId, codes.DeptCode, codes.CompanyCode));
    }

    public async Task<string> PreviewAsync(LetterNumberSettingDto dto, int userId)
    {
        var parts = NormalizeParts(dto.Parts, requireSerial: false);
        var cfg = new LetterNumberSetting
        {
            SourceType = EnsureSupportedSourceType(dto.SourceType),
            PartsOrder = string.Join(",", parts),
            Separator = (dto.Separator ?? "").Length <= 5 ? dto.Separator ?? "" : (dto.Separator ?? "")[..5],
            Prefix = dto.Prefix?.Trim(),
            Suffix = dto.Suffix?.Trim(),
            Text1 = dto.Text1?.Trim(),
            Text2 = dto.Text2?.Trim(),
            YearDigits = dto.YearDigits is 2 or 4 ? dto.YearDigits : 4,
            SerialDigits = Math.Clamp(dto.SerialDigits, 0, 10),
            StartNumber = Math.Max(1, dto.StartNumber),
            Step = Math.Max(1, dto.Step),
            ResetPolicy = NormalizeResetPolicy(dto.ResetPolicy),
            UsePersianDigits = dto.UsePersianDigits,
            DefaultDeptCode = dto.DefaultDeptCode?.Trim(),
            DefaultCompanyCode = dto.DefaultCompanyCode?.Trim()
        };

        var serial = await NextSerialAsync(cfg, DateTime.Now);
        var codes = await ResolveUserCodesAsync(userId, cfg.PartsOrder);
        return Format(cfg, serial, DateTime.Now, userId, codes.DeptCode, codes.CompanyCode);
    }

    /// <summary>
    /// خروجی دقیقاً طبق ترتیب PartsOrder ساخته می‌شود. اجزای خالی حذف می‌شوند تا
    /// جداکننده‌ی اضافه در شماره باقی نماند.
    /// </summary>
    private static string Format(
        LetterNumberSetting cfg,
        int serial,
        DateTime date,
        int userId,
        string? userDeptCode,
        string? userCompanyCode)
    {
        var pc = new PersianCalendar();
        var year = pc.GetYear(date);
        var month = pc.GetMonth(date);
        var day = pc.GetDayOfMonth(date);
        var deptCode = string.IsNullOrWhiteSpace(userDeptCode) ? cfg.DefaultDeptCode : userDeptCode;
        var companyCode = string.IsNullOrWhiteSpace(userCompanyCode) ? cfg.DefaultCompanyCode : userCompanyCode;

        var order = (cfg.PartsOrder ?? "year,serial")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pieces = new List<string>();

        foreach (var part in order)
        {
            var value = part switch
            {
                "prefix" => cfg.Prefix,
                "suffix" => cfg.Suffix,
                "text1" => cfg.Text1,
                "text2" => cfg.Text2,
                "year" => cfg.YearDigits == 2
                    ? (year % 100).ToString("00", CultureInfo.InvariantCulture)
                    : year.ToString(CultureInfo.InvariantCulture),
                "month" => month.ToString("00", CultureInfo.InvariantCulture),
                "day" => day.ToString("00", CultureInfo.InvariantCulture),
                "dept" => deptCode,
                "company" => companyCode,
                "user" => userId > 0 ? userId.ToString(CultureInfo.InvariantCulture) : null,
                "serial" => cfg.SerialDigits > 0
                    ? serial.ToString(CultureInfo.InvariantCulture).PadLeft(cfg.SerialDigits, '0')
                    : serial.ToString(CultureInfo.InvariantCulture),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(value)) pieces.Add(value.Trim());
        }

        var result = string.Join(cfg.Separator ?? "", pieces);
        return cfg.UsePersianDigits ? ToPersianDigits(result) : result;
    }

    private async Task ReapplyToExistingLettersAsync(LetterNumberSetting cfg)
    {
        var letters = await _db.InnerLetters
            .Where(l => !l.IsDelete && !l.Source.IsDelete)
            .ToListAsync();
        if (letters.Count == 0) return;

        var needsOrgCodes = cfg.PartsOrder.Split(',')
            .Any(p => p is "dept" or "company");
        var codesByUser = new Dictionary<int, (string? DeptCode, string? CompanyCode)>();

        if (needsOrgCodes)
        {
            foreach (var userId in letters.Select(l => l.CreatorUserId).Distinct())
                codesByUser[userId] = await ResolveUserCodesAsync(userId, cfg.PartsOrder);
        }

        foreach (var letter in letters)
        {
            var codes = codesByUser.GetValueOrDefault(letter.CreatorUserId);
            letter.LetterNumber = Format(
                cfg, letter.Number, letter.DateSabt, letter.CreatorUserId, codes.DeptCode, codes.CompanyCode);
        }
    }

    private async Task<(string? DeptCode, string? CompanyCode)> ResolveUserCodesAsync(
        int userId, string partsOrder)
    {
        if (userId <= 0) return (null, null);
        var parts = partsOrder.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var needsDept = parts.Contains("dept", StringComparer.Ordinal);
        var needsCompany = parts.Contains("company", StringComparer.Ordinal);
        if (!needsDept && !needsCompany) return (null, null);

        var username = await _db.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => x.Username)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(username)) return (null, null);

        var systemUser = await _db.SystemUsers.AsNoTracking()
            .Where(x => x.Username == username)
            .Select(x => new { x.DepartmentId, x.CompanyId })
            .FirstOrDefaultAsync();
        if (systemUser == null) return (null, null);

        string? deptCode = null;
        string? companyCode = null;
        if (needsDept && systemUser.DepartmentId is int departmentId)
        {
            deptCode = await _db.SystemDepartments.AsNoTracking()
                .Where(d => d.Id == departmentId && d.IsActive)
                .Select(d => d.Code)
                .FirstOrDefaultAsync();
        }
        if (needsCompany && systemUser.CompanyId is int companyId)
        {
            companyCode = await _db.SystemCompanies.AsNoTracking()
                .Where(c => c.Id == companyId && c.IsActive)
                .Select(c => c.Code)
                .FirstOrDefaultAsync();
        }

        return (deptCode, companyCode);
    }

    private static string ToPersianDigits(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            sb.Append(ch is >= '0' and <= '9' ? (char)('۰' + ch - '0') : ch);
        return sb.ToString();
    }
}
