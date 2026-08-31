using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>دستور کار — ایجاد برای خود/دیگران، پاسخ، تایید/رد، تمدید (تا ۵ بار)، بستن و بایگانی.</summary>
[ApiController]
[Route("api/workorders")]
[Authorize]
public class WorkOrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly FileStore _store;
    public WorkOrdersController(AppDbContext db, INotifyService notify, FileStore store) { _db = db; _notify = notify; _store = store; }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyUsername => User.FindFirstValue(ClaimTypes.Name) ?? "";
    private bool IsLegacyAdmin => User.IsInRole("Admin");

    private async Task<bool> HasAsync(string action)
    {
        var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId);
        if (!hasRoles)
        {
            var legacy = User.FindFirstValue(ClaimTypes.Role);
            if (legacy == "Admin") return true;
            if (legacy == "Operator") return action is "Create" or "View";
            return false;
        }
        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId)
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == "WorkOrders" && p.Action == action);
    }

    private void Log(int orderId, string action, string? text) =>
        _db.WorkOrderLogs.Add(new WorkOrderLog { OrderId = orderId, ActorName = MyUsername, Action = action, Text = text });

    // ================== دسترسی‌ها و لیست افراد مجاز ==================
    [HttpGet("my-access")]
    public async Task<IActionResult> MyAccess() => Ok(new
    {
        userId = MyUserId,
        canView = await HasAsync("View"),
        canCreate = await HasAsync("Create"),
        canAssignOthers = await HasAsync("AssignOthers")
    });

    /// <summary>افرادی که کاربر جاری می‌تواند به آن‌ها دستور کار بدهد (بند ۶).</summary>
    [HttpGet("targets")]
    public async Task<IActionResult> Targets()
    {
        var canOthers = await HasAsync("AssignOthers");
        var me = await _db.Users.FindAsync(MyUserId);
        var myFull = $"{me?.FirstName} {me?.LastName}".Trim();
        var result = new List<object> { new { Id = MyUserId, Username = (string.IsNullOrWhiteSpace(myFull) ? MyUsername : myFull) + " (خودم)" } };

        if (canOthers)
        {
            List<int> allowedIds;
            if (IsLegacyAdmin)
                allowedIds = await _db.Users.Where(u => u.IsActive && u.Id != MyUserId).Select(u => u.Id).ToListAsync();
            else
                allowedIds = await _db.WorkOrderAllowedAssignees.Where(a => a.OwnerUserId == MyUserId)
                    .Select(a => a.TargetUserId).ToListAsync();

            var users = await _db.Users.Where(u => allowedIds.Contains(u.Id) && u.IsActive && u.Id != MyUserId)
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Username }).ToListAsync();
            // نمایش با نام و نام خانوادگی (بند ۲)
            result.AddRange(users.Select(u => (object)new
            {
                u.Id,
                Username = string.IsNullOrWhiteSpace($"{u.FirstName} {u.LastName}".Trim())
                    ? u.Username : $"{u.FirstName} {u.LastName}".Trim() + $" ({u.Username})"
            }));
        }
        return Ok(result);
    }

    /// <summary>لیست مجاز یک کاربر (پیکربندی — فقط مدیر).</summary>
    [HttpGet("allowed/{userId:int}")]
    public async Task<IActionResult> GetAllowed(int userId)
    {
        if (!IsLegacyAdmin && !await HasAsync("AssignOthers")) return Forbid();
        return Ok(await _db.WorkOrderAllowedAssignees.Where(a => a.OwnerUserId == userId)
            .Select(a => a.TargetUserId).ToListAsync());
    }

    [HttpPost("allowed/{userId:int}")]
    public async Task<IActionResult> SetAllowed(int userId, [FromBody] List<int> targetIds)
    {
        if (!IsLegacyAdmin) return Forbid();
        var old = await _db.WorkOrderAllowedAssignees.Where(a => a.OwnerUserId == userId).ToListAsync();
        _db.WorkOrderAllowedAssignees.RemoveRange(old);
        foreach (var t in targetIds.Distinct().Where(t => t != userId))
            _db.WorkOrderAllowedAssignees.Add(new WorkOrderAllowedAssignee { OwnerUserId = userId, TargetUserId = t });
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ================== ایجاد دستور کار ==================
    public class CreateDto
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime DueAt { get; set; }
        public List<int> AssigneeUserIds { get; set; } = new();
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();
        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { message = "عنوان دستور کار را وارد کنید." });
        if (dto.AssigneeUserIds.Count == 0)
            return BadRequest(new { message = "حداقل یک نفر را انتخاب کنید." });
        // بند ۱: تاریخ و ساعت مقرر نباید قبل از زمان ثبت باشد
        if (dto.DueAt <= DateTime.Now)
            return BadRequest(new { message = "تاریخ و ساعت مقرر نمی‌تواند قبل از زمان ثبت باشد." });

        // بند ۵ و ۶: بدون مجوز «به دیگران»، فقط خودش | با مجوز، فقط لیست مجاز
        var others = dto.AssigneeUserIds.Where(id => id != MyUserId).Distinct().ToList();
        if (others.Count > 0)
        {
            if (!await HasAsync("AssignOthers"))
                return BadRequest(new { message = "شما فقط مجاز به ثبت دستور کار برای خودتان هستید." });
            if (!IsLegacyAdmin)
            {
                var allowed = await _db.WorkOrderAllowedAssignees.Where(a => a.OwnerUserId == MyUserId)
                    .Select(a => a.TargetUserId).ToListAsync();
                var illegal = others.Where(id => !allowed.Contains(id)).ToList();
                if (illegal.Count > 0)
                    return BadRequest(new { message = "برخی افراد انتخابی در لیست مجاز شما نیستند." });
            }
        }

        var wo = new WorkOrder
        {
            Title = dto.Title.Trim(),
            Description = dto.Description ?? "",
            OwnerUserId = MyUserId,
            OwnerName = MyUsername,
            DueAt = dto.DueAt,
            Status = "Open"
        };
        _db.WorkOrders.Add(wo);
        await _db.SaveChangesAsync();

        var pc = new System.Globalization.PersianCalendar();
        var py = pc.GetYear(DateTime.Now);
        var prefix = $"WO/{py}/";
        wo.Number = $"{prefix}{await _db.WorkOrders.CountAsync(w => w.Number.StartsWith(prefix)) + 1}";

        var users = await _db.Users.Where(u => dto.AssigneeUserIds.Contains(u.Id)).ToListAsync();

        // بند ۲: همه گیرندگان باید نام و نام خانوادگی داشته باشند
        var noName = users.Where(u => string.IsNullOrWhiteSpace(u.FirstName) || string.IsNullOrWhiteSpace(u.LastName))
            .Select(u => u.Username).ToList();
        if (noName.Count > 0)
        {
            _db.WorkOrders.Remove(wo);
            await _db.SaveChangesAsync();
            return BadRequest(new { message = $"این کاربران نام و نام خانوادگی ندارند: {string.Join("، ", noName)} — از بخش کاربران تکمیل کنید." });
        }

        foreach (var uid in dto.AssigneeUserIds.Distinct())
        {
            var u = users.FirstOrDefault(x => x.Id == uid);
            if (u == null) continue;
            _db.WorkOrderAssignees.Add(new WorkOrderAssignee { OrderId = wo.Id, UserId = u.Id, Name = $"{u.FirstName} {u.LastName}".Trim() });
        }

        Log(wo.Id, "Created", $"دستور کار {wo.Number} «{wo.Title}» — مهلت: {wo.DueAt:yyyy/MM/dd HH:mm} — گیرندگان: {string.Join("، ", users.Select(u => u.Username))}");
        await _db.SaveChangesAsync();

        foreach (var uid in dto.AssigneeUserIds.Distinct().Where(id => id != MyUserId))
            await _notify.SendAsync(uid, "دستور کار جدید 📋",
                $"{wo.Number} — «{wo.Title}» — مهلت: {ToFa(wo.DueAt)}",
                MyUsername, "دستور کار", $"/work-orders?open={wo.Id}");
        await _notify.BroadcastChangedAsync("workorders");

        return Ok(new { id = wo.Id, number = wo.Number });
    }

    private static string ToFa(DateTime d)
    {
        var pc = new System.Globalization.PersianCalendar();
        return $"{pc.GetYear(d)}/{pc.GetMonth(d):00}/{pc.GetDayOfMonth(d):00} {d:HH:mm}";
    }

    // ================== لیست‌ها ==================
    private async Task<List<object>> BuildList(IQueryable<WorkOrder> q)
    {
        var orders = await q.OrderByDescending(w => w.Id).ToListAsync();
        var ids = orders.Select(w => w.Id).ToList();
        var asgs = await _db.WorkOrderAssignees.Where(a => ids.Contains(a.OrderId)).ToListAsync();
        var attCounts = await _db.WorkOrderAttachments.Where(a => ids.Contains(a.OrderId))
            .GroupBy(a => a.OrderId).Select(g => new { g.Key, C = g.Count() }).ToListAsync();

        return orders.Select(w => (object)new
        {
            w.Id, w.Number, w.Title, w.Description, w.OwnerUserId, w.OwnerName,
            w.DueAt, w.Status, w.CloseNote, w.ClosedAt, w.ExtensionCount, w.CreatedAt,
            AttachmentCount = attCounts.FirstOrDefault(c => c.Key == w.Id)?.C ?? 0,
            Assignees = asgs.Where(a => a.OrderId == w.Id).Select(a => new
            {
                a.Id, a.UserId, a.Name, a.SeenAt, a.RepliedAt, a.Done, a.ReplyText,
                a.OwnerDecision, a.OwnerDecisionNote
            }).ToList()
        }).ToList();
    }

    /// <summary>دستورهایی که من داده‌ام (باز).</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine() =>
        Ok(await BuildList(_db.WorkOrders.Where(w => w.OwnerUserId == MyUserId && w.Status == "Open")));

    /// <summary>دستورهای محول به من (باز).</summary>
    [HttpGet("assigned")]
    public async Task<IActionResult> Assigned()
    {
        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);
        return Ok(await BuildList(_db.WorkOrders.Where(w => myOrderIds.Contains(w.Id) && w.Status == "Open")));
    }

    /// <summary>بایگانی — دستورهای بسته‌شده (من دستور داده‌ام یا به من محول شده).</summary>
    [HttpGet("archive")]
    public async Task<IActionResult> Archive()
    {
        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);
        return Ok(await BuildList(_db.WorkOrders.Where(w =>
            w.Status == "Closed" && (w.OwnerUserId == MyUserId || myOrderIds.Contains(w.Id)))));
    }

    /// <summary>تقویم شمسی — دستورهای بازه زمانی (بند ۱۳).</summary>
    [HttpGet("calendar")]
    public async Task<IActionResult> Calendar([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);
        var orders = await _db.WorkOrders
            .Where(w => w.DueAt >= from && w.DueAt < to && (w.OwnerUserId == MyUserId || myOrderIds.Contains(w.Id)))
            .ToListAsync();
        var ids = orders.Select(o => o.Id).ToList();
        var asgs = await _db.WorkOrderAssignees.Where(a => ids.Contains(a.OrderId)).ToListAsync();

        return Ok(orders.Select(w =>
        {
            var mine = w.OwnerUserId == MyUserId;
            var list = asgs.Where(a => a.OrderId == w.Id).ToList();
            var toSelf = list.All(a => a.UserId == w.OwnerUserId);
            // Kind: Self (برای خودم) | Others (برای دیگران) | Mixed (مشترک)
            var kind = mine && toSelf ? "Self"
                     : mine && list.Any(a => a.UserId == w.OwnerUserId) ? "Mixed"
                     : mine ? "Others" : "Assigned";

            // وضعیت رنگی (همان قوانین بند ۱۱ و ۱۲) — برای تیک/ضربدر در تقویم
            string tone;
            var allDone = list.Count > 0 && list.All(a => a.Done == true);
            if (allDone)
            {
                var last = list.Max(a => a.RepliedAt) ?? DateTime.MaxValue;
                tone = last <= w.DueAt ? "ontime" : "latedone";
            }
            else if (w.Status == "Closed") tone = "closed";
            else if (w.DueAt < DateTime.Now && w.DueAt.Date != DateTime.Today) tone = "late";
            else if (w.DueAt.Date == DateTime.Today) tone = DateTime.Now > w.DueAt ? "late" : "today";
            else tone = "none";

            return new { w.Id, w.Number, w.Title, w.DueAt, w.Status, Kind = kind, Tone = tone };
        }));
    }

    // ================== رویت (بند ۸) ==================
    [HttpPost("{id:int}/seen")]
    public async Task<IActionResult> Seen(int id)
    {
        var asg = await _db.WorkOrderAssignees.FirstOrDefaultAsync(a => a.OrderId == id && a.UserId == MyUserId);
        if (asg != null && asg.SeenAt == null)
        {
            asg.SeenAt = DateTime.Now;
            Log(id, "Seen", $"{MyUsername} دستور کار را رویت کرد.");
            await _db.SaveChangesAsync();
        }
        return Ok();
    }

    // ================== پاسخ گیرنده ==================
    public class ReplyDto { public bool Done { get; set; } = true; public string? Text { get; set; } }

    [HttpPost("{id:int}/reply")]
    public async Task<IActionResult> Reply(int id, [FromBody] ReplyDto dto)
    {
        var wo = await _db.WorkOrders.FindAsync(id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.Status == "Closed")
            return BadRequest(new { message = "این دستور کار بسته شده و قابل تغییر نیست." }); // بند ۱۰

        var asg = await _db.WorkOrderAssignees.FirstOrDefaultAsync(a => a.OrderId == id && a.UserId == MyUserId);
        if (asg == null) return NotFound(new { message = "این دستور کار به شما محول نشده است." });

        asg.Done = dto.Done;
        asg.ReplyText = dto.Text?.Trim();
        asg.RepliedAt = DateTime.Now;
        asg.OwnerDecision = null; // پاسخ جدید = تصمیم قبلی باطل
        if (asg.SeenAt == null) asg.SeenAt = DateTime.Now;

        Log(id, "Reply", $"{MyUsername}: {(dto.Done ? "✅ انجام شد" : "❌ انجام نشد")}{(string.IsNullOrWhiteSpace(dto.Text) ? "" : " — " + dto.Text)}");
        await _db.SaveChangesAsync();

        if (wo.OwnerUserId != MyUserId)
            await _notify.SendAsync(wo.OwnerUserId, "پاسخ دستور کار",
                $"{wo.Number} — {MyUsername}: {(dto.Done ? "انجام شد" : "انجام نشد")}",
                MyUsername, "دستور کار", $"/work-orders?open={id}");
        await _notify.BroadcastChangedAsync("workorders");
        return Ok();
    }

    // ================== تایید/رد پاسخ هر گیرنده (بند ۹ و ۱۸) ==================
    public class DecideDto { public bool Approved { get; set; } public string? Note { get; set; } }

    [HttpPost("{id:int}/assignees/{asgId:int}/decide")]
    public async Task<IActionResult> Decide(int id, int asgId, [FromBody] DecideDto dto)
    {
        var wo = await _db.WorkOrders.FindAsync(id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status == "Closed") return BadRequest(new { message = "دستور کار بسته شده است." });

        var asg = await _db.WorkOrderAssignees.FirstOrDefaultAsync(a => a.Id == asgId && a.OrderId == id);
        if (asg == null) return NotFound();
        if (asg.RepliedAt == null) return BadRequest(new { message = "این فرد هنوز پاسخی ثبت نکرده است." });

        asg.OwnerDecision = dto.Approved ? "Approved" : "Rejected";
        asg.OwnerDecisionNote = dto.Note?.Trim();
        if (!dto.Approved)
        {
            // رد: پاسخ باطل و برگشت به گیرنده
            asg.RepliedAt = null;
            asg.Done = null;
        }

        Log(id, dto.Approved ? "Approved" : "Rejected",
            $"پاسخ {asg.Name} {(dto.Approved ? "تایید" : "رد")} شد{(string.IsNullOrWhiteSpace(dto.Note) ? "" : " — " + dto.Note)}");
        await _db.SaveChangesAsync();

        await _notify.SendAsync(asg.UserId,
            dto.Approved ? "پاسخ شما تایید شد ✅" : "پاسخ شما رد شد ❌",
            $"{wo.Number} — «{wo.Title}»{(string.IsNullOrWhiteSpace(dto.Note) ? "" : " — " + dto.Note)}",
            MyUsername, "دستور کار", $"/work-orders?open={id}");
        await _notify.BroadcastChangedAsync("workorders");
        return Ok();
    }

    // ================== تمدید مهلت — تا ۵ بار (بند ۱۶) ==================
    public class ExtendDto { public DateTime NewDueAt { get; set; } public string? Note { get; set; } }

    [HttpPost("{id:int}/extend")]
    public async Task<IActionResult> Extend(int id, [FromBody] ExtendDto dto)
    {
        var wo = await _db.WorkOrders.FindAsync(id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status == "Closed") return BadRequest(new { message = "دستور کار بسته شده است." });
        if (wo.ExtensionCount >= 5)
            return BadRequest(new { message = "حداکثر ۵ بار امکان تمدید وجود دارد." });
        if (dto.NewDueAt <= DateTime.Now)
            return BadRequest(new { message = "تاریخ تمدید نمی‌تواند قبل از زمان فعلی باشد." });

        var old = wo.DueAt;
        wo.DueAt = dto.NewDueAt;
        wo.ExtensionCount++;

        Log(id, "Extended", $"تمدید {wo.ExtensionCount} از ۵ — از {ToFa(old)} به {ToFa(dto.NewDueAt)}{(string.IsNullOrWhiteSpace(dto.Note) ? "" : " — " + dto.Note)}");
        await _db.SaveChangesAsync();

        var asgs = await _db.WorkOrderAssignees.Where(a => a.OrderId == id).ToListAsync();
        foreach (var a in asgs.Where(a => a.UserId != MyUserId))
            await _notify.SendAsync(a.UserId, "تمدید مهلت دستور کار ⏳",
                $"{wo.Number} — مهلت جدید: {ToFa(dto.NewDueAt)}",
                MyUsername, "دستور کار", $"/work-orders?open={id}");
        await _notify.BroadcastChangedAsync("workorders");
        return Ok(new { extensionCount = wo.ExtensionCount });
    }

    // ================== بستن نهایی (بند ۹ و ۱۰) ==================
    public class CloseDto { public string? Note { get; set; } }

    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> Close(int id, [FromBody] CloseDto dto)
    {
        var wo = await _db.WorkOrders.FindAsync(id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status == "Closed") return BadRequest(new { message = "قبلاً بسته شده است." });

        wo.Status = "Closed";
        wo.ClosedAt = DateTime.Now;
        wo.CloseNote = dto.Note?.Trim();

        Log(id, "Closed", $"دستور کار توسط {MyUsername} نهایی و بسته شد{(string.IsNullOrWhiteSpace(dto.Note) ? "" : " — " + dto.Note)}");
        await _db.SaveChangesAsync();

        var asgs = await _db.WorkOrderAssignees.Where(a => a.OrderId == id).ToListAsync();
        foreach (var a in asgs.Where(a => a.UserId != MyUserId))
            await _notify.SendAsync(a.UserId, "دستور کار بسته شد 🔒",
                $"{wo.Number} — «{wo.Title}»", MyUsername, "دستور کار", $"/work-orders?open={id}");
        await _notify.BroadcastChangedAsync("workorders");
        return Ok();
    }

    // ================== تاریخچه (بند ۱۷ و ۱۸) ==================
    [HttpGet("{id:int}/logs")]
    public async Task<IActionResult> Logs(int id) =>
        Ok(await _db.WorkOrderLogs.Where(l => l.OrderId == id).OrderBy(l => l.Id)
            .Select(l => new { l.Id, l.ActorName, l.Action, l.Text, l.CreatedAt }).ToListAsync());

    // ================== پیوست‌ها (بند ۴) ==================
    [HttpGet("{id:int}/attachments")]
    public async Task<IActionResult> Attachments(int id)
    {
        var rows = await _db.WorkOrderAttachments.Where(a => a.OrderId == id)
            .Select(a => new { a.Id, a.FileName, a.UploaderName, a.UploadedAt, a.FilePath, a.Data })
            .ToListAsync();
        return Ok(rows.Select(a => new { a.Id, a.FileName, a.UploaderName, a.UploadedAt,
            Size = a.FilePath is not null ? _store.Size(a.FilePath) : (long)a.Data.Length }));
    }

    [HttpPost("{id:int}/attachments")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> Upload(int id, IFormFile file)
    {
        var wo = await _db.WorkOrders.FindAsync(id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (file == null || file.Length == 0) return BadRequest(new { message = "فایلی انتخاب نشده است." });
        if (file.Length > 10 * 1024 * 1024) return BadRequest(new { message = "حداکثر حجم فایل ۱۰ مگابایت است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        var relPath = await _store.SaveAsync("work-orders", id, ms, file.FileName);
        _db.WorkOrderAttachments.Add(new WorkOrderAttachment
        {
            OrderId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType ?? "application/octet-stream",
            FilePath = relPath,
            Data = Array.Empty<byte>(),
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
        var att = await _db.WorkOrderAttachments.FindAsync(attId);
        if (att == null) return NotFound();
        var bytes = _store.ReadBytes(att.FilePath) ?? (att.Data is { Length: > 0 } ? att.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });
        return File(bytes, att.ContentType, att.FileName);
    }
}
