using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services.Core;
using Inventory.Api.Services.DevTeam;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.DevTeam;

/// <summary>
/// مدیریت برنامه‌نویسان — اعضای تیم، مالکیت ماژول‌ها، آیتم‌های کاری و بورد.
/// <para>
/// دسترسی‌ها از ماژول <c>DevTeam</c> می‌آیند: Read (مشاهده)، Create، Update،
/// Delete و Manage (تعیین مالک ماژول و هم‌زمان‌سازی با مخزن).
/// </para>
/// </summary>
[ApiController]
[Route("api/devteam")]
[Authorize]
public class DevTeamController : ControllerBase
{
    private const string Module = "DevTeam";

    private readonly AppDbContext _db;
    private readonly IDevTeamService _svc;
    private readonly IEffectivePermissions _perms;

    public DevTeamController(AppDbContext db, IDevTeamService svc, IEffectivePermissions perms)
    {
        _db = db;
        _svc = svc;
        _perms = perms;
    }

    // ================== دسترسی‌ها ==================

    private Task<HashSet<string>> MyPermissionsAsync() => _perms.GetAsync(User);

    private async Task<bool> HasAsync(string action) =>
        await _perms.IsLegacyAdminAsync(User) || (await MyPermissionsAsync()).Contains($"{Module}.{action}");

    private string MyDisplayName => User.FindFirstValue(ClaimTypes.Name) ?? "";

    /// <summary>
    /// اگر مجوز نداشته باشد پاسخ ۴۰۳ فارسی می‌دهد، وگرنه null.
    /// <para>
    /// نوع بازگشتی عمداً <c>ObjectResult</c> است نه <c>IActionResult</c>: اکشن‌هایی که
    /// <c>ActionResult&lt;T&gt;</c> برمی‌گردانند فقط از <c>ActionResult</c> تبدیل ضمنی دارند،
    /// پس با IActionResult کامپایل نمی‌شد.
    /// </para>
    /// </summary>
    private async Task<ObjectResult?> GuardAsync(string action)
        => await HasAsync(action) ? null : StatusCode(403, new { message = $"شما مجوز «{action}» ماژول مدیریت برنامه‌نویسان را ندارید." });

    /// <summary>
    /// جدول‌ها را در نخستین استفاده می‌سازد. همان الگوی داشبورد شخصی و کارتابل
    /// نامه وارده: چون Migrate/EnsureCreated جدول تازه را به دیتابیس قدیمی
    /// اضافه نمی‌کنند، اینجا خودتعمیری انجام می‌شود.
    /// </summary>
    private Task EnsureSchemaAsync() => DevTeamSchemaV1.EnsureOnceAsync(_db);

    // ================== بورد و شاخص‌ها ==================

    /// <summary>
    /// کل بورد در یک پاسخ: ستون‌ها، بار کاری اعضا، بار ماژول‌ها و شاخص‌ها.
    /// این همان نقطه‌ای است که پرسش «کدام آیتم در حال انجام است» پاسخ می‌گیرد.
    /// </summary>
    [HttpGet("board")]
    public async Task<ActionResult<DevBoardDto>> GetBoard(
        [FromQuery] int? moduleId, [FromQuery] int? assigneeId)
    {
        if (await GuardAsync("Read") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        return Ok(await _svc.GetBoardAsync(moduleId, assigneeId));
    }

    /// <summary>
    /// رویدادهای اخیر تیم — پاسخ «چه کسی روی چه بخشی کار کرده».
    /// </summary>
    [HttpGet("activity")]
    public async Task<ActionResult<List<DevTaskLogDto>>> GetActivity(
        [FromQuery] int days = 14, [FromQuery] int take = 200)
    {
        if (await GuardAsync("Read") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        return Ok(await _svc.GetActivityAsync(days, take));
    }

    // ================== اعضای تیم ==================

    [HttpGet("members")]
    public async Task<ActionResult<List<DevMemberDto>>> GetMembers([FromQuery] bool activeOnly = false)
    {
        if (await GuardAsync("Read") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        return Ok(await _svc.GetMembersAsync(activeOnly));
    }

    [HttpPost("members")]
    public async Task<ActionResult<DevMemberDto>> SaveMember([FromBody] DevMemberDto dto)
    {
        if (await GuardAsync(dto.Id > 0 ? "Update" : "Create") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        return Ok(await _svc.SaveMemberAsync(dto));
    }

    [HttpDelete("members/{id:int}")]
    public async Task<IActionResult> DeleteMember(int id)
    {
        if (await GuardAsync("Delete") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        await _svc.DeleteMemberAsync(id);
        return Ok(new { ok = true });
    }

    // ================== ماژول‌ها و مالکیت ==================

    [HttpGet("modules")]
    public async Task<ActionResult<List<DevModuleDto>>> GetModules([FromQuery] bool activeOnly = false)
    {
        if (await GuardAsync("Read") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        return Ok(await _svc.GetModulesAsync(activeOnly));
    }

    [HttpPost("modules")]
    public async Task<ActionResult<DevModuleDto>> SaveModule([FromBody] DevModuleDto dto)
    {
        if (await GuardAsync(dto.Id > 0 ? "Update" : "Create") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        return Ok(await _svc.SaveModuleAsync(dto));
    }

    [HttpDelete("modules/{id:int}")]
    public async Task<IActionResult> DeleteModule(int id)
    {
        if (await GuardAsync("Delete") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        await _svc.DeleteModuleAsync(id);
        return Ok(new { ok = true });
    }

    /// <summary>تعیین مالک ماژول — همان چیزی که بعداً CODEOWNERS از آن ساخته می‌شود.</summary>
    [HttpPost("modules/{id:int}/owner")]
    public async Task<ActionResult<DevModuleDto>> SetModuleOwner(int id, [FromBody] SetOwnerRequest request)
    {
        if (await GuardAsync("Manage") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        return Ok(await _svc.SetModuleOwnerAsync(id, request.OwnerId));
    }

    /// <summary>
    /// هم‌زمان‌سازی فهرست ماژول‌ها با ساختار واقعی مخزن از روی
    /// <c>tools/ownership-map.csv</c>. فقط ماژول‌های تازه را اضافه می‌کند و
    /// مالک‌های تعیین‌شدهٔ دستی را دست نمی‌زند.
    /// </summary>
    [HttpPost("modules/sync-from-repo")]
    public async Task<IActionResult> SyncModulesFromRepo()
    {
        if (await GuardAsync("Manage") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();

        // فایل در ریشهٔ مخزن است (tools/ownership-map.csv) ولی سرویس می‌تواند از
        // bin/Debug، از پوشهٔ پروژه یا از مسیر انتشار اجرا شود. به‌جای حدس زدنِ تعداد
        // «..»، از هر دو مبدأ به بالا می‌رویم و نخستین نمونهٔ موجود را برمی‌داریم؛
        // در استقرار واقعی هم نسخهٔ کپی‌شده کنار exe (Content در csproj) پیدا می‌شود.
        var path = FindOwnershipMap();
        if (path is null)
            return BadRequest(new
            {
                message = "فایل tools/ownership-map.csv پیدا نشد. آن را از ریشهٔ مخزن کنار سرویس API بگذارید " +
                          "یا مطمئن شوید برنامه از داخل مخزن اجرا می‌شود."
            });

        var added = await _svc.SyncModulesFromRepoAsync(path);
        return Ok(new { ok = true, added, source = Path.GetFullPath(path) });
    }

    // ================== آیتم‌های کاری ==================

    [HttpGet("tasks")]
    public async Task<ActionResult<PagedResult<DevTaskDto>>> GetTasks(
        [FromQuery] string? search, [FromQuery] DevTaskStatus? status,
        [FromQuery] int? moduleId, [FromQuery] int? assigneeId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (await GuardAsync("Read") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        return Ok(await _svc.GetTasksAsync(search, status, moduleId, assigneeId, page, pageSize));
    }

    [HttpGet("tasks/{id:int}")]
    public async Task<ActionResult<DevTaskDto>> GetTask(int id)
    {
        if (await GuardAsync("Read") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        var t = await _svc.GetTaskAsync(id);
        return t is null ? NotFound(new { message = "آیتم یافت نشد." }) : Ok(t);
    }

    [HttpPost("tasks")]
    public async Task<ActionResult<DevTaskDto>> SaveTask([FromBody] DevTaskDto dto)
    {
        if (await GuardAsync(dto.Id > 0 ? "Update" : "Create") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        if (dto.Id <= 0 && string.IsNullOrWhiteSpace(dto.CreatedBy)) dto.CreatedBy = MyDisplayName;
        return Ok(await _svc.SaveTaskAsync(dto));
    }

    [HttpDelete("tasks/{id:int}")]
    public async Task<IActionResult> DeleteTask(int id)
    {
        if (await GuardAsync("Delete") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        await _svc.DeleteTaskAsync(id);
        return Ok(new { ok = true });
    }

    /// <summary>تغییر وضعیت آیتم — ستون بورد را جابه‌جا می‌کند و رویدادش را در تاریخچه ثبت می‌کند.</summary>
    [HttpPost("tasks/{id:int}/status")]
    public async Task<ActionResult<DevTaskDto>> SetStatus(int id, [FromBody] DevTaskStatusRequest request)
    {
        if (await GuardAsync("Update") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        return Ok(await _svc.SetStatusAsync(id, request));
    }

    /// <summary>واگذاری آیتم به یک عضو تیم.</summary>
    [HttpPost("tasks/{id:int}/assign")]
    public async Task<ActionResult<DevTaskDto>> Assign(int id, [FromBody] AssignRequest request)
    {
        if (await GuardAsync("Update") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        return Ok(await _svc.AssignAsync(id, request.MemberId, request.Note));
    }

    /// <summary>تاریخچهٔ یک آیتم — چه کسی و چه زمانی چه کرد.</summary>
    [HttpGet("tasks/{id:int}/logs")]
    public async Task<ActionResult<List<DevTaskLogDto>>> GetTaskLogs(int id)
    {
        if (await GuardAsync("Read") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();
        return Ok(await _svc.GetTaskLogsAsync(id));
    }

    /// <summary>ثبت یک رویداد دستی در تاریخچهٔ آیتم (پیشرفت، کامیت، بازبینی، یادداشت).</summary>
    [HttpPost("tasks/{id:int}/logs")]
    public async Task<ActionResult<DevTaskLogDto>> AddTaskLog(int id, [FromBody] DevTaskLogRequest request)
    {
        if (await GuardAsync("Update") is ObjectResult denied) return denied;
        await EnsureSchemaAsync();

        return Ok(await _svc.AddLogAsync(id, request));
    }

    /// <summary>
    /// یافتن <c>tools/ownership-map.csv</c> با پیمایش رو به بالا از دو مبدأ
    /// «پوشهٔ کاری جاری» و «پوشهٔ اجرایی». حداکثر ۸ سطح بالا می‌رود تا هم در
    /// اجرای توسعه (bin/Debug/net8.0) و هم در استقرار واقعی جواب بدهد.
    /// </summary>
    private static string? FindOwnershipMap()
    {
        foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(root);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "tools", "ownership-map.csv");
                if (System.IO.File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}

/// <summary>بدنهٔ درخواست تعیین مالک ماژول.</summary>
public class SetOwnerRequest
{
    public int? OwnerId { get; set; }
}

/// <summary>بدنهٔ درخواست واگذاری آیتم.</summary>
public class AssignRequest
{
    public int? MemberId { get; set; }
    public string? Note { get; set; }
}
