using System.Globalization;
using ClosedXML.Excel;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// سرویس تکمیلی حضوروغیاب و زمان‌بندی: قوانین، موجودی مرخصی، تصویب چندمرحله‌ای،
/// اضافه‌کاری، روستِر شیفت چرخشی، ایمپورت دستگاه.
/// </summary>
public class HrTimeService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly AttendanceRecalcService _recalc;
    private static readonly PersianCalendar Pc = new();

    public HrTimeService(AppDbContext db, INotifyService notify, AttendanceRecalcService recalc)
    { _db = db; _notify = notify; _recalc = recalc; }

    public static int JalaliYear(DateTime d) => Pc.GetYear(d);
    public static double QuotaOf(LeaveRequest l) => l.Type == "Hourly" ? l.Hours / 8.0 : (l.Type == "Daily" ? l.Days : 0);

    // ================= قوانین =================

    public static readonly (string Key, string Value, string Title)[] DefaultRules =
    [
        ("night.start", "22:00", "شروع شب‌کاری"),
        ("night.end", "06:00", "پایان شب‌کاری"),
        ("ot.coef.normal", "1.4", "ضریب اضافه‌کاری عادی"),
        ("ot.coef.holiday", "1.4", "ضریب تعطیل‌کاری"),
        ("ot.coef.night", "1.35", "ضریب شب‌کاری"),
        ("leave.annual.grant", "24", "سهمیه سالانه استحقاقی (روز)"),
        ("leave.sick.grant", "90", "سقف سالانه استعلاجی (روز)"),
        ("leave.maternity.grant", "270", "سقف مرخصی زایمان (روز)"),
        ("leave.unpaid.grant", "9999", "سقف سالانه بدون حقوق (روز)"),
        ("mission.allowance.inner", "0", "حق ماموریت داخل شهر (ریال/روز)"),
        ("mission.allowance.outer", "1000000", "حق ماموریت خارج شهر (ریال/روز)"),
    ];

    public async Task<Dictionary<string, string>> GetRulesAsync()
    {
        var rows = await _db.HrTimeRules.AsNoTracking().ToListAsync();
        var map = rows.ToDictionary(r => r.Key, r => r.Value);
        var missing = DefaultRules.Where(d => !map.ContainsKey(d.Key)).ToList();
        if (missing.Count > 0)
        {
            foreach (var (k, v, t) in missing)
                _db.HrTimeRules.Add(new HrTimeRule { Key = k, Value = v, Title = t });
            await _db.SaveChangesAsync();
            foreach (var (k, v, _) in missing) map[k] = v;
        }
        return map;
    }

    public async Task SaveRulesAsync(Dictionary<string, string> values)
    {
        var rows = await _db.HrTimeRules.ToListAsync();
        foreach (var (k, v) in values)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            var row = rows.FirstOrDefault(r => r.Key == k.Trim());
            if (row == null)
            {
                var def = DefaultRules.FirstOrDefault(d => d.Key == k.Trim());
                _db.HrTimeRules.Add(new HrTimeRule { Key = k.Trim(), Value = (v ?? "").Trim(), Title = def.Title });
            }
            else row.Value = (v ?? "").Trim();
        }
        await _db.SaveChangesAsync();
    }

    public async Task<string> RuleAsync(string key, string fallback = "")
        => (await GetRulesAsync()).TryGetValue(key, out var v) ? v : fallback;

    // ================= موجودی مرخصی =================

    public record BalanceRow(string Category, double Granted, double Used, double Pending, double Remaining);

    public async Task<List<BalanceRow>> GetBalancesAsync(int userId, int year)
    {
        var rules = await GetRulesAsync();
        double G(string k, double fb) => rules.TryGetValue(k, out var v) && double.TryParse(v, out var d) ? d : fb;
        var grants = new Dictionary<string, double>
        {
            ["Annual"] = G("leave.annual.grant", 24),
            ["Sick"] = G("leave.sick.grant", 90),
            ["Maternity"] = G("leave.maternity.grant", 270),
            ["Unpaid"] = G("leave.unpaid.grant", 9999),
        };
        // ردیف‌های ذخیره‌شده (قابل ویرایش دستی توسط کارگزینی) بر پیش‌فرض قوانین اولویت دارند
        var saved = await _db.HrLeaveBalances.AsNoTracking()
            .Where(b => b.UserId == userId && b.Year == year).ToListAsync();
        foreach (var s in saved) grants[s.Category] = s.TotalDays;

        var reqs = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == userId && (l.Type == "Daily" || l.Type == "Hourly")
                        && (l.Status == "Approved" || l.Status == "Pending")).ToListAsync();
        var reqIds = reqs.Select(l => l.Id).ToList();
        var catMap = reqIds.Count == 0 ? new Dictionary<int, string>()
            : await _db.HrLeaveExtras.AsNoTracking().Where(x => reqIds.Contains(x.LeaveRequestId))
                .ToDictionaryAsync(x => x.LeaveRequestId, x => x.Category);
        string CatOf(LeaveRequest l) => catMap.TryGetValue(l.Id, out var c) && !string.IsNullOrWhiteSpace(c) ? c : "Annual";
        var cats = new[] { "Annual", "Sick", "Maternity", "Unpaid" };
        return cats.Select(c =>
        {
            var mine = reqs.Where(l => CatOf(l) == c && JalaliYear(l.StartDate) == year).ToList();
            var used = Math.Round(mine.Where(l => l.Status == "Approved").Sum(QuotaOf), 2);
            var pend = Math.Round(mine.Where(l => l.Status == "Pending").Sum(QuotaOf), 2);
            var g = grants[c];
            return new BalanceRow(c, g, used, pend, Math.Round(g - used - pend, 2));
        }).ToList();
    }

    public async Task SetGrantAsync(int userId, int year, string category, double days)
    {
        var row = await _db.HrLeaveBalances
            .FirstOrDefaultAsync(b => b.UserId == userId && b.Year == year && b.Category == category);
        if (row == null)
            _db.HrLeaveBalances.Add(new HrLeaveBalance { UserId = userId, Year = year, Category = category, TotalDays = days });
        else { row.TotalDays = days; row.UpdatedAt = DateTime.Now; }
        await _db.SaveChangesAsync();
    }

    // ================= تصویب چندمرحله‌ای =================

    /// <summary>پیوند کاربر ورود به پرونده پرسنلی (جایگزین ماژول برای اتصال کاربر-پرونده)</summary>
    public async Task LinkUserAsync(int employeeId, int userId)
    {
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == employeeId))
            throw new InvalidOperationException("پرونده پرسنلی پیدا نشد.");
        if (!await _db.Users.AnyAsync(u => u.Id == userId))
            throw new InvalidOperationException("کاربر پیدا نشد.");
        var olds = await _db.HrUserLinks.Where(x => x.EmployeeId == employeeId || x.UserId == userId).ToListAsync();
        _db.HrUserLinks.RemoveRange(olds);
        _db.HrUserLinks.Add(new HrUserLink { EmployeeId = employeeId, UserId = userId });
        await _db.SaveChangesAsync();
    }

    public async Task<int?> EmployeeIdOfUserAsync(int userId)
        => await _db.HrUserLinks.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => (int?)x.EmployeeId).FirstOrDefaultAsync();

    public async Task<int?> UserIdOfEmployeeAsync(int employeeId)
        => await _db.HrUserLinks.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .Select(x => (int?)x.UserId).FirstOrDefaultAsync();

    /// <summary>مدیر مستقیم از روی پرونده پرسنلی + پیوند کاربر — fallback: بدون مرحله مدیر</summary>
    public async Task<(int? UserId, string? Name)> ResolveManagerAsync(int userId)
    {
        var empId = await EmployeeIdOfUserAsync(userId);
        if (empId == null) return (null, null);
        var emp = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == empId && e.ManagerId != null);
        if (emp?.ManagerId == null) return (null, null);
        var mgr = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == emp.ManagerId);
        if (mgr == null) return (null, null);
        var mgrUser = await UserIdOfEmployeeAsync(mgr.Id);
        if (mgrUser == null || mgrUser == userId) return (null, null);
        return (mgrUser, $"{mgr.FirstName} {mgr.LastName}".Trim());
    }

    public async Task<List<HrRequestStep>> CreateStepsAsync(string requestType, int requestId, int requesterId)
    {
        var steps = new List<HrRequestStep>();
        var (mgrId, mgrName) = await ResolveManagerAsync(requesterId);
        if (mgrId != null)
            steps.Add(new HrRequestStep { RequestType = requestType, RequestId = requestId, StepNo = 1, Role = "Manager", ApproverUserId = mgrId, ApproverName = mgrName });
        steps.Add(new HrRequestStep { RequestType = requestType, RequestId = requestId, StepNo = steps.Count + 1, Role = "Hr" });
        _db.HrRequestSteps.AddRange(steps);
        await _db.SaveChangesAsync();
        return steps;
    }

    public async Task SkipStepsAsync(string requestType, int requestId, int byUserId, string byName)
    {
        var steps = await _db.HrRequestSteps
            .Where(s => s.RequestType == requestType && s.RequestId == requestId && s.Status == "Pending").ToListAsync();
        foreach (var s in steps)
        {
            s.Status = "Skipped"; s.DecidedByUserId = byUserId; s.DecidedByName = byName; s.DecidedAt = DateTime.Now;
            s.Note = "تعیین‌تکلیف مستقیم";
        }
        if (steps.Count > 0) await _db.SaveChangesAsync();
    }

    public record DecideResult(bool Ok, string Message);

    public async Task<DecideResult> DecideStepAsync(int stepId, int callerId, string callerName, bool callerIsHr, bool approve, string? note)
    {
        var step = await _db.HrRequestSteps.FirstOrDefaultAsync(s => s.Id == stepId);
        if (step == null) return new(false, "مرحله پیدا نشد.");
        if (step.Status != "Pending") return new(false, "این مرحله قبلاً بررسی شده است.");
        if (step.RequestType == "Leave")
        {
            var st = await _db.LeaveRequests.AsNoTracking().Where(l => l.Id == step.RequestId)
                .Select(l => l.Status).FirstOrDefaultAsync();
            if (st != null && st != "Pending")
            {
                await SkipStepsAsync(step.RequestType, step.RequestId, callerId, callerName);
                return new(false, "این درخواست قبلاً تعیین‌تکلیف شده است.");
            }
        }
        // ترتیب مراحل باید رعایت شود
        var earlierPending = await _db.HrRequestSteps.AnyAsync(s => s.RequestType == step.RequestType
            && s.RequestId == step.RequestId && s.Status == "Pending" && s.StepNo < step.StepNo);
        if (earlierPending) return new(false, "مرحله‌ی قبلی هنوز بررسی نشده است.");
        // دسترسی: مرحله مدیر = مدیر پرونده یا کارگزینی؛ مرحله کارگزینی = فقط کارگزینی
        if (step.Role == "Manager")
        {
            if (step.ApproverUserId != callerId && !callerIsHr)
                return new(false, "فقط مدیر مستقیم یا کارگزینی می‌تواند این مرحله را بررسی کند.");
        }
        else if (!callerIsHr) return new(false, "فقط کارگزینی می‌تواند این مرحله را بررسی کند.");

        step.Status = approve ? "Approved" : "Rejected";
        step.Note = note; step.DecidedByUserId = callerId; step.DecidedByName = callerName; step.DecidedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        if (!approve)
        {
            await SkipStepsAsync(step.RequestType, step.RequestId, callerId, callerName); // بقیه مراحل منتفی
            await FinalizeAsync(step.RequestType, step.RequestId, false, callerId, callerName, note);
            return new(true, "درخواست رد شد.");
        }
        var morePending = await _db.HrRequestSteps.AnyAsync(s => s.RequestType == step.RequestType
            && s.RequestId == step.RequestId && s.Status == "Pending");
        if (morePending)
        {
            await NotifyNextAsync(step);
            return new(true, "مرحله تایید شد و به مرحله بعد رفت.");
        }
        var fin = await FinalizeAsync(step.RequestType, step.RequestId, true, callerId, callerName, note);
        return fin;
    }

    private async Task NotifyNextAsync(HrRequestStep done)
    {
        var next = await _db.HrRequestSteps.AsNoTracking()
            .Where(s => s.RequestType == done.RequestType && s.RequestId == done.RequestId && s.Status == "Pending")
            .OrderBy(s => s.StepNo).FirstOrDefaultAsync();
        if (next == null) return;
        var (title, num) = await RequestTitleAsync(done.RequestType, done.RequestId);
        if (next.Role == "Manager" && next.ApproverUserId != null)
            await _notify.SendAsync(next.ApproverUserId.Value, "درخواست نیازمند تایید مدیر",
                $"{num} — {title}", done.DecidedByName ?? "", "تصویب درخواست‌ها", "/hr-time");
        else
            await _notify.SendManyAsync(await HrApproverIdsAsync(), "درخواست نیازمند تایید کارگزینی",
                $"{num} — {title}", done.DecidedByName ?? "", "تصویب درخواست‌ها", "/hr-time");
        await _notify.BroadcastChangedAsync("hr-time");
    }

    private async Task<(string Title, string Number)> RequestTitleAsync(string type, int id)
    {
        if (type == "Overtime")
        {
            var o = await _db.HrOvertimeRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            return o == null ? ("اضافه‌کاری", "") : ($"اضافه‌کاری {o.RequesterName}", o.Number);
        }
        var l = await _db.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (l == null) return ("درخواست", "");
        var t = l.Type switch { "Hourly" => "مرخصی ساعتی", "HourlyMission" => "ماموریت ساعتی", "Mission" => "ماموریت", _ => "مرخصی روزانه" };
        return ($"{t} {l.RequesterName}", l.Number);
    }

    private async Task<DecideResult> FinalizeAsync(string type, int id, bool approve, int byId, string byName, string? note)
    {
        if (type == "Overtime")
        {
            var o = await _db.HrOvertimeRequests.FirstOrDefaultAsync(x => x.Id == id);
            if (o == null) return new(false, "درخواست اضافه‌کاری پیدا نشد.");
            o.Status = approve ? "Approved" : "Rejected";
            o.DecidedByUserId = byId; o.DecidedByName = byName; o.DecidedAt = DateTime.Now;
            if (!approve) o.Reason = (o.Reason ?? "") + (string.IsNullOrWhiteSpace(note) ? "" : $" [علت رد: {note}]");
            await _db.SaveChangesAsync();
            await _notify.SendAsync(o.RequesterUserId, approve ? "اضافه‌کاری تایید شد" : "اضافه‌کاری رد شد",
                $"{o.Number} توسط {byName} " + (approve ? "تایید شد." : $"رد شد. {note}"), byName, "اضافه‌کاری", "/hr-time");
            await _notify.BroadcastChangedAsync("hr-time");
            return new(true, approve ? "اضافه‌کاری تایید نهایی شد." : "اضافه‌کاری رد شد.");
        }
        var req = await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == id);
        if (req == null) return new(false, "درخواست پیدا نشد.");
        if (req.Status != "Pending")
        {
            await SkipStepsAsync(type, id, byId, byName);
            return new(false, "این درخواست قبلاً تعیین‌تکلیف شده است.");
        }
        var reqCat = await _db.HrLeaveExtras.AsNoTracking().Where(x => x.LeaveRequestId == req.Id)
            .Select(x => x.Category).FirstOrDefaultAsync() ?? "Annual";
        if (approve && req.Type is "Daily" or "Hourly")
        {
            var jy = JalaliYear(req.StartDate);
            var bals = await GetBalancesAsync(req.RequesterUserId, jy);
            var bal = bals.FirstOrDefault(b => b.Category == reqCat);
            var consume = QuotaOf(req);
            if (bal != null && consume > bal.Granted - bal.Used + 0.0001)
                return new(false, $"موجودی مرخصی «{req.RequesterName}» کافی نیست — لطفاً رد کنید.");
        }
        req.Status = approve ? "Approved" : "Rejected";
        req.ApprovedByUserId = byId; req.ApprovedByName = byName; req.ApprovedAt = DateTime.Now;
        if (!approve) req.ApproveNote = note;
        await _db.SaveChangesAsync();
        if (approve && req.Type is "Hourly" or "HourlyMission" or "Daily")
        {
            try
            {
                for (var d = req.StartDate.Date; d <= req.EndDate.Date; d = d.AddDays(1))
                    await _recalc.RecalcDayAsync(req.RequesterUserId, d, DateTime.Now);
            }
            catch { /* خطای بازحساب مانع تایید نیست */ }
        }
        var typeFa = req.Type switch { "Hourly" => "مرخصی ساعتی", "HourlyMission" => "ماموریت ساعتی", "Mission" => "ماموریت", _ => "مرخصی روزانه" };
        await _notify.SendAsync(req.RequesterUserId, approve ? $"{typeFa} شما تایید شد" : $"{typeFa} شما رد شد",
            $"{req.Number} توسط {byName} " + (approve ? "تایید شد." : $"رد شد. {note}"), byName, "مرخصی و ماموریت", "/leave");
        await _notify.BroadcastChangedAsync("leave-requests");
        await _notify.BroadcastChangedAsync("hr-time");
        return new(true, approve ? "درخواست تایید نهایی شد." : "درخواست رد شد.");
    }

    public async Task<List<int>> HrApproverIdsAsync()
    {
        var rbac = await _db.UserRoles
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur.UserId, rp.PermissionId })
            .Join(_db.Permissions, x => x.PermissionId, p => p.Id, (x, p) => new { x.UserId, p.Module, p.Action })
            .Where(x => x.Module == "LeaveRequests" && x.Action == "Approve")
            .Select(x => x.UserId).Distinct().ToListAsync();
        var legacy = await _db.Users
            .Where(u => u.Role == "Admin" && u.IsActive && !_db.UserRoles.Any(ur => ur.UserId == u.Id))
            .Select(u => u.Id).ToListAsync();
        return rbac.Concat(legacy).Distinct().ToList();
    }

    // ================= اضافه‌کاری =================

    public async Task<HrOvertimeRequest> CreateOvertimeAsync(int userId, string userName, DateTime date, TimeSpan start, TimeSpan end, string type, string? reason)
    {
        if (end <= start) throw new InvalidOperationException("ساعت پایان باید بعد از ساعت شروع باشد.");
        var jy = JalaliYear(date);
        var serial = await _db.HrOvertimeRequests.CountAsync(o => o.Number.StartsWith($"OT/{jy}/")) + 1;
        var o = new HrOvertimeRequest
        {
            Number = $"OT/{jy}/{serial}",
            RequesterUserId = userId, RequesterName = userName,
            WorkDate = date.Date, StartTime = start, EndTime = end,
            Minutes = (int)(end - start).TotalMinutes, Type = type, Reason = reason, Status = "Pending",
        };
        _db.HrOvertimeRequests.Add(o);
        await _db.SaveChangesAsync();
        await CreateStepsAsync("Overtime", o.Id, userId);
        var targets = (await HrApproverIdsAsync()).ToList();
        var (mgrId, _) = await ResolveManagerAsync(userId);
        if (mgrId != null) targets.Add(mgrId.Value);
        await _notify.SendManyAsync(targets.Distinct(), "درخواست اضافه‌کاری جدید",
            $"{o.Number} — {userName}", userName, "اضافه‌کاری", "/hr-time");
        await _notify.BroadcastChangedAsync("hr-time");
        return o;
    }

    // ================= روستِر شیفت =================

    public async Task<int> GenerateRosterAsync(List<int> userIds, DateTime from, DateTime to, List<int> pattern, int startOffset)
    {
        if (pattern.Count == 0) throw new InvalidOperationException("الگوی شیفت خالی است.");
        var shifts = await _db.ShiftGroups.AsNoTracking().Where(s => pattern.Contains(s.Id)).Select(s => s.Id).ToListAsync();
        if (shifts.Count != pattern.Distinct().Count()) throw new InvalidOperationException("یکی از شیفت‌های الگو معتبر نیست.");
        if ((to.Date - from.Date).TotalDays > 366) throw new InvalidOperationException("بازه‌ی تولید حداکثر یک سال است.");
        var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => u.Id).ToListAsync();
        var dates = Enumerable.Range(0, (int)(to.Date - from.Date).TotalDays + 1).Select(i => from.Date.AddDays(i)).ToList();
        var existing = await _db.HrShiftRosters
            .Where(r => users.Contains(r.UserId) && r.Date >= from.Date && r.Date <= to.Date).ToListAsync();
        var n = 0;
        foreach (var u in users)
            for (var i = 0; i < dates.Count; i++)
            {
                var sid = pattern[(startOffset + i) % pattern.Count];
                var ex = existing.FirstOrDefault(r => r.UserId == u && r.Date == dates[i]);
                if (ex == null) { _db.HrShiftRosters.Add(new HrShiftRoster { UserId = u, Date = dates[i], ShiftGroupId = sid }); n++; }
                else if (ex.ShiftGroupId != sid) { ex.ShiftGroupId = sid; n++; }
            }
        await _db.SaveChangesAsync();
        return n;
    }

    public async Task<ShiftGroup?> RosterShiftAsync(int userId, DateTime date)
    {
        var r = await _db.HrShiftRosters.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Date == date.Date);
        if (r == null) return null;
        return await _db.ShiftGroups.AsNoTracking().FirstOrDefaultAsync(s => s.Id == r.ShiftGroupId);
    }

    // ================= ایمپورت دستگاه =================

    public record ImportResult(int Imported, int Skipped, List<string> Errors);

    public async Task<ImportResult> ImportPunchesAsync(Stream stream, string fileName, string deviceCode)
    {
        var rows = new List<(string Code, DateTime Time, string Dir)>();
        var errors = new List<string>();
        if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            foreach (var r in ws.RowsUsed().Skip(1).Take(20000))
            {
                var code = r.Cell(1).GetString().Trim();
                if (string.IsNullOrEmpty(code)) continue;
                if (!r.Cell(2).TryGetValue(out DateTime t)) { errors.Add($"سطر {r.RowNumber()}: تاریخ نامعتبر"); continue; }
                var dir = r.Cell(3).GetString().Trim();
                rows.Add((code, t, dir is "In" or "Out" ? dir : "Auto"));
            }
        }
        else
        {
            using var sr = new StreamReader(stream);
            var first = true; var ln = 0;
            while (await sr.ReadLineAsync() is { } line)
            {
                ln++;
                if (first) { first = false; continue; } // سطر عنوان
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = line.Split(',', ';', '\t');
                if (p.Length < 2 || !DateTime.TryParse(p[1].Trim(), out var t)) { errors.Add($"سطر {ln}: قالب نامعتبر"); continue; }
                var dir = p.Length > 2 ? p[2].Trim() : "Auto";
                rows.Add((p[0].Trim(), t, dir is "In" or "Out" ? dir : "Auto"));
                if (rows.Count >= 20000) break;
            }
        }
        var maps = await _db.HrDeviceUserMaps.AsNoTracking().ToListAsync();
        int MapOf(string code) => maps.FirstOrDefault(m => m.UserCode == code
            && (m.DeviceCode == null || m.DeviceCode == deviceCode))?.SystemUserId ?? 0;
        var imported = 0;
        foreach (var (code, time, dir) in rows)
        {
            var dup = await _db.HrDevicePunches.AnyAsync(p => p.DeviceCode == deviceCode && p.UserCode == code && p.PunchTime == time);
            if (dup) continue;
            var uid = MapOf(code);
            _db.HrDevicePunches.Add(new HrDevicePunch
            { DeviceCode = deviceCode, UserCode = code, PunchTime = time, Direction = dir, MappedUserId = uid == 0 ? null : uid });
            imported++;
        }
        await _db.SaveChangesAsync();
        return new(imported, rows.Count - imported, errors.Take(20).ToList());
    }

    public record ApplyResult(int Punches, int Records, List<string> Unmapped);

    public async Task<ApplyResult> ApplyPunchesAsync(DateTime from, DateTime to)
    {
        var punches = await _db.HrDevicePunches
            .Where(p => p.PunchTime >= from.Date && p.PunchTime < to.Date.AddDays(1) && p.AppliedRecordId == null && p.MappedUserId != null)
            .OrderBy(p => p.PunchTime).Take(5000).ToListAsync();
        var unmapped = await _db.HrDevicePunches.AsNoTracking()
            .Where(p => p.PunchTime >= from.Date && p.PunchTime < to.Date.AddDays(1) && p.MappedUserId == null)
            .Select(p => p.UserCode).Distinct().Take(50).ToListAsync();
        var recCount = 0;
        foreach (var g in punches.GroupBy(p => (p.MappedUserId!.Value, p.PunchTime.Date)))
        {
            var (uid, date) = g.Key;
            var list = g.OrderBy(p => p.PunchTime).ToList();
            var pairs = new List<(DateTime In, DateTime? Out)>();
            if (list.Any(p => p.Direction != "Auto"))
            {
                DateTime? open = null;
                foreach (var p in list)
                {
                    if (p.Direction == "In") open ??= p.PunchTime;
                    else if (p.Direction == "Out" && open != null) { pairs.Add((open.Value, p.PunchTime)); open = null; }
                }
                if (open != null) pairs.Add((open.Value, null));
            }
            else if (list.Count == 1) pairs.Add((list[0].PunchTime, null));
            else pairs.Add((list.First().PunchTime, list.Last().PunchTime));
            var user = await _db.Users.Include(u => u.ShiftGroup).FirstOrDefaultAsync(u => u.Id == uid);
            if (user == null) continue;
            var rosterSg = await RosterShiftAsync(uid, date);
            var sg = rosterSg ?? user.ShiftGroup;
            var rec = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.UserId == uid && a.WorkDate == date);
            if (rec == null)
            {
                rec = new AttendanceRecord
                {
                    WorkDate = date, UserId = uid,
                    UserName = string.IsNullOrWhiteSpace(user.FirstName) ? user.Username : $"{user.FirstName} {user.LastName}".Trim(),
                    ShiftGroupId = sg?.Id, CreatedAt = DateTime.Now,
                };
                _db.AttendanceRecords.Add(rec);
                await _db.SaveChangesAsync();
                recCount++;
            }
            var seq = await _db.AttendanceSegments.Where(s => s.UserId == uid && s.WorkDate == date).MaxAsync(s => (int?)s.Seq) ?? 0;
            foreach (var (In, Out) in pairs)
            {
                seq++;
                _db.AttendanceSegments.Add(new AttendanceSegment
                {
                    UserId = uid, UserName = rec.UserName, WorkDate = date, Seq = seq,
                    EnterAt = In, ExitAt = Out, EnterDevice = "DeviceImport", Note = "ایمپورت دستگاه",
                });
            }
            foreach (var p in list) p.AppliedRecordId = rec.Id;
            await _db.SaveChangesAsync();
            await _recalc.RecalcDayAsync(uid, date, DateTime.Now);
        }
        return new(punches.Count, recCount, unmapped);
    }

    // ================= تفکیک اضافه‌کاری (مستقل از موتور قبلی؛ فقط-خواندنی) =================

    /// <summary>دقیقه‌های هم‌پوشانی بازه با پنجره شب</summary>
    public static int NightMinutes(DateTime from, DateTime to, TimeSpan nightStart, TimeSpan nightEnd)
    {
        if (to <= from) return 0;
        var total = 0;
        for (var d = from.Date.AddDays(-1); d <= to.Date.AddDays(1); d = d.AddDays(1))
        {
            var ns = d.Add(nightStart);
            var ne = nightEnd > nightStart ? d.Add(nightEnd) : d.AddDays(1).Add(nightEnd);
            var s0 = from > ns ? from : ns;
            var e0 = to < ne ? to : ne;
            if (e0 > s0) total += (int)Math.Floor((e0 - s0).TotalMinutes);
        }
        return total;
    }

    /// <summary>تفکیک اضافه‌کاری روز به عادی/تعطیل/شب</summary>
    public static (int normal, int holiday, int night) SplitOvertime(WorkDayRule rule, DateTime date,
        DateTime shiftEnd, DateTime? exitAt, int overtimeMin, TimeSpan? nightStart = null, TimeSpan? nightEnd = null)
    {
        if (overtimeMin <= 0) return (0, 0, 0);
        if (!rule.IsWorkday || rule.Source == "Holiday") return (0, overtimeMin, 0);
        var ns = nightStart ?? new TimeSpan(22, 0, 0);
        var ne = nightEnd ?? new TimeSpan(6, 0, 0);
        var exit = exitAt ?? shiftEnd;
        var night = exit > shiftEnd ? Math.Min(overtimeMin, NightMinutes(shiftEnd, exit, ns, ne)) : 0;
        return (overtimeMin - night, 0, night);
    }

    public static int PersianMonth(DateTime d) => Pc.GetMonth(d);
    public const int HourlyWorkdayHours = 8;

    /// <summary>محاسبه‌ی فقط-خواندنی تفکیک اضافه‌کاری یک روز و ذخیره در جدول مجزای HrDayExtra</summary>
    public async Task<HrDayExtra> ComputeDayExtrasAsync(int userId, DateTime date)
    {
        date = date.Date;
        var rec = await _db.AttendanceRecords.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.WorkDate == date);
        var user = await _db.Users.Include(u => u.ShiftGroup).AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);
        var sg = await RosterShiftAsync(userId, date) ?? user?.ShiftGroup;
        var cal = await _db.WorkCalendarDays.AsNoTracking().FirstOrDefaultAsync(d => d.Date.Date == date);
        var hol = await _db.CompanyHolidays.AsNoTracking().FirstOrDefaultAsync(h => h.HolidayDate.Date == date);
        var settings = await _db.WorkCalendarSettings.AsNoTracking().FirstOrDefaultAsync();
        var rule = WorkRules.Resolve(date, sg, cal, hol, settings);
        var rules = await GetRulesAsync();
        TimeSpan.TryParse(rules.TryGetValue("night.start", out var nsv) ? nsv : "22:00", out var nst);
        TimeSpan.TryParse(rules.TryGetValue("night.end", out var nev) ? nev : "06:00", out var nen);
        var ot = rec?.OvertimeMinutes ?? 0;
        var (n, h, ni) = SplitOvertime(rule, date, WorkRules.ShiftEnd(date, rule), rec?.ExitAt, ot, nst, nen);
        var ex = await _db.HrDayExtras.FirstOrDefaultAsync(x => x.UserId == userId && x.WorkDate == date);
        if (ex == null)
        {
            ex = new HrDayExtra { UserId = userId, WorkDate = date };
            _db.HrDayExtras.Add(ex);
        }
        ex.OtNormalMinutes = n; ex.OtHolidayMinutes = h; ex.OtNightMinutes = ni;
        ex.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return ex;
    }

    public record ExtrasSum(double OtN, double OtH, double OtNight);

    /// <summary>جمع تفکیک اضافه‌کاری یک ماه شمسی (ساعت) — برای حقوق</summary>
    public async Task<ExtrasSum> MonthExtrasSumAsync(int userId, int jy, int jm)
    {
        var start = Pc.ToDateTime(jy, jm, 1, 0, 0, 0, 0);
        var days = Pc.GetDaysInMonth(jy, jm);
        var end = start.AddDays(days);
        var rows = await _db.HrDayExtras.AsNoTracking()
            .Where(x => x.UserId == userId && x.WorkDate >= start && x.WorkDate < end).ToListAsync();
        var have = rows.Select(r => r.WorkDate).ToHashSet();
        foreach (var i in Enumerable.Range(0, days))
        {
            var d = start.AddDays(i);
            if (!have.Contains(d)) rows.Add(await ComputeDayExtrasAsync(userId, d));
        }
        return new(Math.Round(rows.Sum(r => r.OtNormalMinutes) / 60.0, 2),
                   Math.Round(rows.Sum(r => r.OtHolidayMinutes) / 60.0, 2),
                   Math.Round(rows.Sum(r => r.OtNightMinutes) / 60.0, 2));
    }

    /// <summary>ثبت مختصات ورود/خروج موبایل در جدول مجزا (بدون دست‌کاری تردد قبلی)</summary>
    public async Task SaveGpsAsync(int userId, DateTime date, bool isEnter, double lat, double lng)
    {
        date = date.Date;
        var ex = await _db.HrDayExtras.FirstOrDefaultAsync(x => x.UserId == userId && x.WorkDate == date);
        if (ex == null)
        {
            ex = new HrDayExtra { UserId = userId, WorkDate = date };
            _db.HrDayExtras.Add(ex);
        }
        if (isEnter) { ex.EnterLat = lat; ex.EnterLng = lng; }
        else { ex.ExitLat = lat; ex.ExitLng = lng; }
        ex.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    // ================= ثبت درخواست مرخصی/ماموریت از مسیر ماژول جدید =================

    public record LeaveCreateResult(bool Ok, string Message, int Id = 0, string Number = "");

    /// <summary>
    /// ثبت درخواست در جدول مرخصی (همان ساختار قبلی، بدون هیچ ستون جدید) + ذخیره‌ی
    /// دسته/نوع‌ماموریت/حق‌ماموریت در جدول مجزای HrLeaveExtra + ایجاد گردش چندمرحله‌ای.
    /// </summary>
    public async Task<LeaveCreateResult> CreateLeaveAsync(int userId, string userName, string type,
        DateTime startDate, DateTime endDate, string? startTime, string? endTime,
        string? destination, string? reason, string? category, string? missionKind)
    {
        if (type is not ("Daily" or "Hourly" or "HourlyMission" or "Mission"))
            return new(false, "نوع درخواست نامعتبر است.");
        if (type is "Hourly" or "HourlyMission") endDate = startDate;
        if (startDate.Date > endDate.Date) return new(false, "تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد.");
        double days = 0, hours = 0;
        TimeSpan? st = null, en = null;
        if (type == "Daily")
        {
            days = (endDate.Date - startDate.Date).TotalDays + 1;
            if (days > 366) return new(false, "بازه‌ی درخواست معتبر نیست.");
        }
        else if (type is "Hourly" or "HourlyMission")
        {
            if (string.IsNullOrWhiteSpace(startTime) || !TimeSpan.TryParse(startTime, out var s0))
                return new(false, "ساعت شروع را درست وارد کنید (مثلاً 08:00).");
            if (string.IsNullOrWhiteSpace(endTime) || !TimeSpan.TryParse(endTime, out var e0))
                return new(false, "ساعت پایان را درست وارد کنید (مثلاً 10:30).");
            if (e0 <= s0) return new(false, "ساعت پایان باید بعد از ساعت شروع باشد.");
            hours = Math.Round((e0 - s0).TotalHours, 2);
            if (hours > HourlyWorkdayHours) return new(false, $"بازه‌ی ساعتی نمی‌تواند بیش از {HourlyWorkdayHours} ساعت باشد.");
            st = s0; en = e0; endDate = startDate;
        }
        else
        {
            days = (endDate.Date - startDate.Date).TotalDays + 1;
            if (string.IsNullOrWhiteSpace(destination)) return new(false, "مقصد ماموریت را وارد کنید.");
        }
        if (type == "HourlyMission" && string.IsNullOrWhiteSpace(destination))
            return new(false, "مقصد ماموریت را وارد کنید.");

        var cat = category is "Annual" or "Sick" or "Maternity" or "Unpaid" ? category : "Annual";
        double consume = type == "Hourly" ? hours / HourlyWorkdayHours : days;
        if (type is "Daily" or "Hourly" && consume > 0)
        {
            var jy0 = JalaliYear(startDate);
            var bal0 = (await GetBalancesAsync(userId, jy0)).FirstOrDefault(b => b.Category == cat);
            var grant = bal0?.Granted ?? 24;
            var remain = grant - (bal0?.Used ?? 0) - (bal0?.Pending ?? 0);
            if (consume > remain + 0.0001)
                return new(false, $"موجودی مرخصی شما کافی نیست. (مانده: {Math.Round(remain, 2)} از {grant} روز)");
        }

        var reqJy = JalaliYear(startDate);
        var serial = await _db.LeaveRequests.CountAsync(l => l.Number.StartsWith($"LR/{reqJy}/")) + 1;
        string? mk = type is "Mission" or "HourlyMission" ? (missionKind == "Outer" ? "Outer" : "Inner") : null;
        decimal allowance = 0;
        if (mk != null)
        {
            var rules0 = await GetRulesAsync();
            var perDay = rules0.TryGetValue(mk == "Outer" ? "mission.allowance.outer" : "mission.allowance.inner", out var av)
                && decimal.TryParse(av, out var ad) ? ad : 0;
            var mDays = type == "HourlyMission" ? hours / HourlyWorkdayHours : Math.Max(days, 1);
            allowance = perDay * (decimal)Math.Max(mDays, 0);
        }
        var req = new LeaveRequest
        {
            Number = $"LR/{reqJy}/{serial}", Type = type,
            RequesterUserId = userId, RequesterName = userName,
            StartDate = startDate.Date, EndDate = endDate.Date,
            StartTime = st, EndTime = en, Days = days, Hours = hours,
            Destination = destination, Reason = reason,
            Status = "Pending", CreatedAt = DateTime.Now,
        };
        _db.LeaveRequests.Add(req);
        await _db.SaveChangesAsync();
        _db.HrLeaveExtras.Add(new HrLeaveExtra
        { LeaveRequestId = req.Id, Category = cat, MissionKind = mk, AllowanceAmount = allowance });
        await _db.SaveChangesAsync();

        var steps = await CreateStepsAsync("Leave", req.Id, userId);
        var targets = (await HrApproverIdsAsync()).ToList();
        var mgrStep = steps.FirstOrDefault(x => x.Role == "Manager" && x.ApproverUserId != null);
        if (mgrStep?.ApproverUserId != null && !targets.Contains(mgrStep.ApproverUserId.Value))
            targets.Add(mgrStep.ApproverUserId.Value);
        var typeFa = type switch { "Hourly" => "مرخصی ساعتی", "HourlyMission" => "ماموریت ساعتی", "Mission" => "ماموریت", _ => "مرخصی روزانه" };
        await _notify.SendManyAsync(targets.Distinct(), "درخواست منابع انسانی جدید",
            $"{req.Number} — {typeFa} توسط {userName}", userName, "مرخصی و ماموریت", "/hr-time");
        await _notify.BroadcastChangedAsync("hr-time");
        return new(true, "ثبت شد.", req.Id, req.Number);
    }

    /// <summary>
    /// ایجاد گردش برای درخواست‌های درانتظاری که از مسیر قبلی ثبت شده‌اند و مرحله ندارند —
    /// تا صندوق ورودی ماژول جدید همه را پوشش دهد، بدون هیچ تغییری در کد قبلی.
    /// </summary>
    public async Task<int> EnsureStepsForPendingAsync()
    {
        var pendIds = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.Status == "Pending").Select(l => l.Id).ToListAsync();
        if (pendIds.Count == 0) return 0;
        var withSteps = await _db.HrRequestSteps.AsNoTracking()
            .Where(s => s.RequestType == "Leave" && pendIds.Contains(s.RequestId))
            .Select(s => s.RequestId).Distinct().ToListAsync();
        var missing = pendIds.Except(withSteps).ToList();
        var n = 0;
        foreach (var id in missing)
        {
            var uid = await _db.LeaveRequests.AsNoTracking().Where(l => l.Id == id)
                .Select(l => l.RequesterUserId).FirstOrDefaultAsync();
            await CreateStepsAsync("Leave", id, uid);
            n++;
        }
        return n;
    }
}
