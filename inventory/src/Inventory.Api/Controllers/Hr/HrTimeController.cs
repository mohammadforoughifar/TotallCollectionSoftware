using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>API تکمیلی حضوروغیاب و زمان‌بندی: قوانین، موجودی، تصویب چندمرحله‌ای، اضافه‌کاری، روستِر، دستگاه</summary>
[ApiController]
[Route("api/hr-time")]
[Authorize]
public class HrTimeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly HrTimeService _svc;

    public HrTimeController(AppDbContext db, HrTimeService svc) { _db = db; _svc = svc; }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyName => User.FindFirstValue(ClaimTypes.Name) ?? "";

    private bool? _isHr;
    private async Task<bool> IsHrAsync()
    {
        if (_isHr.HasValue) return _isHr.Value;
        if (User.IsInRole("Admin")) { _isHr = true; return true; }
        if (User.HasClaim("permission", "LeaveRequests.Approve")) { _isHr = true; return true; }
        _isHr = await _db.UserRoles.Where(ur => ur.UserId == MyUserId)
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, pm => pm.Id, (rp, pm) => pm)
            .AnyAsync(pm => pm.Module == "LeaveRequests" && pm.Action == "Approve");
        return _isHr.Value;
    }

    // ================= قوانین =================

    [HttpGet("rules")]
    public async Task<IActionResult> Rules()
    {
        if (!await IsHrAsync()) return Forbid();
        return Ok(await _svc.GetRulesAsync());
    }

    [HttpPut("rules")]
    public async Task<IActionResult> SaveRules([FromBody] Dictionary<string, string> values)
    {
        if (!await IsHrAsync()) return Forbid();
        await _svc.SaveRulesAsync(values ?? new());
        return Ok(new { ok = true });
    }

    // ================= موجودی مرخصی =================

    [HttpGet("balances")]
    public async Task<IActionResult> Balances([FromQuery] int? userId, [FromQuery] int? year)
    {
        var uid = userId ?? MyUserId;
        if (uid != MyUserId && !await IsHrAsync()) return Forbid();
        var jy = year ?? HrTimeService.JalaliYear(DateTime.Now);
        return Ok(new { year = jy, items = await _svc.GetBalancesAsync(uid, jy) });
    }

    public class GrantInput { public int UserId { get; set; } public int Year { get; set; } public string Category { get; set; } = "Annual"; public double Days { get; set; } }

    [HttpPost("balances/grant")]
    public async Task<IActionResult> Grant([FromBody] GrantInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        if (input.Category is not ("Annual" or "Sick" or "Maternity" or "Unpaid"))
            return BadRequest(new { message = "دسته مرخصی نامعتبر است." });
        await _svc.SetGrantAsync(input.UserId, input.Year, input.Category, Math.Max(0, input.Days));
        return Ok(new { ok = true });
    }

    // ================= مراحل تصویب =================

    [HttpGet("steps/pending")]
    public async Task<IActionResult> PendingSteps([FromQuery] bool all = false)
    {
        var hr = await IsHrAsync();
        var q = _db.HrRequestSteps.AsNoTracking().Where(s => s.Status == "Pending");
        if (all && hr) { /* همه */ }
        else if (hr) q = q.Where(s => s.Role == "Hr" || s.ApproverUserId == MyUserId);
        else q = q.Where(s => s.ApproverUserId == MyUserId);
        var steps = await q.OrderBy(s => s.CreatedAt).Take(200).ToListAsync();
        // مرحله‌های درخواست‌هایی که از مسیر قبلی تعیین‌تکلیف شده‌اند، نمایش داده نشوند
        var pendLeaveIds = steps.Where(s => s.RequestType == "Leave").Select(s => s.RequestId).Distinct().ToList();
        if (pendLeaveIds.Count > 0)
        {
            var stillPend = await _db.LeaveRequests.AsNoTracking()
                .Where(l => pendLeaveIds.Contains(l.Id) && l.Status == "Pending")
                .Select(l => l.Id).ToListAsync();
            steps = steps.Where(x => x.RequestType != "Leave" || stillPend.Contains(x.RequestId)).ToList();
        }
        var leaveIds = steps.Where(s => s.RequestType == "Leave").Select(s => s.RequestId).Distinct().ToList();
        var otIds = steps.Where(s => s.RequestType == "Overtime").Select(s => s.RequestId).Distinct().ToList();
        var leaves = await _db.LeaveRequests.AsNoTracking().Where(l => leaveIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id);
        var ots = await _db.HrOvertimeRequests.AsNoTracking().Where(o => otIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id);
        return Ok(steps.Select(s =>
        {
            leaves.TryGetValue(s.RequestId, out var l);
            ots.TryGetValue(s.RequestId, out var o);
            var isLeave = s.RequestType == "Leave";
            return new
            {
                s.Id, s.RequestType, s.RequestId, s.StepNo, s.Role, s.ApproverName, s.CreatedAt,
                Number = isLeave ? l?.Number : o?.Number,
                Requester = isLeave ? l?.RequesterName : o?.RequesterName,
                Title = isLeave
                    ? l?.Type switch { "Hourly" => "مرخصی ساعتی", "HourlyMission" => "ماموریت ساعتی", "Mission" => "ماموریت", _ => "مرخصی روزانه" }
                    : "اضافه‌کاری",
                Detail = isLeave
                    ? $"{l?.StartDate:yyyy/MM/dd} تا {l?.EndDate:yyyy/MM/dd}" + (string.IsNullOrWhiteSpace(l?.Destination) ? "" : $" — {l.Destination}")
                    : $"{o?.WorkDate:yyyy/MM/dd} {o?.StartTime:hh\\:mm}–{o?.EndTime:hh\\:mm}",
            };
        }));
    }

    [HttpGet("steps")]
    public async Task<IActionResult> Timeline([FromQuery] string requestType, [FromQuery] int requestId)
    {
        var hr = await IsHrAsync();
        var steps = await _db.HrRequestSteps.AsNoTracking()
            .Where(s => s.RequestType == requestType && s.RequestId == requestId)
            .OrderBy(s => s.StepNo).ToListAsync();
        if (!hr)
        {
            var mine = requestType == "Overtime"
                ? await _db.HrOvertimeRequests.AnyAsync(o => o.Id == requestId && o.RequesterUserId == MyUserId)
                : await _db.LeaveRequests.AnyAsync(l => l.Id == requestId && l.RequesterUserId == MyUserId);
            var involved = steps.Any(s => s.ApproverUserId == MyUserId);
            if (!mine && !involved) return Forbid();
        }
        return Ok(steps);
    }

    public class DecideInput { public bool Approve { get; set; } public string? Note { get; set; } }

    [HttpPost("steps/{id:int}/decide")]
    public async Task<IActionResult> Decide(int id, [FromBody] DecideInput input)
    {
        var r = await _svc.DecideStepAsync(id, MyUserId, MyName, await IsHrAsync(), input?.Approve == true, input?.Note);
        if (!r.Ok) return BadRequest(new { message = r.Message });
        return Ok(new { ok = true, message = r.Message });
    }

    // ================= اضافه‌کاری =================

    [HttpGet("overtime")]
    public async Task<IActionResult> Overtime([FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? userId)
    {
        var hr = await IsHrAsync();
        var q = _db.HrOvertimeRequests.AsNoTracking().AsQueryable();
        if (!hr || userId == null) { if (!hr) q = q.Where(o => o.RequesterUserId == MyUserId); }
        else q = q.Where(o => o.RequesterUserId == userId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(o => o.Status == status);
        if (from != null) q = q.Where(o => o.WorkDate >= from.Value.Date);
        if (to != null) q = q.Where(o => o.WorkDate <= to.Value.Date);
        return Ok(await q.OrderByDescending(o => o.WorkDate).Take(500).ToListAsync());
    }

    public class OvertimeInput
    {
        public DateTime WorkDate { get; set; }
        public string StartTime { get; set; } = "";
        public string EndTime { get; set; } = "";
        public string Type { get; set; } = "Normal";
        public string? Reason { get; set; }
    }

    [HttpPost("overtime")]
    public async Task<IActionResult> CreateOvertime([FromBody] OvertimeInput input)
    {
        if (input.Type is not ("Normal" or "Holiday" or "Night"))
            return BadRequest(new { message = "نوع اضافه‌کاری نامعتبر است." });
        if (!TimeSpan.TryParse(input.StartTime, out var st) || !TimeSpan.TryParse(input.EndTime, out var en))
            return BadRequest(new { message = "قالب ساعت نامعتبر است (HH:mm)." });
        try
        {
            var o = await _svc.CreateOvertimeAsync(MyUserId, MyName, input.WorkDate, st, en, input.Type, input.Reason);
            return Ok(new { id = o.Id, number = o.Number });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("overtime/{id:int}")]
    public async Task<IActionResult> DeleteOvertime(int id)
    {
        var o = await _db.HrOvertimeRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return NotFound();
        if (o.RequesterUserId != MyUserId && !await IsHrAsync()) return Forbid();
        if (o.Status != "Pending") return BadRequest(new { message = "فقط درخواست در انتظار قابل حذف است." });
        _db.HrRequestSteps.RemoveRange(_db.HrRequestSteps.Where(s => s.RequestType == "Overtime" && s.RequestId == id));
        _db.HrOvertimeRequests.Remove(o);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================= روستِر =================

    [HttpGet("roster")]
    public async Task<IActionResult> Roster([FromQuery] int? userId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var uid = userId ?? MyUserId;
        if (uid != MyUserId && !await IsHrAsync()) return Forbid();
        var f = (from ?? DateTime.Today).Date; var t = (to ?? DateTime.Today.AddDays(30)).Date;
        var rows = await _db.HrShiftRosters.AsNoTracking()
            .Where(r => r.UserId == uid && r.Date >= f && r.Date <= t).OrderBy(r => r.Date).ToListAsync();
        var shifts = await _db.ShiftGroups.AsNoTracking().ToDictionaryAsync(s => s.Id);
        return Ok(rows.Select(r => new
        {
            r.Id, r.UserId, r.Date, r.ShiftGroupId,
            ShiftName = shifts.TryGetValue(r.ShiftGroupId, out var s) ? s.Name : null,
        }));
    }

    public class RosterItem { public int UserId { get; set; } public DateTime Date { get; set; } public int ShiftGroupId { get; set; } }

    [HttpPost("roster")]
    public async Task<IActionResult> SaveRoster([FromBody] List<RosterItem> items)
    {
        if (!await IsHrAsync()) return Forbid();
        if (items == null || items.Count == 0 || items.Count > 1000)
            return BadRequest(new { message = "فهرست نامعتبر است." });
        var shiftIds = items.Select(i => i.ShiftGroupId).Distinct().ToList();
        var ok = await _db.ShiftGroups.CountAsync(s => shiftIds.Contains(s.Id));
        if (ok != shiftIds.Count) return BadRequest(new { message = "یکی از شیفت‌ها معتبر نیست." });
        var n = 0;
        foreach (var it in items)
        {
            var d = it.Date.Date;
            var ex = await _db.HrShiftRosters.FirstOrDefaultAsync(r => r.UserId == it.UserId && r.Date == d);
            if (ex == null) { _db.HrShiftRosters.Add(new HrShiftRoster { UserId = it.UserId, Date = d, ShiftGroupId = it.ShiftGroupId }); n++; }
            else if (ex.ShiftGroupId != it.ShiftGroupId) { ex.ShiftGroupId = it.ShiftGroupId; n++; }
        }
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, count = n });
    }

    public class GenerateInput
    {
        public List<int> UserIds { get; set; } = new();
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<int> Pattern { get; set; } = new();
        public int StartOffset { get; set; }
    }

    [HttpPost("roster/generate")]
    public async Task<IActionResult> GenerateRoster([FromBody] GenerateInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try
        {
            var n = await _svc.GenerateRosterAsync(input.UserIds, input.From, input.To, input.Pattern, input.StartOffset);
            return Ok(new { ok = true, count = n });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("roster/{id:int}")]
    public async Task<IActionResult> DeleteRoster(int id)
    {
        if (!await IsHrAsync()) return Forbid();
        var r = await _db.HrShiftRosters.FindAsync(id);
        if (r == null) return NotFound();
        _db.HrShiftRosters.Remove(r);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================= دستگاه =================

    [HttpGet("device/mappings")]
    public async Task<IActionResult> Mappings()
    {
        if (!await IsHrAsync()) return Forbid();
        return Ok(await _db.HrDeviceUserMaps.AsNoTracking().OrderBy(m => m.UserCode).Take(1000).ToListAsync());
    }

    public class MapInput { public string? DeviceCode { get; set; } public string UserCode { get; set; } = ""; public int SystemUserId { get; set; } }

    [HttpPost("device/mappings")]
    public async Task<IActionResult> SaveMapping([FromBody] MapInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        if (string.IsNullOrWhiteSpace(input.UserCode)) return BadRequest(new { message = "کد پرسنلی دستگاه را وارد کنید." });
        if (!await _db.Users.AnyAsync(u => u.Id == input.SystemUserId)) return BadRequest(new { message = "کاربر معتبر نیست." });
        var ex = await _db.HrDeviceUserMaps.FirstOrDefaultAsync(m => m.UserCode == input.UserCode && (m.DeviceCode ?? "") == (input.DeviceCode ?? ""));
        if (ex == null) _db.HrDeviceUserMaps.Add(new HrDeviceUserMap { DeviceCode = input.DeviceCode, UserCode = input.UserCode.Trim(), SystemUserId = input.SystemUserId });
        else ex.SystemUserId = input.SystemUserId;
        // ترددهای قبلیِ بدون نگاشت را هم به‌روزرسانی کن
        var olds = await _db.HrDevicePunches.Where(p => p.UserCode == input.UserCode && p.MappedUserId == null
            && (input.DeviceCode == null || p.DeviceCode == input.DeviceCode)).ToListAsync();
        foreach (var p in olds) p.MappedUserId = input.SystemUserId;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("device/mappings/{id:int}")]
    public async Task<IActionResult> DeleteMapping(int id)
    {
        if (!await IsHrAsync()) return Forbid();
        var m = await _db.HrDeviceUserMaps.FindAsync(id);
        if (m == null) return NotFound();
        _db.HrDeviceUserMaps.Remove(m);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("device/upload")]
    public async Task<IActionResult> UploadPunches([FromForm] IFormFile file, [FromForm] string? deviceCode)
    {
        if (!await IsHrAsync()) return Forbid();
        if (file == null || file.Length == 0) return BadRequest(new { message = "فایل انتخاب نشده است." });
        deviceCode = string.IsNullOrWhiteSpace(deviceCode) ? Request.Query["deviceCode"].ToString() : deviceCode;
        if (string.IsNullOrWhiteSpace(deviceCode)) return BadRequest(new { message = "کد دستگاه را وارد کنید." });
        var name = file.FileName.ToLowerInvariant();
        if (!name.EndsWith(".xlsx") && !name.EndsWith(".csv") && !name.EndsWith(".txt"))
            return BadRequest(new { message = "فقط فایل Excel (.xlsx) یا CSV پذیرفته می‌شود." });
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        var r = await _svc.ImportPunchesAsync(ms, file.FileName, deviceCode.Trim());
        return Ok(new { ok = true, imported = r.Imported, skipped = r.Skipped, errors = r.Errors });
    }

    public class ApplyInput { public DateTime From { get; set; } public DateTime To { get; set; } }

    [HttpPost("device/apply")]
    public async Task<IActionResult> ApplyPunches([FromBody] ApplyInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        var r = await _svc.ApplyPunchesAsync(input.From, input.To);
        return Ok(new { ok = true, punches = r.Punches, records = r.Records, unmapped = r.Unmapped });
    }

    [HttpGet("device/punches")]
    public async Task<IActionResult> Punches([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] bool unmappedOnly = false, [FromQuery] bool unappliedOnly = false)
    {
        if (!await IsHrAsync()) return Forbid();
        var q = _db.HrDevicePunches.AsNoTracking().AsQueryable();
        if (from != null) q = q.Where(p => p.PunchTime >= from.Value.Date);
        if (to != null) q = q.Where(p => p.PunchTime < to.Value.Date.AddDays(1));
        if (unmappedOnly) q = q.Where(p => p.MappedUserId == null);
        if (unappliedOnly) q = q.Where(p => p.AppliedRecordId == null);
        return Ok(await q.OrderByDescending(p => p.PunchTime).Take(500).ToListAsync());
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users()
    {
        if (!await IsHrAsync()) return Forbid();
        var list = await _db.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.Username)
            .Select(u => new { u.Id, u.Username, FullName = string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : (u.FirstName + " " + u.LastName).Trim() })
            .Take(2000).ToListAsync();
        return Ok(list);
    }

    [HttpGet("manager")]
    public async Task<IActionResult> MyManager()
    {
        var (id, name) = await _svc.ResolveManagerAsync(MyUserId);
        return Ok(new { userId = id, name });
    }

    // ================= پیوند کاربر ورود به پرونده =================

    public class LinkInput { public int EmployeeId { get; set; } public int UserId { get; set; } }

    [HttpPost("user-links")]
    public async Task<IActionResult> LinkUser([FromBody] LinkInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try
        {
            await _svc.LinkUserAsync(input.EmployeeId, input.UserId);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("user-links")]
    public async Task<IActionResult> UserLinks()
    {
        if (!await IsHrAsync()) return Forbid();
        return Ok(await _db.HrUserLinks.AsNoTracking().OrderBy(x => x.Id).Take(2000).ToListAsync());
    }

    // ================= درخواست مرخصی/ماموریت از مسیر ماژول جدید =================

    public class LeaveInput
    {
        public string Type { get; set; } = "Daily";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public string? Destination { get; set; }
        public string? Reason { get; set; }
        public string? Category { get; set; }
        public string? MissionKind { get; set; }
    }

    [HttpPost("leaves")]
    public async Task<IActionResult> CreateLeave([FromBody] LeaveInput input)
    {
        if (input == null) return BadRequest(new { message = "داده‌ای ارسال نشده است." });
        var r = await _svc.CreateLeaveAsync(MyUserId, MyName, input.Type, input.StartDate, input.EndDate,
            input.StartTime, input.EndTime, input.Destination, input.Reason, input.Category, input.MissionKind);
        if (!r.Ok) return BadRequest(new { message = r.Message });
        return Ok(new { id = r.Id, number = r.Number });
    }

    [HttpGet("leaves/mine")]
    public async Task<IActionResult> MyLeaves()
    {
        var rows = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == MyUserId).OrderByDescending(l => l.Id).Take(200).ToListAsync();
        var ids = rows.Select(l => l.Id).ToList();
        var extras = ids.Count == 0 ? new Dictionary<int, HrLeaveExtra>()
            : await _db.HrLeaveExtras.AsNoTracking().Where(x => ids.Contains(x.LeaveRequestId))
                .ToDictionaryAsync(x => x.LeaveRequestId);
        return Ok(rows.Select(l => new
        {
            l.Id, l.Number, l.Type, l.StartDate, l.EndDate, l.Days, l.Hours,
            l.Destination, l.Reason, l.Status, l.ApprovedByName, l.ApprovedAt, l.CreatedAt,
            Category = extras.TryGetValue(l.Id, out var x) ? x.Category : "Annual",
            MissionKind = extras.TryGetValue(l.Id, out var x2) ? x2.MissionKind : null,
            AllowanceAmount = extras.TryGetValue(l.Id, out var x3) ? x3.AllowanceAmount : 0,
        }));
    }

    [HttpGet("leaves/pending")]
    public async Task<IActionResult> PendingLeaves()
    {
        if (!await IsHrAsync()) return Forbid();
        await _svc.EnsureStepsForPendingAsync();
        var rows = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.Status == "Pending").OrderBy(l => l.CreatedAt).Take(300).ToListAsync();
        var ids = rows.Select(l => l.Id).ToList();
        var extras = ids.Count == 0 ? new Dictionary<int, HrLeaveExtra>()
            : await _db.HrLeaveExtras.AsNoTracking().Where(x => ids.Contains(x.LeaveRequestId))
                .ToDictionaryAsync(x => x.LeaveRequestId);
        var steped = await _db.HrRequestSteps.AsNoTracking()
            .Where(x => x.RequestType == "Leave" && ids.Contains(x.RequestId) && x.Status == "Pending")
            .GroupBy(x => x.RequestId).ToDictionaryAsync(g => g.Key, g => g.Min(x => x.StepNo));
        return Ok(rows.Select(l => new
        {
            l.Id, l.Number, l.Type, l.RequesterName, l.StartDate, l.EndDate, l.Days, l.Hours,
            l.Destination, l.Reason, l.CreatedAt,
            Category = extras.TryGetValue(l.Id, out var x) ? x.Category : "Annual",
            MissionKind = extras.TryGetValue(l.Id, out var x2) ? x2.MissionKind : null,
            AllowanceAmount = extras.TryGetValue(l.Id, out var x3) ? x3.AllowanceAmount : 0,
            CurrentStep = steped.TryGetValue(l.Id, out var n) ? n : 0,
        }));
    }

    public class ExtraInput { public string? Category { get; set; } public string? MissionKind { get; set; } }

    [HttpPost("leaves/{id:int}/extra")]
    public async Task<IActionResult> SaveLeaveExtra(int id, [FromBody] ExtraInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        var req = await _db.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        if (req == null) return NotFound();
        var ex = await _db.HrLeaveExtras.FirstOrDefaultAsync(x => x.LeaveRequestId == id);
        if (ex == null) { ex = new HrLeaveExtra { LeaveRequestId = id }; _db.HrLeaveExtras.Add(ex); }
        if (input?.Category is "Annual" or "Sick" or "Maternity" or "Unpaid") ex.Category = input.Category;
        if (req.Type is "Mission" or "HourlyMission")
        {
            ex.MissionKind = input?.MissionKind == "Outer" ? "Outer" : "Inner";
            var rules = await _svc.GetRulesAsync();
            var perDay = rules.TryGetValue(ex.MissionKind == "Outer" ? "mission.allowance.outer" : "mission.allowance.inner", out var av)
                && decimal.TryParse(av, out var ad) ? ad : 0;
            var mDays = req.Type == "HourlyMission" ? req.Hours / 8.0 : Math.Max(req.Days, 1);
            ex.AllowanceAmount = perDay * (decimal)Math.Max(mDays, 0);
        }
        else { ex.MissionKind = null; ex.AllowanceAmount = 0; }
        ex.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, allowanceAmount = ex.AllowanceAmount });
    }

    public class GpsInput { public DateTime Date { get; set; } public bool IsEnter { get; set; } public double Lat { get; set; } public double Lng { get; set; } }

    /// <summary>ثبت مختصات ورود/خروج موبایل در جدول مجزای ماژول</summary>
    [HttpPost("clock/gps")]
    public async Task<IActionResult> SaveGps([FromBody] GpsInput input)
    {
        if (input == null) return BadRequest(new { message = "داده‌ای ارسال نشده است." });
        await _svc.SaveGpsAsync(MyUserId, input.Date == default ? DateTime.Today : input.Date, input.IsEnter, input.Lat, input.Lng);
        return Ok(new { ok = true });
    }
}
