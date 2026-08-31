using System.Security.Claims;
using System.Text;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Inventory.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// حضور و غیاب پرسنل — ورود/خروج روزانه + مدیریت شیفت‌ها + گزارش کسری کار
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private const string Module = "Attendance";

    /// <summary>ساعت کاری استاندارد روزانه (دقیقه) — برای محاسبه کسری روزانه بدون شیفت</summary>
    private const int StandardWorkdayMinutes = 8 * 60;

    /// <summary>حداکثر تعداد جفت ورود/خروج در یک روز</summary>
    private const int MaxDailyPairs = 5;

    private readonly AppDbContext _db;
    private readonly INotifyService _notify;

    public AttendanceController(AppDbContext db, INotifyService notify, AttendanceRecalcService recalc)
    {
        _db = db; _notify = notify; _recalc = recalc;
    }

    private readonly AttendanceRecalcService _recalc;

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyName => User.FindFirstValue(ClaimTypes.Name) ?? "";
    private string? MyIp => HttpContext?.Connection?.RemoteIpAddress?.ToString();

    /// <summary>بررسی دسترسی RBAC (سازگاری با کاربران قدیمی بدون نقش RBAC).</summary>
    private async Task<bool> HasAsync(string action)
    {
        var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId);
        if (!hasRoles)
        {
            var legacy = User.FindFirstValue(ClaimTypes.Role);
            if (legacy == "Admin") return true;
            if (legacy is "Operator" or "Accountant") return action is "SelfCheckin";
            return action is "SelfCheckin" && legacy == "Referrer";
        }
        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId)
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == Module && p.Action == action);
    }

    private bool? _isAdmin;
    private async Task<bool> IsAdminAsync()
    {
        if (_isAdmin.HasValue) return _isAdmin.Value;
        if (User.IsInRole("Admin")) { _isAdmin = true; return true; }
        _isAdmin = await HasAsync("ManageShifts") || await HasAsync("ViewAll") || await HasAsync("Report");
        return _isAdmin.Value;
    }

    // ================== شیفت‌ها ==================
    [HttpGet("shifts")]
    public async Task<IActionResult> GetShifts() => Ok((await _db.ShiftGroups.ToListAsync()).OrderBy(s => s.StartTime).ToList());

    [HttpPost("shifts")]
    public async Task<IActionResult> SaveShift([FromBody] ShiftInput input)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        if (string.IsNullOrWhiteSpace(input.Name)) return BadRequest(new { message = "نام شیفت الزامی است." });

        if (!TimeSpan.TryParse(input.StartTime, out var st)) return BadRequest(new { message = "ساعت شروع نامعتبر است." });
        if (!TimeSpan.TryParse(input.EndTime, out var et)) return BadRequest(new { message = "ساعت پایان نامعتبر است." });

        ShiftGroup sg;
        if (input.Id > 0)
        {
            sg = await _db.ShiftGroups.FindAsync(input.Id);
            if (sg == null) return NotFound(new { message = "شیفت یافت نشد." });
        }
        else
        {
            sg = new ShiftGroup();
            _db.ShiftGroups.Add(sg);
        }
        sg.Name = input.Name.Trim();
        sg.Description = input.Description;
        sg.StartTime = st;
        sg.EndTime = et;
        sg.GraceMinutes = Math.Max(0, input.GraceMinutes);
        sg.IncludeFriday = input.IncludeFriday;
        sg.IsActive = input.IsActive;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { id = sg.Id });
    }

    [HttpDelete("shifts/{id:int}")]
    public async Task<IActionResult> DeleteShift(int id)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        var sg = await _db.ShiftGroups.FindAsync(id);
        if (sg == null) return NotFound();
        if (await _db.Users.AnyAsync(u => u.ShiftGroupId == id) || await _db.AttendanceRecords.AnyAsync(a => a.ShiftGroupId == id))
            return BadRequest(new { message = "این شیفت به پرسنل یا رکوردی متصل است و قابل حذف نیست." });
        _db.ShiftGroups.Remove(sg);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true });
    }

    public class ShiftInput
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string StartTime { get; set; } = "08:00";
        public string EndTime { get; set; } = "16:30";
        public int GraceMinutes { get; set; } = 10;
        public bool IncludeFriday { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // ================== تخصیص شیفت به پرسنل ==================
    [HttpPost("assign-shift")]
    public async Task<IActionResult> AssignShift([FromBody] AssignShiftInput input)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        var user = await _db.Users.FindAsync(input.UserId);
        if (user == null) return NotFound(new { message = "کاربر یافت نشد." });
        if (input.ShiftGroupId.HasValue && !await _db.ShiftGroups.AnyAsync(s => s.Id == input.ShiftGroupId))
            return BadRequest(new { message = "شیفت معتبر نیست." });
        user.ShiftGroupId = input.ShiftGroupId;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true });
    }
    public class AssignShiftInput { public int UserId { get; set; } public int? ShiftGroupId { get; set; } }

    // ================== لیست پرسنل ==================
    [HttpGet("personnel")]
    public async Task<IActionResult> GetPersonnel()
    {
        var canManage = await HasAsync("ManageShifts") || User.IsInRole("Admin");
        var canView = await IsAdminAsync();
        if (!canManage && !canView) return Forbid();
        var list = await _db.Users.Where(u => u.IsActive).OrderBy(u => u.Username).ToListAsync();
        var shifts = await _db.ShiftGroups.ToDictionaryAsync(s => s.Id);
        return Ok(list.Select(u => new
        {
            u.Id,
            u.Username,
            FullName = string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : $"{u.FirstName} {u.LastName}".Trim(),
            u.ShiftGroupId,
            ShiftName = u.ShiftGroupId.HasValue && shifts.TryGetValue(u.ShiftGroupId.Value, out var s) ? s.Name : null,
        }));
    }

    // ================== شیفت پیش‌فرض کاربر جاری ==================
    [HttpGet("my-shift")]
    public async Task<IActionResult> MyShift()
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        var user = await _db.Users.Include(u => u.ShiftGroup).FirstOrDefaultAsync(u => u.Id == MyUserId);
        if (user?.ShiftGroup == null) return Ok(null);
        var sg = user.ShiftGroup;
        return Ok(new { sg.Id, sg.Name, sg.Description, sg.StartTime, sg.EndTime, sg.GraceMinutes, sg.IncludeFriday });
    }

    // ================== کمک‌محاسبه کسری کار ==================
    private static int ScheduledMinutes(ShiftGroup? sg)
    {
        if (sg == null) return StandardWorkdayMinutes;
        var m = (int)(sg.EndTime - sg.StartTime).TotalMinutes;
        return m > 0 ? m : StandardWorkdayMinutes;
    }

    /// <summary>
    /// محاسبه و بروزرسانی فیلدهای محاسبه‌شده رکورد (کارکرد، تاخیر، تعجیل، کسری).
    /// hasDailyLeave = مرخصی «روزانه» تاییدشده برای کل روز (مرخصی ساعتی از طریق CoveredGapMinutes کسر می‌شود).
    /// </summary>
    private static void RecalcRecord(AttendanceRecord rec, ShiftGroup? sg, bool hasDailyLeave)
    {
        int scheduled = ScheduledMinutes(sg);

        // مرخصی روزانه تاییدشده دارد → کسری صفر و وضعیت روز مرخصی است
        if (hasDailyLeave)
        {
            rec.DeficitMinutes = 0;
            if (string.IsNullOrEmpty(rec.FinalStatus) || rec.FinalStatus == "Present")
                rec.FinalStatus = "LeaveDay";
            return;
        }

        // کسری = موظفی - کارکرد - غیبتِ پوشش‌شده با مرخصی/ماموریت ساعتی تاییدشده
        int deficit = scheduled - rec.WorkMinutes - rec.CoveredGapMinutes;
        rec.DeficitMinutes = Math.Max(0, deficit);

        // تعیین FinalStatus درصورتی که به طور صریح تنظیم نشده
        if (string.IsNullOrEmpty(rec.FinalStatus))
        {
            if (!rec.EnterAt.HasValue && !rec.ExitAt.HasValue)
                rec.FinalStatus = "Absent";
            else
                rec.FinalStatus = "Present";
        }
    }

    /// <summary>آیا در این روز مرخصی تاییدشده (روزانه/ساعتی) دارد؟ (برای نمایش «مرخصی دارید»)</summary>
    private async Task<bool> HasApprovedLeaveAsync(int userId, DateTime date)
    {
        var leaves = await _db.LeaveRequests
            .Where(l => l.RequesterUserId == userId && l.Status == "Approved"
                        && (l.Type == "Daily" || l.Type == "Hourly"))
            .ToListAsync();
        foreach (var l in leaves)
        {
            if (l.Type == "Daily" && date >= l.StartDate.Date && date <= l.EndDate.Date) return true;
            if (l.Type == "Hourly" && l.StartDate.Date == date) return true;
        }
        return false;
    }

    /// <summary>آیا در این روز مرخصی «روزانه» تاییدشده دارد؟ (وضعیت کل روز = مرخصی)</summary>
    private async Task<bool> HasDailyLeaveAsync(int userId, DateTime date)
    {
        return await _db.LeaveRequests
            .AnyAsync(l => l.RequesterUserId == userId && l.Status == "Approved" && l.Type == "Daily"
                           && l.StartDate.Date <= date.Date && l.EndDate.Date >= date.Date);
    }

    /// <summary>درخواست‌های مرخصی/ماموریت ساعتی تاییدشده‌ی کاربر برای آن روز (کاندیداهای پوشش بازه‌ی غیبت).</summary>
    private async Task<List<LeaveRequest>> HourlyCoversAsync(int userId, DateTime date)
    {
        return await _db.LeaveRequests
            .Where(l => l.RequesterUserId == userId
                        && l.Status == "Approved"
                        && (l.Type == "Hourly" || l.Type == "HourlyMission")
                        && l.StartDate == date.Date
                        && l.StartTime != null
                        && l.EndTime != null)
            .ToListAsync();
    }

    /// <summary>درخواستی پیدا می‌کند که کل بازه‌ی [gapStart, gapEnd] را پوشش دهد (وگرنه null).</summary>
    private static LeaveRequest? FindCoveringLeave(List<LeaveRequest> covers, DateTime gapStart, DateTime gapEnd)
    {
        var gs = gapStart.TimeOfDay;
        var ge = gapEnd.TimeOfDay;
        return covers.FirstOrDefault(l => l.StartTime <= gs && l.EndTime >= ge);
    }

    /// <summary>
    /// بازسازی رکورد تجمیعی روز از روی بازه‌های ورود/خروج (کارکرد، غیبت پوشش‌شده، تاخیر، تعجیل، کسری).
    /// </summary>
    private async Task AggregateAsync(AttendanceRecord rec, List<AttendanceSegment> segs, ShiftGroup? sg, DateTime asOf)
    {
        segs = segs.Where(s => s.EnterAt.HasValue).OrderBy(s => s.Seq).ToList();

        rec.HasApprovedLeave = await HasApprovedLeaveAsync(rec.UserId, rec.WorkDate);
        bool hasDaily = await HasDailyLeaveAsync(rec.UserId, rec.WorkDate);

        if (segs.Count == 0)
        {
            rec.EnterAt = null;
            rec.ExitAt = null;
            rec.WorkMinutes = 0;
            rec.CoveredGapMinutes = 0;
            rec.LateMinutes = 0;
            rec.EarlyLeaveMinutes = 0;
            rec.EnterStatus = null;
            RecalcRecord(rec, sg, hasDaily);
            return;
        }

        rec.EnterAt = segs.First().EnterAt;
        rec.ExitAt = segs.Last().ExitAt;
        rec.LateMinutes = segs.Sum(s => s.LateMinutes);
        rec.EnterStatus = segs.First().EnterStatus;
        rec.Note = segs.LastOrDefault(s => s.ExitAt.HasValue && !string.IsNullOrWhiteSpace(s.Note))?.Note;

        // کارکرد = مجموع بازه‌های بسته‌شده + بازه‌ی بازِ آخر تا «اکنون»
        int work = 0;
        foreach (var s in segs)
            if (s.EnterAt.HasValue && s.ExitAt.HasValue)
                work += Math.Max(0, (int)(s.ExitAt.Value - s.EnterAt.Value).TotalMinutes);
        var lastOpen = segs.Last();
        if (!lastOpen.ExitAt.HasValue)
            work += Math.Max(0, (int)(asOf - lastOpen.EnterAt!.Value).TotalMinutes);
        rec.WorkMinutes = work;

        // غیبت پوشش‌شده: هر بازه‌ی [خروج، ورود بعدی] که پوشش دارد + خروج آخر تا پایان شیفت (یا اکنون)
        int covered = 0;
        var scheduledEnd = sg?.EndTime ?? new TimeSpan(16, 30, 0);
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            if (!s.ExitAt.HasValue) continue;
            if (i + 1 < segs.Count)
            {
                if (s.ExitCovered && segs[i + 1].EnterAt.HasValue)
                    covered += Math.Max(0, (int)(segs[i + 1].EnterAt!.Value - s.ExitAt.Value).TotalMinutes);
            }
            else
            {
                var gapEnd = rec.WorkDate.Add(scheduledEnd);
                if (gapEnd > asOf) gapEnd = asOf;
                if (s.ExitCovered)
                    covered += Math.Max(0, (int)(gapEnd - s.ExitAt.Value).TotalMinutes);
            }
        }

        // بخش دیررس بودن [شروع شیفت، اولین ورود] — اگر مرخصی/ماموریت ساعتی تاییدشده آن را پوشش داده باشد
        var covers = await HourlyCoversAsync(rec.UserId, rec.WorkDate);
        covered += AttendanceRecalcService.LateCoveredMinutes(sg, rec.WorkDate, segs.FirstOrDefault(s => s.EnterAt.HasValue)?.EnterAt, covers);

        rec.CoveredGapMinutes = covered;

        // خروج زودهنگام: آخرین خروجِ بسته، اگر قبل از پایان شیفت بوده
        rec.EarlyLeaveMinutes = 0;
        var lastClosed = segs.LastOrDefault(s => s.ExitAt.HasValue);
        if (lastClosed?.ExitAt.HasValue == true && sg != null)
        {
            var scheduledExit = rec.WorkDate.Add(sg.EndTime);
            var diff = (int)(scheduledExit - lastClosed.ExitAt.Value).TotalMinutes;
            if (diff > sg.GraceMinutes) rec.EarlyLeaveMinutes = diff;
        }

        if (string.IsNullOrEmpty(rec.FinalStatus))
            rec.FinalStatus = "Present";
        RecalcRecord(rec, sg, hasDaily);
    }

    /// <summary>
    /// بررسی پوشش بازه‌ی غیبتِ بازه‌ی داده‌شده و به‌روزرسانی فیلدهای مربوطه‌ی آن بازه.
    /// returns: آیا پوشش کامل دارد؟
    /// </summary>
    private async Task<bool> EvaluateGapAsync(AttendanceSegment seg, DateTime gapEnd)
    {
        if (!seg.ExitAt.HasValue) return false;
        var covers = await HourlyCoversAsync(seg.UserId, seg.WorkDate);
        var cover = FindCoveringLeave(covers, seg.ExitAt.Value, gapEnd);
        seg.ExitCovered = cover != null;
        seg.LinkedLeaveRequestId = cover?.Id;
        seg.LinkedLeaveNumber = cover?.Number;
        return cover != null;
    }

    /// <summary>پوشش موقت هنگام خروج: آیا درخواست تاییدشده‌ای «فعال» است که لحظه‌ی خروج را داخل خودش داشته باشد؟</summary>
    private async Task<bool> IsCoveredAtAsync(int userId, DateTime date, DateTime at)
    {
        var covers = await HourlyCoversAsync(userId, date);
        var t = at.TimeOfDay;
        return covers.Any(l => l.StartTime <= t && l.EndTime >= t);
    }

    // ================== ورود ==================
    [HttpPost("check-in")]
    public async Task<IActionResult> CheckIn([FromBody] CheckInInput? input)
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        var today = DateTime.Today;
        var now = DateTime.Now;
        var user = await _db.Users.Include(u => u.ShiftGroup).FirstOrDefaultAsync(u => u.Id == MyUserId);
        if (user == null) return Unauthorized();

        var rec = await _db.AttendanceRecords.Include(a => a.ShiftGroup)
            .FirstOrDefaultAsync(a => a.UserId == MyUserId && a.WorkDate == today);
        var segs = await _db.AttendanceSegments
            .Where(s => s.UserId == MyUserId && s.WorkDate == today)
            .OrderBy(s => s.Seq)
            .ToListAsync();

        var last = segs.LastOrDefault();
        if (last != null && !last.ExitAt.HasValue)
            return BadRequest(new { message = "شما در حال حاضر در محل حضور دارید. ابتدا خروج بزنید." });
        if (segs.Count >= MaxDailyPairs)
            return BadRequest(new { message = $"حداکثر تعداد ورود/خروج در روز ({Fa.Digits(MaxDailyPairs)} بار) تمام شده است." });

        ShiftGroup? shift = null;
        if (input?.ShiftGroupId is > 0)
            shift = await _db.ShiftGroups.FindAsync(input.ShiftGroupId) ?? user.ShiftGroup;
        else
            shift = rec?.ShiftGroup ?? user.ShiftGroup;

        var seq = segs.Count + 1;
        var seg = new AttendanceSegment
        {
            UserId = MyUserId,
            UserName = string.IsNullOrWhiteSpace(user.FirstName) ? user.Username : $"{user.FirstName} {user.LastName}".Trim(),
            WorkDate = today,
            Seq = seq,
            EnterAt = now,
            EnterIp = MyIp,
            Note = input?.Note
        };

        // تاخیر فقط برای اولین ورود روز محاسبه می‌شود
        if (seq == 1 && shift != null)
        {
            var scheduledEnter = today.Add(shift.StartTime);
            var diff = (int)(now - scheduledEnter).TotalMinutes;
            if (diff > shift.GraceMinutes)
            {
                seg.LateMinutes = diff - shift.GraceMinutes;
                seg.EnterStatus = "Late";
            }
            else
            {
                seg.EnterStatus = "OnTime";
            }
        }
        else
        {
            seg.EnterStatus = "Return";
        }

        // بازه‌ی غیبتِ قبل (خروج قبلی تا اکنون) — حالا که کامل شد، پوشش آن را قطعی می‌کنیم
        if (last != null && last.ExitAt.HasValue)
            await EvaluateGapAsync(last, now);

        if (rec == null)
        {
            rec = new AttendanceRecord
            {
                WorkDate = today,
                UserId = MyUserId,
                UserName = seg.UserName,
                ShiftGroupId = shift?.Id,
                FinalStatus = "Present",
            };
            _db.AttendanceRecords.Add(rec);
        }
        else
        {
            if (shift != null) rec.ShiftGroupId = shift.Id;
            rec.UpdatedAt = now;
        }

        _db.AttendanceSegments.Add(seg);
        segs.Add(seg);
        await AggregateAsync(rec, segs, shift, now);
        await _db.SaveChangesAsync();

        if (seq == 1 && seg.LateMinutes > 5)
        {
            try
            {
                var hrIds = await GetAttendanceManagerIds();
                await _notify.SendManyAsync(hrIds,
                    "تاخیر در ورود",
                    $"{rec.UserName} با {Fa.Digits(seg.LateMinutes)} دقیقه تاخیر در {now:HH:mm} وارد شد.",
                    rec.UserName, "حضور و غیاب", "/attendance-admin");
            }
            catch { }
        }
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true, record = Map(rec, segs) });
    }
    public class CheckInInput { public int? ShiftGroupId { get; set; } public string? Note { get; set; } }

    // ================== خروج ==================
    [HttpPost("check-out")]
    public async Task<IActionResult> CheckOut([FromBody] CheckOutInput? input)
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        var today = DateTime.Today;
        var now = DateTime.Now;

        var rec = await _db.AttendanceRecords.Include(a => a.ShiftGroup).FirstOrDefaultAsync(a => a.UserId == MyUserId && a.WorkDate == today);
        var segs = await _db.AttendanceSegments
            .Where(s => s.UserId == MyUserId && s.WorkDate == today)
            .OrderBy(s => s.Seq)
            .ToListAsync();

        var last = segs.LastOrDefault();
        if (rec == null || last == null || last.EnterAt == null)
            return BadRequest(new { message = "ابتدا ورود خود را ثبت کنید." });
        if (last.ExitAt.HasValue)
            return BadRequest(new { message = "شما در حال حاضر در محل حضور دارید. ابتدا ورود بزنید." });

        last.ExitAt = now;
        last.ExitIp = MyIp;
        if (!string.IsNullOrWhiteSpace(input?.Note)) last.Note = input.Note;

        // پوشش موقت: لحظه‌ی خروج باید داخل بازه‌ی درخواست تاییدشده باشد
        // (بررسی قطعی‌ی «پوشش کامل بازه» هنگام ورود مجدد یا پایان شیفت انجام می‌شود)
        last.ExitCovered = await IsCoveredAtAsync(MyUserId, today, now);
        last.LinkedLeaveRequestId = null;
        last.LinkedLeaveNumber = null;

        rec.UpdatedAt = now;
        await AggregateAsync(rec, segs, rec.ShiftGroup, now);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");

        var covered = last.ExitCovered;
        return Ok(new
        {
            ok = true,
            record = Map(rec, segs),
            covered,
            message = covered
                ? "خروج ثبت شد. بازه‌ی غیبت شما با درخواست تاییدشده پوشش دارد."
                : "خروج ثبت شد. توجه: اگر بازه‌ی غیبت با مرخصی/ماموریت ساعتی تاییدشده پوشش نداشته باشد، در گزارش به‌عنوان کسری حساب می‌شود."
        });
    }
    public class CheckOutInput { public string? Note { get; set; } }

    // ================== وضعیت امروز من ==================
    [HttpGet("my-today")]
    public async Task<IActionResult> MyToday()
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        var today = DateTime.Today;
        var rec = await _db.AttendanceRecords.Include(a => a.ShiftGroup)
            .FirstOrDefaultAsync(a => a.UserId == MyUserId && a.WorkDate == today);
        if (rec == null) return Ok(null);
        var segs = await _db.AttendanceSegments
            .Where(s => s.UserId == MyUserId && s.WorkDate == today)
            .OrderBy(s => s.Seq)
            .ToListAsync();
        // بازه‌ی بازِ آخر (اگر خروج زودهنگام داشته و هنوز نیامده باشد) را تا «اکنون» ارزیابی می‌کنیم
        var last = segs.LastOrDefault();
        if (last != null && last.ExitAt.HasValue)
            await EvaluateGapAsync(last, DateTime.Now);
        await AggregateAsync(rec, segs, rec.ShiftGroup, DateTime.Now);
        await _db.SaveChangesAsync();
        return Ok(Map(rec, segs));
    }

    // ================== خلاصه ماه من (برای کارتابل) ==================
    [HttpGet("my-summary")]
    public async Task<IActionResult> MySummary([FromQuery] int? jy, [FromQuery] int? jm)
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        int y, m;
        if (jy.HasValue && jm.HasValue) { y = jy.Value; m = jm.Value; }
        else { (y, m, _) = PersianDate.FromGregorian(DateTime.Now); }
        var from = PersianDate.ToGregorian(y, m, 1);
        var to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);

        var list = await _db.AttendanceRecords.Include(a => a.ShiftGroup)
            .Where(a => a.UserId == MyUserId && a.WorkDate >= from && a.WorkDate < to).ToListAsync();

        // محاسبه برای روزهایی که رکورد ندارند
        var recalc = false;
        var workdays = WorkdayCount(from, to);
        foreach (var r in list)
        {
            var h = await HasDailyLeaveAsync(MyUserId, r.WorkDate);
            var before = r.DeficitMinutes;
            RecalcRecord(r, r.ShiftGroup, h);
            if (r.DeficitMinutes != before) recalc = true;
        }
        // روزهای بدون رکورد ولی با مرخصی «روزانه» تاییدشده → ایجاد رکورد LeaveDay
        var allLeaves = await _db.LeaveRequests
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Approved" && l.Type == "Daily"
                        && l.StartDate < to && l.EndDate >= from).ToListAsync();
        foreach (var l in allLeaves)
        {
            var s = l.StartDate.Date < from ? from : l.StartDate.Date;
            var e = l.EndDate.Date >= to ? to.AddDays(-1) : l.EndDate.Date;
            for (var d = s; d <= e; d = d.AddDays(1))
            {
                if (d < from || d >= to) continue;
                if (list.Any(r => r.WorkDate == d)) continue;
                var r = new AttendanceRecord
                {
                    WorkDate = d, UserId = MyUserId, UserName = MyName, FinalStatus = "LeaveDay",
                    HasApprovedLeave = true, CreatedAt = DateTime.Now
                };
                _db.AttendanceRecords.Add(r);
                list.Add(r);
                recalc = true;
            }
        }
        if (recalc) await _db.SaveChangesAsync();

        var summary = new
        {
            Year = y,
            Month = m,
            Present = list.Count(x => x.FinalStatus == "Present"),
            Absent = list.Count(x => x.FinalStatus == "Absent"),
            Leave = list.Count(x => x.FinalStatus == "LeaveDay"),
            Late = list.Count(x => x.EnterStatus == "Late"),
            WorkMinutes = list.Sum(x => x.WorkMinutes),
            DeficitMinutes = list.Sum(x => x.DeficitMinutes),
            TotalLateMinutes = list.Sum(x => x.LateMinutes),
            Workdays = workdays
        };
        return Ok(summary);
    }

    // ================== تاریخچه من ==================
    [HttpGet("my-history")]
    public async Task<IActionResult> MyHistory([FromQuery] int? jy, [FromQuery] int? jm)
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        DateTime from, to; int y, m;
        if (jy.HasValue && jm.HasValue) { y = jy.Value; m = jm.Value; }
        else { (y, m, _) = PersianDate.FromGregorian(DateTime.Now); }
        from = PersianDate.ToGregorian(y, m, 1);
        to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);

        var list = await _db.AttendanceRecords.Include(a => a.ShiftGroup)
            .Where(a => a.UserId == MyUserId && a.WorkDate >= from && a.WorkDate < to)
            .OrderByDescending(a => a.WorkDate)
            .ToListAsync();
        var segs = await _db.AttendanceSegments
            .Where(s => s.UserId == MyUserId && s.WorkDate >= from && s.WorkDate < to)
            .OrderBy(s => s.Seq)
            .ToListAsync();
        var byDate = segs.GroupBy(s => s.WorkDate).ToDictionary(g => g.Key, g => g.ToList());
        return Ok(new { year = y, month = m, items = list.Select(r => Map(r, byDate.GetValueOrDefault(r.WorkDate))) });
    }

    // ================== روزهای دارای کسری + بازه‌های بدون پوشش (برای درخواست آسان مرخصی) ==================
    [HttpGet("my-deficit-ranges")]
    public async Task<IActionResult> MyDeficitRanges([FromQuery] int? jy, [FromQuery] int? jm)
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        int y, m;
        if (jy.HasValue && jm.HasValue) { y = jy.Value; m = jm.Value; }
        else { (y, m, _) = PersianDate.FromGregorian(DateTime.Now); }
        var from = PersianDate.ToGregorian(y, m, 1);
        var to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);

        var user = await _db.Users.Include(u => u.ShiftGroup).AsNoTracking().FirstOrDefaultAsync(u => u.Id == MyUserId);
        if (user == null) return Unauthorized();
        var sg = user.ShiftGroup;
        var shiftStart = sg?.StartTime ?? new TimeSpan(8, 0, 0);
        var shiftEndTs = sg?.EndTime ?? new TimeSpan(16, 30, 0);
        var scheduled = ScheduledMinutes(sg);
        var now = DateTime.Now;

        var recs = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.UserId == MyUserId && a.WorkDate >= from && a.WorkDate < to)
            .ToDictionaryAsync(a => a.WorkDate);
        var segs = await _db.AttendanceSegments.AsNoTracking()
            .Where(s => s.UserId == MyUserId && s.WorkDate >= from && s.WorkDate < to)
            .OrderBy(s => s.Seq)
            .ToListAsync();
        var covers = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Approved"
                        && (l.Type == "Hourly" || l.Type == "HourlyMission")
                        && l.StartDate >= from && l.StartDate < to
                        && l.StartTime != null && l.EndTime != null)
            .ToListAsync();
        var pendings = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Pending"
                        && (l.Type == "Hourly" || l.Type == "HourlyMission")
                        && l.StartDate >= from && l.StartDate < to
                        && l.StartTime != null && l.EndTime != null)
            .ToListAsync();

        var items = new List<object>();
        for (var d = from; d < to; d = d.AddDays(1))
        {
            if (sg != null && !sg.IncludeFriday && d.DayOfWeek == DayOfWeek.Friday) continue;

            recs.TryGetValue(d.Date, out var rec);
            var dSegs = segs.Where(s => s.WorkDate == d.Date).OrderBy(s => s.Seq).ToList();
            var dayCovers = covers.Where(l => l.StartDate.Date == d.Date).ToList();
            var dayPendings = pendings.Where(l => l.StartDate.Date == d.Date).ToList();

            // کسری روز: از رکورد (برای روزهای گذشته) یا محاسبه‌ی زنده برای امروز
            int deficit;
            if (rec != null) deficit = d.Date == DateTime.Today ? LiveDeficit(rec, dSegs, sg, now) : rec.DeficitMinutes;
            else deficit = dSegs.Count == 0 ? scheduled : 0;
            if (deficit <= 0) continue;

            var ranges = new List<object>();

            // ۱) روز کامل بدون حضور و بدون رکورد
            if (rec == null && dSegs.Count == 0)
            {
                var pending = dayPendings.FirstOrDefault(l =>
                    (l.EndTime!.Value - l.StartTime!.Value).TotalMinutes >= scheduled);
                ranges.Add(new { start = shiftStart, end = shiftEndTs, minutes = scheduled, kind = "Day", pendingNumber = pending?.Number });
                items.Add(new { date = d.Date, deficitMinutes = deficit, ranges });
                continue;
            }

            // ۲) بازه‌های غیبتِ بدون پوشش (خروج ← ورود بعدی / خروج آخر ← پایان شیفت)
            for (var i = 0; i < dSegs.Count; i++)
            {
                var s = dSegs[i];
                if (s.ExitAt == null) continue;
                var gapEnd = (i + 1 < dSegs.Count && dSegs[i + 1].EnterAt.HasValue)
                    ? dSegs[i + 1].EnterAt!.Value
                    : d.Date.Add(shiftEndTs);
                var gapMin = (int)(gapEnd - s.ExitAt.Value).TotalMinutes;
                if (gapMin <= 0) continue;
                var gs = s.ExitAt.Value.TimeOfDay;
                var ge = gapEnd.TimeOfDay;
                var covered = dayCovers.Any(l => l.StartTime <= gs && l.EndTime >= ge);
                if (covered) continue;
                var pending = dayPendings.FirstOrDefault(l => l.StartTime <= gs && l.EndTime >= ge);
                ranges.Add(new { start = gs, end = ge, minutes = gapMin, kind = "Gap", pendingNumber = pending?.Number });
            }

            // ۳) بخش دیررس بودن [شروع شیفت، اولین ورود] — این هم با مرخصی/ماموریت ساعتی قابل پوشش است
            var firstEnter = dSegs.FirstOrDefault(s => s.EnterAt.HasValue)?.EnterAt;
            if (firstEnter.HasValue && sg != null)
            {
                var schedStart = d.Date.Add(shiftStart);
                if (firstEnter.Value > schedStart)
                {
                    var lateMin = (int)(firstEnter.Value - schedStart).TotalMinutes;
                    var gs2 = shiftStart;
                    var ge2 = firstEnter.Value.TimeOfDay;
                    var coveredLate = dayCovers.Any(l => l.StartTime <= gs2 && l.EndTime >= ge2);
                    if (!coveredLate)
                    {
                        var pending = dayPendings.FirstOrDefault(l => l.StartTime <= gs2 && l.EndTime >= ge2);
                        ranges.Add(new { start = gs2, end = ge2, minutes = lateMin, kind = "Late", pendingNumber = pending?.Number });
                    }
                }
            }

            if (ranges.Count == 0)
                ranges.Add(new { start = shiftStart, end = shiftEndTs, minutes = deficit, kind = "Day", pendingNumber = (string?)null });

            items.Add(new { date = d.Date, deficitMinutes = deficit, ranges });
        }

        return Ok(new { year = y, month = m, items });
    }

    /// <summary>کسری زنده برای امروز (با درنظر گرفتن زمان اکنون برای بازه‌ی باز).</summary>
    private static int LiveDeficit(AttendanceRecord rec, List<AttendanceSegment> segs, ShiftGroup? sg, DateTime now)
    {
        var scheduled = ScheduledMinutes(sg);
        var shiftEnd = rec.WorkDate.Add(sg?.EndTime ?? new TimeSpan(16, 30, 0));
        int work = 0;
        foreach (var s in segs)
        {
            if (!s.EnterAt.HasValue) continue;
            if (s.ExitAt.HasValue)
                work += Math.Max(0, (int)(s.ExitAt.Value - s.EnterAt.Value).TotalMinutes);
            else
            {
                var until = shiftEnd < now ? shiftEnd : now;
                work += Math.Max(0, (int)(until - s.EnterAt.Value).TotalMinutes);
            }
        }
        return Math.Max(0, scheduled - work - rec.CoveredGapMinutes);
    }

    // ================== کل تردد ماه (همه‌ی روزها با وضعیت رنگی) ==================
    [HttpGet("my-month-view")]
    public async Task<IActionResult> MyMonthView([FromQuery] int? jy, [FromQuery] int? jm)
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        int y, m;
        if (jy.HasValue && jm.HasValue) { y = jy.Value; m = jm.Value; }
        else { (y, m, _) = PersianDate.FromGregorian(DateTime.Now); }
        var from = PersianDate.ToGregorian(y, m, 1);
        var to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);
        var today = DateTime.Today;

        var user = await _db.Users.Include(u => u.ShiftGroup).AsNoTracking().FirstOrDefaultAsync(u => u.Id == MyUserId);
        if (user == null) return Unauthorized();
        var sg = user.ShiftGroup;
        var shiftStart = sg?.StartTime ?? new TimeSpan(8, 0, 0);
        var shiftEndTs = sg?.EndTime ?? new TimeSpan(16, 30, 0);
        var scheduled = ScheduledMinutes(sg);

        var recs = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.UserId == MyUserId && a.WorkDate >= from && a.WorkDate < to)
            .ToDictionaryAsync(a => a.WorkDate);
        var segs = await _db.AttendanceSegments.AsNoTracking()
            .Where(s => s.UserId == MyUserId && s.WorkDate >= from && s.WorkDate < to)
            .OrderBy(s => s.Seq).ToListAsync();
        var hourlyApproved = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Approved"
                        && (l.Type == "Hourly" || l.Type == "HourlyMission")
                        && l.StartDate >= from && l.StartDate < to
                        && l.StartTime != null && l.EndTime != null).ToListAsync();
        var dailyApproved = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Approved" && l.Type == "Daily"
                        && l.EndDate >= from && l.StartDate < to).ToListAsync();
        var missionApproved = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Approved" && l.Type == "Mission"
                        && l.EndDate >= from && l.StartDate < to).ToListAsync();
        var pendings = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Pending"
                        && (l.Type == "Hourly" || l.Type == "HourlyMission")
                        && l.StartDate >= from && l.StartDate < to
                        && l.StartTime != null && l.EndTime != null).ToListAsync();
        var holidays = (await _db.CompanyHolidays.AsNoTracking()
            .Where(h => h.HolidayDate >= from && h.HolidayDate < to).ToListAsync())
            .ToDictionary(h => h.HolidayDate);

        var items = new List<object>();
        for (var d = from; d < to; d = d.AddDays(1))
        {
            var date = d.Date;
            var isFuture = date > today;
            var isToday = date == today;
            recs.TryGetValue(date, out var rec);
            var dSegs = segs.Where(s => s.WorkDate == date).OrderBy(s => s.Seq).ToList();
            var dayHourly = hourlyApproved.Where(l => l.StartDate.Date == date).ToList();
            var dayPend = pendings.Where(l => l.StartDate.Date == date).ToList();

            string status;
            string leaveType = "None";
            string? holidayName = null;
            int deficit;

            // روزهای آینده → سفید
            if (isFuture)
            {
                status = "White";
                deficit = 0;
            }
            // امروز → زرد (در حال انجام)
            else if (isToday)
            {
                status = "Yellow";
                leaveType = dayHourly.Count > 0 ? (dayHourly[0].Type == "Mission" ? "HourlyMission" : dayHourly[0].Type) : "None";
                deficit = LiveDeficit(rec ?? new AttendanceRecord { WorkDate = date, CoveredGapMinutes = 0 }, dSegs, sg, DateTime.Now);
            }
            // روزهای گذشته
            else
            {
                if (holidays.TryGetValue(date, out var hol))
                {
                    status = "Green"; leaveType = "Holiday"; holidayName = hol.Name; deficit = 0;
                }
                else if (dailyApproved.Any(l => l.StartDate.Date <= date && l.EndDate.Date >= date))
                {
                    status = "Green"; leaveType = "Daily"; deficit = 0;
                }
                else if (missionApproved.Any(l => l.StartDate.Date <= date && l.EndDate.Date >= date))
                {
                    status = "Green"; leaveType = "Mission"; deficit = 0;
                }
                else
                {
                    // اگر روز گذشته ولی بازه‌ی آخرش باز مانده (خروج نزده)، ابتدا نهایی‌اش می‌کنیم
                    if (rec != null)
                    {
                        var lastSeg = dSegs.LastOrDefault();
                        if (lastSeg != null && lastSeg.EnterAt.HasValue && lastSeg.ExitAt == null)
                        {
                            await _recalc.RecalcDayAsync(MyUserId, date, DateTime.Now);
                            recs.TryGetValue(date, out var fresh);
                            rec = fresh;
                        }
                    }
                    deficit = rec?.DeficitMinutes ?? (dSegs.Count == 0 ? scheduled : 0);
                    status = deficit <= 0 ? "Green" : "Red";
                    if (dayHourly.Count > 0) leaveType = dayHourly[0].Type;
                    else if (dayPend.Count > 0) leaveType = dayPend[0].Type;
                }
            }

            // بازه‌های بدون پوشش (فقط برای روزهای قرمز — برای دکمه‌ی درخواست)
            var ranges = new List<object>();
            if (status == "Red")
            {
                if (rec == null && dSegs.Count == 0)
                {
                    var pending = dayPend.FirstOrDefault(l => (l.EndTime!.Value - l.StartTime!.Value).TotalMinutes >= scheduled);
                    ranges.Add(new { start = shiftStart, end = shiftEndTs, minutes = scheduled, kind = "Day", pendingNumber = pending?.Number });
                }
                else
                {
                    for (var i = 0; i < dSegs.Count; i++)
                    {
                        var s = dSegs[i];
                        if (s.ExitAt == null) continue;
                        var gapEnd = (i + 1 < dSegs.Count && dSegs[i + 1].EnterAt.HasValue)
                            ? dSegs[i + 1].EnterAt!.Value
                            : date.Add(shiftEndTs);
                        var gapMin = (int)(gapEnd - s.ExitAt.Value).TotalMinutes;
                        if (gapMin <= 0) continue;
                        var gs = s.ExitAt.Value.TimeOfDay;
                        var ge = gapEnd.TimeOfDay;
                        if (dayHourly.Any(l => l.StartTime <= gs && l.EndTime >= ge)) continue;
                        var pending = dayPend.FirstOrDefault(l => l.StartTime <= gs && l.EndTime >= ge);
                        ranges.Add(new { start = gs, end = ge, minutes = gapMin, kind = "Gap", pendingNumber = pending?.Number });
                    }
                    var firstEnter = dSegs.FirstOrDefault(s => s.EnterAt.HasValue)?.EnterAt;
                    if (firstEnter.HasValue && sg != null)
                    {
                        var schedStart = date.Add(shiftStart);
                        if (firstEnter.Value > schedStart)
                        {
                            var lateMin = (int)(firstEnter.Value - schedStart).TotalMinutes;
                            var gs2 = shiftStart; var ge2 = firstEnter.Value.TimeOfDay;
                            if (!dayHourly.Any(l => l.StartTime <= gs2 && l.EndTime >= ge2))
                            {
                                var pending = dayPend.FirstOrDefault(l => l.StartTime <= gs2 && l.EndTime >= ge2);
                                ranges.Add(new { start = gs2, end = ge2, minutes = lateMin, kind = "Late", pendingNumber = pending?.Number });
                            }
                        }
                    }
                    if (ranges.Count == 0)
                        ranges.Add(new { start = shiftStart, end = shiftEndTs, minutes = deficit, kind = "Day", pendingNumber = (string?)null });
                }
            }

            items.Add(new
            {
                date,
                isFuture,
                isToday,
                status,
                leaveType,
                holidayName,
                deficitMinutes = deficit,
                enterTimes = dSegs.Where(s => s.EnterAt.HasValue).Select(s => s.EnterAt!.Value.ToString("HH:mm")).ToList(),
                exitTimes = dSegs.Where(s => s.ExitAt.HasValue).Select(s => s.ExitAt!.Value.ToString("HH:mm")).ToList(),
                segments = dSegs.Where(s => s.EnterAt.HasValue).Select(MapSegment).ToList(),
                ranges
            });
        }

        return Ok(new { year = y, month = m, items });
    }

    // ================== گزارش ادمین (جزئیات روزانه) ==================
    [HttpGet("report")]
    public async Task<IActionResult> Report([FromQuery] int? jy, [FromQuery] int? jm, [FromQuery] int? userId)
    {
        if (!await IsAdminAsync()) return Forbid();
        int y, m; DateTime from, to;
        if (jy.HasValue && jm.HasValue) { y = jy.Value; m = jm.Value; }
        else { (y, m, _) = PersianDate.FromGregorian(DateTime.Now); }
        from = PersianDate.ToGregorian(y, m, 1);
        to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);

        var q = _db.AttendanceRecords.Include(a => a.ShiftGroup).Where(a => a.WorkDate >= from && a.WorkDate < to);
        if (userId.HasValue && userId > 0) q = q.Where(a => a.UserId == userId);
        var list = await q.OrderByDescending(a => a.WorkDate).ThenBy(a => a.UserId).ToListAsync();
        var segq = _db.AttendanceSegments.Where(s => s.WorkDate >= from && s.WorkDate < to);
        if (userId.HasValue && userId > 0) segq = segq.Where(s => s.UserId == userId);
        var segs = await segq.OrderBy(s => s.Seq).ToListAsync();
        var byDateUser = segs.GroupBy(s => (s.UserId, s.WorkDate)).ToDictionary(g => g.Key, g => g.ToList());
        return Ok(new { year = y, month = m, items = list.Select(r => Map(r, byDateUser.GetValueOrDefault((r.UserId, r.WorkDate)))) });
    }

    // ================== گزارش خلاصه ماهانه (به ازای هر کاربر) ==================
    [HttpGet("monthly-report")]
    public async Task<IActionResult> MonthlyReport([FromQuery] int? jy, [FromQuery] int? jm)
    {
        if (!await IsAdminAsync()) return Forbid();
        int y, m;
        if (jy.HasValue && jm.HasValue) { y = jy.Value; m = jm.Value; }
        else { (y, m, _) = PersianDate.FromGregorian(DateTime.Now); }
        var from = PersianDate.ToGregorian(y, m, 1);
        var to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);
        var daysInMonth = PersianDate.DaysInMonth(y, m);

        var users = await _db.Users.Where(u => u.IsActive).OrderBy(u => u.Username).ToListAsync();
        var recsAll = await _db.AttendanceRecords.Include(a => a.ShiftGroup)
            .Where(a => a.WorkDate >= from && a.WorkDate < to).ToListAsync();
        var leavesAll = await _db.LeaveRequests
            .Where(l => l.Status == "Approved" && (l.Type == "Daily" || l.Type == "Hourly") && l.StartDate < to && l.EndDate >= from).ToListAsync();
        var holidaysAll = await _db.CompanyHolidays.AsNoTracking()
            .Where(h => h.HolidayDate >= from && h.HolidayDate < to).ToListAsync();
        var holidayDates = holidaysAll.Select(h => h.HolidayDate).ToHashSet();

        var rows = new List<object>();
        foreach (var u in users)
        {
            var mine = recsAll.Where(r => r.UserId == u.Id).ToList();
            var uLeaves = leavesAll.Where(l => l.RequesterUserId == u.Id).ToList();

            // محاسبه تعداد روزهای مرخصی تاییدشده در ماه
            double leaveDays = 0;
            foreach (var l in uLeaves)
            {
                var s = l.StartDate.Date < from ? from : l.StartDate.Date;
                var e = l.EndDate.Date >= to ? to.AddDays(-1) : l.EndDate.Date;
                if (l.Type == "Daily") leaveDays += (e - s).TotalDays + 1;
                else if (l.Type == "Hourly") leaveDays += l.Hours / 8.0;
            }

            int present = mine.Count(r => r.FinalStatus == "Present");
            int leaveRec = mine.Count(r => r.FinalStatus == "LeaveDay");
            int absent = mine.Count(r => r.FinalStatus == "Absent");
            int late = mine.Count(r => r.EnterStatus == "Late");
            int workMin = mine.Sum(r => r.WorkMinutes);
            int lateMin = mine.Sum(r => r.LateMinutes);
            int earlyMin = mine.Sum(r => r.EarlyLeaveMinutes);

            // محاسبه کسری با درنظر گرفتن مرخصی
            int deficit = 0;
            int workdayCount = 0;
            var uShift = u.ShiftGroup;
            for (var d = from; d < to; d = d.AddDays(1))
            {
                if (!IsWorkday(d, uShift)) continue;
                workdayCount++;
                var rec = mine.FirstOrDefault(r => r.WorkDate == d);
                // تعطیلی شرکتی کل روز را از کسری مستثنا می‌کند
                if (holidayDates.Contains(d.Date)) continue;
                // فقط مرخصی «روزانه» کل روز را از کسری مستثنا می‌کند؛ مرخصی ساعتی از طریق غیبت پوشش‌شده در DeficitMinutes لحاظ شده است
                var dayHasDailyLeave = uLeaves.Any(l =>
                    l.Type == "Daily" && d >= l.StartDate.Date && d <= l.EndDate.Date);
                if (dayHasDailyLeave) continue;
                if (rec != null) deficit += rec.DeficitMinutes;
                else deficit += ScheduledMinutes(uShift); // روز کاری بدون ورود/خروج = کسری کامل
            }

            rows.Add(new
            {
                UserId = u.Id,
                UserName = string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : $"{u.FirstName} {u.LastName}".Trim(),
                ShiftName = uShift?.Name ?? "—",
                Workdays = workdayCount,
                Present = present,
                Absent = absent,
                LeaveDays = Math.Round(leaveDays + leaveRec, 2),
                LateDays = late,
                TotalLateMinutes = lateMin,
                TotalEarlyLeaveMinutes = earlyMin,
                WorkMinutes = workMin,
                DeficitMinutes = deficit,
                DeficitHours = Math.Round(deficit / 60.0, 2)
            });
        }

        return Ok(new
        {
            year = y,
            month = m,
            monthName = PersianDate.MonthName(m),
            days = daysInMonth,
            items = rows
        });
    }

    // ================== وضعیت امروز همه ==================
    [HttpGet("today-status")]
    public async Task<IActionResult> TodayStatus()
    {
        if (!await IsAdminAsync()) return Forbid();
        var today = DateTime.Today;
        var users = await _db.Users.Include(u => u.ShiftGroup).Where(u => u.IsActive).ToListAsync();
        var recs = await _db.AttendanceRecords.Include(a => a.ShiftGroup)
            .Where(a => a.WorkDate == today).ToDictionaryAsync(a => a.UserId);
        var allSegs = await _db.AttendanceSegments
            .Where(s => s.WorkDate == today)
            .OrderBy(s => s.Seq)
            .ToListAsync();

        var items = new List<object>();
        var now = DateTime.Now;
        foreach (var u in users)
        {
            recs.TryGetValue(u.Id, out var r);
            var hasLeave = await HasApprovedLeaveAsync(u.Id, today);
            var uSegs = allSegs.Where(s => s.UserId == u.Id).OrderBy(s => s.Seq).ToList();
            if (r != null)
            {
                // بازه‌ی غیبتِ بازِ آخر را تا «اکنون» ارزیابی و تجمیعی را به‌روز می‌کنیم
                var uLast = uSegs.LastOrDefault();
                if (uLast != null && uLast.ExitAt.HasValue)
                    await EvaluateGapAsync(uLast, now);
                await AggregateAsync(r, uSegs, r.ShiftGroup, now);
            }

            // تعیین وضعیت نهایی با درنظر گرفتن زمان فعلی (قبل از پایان شیفت، غیبت قطعی نیست)
            string? finalStatus = r?.FinalStatus;
            if (r == null)
            {
                if (hasLeave) finalStatus = "LeaveDay";
                else
                {
                    // اگر هنوز شیفت شروع نشده (یا شروع+تاخیر مجاز نگذشته)، وضعیت «در انتظار»
                    var shiftEnd = u.ShiftGroup?.EndTime ?? new TimeSpan(16, 30, 0);
                    var grace = u.ShiftGroup?.GraceMinutes ?? 10;
                    var shiftStart = u.ShiftGroup?.StartTime ?? new TimeSpan(8, 0, 0);
                    var deadline = today.Add(shiftStart).AddMinutes(grace + 30); // ۳۰ دقیقه فرصت اضافه
                    finalStatus = now < deadline ? "Pending" : "Absent";
                }
            }

            items.Add(new
            {
                UserId = u.Id,
                FullName = string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : $"{u.FirstName} {u.LastName}".Trim(),
                ShiftName = u.ShiftGroup?.Name ?? "—",
                ShiftStart = (TimeSpan?)u.ShiftGroup?.StartTime,
                ShiftEnd = (TimeSpan?)u.ShiftGroup?.EndTime,
                EnterAt = r?.EnterAt,
                ExitAt = r?.ExitAt,
                EnterStatus = r?.EnterStatus,
                LateMinutes = r?.LateMinutes ?? 0,
                EarlyLeaveMinutes = r?.EarlyLeaveMinutes ?? 0,
                WorkMinutes = r?.WorkMinutes ?? 0,
                DeficitMinutes = r?.DeficitMinutes ?? 0,
                CoveredGapMinutes = r?.CoveredGapMinutes ?? 0,
                FinalStatus = finalStatus,
                HasApprovedLeave = r?.HasApprovedLeave ?? hasLeave,
                Note = r?.Note,
                InOutCount = uSegs.Count(s => s.EnterAt.HasValue),
                IsInNow = uSegs.LastOrDefault() is { EnterAt: not null, ExitAt: null },
                UncoveredGaps = uSegs.Count(s => s.ExitAt.HasValue && !s.ExitCovered),
                Segments = uSegs.Where(s => s.EnterAt.HasValue).OrderBy(s => s.Seq).Select(MapSegment).ToList()
            });
        }
        items = items.OrderBy(x => ((dynamic)x).FinalStatus == "Present" ? 0 : 1)
                     .ThenBy(x => ((dynamic)x).FullName).ToList();

        var stats = new
        {
            Total = items.Count,
            Present = items.Count(x => ((dynamic)x).FinalStatus == "Present"),
            Absent = items.Count(x => ((dynamic)x).FinalStatus == "Absent"),
            Pending = items.Count(x => ((dynamic)x).FinalStatus == "Pending"),
            Late = items.Count(x => ((dynamic)x).EnterStatus == "Late"),
            OnLeave = items.Count(x => ((dynamic)x).HasApprovedLeave),
        };
        return Ok(new { stats, items });
    }

    // ================== اصلاح رکورد ==================
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AdminUpdateInput input)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        var rec = await _db.AttendanceRecords.Include(a => a.ShiftGroup).FirstOrDefaultAsync(a => a.Id == id);
        if (rec == null) return NotFound();
        rec.ShiftGroupId = input.ShiftGroupId == 0 ? null : input.ShiftGroupId;
        if (input.FinalStatus != null) rec.FinalStatus = input.FinalStatus;

        // بارگذاری شیفت جدید
        if (rec.ShiftGroupId.HasValue && (rec.ShiftGroup == null || rec.ShiftGroup.Id != rec.ShiftGroupId.Value))
            rec.ShiftGroup = await _db.ShiftGroups.FindAsync(rec.ShiftGroupId.Value);

        // اگر بازه‌های ورود/خروج وجود دارد، ویرایش را روی آن‌ها اعمال و تجمیعی را بازسازی می‌کنیم
        var segs = await _db.AttendanceSegments
            .Where(s => s.UserId == rec.UserId && s.WorkDate == rec.WorkDate)
            .OrderBy(s => s.Seq)
            .ToListAsync();
        if (segs.Count > 0)
        {
            var first = segs.First();
            if (input.ClearEnter == true) first.EnterAt = null;
            else if (input.EnterAt.HasValue) first.EnterAt = input.EnterAt.Value;
            var last = segs.Last();
            last.ExitAt = input.ExitAt;
            if (!string.IsNullOrWhiteSpace(input.Note)) last.Note = input.Note;
            // اگر بازه‌ی غیبتی بسته شده، پوشش آن را دوباره ارزیابی کن
            for (var i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                if (s.ExitAt == null) continue;
                var gapEnd = i + 1 < segs.Count ? segs[i + 1].EnterAt : null;
                if (gapEnd.HasValue)
                    await EvaluateGapAsync(s, gapEnd.Value);
            }
            await AggregateAsync(rec, segs, rec.ShiftGroup, DateTime.Now);
        }
        else
        {
            // رفتار قدیمی (رکورد بدون بازه)
            if (input.EnterAt.HasValue) rec.EnterAt = input.EnterAt.Value;
            if (input.ClearEnter == true) rec.EnterAt = null;
            rec.ExitAt = input.ExitAt;
            rec.Note = input.Note;
            if (rec.EnterAt.HasValue && rec.ExitAt.HasValue)
                rec.WorkMinutes = Math.Max(0, (int)(rec.ExitAt.Value - rec.EnterAt.Value).TotalMinutes);
            var hasDaily = await HasDailyLeaveAsync(rec.UserId, rec.WorkDate);
            rec.HasApprovedLeave = await HasApprovedLeaveAsync(rec.UserId, rec.WorkDate);
            RecalcRecord(rec, rec.ShiftGroup, hasDaily);
        }

        rec.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true });
    }
    public class AdminUpdateInput
    {
        public DateTime? EnterAt { get; set; }
        public bool? ClearEnter { get; set; }
        public DateTime? ExitAt { get; set; }
        public string? Note { get; set; }
        public int? ShiftGroupId { get; set; }
        public string? FinalStatus { get; set; }
    }

    // ================== مدیریت بازه‌های ورود/خروج (ادمین) ==================
    [HttpPut("segments/{id:int}")]
    public async Task<IActionResult> UpdateSegment(int id, [FromBody] SegmentUpdateInput input)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        var seg = await _db.AttendanceSegments.FirstOrDefaultAsync(s => s.Id == id);
        if (seg == null) return NotFound();

        if (input.ClearEnter == true) seg.EnterAt = null;
        else if (input.EnterAt.HasValue) seg.EnterAt = input.EnterAt.Value;
        seg.ExitAt = input.ExitAt;
        if (input.Note != null) seg.Note = input.Note;

        var rec = await _db.AttendanceRecords.Include(a => a.ShiftGroup).FirstOrDefaultAsync(a => a.UserId == seg.UserId && a.WorkDate == seg.WorkDate);
        if (rec != null)
        {
            var segs = await _db.AttendanceSegments
                .Where(s => s.UserId == seg.UserId && s.WorkDate == seg.WorkDate)
                .OrderBy(s => s.Seq).ToListAsync();
            for (var i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                if (s.ExitAt == null) continue;
                var gapEnd = i + 1 < segs.Count ? segs[i + 1].EnterAt : null;
                if (gapEnd.HasValue) await EvaluateGapAsync(s, gapEnd.Value);
            }
            await AggregateAsync(rec, segs, rec.ShiftGroup, DateTime.Now);
            rec.UpdatedAt = DateTime.Now;
        }
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true });
    }

    [HttpDelete("segments/{id:int}")]
    public async Task<IActionResult> DeleteSegment(int id)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        var seg = await _db.AttendanceSegments.FirstOrDefaultAsync(s => s.Id == id);
        if (seg == null) return NotFound();

        _db.AttendanceSegments.Remove(seg);
        // شماره‌گذاری مجدد بازه‌های باقی‌مانده
        var rest = await _db.AttendanceSegments
            .Where(s => s.UserId == seg.UserId && s.WorkDate == seg.WorkDate)
            .OrderBy(s => s.Seq).ToListAsync();
        for (var i = 0; i < rest.Count; i++) rest[i].Seq = i + 1;

        var rec = await _db.AttendanceRecords.Include(a => a.ShiftGroup).FirstOrDefaultAsync(a => a.UserId == seg.UserId && a.WorkDate == seg.WorkDate);
        if (rec != null)
        {
            for (var i = 0; i < rest.Count; i++)
            {
                var s = rest[i];
                if (s.ExitAt == null) continue;
                var gapEnd = i + 1 < rest.Count ? rest[i + 1].EnterAt : null;
                if (gapEnd.HasValue) await EvaluateGapAsync(s, gapEnd.Value);
            }
            await AggregateAsync(rec, rest, rec.ShiftGroup, DateTime.Now);
            rec.UpdatedAt = DateTime.Now;
        }
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true });
    }
    public class SegmentUpdateInput
    {
        public DateTime? EnterAt { get; set; }
        public bool? ClearEnter { get; set; }
        public DateTime? ExitAt { get; set; }
        public string? Note { get; set; }
    }

    // ================== خروجی اکسل/CSV گزارش ==================
    [HttpGet("monthly-report.csv")]
    public async Task<IActionResult> MonthlyReportCsv([FromQuery] int? jy, [FromQuery] int? jm)
    {
        if (!await IsAdminAsync()) return Forbid();
        var data = await MonthlyReport(jy, jm) as OkObjectResult;
        if (data?.Value == null) return BadRequest();
        var wrap = (dynamic)data.Value;
        int y = (int)wrap.year, m = (int)wrap.month;
        var items = (IEnumerable<dynamic>)wrap.items;

        var sb = new StringBuilder();
        sb.Append('\uFEFF'); // BOM برای فارسی در Excel
        sb.AppendLine("نام کاربر,شیفت,روزکاری,حاضر,غایب,مرخصی(روز),با تاخیر,تاخیر(دقیقه),تعجیل(دقیقه),کارکرد(دقیقه),کسری(دقیقه),کسری(ساعت)");
        foreach (var r in items)
        {
            sb.AppendJoin(',',
                r.UserName, r.ShiftName, r.Workdays, r.Present, r.Absent, r.LeaveDays,
                r.LateDays, r.TotalLateMinutes, r.TotalEarlyLeaveMinutes,
                r.WorkMinutes, r.DeficitMinutes, r.DeficitHours);
            sb.AppendLine();
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv; charset=utf-8", $"attendance-monthly-{y}-{m}.csv");
    }

    // ================== کمک‌ها ==================
    private async Task<List<int>> GetAttendanceManagerIds()
    {
        var rbac = await _db.UserRoles
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur.UserId, rp.PermissionId })
            .Join(_db.Permissions, x => x.PermissionId, p => p.Id, (x, p) => new { x.UserId, p.Module, p.Action })
            .Where(x => x.Module == Module && (x.Action == "ManageShifts" || x.Action == "ViewAll" || x.Action == "Report"))
            .Select(x => x.UserId).Distinct().ToListAsync();
        var legacyAdmins = await _db.Users.Where(u => u.Role == "Admin" && u.IsActive && !_db.UserRoles.Any(ur => ur.UserId == u.Id)).Select(u => u.Id).ToListAsync();
        return rbac.Concat(legacyAdmins).Distinct().ToList();
    }

    private static bool IsWorkday(DateTime date, ShiftGroup? sg)
    {
        if (sg != null && !sg.IncludeFriday && date.DayOfWeek == DayOfWeek.Friday) return false;
        return true;
    }

    private static int WorkdayCount(DateTime from, DateTime to)
    {
        int c = 0;
        for (var d = from; d < to; d = d.AddDays(1))
            if (d.DayOfWeek != DayOfWeek.Friday) c++;
        return c;
    }

    private static object Map(AttendanceRecord r, List<AttendanceSegment>? segs = null) => new
    {
        r.Id,
        r.WorkDate,
        r.UserId,
        r.UserName,
        r.ShiftGroupId,
        ShiftName = r.ShiftGroup?.Name,
        ShiftStart = r.ShiftGroup?.StartTime,
        ShiftEnd = r.ShiftGroup?.EndTime,
        r.EnterAt,
        r.ExitAt,
        r.EnterIp, r.ExitIp,
        r.EnterStatus,
        r.LateMinutes,
        r.EarlyLeaveMinutes,
        r.WorkMinutes,
        r.DeficitMinutes,
        r.CoveredGapMinutes,
        r.HasApprovedLeave,
        r.FinalStatus,
        r.Note,
        r.CreatedAt, r.UpdatedAt,
        InOutCount = segs?.Count(s => s.EnterAt.HasValue) ?? 0,
        Segments = segs?.Where(s => s.EnterAt.HasValue).OrderBy(s => s.Seq).Select(MapSegment).ToList() ?? new List<object>()
    };

    private static object MapSegment(AttendanceSegment s) => new
    {
        s.Id,
        s.Seq,
        s.EnterAt,
        s.ExitAt,
        s.EnterStatus,
        s.LateMinutes,
        s.ExitCovered,
        s.LinkedLeaveNumber,
        s.Note
    };
}
