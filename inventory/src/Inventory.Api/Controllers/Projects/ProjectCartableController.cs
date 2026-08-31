using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// کارتابل بخش مدیریت پروژه — دو صف جدا:
///  • صف «مدیر»: پروژه‌های تازه‌ثبت‌شده (FlowStatus=0) → تایید (به کارتابل کارشناسی) یا رد (با دلیل).
///  • صف «کارشناسی»: پروژه‌های تاییدشدهٔ مدیر (FlowStatus=1) → ثبت «اتمام کارشناسی» (نهایی = 3).
/// همهٔ رویدادها: زنگ اعلان به گروه مجاز + پخش بلادرنگ (SignalR) برای رفرش زندهٔ لیست‌ها/کارتابل‌ها.
/// ماژول دسترسی: ProjectCartable — اکشن‌ها: Read (مشاهده/شمارش)، Manager (تایید/رد)، Expert (اتمام کارشناسی).
/// </summary>
[Route("api/projectcartable")]
public class ProjectCartableController : RbacControllerBase
{
    private const string CCModule = "ProjectCartable";
    private readonly INotifyService _notify;

    public ProjectCartableController(AppDbContext db, INotifyService notify) : base(db) => _notify = notify;

    private static string DisplayOf(User u)
    {
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? u.Username : full;
    }

    /// <summary>نام فارسی وضعیت گردش‌کار</summary>
    public static string FlowNameFa(int s) => s switch
    {
        0 => "در انتظار تایید مدیر",
        1 => "در انتظار کارشناسی",
        2 => "رد شده توسط مدیر",
        3 => "نهایی (کارشناسی انجام شد)",
        _ => "-"
    };

    private Task<bool> CanManagerAsync() => HasAsync(CCModule, "Manager");
    private Task<bool> CanExpertAsync() => HasAsync(CCModule, "Expert");

    // ==================== شمارش برای بج/عنوان صفحه ====================
    [HttpGet("counts")]
    public async Task<IActionResult> Counts()
    {
        if (await ForbiddenUnlessAsync(CCModule, "Read") is { } forbid) return forbid;
        return Ok(new ProjectCartableCountsDto
        {
            Manager = await Db.ProjectEntryExits.CountAsync(p => !p.IsDelete && p.FlowStatus == 0),
            Expert = await Db.ProjectEntryExits.CountAsync(p => !p.IsDelete && p.FlowStatus == 1)
        });
    }

    // ==================== لیست صف‌ها ====================
    /// <param name="kind">manager (در انتظار تایید مدیر) | expert (در انتظار کارشناسی)</param>
    [HttpGet("queue")]
    public async Task<IActionResult> Queue([FromQuery] string? kind)
    {
        if (await ForbiddenUnlessAsync(CCModule, "Read") is { } forbid) return forbid;

        var isManager = string.Equals(kind, "manager", StringComparison.OrdinalIgnoreCase);
        var status = isManager ? 0 : 1;

        // کارتابل FIFO — قدیمی‌ترین در انتظار، بالای لیست
        var list = await Db.ProjectEntryExits.AsNoTracking()
            .Include(p => p.KarFarma)
            .Include(p => p.User)
            .Include(p => p.Attaches.Where(a => !a.IsDelete))
            .Where(p => !p.IsDelete && p.FlowStatus == status)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToListAsync();

        var today = DateTime.Today;
        return Ok(list.Select(p => new
        {
            p.Id,
            p.CodeProject,
            p.ReturnProjectId,
            p.SerialNumber,
            p.ProjectName,
            KarFarmaName = p.KarFarma?.Name,
            p.EntryDate,
            p.ExitDate,
            p.CreatedAt,
            RegisterUser = p.User is null ? null : DisplayOf(p.User),
            DaysWaiting = Math.Max(0, (today - p.CreatedAt.Date).Days),
            AttachCount = p.Attaches.Count
        }).ToList());
    }

    // ==================== اکشن‌های مدیر ====================
    /// <summary>تایید مدیر — پروژه به کارتابل کارشناسی می‌رود</summary>
    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, [FromBody] ProjectFlowActionDto dto)
    {
        if (!await CanManagerAsync())
            return StatusCode(403, new { message = "شما مجوز تایید مدیر (ProjectCartable.Manager) را ندارید." });

        var p = await Db.ProjectEntryExits.FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (p.FlowStatus != 0)
            return BadRequest(new { message = $"این پروژه دیگر در کارتابل مدیر نیست — وضعیت فعلی: «{FlowNameFa(p.FlowStatus)}»." });

        p.FlowStatus = 1;
        p.ManagerActionById = MyUserId;
        p.ManagerActionAt = DateTime.Now;
        p.ManagerNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        await Db.SaveChangesAsync();

        var me = await MyDisplayAsync();
        try
        {
            // زنگ به کارشناس‌ها: پروژه در صف کارشناسی آماده است
            var experts = await UsersWithPermissionAsync(CCModule, "Expert", excludeSelf: true);
            await _notify.SendManyAsync(experts, "پروژه در کارتابل کارشناسی",
                $"«{p.ProjectName}» (کد {p.CodeProject}) توسط مدیر تایید شد — آمادهٔ کارشناسی.",
                me, "مدیریت پروژه‌ها", $"/project-cartable?queue=expert");
            // زنگ به ثبت‌کننده: مدیر تایید کرد
            if (p.UserId != MyUserId)
                await _notify.SendAsync(p.UserId, "تایید مدیر",
                    $"پروژهٔ «{p.ProjectName}» (کد {p.CodeProject}) توسط مدیر تایید شد." +
                    (p.ManagerNote is null ? "" : $" یادداشت: {p.ManagerNote}"),
                    me, "مدیریت پروژه‌ها", "/projects");
            await _notify.BroadcastChangedAsync("projects");
        }
        catch { }

        return Ok(new { id = p.Id, flowStatus = p.FlowStatus });
    }

    /// <summary>رد مدیر — دلیل رد الزامی است</summary>
    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] ProjectFlowActionDto dto)
    {
        if (!await CanManagerAsync())
            return StatusCode(403, new { message = "شما مجوز تایید/رد مدیر (ProjectCartable.Manager) را ندارید." });

        var note = dto.Note?.Trim();
        if (string.IsNullOrWhiteSpace(note))
            return BadRequest(new { message = "دلیل رد پروژه را بنویسید — بدون دلیل، رد ممکن نیست." });

        var p = await Db.ProjectEntryExits.FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (p.FlowStatus != 0)
            return BadRequest(new { message = $"این پروژه دیگر در کارتابل مدیر نیست — وضعیت فعلی: «{FlowNameFa(p.FlowStatus)}»." });

        p.FlowStatus = 2;
        p.ManagerActionById = MyUserId;
        p.ManagerActionAt = DateTime.Now;
        p.ManagerNote = note;
        await Db.SaveChangesAsync();

        var me = await MyDisplayAsync();
        try
        {
            if (p.UserId != MyUserId)
                await _notify.SendAsync(p.UserId, "رد پروژه توسط مدیر",
                    $"پروژهٔ «{p.ProjectName}» (کد {p.CodeProject}) رد شد. دلیل: {note}",
                    me, "مدیریت پروژه‌ها", "/projects");
            await _notify.BroadcastChangedAsync("projects");
        }
        catch { }

        return Ok(new { id = p.Id, flowStatus = p.FlowStatus });
    }

    // ==================== اکشن کارشناس ====================
    /// <summary>اتمام کارشناسی — پروژه نهایی و از کارتابل خارج می‌شود</summary>
    [HttpPost("{id:int}/expert-done")]
    public async Task<IActionResult> ExpertDone(int id, [FromBody] ProjectFlowActionDto dto)
    {
        if (!await CanExpertAsync())
            return StatusCode(403, new { message = "شما مجوز اتمام کارشناسی (ProjectCartable.Expert) را ندارید." });

        var p = await Db.ProjectEntryExits.FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (p.FlowStatus != 1)
            return BadRequest(new { message = p.FlowStatus == 0
                ? "این پروژه هنوز در کارتابل مدیر است — ابتدا باید مدیر تایید کند."
                : $"این پروژه دیگر در کارتابل کارشناسی نیست — وضعیت فعلی: «{FlowNameFa(p.FlowStatus)}»." });

        p.FlowStatus = 3;
        p.ExpertActionById = MyUserId;
        p.ExpertActionAt = DateTime.Now;
        p.ExpertNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        await Db.SaveChangesAsync();

        var me = await MyDisplayAsync();
        try
        {
            var managers = await UsersWithPermissionAsync(CCModule, "Manager", excludeSelf: true);
            await _notify.SendManyAsync(managers, "کارشناسی پروژه انجام شد",
                $"«{p.ProjectName}» (کد {p.CodeProject}) از کارتابل کارشناسی خارج و نهایی شد.",
                me, "مدیریت پروژه‌ها", "/projects");
            if (p.UserId != MyUserId)
                await _notify.SendAsync(p.UserId, "پروژه نهایی شد",
                    $"کارشناسی «{p.ProjectName}» (کد {p.CodeProject}) انجام شد." +
                    (p.ExpertNote is null ? "" : $" یادداشت کارشناس: {p.ExpertNote}"),
                    me, "مدیریت پروژه‌ها", "/projects");
            await _notify.BroadcastChangedAsync("projects");
        }
        catch { }

        return Ok(new { id = p.Id, flowStatus = p.FlowStatus });
    }

    private async Task<string> MyDisplayAsync()
    {
        var u = await Db.Users.FindAsync(MyUserId);
        if (u is null) return MyUsername;
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? u.Username : full;
    }
}
