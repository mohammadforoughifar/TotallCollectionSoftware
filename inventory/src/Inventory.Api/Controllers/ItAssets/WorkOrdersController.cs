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

    private async Task<string> MyDisplayNameAsync()
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == MyUserId);
        var n = UserDisplay.Name(me);
        return string.IsNullOrWhiteSpace(n) ? MyUsername : n;
    }

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

        /// <summary>اولویت: 0=کم | 1=عادی | 2=بالا | 3=فوری (پیش‌فرض: عادی).</summary>
        public int Priority { get; set; } = WorkOrderPriority.Normal;

        /// <summary>تکرار: 0=بدون تکرار | 1=روزانه | 2=هفتگی | 3=ماهانه.</summary>
        public int Recurrence { get; set; } = WorkOrderRecurrence.None;

        /// <summary>آیتم‌های چک‌لیست زیرکار (اختیاری) — به ترتیب لیست ذخیره می‌شوند.</summary>
        public List<string> ChecklistItems { get; set; } = new();

        /// <summary>ماژول مبدأ — مثلاً "InnerLetter" برای نامه داخلی (اختیاری).</summary>
        public string? SourceModule { get; set; }
        public int? SourceId { get; set; }
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
        if (!WorkOrderPriority.IsValid(dto.Priority))
            return BadRequest(new { message = "اولویت انتخابی نامعتبر است." });
        if (!WorkOrderRecurrence.IsValid(dto.Recurrence))
            return BadRequest(new { message = "الگوی تکرار نامعتبر است." });

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

        // اتصال به مبدأ (سورس) — اختیاری. اگر عنوان سورس داده شده باشد، همزمان نباید خالی/بی‌اعتبار باشد.
        if (!string.IsNullOrWhiteSpace(dto.SourceModule) && dto.SourceId is not null and > 0)
        {
            dto.SourceModule = dto.SourceModule.Trim();
            // اعتبارسنجی: ماژول سورس شناخته‌شده باشد (برای حفظ عمومی‌بودن، فقط طول/الگو بررسی می‌شود)
            if (dto.SourceModule.Length > 50)
                return BadRequest(new { message = "نام ماژول سورس نامعتبر است." });
        }
        else
        {
            dto.SourceModule = null;
            dto.SourceId = null;
        }

        var wo = new WorkOrder
        {
            Title = dto.Title.Trim(),
            Description = dto.Description ?? "",
            OwnerUserId = MyUserId,
            OwnerName = await MyDisplayNameAsync(),
            DueAt = dto.DueAt,
            Status = "Open",
            Priority = dto.Priority,
            Recurrence = dto.Recurrence,
            SourceModule = dto.SourceModule,
            SourceId = dto.SourceId
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

        // چک‌لیست زیرکار (اختیاری)
        var clOrder = 0;
        foreach (var item in dto.ChecklistItems.Where(t => !string.IsNullOrWhiteSpace(t)).Take(50))
            _db.WorkOrderChecklistItems.Add(new WorkOrderChecklistItem
            { OrderId = wo.Id, Text = item.Trim(), SortOrder = clOrder++ });

        var actor = await MyDisplayNameAsync();
        Log(wo.Id, "Created", $"دستور کار {wo.Number} «{wo.Title}» — مهلت: {wo.DueAt:yyyy/MM/dd HH:mm} — گیرندگان: {string.Join("، ", users.Select(u => UserDisplay.Name(u)))}");
        await _db.SaveChangesAsync();

        var prTag = wo.Priority >= WorkOrderPriority.High ? $" — اولویت: {WorkOrderPriority.ToFa(wo.Priority)}" : "";
        foreach (var uid in dto.AssigneeUserIds.Distinct().Where(id => id != MyUserId))
            await _notify.SendAsync(uid, wo.Priority == WorkOrderPriority.Urgent ? "دستور کار فوری 🔴" : "دستور کار جدید 📋",
                $"{wo.Number} — «{wo.Title}» — مهلت: {ToFa(wo.DueAt)}{prTag}",
                actor, "دستور کار", $"/work-orders?open={wo.Id}");
        await _notify.BroadcastChangedAsync("workorders");

        return Ok(new { id = wo.Id, number = wo.Number });
    }

    /// <summary>ویرایش دستور کار باز توسط دستوردهنده — عنوان، شرح، مهلت و گیرندگان.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();
        var wo = await _db.WorkOrders.FindAsync(id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status == "Closed")
            return BadRequest(new { message = "دستور کار بسته شده و قابل ویرایش نیست." });
        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { message = "عنوان دستور کار را وارد کنید." });
        if (dto.AssigneeUserIds.Count == 0)
            return BadRequest(new { message = "حداقل یک نفر را انتخاب کنید." });
        if (dto.DueAt <= DateTime.Now)
            return BadRequest(new { message = "تاریخ و ساعت مقرر نمی‌تواند قبل از زمان فعلی باشد." });
        if (!WorkOrderPriority.IsValid(dto.Priority))
            return BadRequest(new { message = "اولویت انتخابی نامعتبر است." });
        if (!WorkOrderRecurrence.IsValid(dto.Recurrence))
            return BadRequest(new { message = "الگوی تکرار نامعتبر است." });

        var others = dto.AssigneeUserIds.Where(x => x != MyUserId).Distinct().ToList();
        if (others.Count > 0)
        {
            if (!await HasAsync("AssignOthers"))
                return BadRequest(new { message = "شما فقط مجاز به ثبت دستور کار برای خودتان هستید." });
            if (!IsLegacyAdmin)
            {
                var allowed = await _db.WorkOrderAllowedAssignees.Where(a => a.OwnerUserId == MyUserId)
                    .Select(a => a.TargetUserId).ToListAsync();
                if (others.Any(x => !allowed.Contains(x)))
                    return BadRequest(new { message = "برخی افراد انتخابی در لیست مجاز شما نیستند." });
            }
        }

        var users = await _db.Users.Where(u => dto.AssigneeUserIds.Contains(u.Id)).ToListAsync();
        var noName = users.Where(u => string.IsNullOrWhiteSpace(u.FirstName) || string.IsNullOrWhiteSpace(u.LastName))
            .Select(u => u.Username).ToList();
        if (noName.Count > 0)
            return BadRequest(new { message = $"این کاربران نام و نام خانوادگی ندارند: {string.Join("، ", noName)} — از بخش کاربران تکمیل کنید." });

        var existing = await _db.WorkOrderAssignees.Where(a => a.OrderId == id).ToListAsync();
        var keep = dto.AssigneeUserIds.Distinct().ToHashSet();
        foreach (var a in existing.Where(a => !keep.Contains(a.UserId)).ToList())
        {
            if (a.RepliedAt != null)
                return BadRequest(new { message = $"نمی‌توان «{a.Name}» را حذف کرد چون پاسخ ثبت کرده است." });
            _db.WorkOrderAssignees.Remove(a);
        }
        foreach (var uid in keep.Where(uid => !existing.Any(a => a.UserId == uid)))
        {
            var u = users.FirstOrDefault(x => x.Id == uid);
            if (u == null) continue;
            _db.WorkOrderAssignees.Add(new WorkOrderAssignee { OrderId = wo.Id, UserId = u.Id, Name = UserDisplay.Name(u) });
        }

        wo.Title = dto.Title.Trim();
        wo.Description = dto.Description ?? "";
        wo.DueAt = dto.DueAt;
        wo.Priority = dto.Priority;
        wo.Recurrence = dto.Recurrence;
        wo.OwnerName = await MyDisplayNameAsync();

        // همگام‌سازی چک‌لیست: آیتم‌های تیک‌خورده حفظ می‌شوند (تطبیق بر اساس متن)، بقیه بازنویسی
        var oldItems = await _db.WorkOrderChecklistItems.Where(c => c.OrderId == id).ToListAsync();
        var newTexts = dto.ChecklistItems.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Take(50).ToList();
        _db.WorkOrderChecklistItems.RemoveRange(oldItems.Where(o => !newTexts.Contains(o.Text)));
        var clSort = 0;
        foreach (var text in newTexts)
        {
            var existing2 = oldItems.FirstOrDefault(o => o.Text == text);
            if (existing2 != null) existing2.SortOrder = clSort++;
            else _db.WorkOrderChecklistItems.Add(new WorkOrderChecklistItem { OrderId = id, Text = text, SortOrder = clSort++ });
        }

        Log(wo.Id, "Edited", $"ویرایش دستور کار — مهلت: {ToFa(wo.DueAt)} — گیرندگان: {string.Join("، ", users.Select(UserDisplay.Name))}");
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workorders");
        return Ok(new { id = wo.Id, number = wo.Number });
    }

    private static string ToFa(DateTime d)
    {
        var pc = new System.Globalization.PersianCalendar();
        return $"{pc.GetYear(d)}/{pc.GetMonth(d):00}/{pc.GetDayOfMonth(d):00} {d:HH:mm}";
    }

    // ================== لیست‌ها ==================

    /// <summary>پارامترهای فیلتر لیست — همه اختیاری؛ بدون پارامتر، رفتار قبلی حفظ می‌شود.</summary>
    public class ListFilterDto
    {
        /// <summary>جستجو در شماره و عنوان (بدون حساسیت به بزرگی/کوچکی).</summary>
        public string? Q { get; set; }

        /// <summary>فیلتر اولویت: 0..3 — null یعنی همه.</summary>
        public int? Priority { get; set; }

        /// <summary>بازه مهلت — از (شامل).</summary>
        public DateTime? DueFrom { get; set; }

        /// <summary>بازه مهلت — تا (غیرشامل؛ برای «تا امروز» مقدار فردا را بفرستید).</summary>
        public DateTime? DueTo { get; set; }

        /// <summary>فیلتر شخص: دستوردهنده یا یکی از گیرندگان.</summary>
        public int? UserId { get; set; }
    }

    /// <summary>اعمال فیلترهای مشترک روی کوئری — سمت دیتابیس تا حد ممکن.</summary>
    private IQueryable<WorkOrder> ApplyFilter(IQueryable<WorkOrder> q, ListFilterDto f)
    {
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var term = f.Q.Trim();
            q = q.Where(w => EF.Functions.Like(w.Title, $"%{term}%") || EF.Functions.Like(w.Number, $"%{term}%"));
        }
        if (f.Priority is >= WorkOrderPriority.Low and <= WorkOrderPriority.Urgent)
            q = q.Where(w => w.Priority == f.Priority);
        if (f.DueFrom != null) q = q.Where(w => w.DueAt >= f.DueFrom);
        if (f.DueTo != null) q = q.Where(w => w.DueAt < f.DueTo);
        if (f.UserId is > 0)
        {
            var uid = f.UserId.Value;
            var uidOrders = _db.WorkOrderAssignees.Where(a => a.UserId == uid).Select(a => a.OrderId);
            q = q.Where(w => w.OwnerUserId == uid || uidOrders.Contains(w.Id));
        }
        return q;
    }

    private async Task<List<object>> BuildList(IQueryable<WorkOrder> q)
    {
        // مرتب‌سازی: اول اولویت (فوری بالاتر)، بعد جدیدترین
        var orders = await q.OrderByDescending(w => w.Priority).ThenByDescending(w => w.Id).ToListAsync();
        var ids = orders.Select(w => w.Id).ToList();
        var asgs = await _db.WorkOrderAssignees.Where(a => ids.Contains(a.OrderId)).ToListAsync();
        var attCounts = await _db.WorkOrderAttachments.Where(a => ids.Contains(a.OrderId))
            .GroupBy(a => a.OrderId).Select(g => new { g.Key, C = g.Count() }).ToListAsync();
        var clStats = await _db.WorkOrderChecklistItems.Where(c => ids.Contains(c.OrderId))
            .GroupBy(c => c.OrderId)
            .Select(g => new { g.Key, Total = g.Count(), Done = g.Count(x => x.IsDone) }).ToListAsync();

        return orders.Select(w => (object)new
        {
            w.Id, w.Number, w.Title, w.Description, w.OwnerUserId, w.OwnerName,
            w.DueAt, w.Status, w.CloseNote, w.ClosedAt, w.ExtensionCount, w.CreatedAt,
            w.Priority, w.Recurrence,
            w.SourceModule, w.SourceId,
            ChecklistTotal = clStats.FirstOrDefault(c => c.Key == w.Id)?.Total ?? 0,
            ChecklistDone = clStats.FirstOrDefault(c => c.Key == w.Id)?.Done ?? 0,
            AttachmentCount = attCounts.FirstOrDefault(c => c.Key == w.Id)?.C ?? 0,
            Assignees = asgs.Where(a => a.OrderId == w.Id).Select(a => new
            {
                a.Id, a.UserId, a.Name, a.SeenAt, a.RepliedAt, a.Done, a.ReplyText,
                a.OwnerDecision, a.OwnerDecisionNote
            }).ToList()
        }).ToList();
    }

    /// <summary>دستورهایی که من داده‌ام (باز) — با فیلتر اختیاری جستجو/اولویت/بازه/شخص.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine([FromQuery] ListFilterDto filter) =>
        Ok(await BuildList(ApplyFilter(
            _db.WorkOrders.Where(w => w.OwnerUserId == MyUserId && w.Status == "Open"), filter)));

    /// <summary>دستورهای محول به من (باز) — با فیلتر اختیاری.</summary>
    [HttpGet("assigned")]
    public async Task<IActionResult> Assigned([FromQuery] ListFilterDto filter)
    {
        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);
        return Ok(await BuildList(ApplyFilter(
            _db.WorkOrders.Where(w => myOrderIds.Contains(w.Id) && w.Status == "Open"), filter)));
    }

    /// <summary>بایگانی — دستورهای بسته‌شده (من دستور داده‌ام یا به من محول شده) — با فیلتر اختیاری.</summary>
    [HttpGet("archive")]
    public async Task<IActionResult> Archive([FromQuery] ListFilterDto filter)
    {
        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);
        return Ok(await BuildList(ApplyFilter(_db.WorkOrders.Where(w =>
            w.Status == "Closed" && (w.OwnerUserId == MyUserId || myOrderIds.Contains(w.Id))), filter)));
    }

    /// <summary>دستورهای کارِ ساخته‌شده از یک مبدأ (سورس) — مثلاً نامه داخلی. برای لینک/نشان «دستورکار شده».</summary>
    /// <remarks>فقط متادیتای سبک برمی‌گرداند (بدون شرح/گیرندگان) تا به‌عنوان نشان در کارتابل نامه استفاده شود.</remarks>
    [HttpGet("for-source")]
    public async Task<IActionResult> ForSource([FromQuery] string? module, [FromQuery] int? sourceId)
    {
        if (string.IsNullOrWhiteSpace(module) || sourceId is not > 0)
            return Ok(new List<object>());
        var list = await _db.WorkOrders
            .Where(w => w.SourceModule == module && w.SourceId == sourceId)
            .OrderByDescending(w => w.Id)
            .Select(w => new { w.Id, w.Number, w.Title, w.OwnerName, w.Status, w.SourceModule, w.SourceId })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>جزئیات یک دستور کار به‌صورت مستقیم — برای لینک عمیق (مثلاً از نشان «دستورکار شده» نامه).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        if (!await HasAsync("View")) return Forbid();
        var list = await BuildList(_db.WorkOrders.Where(w => w.Id == id));
        return list.Count == 0 ? NotFound() : Ok(list[0]);
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

        // ---------- تکرارشونده: بعد از بستن، نوبت بعدی خودکار ساخته می‌شود ----------
        int? nextId = null;
        if (wo.Recurrence != WorkOrderRecurrence.None)
        {
            nextId = await CreateNextOccurrenceAsync(wo, asgs);
            if (nextId != null)
                Log(id, "Recurred", $"نوبت بعدی ({WorkOrderRecurrence.ToFa(wo.Recurrence)}) به‌صورت خودکار ساخته شد.");
            await _db.SaveChangesAsync();
        }

        await _notify.BroadcastChangedAsync("workorders");
        return Ok(new { nextId });
    }

    /// <summary>
    /// ساخت نوبت بعدیِ دستور تکرارشونده — کپی عنوان/شرح/اولویت/گیرندگان/چک‌لیست (تیک‌نخورده)
    /// با مهلت جابه‌جاشده طبق الگو. اگر مهلت محاسبه‌شده در گذشته بود، از الان به جلو می‌غلتد.
    /// </summary>
    private async Task<int?> CreateNextOccurrenceAsync(WorkOrder prev, List<WorkOrderAssignee> prevAsgs)
    {
        var nextDue = prev.Recurrence switch
        {
            WorkOrderRecurrence.Daily => prev.DueAt.AddDays(1),
            WorkOrderRecurrence.Weekly => prev.DueAt.AddDays(7),
            WorkOrderRecurrence.Monthly => prev.DueAt.AddMonths(1),
            _ => prev.DueAt
        };
        while (nextDue <= DateTime.Now)
            nextDue = prev.Recurrence switch
            {
                WorkOrderRecurrence.Daily => nextDue.AddDays(1),
                WorkOrderRecurrence.Weekly => nextDue.AddDays(7),
                WorkOrderRecurrence.Monthly => nextDue.AddMonths(1),
                _ => nextDue.AddDays(1)
            };

        var next = new WorkOrder
        {
            Title = prev.Title,
            Description = prev.Description,
            OwnerUserId = prev.OwnerUserId,
            OwnerName = prev.OwnerName,
            DueAt = nextDue,
            Status = "Open",
            Priority = prev.Priority,
            Recurrence = prev.Recurrence,
            RecurrenceParentId = prev.Id,
            SourceModule = prev.SourceModule,
            SourceId = prev.SourceId
        };
        _db.WorkOrders.Add(next);
        await _db.SaveChangesAsync();

        var pc = new System.Globalization.PersianCalendar();
        var prefix = $"WO/{pc.GetYear(DateTime.Now)}/";
        next.Number = $"{prefix}{await _db.WorkOrders.CountAsync(w => w.Number.StartsWith(prefix)) + 1}";

        foreach (var a in prevAsgs)
            _db.WorkOrderAssignees.Add(new WorkOrderAssignee { OrderId = next.Id, UserId = a.UserId, Name = a.Name });

        // چک‌لیست: همان گام‌ها ولی همه تیک‌نخورده
        var clItems = await _db.WorkOrderChecklistItems.Where(c => c.OrderId == prev.Id)
            .OrderBy(c => c.SortOrder).ToListAsync();
        foreach (var c in clItems)
            _db.WorkOrderChecklistItems.Add(new WorkOrderChecklistItem
            { OrderId = next.Id, Text = c.Text, SortOrder = c.SortOrder });

        _db.WorkOrderLogs.Add(new WorkOrderLog
        {
            OrderId = next.Id,
            ActorName = "سیستم",
            Action = "Created",
            Text = $"ایجاد خودکار نوبت {WorkOrderRecurrence.ToFa(prev.Recurrence)} — ادامه {prev.Number} — مهلت: {ToFa(nextDue)}"
        });
        await _db.SaveChangesAsync();

        foreach (var uid in prevAsgs.Select(a => a.UserId).Distinct().Where(u => u != prev.OwnerUserId))
            await _notify.SendAsync(uid, "دستور کار تکرارشونده 🔁",
                $"{next.Number} — «{next.Title}» — مهلت: {ToFa(nextDue)}",
                prev.OwnerName, "دستور کار", $"/work-orders?open={next.Id}");

        return next.Id;
    }

    // ================== چک‌لیست زیرکار ==================

    /// <summary>آیتم‌های چک‌لیست یک دستور کار — به ترتیب SortOrder.</summary>
    [HttpGet("{id:int}/checklist")]
    public async Task<IActionResult> Checklist(int id)
    {
        if (!await CanSeeOrderAsync(id)) return Forbid();
        return Ok(await _db.WorkOrderChecklistItems.Where(c => c.OrderId == id)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Text, c.SortOrder, c.IsDone, c.DoneByName, c.DoneAt })
            .ToListAsync());
    }

    public class ChecklistAddDto { public string Text { get; set; } = ""; }

    /// <summary>افزودن آیتم چک‌لیست — فقط دستوردهنده و فقط تا وقتی دستور باز است.</summary>
    [HttpPost("{id:int}/checklist")]
    public async Task<IActionResult> ChecklistAdd(int id, [FromBody] ChecklistAddDto dto)
    {
        var wo = await _db.WorkOrders.FindAsync(id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status == "Closed") return BadRequest(new { message = "دستور کار بسته شده است." });
        if (string.IsNullOrWhiteSpace(dto.Text)) return BadRequest(new { message = "متن آیتم را وارد کنید." });
        if (await _db.WorkOrderChecklistItems.CountAsync(c => c.OrderId == id) >= 50)
            return BadRequest(new { message = "حداکثر ۵۰ آیتم مجاز است." });

        var maxSort = await _db.WorkOrderChecklistItems.Where(c => c.OrderId == id)
            .Select(c => (int?)c.SortOrder).MaxAsync() ?? -1;
        var item = new WorkOrderChecklistItem { OrderId = id, Text = dto.Text.Trim(), SortOrder = maxSort + 1 };
        _db.WorkOrderChecklistItems.Add(item);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workorders");
        return Ok(new { item.Id });
    }

    public class ChecklistToggleDto { public bool IsDone { get; set; } }

    /// <summary>تیک/برداشتن تیک آیتم — دستوردهنده یا هر گیرنده، تا وقتی دستور باز است.</summary>
    [HttpPost("{id:int}/checklist/{itemId:int}/toggle")]
    public async Task<IActionResult> ChecklistToggle(int id, int itemId, [FromBody] ChecklistToggleDto dto)
    {
        var wo = await _db.WorkOrders.FindAsync(id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.Status == "Closed") return BadRequest(new { message = "دستور کار بسته شده است." });

        var isOwner = wo.OwnerUserId == MyUserId;
        var isAssignee = await _db.WorkOrderAssignees.AnyAsync(a => a.OrderId == id && a.UserId == MyUserId);
        if (!isOwner && !isAssignee) return Forbid();

        var item = await _db.WorkOrderChecklistItems.FirstOrDefaultAsync(c => c.Id == itemId && c.OrderId == id);
        if (item == null) return NotFound(new { message = "آیتم پیدا نشد." });

        item.IsDone = dto.IsDone;
        if (dto.IsDone)
        {
            item.DoneByUserId = MyUserId;
            item.DoneByName = await MyDisplayNameAsync();
            item.DoneAt = DateTime.Now;
        }
        else
        {
            item.DoneByUserId = null;
            item.DoneByName = null;
            item.DoneAt = null;
        }
        await _db.SaveChangesAsync();

        var total = await _db.WorkOrderChecklistItems.CountAsync(c => c.OrderId == id);
        var done = await _db.WorkOrderChecklistItems.CountAsync(c => c.OrderId == id && c.IsDone);
        await _notify.BroadcastChangedAsync("workorders");
        return Ok(new { total, done });
    }

    /// <summary>حذف آیتم چک‌لیست — فقط دستوردهنده، فقط دستور باز.</summary>
    [HttpDelete("{id:int}/checklist/{itemId:int}")]
    public async Task<IActionResult> ChecklistDelete(int id, int itemId)
    {
        var wo = await _db.WorkOrders.FindAsync(id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status == "Closed") return BadRequest(new { message = "دستور کار بسته شده است." });

        var item = await _db.WorkOrderChecklistItems.FirstOrDefaultAsync(c => c.Id == itemId && c.OrderId == id);
        if (item == null) return NotFound();
        _db.WorkOrderChecklistItems.Remove(item);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workorders");
        return Ok();
    }

    /// <summary>آیا کاربر جاری حق دیدن این دستور را دارد؟ (دستوردهنده یا گیرنده یا مجوز View)</summary>
    private async Task<bool> CanSeeOrderAsync(int id)
    {
        if (await HasAsync("View")) return true;
        var wo = await _db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return false;
        return wo.OwnerUserId == MyUserId
            || await _db.WorkOrderAssignees.AnyAsync(a => a.OrderId == id && a.UserId == MyUserId);
    }

    // ================== داشبورد آماری ==================

    /// <summary>
    /// آمار دستورهای کار مرتبط با کاربر جاری (دستوردهنده یا گیرنده).
    /// کارت‌های KPI + توزیع اولویت + عملکرد گیرندگان + روند ۶ ماه اخیر (شمسی).
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);
        var orders = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.OwnerUserId == MyUserId || myOrderIds.Contains(w.Id))
            .ToListAsync();
        var ids = orders.Select(o => o.Id).ToList();
        var asgs = await _db.WorkOrderAssignees.AsNoTracking()
            .Where(a => ids.Contains(a.OrderId)).ToListAsync();

        var now = DateTime.Now;

        // وضعیت هر دستور از دید نتیجه (همان قوانین Tone کلاینت)
        string ToneOf(WorkOrder w)
        {
            var list = asgs.Where(a => a.OrderId == w.Id).ToList();
            var allDone = list.Count > 0 && list.All(a => a.Done == true);
            if (allDone)
            {
                var last = list.Max(a => a.RepliedAt) ?? DateTime.MaxValue;
                return last <= w.DueAt ? "ontime" : "latedone";
            }
            if (w.Status == "Closed") return "closednodone";
            if (w.DueAt < now) return "late";
            return "open";
        }

        var tones = orders.ToDictionary(o => o.Id, ToneOf);

        var open = orders.Count(o => o.Status == "Open");
        var overdue = orders.Count(o => o.Status == "Open" && tones[o.Id] == "late");
        var dueToday = orders.Count(o => o.Status == "Open" && o.DueAt.Date == now.Date);
        var doneOnTime = orders.Count(o => tones[o.Id] == "ontime");
        var doneLate = orders.Count(o => tones[o.Id] == "latedone");
        var closedNoDone = orders.Count(o => tones[o.Id] == "closednodone");

        // توزیع اولویت دستورهای باز
        var byPriority = orders.Where(o => o.Status == "Open")
            .GroupBy(o => o.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Priority).ToList();

        // عملکرد گیرندگان — فقط دستورهایی که «من» داده‌ام (برای مدیر معنا دارد)
        var mineIds = orders.Where(o => o.OwnerUserId == MyUserId).Select(o => o.Id).ToHashSet();
        var dueMap = orders.Where(o => mineIds.Contains(o.Id)).ToDictionary(o => o.Id, o => o.DueAt);
        var perAssignee = asgs.Where(a => mineIds.Contains(a.OrderId) && a.UserId != MyUserId)
            .GroupBy(a => new { a.UserId, a.Name })
            .Select(g => new
            {
                g.Key.UserId,
                g.Key.Name,
                Total = g.Count(),
                Done = g.Count(x => x.Done == true),
                OnTime = g.Count(x => x.Done == true && x.RepliedAt != null && x.RepliedAt <= dueMap[x.OrderId]),
                NotDone = g.Count(x => x.Done == false),
                Pending = g.Count(x => x.RepliedAt == null)
            })
            .OrderByDescending(x => x.Total).Take(15).ToList();

        // روند ۶ ماه اخیر شمسی — تعداد ایجادشده / انجام به‌موقع / انجام با تاخیر
        var pc = new System.Globalization.PersianCalendar();
        var months = new List<object>();
        for (var i = 5; i >= 0; i--)
        {
            var refDate = now.AddMonths(-i);
            var py = pc.GetYear(refDate); var pm = pc.GetMonth(refDate);
            var monthOrders = orders.Where(o => pc.GetYear(o.CreatedAt) == py && pc.GetMonth(o.CreatedAt) == pm).ToList();
            months.Add(new
            {
                Year = py,
                Month = pm,
                Created = monthOrders.Count,
                OnTime = monthOrders.Count(o => tones[o.Id] == "ontime"),
                Late = monthOrders.Count(o => tones[o.Id] == "latedone"),
                NotDone = monthOrders.Count(o => tones[o.Id] is "late" or "closednodone")
            });
        }

        return Ok(new
        {
            Cards = new { Total = orders.Count, Open = open, Overdue = overdue, DueToday = dueToday, DoneOnTime = doneOnTime, DoneLate = doneLate, ClosedNoDone = closedNoDone },
            ByPriority = byPriority,
            PerAssignee = perAssignee,
            Months = months
        });
    }

    // ================== خروجی Excel / PDF ==================

    /// <summary>
    /// خروجی گرفتن از لیست دستورها — همان فیلترهای لیست + انتخاب تب.
    /// tab: mine | assigned | archive — format: xlsx | pdf
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string tab = "mine", [FromQuery] string format = "xlsx",
        [FromQuery] ListFilterDto? filter = null)
    {
        if (!await HasAsync("View")) return Forbid();
        filter ??= new ListFilterDto();

        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);
        var (query, tabFa) = tab switch
        {
            "assigned" => (_db.WorkOrders.Where(w => myOrderIds.Contains(w.Id) && w.Status == "Open"), "محول به من"),
            "archive" => (_db.WorkOrders.Where(w => w.Status == "Closed" && (w.OwnerUserId == MyUserId || myOrderIds.Contains(w.Id))), "بایگانی"),
            _ => (_db.WorkOrders.Where(w => w.OwnerUserId == MyUserId && w.Status == "Open"), "دستورهای من")
        };

        var orders = await ApplyFilter(query, filter)
            .OrderByDescending(w => w.Priority).ThenByDescending(w => w.Id).ToListAsync();
        var ids = orders.Select(o => o.Id).ToList();
        var asgs = await _db.WorkOrderAssignees.AsNoTracking().Where(a => ids.Contains(a.OrderId)).ToListAsync();
        var clStats = await _db.WorkOrderChecklistItems.Where(c => ids.Contains(c.OrderId))
            .GroupBy(c => c.OrderId)
            .Select(g => new { g.Key, Total = g.Count(), Done = g.Count(x => x.IsDone) }).ToListAsync();

        var spec = new Services.Export.ExportSpec
        {
            Title = "گزارش دستور کار",
            Subtitle = tabFa,
            Module = "دستور کار",
            FileBaseName = $"workorders_{tab}",
            Landscape = true,
            Columns =
            {
                // عرض‌ها بر حسب point در PDF (مثل DocArchiveExportService) — صفر یعنی نسبی
                new("شماره", Services.Export.ExportValueKind.Text, 70),
                new("عنوان") { Wrap = true },
                new("اولویت", Services.Export.ExportValueKind.Text, 45),
                new("تکرار", Services.Export.ExportValueKind.Text, 48),
                new("دستوردهنده", Services.Export.ExportValueKind.Text, 80),
                new("مهلت", Services.Export.ExportValueKind.DateTime, 85),
                new("گیرندگان") { Wrap = true },
                new("پاسخ", Services.Export.ExportValueKind.Text, 50),
                new("چک‌لیست", Services.Export.ExportValueKind.Text, 55),
                new("وضعیت", Services.Export.ExportValueKind.Text, 70),
            }
        };

        if (!string.IsNullOrWhiteSpace(filter.Q)) spec.Meta.Add(new("جستجو", filter.Q));
        if (filter.Priority is >= 0) spec.Meta.Add(new("اولویت", WorkOrderPriority.ToFa(filter.Priority.Value)));

        var now = DateTime.Now;
        foreach (var w in orders)
        {
            var list = asgs.Where(a => a.OrderId == w.Id).ToList();
            var replied = list.Count(a => a.RepliedAt != null);
            var cl = clStats.FirstOrDefault(c => c.Key == w.Id);
            var allDone = list.Count > 0 && list.All(a => a.Done == true);
            var lastReply = allDone ? list.Max(a => a.RepliedAt) : null;
            var status = allDone
                ? (lastReply <= w.DueAt ? "انجام به‌موقع" : "انجام با تاخیر")
                : w.Status == "Closed" ? "بسته شده"
                : w.DueAt < now ? "گذشته از مهلت"
                : w.DueAt.Date == now.Date ? "مهلت امروز" : "در جریان";

            var row = new Services.Export.ExportRow(
                w.Number, w.Title,
                WorkOrderPriority.ToFa(w.Priority),
                WorkOrderRecurrence.ToFa(w.Recurrence),
                w.OwnerName, w.DueAt,
                string.Join("، ", list.Select(a => a.Name)),
                $"{replied} از {list.Count}",
                cl == null ? "—" : $"{cl.Done} از {cl.Total}",
                status);
            if (status == "گذشته از مهلت") row.Style = Services.Export.ExportRowStyle.Danger;
            else if (status == "انجام به‌موقع") row.Style = Services.Export.ExportRowStyle.Success;
            spec.Rows.Add(row);
        }

        spec.Summary.Add(new("تعداد کل", spec.Rows.Count.ToString()));
        spec.Summary.Add(new("گذشته از مهلت", orders.Count(o => o.Status == "Open" && o.DueAt < now).ToString()));

        var isExcel = format.Equals("xlsx", StringComparison.OrdinalIgnoreCase);
        // عرض ستون در اکسل کاراکتری است؛ صفر می‌کنیم تا خودِ ExcelWriter بر اساس محتوا تنظیم کند
        if (isExcel) foreach (var c in spec.Columns) c.Width = 0;
        var bytes = isExcel ? Services.Export.ExcelWriter.Build(spec) : Services.Export.PdfWriter.Build(spec);
        return File(bytes,
            isExcel ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "application/pdf",
            spec.FileName(isExcel ? "xlsx" : "pdf"));
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
