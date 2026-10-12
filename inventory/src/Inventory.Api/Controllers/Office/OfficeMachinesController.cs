using Inventory.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;
using Paging = Inventory.Api.Services.Paging;

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
public class OfficeMachinesController : RbacControllerBase
{
    // باگ B8: این کنترلر پیش‌تر مستقیماً از ControllerBase ارث می‌برد، پس نه نقش
    // «Admin,Operator» را داشت (که از ApiControllerBase می‌آید) و نه هیچ بررسی
    // ماژول.اکشن. تنها محافظتش [Authorize] سراسری بود — یعنی «هر» کاربر لاگین‌کرده
    // می‌تواند فهرست ماشین‌ها، سابقهٔ تعمیر و «هزینه‌های مالی» آن‌ها را بخواند و
    // ایجاد/ویرایش/حذف کند.
    //
    // ماژول مجوز عمداً «SystemUsers» انتخاب شد، نه یک ماژول جدید: منوی کلاینت همین
    // صفحه را با پیشوند «SystemUsers.» گیت می‌کند (MainLayout.razor:1365) و آن را در
    // بخش مدیریتی قرار می‌دهد (NavMenu.razor:782). استفاده از همان ماژول باعث می‌شود
    // API و UI دقیقاً یک قاعده داشته باشند و نیاز به تغییر seeder یا کلاینت نباشد.
    // نقش Operator عمداً مجوزهای SystemUsers را نمی‌گیرد (RbacSeeder:438)، پس این
    // صفحه عملاً مدیریتی می‌ماند — همان رفتار قبلی منو، حالا با اجبار سمت سرور.
    private const string Module = "SystemUsers";

    public OfficeMachinesController(AppDbContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? q, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var pagination = new Paging.Request(skip, take);
        var query = Db.OfficeMachines.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToUpper();
            query = query.Where(m => m.Model.ToUpper().Contains(term)
                || (m.SerialNumber ?? "").ToUpper().Contains(term)
                || (m.Location ?? "").ToUpper().Contains(term));
        }
        var machines = await query.OrderBy(m => m.Id).ToPageListAsync(pagination);
        var ids = machines.Select(m => m.Id).ToList();
        var repairs = await Db.OfficeMachineRepairs.AsNoTracking().Where(r => ids.Contains(r.MachineId)).ToListAsync();
        var costs = await Db.OfficeMachineCosts.AsNoTracking().Where(c => ids.Contains(c.MachineId)).ToListAsync();

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

        return Ok(pagination.Result(dto));
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] OfficeMachine m)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        if (string.IsNullOrWhiteSpace(m.Model))
            return BadRequest(new { message = "مدل الزامی است." });
        Db.OfficeMachines.Add(m);
        await Db.SaveChangesAsync();
        return Ok(new { id = m.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(int id, [FromBody] OfficeMachine m)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        var item = await Db.OfficeMachines.FirstOrDefaultAsync(x => x.Id == id);
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
        await Db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>سیستم‌های تاییدشده برای انتخاب در ماشین کابلی — با نام مالک و IP.</summary>
    [HttpGet("systems-lookup")]
    public async Task<IActionResult> SystemsLookup([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await Paging.ResultAsync(Db.SystemInfos.AsNoTracking().Where(s => s.IsApproved)
            .Select(s => new
            {
                s.Id,
                Label = s.AgentId ?? "",
                Ip = Db.SystemNetAdapters.Where(n => n.SystemInfoId == s.Id && n.Ipv4 != "")
                    .Select(n => n.Ipv4).FirstOrDefault() ?? "",
                Owner = Db.SystemUsers.Where(u => u.Id == s.UserId)
                    .Select(u => (u.FirstName + " " + u.LastName).Trim()).FirstOrDefault() ?? ""
            }), skip, take));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;
        var item = await Db.OfficeMachines.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        Db.OfficeMachineRepairs.RemoveRange(Db.OfficeMachineRepairs.Where(r => r.MachineId == id));
        Db.OfficeMachineCosts.RemoveRange(Db.OfficeMachineCosts.Where(c => c.MachineId == id));
        Db.OfficeMachines.Remove(item);
        await Db.SaveChangesAsync();
        return Ok();
    }

    // ================= تعمیرات =================

    [HttpGet("{id}/repairs")]
    public async Task<IActionResult> Repairs(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await Paging.ResultAsync(Db.OfficeMachineRepairs.AsNoTracking()
            .Where(r => r.MachineId == id)
            .OrderByDescending(r => r.RepairDate), skip, take));
    }

    [HttpPost("{id}/repairs")]
    public async Task<IActionResult> AddRepair(int id, [FromBody] OfficeMachineRepair r)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        if (string.IsNullOrWhiteSpace(r.Problem))
            return BadRequest(new { message = "شرح ایراد الزامی است." });
        if (!await Db.OfficeMachines.AnyAsync(m => m.Id == id))
            return NotFound(new { message = "ماشین یافت نشد." });
        r.MachineId = id;
        Db.OfficeMachineRepairs.Add(r);
        await Db.SaveChangesAsync();
        return Ok(new { id = r.Id });
    }

    [HttpPut("repairs/{repairId}")]
    public async Task<IActionResult> UpdateRepair(int repairId, [FromBody] OfficeMachineRepair r)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        var item = await Db.OfficeMachineRepairs.FirstOrDefaultAsync(x => x.Id == repairId);
        if (item == null) return NotFound();
        if (string.IsNullOrWhiteSpace(r.Problem))
            return BadRequest(new { message = "شرح ایراد الزامی است." });
        item.GoneDate = r.GoneDate;
        item.ReturnDate = r.ReturnDate;
        item.Problem = r.Problem;
        item.PerformedWork = r.PerformedWork;
        item.Cost = r.Cost;
        item.Fixed = r.Fixed;
        await Db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("repairs/{repairId}")]
    public async Task<IActionResult> DeleteRepair(int repairId)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;
        var r = await Db.OfficeMachineRepairs.FirstOrDefaultAsync(x => x.Id == repairId);
        if (r == null) return NotFound();
        Db.OfficeMachineRepairs.Remove(r);
        await Db.SaveChangesAsync();
        return Ok();
    }

    // ================= هزینه‌ها =================

    [HttpGet("{id}/costs")]
    public async Task<IActionResult> Costs(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await Paging.ResultAsync(Db.OfficeMachineCosts.AsNoTracking()
            .Where(c => c.MachineId == id)
            .OrderByDescending(c => c.CostDate), skip, take));
    }

    [HttpPost("{id}/costs")]
    public async Task<IActionResult> AddCost(int id, [FromBody] OfficeMachineCost c)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        if (c.Amount <= 0)
            return BadRequest(new { message = "مبلغ باید بزرگ‌تر از صفر باشد." });
        if (!await Db.OfficeMachines.AnyAsync(m => m.Id == id))
            return NotFound(new { message = "ماشین یافت نشد." });
        c.MachineId = id;
        Db.OfficeMachineCosts.Add(c);
        await Db.SaveChangesAsync();
        return Ok(new { id = c.Id });
    }

    [HttpDelete("costs/{costId}")]
    public async Task<IActionResult> DeleteCost(int costId)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;
        var c = await Db.OfficeMachineCosts.FirstOrDefaultAsync(x => x.Id == costId);
        if (c == null) return NotFound();
        Db.OfficeMachineCosts.Remove(c);
        await Db.SaveChangesAsync();
        return Ok();
    }
}
