using System.Globalization;
using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Inventory.Api.Services.ItAssets;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>پروژه کاری — پوشه سبک روی دستورهای کار (مدت‌زمان، تمدید، اعضا، پیشرفت وزنی خودکار).</summary>
[ApiController]
[Route("api/workprojects")]
[Authorize]
public class WorkProjectsController : ControllerBase
{
    private const string Module = "WorkProjects";
    private const int MaxExtension = 5;

    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    public WorkProjectsController(AppDbContext db, INotifyService notify)
    { _db = db; _notify = notify; }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyUsername => User.FindFirstValue(ClaimTypes.Name) ?? "";
    private bool IsLegacyAdmin => User.IsInRole("Admin");

    private async Task<int?> ActiveCompanyIdAsync()
    {
        if (!int.TryParse(Request.Headers["X-Company-Id"].FirstOrDefault(), out var id) || id <= 0) return null;
        return await _db.UserCompanyAccesses.AnyAsync(x => x.UserId == MyUserId && x.CompanyId == id) ? id : null;
    }

    private async Task<string> MyDisplayNameAsync()
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == MyUserId);
        var n = UserDisplay.Name(me);
        return string.IsNullOrWhiteSpace(n) ? MyUsername : n;
    }

    /// <summary>بررسی مجوز با ماژول دلخواه (مثلاً «دستور کار» برای کمبوی پروژه در فیلتر).</summary>
    private async Task<bool> HasAsyncIn(string module, string action)
    {
        var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive));
        if (!hasRoles)
        {
            var legacy = User.FindFirstValue(ClaimTypes.Role);
            if (legacy == "Admin") return true;
            if (legacy == "Operator") return action is "Create" or "View";
            return false;
        }
        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == module && p.Action == action);
    }

    /// <summary>مثل دستور کار: ادمین قدیمی همه‌چیز، اپراتور قدیمی مشاهده/ساخت، بقیه از RBAC.</summary>
    private async Task<bool> HasAsync(string action)
    {
        var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive));
        if (!hasRoles)
        {
            var legacy = User.FindFirstValue(ClaimTypes.Role);
            if (legacy == "Admin") return true;
            if (legacy == "Operator") return action is "Create" or "View";
            return false;
        }
        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == Module && p.Action == action);
    }

    /// <summary>ویرایش/تمدید/بستن/اعضا: مالک (مدیر پروژه) همیشه مجاز است، بقیه با مجوز Update.</summary>
    private async Task<bool> CanManageAsync(WorkProject p)
        => p.OwnerUserId == MyUserId || IsLegacyAdmin || await HasAsync("Update");

    private void Log(int projectId, string action, string? text) =>
        _db.WorkProjectLogs.Add(new WorkProjectLog { ProjectId = projectId, ActorName = MyUsername, Action = action, Text = text });

    private static string ToFa(DateTime d)
    {
        var pc = new PersianCalendar();
        return $"{pc.GetYear(d)}/{pc.GetMonth(d):00}/{pc.GetDayOfMonth(d):00}";
    }

    private static string? NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags == null) return null;
        var list = tags.Select(t => (t ?? "").Trim().Replace(",", "،"))
            .Where(t => t.Length > 0)
            .Select(t => t.Length > 30 ? t[..30] : t)
            .Distinct().Take(5).ToList();
        return list.Count == 0 ? null : "," + string.Join(",", list) + ",";
    }

    private static List<string> TagsToList(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? new List<string>()
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>پروژه بازِ حذف‌نشدهٔ شرکت فعال (رکورد حذف‌شده با فیلتر سراسری دیده نمی‌شود).</summary>
    private Task<WorkProject?> GetProjectAsync(int id, int companyId)
        => _db.WorkProjects.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);

    // ================== دسترسی‌ها ==================

    [HttpGet("my-access")]
    public async Task<IActionResult> MyAccess() => Ok(new
    {
        userId = MyUserId,
        canView = await HasAsync("View"),
        canCreate = await HasAsync("Create"),
        canUpdate = await HasAsync("Update"),
        canDelete = await HasAsync("Delete")
    });

    /// <summary>کاربران فعال — برای انتخاب اعضای پروژه.</summary>
    [HttpGet("users")]
    public async Task<IActionResult> Users()
    {
        if (!await HasAsync("View")) return Forbid();
        // نام نمایشی در حافظه ساخته می‌شود (متد سی‌شارپی قابل ترجمه به SQL نیست)
        var users = await _db.Users.AsNoTracking().Where(u => u.IsActive)
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ToListAsync();
        return Ok(users.Select(u => new LookupItem { Id = u.Id, Name = UserDisplay.Name(u) }).ToList());
    }

    /// <summary>جستجوی سبک پروژه‌های باز — برای کمبوی «پروژه» در فرم دستور کار.</summary>
    /// <summary>
    /// جستجوی سمت سرور پروژه‌های کاری برای کمبوها.
    /// <para>
    /// نکته‌های دسترسی: پیش‌تر فقط با مجوز «پروژه‌های کاری/مشاهده» جواب می‌داد؛ کاربری که
    /// تنها مجوز «دستور کار» داشت، فیلتر پروژهٔ همان صفحه را خالی می‌دید (۴۰۳ و بلعیده‌شدن
    /// خطا در کلاینت) ⇒ «فیلتر پروژه کار نمی‌کند». اکنون مجوز دستور کار هم کافی است.
    /// </para>
    /// <param name="openOnly">true (پیش‌فرض): فقط پروژه‌های باز — برای فرم اتصال دستور کار.
    /// برای «فیلتر» باید false باشد، وگرنه دستورهای متصل به پروژهٔ بسته‌شده قابل فیلتر نیستند.</param>
    /// </summary>
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] string? q, [FromQuery] int? take, [FromQuery] bool openOnly = true)
    {
        if (!await HasAsync("View") && !await HasAsyncIn("WorkOrders", "View")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var query = _db.WorkProjects.AsNoTracking().Where(p => p.CompanyId == companyId.Value);
        if (openOnly) query = query.Where(p => p.Status == WorkProjectStatus.Open);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = Inventory.Shared.Fa.ToEn(q.Trim()).Replace('\u064A', '\u06CC').Replace('\u0643', '\u06A9');
            query = query.Where(p => EF.Functions.Like(p.Title, $"%{term}%") || EF.Functions.Like(p.Number, $"%{term}%"));
        }
        var n = Math.Clamp(take is > 0 ? take.Value : 20, 1, 50);
        var list = await query.OrderBy(p => p.DueAt).ThenByDescending(p => p.Id).Take(n)
            .Select(p => new WorkProjectLookupDto { Id = p.Id, Code = p.Number, Name = p.Title, DueAt = p.DueAt })
            .ToListAsync();
        return Ok(list);
    }

    // ================== لیست + جزئیات ==================

    private record ProjStats(int OrderCount, int Open, int Closed, int Overdue, int? Progress);

    /// <summary>آمار دستورهای چند پروژه در ۲ کوئری (دستورها + چک‌لیست‌ها).</summary>
    private async Task<Dictionary<int, ProjStats>> StatsForAsync(List<int> ids, DateTime now)
    {
        var empty = ids.ToDictionary(id => id, _ => new ProjStats(0, 0, 0, 0, null));
        if (ids.Count == 0) return empty;
        var orders = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.ProjectId != null && ids.Contains(w.ProjectId.Value))
            .Select(w => new { w.Id, Pid = w.ProjectId!.Value, w.Status, w.Weight, w.DueAt })
            .ToListAsync();
        if (orders.Count == 0) return empty;
        var orderIds = orders.Select(o => o.Id).ToList();
        var cl = await _db.WorkOrderChecklistItems.AsNoTracking()
            .Where(c => orderIds.Contains(c.OrderId))
            .GroupBy(c => c.OrderId)
            .Select(g => new { Id = g.Key, Total = g.Count(), Done = g.Count(x => x.IsDone) })
            .ToDictionaryAsync(x => x.Id);
        return ids.ToDictionary(id => id, id =>
        {
            var mine = orders.Where(o => o.Pid == id).ToList();
            var progresses = mine.Select(o =>
            {
                cl.TryGetValue(o.Id, out var s);
                return (o.Weight, WorkProjectProgress.OrderProgress(o.Status == "Closed", s?.Total ?? 0, s?.Done ?? 0));
            }).ToList();
            return new ProjStats(
                mine.Count,
                mine.Count(o => o.Status == "Open"),
                mine.Count(o => o.Status == "Closed"),
                mine.Count(o => o.Status == "Open" && o.DueAt < now),
                WorkProjectProgress.ProjectProgress(progresses));
        });
    }

    private static WorkProjectListItemDto ToListItem(
        WorkProject p, ProjStats s, List<WorkProjectMember> members,
        HashSet<int> myMemberProjectIds, WorkProjectLog? lastLog, int myUserId, DateTime today)
    {
        var mine = members.Where(m => m.ProjectId == p.Id).OrderBy(m => m.Name).ToList();
        var isOverdue = p.Status == WorkProjectStatus.Open && p.DueAt.Date < today;
        return new WorkProjectListItemDto
        {
            Id = p.Id, Number = p.Number, Title = p.Title,
            OwnerUserId = p.OwnerUserId, OwnerName = p.OwnerName,
            StartAt = p.StartAt, DueAt = p.DueAt, Status = p.Status,
            Priority = p.Priority, ExtensionCount = p.ExtensionCount, CreatedAt = p.CreatedAt,
            Progress = s.Progress,
            OrderCount = s.OrderCount, OpenOrderCount = s.Open,
            ClosedOrderCount = s.Closed, OverdueOrderCount = s.Overdue,
            MemberCount = mine.Count,
            MemberNames = mine.Take(4).Select(m => m.Name).ToList(),
            DaysLeft = (p.DueAt.Date - today).Days,
            IsOverdue = isOverdue,
            IsMine = p.OwnerUserId == myUserId,
            IsMember = myMemberProjectIds.Contains(p.Id),
            LastActivityAt = lastLog?.CreatedAt,
            LastActivityText = lastLog is null ? null : $"{WorkProjectLogDto.ToFa(lastLog.Action)} — {lastLog.ActorName}"
        };
    }

    /// <summary>لیست پروژه‌های شرکت فعال + پیشرفت/شمارش/آخرین فعالیت هر سطر.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q, [FromQuery] string? status, [FromQuery] string? mine,
        [FromQuery] bool? overdue, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (!await HasAsync("View")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });

        var today = DateTime.Today;
        var query = _db.WorkProjects.AsNoTracking().Where(p => p.CompanyId == companyId.Value);
        if (status == "open") query = query.Where(p => p.Status == WorkProjectStatus.Open);
        else if (status == "closed") query = query.Where(p => p.Status == WorkProjectStatus.Closed);
        if (mine == "owner") query = query.Where(p => p.OwnerUserId == MyUserId);
        else if (mine == "member")
        {
            var myProjects = _db.WorkProjectMembers.Where(m => m.UserId == MyUserId).Select(m => m.ProjectId);
            query = query.Where(p => myProjects.Contains(p.Id));
        }
        if (overdue == true) query = query.Where(p => p.Status == WorkProjectStatus.Open && p.DueAt < today);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => EF.Functions.Like(p.Title, $"%{term}%")
                || EF.Functions.Like(p.Number, $"%{term}%")
                || EF.Functions.Like(p.OwnerName, $"%{term}%")
                || (p.Tags != null && EF.Functions.Like(p.Tags, $"%{term}%")));
        }

        var projects = await query.ToListAsync();
        var ids = projects.Select(p => p.Id).ToList();
        var now = DateTime.Now;
        var stats = await StatsForAsync(ids, now);
        var members = ids.Count == 0 ? new() : await _db.WorkProjectMembers.AsNoTracking()
            .Where(m => ids.Contains(m.ProjectId)).ToListAsync();
        var myMemberIds = members.Where(m => m.UserId == MyUserId).Select(m => m.ProjectId).ToHashSet();
        var lastLogs = ids.Count == 0 ? new() : await _db.WorkProjectLogs.AsNoTracking()
            .Where(l => ids.Contains(l.ProjectId))
            .GroupBy(l => l.ProjectId)
            .Select(g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefault())
            .ToListAsync();
        var lastByProject = lastLogs.Where(l => l != null).ToDictionary(l => l!.ProjectId);

        var items = projects.Select(p => ToListItem(p, stats[p.Id], members, myMemberIds,
            lastByProject.TryGetValue(p.Id, out var l) ? l : null, MyUserId, today)).ToList();

        items = (sort ?? "due") switch
        {
            "newest" => items.OrderByDescending(i => i.Id).ToList(),
            "progressAsc" => items.OrderBy(i => i.Progress is null).ThenBy(i => i.Progress).ToList(),
            "progressDesc" => items.OrderBy(i => i.Progress is null).ThenByDescending(i => i.Progress).ToList(),
            _ => items.OrderBy(i => i.Status == WorkProjectStatus.Closed).ThenBy(i => i.DueAt).ThenByDescending(i => i.Id).ToList()
        };

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        return Ok(new WorkProjectPageDto
        {
            Items = items.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            TotalCount = items.Count, Page = page, PageSize = pageSize
        });
    }

    /// <summary>جزئیات کامل یک پروژه (مشخصات + اعضا + آمار دستورها).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        if (!await HasAsync("View")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var p = await _db.WorkProjects.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });

        var ids = new List<int> { p.Id };
        var stats = await StatsForAsync(ids, DateTime.Now);
        var members = await _db.WorkProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == p.Id).OrderBy(m => m.Name).ToListAsync();
        var lastLog = await _db.WorkProjectLogs.AsNoTracking()
            .Where(l => l.ProjectId == p.Id)
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id).FirstOrDefaultAsync();
        var myMemberIds = members.Any(m => m.UserId == MyUserId)
            ? new HashSet<int> { p.Id } : new HashSet<int>();

        var item = ToListItem(p, stats[p.Id], members, myMemberIds, lastLog, MyUserId, DateTime.Today);
        return Ok(new WorkProjectDetailDto
        {
            Id = item.Id, Number = item.Number, Title = item.Title,
            OwnerUserId = item.OwnerUserId, OwnerName = item.OwnerName,
            StartAt = item.StartAt, DueAt = item.DueAt, Status = item.Status,
            Priority = item.Priority, ExtensionCount = item.ExtensionCount, CreatedAt = item.CreatedAt,
            Progress = item.Progress, OrderCount = item.OrderCount, OpenOrderCount = item.OpenOrderCount,
            ClosedOrderCount = item.ClosedOrderCount, OverdueOrderCount = item.OverdueOrderCount,
            MemberCount = item.MemberCount, MemberNames = item.MemberNames,
            DaysLeft = item.DaysLeft, IsOverdue = item.IsOverdue,
            IsMine = item.IsMine, IsMember = item.IsMember,
            LastActivityAt = item.LastActivityAt, LastActivityText = item.LastActivityText,
            Description = p.Description, Tags = TagsToList(p.Tags),
            CloseNote = p.CloseNote, ClosedAt = p.ClosedAt,
            Members = members.Select(m => new WorkProjectMemberDto
                { Id = m.Id, UserId = m.UserId, Name = m.Name, AddedAt = m.AddedAt }).ToList(),
            CanManage = await CanManageAsync(p)
        });
    }

    // ================== ساخت/ویرایش ==================

    private async Task<IActionResult?> ValidateMembersAsync(List<int> userIds)
    {
        var distinct = userIds.Distinct().ToList();
        if (distinct.Count == 0) return null;
        var users = await _db.Users.Where(u => distinct.Contains(u.Id) && u.IsActive).ToListAsync();
        if (users.Count != distinct.Count)
            return BadRequest(new { message = "برخی اعضا وجود ندارند یا غیرفعال‌اند؛ فقط کاربران فعال را انتخاب کنید." });
        var noName = users.Where(u => string.IsNullOrWhiteSpace(u.FirstName) || string.IsNullOrWhiteSpace(u.LastName))
            .Select(u => u.Username).ToList();
        if (noName.Count > 0)
            return BadRequest(new { message = $"این کاربران نام و نام خانوادگی ندارند: {string.Join("، ", noName)} — از بخش کاربران تکمیل کنید." });
        return null;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveWorkProjectDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var title = (dto.Title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { message = "عنوان پروژه را وارد کنید." });
        if (title.Length > 200)
            return BadRequest(new { message = "عنوان پروژه حداکثر ۲۰۰ حرف است." });
        if (!WorkOrderPriority.IsValid(dto.Priority))
            return BadRequest(new { message = "اولویت انتخابی نامعتبر است." });
        var start = dto.StartAt.Date;
        var due = dto.DueAt.Date;
        if (due < start)
            return BadRequest(new { message = "مهلت پایان نمی‌تواند قبل از تاریخ شروع باشد." });
        if (due < DateTime.Today)
            return BadRequest(new { message = "مهلت پایان نمی‌تواند قبل از امروز باشد." });
        var memberIds = dto.MemberIds.Distinct().ToList();
        if (await ValidateMembersAsync(memberIds) is { } bad) return bad;

        var p = new WorkProject
        {
            CompanyId = companyId.Value, Title = title,
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            OwnerUserId = MyUserId, OwnerName = await MyDisplayNameAsync(),
            StartAt = start, DueAt = due, Priority = dto.Priority,
            Tags = NormalizeTags(dto.Tags)
        };
        _db.WorkProjects.Add(p);
        await _db.SaveChangesAsync();
        p.Number = $"PRJ/{new PersianCalendar().GetYear(p.CreatedAt)}/{p.Id}";

        var users = await _db.Users.Where(u => memberIds.Contains(u.Id)).ToListAsync();
        foreach (var u in users)
            _db.WorkProjectMembers.Add(new WorkProjectMember
                { ProjectId = p.Id, UserId = u.Id, Name = UserDisplay.Name(u) });
        Log(p.Id, "Created", $"پروژه {p.Number} «{p.Title}» — مهلت: {ToFa(p.DueAt)} — اعضا: {users.Count} نفر");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        foreach (var u in users.Where(u => u.Id != MyUserId))
            await _notify.SendAsync(u.Id, "به پروژه کاری اضافه شدید 🏗",
                $"{p.Number} — «{p.Title}» — مهلت: {ToFa(p.DueAt)}",
                p.OwnerName, "پروژه کاری", $"/work-projects?open={p.Id}");
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok(new { id = p.Id, number = p.Number });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveWorkProjectDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var p = await GetProjectAsync(id, companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageAsync(p)) return Forbid();
        if (p.Status == WorkProjectStatus.Closed)
            return BadRequest(new { message = "پروژه بسته شده و قابل ویرایش نیست؛ اول آن را بازگشایی کنید." });

        var title = (dto.Title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { message = "عنوان پروژه را وارد کنید." });
        if (title.Length > 200)
            return BadRequest(new { message = "عنوان پروژه حداکثر ۲۰۰ حرف است." });
        if (!WorkOrderPriority.IsValid(dto.Priority))
            return BadRequest(new { message = "اولویت انتخابی نامعتبر است." });
        var start = dto.StartAt.Date;
        var due = dto.DueAt.Date;
        if (due < start)
            return BadRequest(new { message = "مهلت پایان نمی‌تواند قبل از تاریخ شروع باشد." });

        p.Title = title;
        p.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        p.StartAt = start;
        p.DueAt = due;
        p.Priority = dto.Priority;
        p.Tags = NormalizeTags(dto.Tags);
        p.OwnerName = await MyDisplayNameAsync();

        Log(p.Id, "Edited", $"ویرایش مشخصات پروژه — مهلت: {ToFa(p.DueAt)}");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok(new { id = p.Id });
    }

    // ================== تمدید مهلت ==================

    [HttpPost("{id:int}/extend")]
    public async Task<IActionResult> Extend(int id, [FromBody] ExtendWorkProjectDto dto)
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var p = await GetProjectAsync(id, companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageAsync(p)) return Forbid();
        if (p.Status == WorkProjectStatus.Closed)
            return BadRequest(new { message = "پروژه بسته شده است." });
        if (p.ExtensionCount >= MaxExtension)
            return BadRequest(new { message = $"حداکثر {MaxExtension} بار امکان تمدید وجود دارد." });
        if (string.IsNullOrWhiteSpace(dto.Note))
            return BadRequest(new { message = "دلیل تمدید را وارد کنید." });
        var newDue = dto.NewDueAt.Date;
        if (newDue <= DateTime.Today)
            return BadRequest(new { message = "مهلت جدید باید بعد از امروز باشد." });
        if (newDue <= p.DueAt.Date)
            return BadRequest(new { message = "مهلت جدید باید بعد از مهلت فعلی باشد." });

        var old = p.DueAt;
        p.DueAt = newDue;
        p.ExtensionCount++;

        Log(p.Id, "Extended", $"تمدید {p.ExtensionCount} از {MaxExtension} — از {ToFa(old)} به {ToFa(newDue)} — {dto.Note!.Trim()}");
        await _db.SaveChangesAsync();

        var members = await _db.WorkProjectMembers.Where(m => m.ProjectId == id).ToListAsync();
        foreach (var m in members.Where(m => m.UserId != MyUserId))
            await _notify.SendAsync(m.UserId, "تمدید مهلت پروژه ⏳",
                $"{p.Number} — مهلت جدید: {ToFa(newDue)}", MyUsername, "پروژه کاری", $"/work-projects?open={id}");
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok(new { extensionCount = p.ExtensionCount });
    }

    // ================== بستن / بازگشایی / حذف ==================

    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> Close(int id, [FromBody] CloseWorkProjectDto dto)
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var p = await GetProjectAsync(id, companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageAsync(p)) return Forbid();
        if (p.Status == WorkProjectStatus.Closed)
            return BadRequest(new { message = "قبلاً بسته شده است." });

        var openOrders = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.ProjectId == id && w.Status == "Open")
            .Select(w => w.Number).ToListAsync();
        if (openOrders.Count > 0)
        {
            var shown = string.Join("، ", openOrders.Take(5));
            var more = openOrders.Count > 5 ? $" و {openOrders.Count - 5} مورد دیگر" : "";
            return BadRequest(new { message = $"اول دستورهای باز پروژه بسته یا جدا شوند: {shown}{more}" });
        }

        p.Status = WorkProjectStatus.Closed;
        p.ClosedAt = DateTime.Now;
        p.CloseNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();

        Log(p.Id, "Closed", $"پروژه توسط {MyUsername} نهایی و بسته شد{(p.CloseNote is null ? "" : " — " + p.CloseNote)}");
        await _db.SaveChangesAsync();

        var members = await _db.WorkProjectMembers.Where(m => m.ProjectId == id).ToListAsync();
        foreach (var m in members.Where(m => m.UserId != MyUserId))
            await _notify.SendAsync(m.UserId, "پروژه کاری بسته شد 🔒",
                $"{p.Number} — «{p.Title}»", MyUsername, "پروژه کاری", $"/work-projects?open={id}");
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok();
    }

    [HttpPost("{id:int}/reopen")]
    public async Task<IActionResult> Reopen(int id)
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var p = await GetProjectAsync(id, companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageAsync(p)) return Forbid();
        if (p.Status == WorkProjectStatus.Open)
            return BadRequest(new { message = "پروژه باز است." });

        p.Status = WorkProjectStatus.Open;
        p.ClosedAt = null;
        Log(p.Id, "Reopened", $"پروژه توسط {MyUsername} بازگشایی شد.");
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await HasAsync("Delete")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var p = await GetProjectAsync(id, companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });

        var openCount = await _db.WorkOrders.CountAsync(w => w.ProjectId == id && w.Status == "Open");
        if (openCount > 0)
            return BadRequest(new { message = $"این پروژه {openCount} دستور باز دارد؛ اول آن‌ها را ببندید یا از پروژه جدا کنید." });

        p.DeletedAt = DateTime.Now;
        p.DeletedByUserId = MyUserId;
        Log(p.Id, "Deleted", $"حذف پروژه {p.Number} توسط {MyUsername}؛ سوابق دستورها محفوظ است.");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _notify.BroadcastChangedAsync("workprojects");
        await _notify.BroadcastChangedAsync("workorders");
        return NoContent();
    }

    // ================== اعضا ==================

    [HttpPost("{id:int}/members")]
    public async Task<IActionResult> AddMembers(int id, [FromBody] AddWorkProjectMembersDto dto)
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var p = await GetProjectAsync(id, companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageAsync(p)) return Forbid();
        if (p.Status == WorkProjectStatus.Closed)
            return BadRequest(new { message = "پروژه بسته شده است." });

        var userIds = dto.UserIds.Distinct().Where(u => u > 0).ToList();
        if (userIds.Count == 0)
            return BadRequest(new { message = "حداقل یک نفر را انتخاب کنید." });
        if (await ValidateMembersAsync(userIds) is { } bad) return bad;

        var existing = await _db.WorkProjectMembers.Where(m => m.ProjectId == id)
            .Select(m => m.UserId).ToListAsync();
        var fresh = userIds.Where(u => !existing.Contains(u)).ToList();
        if (fresh.Count == 0)
            return BadRequest(new { message = "این افراد قبلاً عضو پروژه هستند." });

        var users = await _db.Users.Where(u => fresh.Contains(u.Id)).ToListAsync();
        foreach (var u in users)
            _db.WorkProjectMembers.Add(new WorkProjectMember
                { ProjectId = p.Id, UserId = u.Id, Name = UserDisplay.Name(u) });
        Log(p.Id, "MemberAdded", $"افزودن عضو: {string.Join("، ", users.Select(UserDisplay.Name))}");
        await _db.SaveChangesAsync();

        foreach (var u in users.Where(u => u.Id != MyUserId))
            await _notify.SendAsync(u.Id, "به پروژه کاری اضافه شدید 🏗",
                $"{p.Number} — «{p.Title}»", MyUsername, "پروژه کاری", $"/work-projects?open={id}");
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok(new { added = users.Count });
    }

    [HttpDelete("{id:int}/members/{userId:int}")]
    public async Task<IActionResult> RemoveMember(int id, int userId)
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var p = await GetProjectAsync(id, companyId.Value);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (!await CanManageAsync(p)) return Forbid();
        if (p.Status == WorkProjectStatus.Closed)
            return BadRequest(new { message = "پروژه بسته شده است." });

        var m = await _db.WorkProjectMembers.FirstOrDefaultAsync(x => x.ProjectId == id && x.UserId == userId);
        if (m is null) return NotFound(new { message = "این کاربر عضو پروژه نیست." });

        _db.WorkProjectMembers.Remove(m);
        Log(p.Id, "MemberRemoved", $"حذف عضو: {m.Name}");
        await _db.SaveChangesAsync();

        if (userId != MyUserId)
            await _notify.SendAsync(userId, "از پروژه کاری حذف شدید",
                $"{p.Number} — «{p.Title}»", MyUsername, "پروژه کاری", $"/work-projects?open={id}");
        await _notify.BroadcastChangedAsync("workprojects");
        return Ok();
    }

    // ================== تاریخچه فعالیت ==================

    [HttpGet("{id:int}/activity")]
    public async Task<IActionResult> Activity(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await HasAsync("View")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var exists = await _db.WorkProjects.AsNoTracking()
            .AnyAsync(p => p.Id == id && p.CompanyId == companyId.Value);
        if (!exists) return NotFound(new { message = "پروژه پیدا نشد." });

        var n = Math.Clamp(take is > 0 ? take.Value : 20, 1, 100);
        var logs = await _db.WorkProjectLogs.AsNoTracking()
            .Where(l => l.ProjectId == id)
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Skip(Math.Max(0, skip)).Take(n)
            .Select(l => new WorkProjectLogDto
            {
                Id = l.Id, ActorName = l.ActorName, Action = l.Action,
                Text = l.Text, CreatedAt = l.CreatedAt
            }).ToListAsync();
        foreach (var l in logs) l.ActionFa = WorkProjectLogDto.ToFa(l.Action);
        return Ok(logs);
    }
}
