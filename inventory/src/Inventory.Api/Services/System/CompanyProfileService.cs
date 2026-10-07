using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

public interface ICompanyProfileService
{
    /// <summary>پروفایل شرکت (تک ردیف؛ اگر ثبت نشده باشد، مقدار پیش‌فرض برمی‌گردد).</summary>
    Task<CompanyProfileDto> GetAsync();

    /// <summary>ذخیرهٔ تعریف شرکت + تنظیمات چاپ (upsert روی تک ردیف جدول).</summary>
    Task<CompanyProfileDto> SaveAsync(CompanyProfileDto dto);
}

/// <summary>پروفایل شرکت/فروشگاه + تنظیمات چاپ فاکتور — جدول CompanyProfiles (یک ردیف).</summary>
public class CompanyProfileService : ICompanyProfileService
{
    private readonly AppDbContext _db;
    public CompanyProfileService(AppDbContext db) => _db = db;

    public async Task<CompanyProfileDto> GetAsync()
    {
        var e = await _db.CompanyProfiles.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync();
        return e is null ? new CompanyProfileDto() : Map(e);
    }

    public async Task<CompanyProfileDto> SaveAsync(CompanyProfileDto dto)
    {
        var paper = (dto.PrintPaperSize ?? "A4").Trim().ToUpperInvariant();
        if (paper != "A5") paper = "A4";

        var e = await _db.CompanyProfiles.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (e is null)
        {
            e = new CompanyProfile();
            _db.CompanyProfiles.Add(e);
        }

        e.Name = (dto.Name ?? "").Trim();
        e.AccountNumber = TrimOrNull(dto.AccountNumber);
        e.Phone = TrimOrNull(dto.Phone);
        e.Address = TrimOrNull(dto.Address);
        e.EconomicCode = TrimOrNull(dto.EconomicCode);
        e.TaxId = TrimOrNull(dto.TaxId);
        e.Email = TrimOrNull(dto.Email);
        e.Website = TrimOrNull(dto.Website);
        e.PrintHeaderLines = TrimOrNull(dto.PrintHeaderLines);
        e.PrintFooterLines = TrimOrNull(dto.PrintFooterLines);
        e.PrintPaperSize = paper;
        e.PrintShowSignatures = dto.PrintShowSignatures;
        e.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return Map(e);
    }

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static CompanyProfileDto Map(CompanyProfile e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        AccountNumber = e.AccountNumber,
        Phone = e.Phone,
        Address = e.Address,
        EconomicCode = e.EconomicCode,
        TaxId = e.TaxId,
        Email = e.Email,
        Website = e.Website,
        PrintHeaderLines = e.PrintHeaderLines,
        PrintFooterLines = e.PrintFooterLines,
        PrintPaperSize = e.PrintPaperSize,
        PrintShowSignatures = e.PrintShowSignatures,
        UpdatedAt = e.UpdatedAt
    };
}
