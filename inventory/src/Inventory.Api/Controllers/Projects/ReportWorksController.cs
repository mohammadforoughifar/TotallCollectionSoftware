using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>گزارش‌های کار روی پروژه‌ها — ماژول دسترسی: ReportWorks</summary>
[Route("api/reportworks")]
public class ReportWorksController : RbacControllerBase
{
    private const string Module = "ReportWorks";

    public ReportWorksController(AppDbContext db) : base(db) { }

    private static string DisplayOf(User u)
    {
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? u.Username : full;
    }

    /// <summary>زمان صرف‌شده خالص = (پایان − شروع) − (صبحانه + ناهار) — حداقل صفر</summary>
    public static TimeSpan CalcSpent(TimeOnly start, TimeOnly end, TimeOnly breakfast, TimeOnly lunch)
    {
        var total = end - start;
        if (total < TimeSpan.Zero) total += TimeSpan.FromDays(1); // شیفت شبانه
        var spent = total - breakfast.ToTimeSpan() - lunch.ToTimeSpan();
        return spent < TimeSpan.Zero ? TimeSpan.Zero : spent;
    }

    /// <summary>لیست گزارش‌های کار با فیلتر</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? projectId,
        [FromQuery] int? userId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;

        var query = Db.ReportWorks.AsNoTracking()
            .Include(r => r.Project)
            .Include(r => r.User)
            .Where(r => !r.IsDelete);

        if (projectId is > 0) query = query.Where(r => r.ProjectId == projectId);
        if (userId is > 0) query = query.Where(r => r.UserId == userId);
        if (from is not null) query = query.Where(r => r.ReportDate >= from.Value.Date);
        if (to is not null) query = query.Where(r => r.ReportDate <= to.Value.Date.AddDays(1).AddTicks(-1));

        var list = await query.OrderByDescending(r => r.ReportDate).ThenByDescending(r => r.Id).ToListAsync();
        return Ok(list.Select(ToDto).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var r = await Db.ReportWorks.AsNoTracking()
            .Include(x => x.Project)
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        return r is null ? NotFound(new { message = "گزارش کار پیدا نشد." }) : Ok(ToDto(r));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ReportWorkDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var err = await ValidateAsync(dto);
        if (err is not null) return err;

        var entity = new ReportWork();
        Map(dto, entity);
        // گزارش بر اساس «کد پروژه» ذخیره می‌شود نه فقط Id (کپی موقع ثبت)
        entity.CodeProject = await CodeOfProjectAsync(dto.ProjectId);
        // کاربر گزارش‌دهنده همیشه کاربر لاگین فعلی است (تو بک‌اند)
        entity.UserId = MyUserId;
        entity.CreatedAt = DateTime.Now;

        Db.ReportWorks.Add(entity);
        await Db.SaveChangesAsync();
        await RecalcProjectTotalAsync(entity.ProjectId);
        return Ok(new { id = entity.Id, spentTime = entity.SpentTime, codeProject = entity.CodeProject });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ReportWorkDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        var err = await ValidateAsync(dto);
        if (err is not null) return err;

        var entity = await Db.ReportWorks.FirstOrDefaultAsync(r => r.Id == id && !r.IsDelete);
        if (entity is null) return NotFound(new { message = "گزارش کار پیدا نشد." });

        var oldProjectId = entity.ProjectId;
        var originUserId = entity.UserId; // ثبت‌کننده اصلی حفظ می‌شود
        Map(dto, entity);
        entity.UserId = originUserId;
        // اگر پروژه عوض شد، کد پروژه هم به‌روزرسانی شود
        if (oldProjectId != entity.ProjectId)
            entity.CodeProject = await CodeOfProjectAsync(entity.ProjectId);
        await Db.SaveChangesAsync();

        await RecalcProjectTotalAsync(entity.ProjectId);
        if (oldProjectId != entity.ProjectId)
            await RecalcProjectTotalAsync(oldProjectId);
        return Ok(new { id = entity.Id, spentTime = entity.SpentTime });
    }

    /// <summary>حذف نرم</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;

        var entity = await Db.ReportWorks.FirstOrDefaultAsync(r => r.Id == id && !r.IsDelete);
        if (entity is null) return NotFound(new { message = "گزارش کار پیدا نشد." });

        entity.IsDelete = true;
        await Db.SaveChangesAsync();
        await RecalcProjectTotalAsync(entity.ProjectId);
        return Ok(new { ok = true });
    }

    // ==================== کمکی ====================

    /// <summary>کد پروژه از روی شناسه (مثل «RE1-2001» یا «2001»)</summary>
    private async Task<string> CodeOfProjectAsync(int projectId)
    {
        var code = await Db.ProjectEntryExits.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.CodeProject)
            .FirstOrDefaultAsync();
        return code ?? "";
    }

    private async Task<IActionResult?> ValidateAsync(ReportWorkDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.WorkDescription))
            return BadRequest(new { message = "شرح کار الزامی است." });
        if (!await Db.ProjectEntryExits.AnyAsync(p => p.Id == dto.ProjectId && !p.IsDelete))
            return BadRequest(new { message = "پروژه انتخاب نشده یا معتبر نیست." });
        if (dto.EndTime == dto.StartTime)
            return BadRequest(new { message = "ساعت شروع و پایان نمی‌توانند یکسان باشند." });
        return null;
    }

    private static void Map(ReportWorkDto dto, ReportWork e)
    {
        e.ReportDate = dto.ReportDate.Date;
        e.UserId = dto.UserId;
        e.WorkDescription = dto.WorkDescription.Trim();
        e.ProjectId = dto.ProjectId;
        e.StartTime = dto.StartTime;
        e.EndTime = dto.EndTime;
        e.BreakfastTime = dto.BreakfastTime;
        e.LunchTime = dto.LunchTime;
        e.SpentTime = CalcSpent(dto.StartTime, dto.EndTime, dto.BreakfastTime, dto.LunchTime);
    }

    /// <summary>جمع ساعات پروژه از روی گزارش‌های فعال به‌روزرسانی می‌شود</summary>
    private async Task RecalcProjectTotalAsync(int projectId)
    {
        var project = await Db.ProjectEntryExits.FirstOrDefaultAsync(p => p.Id == projectId);
        if (project is null) return;
        // جمع در حافظه — EF نمی‌تواند Sum روی TimeSpan را به SQL ترجمه کند
        var spans = await Db.ReportWorks
            .Where(r => r.ProjectId == projectId && !r.IsDelete)
            .Select(r => r.SpentTime)
            .ToListAsync();
        project.TotalSpentTime = TimeSpan.FromTicks(spans.Sum(s => s.Ticks));
        await Db.SaveChangesAsync();
    }

    private static ReportWorkDto ToDto(ReportWork r) => new()
    {
        Id = r.Id,
        ReportDate = r.ReportDate,
        UserId = r.UserId,
        WorkDescription = r.WorkDescription,
        ProjectId = r.ProjectId,
        // اولویت با کد ذخیره‌شده روی گزارش (اسنادی)؛ برای داده‌های خیلی قدیمی از خود پروژه
        CodeProject = !string.IsNullOrEmpty(r.CodeProject) ? r.CodeProject : (r.Project?.CodeProject ?? ""),
        StartTime = r.StartTime,
        EndTime = r.EndTime,
        BreakfastTime = r.BreakfastTime,
        LunchTime = r.LunchTime,
        SpentTime = r.SpentTime,
        ProjectName = r.Project is null ? null : r.Project.ProjectName,
        UserName = r.User is null ? null : DisplayOf(r.User)
    };
}
