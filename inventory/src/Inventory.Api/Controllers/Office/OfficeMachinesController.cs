using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;

namespace Inventory.Api.Controllers;

public class MachineDto
{
    public int Id { get; set; }
    public string Model { get; set; } = "";
    public string? SerialNumber { get; set; }
    public string? Location { get; set; }
    public DateTime? InstallDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? GoneDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public int RepairCount { get; set; }
    public decimal TotalCost { get; set; }
}

/// <summary>مدیریت ماشین‌های اداری + سابقه‌ی تعمیر + هزینه‌ها.</summary>
[ApiController]
[Route("api/[controller]")]
public class OfficeMachinesController : ControllerBase
{
    private readonly AppDbContext _db;
    public OfficeMachinesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? q)
    {
        var machines = await _db.OfficeMachines.AsNoTracking().ToListAsync();
        var repairs = await _db.OfficeMachineRepairs.AsNoTracking().ToListAsync();
        var costs = await _db.OfficeMachineCosts.AsNoTracking().ToListAsync();

        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            machines = machines.Where(m =>
                m.Model.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (m.SerialNumber ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (m.Location ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var dto = machines.OrderBy(m => m.Id).Select(m => new MachineDto
        {
            Id = m.Id,
            Model = m.Model,
            SerialNumber = m.SerialNumber,
            Location = m.Location,
            InstallDate = m.InstallDate,
            IsActive = m.IsActive,
            GoneDate = m.GoneDate,
            ReturnDate = m.ReturnDate,
            Notes = m.Notes,
            CreatedAt = m.CreatedAt,
            RepairCount = repairs.Count(r => r.MachineId == m.Id),
            TotalCost = costs.Where(c => c.MachineId == m.Id).Sum(c => c.Amount)
                           + repairs.Where(r => r.MachineId == m.Id).Sum(r => r.Cost)
        }).ToList();

        return Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] OfficeMachine m)
    {
        if (string.IsNullOrWhiteSpace(m.Model))
            return BadRequest(new { message = "مدل الزامی است." });
        _db.OfficeMachines.Add(m);
        await _db.SaveChangesAsync();
        return Ok(new { id = m.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(int id, [FromBody] OfficeMachine m)
    {
        var item = await _db.OfficeMachines.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        item.Model = m.Model;
        item.SerialNumber = m.SerialNumber;
        item.Location = m.Location;
        item.InstallDate = m.InstallDate;
        item.IsActive = m.IsActive;
        item.GoneDate = m.GoneDate;
        item.ReturnDate = m.ReturnDate;
        item.Notes = m.Notes;
        // نوع اتصال: شبکه (IP) یا کابلی (سیستم متصل)
        item.ConnectionType = m.ConnectionType == "Cable" ? "Cable" : "Network";
        item.IpAddress = item.ConnectionType == "Network" ? m.IpAddress : null;
        item.LinkedSystemInfoId = item.ConnectionType == "Cable" ? m.LinkedSystemInfoId : null;
        item.LinkedSystemLabel = item.ConnectionType == "Cable" ? m.LinkedSystemLabel : null;
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>سیستم‌های تاییدشده برای انتخاب در ماشین کابلی — با نام مالک و IP.</summary>
    [HttpGet("systems-lookup")]
    public async Task<IActionResult> SystemsLookup() =>
        Ok(await _db.SystemInfos.AsNoTracking().Where(s => s.IsApproved)
            .Select(s => new
            {
                s.Id,
                Label = s.AgentId ?? "",
                Ip = _db.SystemNetAdapters.Where(n => n.SystemInfoId == s.Id && n.Ipv4 != "")
                    .Select(n => n.Ipv4).FirstOrDefault() ?? "",
                Owner = _db.SystemUsers.Where(u => u.Id == s.UserId)
                    .Select(u => (u.FirstName + " " + u.LastName).Trim()).FirstOrDefault() ?? ""
            }).ToListAsync());

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.OfficeMachines.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        _db.OfficeMachineRepairs.RemoveRange(_db.OfficeMachineRepairs.Where(r => r.MachineId == id));
        _db.OfficeMachineCosts.RemoveRange(_db.OfficeMachineCosts.Where(c => c.MachineId == id));
        _db.OfficeMachines.Remove(item);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ================= تعمیرات =================

    [HttpGet("{id}/repairs")]
    public async Task<IActionResult> Repairs(int id)
        => Ok(await _db.OfficeMachineRepairs.AsNoTracking()
            .Where(r => r.MachineId == id)
            .OrderByDescending(r => r.RepairDate)
            .ToListAsync());

    [HttpPost("{id}/repairs")]
    public async Task<IActionResult> AddRepair(int id, [FromBody] OfficeMachineRepair r)
    {
        if (string.IsNullOrWhiteSpace(r.Problem))
            return BadRequest(new { message = "شرح ایراد الزامی است." });
        if (!await _db.OfficeMachines.AnyAsync(m => m.Id == id))
            return NotFound(new { message = "ماشین یافت نشد." });
        r.MachineId = id;
        _db.OfficeMachineRepairs.Add(r);
        await _db.SaveChangesAsync();
        return Ok(new { id = r.Id });
    }

    [HttpPut("repairs/{repairId}")]
    public async Task<IActionResult> UpdateRepair(int repairId, [FromBody] OfficeMachineRepair r)
    {
        var item = await _db.OfficeMachineRepairs.FirstOrDefaultAsync(x => x.Id == repairId);
        if (item == null) return NotFound();
        if (string.IsNullOrWhiteSpace(r.Problem))
            return BadRequest(new { message = "شرح ایراد الزامی است." });
        item.GoneDate = r.GoneDate;
        item.ReturnDate = r.ReturnDate;
        item.Problem = r.Problem;
        item.PerformedWork = r.PerformedWork;
        item.Cost = r.Cost;
        item.Fixed = r.Fixed;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("repairs/{repairId}")]
    public async Task<IActionResult> DeleteRepair(int repairId)
    {
        var r = await _db.OfficeMachineRepairs.FirstOrDefaultAsync(x => x.Id == repairId);
        if (r == null) return NotFound();
        _db.OfficeMachineRepairs.Remove(r);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ================= هزینه‌ها =================

    [HttpGet("{id}/costs")]
    public async Task<IActionResult> Costs(int id)
        => Ok(await _db.OfficeMachineCosts.AsNoTracking()
            .Where(c => c.MachineId == id)
            .OrderByDescending(c => c.CostDate)
            .ToListAsync());

    [HttpPost("{id}/costs")]
    public async Task<IActionResult> AddCost(int id, [FromBody] OfficeMachineCost c)
    {
        if (c.Amount <= 0)
            return BadRequest(new { message = "مبلغ باید بزرگ‌تر از صفر باشد." });
        if (!await _db.OfficeMachines.AnyAsync(m => m.Id == id))
            return NotFound(new { message = "ماشین یافت نشد." });
        c.MachineId = id;
        _db.OfficeMachineCosts.Add(c);
        await _db.SaveChangesAsync();
        return Ok(new { id = c.Id });
    }

    [HttpDelete("costs/{costId}")]
    public async Task<IActionResult> DeleteCost(int costId)
    {
        var c = await _db.OfficeMachineCosts.FirstOrDefaultAsync(x => x.Id == costId);
        if (c == null) return NotFound();
        _db.OfficeMachineCosts.Remove(c);
        await _db.SaveChangesAsync();
        return Ok();
    }
}
