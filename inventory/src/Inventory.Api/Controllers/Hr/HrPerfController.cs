using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>ارزیابی عملکرد و KPI: دوره، شاخص، نمره‌دهی، کارنامه (فاز ۴)</summary>
[ApiController]
[Route("api/hr-perf")]
[Authorize]
public class HrPerfController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly HrPerfService _svc;

    public HrPerfController(AppDbContext db, HrPerfService svc) { _db = db; _svc = svc; }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyName => User.FindFirstValue(ClaimTypes.Name) ?? "";

    private bool? _isHr;
    private async Task<bool> IsHrAsync()
    {
        if (_isHr.HasValue) return _isHr.Value;
        if (User.IsInRole("Admin")) { _isHr = true; return true; }
        if (User.HasClaim("permission", "LeaveRequests.Approve")) { _isHr = true; return true; }
        _isHr = await _db.UserRoles.Where(ur => ur.UserId == MyUserId)
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, pm => pm.Id, (rp, pm) => pm)
            .AnyAsync(pm => pm.Module == "LeaveRequests" && pm.Action == "Approve");
        return _isHr.Value;
    }

    // ================= دوره =================

    [HttpGet("periods")]
    public async Task<IActionResult> Periods()
    {
        if (!await IsHrAsync()) return Forbid();
        return Ok(await _svc.PeriodsAsync());
    }

    public class PeriodInput
    {
        public string Title { get; set; } = "";
        public int Year { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal BonusMonthSalary { get; set; } = 1;
        public double MinScoreForBonus { get; set; } = 60;
    }

    [HttpPost("periods")]
    public async Task<IActionResult> CreatePeriod([FromBody] PeriodInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try
        {
            return Ok(await _svc.CreatePeriodAsync(input.Title, input.Year, input.StartDate, input.EndDate,
                input.BonusMonthSalary, input.MinScoreForBonus, MyName));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    public class StatusInput { public string Status { get; set; } = ""; }

    [HttpPost("periods/{id:int}/status")]
    public async Task<IActionResult> PeriodStatus(int id, [FromBody] StatusInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try { return Ok(await _svc.SetPeriodStatusAsync(id, input.Status)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("periods/{id:int}")]
    public async Task<IActionResult> DeletePeriod(int id)
    {
        if (!await IsHrAsync()) return Forbid();
        try { await _svc.DeletePeriodAsync(id); return Ok(new { ok = true }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ================= شاخص =================

    [HttpGet("periods/{id:int}/kpis")]
    public async Task<IActionResult> Kpis(int id)
    {
        if (!await IsHrAsync()) return Forbid();
        return Ok(await _svc.KpisAsync(id));
    }

    public class KpiInput
    {
        public int PeriodId { get; set; }
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public double Weight { get; set; }
        public double MaxScore { get; set; } = 100;
        public string Category { get; set; } = "General";
        public bool IsActive { get; set; } = true;
    }

    [HttpPost("kpis")]
    public async Task<IActionResult> SaveKpi([FromBody] KpiInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try
        {
            return Ok(await _svc.SaveKpiAsync(input.PeriodId, input.Id, input.Code, input.Title, input.Description,
                input.Weight, input.MaxScore, input.Category, input.IsActive));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("kpis/{id:int}")]
    public async Task<IActionResult> DeleteKpi(int id)
    {
        if (!await IsHrAsync()) return Forbid();
        try { await _svc.DeleteKpiAsync(id); return Ok(new { ok = true }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ================= نمره‌دهی =================

    public class ScoreRowInput { public int KpiId { get; set; } public double Score { get; set; } public string? Note { get; set; } }

    public class ScoresInput
    {
        public int PeriodId { get; set; }
        public int EmployeeId { get; set; }
        public List<ScoreRowInput> Items { get; set; } = new();
    }

    [HttpPost("scores")]
    public async Task<IActionResult> SaveScores([FromBody] ScoresInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try
        {
            var n = await _svc.SaveScoresAsync(input.PeriodId, input.EmployeeId,
                input.Items.Select(i => new HrPerfService.ScoreItem(i.KpiId, i.Score, i.Note)).ToList(),
                MyUserId, MyName);
            return Ok(new { ok = true, count = n });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("scores")]
    public async Task<IActionResult> Scores([FromQuery] int periodId, [FromQuery] int employeeId)
    {
        if (!await IsHrAsync()) return Forbid();
        return Ok(await _svc.ScoresAsync(periodId, employeeId));
    }

    // ================= کارنامه =================

    [HttpGet("periods/{id:int}/results")]
    public async Task<IActionResult> Results(int id)
    {
        if (!await IsHrAsync()) return Forbid();
        try { return Ok(await _svc.ResultsAsync(id)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    public class ComputeInput { public int PeriodId { get; set; } public int EmployeeId { get; set; } }

    [HttpPost("results/compute")]
    public async Task<IActionResult> Compute([FromBody] ComputeInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try { return Ok(await _svc.ComputeResultAsync(input.PeriodId, input.EmployeeId)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    public class FinalizeInput
    {
        public int PeriodId { get; set; }
        public int EmployeeId { get; set; }
        public int PayYear { get; set; }
        public int PayMonth { get; set; }
        public string? Note { get; set; }
    }

    [HttpPost("results/finalize")]
    public async Task<IActionResult> Finalize([FromBody] FinalizeInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try { return Ok(await _svc.FinalizeAsync(input.PeriodId, input.EmployeeId, input.PayYear, input.PayMonth, MyName, input.Note)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("results/reopen")]
    public async Task<IActionResult> Reopen([FromBody] ComputeInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try { return Ok(await _svc.ReopenResultAsync(input.PeriodId, input.EmployeeId)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("periods/{id:int}/summary")]
    public async Task<IActionResult> Summary(int id)
    {
        if (!await IsHrAsync()) return Forbid();
        try { return Ok(await _svc.SummaryAsync(id)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("employees")]
    public async Task<IActionResult> Employees()
    {
        if (!await IsHrAsync()) return Forbid();
        return Ok(await _db.HrEmployees.AsNoTracking().OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .Select(e => new { e.Id, e.Code, e.FirstName, e.LastName, e.BaseSalary, e.Status })
            .Take(2000).ToListAsync());
    }
}
