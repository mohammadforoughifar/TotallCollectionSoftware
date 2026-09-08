using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>درخواست خدمت از واحد آی‌تی — ثبت، ارجاع، گزارش کارشناس، تایید/رد مدیر و تکمیل.
/// درخواست‌دهنده فرایند داخلی واحد IT را نمی‌بیند (فقط پاسخ نهایی مدیر).</summary>
[ApiController]
[Route("api/itrequests")]
[Authorize]
public class ItRequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly FileStore _store;
    public ItRequestsController(AppDbContext db, INotifyService notify, FileStore store) { _db = db; _notify = notify; _store = store; }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyUsername => User.FindFirstValue(ClaimTypes.Name) ?? "";

    /// <summary>بررسی دسترسی RBAC کاربر جاری — با سازگاری عقب‌رو برای کاربران بدون نقش RBAC.</summary>
    private async Task<bool> HasAsync(string action)
    {
        var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId);
        if (!hasRoles)
        {
            var legacy = User.FindFirstValue(ClaimTypes.Role);
            if (legacy == "Admin") return true;
            if (legacy == "Operator") return action is "Create" or "ViewDepartment";
            return false;
        }

        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId)
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == "ItRequests" && p.Action == action);
    }

    /// <summary>کاربران دارای مجوز مدیر IT (برای نوتیفیکیشن).</summary>
    private async Task<List<int>> ManagerUserIdsAsync()
    {
        var rbac = await _db.UserRoles
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur.UserId, rp.PermissionId })
            .Join(_db.Permissions, x => x.PermissionId, p => p.Id, (x, p) => new { x.UserId, p.Module, p.Action })
            .Where(x => x.Module == "ItRequests" && x.Action == "Manage")
            .Select(x => x.UserId).Distinct().ToListAsync();
        // ادمین‌های قدیمی بدون نقش RBAC
        var legacyAdmins = await _db.Users
            .Where(u => u.Role == "Admin" && u.IsActive && !_db.UserRoles.Any(ur => ur.UserId == u.Id))
            .Select(u => u.Id).ToListAsync();
        return rbac.Concat(legacyAdmins).Distinct().ToList();
    }

    private void Log(int reqId, string role, string action, string? text, bool internalOnly = true) =>
        _db.ItRequestLogs.Add(new ItRequestLog
        {
            RequestId = reqId, ActorName = MyUsername, ActorRole = role,
            Action = action, Text = text, InternalOnly = internalOnly
        });

    private Task<SystemUser?> MySystemUserAsync() =>
        _db.SystemUsers.FirstOrDefaultAsync(su => su.Username == MyUsername);

    // ================== دسترسی‌های من ==================
    [HttpGet("my-access")]
    public async Task<IActionResult> MyAccess() => Ok(new
    {
        userId = MyUserId,
        canCreate = await HasAsync("Create"),
        isExpert = await HasAsync("Expert"),
        isManager = await HasAsync("Manage")
    });

    // ================== سیستم‌های قابل انتخاب (با IP) ==================
    [HttpGet("systems")]
    public async Task<IActionResult> Systems()
    {
        var viewCompany = await HasAsync("ViewCompany");
        var viewDepartment = await HasAsync("ViewDepartment");
        var me = await MySystemUserAsync();

        var q = _db.SystemInfos.AsNoTracking().Where(s => s.IsApproved);

        if (viewCompany)
        {
            if (me?.CompanyId is > 0) q = q.Where(s => s.CompanyId == me.CompanyId);
        }
        else if (viewDepartment)
        {
            if (me?.DepartmentId is > 0) q = q.Where(s => s.DepartmentId == me.DepartmentId);
            else q = q.Where(s => false);
        }
        else
        {
            var myId = me?.Id ?? -1;
            q = q.Where(s => s.UserId == myId);
        }

        var systems = await q
            .Select(s => new
            {
                s.Id,
                Label = s.AgentId ?? "",
                // آی‌پی به جای مشخصات ویندوز
                Ip = _db.SystemNetAdapters.Where(n => n.SystemInfoId == s.Id && n.Ipv4 != "")
                    .Select(n => n.Ipv4).FirstOrDefault() ?? "",
                UserName = _db.SystemUsers.Where(u => u.Id == s.UserId)
                    .Select(u => (u.FirstName + " " + u.LastName).Trim()).FirstOrDefault(),
                DepartmentName = _db.SystemDepartments.Where(d => d.Id == s.DepartmentId)
                    .Select(d => d.Name).FirstOrDefault()
            })
            .ToListAsync();

        return Ok(systems);
    }

    // ================== کارشناسان (برای ارجاع) ==================
    [HttpGet("experts")]
    public async Task<IActionResult> Experts()
    {
        if (!await HasAsync("Manage")) return Forbid();

        var expertUserIds = await _db.UserRoles
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur.UserId, rp.PermissionId })
            .Join(_db.Permissions, x => x.PermissionId, p => p.Id, (x, p) => new { x.UserId, p.Module, p.Action })
            .Where(x => x.Module == "ItRequests" && x.Action == "Expert")
            .Select(x => x.UserId).Distinct().ToListAsync();

        var experts = await _db.Users.Where(u => expertUserIds.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, u.Username }).ToListAsync();
        return Ok(experts);
    }

    // ================== ثبت درخواست ==================
    public class CreateDto
    {
        public string RequesterName { get; set; } = "";
        public int? SystemInfoId { get; set; }
        public string? SystemLabel { get; set; }
        public string RequestType { get; set; } = "Hardware";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();
        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { message = "موضوع درخواست را وارد کنید." });

        var req = new ItRequest
        {
            RequesterName = string.IsNullOrWhiteSpace(dto.RequesterName) ? MyUsername : dto.RequesterName.Trim(),
            RequesterUserId = MyUserId,
            SystemInfoId = dto.SystemInfoId,
            SystemLabel = dto.SystemLabel,
            RequestType = dto.RequestType is "Software" or "Network" or "Telecom" ? dto.RequestType : "Hardware",
            Title = dto.Title.Trim(),
            Description = dto.Description ?? "",
            Status = "New"
        };
        _db.ItRequests.Add(req);
        await _db.SaveChangesAsync();

        // شماره منحصربه‌فرد: IT/سال شمسی/سریال — سریال هر سال از ۱ شروع می‌شود
        var pc = new System.Globalization.PersianCalendar();
        var py = pc.GetYear(DateTime.Now);
        var prefix = $"IT/{py}/";
        var serial = await _db.ItRequests.CountAsync(r => r.Number.StartsWith(prefix)) + 1;
        req.Number = $"{prefix}{serial}";
        Log(req.Id, "Requester", "Created", $"درخواست {req.Number} «{req.Title}» ثبت شد.", internalOnly: false);
        await _db.SaveChangesAsync();

        // نوتیفیکیشن به مدیران IT — لینک مستقیم به همان درخواست
        await _notify.SendManyAsync(await ManagerUserIdsAsync(),
            "درخواست IT جدید", $"{req.Number} — «{req.Title}» توسط {req.RequesterName}",
            req.RequesterName, "درخواست خدمت IT", $"/it-requests?open={req.Id}");
        await _notify.BroadcastChangedAsync("itrequests");

        return Ok(new { id = req.Id });
    }

    // ================== فهرست‌ها ==================
    private object ToItem(ItRequest r, List<ItRequestAssignment> asg, int attachCount, bool internalView, HashSet<int>? seenIds = null) => new
    {
        r.Id, r.Number, Seen = seenIds == null || seenIds.Contains(r.Id),
        r.RequesterName, r.RequesterUserId, r.SystemInfoId, r.SystemLabel,
        r.RequestType, r.Title, r.Description, r.Status,
        // درخواست‌دهنده فرایند داخلی را نمی‌بیند
        ManagerNote = internalView ? r.ManagerNote : null,
        r.FinalResponse,
        r.CreatedAt, r.AssignedAt, r.ApprovedAt, r.CompletedAt,
        AttachmentCount = attachCount,
        Assignments = internalView
            ? asg.Select(a => new
            {
                a.Id, a.ExpertUserId, a.ExpertName, a.ManagerInstruction,
                a.ExpertReport, a.ReportSubmitted, a.Done, a.ManagerDecision, a.ManagerDecisionNote,
                a.IncludeInFinal, a.RepliedAt
            }).Cast<object>().ToList()
            : new List<object>()
    };

    private async Task<List<object>> BuildList(IQueryable<ItRequest> q, bool internalView)
    {
        var reqs = await q.OrderByDescending(r => r.Id).ToListAsync();
        var ids = reqs.Select(r => r.Id).ToList();
        var asgs = await _db.ItRequestAssignments.Where(a => ids.Contains(a.RequestId)).ToListAsync();
        var attCounts = await _db.ItRequestAttachments.Where(a => ids.Contains(a.RequestId))
            .GroupBy(a => a.RequestId).Select(g => new { g.Key, C = g.Count() }).ToListAsync();
        // رویت‌شده‌های کاربر جاری — برای نشانگر «جدید»
        var seenIds = (await _db.ItRequestSeens.Where(sn => sn.UserId == MyUserId && ids.Contains(sn.RequestId))
            .Select(sn => sn.RequestId).ToListAsync()).ToHashSet();
        return reqs.Select(r => ToItem(r,
            asgs.Where(a => a.RequestId == r.Id).ToList(),
            attCounts.FirstOrDefault(c => c.Key == r.Id)?.C ?? 0, internalView, seenIds)).ToList();
    }

    // ================== رویت درخواست (نشانگر «جدید» برداشته می‌شود) ==================
    [HttpPost("{id:int}/seen")]
    public async Task<IActionResult> MarkSeen(int id)
    {
        if (!await _db.ItRequestSeens.AnyAsync(sn => sn.RequestId == id && sn.UserId == MyUserId))
        {
            _db.ItRequestSeens.Add(new ItRequestSeen { RequestId = id, UserId = MyUserId });
            await _db.SaveChangesAsync();
        }
        return Ok();
    }

    /// <summary>کارتابل درخواست‌دهنده — بدون جزئیات داخلی واحد IT.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine() =>
        Ok(await BuildList(_db.ItRequests.Where(r => r.RequesterUserId == MyUserId), internalView: false));

    /// <summary>کارتابل مدیر آی‌تی.</summary>
    [HttpGet("manager")]
    public async Task<IActionResult> ManagerInbox()
    {
        if (!await HasAsync("Manage")) return Forbid();
        return Ok(await BuildList(_db.ItRequests, internalView: true));
    }

    /// <summary>کارتابل کارشناس.</summary>
    [HttpGet("expert")]
    public async Task<IActionResult> ExpertInbox()
    {
        if (!await HasAsync("Expert")) return Forbid();
        var myReqIds = _db.ItRequestAssignments.Where(a => a.ExpertUserId == MyUserId).Select(a => a.RequestId);
        return Ok(await BuildList(_db.ItRequests.Where(r => myReqIds.Contains(r.Id)), internalView: true));
    }

    // ================== آرشیو رفت‌وبرگشت‌ها ==================
    [HttpGet("{id:int}/logs")]
    public async Task<IActionResult> Logs(int id)
    {
        var req = await _db.ItRequests.FindAsync(id);
        if (req == null) return NotFound();

        var isIt = await HasAsync("Manage") || await HasAsync("Expert");
        var q = _db.ItRequestLogs.Where(l => l.RequestId == id);
        if (!isIt) q = q.Where(l => !l.InternalOnly); // درخواست‌دهنده فقط رویدادهای عمومی

        return Ok(await q.OrderBy(l => l.Id)
            .Select(l => new { l.Id, l.ActorName, l.ActorRole, l.Action, l.Text, l.CreatedAt })
            .ToListAsync());
    }

    // ================== ارجاع مدیر ==================
    public class AssignDto
    {
        public string? ManagerNote { get; set; }
        public List<AssignItem> Experts { get; set; } = new();
        public class AssignItem { public int UserId { get; set; } public string? Instruction { get; set; } }
    }

    [HttpPost("{id:int}/assign")]
    public async Task<IActionResult> Assign(int id, [FromBody] AssignDto dto)
    {
        if (!await HasAsync("Manage")) return Forbid();
        var req = await _db.ItRequests.FindAsync(id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });
        if (dto.Experts.Count == 0) return BadRequest(new { message = "حداقل یک کارشناس انتخاب کنید." });

        var old = await _db.ItRequestAssignments.Where(a => a.RequestId == id).ToListAsync();
        _db.ItRequestAssignments.RemoveRange(old);

        var users = await _db.Users.Where(u => dto.Experts.Select(e => e.UserId).Contains(u.Id)).ToListAsync();
        var names = new List<string>();
        foreach (var e in dto.Experts)
        {
            var u = users.FirstOrDefault(x => x.Id == e.UserId);
            if (u == null) continue;
            names.Add(u.Username);
            _db.ItRequestAssignments.Add(new ItRequestAssignment
            {
                RequestId = id, ExpertUserId = u.Id, ExpertName = u.Username,
                ManagerInstruction = e.Instruction?.Trim()
            });
        }

        req.ManagerNote = dto.ManagerNote?.Trim();
        req.Status = "Assigned";
        req.AssignedAt = DateTime.Now;

        // آرشیو: نام کارشناسان در ارجاع ثبت می‌شود
        Log(id, "Manager", "Assigned", $"ارجاع به: {string.Join("، ", names)}" +
            (string.IsNullOrWhiteSpace(dto.ManagerNote) ? "" : $" — توضیح: {dto.ManagerNote}"));
        await _db.SaveChangesAsync();

        // نوتیفیکیشن به هر کارشناس
        foreach (var e in dto.Experts)
        {
            var inst = dto.Experts.FirstOrDefault(x => x.UserId == e.UserId)?.Instruction;
            await _notify.SendAsync(e.UserId, "ارجاع درخواست IT",
                $"{req.Number} — «{req.Title}»" + (string.IsNullOrWhiteSpace(inst) ? "" : $" — {inst}"),
                MyUsername, "درخواست خدمت IT", $"/it-requests?open={req.Id}");
        }
        await _notify.BroadcastChangedAsync("itrequests");
        return Ok();
    }

    // ================== گزارش کارشناس (انجام شد / نشد) ==================
    public class ReportDto
    {
        public string Report { get; set; } = "";
        public bool Done { get; set; } = true;
    }

    [HttpPost("{id:int}/report")]
    public async Task<IActionResult> SubmitReport(int id, [FromBody] ReportDto dto)
    {
        if (!await HasAsync("Expert")) return Forbid();
        var asg = await _db.ItRequestAssignments
            .FirstOrDefaultAsync(a => a.RequestId == id && a.ExpertUserId == MyUserId);
        if (asg == null) return NotFound(new { message = "این درخواست به شما ارجاع نشده است." });

        var req = await _db.ItRequests.FindAsync(id);

        asg.ExpertReport = dto.Report?.Trim();
        asg.Done = dto.Done;
        asg.ReportSubmitted = true;
        asg.ManagerDecision = null; // گزارش جدید = تصمیم قبلی مدیر باطل
        asg.RepliedAt = DateTime.Now;

        Log(id, "Expert", "Report", $"{MyUsername}: {(dto.Done ? "✅ انجام شد" : "❌ انجام نشد")} — {asg.ExpertReport}");
        await _db.SaveChangesAsync();

        // مدیر متوجه شود کدام کارشناس پاسخ داده (بند ۱۱)
        await _notify.SendManyAsync(await ManagerUserIdsAsync(),
            "گزارش کارشناس IT",
            $"{MyUsername} برای {req?.Number} «{req?.Title}» گزارش ثبت کرد: {(dto.Done ? "انجام شد" : "انجام نشد")}",
            MyUsername, "درخواست خدمت IT", $"/it-requests?open={id}");
        await _notify.BroadcastChangedAsync("itrequests");

        return Ok();
    }

    // ================== تصمیم مدیر روی گزارش هر کارشناس (تایید/رد — بند ۱۲) ==================
    public class DecisionDto { public bool Approved { get; set; } public string? Note { get; set; } }

    [HttpPost("{id:int}/assignments/{asgId:int}/decide")]
    public async Task<IActionResult> Decide(int id, int asgId, [FromBody] DecisionDto dto)
    {
        if (!await HasAsync("Manage")) return Forbid();
        var asg = await _db.ItRequestAssignments.FirstOrDefaultAsync(a => a.Id == asgId && a.RequestId == id);
        if (asg == null) return NotFound(new { message = "ارجاع پیدا نشد." });
        if (!asg.ReportSubmitted) return BadRequest(new { message = "این کارشناس هنوز گزارشی ثبت نکرده است." });

        var req = await _db.ItRequests.FindAsync(id);

        asg.ManagerDecision = dto.Approved ? "Approved" : "Rejected";
        asg.ManagerDecisionNote = dto.Note?.Trim();
        asg.IncludeInFinal = dto.Approved;

        if (!dto.Approved)
        {
            // رد شد: کارشناس باید دوباره اقدام/گزارش کند — وضعیت کلی تغییر نمی‌کند (بند ۱۳/۱۴)
            asg.ReportSubmitted = false;
        }

        Log(id, "Manager", dto.Approved ? "Approved" : "Rejected",
            $"گزارش {asg.ExpertName} {(dto.Approved ? "تایید" : "رد")} شد" +
            (string.IsNullOrWhiteSpace(dto.Note) ? "" : $" — {dto.Note}"));
        await _db.SaveChangesAsync();

        await _notify.SendAsync(asg.ExpertUserId,
            dto.Approved ? "گزارش شما تایید شد ✅" : "گزارش شما رد شد ❌",
            $"{req?.Number} — «{req?.Title}»" + (string.IsNullOrWhiteSpace(dto.Note) ? "" : $" — {dto.Note}"),
            MyUsername, "درخواست خدمت IT", $"/it-requests?open={id}");
        await _notify.BroadcastChangedAsync("itrequests");

        return Ok();
    }

    // ================== تایید نهایی مدیر (فقط وقتی همه گزارش‌ها تایید شده‌اند — بند ۱۳) ==================
    public class ApproveDto { public string? FinalResponse { get; set; } }

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, [FromBody] ApproveDto dto)
    {
        if (!await HasAsync("Manage")) return Forbid();
        var req = await _db.ItRequests.FindAsync(id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });

        var asgs = await _db.ItRequestAssignments.Where(a => a.RequestId == id).ToListAsync();
        if (asgs.Count == 0)
            return BadRequest(new { message = "ابتدا درخواست را به کارشناس ارجاع دهید." });
        if (asgs.Any(a => a.ManagerDecision != "Approved"))
            return BadRequest(new { message = "تا زمانی که گزارش همه کارشناسان تایید نشده، امکان تایید نهایی نیست." });

        if (string.IsNullOrWhiteSpace(dto.FinalResponse))
            return BadRequest(new { message = "پاسخ نهایی برای درخواست‌کننده را بنویسید." });

        req.FinalResponse = dto.FinalResponse.Trim();
        req.Status = "ManagerApproved";
        req.ApprovedAt = DateTime.Now;

        Log(id, "Manager", "Finalized", $"پاسخ نهایی: {req.FinalResponse}", internalOnly: false);
        await _db.SaveChangesAsync();

        await _notify.SendAsync(req.RequesterUserId, "پاسخ درخواست IT شما",
            $"{req.Number} — «{req.Title}» — {req.FinalResponse}", MyUsername, "درخواست خدمت IT", $"/it-requests?open={id}");
        await _notify.BroadcastChangedAsync("itrequests");

        return Ok();
    }

    // ================== بستن توسط مدیر (انجام کار توسط خود مدیر) ==================
    public class CloseDto { public string FinalResponse { get; set; } = ""; }

    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> CloseByManager(int id, [FromBody] CloseDto dto)
    {
        if (!await HasAsync("Manage")) return Forbid();
        var req = await _db.ItRequests.FindAsync(id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });
        if (req.Status is "Completed" or "Rejected")
            return BadRequest(new { message = "این درخواست قبلاً بسته شده است." });
        if (string.IsNullOrWhiteSpace(dto.FinalResponse))
            return BadRequest(new { message = "پاسخ را بنویسید." });

        req.FinalResponse = dto.FinalResponse.Trim();
        req.Status = "ManagerApproved";
        req.ApprovedAt = DateTime.Now;

        Log(id, "Manager", "Finalized", $"انجام و بسته‌شده توسط مدیر — {req.FinalResponse}", internalOnly: false);
        await _db.SaveChangesAsync();

        await _notify.SendAsync(req.RequesterUserId, "پاسخ درخواست IT شما",
            $"{req.Number} — «{req.Title}» — {req.FinalResponse}", MyUsername, "درخواست خدمت IT", $"/it-requests?open={id}");
        await _notify.BroadcastChangedAsync("itrequests");
        return Ok();
    }

    // ================== رد درخواست توسط مدیر ==================
    public class RejectDto { public string Reason { get; set; } = ""; }

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectDto dto)
    {
        if (!await HasAsync("Manage")) return Forbid();
        var req = await _db.ItRequests.FindAsync(id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });
        if (req.Status is "Completed" or "Rejected")
            return BadRequest(new { message = "این درخواست قبلاً بسته شده است." });
        if (string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest(new { message = "دلیل رد را بنویسید." });

        req.FinalResponse = dto.Reason.Trim();
        req.Status = "Rejected";
        req.ApprovedAt = DateTime.Now;

        Log(id, "Manager", "RejectedRequest", $"درخواست رد شد — {req.FinalResponse}", internalOnly: false);
        await _db.SaveChangesAsync();

        await _notify.SendAsync(req.RequesterUserId, "درخواست IT شما رد شد",
            $"{req.Number} — «{req.Title}» — {req.FinalResponse}", MyUsername, "درخواست خدمت IT", $"/it-requests?open={id}");
        await _notify.BroadcastChangedAsync("itrequests");
        return Ok();
    }

    // ================== کانفیگ اتصال به سرور مرکزی (برای همه کاربران) ==================
    /// <summary>اگر ItServerUrl تنظیم شده باشد، این نصب «شعبه» است و درخواست‌ها به سرور مرکزی می‌روند.</summary>
    [HttpGet("config")]
    public async Task<IActionResult> Config()
    {
        var st = await _db.AppSettings.FirstOrDefaultAsync();
        return Ok(new
        {
            itServerUrl = st?.ItServerUrl ?? "",
            itCompanyName = st?.ItCompanyName ?? ""
        });
    }

    // ================== درخواست از بیرون (شرکت‌های راه دور — بدون لاگین) ==================
    public class ExternalCreateDto
    {
        public string RequesterName { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string? Phone { get; set; }
        public string? SystemLabel { get; set; }
        public string RequestType { get; set; } = "Hardware";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
    }

    /// <summary>ثبت درخواست از بیرون سازمان — بدون نیاز به لاگین.
    /// شرکت‌های راه دور از نرم‌افزار محلی خود، درخواست را به سرور مرکزی می‌فرستند.</summary>
    [HttpPost("external")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalCreate([FromBody] ExternalCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RequesterName))
            return BadRequest(new { message = "نام درخواست‌کننده را وارد کنید." });
        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { message = "موضوع درخواست را وارد کنید." });

        var req = new ItRequest
        {
            RequesterName = dto.RequesterName.Trim() +
                            (string.IsNullOrWhiteSpace(dto.CompanyName) ? "" : $" ({dto.CompanyName.Trim()})"),
            RequesterUserId = 0, // کاربر بیرونی — لاگین ندارد
            SystemLabel = (string.IsNullOrWhiteSpace(dto.SystemLabel) ? "" : dto.SystemLabel.Trim() + " — ") +
                          (string.IsNullOrWhiteSpace(dto.CompanyName) ? "درخواست بیرونی" : $"شرکت: {dto.CompanyName.Trim()}"),
            RequestType = dto.RequestType is "Software" or "Network" or "Telecom" ? dto.RequestType : "Hardware",
            Title = dto.Title.Trim(),
            Description = (dto.Description ?? "") +
                          (string.IsNullOrWhiteSpace(dto.Phone) ? "" : $"<div>📞 تماس: {dto.Phone.Trim()}</div>"),
            Status = "New"
        };
        _db.ItRequests.Add(req);
        await _db.SaveChangesAsync();

        var pcx = new System.Globalization.PersianCalendar();
        var pyx = pcx.GetYear(DateTime.Now);
        var prefixx = $"IT/{pyx}/";
        var serialx = await _db.ItRequests.CountAsync(r => r.Number.StartsWith(prefixx)) + 1;
        req.Number = $"{prefixx}{serialx}";

        _db.ItRequestLogs.Add(new ItRequestLog
        {
            RequestId = req.Id, ActorName = req.RequesterName, ActorRole = "Requester",
            Action = "Created", Text = $"درخواست بیرونی {req.Number} ثبت شد.", InternalOnly = false
        });
        await _db.SaveChangesAsync();

        await _notify.SendManyAsync(await ManagerUserIdsAsync(),
            "درخواست IT بیرونی 🌐", $"{req.Number} — «{req.Title}» از {req.RequesterName}",
            req.RequesterName, "درخواست خدمت IT", $"/it-requests?open={req.Id}");
        await _notify.BroadcastChangedAsync("itrequests");

        // شماره پیگیری به درخواست‌دهنده برگردانده می‌شود
        return Ok(new { id = req.Id, number = req.Number });
    }

    /// <summary>پیگیری وضعیت درخواست با شماره — بدون لاگین.</summary>
    [HttpGet("track")]
    [AllowAnonymous]
    public async Task<IActionResult> Track([FromQuery] string number)
    {
        if (string.IsNullOrWhiteSpace(number))
            return BadRequest(new { message = "شماره درخواست را وارد کنید." });

        var req = await _db.ItRequests.FirstOrDefaultAsync(r => r.Number == number.Trim());
        if (req == null) return NotFound(new { message = "درخواستی با این شماره پیدا نشد." });

        return Ok(new
        {
            req.Number, req.Title, req.RequestType, req.Status,
            req.CreatedAt, req.CompletedAt,
            // فقط پاسخ نهایی — فرایند داخلی محرمانه است
            FinalResponse = req.Status is "ManagerApproved" or "Completed" or "Rejected" ? req.FinalResponse : null
        });
    }

    // ================== تایید تکمیل با شماره (درخواست‌دهنده بیرونی — بدون لاگین) ==================
    [HttpPost("track/complete")]
    [AllowAnonymous]
    public async Task<IActionResult> TrackComplete([FromQuery] string number)
    {
        var req = await _db.ItRequests.FirstOrDefaultAsync(r => r.Number == (number ?? "").Trim());
        if (req == null) return NotFound(new { message = "درخواستی با این شماره پیدا نشد." });
        if (req.Status != "ManagerApproved")
            return BadRequest(new { message = "این درخواست هنوز پاسخ نهایی نگرفته است." });

        req.Status = "Completed";
        req.CompletedAt = DateTime.Now;
        _db.ItRequestLogs.Add(new ItRequestLog
        {
            RequestId = req.Id, ActorName = req.RequesterName, ActorRole = "Requester",
            Action = "Completed", Text = "تایید نهایی توسط درخواست‌دهنده (از راه دور).", InternalOnly = false
        });
        await _db.SaveChangesAsync();

        await _notify.SendManyAsync(await ManagerUserIdsAsync(),
            "درخواست IT تکمیل شد", $"{req.Number} — «{req.Title}» توسط {req.RequesterName} تایید شد.",
            req.RequesterName, "درخواست خدمت IT", $"/it-requests?open={req.Id}");
        await _notify.BroadcastChangedAsync("itrequests");
        return Ok();
    }

    // ================== آمار درخواست‌ها (داشبورد سخت‌افزار) ==================
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var total = await _db.ItRequests.CountAsync();
        var completed = await _db.ItRequests.CountAsync(r => r.Status == "Completed");
        var inProgress = await _db.ItRequests.CountAsync(r => r.Status == "Assigned");
        // در انتظار مدیر: جدید (قبل از ارجاع) + گزارش‌های ثبت‌شده منتظر تایید/رد مدیر
        var newOnes = await _db.ItRequests.CountAsync(r => r.Status == "New");
        var waitingDecision = await _db.ItRequests.CountAsync(r =>
            r.Status == "Assigned" &&
            _db.ItRequestAssignments.Any(a => a.RequestId == r.Id && a.ReportSubmitted && a.ManagerDecision == null));
        var rejected = await _db.ItRequests.CountAsync(r => r.Status == "Rejected");
        var waitingRequester = await _db.ItRequests.CountAsync(r => r.Status == "ManagerApproved");

        return Ok(new
        {
            total,
            completed,
            inProgress,
            waitingManager = newOnes + waitingDecision,
            newOnes,
            waitingDecision,
            waitingRequester,
            rejected
        });
    }

    // ================== سوابق درخواست‌های یک سیستم (برای شناسنامه سیستم) ==================
    [HttpGet("by-system/{sysId:int}")]
    public async Task<IActionResult> BySystem(int sysId)
    {
        var reqs = await _db.ItRequests.Where(r => r.SystemInfoId == sysId)
            .OrderByDescending(r => r.Id).ToListAsync();
        var ids = reqs.Select(r => r.Id).ToList();
        var asgs = await _db.ItRequestAssignments.Where(a => ids.Contains(a.RequestId)).ToListAsync();

        return Ok(reqs.Select(r => new
        {
            r.Id, r.Number, r.Title, r.RequestType, r.Status, r.RequesterName,
            r.CreatedAt, r.CompletedAt, r.FinalResponse,
            Experts = asgs.Where(a => a.RequestId == r.Id).Select(a => a.ExpertName).ToList()
        }));
    }

    // ================== تکمیل توسط درخواست‌دهنده ==================
    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> Complete(int id)
    {
        var req = await _db.ItRequests.FindAsync(id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });
        if (req.RequesterUserId != MyUserId) return Forbid();
        if (req.Status != "ManagerApproved")
            return BadRequest(new { message = "درخواست هنوز توسط مدیر تایید نشده است." });

        req.Status = "Completed";
        req.CompletedAt = DateTime.Now;

        Log(id, "Requester", "Completed", "درخواست توسط درخواست‌دهنده تایید و تکمیل شد.", internalOnly: false);
        await _db.SaveChangesAsync();

        await _notify.SendManyAsync(await ManagerUserIdsAsync(),
            "درخواست IT تکمیل شد", $"{req.Number} — «{req.Title}» توسط {req.RequesterName} تایید نهایی شد.",
            req.RequesterName, "درخواست خدمت IT", $"/it-requests?open={id}");
        await _notify.BroadcastChangedAsync("itrequests");

        return Ok();
    }

    // ================== پیوست‌ها ==================
    [HttpGet("{id:int}/attachments")]
    public async Task<IActionResult> Attachments(int id)
    {
        var rows = await _db.ItRequestAttachments.Where(a => a.RequestId == id)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.UploaderRole, a.UploaderName, a.UploadedAt, a.FilePath, a.Data })
            .ToListAsync();
        return Ok(rows.Select(a => new { a.Id, a.FileName, a.ContentType, a.UploaderRole, a.UploaderName, a.UploadedAt,
            Size = a.FilePath is not null ? _store.Size(a.FilePath) : (long)a.Data.Length }));
    }

    [HttpPost("{id:int}/attachments")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> Upload(int id, IFormFile file, [FromQuery] string role = "Requester")
    {
        var req = await _db.ItRequests.FindAsync(id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });
        if (file == null || file.Length == 0) return BadRequest(new { message = "فایلی انتخاب نشده است." });
        if (file.Length > 10 * 1024 * 1024) return BadRequest(new { message = "حداکثر حجم فایل ۱۰ مگابایت است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        var relPath = await _store.SaveAsync("it-requests", id, ms, file.FileName);

        _db.ItRequestAttachments.Add(new ItRequestAttachment
        {
            RequestId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType ?? "application/octet-stream",
            FilePath = relPath,
            Data = Array.Empty<byte>(),
            UploaderRole = role is "Expert" or "Manager" ? role : "Requester",
            UploaderName = MyUsername,
            UploaderUserId = MyUserId
        });
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("attachments/{attId:int}/download")]
    [AllowAnonymous]
    public async Task<IActionResult> Download(int attId)
    {
        var att = await _db.ItRequestAttachments.FindAsync(attId);
        if (att == null) return NotFound();
        var bytes = _store.ReadBytes(att.FilePath) ?? (att.Data is { Length: > 0 } ? att.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });
        return File(bytes, att.ContentType, att.FileName);
    }
}
