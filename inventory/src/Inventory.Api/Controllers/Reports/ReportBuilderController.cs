using System.Security.Claims;
using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Services.Core;
using Inventory.Api.Services.Reports;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

// =====================================================================
//  گزارش‌ساز شخصی
//
//  • GET    api/reports/datasets        → دیتاست‌های مجاز برای همین کاربر
//  • GET    api/reports/roles           → نقش‌های فعال (برای اشتراک‌گذاری)
//  • POST   api/reports/preview         → اجرای کوئری بدون ذخیره
//  • GET    api/reports                 → گزارش‌های من + اشتراک‌شده با نقش‌هایم
//  • POST   api/reports                 → ذخیرهٔ گزارش جدید
//  • PUT    api/reports/{id}            → ویرایش (فقط مالک)
//  • DELETE api/reports/{id}            → حذف (فقط مالک)
//  • GET    api/reports/{id}/data       → اجرای گزارش ذخیره‌شده (ویجت داشبورد)
//
//  امنیت:
//   ۱) ماژول ReportBuilder (View/Design) برای ورود به این API.
//   ۲) ماژول دادهٔ هر دیتاست، هم در فهرست و هم هنگام اجرا کنترل می‌شود؛
//      پس دستکاری QueryJson در کلاینت باعث نشت داده نمی‌شود.
//   ۳) گزارش دیگران فقط در صورت اشتراک با یکی از نقش‌های کاربر قابل
//      مشاهده است؛ در غیر این صورت ۴۰۴ برمی‌گردد (بدون افشای وجود).
// =====================================================================
[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportBuilderController : ControllerBase
{
    private const string Module = "ReportBuilder";

    private static readonly JsonSerializerOptions JsonOpts = new()
    { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AppDbContext _db;
    private readonly IEffectivePermissions _perms;
    private readonly IReportDatasetProvider _provider;

    public ReportBuilderController(AppDbContext db, IEffectivePermissions perms, IReportDatasetProvider provider)
    {
        _db = db;
        _perms = perms;
        _provider = provider;
    }

    private int MyUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    /// <summary>
    /// خودتعمیری: اگر جدول‌های گزارش‌ساز در زمان راه‌اندازی ساخته نشده باشند (مثلاً کاربرِ
    /// دیتابیس مجوز CREATE TABLE نداشته)، یک بار دیگر در زمان درخواست تلاش می‌شود.
    /// در صورت شکست، پیام خطای واقعی به‌جای خطای مبهم «UserReport» به کاربر برمی‌گردد.
    /// </summary>
    private async Task<IActionResult?> EnsureDbAsync()
    {
        await ReportBuilderSchemaV1.EnsureOnceAsync(_db);
        if (await ReportBuilderSchemaV1.TablesExistAsync(_db)) return null;

        var why = ReportBuilderSchemaV1.LastError is { Length: > 0 } e
            ? " خطای دیتابیس: " + e
            : "";
        return StatusCode(500, new
        {
            message = "جدول‌های گزارش‌ساز (UserReports / UserReportRoleShares) در دیتابیس ساخته نشده‌اند." + why +
                      " — راه‌حل: اسکریپت inventory/sql/UserDashboards-UserReports.sql را در SSMS اجرا کنید " +
                      "یا مطمئن شوید کاربرِ رشتهٔ اتصال، مجوز CREATE TABLE دارد؛ سپس API را ری‌استارت کنید."
        });
    }

    private async Task<IActionResult?> ForbiddenUnlessAsync(string action)
    {
        var perms = await _perms.GetAsync(User);
        if (perms.Contains($"{Module}.{action}")) return null;
        if (await _perms.IsLegacyAdminAsync(User)) return null;
        return StatusCode(403, new { message = "شما به گزارش‌ساز دسترسی ندارید." });
    }

    // ================== متادیتا ==================

    /// <summary>دیتاست‌هایی که کاربر مجوز مشاهدهٔ ماژولشان را دارد.</summary>
    [HttpGet("datasets")]
    public async Task<IActionResult> Datasets()
    {
        if (await ForbiddenUnlessAsync("View") is ObjectResult fb) return fb;

        var perms = await _perms.GetAsync(User);
        var isAdmin = await _perms.IsLegacyAdminAsync(User);

        var list = new List<ReportDatasetDto>();
        foreach (var ds in _provider.Datasets)
        {
            if (!isAdmin && !HasModule(perms, ds.Module)) continue;
            list.Add(ds.ToDto());
        }

        return Ok(new
        {
            canDesign = isAdmin || perms.Contains($"{Module}.Design"),
            datasets = list.OrderBy(d => d.Category).ThenBy(d => d.Title).ToList()
        });
    }

    /// <summary>نقش‌های فعال — برای اشتراک‌گذاری گزارش.</summary>
    [HttpGet("roles")]
    public async Task<IActionResult> Roles()
    {
        if (await ForbiddenUnlessAsync("Design") is ObjectResult fb) return fb;
        var roles = await _db.Roles.Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .Select(r => new ReportRoleDto { Id = r.Id, Name = r.Name })
            .ToListAsync();
        return Ok(roles);
    }

    // ================== اجرا (پیش‌نمایش) ==================

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] ReportQueryDto q)
    {
        if (await ForbiddenUnlessAsync("View") is ObjectResult fb) return fb;
        if (q is null || string.IsNullOrWhiteSpace(q.DatasetKey))
            return BadRequest(new { message = "دیتاست مشخص نشده است." });

        var ds = _provider.Find(q.DatasetKey);
        if (ds is null) return NotFound(new { message = "دیتاست پیدا نشد." });

        if (!await _perms.CanSeeModuleAsync(User, ds.Module))
            return StatusCode(403, new { message = $"به دادهٔ «{ds.Title}» دسترسی ندارید." });

        var res = await ds.RunAsync(_db, Sanitize(q));
        res.Data.Key = "preview:" + ds.Key;
        res.Data.Title = string.IsNullOrWhiteSpace(q.Title) ? ds.Title : q.Title!;
        return Ok(res);
    }

    // ================== گزارش‌های ذخیره‌شده ==================

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (await ForbiddenUnlessAsync("View") is ObjectResult fb) return fb;
        if (await EnsureDbAsync() is ObjectResult dbErr) return dbErr;

        var roleIds = await _perms.MyRoleIdsAsync(MyUserId);
        var rows = await _db.UserReports
            .Include(r => r.RoleShares)
            .Where(r => r.UserId == MyUserId
                        || (r.Visibility == 1 && r.RoleShares.Any(s => roleIds.Contains(s.RoleId))))
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync();

        var ownerIds = rows.Select(r => r.UserId).Distinct().ToList();
        var owners = await _db.Users.Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.FirstName ?? "") + " " + (u.LastName ?? ""));
        var roleMap = await _db.Roles.ToDictionaryAsync(r => r.Id, r => r.Name);
        var perms = await _perms.GetAsync(User);
        var isAdmin = await _perms.IsLegacyAdminAsync(User);

        var list = new List<UserReportDto>();
        foreach (var r in rows)
        {
            var ds = _provider.Find(r.DatasetKey);
            // اگر مجوز ماژول داده از دست رفته باشد، گزارش فهرست می‌شود ولی قابل اجرا نیست
            var allowed = isAdmin || HasModule(perms, ds?.Module ?? r.Module);
            list.Add(new UserReportDto
            {
                Id = r.Id,
                Name = r.Name,
                DatasetKey = r.DatasetKey,
                DatasetTitle = ds?.Title ?? r.DatasetKey,
                Module = ds?.Module ?? r.Module,
                Visibility = (ReportVisibility)r.Visibility,
                SharedRoleIds = r.RoleShares.Select(s => s.RoleId).ToList(),
                SharedRoleNames = r.RoleShares.Select(s => roleMap.GetValueOrDefault(s.RoleId, "?")).ToList(),
                IsMine = r.UserId == MyUserId,
                OwnerName = r.UserId == MyUserId ? "" : owners.GetValueOrDefault(r.UserId, "").Trim(),
                Query = Deserialize(r.QueryJson),
                UpdatedAt = r.UpdatedAt
            });
            if (!allowed) list[^1].Name += " (بدون دسترسی به داده)";
        }
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ReportSaveDto dto)
    {
        if (await ForbiddenUnlessAsync("Design") is ObjectResult fb) return fb;
        if (await EnsureDbAsync() is ObjectResult dbErr) return dbErr;
        if (dto is null) return BadRequest(new { message = "بدنهٔ درخواست خالی است." });
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "نام گزارش را وارد کنید." });

        var ds = _provider.Find(dto.DatasetKey);
        if (ds is null) return NotFound(new { message = "دیتاست پیدا نشد." });
        if (!await _perms.CanSeeModuleAsync(User, ds.Module))
            return StatusCode(403, new { message = $"به دادهٔ «{ds.Title}» دسترسی ندارید." });

        var query = Sanitize(dto.Query ?? new ReportQueryDto());
        query.DatasetKey = ds.Key;

        var report = new UserReport
        {
            UserId = MyUserId,
            Name = Trunc(dto.Name.Trim(), 140),
            DatasetKey = ds.Key,
            Module = ds.Module,
            Visibility = dto.Visibility == ReportVisibility.RoleShared ? 1 : 0,
            QueryJson = JsonSerializer.Serialize(query, JsonOpts),
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        _db.UserReports.Add(report);
        await _db.SaveChangesAsync();

        if (report.Visibility == 1)
            await ReplaceSharesAsync(report.Id, dto.RoleIds ?? new List<int>());

        return Ok(new { id = report.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ReportSaveDto dto)
    {
        if (await ForbiddenUnlessAsync("Design") is ObjectResult fb) return fb;
        if (await EnsureDbAsync() is ObjectResult dbErr) return dbErr;
        var report = await _db.UserReports.FirstOrDefaultAsync(r => r.Id == id && r.UserId == MyUserId);
        if (report is null) return NotFound(new { message = "گزارش پیدا نشد." });
        if (dto is null) return BadRequest(new { message = "بدنهٔ درخواست خالی است." });

        var ds = _provider.Find(dto.DatasetKey ?? report.DatasetKey);
        if (ds is null) return NotFound(new { message = "دیتاست پیدا نشد." });
        if (!await _perms.CanSeeModuleAsync(User, ds.Module))
            return StatusCode(403, new { message = $"به دادهٔ «{ds.Title}» دسترسی ندارید." });

        var query = Sanitize(dto.Query ?? Deserialize(report.QueryJson));
        query.DatasetKey = ds.Key;

        if (!string.IsNullOrWhiteSpace(dto.Name)) report.Name = Trunc(dto.Name.Trim(), 140);
        report.DatasetKey = ds.Key;
        report.Module = ds.Module;
        report.Visibility = dto.Visibility == ReportVisibility.RoleShared ? 1 : 0;
        report.QueryJson = JsonSerializer.Serialize(query, JsonOpts);
        report.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        if (report.Visibility == 1) await ReplaceSharesAsync(report.Id, dto.RoleIds ?? new List<int>());
        else
        {
            var shares = await _db.UserReportRoleShares.Where(s => s.ReportId == report.Id).ToListAsync();
            _db.UserReportRoleShares.RemoveRange(shares);
            await _db.SaveChangesAsync();
        }
        return Ok(new { id = report.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("Design") is ObjectResult fb) return fb;
        if (await EnsureDbAsync() is ObjectResult dbErr) return dbErr;
        var report = await _db.UserReports.FirstOrDefaultAsync(r => r.Id == id && r.UserId == MyUserId);
        if (report is null) return NotFound(new { message = "گزارش پیدا نشد." });

        var shares = await _db.UserReportRoleShares.Where(s => s.ReportId == id).ToListAsync();
        _db.UserReportRoleShares.RemoveRange(shares);
        _db.UserReports.Remove(report);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    /// <summary>اجرای گزارش ذخیره‌شده — ویجت «rep:{id}» در داشبورد همین را صدا می‌زند.</summary>
    [HttpGet("{id:int}/data")]
    public async Task<IActionResult> Run(int id)
    {
        if (await ForbiddenUnlessAsync("View") is ObjectResult fb) return fb;
        if (await EnsureDbAsync() is ObjectResult dbErr) return dbErr;

        var report = await _db.UserReports.Include(r => r.RoleShares).FirstOrDefaultAsync(r => r.Id == id);
        if (report is null || !await CanAccessAsync(report))
            return NotFound(new { message = "گزارش پیدا نشد." });

        var ds = _provider.Find(report.DatasetKey);
        if (ds is null)
            return Ok(new ReportRunResultDto
            { Data = new WidgetDataDto { Key = "rep:" + id, Title = report.Name, Error = "دیتاست این گزارش دیگر در دسترس نیست." } });

        if (!await _perms.CanSeeModuleAsync(User, ds.Module))
            return StatusCode(403, new { message = $"به دادهٔ «{ds.Title}» دسترسی ندارید." });

        var q = Sanitize(Deserialize(report.QueryJson));
        if (string.IsNullOrWhiteSpace(q.Title)) q.Title = report.Name;
        var res = await ds.RunAsync(_db, q);
        res.Data.Key = "rep:" + id;
        if (string.IsNullOrWhiteSpace(res.Data.Title)) res.Data.Title = report.Name;
        return Ok(res);
    }

    // ================== کمکی‌ها ==================

    private async Task<bool> CanAccessAsync(UserReport report)
    {
        if (report.UserId == MyUserId) return true;
        if (report.Visibility != 1) return false;
        var roleIds = await _perms.MyRoleIdsAsync(MyUserId);
        return report.RoleShares.Any(s => roleIds.Contains(s.RoleId));
    }

    private async Task ReplaceSharesAsync(int reportId, List<int> roleIds)
    {
        var valid = await _db.Roles.Where(r => r.IsActive && roleIds.Contains(r.Id)).Select(r => r.Id).ToListAsync();
        var old = await _db.UserReportRoleShares.Where(s => s.ReportId == reportId).ToListAsync();
        _db.UserReportRoleShares.RemoveRange(old);
        foreach (var rid in valid.Distinct())
            _db.UserReportRoleShares.Add(new UserReportRoleShare { ReportId = reportId, RoleId = rid, CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync();
    }

    private static bool HasModule(HashSet<string> perms, string module) =>
        string.IsNullOrEmpty(module)
        || perms.Contains($"{module}.Read") || perms.Contains($"{module}.View") || perms.Contains($"{module}.Access");

    /// <summary>محدودسازی ورودی کاربر تا کوئری سنگین/نامعقول نسازد.</summary>
    private static ReportQueryDto Sanitize(ReportQueryDto q)
    {
        q.Take = Math.Clamp(q.Take <= 0 ? 50 : q.Take, 1, 500);
        q.Columns = (q.Columns ?? new List<string>()).Distinct().Take(12).ToList();
        q.Measures = (q.Measures ?? new List<ReportMeasureDto>()).Take(6).ToList();
        q.Filters = (q.Filters ?? new List<ReportFilterDto>()).Take(20).ToList();
        q.Sorts = (q.Sorts ?? new List<ReportSortDto>()).Take(4).ToList();
        q.Title = q.Title is null ? null : Trunc(q.Title, 160);
        return q;
    }

    private static ReportQueryDto Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<ReportQueryDto>(json, JsonOpts) ?? new ReportQueryDto(); }
        catch { return new ReportQueryDto(); }
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}
