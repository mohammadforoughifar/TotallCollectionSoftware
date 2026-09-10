using System.Globalization;
using System.Text;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

// ============================================================
//  سرویس ساختار شماره نامه (اندیکاتور)
//  ------------------------------------------------------------
//  شماره‌ی نامه از اجزای قابل تنظیم ساخته می‌شود و ترتیب اجزا، جداکننده،
//  تعداد ارقام، ریست سالانه/ماهانه/هرگز، شماره‌ی شروع و گام، همگی از
//  «تنظیمات → ساختار شماره نامه» خوانده و روی نامه‌های جدید اعمال می‌شود.
// ============================================================

public interface ILetterNumberService
{
    /// <summary>تنظیمات فعلی (اگر نبود، پیش‌فرض ساخته می‌شود)</summary>
    Task<LetterNumberSettingDto> GetAsync(int sourceType = 1);

    /// <summary>ذخیره‌ی تنظیمات (با اعتبارسنجی)</summary>
    Task<LetterNumberSettingDto> SaveAsync(LetterNumberSettingDto dto);

    /// <summary>ساخت شماره‌ی نامه‌ی بعدی — (شماره ترتیبی، شماره کامل)</summary>
    Task<(int number, string letterNumber)> NextAsync(int creatorUserId, DateTime date, int sourceType = 1);

    /// <summary>پیش‌نمایش شماره با تنظیمات داده‌شده (بدون ذخیره)</summary>
    Task<string> PreviewAsync(LetterNumberSettingDto dto, int userId);

    /// <summary>فهرست اجزای قابل انتخاب برای رابط کاربری</summary>
    List<LetterNumberPartDto> AvailableParts();

    /// <summary>واحدها و کمپانی‌های تعریف‌شده در تنظیمات سیستم + اجزا (برای صفحه‌ی تنظیمات)</summary>
    Task<LetterNumberLookupsDto> LookupsAsync();
}

public class LetterNumberService : ILetterNumberService
{
    private readonly AppDbContext _db;

    public LetterNumberService(AppDbContext db) => _db = db;

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

    public List<LetterNumberPartDto> AvailableParts() =>
        Parts.Select(p => new LetterNumberPartDto { Code = p.Code, Title = p.Title }).ToList();

    /// <summary>
    /// واحد و کمپانی از «شناسنامه سیستم» خوانده می‌شوند تا در صفحه‌ی ساختار شماره نامه
    /// به‌جای تایپ دستیِ کد، از فهرست موجود انتخاب شوند.
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

    private async Task<LetterNumberSetting> EntityAsync(int sourceType)
    {
        var e = await _db.LetterNumberSettings.FirstOrDefaultAsync(x => x.SourceType == sourceType);
        if (e == null)
        {
            e = new LetterNumberSetting { SourceType = sourceType };
            _db.LetterNumberSettings.Add(e);
            await _db.SaveChangesAsync();
        }
        return e;
    }

    public async Task<LetterNumberSettingDto> GetAsync(int sourceType = 1)
    {
        var e = await EntityAsync(sourceType);
        var dto = ToDto(e);
        dto.NextSerial = await NextSerialAsync(e, DateTime.Now);
        dto.Preview = await BuildAsync(e, dto.NextSerial, DateTime.Now, 0);
        return dto;
    }

    public async Task<LetterNumberSettingDto> SaveAsync(LetterNumberSettingDto dto)
    {
        var sourceType = dto.SourceType <= 0 ? 1 : dto.SourceType;
        var e = await EntityAsync(sourceType);

        // ---------- اعتبارسنجی ----------
        var valid = Parts.Select(p => p.Code).ToHashSet();
        var parts = (dto.Parts ?? new List<string>())
            .Select(p => (p ?? "").Trim().ToLowerInvariant())
            .Where(p => valid.Contains(p))
            .Distinct()
            .ToList();

        if (parts.Count == 0)
            throw new Exception("حداقل یک جزء برای ساختار شماره نامه انتخاب کنید.");
        if (!parts.Contains("serial"))
            throw new Exception("جزء «شماره ترتیبی (سریال)» الزامی است — بدون آن شماره‌ها تکراری می‌شوند.");
        if (dto.YearDigits is not (2 or 4))
            throw new Exception("تعداد ارقام سال باید ۲ یا ۴ باشد.");
        if (dto.SerialDigits is < 0 or > 10)
            throw new Exception("تعداد ارقام سریال باید بین ۰ تا ۱۰ باشد.");
        if (dto.StartNumber < 0)
            throw new Exception("شماره شروع نمی‌تواند منفی باشد.");
        if (dto.Step < 1)
            throw new Exception("گام افزایش شمارنده باید حداقل ۱ باشد.");

        var reset = (dto.ResetPolicy ?? "Yearly").Trim();
        if (reset is not ("Yearly" or "Monthly" or "Never"))
            throw new Exception("قاعده‌ی ریست شمارنده باید Yearly، Monthly یا Never باشد.");

        e.PartsOrder = string.Join(",", parts);
        e.Separator = string.IsNullOrEmpty(dto.Separator) ? "" : dto.Separator.Trim();
        e.Prefix = Trim(dto.Prefix);
        e.Suffix = Trim(dto.Suffix);
        e.Text1 = Trim(dto.Text1);
        e.Text2 = Trim(dto.Text2);
        e.YearDigits = dto.YearDigits;
        e.SerialDigits = dto.SerialDigits;
        e.StartNumber = dto.StartNumber;
        e.Step = dto.Step;
        e.ResetPolicy = reset;
        e.UsePersianDigits = dto.UsePersianDigits;
        e.DefaultDeptCode = Trim(dto.DefaultDeptCode);
        e.DefaultCompanyCode = Trim(dto.DefaultCompanyCode);
        e.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return await GetAsync(sourceType);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static LetterNumberSettingDto ToDto(LetterNumberSetting e) => new()
    {
        SourceType = e.SourceType,
        Parts = (e.PartsOrder ?? "")
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
    /// شماره‌ی ترتیبی بعدی بر اساس قاعده‌ی ریست:
    ///  • Yearly  : در هر سال شمسی از StartNumber شروع می‌شود
    ///  • Monthly : در هر ماه شمسی از StartNumber شروع می‌شود
    ///  • Never   : همیشه ادامه پیدا می‌کند
    /// </summary>
    private async Task<int> NextSerialAsync(LetterNumberSetting cfg, DateTime date)
    {
        var pc = new PersianCalendar();
        var last = await _db.InnerLetters
            .OrderByDescending(l => l.Id)
            .Select(l => new { l.Number, l.DateSabt })
            .FirstOrDefaultAsync();

        if (last == null) return Math.Max(0, cfg.StartNumber);

        var sameBucket = cfg.ResetPolicy switch
        {
            "Never" => true,
            "Monthly" => pc.GetYear(last.DateSabt) == pc.GetYear(date) &&
                         pc.GetMonth(last.DateSabt) == pc.GetMonth(date),
            _ => pc.GetYear(last.DateSabt) == pc.GetYear(date)
        };

        return sameBucket
            ? last.Number + Math.Max(1, cfg.Step)
            : Math.Max(0, cfg.StartNumber);
    }

    public async Task<(int number, string letterNumber)> NextAsync(int creatorUserId, DateTime date, int sourceType = 1)
    {
        var cfg = await EntityAsync(sourceType);
        var serial = await NextSerialAsync(cfg, date);
        var text = await BuildAsync(cfg, serial, date, creatorUserId);
        return (serial, text);
    }

    public async Task<string> PreviewAsync(LetterNumberSettingDto dto, int userId)
    {
        var tmp = new LetterNumberSetting
        {
            SourceType = dto.SourceType <= 0 ? 1 : dto.SourceType,
            PartsOrder = string.Join(",", dto.Parts ?? new List<string>()),
            Separator = dto.Separator ?? "/",
            Prefix = dto.Prefix,
            Suffix = dto.Suffix,
            Text1 = dto.Text1,
            Text2 = dto.Text2,
            YearDigits = dto.YearDigits is 2 or 4 ? dto.YearDigits : 4,
            SerialDigits = Math.Clamp(dto.SerialDigits, 0, 10),
            StartNumber = Math.Max(0, dto.StartNumber),
            Step = Math.Max(1, dto.Step),
            ResetPolicy = dto.ResetPolicy ?? "Yearly",
            UsePersianDigits = dto.UsePersianDigits,
            DefaultDeptCode = dto.DefaultDeptCode,
            DefaultCompanyCode = dto.DefaultCompanyCode
        };
        var serial = await NextSerialAsync(tmp, DateTime.Now);
        return await BuildAsync(tmp, serial, DateTime.Now, userId);
    }

    /// <summary>ساخت متن شماره از روی تنظیمات و اجزا (به ترتیب تعیین‌شده)</summary>
    private async Task<string> BuildAsync(LetterNumberSetting cfg, int serial, DateTime date, int userId)
    {
        var pc = new PersianCalendar();
        int jy = pc.GetYear(date), jm = pc.GetMonth(date), jd = pc.GetDayOfMonth(date);

        string? deptCode = cfg.DefaultDeptCode;
        string? companyCode = cfg.DefaultCompanyCode;

        var order = (cfg.PartsOrder ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // کد واحد/شرکت کاربر ثبت‌کننده — فقط اگر لازم باشد از دیتابیس خوانده می‌شود
        if (userId > 0 && (order.Contains("dept") || order.Contains("company")))
        {
            var u = await _db.Users.AsNoTracking()
                .Where(x => x.Id == userId)
                .Select(x => new { x.Username })
                .FirstOrDefaultAsync();

            if (u != null)
            {
                var su = await _db.SystemUsers.AsNoTracking()
                    .Where(x => x.Username == u.Username)
                    .Select(x => new { x.DepartmentId, x.CompanyId })
                    .FirstOrDefaultAsync();

                if (su?.DepartmentId is int did)
                {
                    var code = await _db.SystemDepartments.AsNoTracking()
                        .Where(d => d.Id == did).Select(d => d.Code).FirstOrDefaultAsync();
                    if (!string.IsNullOrWhiteSpace(code)) deptCode = code;
                }
                if (su?.CompanyId is int cid)
                {
                    var code = await _db.SystemCompanies.AsNoTracking()
                        .Where(c => c.Id == cid).Select(c => c.Code).FirstOrDefaultAsync();
                    if (!string.IsNullOrWhiteSpace(code)) companyCode = code;
                }
            }
        }

        var pieces = new List<string>();
        foreach (var p in order)
        {
            string v = p switch
            {
                "prefix" => cfg.Prefix ?? "",
                "suffix" => cfg.Suffix ?? "",
                "text1" => cfg.Text1 ?? "",
                "text2" => cfg.Text2 ?? "",
                "year" => cfg.YearDigits == 2 ? (jy % 100).ToString("00") : jy.ToString(),
                "month" => jm.ToString("00"),
                "day" => jd.ToString("00"),
                "dept" => deptCode ?? "",
                "company" => companyCode ?? "",
                "user" => userId > 0 ? userId.ToString() : "",
                "serial" => cfg.SerialDigits > 0 ? serial.ToString().PadLeft(cfg.SerialDigits, '0') : serial.ToString(),
                _ => ""
            };
            if (!string.IsNullOrEmpty(v)) pieces.Add(v);
        }

        var text = string.Join(cfg.Separator ?? "", pieces);
        return cfg.UsePersianDigits ? ToPersianDigits(text) : text;
    }

    private static string ToPersianDigits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(ch is >= '0' and <= '9' ? (char)('۰' + (ch - '0')) : ch);
        return sb.ToString();
    }
}
