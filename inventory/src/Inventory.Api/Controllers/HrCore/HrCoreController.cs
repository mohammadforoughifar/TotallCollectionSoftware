using Inventory.Api.Data;
using Inventory.Api.Services.HrCore;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.HrCore;

/// <summary>
/// ================== هسته پرسنلی (کارگزینی) ==================
/// ماژول مستقل HrCore: پرونده پرسنل، ساختار سازمانی، قراردادها، احکام.
/// مجوزها: HrCore.Read / Create / Update / Delete / Manage
/// </summary>
[Route("api/hr-core")]
public class HrCoreController : RbacControllerBase
{
    private const string Mod = "HrCore";
    private readonly IHrCoreService _svc;

    public HrCoreController(AppDbContext db, IHrCoreService svc) : base(db) => _svc = svc;

    // ------------------- پرسنل -------------------

    [HttpGet("employees")]
    public async Task<IActionResult> SearchEmployees([FromQuery] string? q, [FromQuery] int? orgUnitId,
        [FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var (items, total) = await _svc.SearchEmployeesAsync(q, orgUnitId, status, skip, take);
        return Ok(new { total, items });
    }

    [HttpGet("employees/{id:int}")]
    public async Task<IActionResult> GetEmployee(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var e = await _svc.GetEmployeeAsync(id);
        return e is null ? NotFound(new { message = "پرسنل یافت نشد." }) : Ok(e);
    }

    [HttpGet("employees/next-code")]
    public async Task<IActionResult> NextCode()
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(new { code = await _svc.NextEmployeeCodeAsync() });
    }

    [HttpPost("employees")]
    public async Task<IActionResult> CreateEmployee([FromBody] HrEmployeeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.CreateEmployeeAsync(dto));
    }

    [HttpPut("employees/{id:int}")]
    public async Task<IActionResult> UpdateEmployee(int id, [FromBody] HrEmployeeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.UpdateEmployeeAsync(id, dto));
    }

    [HttpPost("employees/{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, [FromQuery] bool active)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.SetEmployeeActiveAsync(id, active);
        return Ok(new { ok = true });
    }

    // ------------------- ساختار سازمانی -------------------

    [HttpGet("org/tree")]
    public async Task<IActionResult> OrgTree()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.GetTreeAsync());
    }

    [HttpGet("org/units")]
    public async Task<IActionResult> OrgUnits()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.ListUnitsAsync());
    }

    [HttpPost("org/units")]
    public async Task<IActionResult> CreateUnit([FromBody] HrOrgUnitSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveUnitAsync(null, dto));
    }

    [HttpPut("org/units/{id:int}")]
    public async Task<IActionResult> UpdateUnit(int id, [FromBody] HrOrgUnitSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveUnitAsync(id, dto));
    }

    [HttpDelete("org/units/{id:int}")]
    public async Task<IActionResult> DeleteUnit(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteUnitAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- قراردادها -------------------

    [HttpGet("contracts")]
    public async Task<IActionResult> Contracts([FromQuery] bool? onlyActive)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.ListContractsAsync(onlyActive));
    }

    [HttpGet("contracts/expiring")]
    public async Task<IActionResult> Expiring([FromQuery] int days = 30)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.ExpiringContractsAsync(days));
    }

    [HttpGet("employees/{id:int}/contracts")]
    public async Task<IActionResult> EmployeeContracts(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.EmployeeContractsAsync(id));
    }

    [HttpPost("contracts")]
    public async Task<IActionResult> CreateContract([FromBody] HrContractSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveContractAsync(null, dto));
    }

    [HttpPut("contracts/{id:int}")]
    public async Task<IActionResult> UpdateContract(int id, [FromBody] HrContractSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveContractAsync(id, dto));
    }

    [HttpDelete("contracts/{id:int}")]
    public async Task<IActionResult> DeleteContract(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteContractAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- احکام -------------------

    [HttpGet("decrees")]
    public async Task<IActionResult> Decrees([FromQuery] int? employeeId, [FromQuery] bool? onlyPending)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.ListDecreesAsync(employeeId, onlyPending));
    }

    [HttpGet("employees/{id:int}/decrees")]
    public async Task<IActionResult> EmployeeDecrees(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.EmployeeDecreesAsync(id));
    }

    [HttpPost("decrees")]
    public async Task<IActionResult> CreateDecree([FromBody] HrDecreeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveDecreeAsync(null, dto, MyUserId, MyUsername));
    }

    [HttpPut("decrees/{id:int}")]
    public async Task<IActionResult> UpdateDecree(int id, [FromBody] HrDecreeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveDecreeAsync(id, dto, MyUserId, MyUsername));
    }

    [HttpPost("decrees/{id:int}/apply")]
    public async Task<IActionResult> ApplyDecree(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.ApplyDecreeAsync(id));
    }

    [HttpDelete("decrees/{id:int}")]
    public async Task<IActionResult> DeleteDecree(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteDecreeAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- داشبورد -------------------

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] int expiringDays = 30)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.DashboardAsync(expiringDays));
    }
}
