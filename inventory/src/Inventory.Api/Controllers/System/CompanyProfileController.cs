using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

// =====================================================================
// پروفایل شرکت/فروشگاه — تعریف شرکت + تنظیمات چاپ فاکتور
//   api/company-profile          دریافت پروفایل (تک ردیف)
//   api/company-profile          ذخیرهٔ پروفایل (مجوز CompanyProfile.Update)
// =====================================================================

/// <summary>تعریف شرکت/فروشگاه و تنظیمات چاپ فاکتورهای فروش/خرید.</summary>
[Route("api/company-profile")]
public class CompanyProfileController : RbacControllerBase
{
    private readonly ICompanyProfileService _svc;
    public CompanyProfileController(AppDbContext db, ICompanyProfileService svc) : base(db) => _svc = svc;

    /// <summary>پروفایل فعلی شرکت (برای سربرگ/فوتر فاکتورهای چاپی).</summary>
    [HttpGet]
    public async Task<ActionResult<CompanyProfileDto>> Get() => Ok(await _svc.GetAsync());

    /// <summary>ذخیرهٔ تعریف شرکت و تنظیمات چاپ.</summary>
    [HttpPost]
    public async Task<ActionResult<CompanyProfileDto>> Save([FromBody] CompanyProfileDto dto)
    {
        if (await ForbiddenUnlessAsync("CompanyProfile", "Update") is ObjectResult forbidden) return forbidden;
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام فروشگاه/شرکت را وارد کنید.");
        return Ok(await _svc.SaveAsync(dto));
    }
}
