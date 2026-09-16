using System.Globalization;
using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Inventory.Api.Services;
using Inventory.Api.Services.ItAssets;
using Inventory.Shared;
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
        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
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
        canAssignOthers = await HasAsync("AssignOthers"),
        canDelete = await HasAsync("Delete")
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

        /// <summary>برچسب‌ها (اختیاری) — حداکثر ۵ برچسب، هر یک تا ۳۰ حرف.</summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// ارجاع زنجیره‌ای (اختیاری) — شناسهٔ دستور والد. فقط گیرندهٔ دستور والد (تا وقتی باز است)
        /// می‌تواند بخشی از کار را به‌صورت زیر-دستور به نفر بعدی ارجاع دهد.
        /// </summary>
        public int? ParentOrderId { get; set; }
    }

    /// <summary>
    /// نرمال‌سازی برچسب‌ها: حذف تکراری/خالی، برش طول، سقف ۵ عدد؛
    /// خروجی به شکل ",کارگاه,برق," ذخیره می‌شود تا فیلتر دقیق LIKE ممکن باشد.
    /// </summary>
    private static string? NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags == null) return null;
        var list = tags.Select(t => (t ?? "").Trim().Replace(",", "،"))
            .Where(t => t.Length > 0)
            .Select(t => t.Length > 30 ? t[..30] : t)
            .Distinct()
            .Take(5)
            .ToList();
        return list.Count == 0 ? null : "," + string.Join(",", list) + ",";
    }

    /// <summary>تبدیل رشتهٔ ذخیره‌شده به لیست برچسب‌ها برای خروجی API.</summary>
    private static List<string> TagsToList(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? new List<string>()
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
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

        // ارجاع زنجیره‌ای — اعتبارسنجی والد: باید باز باشد و من گیرندهٔ آن باشم (یا دستوردهنده‌اش)
        WorkOrder? parent = null;
        if (dto.ParentOrderId is > 0)
        {
            parent = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == dto.ParentOrderId);
            if (parent == null)
                return BadRequest(new { message = "دستور والد یافت نشد." });
            if (parent.Status != "Open")
                return BadRequest(new { message = "دستور والد بسته شده — ارجاع زنجیره‌ای ممکن نیست." });
            var amAssignee = await _db.WorkOrderAssignees.AnyAsync(a => a.OrderId == parent.Id && a.UserId == MyUserId);
            if (!amAssignee && parent.OwnerUserId != MyUserId)
                return BadRequest(new { message = "فقط گیرندگان یا دستوردهندهٔ دستور والد می‌توانند زیر-دستور بسازند." });
            // جلوگیری از عمق بی‌نهایت: حداکثر ۵ سطح زنجیره
            var depth = 0; var cur = parent;
            while (cur?.ParentOrderId is > 0 && depth < 6)
            {
                cur = await _db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == cur.ParentOrderId);
                depth++;
            }
            if (depth >= 5)
                return BadRequest(new { message = "حداکثر عمق زنجیرهٔ ارجاع (۵ سطح) پر شده است." });
            // مهلت زیر-دستور نباید بعد از مهلت والد باشد
            if (dto.DueAt > parent.DueAt)
                return BadRequest(new { message = $"مهلت زیر-دستور نمی‌تواند بعد از مهلت دستور والد ({ToFa(parent.DueAt)}) باشد." });
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

        var users = await _db.Users.Where(u => dto.AssigneeUserIds.Contains(u.Id) && u.IsActive).ToListAsync();
        if (users.Count != dto.AssigneeUserIds.Distinct().Count())
            return BadRequest(new { message = "برخی گیرندگان وجود ندارند یا غیرفعال‌اند." });
        var noName = users.Where(u => string.IsNullOrWhiteSpace(u.FirstName) || string.IsNullOrWhiteSpace(u.LastName))
            .Select(u => u.Username).ToList();
        if (noName.Count > 0)
            return BadRequest(new { message = $"این کاربران نام و نام خانوادگی ندارند: {string.Join("، ", noName)} — از بخش کاربران تکمیل کنید." });
        if (parent != null && WorkOrderSchedule.Dates(dto.DueAt, dto.Recurrence).Last() > parent.DueAt)
            return BadRequest(new { message = "پایان سری تکرار از مهلت دستور والد عبور می‌کند؛ دستور مستقل بسازید یا مهلت والد را اصلاح کنید." });

        var wo = new WorkOrder
        {
            Title = dto.Title.Trim(), Description = dto.Description ?? "",
            OwnerUserId = MyUserId, OwnerName = await MyDisplayNameAsync(), DueAt = dto.DueAt,
            Priority = dto.Priority, Recurrence = dto.Recurrence, SourceModule = dto.SourceModule,
            SourceId = dto.SourceId, Tags = NormalizeTags(dto.Tags), ParentOrderId = parent?.Id
        };
        _db.WorkOrders.Add(wo);
        await _db.SaveChangesAsync();
        // The database identity prevents number reuse after a deletion and count-based races.
        wo.Number = $"WO/{new PersianCalendar().GetYear(wo.CreatedAt)}/{wo.Id}";
        foreach (var u in users)
            _db.WorkOrderAssignees.Add(new WorkOrderAssignee { OrderId = wo.Id, UserId = u.Id, Name = UserDisplay.Name(u) });
        var clOrder = 0;
        foreach (var item in dto.ChecklistItems.Where(t => !string.IsNullOrWhiteSpace(t)).Take(50))
            _db.WorkOrderChecklistItems.Add(new WorkOrderChecklistItem
            { OrderId = wo.Id, Text = item.Trim(), SortOrder = clOrder++ });
        Log(wo.Id, "Created", $"دستور کار {wo.Number} «{wo.Title}» — مهلت: {ToFa(wo.DueAt)} — گیرندگان: {string.Join("، ", users.Select(UserDisplay.Name))}");
        if (parent != null)
        {
            Log(wo.Id, "Chained", $"زیر-دستورِ {parent.Number} «{parent.Title}»");
            Log(parent.Id, "Chained", $"زیر-دستور {wo.Number} «{wo.Title}» توسط {wo.OwnerName} ساخته شد");
        }
        await _db.SaveChangesAsync();
        var added = await new WorkOrderSchedulingService(_db).MaterializeAsync(wo);
        await tx.CommitAsync();

        // One summary per recipient, not dozens of future-occurrence notifications.
        if (parent != null && parent.OwnerUserId != MyUserId)
            await _notify.SendAsync(parent.OwnerUserId, "ارجاع زنجیره‌ای 🔗",
                $"{wo.OwnerName} بخشی از {parent.Number} را به‌صورت زیر-دستور {wo.Number} ارجاع داد.",
                wo.OwnerName, "دستور کار", $"/work-orders?open={wo.Id}");
        var prTag = wo.Priority >= WorkOrderPriority.High ? $" — اولویت: {WorkOrderPriority.ToFa(wo.Priority)}" : "";
        var scheduleTag = added > 0 ? $" — {added + 1} نوبت در تقویم شما ثبت شد" : "";
        foreach (var uid in users.Select(u => u.Id).Where(id => id != MyUserId))
            await _notify.SendAsync(uid, wo.Priority == WorkOrderPriority.Urgent ? "دستور کار فوری 🔴" : "دستور کار جدید 📋",
                $"{wo.Number} — «{wo.Title}» — مهلت: {ToFa(wo.DueAt)}{prTag}{scheduleTag}",
                wo.OwnerName, "دستور کار", $"/work-orders?open={wo.Id}");
        await _notify.BroadcastChangedAsync("workorders");
        return Ok(new { id = wo.Id, number = wo.Number, occurrenceCount = added + 1 });
    }

    /// <summary>ویرایش دستور کار باز توسط دستوردهنده — عنوان، شرح، مهلت و گیرندگان.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
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

        if (wo.RecurrenceSeriesId != null && dto.Recurrence != wo.Recurrence)
            return BadRequest(new { message = "الگوی سری ثبت‌شده قابل تغییر از ویرایش یک نوبت نیست؛ ویرایش فقط روی همین نوبت اعمال می‌شود." });
        if (wo.ParentOrderId.HasValue)
        {
            var parent = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == wo.ParentOrderId);
            var last = wo.RecurrenceSeriesId == null ? WorkOrderSchedule.Dates(dto.DueAt, dto.Recurrence).Last() : dto.DueAt;
            if (parent == null || last > parent.DueAt)
                return BadRequest(new { message = "مهلت نوبت یا سری از مهلت دستور والد عبور می‌کند." });
        }

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
        if (users.Count != dto.AssigneeUserIds.Distinct().Count())
            return BadRequest(new { message = "برخی گیرندگان وجود ندارند." });
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
        wo.Tags = NormalizeTags(dto.Tags);
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
        var added = await new WorkOrderSchedulingService(_db).MaterializeAsync(wo);
        await tx.CommitAsync();
        await _notify.BroadcastChangedAsync("workorders");
        return Ok(new { id = wo.Id, number = wo.Number, occurrenceCount = added + 1 });
    }

    private static string ToFa(DateTime d)
    {
        var pc = new PersianCalendar();
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

        /// <summary>فیلتر برچسب — تطبیق دقیق یک برچسب.</summary>
        public string? Tag { get; set; }
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
        if (!string.IsNullOrWhiteSpace(f.Tag))
        {
            // برچسب‌ها به شکل ",برق,کارگاه," ذخیره می‌شوند → تطبیق دقیق با LIKE '%,برق,%'
            var tagToken = $"%,{f.Tag.Trim().Replace(",", "،")},%";
            q = q.Where(w => w.Tags != null && EF.Functions.Like(w.Tags, tagToken));
        }
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
        var cmCounts = await _db.WorkOrderComments.Where(c => ids.Contains(c.OrderId) && !c.IsDeleted)
            .GroupBy(c => c.OrderId).Select(g => new { g.Key, C = g.Count() }).ToListAsync();

        // ارجاع زنجیره‌ای: شماره/عنوان والدها + خلاصهٔ زیر-دستورها
        var parentIds = orders.Where(w => w.ParentOrderId is > 0).Select(w => w.ParentOrderId!.Value).Distinct().ToList();
        var parents = await _db.WorkOrders.AsNoTracking().Where(w => parentIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Number, w.Title }).ToListAsync();
        var children = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.ParentOrderId != null && ids.Contains(w.ParentOrderId.Value))
            .Select(w => new { w.Id, ParentId = w.ParentOrderId!.Value, w.Number, w.Title, w.Status, w.OwnerName })
            .ToListAsync();

        return orders.Select(w => (object)new
        {
            w.Id, w.Number, w.Title, w.Description, w.OwnerUserId, w.OwnerName,
            w.DueAt, w.Status, w.CloseNote, w.ClosedAt, w.ExtensionCount, w.CreatedAt,
            w.Priority, w.Recurrence, w.RecurrenceSeriesId, w.RecurrenceScheduledAt,
            w.SourceModule, w.SourceId,
            Tags = TagsToList(w.Tags),
            ChecklistTotal = clStats.FirstOrDefault(c => c.Key == w.Id)?.Total ?? 0,
            ChecklistDone = clStats.FirstOrDefault(c => c.Key == w.Id)?.Done ?? 0,
            AttachmentCount = attCounts.FirstOrDefault(c => c.Key == w.Id)?.C ?? 0,
            CommentCount = cmCounts.FirstOrDefault(c => c.Key == w.Id)?.C ?? 0,
            w.ParentOrderId,
            ParentNumber = parents.FirstOrDefault(p => p.Id == w.ParentOrderId)?.Number,
            ParentTitle = parents.FirstOrDefault(p => p.Id == w.ParentOrderId)?.Title,
            Children = children.Where(c => c.ParentId == w.Id)
                .Select(c => new { c.Id, c.Number, c.Title, c.Status, c.OwnerName }).ToList(),
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
        if (!await HasAsync("View") && !await CanSeeOrderAsync(id)) return Forbid();
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
        if (!await _db.WorkOrders.AnyAsync(w => w.Id == id)) return NotFound();
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
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
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
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
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
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
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
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status == "Closed") return BadRequest(new { message = "قبلاً بسته شده است." });

        // ارجاع زنجیره‌ای — تا زیر-دستورهای باز بسته نشوند، والد قابل بستن نیست
        var openChildren = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.ParentOrderId == id && w.Status == "Open")
            .Select(w => w.Number).ToListAsync();
        if (openChildren.Count > 0)
            return BadRequest(new { message = $"ابتدا زیر-دستورهای باز بسته شوند: {string.Join("، ", openChildren)}" });

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

    /// <summary>حذف منطقی یک نوبت؛ نه کل سری. دسترسی مستقل در نقش‌ها.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await HasAsync("Delete")) return Forbid();
        if (!await CanSeeOrderAsync(id)) return Forbid();
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound();
        if (await _db.WorkOrders.AnyAsync(w => w.ParentOrderId == id))
            return BadRequest(new { message = "این دستور زیر‌دستور دارد؛ ابتدا زیر‌دستورها را بررسی و حذف کنید. حذف آبشاری انجام نمی‌شود." });
        wo.DeletedAt = DateTime.Now;
        wo.DeletedByUserId = MyUserId;
        Log(id, "Deleted", $"حذف نوبت {wo.Number} توسط {await MyDisplayNameAsync()} (شناسه کاربر {MyUserId})؛ سوابق محفوظ است.");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _notify.BroadcastChangedAsync("workorders");
        return NoContent();
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
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
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
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
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
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
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
        var wo = await _db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return false;
        if (await HasAsync("View")) return true;
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
        var pc = new PersianCalendar();
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

    // ================== گزارش عملکرد افراد (موج ۸) ==================

    /// <summary>
    /// محاسبهٔ عملکرد گیرندگانِ دستورهایی که کاربر جاری داده است —
    /// دریافتی، انجام به‌موقع/با تاخیر، انجام‌نشده، در انتظار، میانگین تاخیر و درصد به‌موقع.
    /// خروجی برای رتبه‌بندی مرتب می‌شود: درصد به‌موقع ↓ سپس تعداد کل ↓.
    /// <paramref name="days"/>: بازهٔ روزهای اخیر بر اساس تاریخ ایجاد دستور — 0 یعنی همه.
    /// </summary>
    private async Task<List<PerfRow>> BuildPerformanceAsync(int days)
    {
        var q = _db.WorkOrders.AsNoTracking().Where(w => w.OwnerUserId == MyUserId);
        if (days > 0)
        {
            var from = DateTime.Now.AddDays(-days);
            q = q.Where(w => w.CreatedAt >= from);
        }
        var orders = await q.ToListAsync();
        var ids = orders.Select(o => o.Id).ToList();
        var dueMap = orders.ToDictionary(o => o.Id, o => o.DueAt);
        var statusMap = orders.ToDictionary(o => o.Id, o => o.Status);
        var asgs = await _db.WorkOrderAssignees.AsNoTracking()
            .Where(a => ids.Contains(a.OrderId)).ToListAsync();

        var now = DateTime.Now;
        var rows = asgs.GroupBy(a => new { a.UserId, a.Name })
            .Select(g =>
            {
                var done = g.Where(x => x.Done == true).ToList();
                var onTime = done.Count(x => x.RepliedAt != null && x.RepliedAt <= dueMap[x.OrderId]);
                var doneLate = done.Count - onTime;
                // میانگین تاخیر (ساعت) — فقط برای انجام‌های با تاخیر
                var delays = done.Where(x => x.RepliedAt != null && x.RepliedAt > dueMap[x.OrderId])
                    .Select(x => (x.RepliedAt!.Value - dueMap[x.OrderId]).TotalHours).ToList();
                var pending = g.Count(x => x.RepliedAt == null && statusMap[x.OrderId] == "Open");
                return new PerfRow
                {
                    UserId = g.Key.UserId,
                    Name = g.Key.Name,
                    Total = g.Count(),
                    Done = done.Count,
                    OnTime = onTime,
                    DoneLate = doneLate,
                    NotDone = g.Count(x => x.Done == false),
                    Pending = pending,
                    OverdueOpen = g.Count(x => x.RepliedAt == null && statusMap[x.OrderId] == "Open" && dueMap[x.OrderId] < now),
                    AvgDelayHours = delays.Count > 0 ? Math.Round(delays.Average(), 1) : 0,
                    OnTimePercent = g.Count() > 0 ? (int)Math.Round(onTime * 100.0 / g.Count()) : 0
                };
            })
            .OrderByDescending(r => r.OnTimePercent).ThenByDescending(r => r.Total).ThenBy(r => r.Name)
            .ToList();

        for (var i = 0; i < rows.Count; i++) rows[i].Rank = i + 1;
        return rows;
    }

    public class PerfRow
    {
        public int Rank { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; } = "";
        public int Total { get; set; }          // کل دریافتی
        public int Done { get; set; }           // انجام‌شده (تایید گیرنده)
        public int OnTime { get; set; }         // انجام به‌موقع
        public int DoneLate { get; set; }       // انجام با تاخیر
        public int NotDone { get; set; }        // اعلام «انجام نشد»
        public int Pending { get; set; }        // در انتظار پاسخ (دستور باز)
        public int OverdueOpen { get; set; }    // در انتظار و گذشته از مهلت
        public double AvgDelayHours { get; set; } // میانگین تاخیر انجام‌های دیرهنگام (ساعت)
        public int OnTimePercent { get; set; }  // درصد به‌موقع از کل دریافتی
    }

    /// <summary>گزارش عملکرد افراد — فقط دستورهایی که کاربر جاری داده است. days=0 یعنی همهٔ بازه.</summary>
    [HttpGet("performance")]
    public async Task<IActionResult> Performance([FromQuery] int days = 0)
    {
        if (!await HasAsync("View")) return Forbid();
        if (days is < 0 or > 3660) return BadRequest(new { message = "بازهٔ زمانی نامعتبر است." });
        return Ok(await BuildPerformanceAsync(days));
    }

    /// <summary>خروجی Excel/PDF گزارش عملکرد افراد — همان داده با رتبه‌بندی.</summary>
    [HttpGet("performance/export")]
    public async Task<IActionResult> PerformanceExport([FromQuery] string format = "xlsx", [FromQuery] int days = 0)
    {
        if (!await HasAsync("View")) return Forbid();
        if (days is < 0 or > 3660) return BadRequest(new { message = "بازهٔ زمانی نامعتبر است." });
        var rows = await BuildPerformanceAsync(days);

        var periodFa = days switch
        {
            0 => "همهٔ بازهٔ زمانی",
            30 => "۳۰ روز اخیر",
            90 => "۹۰ روز اخیر",
            365 => "سال اخیر",
            _ => $"{days} روز اخیر"
        };

        var spec = new Services.Export.ExportSpec
        {
            Title = "گزارش عملکرد افراد",
            Subtitle = "دستورهای کاری که من داده‌ام",
            Module = "دستور کار",
            FileBaseName = "workorders_performance",
            Landscape = true,
            Columns =
            {
                new("رتبه", Services.Export.ExportValueKind.Number, 35),
                new("نام"),
                new("دریافتی", Services.Export.ExportValueKind.Number, 52),
                new("انجام‌شده", Services.Export.ExportValueKind.Number, 58),
                new("به‌موقع", Services.Export.ExportValueKind.Number, 52),
                new("با تاخیر", Services.Export.ExportValueKind.Number, 52),
                new("انجام نشد", Services.Export.ExportValueKind.Number, 58),
                new("در انتظار", Services.Export.ExportValueKind.Number, 55),
                new("منقضی باز", Services.Export.ExportValueKind.Number, 58),
                new("میانگین تاخیر (ساعت)", Services.Export.ExportValueKind.Number, 78),
                new("٪ به‌موقع", Services.Export.ExportValueKind.Number, 52),
            }
        };
        spec.Meta.Add(new("بازه", periodFa));

        foreach (var r in rows)
        {
            var row = new Services.Export.ExportRow(
                r.Rank, r.Name, r.Total, r.Done, r.OnTime, r.DoneLate,
                r.NotDone, r.Pending, r.OverdueOpen, r.AvgDelayHours, r.OnTimePercent);
            if (r.OnTimePercent >= 80) row.Style = Services.Export.ExportRowStyle.Success;
            else if (r.OverdueOpen > 0) row.Style = Services.Export.ExportRowStyle.Danger;
            spec.Rows.Add(row);
        }

        spec.Summary.Add(new("تعداد افراد", rows.Count.ToString()));
        spec.Summary.Add(new("مجموع دستورهای محول‌شده", rows.Sum(r => r.Total).ToString()));
        spec.Summary.Add(new("مجموع انجام به‌موقع", rows.Sum(r => r.OnTime).ToString()));

        var isExcel = format.Equals("xlsx", StringComparison.OrdinalIgnoreCase);
        if (isExcel) foreach (var c in spec.Columns) c.Width = 0;
        var bytes = isExcel ? Services.Export.ExcelWriter.Build(spec) : Services.Export.PdfWriter.Build(spec);
        return File(bytes,
            isExcel ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "application/pdf",
            spec.FileName(isExcel ? "xlsx" : "pdf"));
    }

    // ================== کانبان: تغییر سریع اولویت ==================

    public class PriorityDto { public int Priority { get; set; } }
    /// <summary>
    /// تغییر سریع اولویت (درگ‌اند‌دراپ کانبان) — فقط دستوردهنده و فقط تا وقتی دستور باز است.
    /// </summary>
    [HttpPost("{id:int}/priority")]
    public async Task<IActionResult> SetPriority(int id, [FromBody] PriorityDto dto)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status != "Open") return BadRequest(new { message = "دستور بسته شده است؛ اولویت قابل تغییر نیست." });
        if (!WorkOrderPriority.IsValid(dto.Priority))
            return BadRequest(new { message = "اولویت انتخابی نامعتبر است." });

        if (wo.Priority != dto.Priority)
        {
            Log(id, "Edited", $"تغییر اولویت از «{WorkOrderPriority.ToFa(wo.Priority)}» به «{WorkOrderPriority.ToFa(dto.Priority)}» (کانبان)");
            wo.Priority = dto.Priority;
            await _db.SaveChangesAsync();
            await _notify.BroadcastChangedAsync("workorders");
        }
        return Ok();
    }

    // ================== برچسب/دسته‌بندی ==================

    /// <summary>
    /// برچسب‌های پرکاربرد کاربر جاری (از دستورهای خودش یا محول به خودش) —
    /// برای پیشنهاد در فرم و چیپ‌های فیلتر. مرتب بر اساس بیشترین استفاده.
    /// </summary>
    [HttpGet("my-tags")]
    public async Task<IActionResult> MyTags()
    {
        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);
        var tagStrings = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Tags != null && (w.OwnerUserId == MyUserId || myOrderIds.Contains(w.Id)))
            .Select(w => w.Tags!)
            .ToListAsync();

        var top = tagStrings.SelectMany(t => TagsToList(t))
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Take(30)
            .Select(g => g.Key)
            .ToList();
        return Ok(top);
    }

    // ================== قالب‌های آمادهٔ دستور کار (موج ۷) ==================

    public class TemplateDto
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public int Priority { get; set; } = WorkOrderPriority.Normal;
        public int Recurrence { get; set; } = WorkOrderRecurrence.None;
        public List<int> AssigneeUserIds { get; set; } = new();
        public List<string> ChecklistItems { get; set; } = new();
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>فهرست قالب‌های کاربر جاری — پرکاربردها بالا. هر کاربر فقط قالب‌های خودش را می‌بیند.</summary>
    [HttpGet("templates")]
    public async Task<IActionResult> Templates()
    {
        if (!await HasAsync("Create")) return Forbid();
        var rows = await _db.WorkOrderTemplates.AsNoTracking()
            .Where(t => t.OwnerUserId == MyUserId)
            .OrderByDescending(t => t.UsageCount).ThenByDescending(t => t.Id)
            .ToListAsync();
        return Ok(rows.Select(t => new
        {
            t.Id, t.Name, t.Title, t.Description, t.Priority, t.Recurrence, t.UsageCount, t.CreatedAt,
            AssigneeUserIds = (t.AssigneeUserIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var v) ? v : 0).Where(v => v > 0).ToList(),
            ChecklistItems = (t.ChecklistItems ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
            Tags = TagsToList(t.Tags)
        }));
    }

    /// <summary>ذخیرهٔ قالب جدید — حداکثر ۲۰ قالب برای هر کاربر تا فهرست شلوغ نشود.</summary>
    [HttpPost("templates")]
    public async Task<IActionResult> TemplateCreate([FromBody] TemplateDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();

        var name = (dto.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "نام قالب را وارد کنید." });
        if (name.Length > 100) return BadRequest(new { message = "نام قالب حداکثر ۱۰۰ حرف است." });
        if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { message = "عنوان دستور کار قالب خالی است." });
        if (!WorkOrderPriority.IsValid(dto.Priority)) return BadRequest(new { message = "اولویت انتخابی نامعتبر است." });
        if (!WorkOrderRecurrence.IsValid(dto.Recurrence)) return BadRequest(new { message = "الگوی تکرار نامعتبر است." });
        if (dto.ChecklistItems.Count > 50) return BadRequest(new { message = "چک‌لیست حداکثر ۵۰ آیتم می‌تواند داشته باشد." });

        var count = await _db.WorkOrderTemplates.CountAsync(t => t.OwnerUserId == MyUserId);
        if (count >= 20) return BadRequest(new { message = "حداکثر ۲۰ قالب می‌توانید ذخیره کنید؛ ابتدا یکی را حذف کنید." });

        // نام تکراری — به‌روزرسانی همان قالب (ذخیرهٔ مجدد با یک نام = جایگزینی)
        var existing = await _db.WorkOrderTemplates
            .FirstOrDefaultAsync(t => t.OwnerUserId == MyUserId && t.Name == name);

        var tpl = existing ?? new WorkOrderTemplate { OwnerUserId = MyUserId, Name = name };
        tpl.Title = dto.Title.Trim();
        tpl.Description = dto.Description ?? "";
        tpl.Priority = dto.Priority;
        tpl.Recurrence = dto.Recurrence;
        tpl.AssigneeUserIds = dto.AssigneeUserIds.Count > 0 ? string.Join(",", dto.AssigneeUserIds.Distinct()) : null;
        tpl.ChecklistItems = dto.ChecklistItems.Count > 0
            ? string.Join("\n", dto.ChecklistItems.Select(s => (s ?? "").Trim()).Where(s => s.Length > 0).Take(50))
            : null;
        tpl.Tags = NormalizeTags(dto.Tags);

        if (existing == null) _db.WorkOrderTemplates.Add(tpl);
        await _db.SaveChangesAsync();
        return Ok(new { tpl.Id, updated = existing != null });
    }

    /// <summary>ثبت یک‌بار استفاده از قالب — برای مرتب‌سازی پرکاربردها در بالا.</summary>
    [HttpPost("templates/{tplId:int}/used")]
    public async Task<IActionResult> TemplateUsed(int tplId)
    {
        var tpl = await _db.WorkOrderTemplates.FirstOrDefaultAsync(t => t.Id == tplId && t.OwnerUserId == MyUserId);
        if (tpl == null) return NotFound(new { message = "قالب پیدا نشد." });
        tpl.UsageCount++;
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>حذف قالب — فقط سازنده.</summary>
    [HttpDelete("templates/{tplId:int}")]
    public async Task<IActionResult> TemplateDelete(int tplId)
    {
        var tpl = await _db.WorkOrderTemplates.FirstOrDefaultAsync(t => t.Id == tplId && t.OwnerUserId == MyUserId);
        if (tpl == null) return NotFound(new { message = "قالب پیدا نشد." });
        _db.WorkOrderTemplates.Remove(tpl);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ================== رشتهٔ گفتگو (کامنت) داخل دستور ==================

    /// <summary>لیست کامنت‌های یک دستور — قدیمی به جدید. کامنت‌های حذف‌شده با متن خالی می‌آیند تا رشتهٔ پاسخ‌ها نشکند.</summary>
    [HttpGet("{id:int}/comments")]
    public async Task<IActionResult> Comments(int id)
    {
        if (!await CanSeeOrderAsync(id)) return Forbid();
        var items = await _db.WorkOrderComments.AsNoTracking()
            .Where(c => c.OrderId == id)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToListAsync();
        return Ok(items.Select(c => new
        {
            c.Id, c.AuthorUserId, c.AuthorName,
            Text = c.IsDeleted ? "" : c.Text,
            c.ReplyToId, c.IsDeleted, c.CreatedAt, c.EditedAt
        }));
    }

    public class CommentDto { public string Text { get; set; } = ""; public int? ReplyToId { get; set; } }

    /// <summary>ثبت کامنت — دستوردهنده و گیرندگان؛ فقط تا وقتی دستور باز است. به بقیهٔ طرف‌ها اعلان می‌رود.</summary>
    [HttpPost("{id:int}/comments")]
    public async Task<IActionResult> AddComment(int id, [FromBody] CommentDto dto)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound();
        if (wo.Status != "Open") return BadRequest(new { message = "دستور بسته شده است؛ امکان ثبت کامنت نیست." });

        var isOwner = wo.OwnerUserId == MyUserId;
        var isAssignee = await _db.WorkOrderAssignees.AnyAsync(a => a.OrderId == id && a.UserId == MyUserId);
        if (!isOwner && !isAssignee) return Forbid();

        var text = (dto.Text ?? "").Trim();
        if (text.Length == 0) return BadRequest(new { message = "متن کامنت خالی است." });
        if (text.Length > 2000) return BadRequest(new { message = "متن کامنت حداکثر ۲۰۰۰ حرف است." });

        // پاسخ باید به کامنتی از همین دستور باشد
        if (dto.ReplyToId is > 0 &&
            !await _db.WorkOrderComments.AnyAsync(c => c.Id == dto.ReplyToId && c.OrderId == id))
            return BadRequest(new { message = "کامنت مرجع یافت نشد." });

        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == MyUserId);
        var myName = UserDisplay.Name(me) is { Length: > 0 } n ? n : MyUsername;

        var cm = new WorkOrderComment
        {
            OrderId = id, AuthorUserId = MyUserId, AuthorName = myName,
            Text = text, ReplyToId = dto.ReplyToId is > 0 ? dto.ReplyToId : null
        };
        _db.WorkOrderComments.Add(cm);
        Log(id, "Commented", text.Length > 120 ? text[..120] + "…" : text);
        await _db.SaveChangesAsync();

        // اعلان به همهٔ طرف‌های دستور به‌جز نویسنده
        var others = await _db.WorkOrderAssignees.Where(a => a.OrderId == id && a.UserId != MyUserId)
            .Select(a => a.UserId).Distinct().ToListAsync();
        if (wo.OwnerUserId != MyUserId) others.Add(wo.OwnerUserId);
        var brief = text.Length > 80 ? text[..80] + "…" : text;
        foreach (var uid in others.Distinct())
            await _notify.SendAsync(uid, "کامنت جدید دستور کار 💬",
                $"{wo.Number} — «{wo.Title}»: {brief}",
                myName, "دستور کار", $"/work-orders?open={wo.Id}");
        await _notify.BroadcastChangedAsync("workorders");

        return Ok(new { cm.Id, cm.AuthorUserId, cm.AuthorName, cm.Text, cm.ReplyToId, cm.IsDeleted, cm.CreatedAt, cm.EditedAt });
    }

    /// <summary>ویرایش کامنت — فقط نویسنده و فقط تا وقتی دستور باز است.</summary>
    [HttpPut("comments/{commentId:int}")]
    public async Task<IActionResult> EditComment(int commentId, [FromBody] CommentDto dto)
    {
        var cm = await _db.WorkOrderComments.FindAsync(commentId);
        if (cm == null || cm.IsDeleted) return NotFound();
        if (cm.AuthorUserId != MyUserId) return Forbid();

        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == cm.OrderId);
        if (wo == null || wo.Status != "Open") return BadRequest(new { message = "دستور بسته شده است." });

        var text = (dto.Text ?? "").Trim();
        if (text.Length == 0) return BadRequest(new { message = "متن کامنت خالی است." });
        if (text.Length > 2000) return BadRequest(new { message = "متن کامنت حداکثر ۲۰۰۰ حرف است." });

        cm.Text = text;
        cm.EditedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workorders");
        return Ok(new { cm.Id, cm.Text, cm.EditedAt });
    }

    /// <summary>حذف نرم کامنت — نویسنده یا دستوردهنده؛ متن پاک می‌شود اما جای آن در رشته می‌ماند.</summary>
    [HttpDelete("comments/{commentId:int}")]
    public async Task<IActionResult> DeleteComment(int commentId)
    {
        var cm = await _db.WorkOrderComments.FindAsync(commentId);
        if (cm == null || cm.IsDeleted) return NotFound();

        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == cm.OrderId);
        if (wo == null) return NotFound();
        if (cm.AuthorUserId != MyUserId && wo.OwnerUserId != MyUserId) return Forbid();

        cm.IsDeleted = true;
        cm.Text = "";
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workorders");
        return Ok();
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
                new("برچسب‌ها", Services.Export.ExportValueKind.Text, 70) { Wrap = true },
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
        if (!string.IsNullOrWhiteSpace(filter.Tag)) spec.Meta.Add(new("برچسب", filter.Tag));

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
                string.Join("، ", TagsToList(w.Tags)),
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
    public async Task<IActionResult> Logs(int id)
    {
        if (!await CanSeeOrderAsync(id)) return Forbid();
        return Ok(await _db.WorkOrderLogs.Where(l => l.OrderId == id).OrderBy(l => l.Id)
            .Select(l => new { l.Id, l.ActorName, l.Action, l.Text, l.CreatedAt }).ToListAsync());
    }

    // ================== پیوست‌ها (بند ۴) ==================
    [HttpGet("{id:int}/attachments")]
    public async Task<IActionResult> Attachments(int id)
    {
        if (!await CanSeeOrderAsync(id)) return Forbid();
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
        if (!await CanSeeOrderAsync(id)) return Forbid();
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (file == null || file.Length == 0) return BadRequest(new { message = "فایلی انتخاب نشده است." });
        if (file.Length > 10 * 1024 * 1024) return BadRequest(new { message = "حداکثر حجم فایل ۱۰ مگابایت است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        var relPath = await _store.SaveAsync("itassets/workorders", id, ms, file.FileName);
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
        if (att == null || !await _db.WorkOrders.AnyAsync(w => w.Id == att.OrderId)) return NotFound();
        var bytes = _store.ReadBytes(att.FilePath) ?? (att.Data is { Length: > 0 } ? att.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });
        return File(bytes, att.ContentType, att.FileName);
    }
}
