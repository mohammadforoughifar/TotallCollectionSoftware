using Inventory.Api.Data;
using Inventory.Api.Services.HrMain;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.HrMain;

/// <summary>
/// ================== منابع انسانی اصلی — مدیریت پایه سازمانی ==================
/// ستون فقرات ماژول HR: پروفایل شرکت، شعب، ساختار سازمانی، پست‌ها،
/// زبان/تقویم، قوانین پیش‌فرض و تعطیلات رسمی.
/// مجوزها: HrMain.Read / Create / Update / Delete / Manage
/// </summary>
[Route("api/hr-main")]
public class HrMainController : RbacControllerBase
{
    private const string Mod = "HrMain";
    private readonly IHrMainService _svc;

    public HrMainController(AppDbContext db, IHrMainService svc) : base(db) => _svc = svc;

    // ------------------- نمای کلی -------------------

    [HttpGet("overview")]
    public async Task<IActionResult> Overview()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.OverviewAsync());
    }

    // ------------------- پروفایل شرکت -------------------

    [HttpGet("company")]
    public async Task<IActionResult> GetCompany()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.GetCompanyAsync());
    }

    [HttpPut("company")]
    public async Task<IActionResult> SaveCompany([FromBody] HrMainCompanySaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveCompanyAsync(dto));
    }

    [HttpPost("company/logo")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> UploadLogo(IFormFile? file)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "فایل لوگو ارسال نشد." });
        await using var stream = file.OpenReadStream();
        return Ok(await _svc.SaveLogoAsync(stream, file.FileName));
    }

    [HttpDelete("company/logo")]
    public async Task<IActionResult> DeleteLogo()
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        await _svc.DeleteLogoAsync();
        return Ok(new { ok = true });
    }

    // ------------------- شعب و دفاتر -------------------

    [HttpGet("branches")]
    public async Task<IActionResult> Branches()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.ListBranchesAsync());
    }

    [HttpPost("branches")]
    public async Task<IActionResult> CreateBranch([FromBody] HrMainBranchSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveBranchAsync(null, dto));
    }

    [HttpPut("branches/{id:int}")]
    public async Task<IActionResult> UpdateBranch(int id, [FromBody] HrMainBranchSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveBranchAsync(id, dto));
    }

    [HttpDelete("branches/{id:int}")]
    public async Task<IActionResult> DeleteBranch(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteBranchAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- ساختار سازمانی -------------------

    [HttpGet("org/tree")]
    public async Task<IActionResult> OrgTree()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.GetTreeAsync());
    }

    [HttpGet("org/nodes")]
    public async Task<IActionResult> OrgNodes()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.ListNodesAsync());
    }

    [HttpPost("org/nodes")]
    public async Task<IActionResult> CreateNode([FromBody] HrMainOrgNodeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveNodeAsync(null, dto));
    }

    [HttpPut("org/nodes/{id:int}")]
    public async Task<IActionResult> UpdateNode(int id, [FromBody] HrMainOrgNodeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveNodeAsync(id, dto));
    }

    [HttpDelete("org/nodes/{id:int}")]
    public async Task<IActionResult> DeleteNode(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteNodeAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- پست‌های سازمانی -------------------

    [HttpGet("positions")]
    public async Task<IActionResult> Positions([FromQuery] int? orgNodeId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.ListPositionsAsync(orgNodeId));
    }

    [HttpPost("positions")]
    public async Task<IActionResult> CreatePosition([FromBody] HrMainPositionSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SavePositionAsync(null, dto));
    }

    [HttpPut("positions/{id:int}")]
    public async Task<IActionResult> UpdatePosition(int id, [FromBody] HrMainPositionSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SavePositionAsync(id, dto));
    }

    [HttpDelete("positions/{id:int}")]
    public async Task<IActionResult> DeletePosition(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeletePositionAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- زبان و تقویم -------------------

    [HttpGet("locale")]
    public async Task<IActionResult> GetLocale()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.GetLocaleAsync());
    }

    [HttpPut("locale")]
    public async Task<IActionResult> SaveLocale([FromBody] HrMainLocaleDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveLocaleAsync(dto));
    }

    // ------------------- قوانین پیش‌فرض -------------------

    [HttpGet("rules")]
    public async Task<IActionResult> GetRules()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.GetRulesAsync());
    }

    [HttpPut("rules")]
    public async Task<IActionResult> SaveRules([FromBody] HrMainRulesDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveRulesAsync(dto));
    }

    // ------------------- تعطیلات رسمی/شرکتی -------------------

    [HttpGet("holidays")]
    public async Task<IActionResult> Holidays([FromQuery] int? year)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.ListHolidaysAsync(year));
    }

    [HttpPost("holidays")]
    public async Task<IActionResult> CreateHoliday([FromBody] HrMainHolidaySaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveHolidayAsync(dto, MyUsername));
    }

    [HttpDelete("holidays/{id:int}")]
    public async Task<IActionResult> DeleteHoliday(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteHolidayAsync(id);
        return Ok(new { ok = true });
    }
}
