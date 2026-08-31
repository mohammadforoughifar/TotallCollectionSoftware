using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// بازحساب کسری یک روز برای یک کاربر — بعد از تایید/تغییر وضعیت درخواست مرخصی/ماموریت.
/// با این کار درخواست‌های «عقب‌النصر» (برای روزهای گذشته) هم بلافاصله روی کسری اعمال می‌شوند.
/// قوانین محاسبه دقیقاً مطابق AttendanceController است:
///   کارکرد = مجموع بازه‌های بسته (+ بازه‌ی باز تا پایان شیفت برای روز جاری)
///   غیبت پوشش‌شده = بازه‌های [خروج، ورود بعدی] یا [خروج آخر، پایان شیفت] که کاملاً داخل
///                  مرخصی/ماموریت ساعتی تاییدشده باشند
///   کسری = max(0, موظفی − کارکرد − غیبت‌پوشش‌شده)
/// </summary>
public class AttendanceRecalcService
{
    private readonly AppDbContext _db;
    public AttendanceRecalcService(AppDbContext db) => _db = db;

    public static int ScheduledMinutes(ShiftGroup? sg)
    {
        if (sg == null) return 8 * 60;
        var m = (int)(sg.EndTime - sg.StartTime).TotalMinutes;
        return m > 0 ? m : 8 * 60;
    }

    /// <summary>
    /// بخش «دیررس بودن» یعنی بازه‌ی [شروع شیفت، اولین ورود] — اگر مرخصی/ماموریت ساعتی تاییدشده
    /// کل این بازه را پوشش دهد، دقیقه‌های آن به‌عنوان غیبت پوشش‌شده حساب می‌شود (کسری نمی‌خورد).
    /// </summary>
    public static int LateCoveredMinutes(ShiftGroup? sg, DateTime workDate, DateTime? firstEnter, List<LeaveRequest> covers)
    {
        if (sg == null || !firstEnter.HasValue) return 0;
        var schedStart = workDate.Add(sg.StartTime);
        if (firstEnter.Value <= schedStart) return 0;
        var gs = schedStart.TimeOfDay;
        var ge = firstEnter.Value.TimeOfDay;
        return covers.Any(l => l.StartTime <= gs && l.EndTime >= ge)
            ? Math.Max(0, (int)(firstEnter.Value - schedStart).TotalMinutes)
            : 0;
    }

    /// <summary>بازحساب یک روز مشخص (اگر رکوردی نیست و مرخصی ساعتی تاییدشده‌ای هم ندارد، کاری نمی‌کند).</summary>
    public async Task RecalcDayAsync(int userId, DateTime date, DateTime asOf)
    {
        date = date.Date;
        var user = await _db.Users.Include(u => u.ShiftGroup).AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var sg = user?.ShiftGroup;
        var userName = user == null ? "" : (string.IsNullOrWhiteSpace(user.FirstName) ? user.Username : $"{user.FirstName} {user.LastName}".Trim());

        // تعطیلی شرکتی → کل روز مرخصی است و کسری نمی‌خورد
        var isCompanyHoliday = await _db.CompanyHolidays.AnyAsync(h => h.HolidayDate == date);

        var rec = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.UserId == userId && a.WorkDate == date);

        if (isCompanyHoliday)
        {
            if (rec == null)
            {
                var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
                rec = new AttendanceRecord
                {
                    WorkDate = date,
                    UserId = userId,
                    UserName = u == null ? "" : (string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : $"{u.FirstName} {u.LastName}".Trim()),
                    ShiftGroupId = u?.ShiftGroup?.Id,
                    FinalStatus = "Holiday",
                    HasApprovedLeave = true,
                    CreatedAt = DateTime.Now,
                };
                _db.AttendanceRecords.Add(rec);
            }
            else
            {
                rec.FinalStatus = "Holiday";
                rec.HasApprovedLeave = true;
            }
            rec.DeficitMinutes = 0;
            rec.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();
            return;
        }

        var hourlies = await _db.LeaveRequests
            .Where(l => l.RequesterUserId == userId && l.Status == "Approved"
                        && (l.Type == "Hourly" || l.Type == "HourlyMission")
                        && l.StartDate == date
                        && l.StartTime != null && l.EndTime != null)
            .ToListAsync();

        // روزی بدون رکورد: فقط اگر مرخصی ساعتی/ماموریت ساعتی تاییدشده دارد، رکورد جایگزین بسازیم
        if (rec == null)
        {
            if (hourlies.Count == 0) return;
            rec = new AttendanceRecord
            {
                WorkDate = date,
                UserId = userId,
                UserName = userName,
                ShiftGroupId = sg?.Id,
                FinalStatus = "LeaveDay",
                CreatedAt = DateTime.Now,
            };
            _db.AttendanceRecords.Add(rec);
        }
        if (!rec.ShiftGroupId.HasValue) rec.ShiftGroupId = sg?.Id;

        var segs = await _db.AttendanceSegments
            .Where(s => s.UserId == userId && s.WorkDate == date)
            .OrderBy(s => s.Seq)
            .ToListAsync();

        // ---------- ارزیابی پوشش هر بازه‌ی غیبت بسته ----------
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            if (s.ExitAt == null) continue;
            var gapEnd = (i + 1 < segs.Count && segs[i + 1].EnterAt.HasValue)
                ? segs[i + 1].EnterAt!.Value
                : date.Add(sg?.EndTime ?? new TimeSpan(16, 30, 0)); // روز تمام شده → تا پایان شیفت
            var gs = s.ExitAt.Value.TimeOfDay;
            var ge = gapEnd.TimeOfDay;
            var cover = hourlies.FirstOrDefault(l => l.StartTime <= gs && l.EndTime >= ge);
            s.ExitCovered = cover != null;
            s.LinkedLeaveRequestId = cover?.Id;
            s.LinkedLeaveNumber = cover?.Number;
        }

        // ---------- تجمیع ----------
        var shiftEnd = date.Add(sg?.EndTime ?? new TimeSpan(16, 30, 0));
        int work = 0;
        foreach (var s in segs)
        {
            if (!s.EnterAt.HasValue) continue;
            if (s.ExitAt.HasValue)
                work += Math.Max(0, (int)(s.ExitAt.Value - s.EnterAt.Value).TotalMinutes);
            else
            {
                var until = shiftEnd < asOf ? shiftEnd : asOf;
                work += Math.Max(0, (int)(until - s.EnterAt.Value).TotalMinutes);
            }
        }

        int covered = 0;
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            if (s.ExitAt == null || !s.ExitCovered) continue;
            var gapEnd = (i + 1 < segs.Count && segs[i + 1].EnterAt.HasValue)
                ? segs[i + 1].EnterAt!.Value
                : shiftEnd;
            covered += Math.Max(0, (int)(gapEnd - s.ExitAt.Value).TotalMinutes);
        }

        // بخش دیررس بودن [شروع شیفت، اولین ورود] — اگر پوشش داده شده باشد
        covered += LateCoveredMinutes(sg, date, segs.FirstOrDefault(s => s.EnterAt.HasValue)?.EnterAt, hourlies);

        var hasDaily = await _db.LeaveRequests
            .AnyAsync(l => l.RequesterUserId == userId && l.Status == "Approved" && l.Type == "Daily"
                           && l.StartDate.Date <= date && l.EndDate.Date >= date);

        rec.WorkMinutes = work;
        rec.CoveredGapMinutes = covered;
        rec.HasApprovedLeave = hasDaily || hourlies.Count > 0;
        if (hasDaily)
        {
            rec.DeficitMinutes = 0;
            if (string.IsNullOrEmpty(rec.FinalStatus) || rec.FinalStatus == "Present")
                rec.FinalStatus = "LeaveDay";
        }
        else
        {
            rec.DeficitMinutes = Math.Max(0, ScheduledMinutes(sg) - work - covered);
            if (string.IsNullOrEmpty(rec.FinalStatus)) rec.FinalStatus = "Present";
        }
        rec.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
