namespace Inventory.Api.Data;

/// <summary>قانون کاریِ یک روز — نتیجه‌ی حل‌شدن تقویم کاری + شیفت کاربر + پیش‌فرض‌ها. ورود/خروج پرسنل بر اساس همین قاعده ارزیابی می‌شود.</summary>
public record WorkDayRule
{
    /// <summary>true = روز کاری (حضور موظف است) — false = تعطیل/غیرکاری</summary>
    public bool IsWorkday { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public int GraceMinutes { get; init; }
    /// <summary>سقفِ ساعتیِ اضافه‌کاری مجاز (فقط در حالت «سقف ساعتی»)</summary>
    public double OvertimeHours { get; init; }
    /// <summary>منبع قاعده: "Calendar" | "Holiday" | "Shift" | "Default"</summary>
    public string Source { get; init; } = "Default";
    public string? Note { get; init; }
    /// <summary>true = این روز تعطیل رسمی کشور است (مثل نوروز، عید فطر، ۲۲ بهمن)</summary>
    public bool IsOfficial { get; init; }

    /// <summary>حالت اضافه‌کاری روز: 0=بدون | 1=بازه زمانی | 2=کل روز | 3=سقف ساعتی</summary>
    public int OvertimeMode { get; init; }
    /// <summary>شروعِ بازه‌ی اضافه‌کاری (حالت ۱)</summary>
    public TimeSpan? OvertimeStart { get; init; }
    /// <summary>پایانِ بازه‌ی اضافه‌کاری (حالت ۱ — ممکن است قبل از شروع = عبور از نیمه‌شب)</summary>
    public TimeSpan? OvertimeEnd { get; init; }

    /// <summary>شروعِ پنجره‌ی دومِ شیفت دوپاره (مثلاً شیفت «۸–۱۳ + ۱۷–۲۱:۳۰») — null = تک‌بازه‌ای</summary>
    public TimeSpan? Start2 { get; init; }

    /// <summary>پایانِ پنجره‌ی دومِ شیفت دوپاره — فقط همراه با Start2 معتبر است</summary>
    public TimeSpan? End2 { get; init; }

    public static WorkDayRule Default() => new()
    {
        IsWorkday = true,
        Start = new TimeSpan(8, 0, 0),
        End = new TimeSpan(16, 30, 0),
        GraceMinutes = 10,
        OvertimeHours = 0,
        Source = "Default",
    };
}

public static class WorkRules
{
    /// <summary>مقادیرِ مجازِ حالتِ اضافه‌کاری</summary>
    public const int OT_None = 0, OT_Window = 1, OT_WholeDay = 2, OT_HourCap = 3;

    /// <summary>
    /// حل‌شدن قاعده‌ی یک روز:
    /// ۱) ردیف تقویم کاری (WorkCalendarDay) — کامل‌ترین منبع
    /// ۲) تعطیل رسمی کشور (CompanyHoliday با IsOfficial=true) → تعطیل (در صورت فعال‌بودن اعمال خودکار)
    /// ۳) تعطیلی شرکتی (CompanyHoliday) → تعطیل
    /// ۴) شیفت کاربر (ShiftGroup) — روزهای تعطیل هفته بر اساس تنظیمات (پیش‌فرض: جمعه؛ IncludeFriday جمعه را شامل کار می‌کند)
    /// ۵) پیش‌فرض: روزهای تعطیل هفته از تنظیمات؛ ساعت کاری و تاخیر مجاز از تنظیمات (۰۸:۰۰–۱۶:۳۰ با ۱۰ دقیقه)
    /// </summary>
    public static WorkDayRule Resolve(DateTime date, ShiftGroup? userShift, WorkCalendarDay? calendarDay,
        CompanyHoliday? companyHoliday, WorkCalendarSettings? settings)
    {
        date = date.Date;

        // ۱) تقویم کاری
        if (calendarDay != null && calendarDay.Date.Date == date)
        {
            return new WorkDayRule
            {
                IsWorkday = calendarDay.IsWorkday,
                Start = calendarDay.StartTime ?? settings?.DefaultStart ?? new TimeSpan(8, 0, 0),
                End = calendarDay.EndTime ?? settings?.DefaultEnd ?? new TimeSpan(16, 30, 0),
                GraceMinutes = calendarDay.GraceMinutes > 0 ? calendarDay.GraceMinutes : (userShift?.GraceMinutes ?? settings?.GraceMinutes ?? 10),
                OvertimeHours = calendarDay.OvertimeHours,
                OvertimeMode = calendarDay.OvertimeMode,
                OvertimeStart = calendarDay.OvertimeStart,
                OvertimeEnd = calendarDay.OvertimeEnd,
                Source = "Calendar",
                Note = calendarDay.Note,
            };
        }

        // ۲ و ۳) تعطیل رسمی کشور / تعطیلی شرکتی
        if (companyHoliday != null && companyHoliday.HolidayDate.Date == date)
        {
            // اگر «اعمال خودکار تعطیلات رسمی» غیرفعال باشد، تعطیل رسمی مثل روز عادی ارزیابی می‌شود
            if (!(companyHoliday.IsOfficial && settings != null && !settings.ApplyOfficialHolidays))
            {
                return new WorkDayRule
                {
                    IsWorkday = false,
                    Start = settings?.DefaultStart ?? new TimeSpan(8, 0, 0),
                    End = settings?.DefaultEnd ?? new TimeSpan(16, 30, 0),
                    GraceMinutes = userShift?.GraceMinutes ?? settings?.GraceMinutes ?? 10,
                    OvertimeHours = 0,
                    Source = "Holiday",
                    Note = companyHoliday.Name,
                    IsOfficial = companyHoliday.IsOfficial,
                };
            }
        }

        // ۴) شیفت کاربر — روزهای تعطیل هفته از تنظیمات (IncludeFriday فقط جمعه را به کار شامل می‌کند)
        if (userShift != null)
        {
            var flags = settings?.RestDayFlags ?? WorkCalendarSettings.RestFriday;
            if (userShift.IncludeFriday) flags &= ~WorkCalendarSettings.RestFriday;
            var isOff = (flags & (1 << (int)date.DayOfWeek)) != 0;
            // شیفت دوپاره: پنجره‌ی دوم فقط وقتی معتبر است که هر دو مقدار تنظیم و پایان بعد از شروع باشد
            TimeSpan? s2 = userShift.StartTime2 is { } sa && userShift.EndTime2 is { } eb && eb > sa ? sa : null;
            TimeSpan? e2 = s2 != null ? userShift.EndTime2 : null;
            return new WorkDayRule
            {
                IsWorkday = !isOff,
                Start = userShift.StartTime,
                End = userShift.EndTime,
                Start2 = s2,
                End2 = e2,
                GraceMinutes = userShift.GraceMinutes,
                OvertimeHours = 0,
                Source = "Shift",
                Note = userShift.Name,
            };
        }

        // ۵) پیش‌فرض — بر اساس تنظیمات تقویم کاری
        var restFlags = settings?.RestDayFlags ?? WorkCalendarSettings.RestFriday;
        var def = new WorkDayRule
        {
            IsWorkday = true,
            Start = settings?.DefaultStart ?? new TimeSpan(8, 0, 0),
            End = settings?.DefaultEnd ?? new TimeSpan(16, 30, 0),
            GraceMinutes = settings?.GraceMinutes ?? 10,
            OvertimeHours = 0,
            Source = "Default",
        };
        return WorkCalendarSettings.IsRestDay(restFlags, date)
            ? def with { IsWorkday = false, Source = "Default", Note = "روز تعطیل هفته" }
            : def;
    }

    /// <summary>حل‌شدن قاعده با مقادیر پیش‌فرض قدیمی (بدون تنظیمات) — برای سازگاری با کدهای موجود.</summary>
    public static WorkDayRule Resolve(DateTime date, ShiftGroup? userShift, WorkCalendarDay? calendarDay, CompanyHoliday? companyHoliday)
        => Resolve(date, userShift, calendarDay, companyHoliday, null);

    /// <summary>آیا شیفت از نیمه‌شب عبور می‌کند؟ (ساعت پایان قبل از ساعت شروع = شیفت شب)</summary>
    public static bool CrossesMidnight(WorkDayRule rule) => rule.End < rule.Start;

    /// <summary>پنجره‌ی کاری اولِ روز به‌صورت مطلق (با پشتیبانی شیفت شبِ عبورکننده از نیمه‌شب)</summary>
    public static (DateTime From, DateTime To) WorkWindow1(DateTime date, WorkDayRule rule)
        => rule.End < rule.Start
            ? (date.Date + rule.Start, date.Date.AddDays(1) + rule.End)
            : (date.Date + rule.Start, date.Date + rule.End);

    /// <summary>پنجره‌ی کاری دومِ روز (شیفت دوپاره) — null یعنی شیفت تک‌بازه‌ای است</summary>
    public static (DateTime From, DateTime To)? WorkWindow2(DateTime date, WorkDayRule rule)
        => rule is { Start2: { } s2, End2: { } e2 } && e2 > s2
            ? ((DateTime From, DateTime To)?)(date.Date + s2, date.Date + e2)
            : null;

    /// <summary>زمان پایانِ مؤثرِ شیفت (پایانِ آخرین پنجره — اگر شیفت شب است، پایان در روز بعد)</summary>
    public static DateTime ShiftEnd(DateTime date, WorkDayRule rule)
        => WorkWindow2(date, rule)?.To ?? WorkWindow1(date, rule).To;

    /// <summary>مدت کاریِ یک روز بر اساس قاعده (جمعِ پنجره‌ها؛ با پشتیبانی شیفت شب و شیفت دوپاره)</summary>
    public static TimeSpan ShiftDuration(WorkDayRule rule)
    {
        var d = rule.End < rule.Start ? TimeSpan.FromDays(1) - rule.Start + rule.End : rule.End - rule.Start;
        if (rule is { Start2: { } s2, End2: { } e2 } && e2 > s2) d += e2 - s2;
        return d;
    }

    /// <summary>
    /// بازه‌ی اضافه‌کاریِ روز (حالت ۱) به‌عنوان بازه‌ی زمانیِ مطلق:
    /// [تاریخ + شروع، تاریخ (+ یک روز اگر عبور از نیمه‌شب) + پایان]
    /// </summary>
    public static (DateTime From, DateTime To)? OvertimeWindow(DateTime date, WorkDayRule rule)
    {
        if (rule.OvertimeMode != OT_Window || rule.OvertimeStart is not { } os || rule.OvertimeEnd is not { } oe)
            return null;
        var from = date.Date + os;
        var to = date.Date + (oe < os ? oe + TimeSpan.FromDays(1) : oe);
        return (from, to);
    }

    /// <summary>آیا لحظه‌ی «when» داخل بازه‌ی اضافه‌کاریِ روزِ «date» است؟ (حالت ۱)</summary>
    public static bool InOvertimeWindow(DateTime when, DateTime date, WorkDayRule rule)
    {
        var w = OvertimeWindow(date, rule);
        return w is { } ww && when >= ww.From && when < ww.To;
    }

    /// <summary>آیا حضورِ لحظه‌ایِ «when» در تعطیلِ «date» به‌عنوان اضافه‌کاری مجاز است؟ (برای تصمیمِ لحظه‌ی ورود)</summary>
    public static bool OvertimeAllowedAt(DateTime when, DateTime date, WorkDayRule rule)
    {
        if (rule.IsWorkday) return true; // روز کاری: قواعدِ جدا در کنترلر/ارزیابی اعمال می‌شود
        if (rule.OvertimeMode == OT_WholeDay) return true;
        if (rule.OvertimeMode == OT_Window) return InOvertimeWindow(when, date, rule);
        return rule.OvertimeHours > 0;
    }

    /// <summary>تداخلِ دقیقه‌ایِ دو بازه (هر دو بسته)</summary>
    public static int OverlapMinutes(DateTime a1, DateTime a2, DateTime b1, DateTime b2)
    {
        var from = a1 > b1 ? a1 : b1;
        var to = a2 < b2 ? a2 : b2;
        return to > from ? (int)Math.Floor((to - from).TotalMinutes) : 0;
    }

    /// <summary>
    /// ارزیابی یک بازه‌ی تردد [enterAt, exitAt] (exitAt می‌تواند null باشد = هنوز در محل)
    /// بر اساس قاعده‌ی روز. مقدار برگشتی:
    ///  - workMin: دقیقه‌ی حاضر در محل (کار عادی)
    ///  - overtimeMin: دقیقه‌ی اضافه‌کاری مجاز/ثبت‌شده
    ///  - unauthorizedMin: دقیقه‌ی تردد غیرمجاز
    ///  - isUnauthorized: آیا کل بازه غیرمجاز است؟
    ///
    /// قواعدِ اضافه‌کاری:
    ///  - «بدون»: روز کاری → کارِ بعد از پایان = اضافه‌کاری (نامحدود)؛ تعطیل → هر تردد غیرمجاز
    ///  - «بازه زمانی»: فقط حضورِ داخلِ بازه = اضافه‌کاری؛ بیرونِ بازه (خارج از شیفت) = تردد غیرمجاز
    ///  - «کل روز»: همه‌ی حضور = اضافه‌کاری (نه کار عادی، نه غیرمجاز، نه کسری)
    ///  - «سقف ساعتی»: تا سقفِ ساعتی = اضافه‌کاری؛ مازاد = تردد غیرمجاز
    /// </summary>
    public static (int workMin, int overtimeMin, int unauthorizedMin, bool isUnauthorized)
        EvaluateSegment(WorkDayRule rule, DateTime date, DateTime enterAt, DateTime? exitAt, DateTime asOf)
    {
        date = date.Date;
        var dayEnd = ShiftEnd(date, rule);

        var end = exitAt ?? asOf;
        // بازه‌ی بازِ فراموش‌شده (خروج ثبت نشده و روزِ کاری تمام شده):
        // بیش از پایانِ مقررِ شیفت حساب نمی‌شود تا کارکردِ غیرواقعیِ بازه‌ی باز تولید نکند
        if (exitAt == null && asOf > date.AddDays(1))
            end = end > dayEnd ? dayEnd : end;
        if (end < enterAt) end = enterAt;
        var totalMin = Math.Max(0, (int)Math.Floor((end - enterAt).TotalMinutes));

        // ۰) اضافه‌کاریِ کلِ روز: همه‌ی حضورِ روز = اضافه‌کاری
        if (rule.OvertimeMode == OT_WholeDay)
            return (0, totalMin, 0, false);

        var win = OvertimeWindow(date, rule);

        if (!rule.IsWorkday)
        {
            // ---- تعطیل/جمعه ----
            if (win is { } w)
            {
                // بازه‌ی اضافه‌کاری در تعطیل: داخلِ بازه = اضافه‌کاری، بیرونِ بازه = تردد غیرمجاز
                var inWin = OverlapMinutes(enterAt, end, w.From, w.To);
                var outWin = totalMin - inWin;
                return (0, inWin, outWin, outWin > 0 && inWin == 0);
            }
            if (rule.OvertimeMode == OT_HourCap || rule.OvertimeHours > 0)
            {
                // سقفِ ساعتی: تا سقف = اضافه‌کاری، مازاد = تردد غیرمجاز
                var allowedMin = (int)Math.Round(rule.OvertimeHours * 60);
                if (totalMin <= allowedMin)
                    return (totalMin, totalMin, 0, false);
                return (allowedMin, allowedMin, totalMin - allowedMin, false);
            }
            // بدونِ اضافه‌کاری مجاز: کلِ حضور = تردد غیرمجاز
            return (0, 0, totalMin, true);
        }

        // ---- روز کاری ----

        // ورود بعد از پایان کاملِ روز (حتی با grace):
        // اگر داخلِ بازه‌ی اضافه‌کاریِ مشخص‌شده باشد = اضافه‌کاری، وگرنه = تردد غیرمجاز
        if (enterAt > dayEnd.AddMinutes(rule.GraceMinutes))
        {
            if (win is { } wEntry && enterAt >= wEntry.From && enterAt < wEntry.To)
            {
                var inWin = OverlapMinutes(enterAt, end, wEntry.From, wEntry.To);
                return (0, inWin, totalMin - inWin, false);
            }
            return (0, 0, totalMin, true);
        }

        int workMin = 0, rawOvertime = 0, unauthorizedMin = 0;

        // پنجره‌های کاری روز (پنجره‌ی دوم فقط در شیفت دوپاره)
        var (f1, t1) = WorkWindow1(date, rule);
        var w2 = WorkWindow2(date, rule);

        // ۱) حضور زودتر از شروعِ شیفت
        if (enterAt < f1)
        {
            var earlyMin = (int)Math.Floor((f1 - enterAt).TotalMinutes);
            var inWin = win is { } w1e ? OverlapMinutes(enterAt, f1, w1e.From, w1e.To) : 0;
            var earlyOut = earlyMin - inWin;
            rawOvertime += inWin;                 // بخشِ داخلِ بازه‌ی اضافه‌کاری = اضافه‌کاری
            if (earlyOut > 120)
                unauthorizedMin += earlyOut;       // حضور زودرس بیش از ۲ ساعت (بیرون از بازه) = تردد غیرمجاز
            // حضور زودترِ کم‌تر از ۲ ساعت (بیرون از بازه) = بی‌اعتبار: نه کار، نه تخلف
        }

        // ۲) بخشِ داخلِ پنجره‌ی اول — حضورِ این بخش = کار (مگر در بازه‌ی اضافه‌کاری = اضافه‌کاری)
        var s1b = enterAt > f1 ? enterAt : f1;
        var e1b = end < t1 ? end : t1;
        if (e1b > s1b)
        {
            var span1 = (int)Math.Floor((e1b - s1b).TotalMinutes);
            var otIn = win is { } w2e ? OverlapMinutes(s1b, e1b, w2e.From, w2e.To) : 0;
            workMin += span1 - otIn;
            rawOvertime += otIn;
        }

        // ۲‌ب) بخشِ داخلِ پنجره‌ی دوم (شیفت دوپاره) — حضور در فاصله‌ی بین دو پنجره (نهار و...) نه کار است نه تخلف
        if (w2 is { } ww)
        {
            var s2b = enterAt > ww.From ? enterAt : ww.From;
            var e2b = end < ww.To ? end : ww.To;
            if (e2b > s2b)
            {
                var span2 = (int)Math.Floor((e2b - s2b).TotalMinutes);
                var otIn2 = win is { } w2f ? OverlapMinutes(s2b, e2b, w2f.From, w2f.To) : 0;
                workMin += span2 - otIn2;
                rawOvertime += otIn2;
            }
        }

        // ۳) حضور بعد از پایانِ شیفت
        if (end > dayEnd)
        {
            var afterMin = (int)Math.Floor((end - dayEnd).TotalMinutes);
            if (win is { } w3)
            {
                // بازه‌ی اضافه‌کاری مشخص شده: داخلِ بازه = اضافه‌کاری، بیرونِ بازه = تردد غیرمجاز
                var inWin = OverlapMinutes(dayEnd, end, w3.From, w3.To);
                rawOvertime += inWin;
                unauthorizedMin += afterMin - inWin;
            }
            else
            {
                rawOvertime += afterMin; // بدون تنظیمِ اضافه‌کاری: کارِ بعد از پایان = اضافه‌کاری
            }
        }

        // ۴) سقفِ ساعتیِ اضافه‌کاری (حالت ۳ بدون بازه): مازاد بر سقف = تردد غیرمجاز
        var overtimeMin = rawOvertime;
        if (rule.OvertimeMode == OT_HourCap && win == null)
        {
            var allowedMin = (int)Math.Round(rule.OvertimeHours * 60);
            var over = Math.Max(0, overtimeMin - allowedMin);
            overtimeMin -= over;
            unauthorizedMin += over;
        }

        return (workMin, overtimeMin, unauthorizedMin, false);
    }
}

/// <summary>
/// بازحساب مشترک فیلدهای تجمیعی رکورد روزانه از روی بازه‌ها + قاعده‌ی کاری روز.
/// هم در لحظه‌ی ورود/خروج (Controller) و هم در بازحساب بعد از تایید مرخصی (RecalcService) استفاده می‌شود.
/// </summary>
public static class AttendanceMath
{
    /// <summary>دقیقه‌ی موظفی یک روز کاری بر اساس قاعده (روزِ «کل روز اضافه‌کاری» موظفیتِ عادی ندارد)</summary>
    public static int ScheduledMinutes(WorkDayRule rule) =>
        rule.IsWorkday && rule.OvertimeMode != WorkRules.OT_WholeDay
            ? (int)WorkRules.ShiftDuration(rule).TotalMinutes
            : 0;

    public static void Recompute(AttendanceRecord rec, List<AttendanceSegment> segs, WorkDayRule rule,
        List<LeaveRequest> hourlies, bool hasDailyLeave, DateTime asOf)
    {
        segs = segs.Where(s => s.EnterAt.HasValue).OrderBy(s => s.Seq).ToList();

        if (segs.Count == 0)
        {
            rec.EnterAt = null;
            rec.ExitAt = null;
            rec.WorkMinutes = 0;
            rec.CoveredGapMinutes = 0;
            rec.LateMinutes = 0;
            rec.EarlyLeaveMinutes = 0;
            rec.OvertimeMinutes = 0;
            rec.UnauthorizedMinutes = 0;
            rec.EnterStatus = null;
        }
        else
        {
            rec.EnterAt = segs.First().EnterAt;
            rec.ExitAt = segs.Last().ExitAt;
            rec.LateMinutes = segs.Sum(s => s.LateMinutes);
            rec.EnterStatus = segs.First().EnterStatus;
            rec.Note = segs.LastOrDefault(s => s.ExitAt.HasValue && !string.IsNullOrWhiteSpace(s.Note))?.Note;

            int work = 0, overtime = 0, unauthorized = 0;
            foreach (var s in segs)
            {
                var (w, o, u, unauth) = WorkRules.EvaluateSegment(rule, rec.WorkDate, s.EnterAt!.Value, s.ExitAt, asOf);
                work += w; overtime += o; unauthorized += u;
                s.IsUnauthorized = unauth;
                if (s.ExitAt.HasValue) s.OvertimeMinutes = o;
            }
            rec.WorkMinutes = work;
            rec.OvertimeMinutes = overtime;
            rec.UnauthorizedMinutes = unauthorized;

            // غیبت پوشش‌شده: هر بازه‌ی [خروج، ورود بعدی] که پوشش دارد + خروج آخر تا پایان شیفت (یا اکنون)
            int covered = 0;
            var scheduledEnd = WorkRules.ShiftEnd(rec.WorkDate, rule);
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
                    var gapEnd = scheduledEnd < asOf ? scheduledEnd : asOf;
                    if (s.ExitCovered)
                        covered += Math.Max(0, (int)(gapEnd - s.ExitAt.Value).TotalMinutes);
                }
            }
            covered += LateCoveredMinutes(rule, rec.WorkDate, segs.FirstOrDefault(s => s.EnterAt.HasValue)?.EnterAt, hourlies);
            rec.CoveredGapMinutes = covered;

            // خروج زودهنگام: آخرین خروجِ بسته، اگر قبل از پایان شیفت روز کاری بوده
            // (روزِ «کل روز اضافه‌کاری» خروج زودهنگام ندارد — همه‌ی حضور اضافه‌کار است)
            rec.EarlyLeaveMinutes = 0;
            var lastClosed = segs.LastOrDefault(s => s.ExitAt.HasValue);
            if (rule.IsWorkday && rule.OvertimeMode != WorkRules.OT_WholeDay && lastClosed?.ExitAt.HasValue == true)
            {
                var diff = (int)(scheduledEnd - lastClosed.ExitAt.Value).TotalMinutes;
                if (diff > rule.GraceMinutes) rec.EarlyLeaveMinutes = diff;
            }
        }

        rec.HasApprovedLeave = hasDailyLeave || hourlies.Count > 0;

        // کسری
        if (hasDailyLeave)
        {
            rec.DeficitMinutes = 0;
            if (string.IsNullOrEmpty(rec.FinalStatus) || rec.FinalStatus == "Present")
                rec.FinalStatus = "LeaveDay";
        }
        else if (!rule.IsWorkday || rule.OvertimeMode == WorkRules.OT_WholeDay)
        {
            // تعطیل یا روزِ «کل روز اضافه‌کاری»: موظفیتِ عادیِ روزانه ندارد → کسری صفر
            // (حضورِ غیرمجاز جداگانه ثبت شده)
            rec.DeficitMinutes = 0;
        }
        else
        {
            var deficit = ScheduledMinutes(rule) - rec.WorkMinutes - rec.CoveredGapMinutes;
            rec.DeficitMinutes = Math.Max(0, deficit);
        }

        if (string.IsNullOrEmpty(rec.FinalStatus))
            rec.FinalStatus = "Present";
    }

    /// <summary>بخش دیررس بودن [شروع روز، اولین ورود] — اگر مرخصی/ماموریت ساعتی تاییدشده آن را پوشش داده باشد</summary>
    public static int LateCoveredMinutes(WorkDayRule rule, DateTime date, DateTime? firstEnter, List<LeaveRequest> covers)
    {
        if (!rule.IsWorkday || firstEnter == null) return 0;
        var schedStart = date.Add(rule.Start);
        if (firstEnter.Value <= schedStart) return 0;
        var gs = schedStart.TimeOfDay;
        var ge = firstEnter.Value.TimeOfDay;
        return covers.Any(l => l.StartTime <= gs && l.EndTime >= ge)
            ? Math.Max(0, (int)(firstEnter.Value - schedStart).TotalMinutes)
            : 0;
    }
}
