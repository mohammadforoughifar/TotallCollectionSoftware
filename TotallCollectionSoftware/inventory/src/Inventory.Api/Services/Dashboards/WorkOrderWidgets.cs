using Inventory.Api.Data;
using Inventory.Api.Services.Core;
using Inventory.Api.Services.ItAssets;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Dashboards;

/// <summary>
/// ویجت‌های «دستور کار» و «پروژه کاری» برای داشبورد شخصی.
///
/// <para>
/// چرا یک سرویس جدا؟ موتور قبلی (<see cref="WidgetDataService"/>) یک <c>switch</c> با ۴۸ شاخه
/// برای همهٔ ماژول‌هاست. دستور کار به‌تنهایی ۲۳ ویجت دارد و منطق مشترک زیادی (دامنه، نتیجه،
/// بازهٔ شمسی) دارد؛ نگه‌داشتن آن در یک فایل مستقل، هم خوانایی و هم تست‌پذیری را بالا می‌برد.
/// </para>
///
/// <para>
/// <b>سه اصلاح مهم نسبت به ویجت‌های قبلی:</b>
/// <list type="number">
///   <item><b>دامنهٔ کاربر:</b> ویجت <c>wo-by-status</c> قبلاً بدون هیچ فیلتر کاربری، کل دستورهای
///   سازمان را می‌شمرد. اینجا همهٔ ویجت‌ها از <see cref="Orders"/> عبور می‌کنند که دامنه شخصی دارد.</item>
///   <item><b>شرکت فعال:</b> جدول <c>WorkOrder</c> فیلتر سراسری شرکتی ندارد (فقط <c>DeletedAt</c>)،
///   پس اینجا صریحاً روی <c>CompanyId</c> فیلتر می‌شود — مثل <c>WorkOrdersController.Stats</c>.</item>
///   <item><b>ساعت تهران:</b> به‌جای <c>DateTime.Now</c> از <see cref="TehranClock.Now"/> استفاده
///   می‌شود تا مرز «امروز» روی سرورِ غیرتهرانی جابه‌جا نشود.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>دامنه‌ها عمداً فقط شخصی‌اند</b> (محول‌شده به من / صادرشده توسط من / هر دو). نمای «کل شرکت»
/// به یک مجوز تازه (<c>WorkOrders.ViewAll</c>) نیاز دارد و در این موج اضافه نشده تا راه نشت داده
/// باز نشود.
/// </para>
/// </summary>
public interface IWorkOrderWidgets
{
    /// <summary>آیا این کلید را این سرویس پاسخ می‌دهد؟</summary>
    bool Handles(string key);

    Task<WidgetDataDto> GetAsync(WidgetDefDto def, int userId, DashChartType? chartType,
        DashRange range, IReadOnlyDictionary<string, string> config, CancellationToken ct = default);
}

public sealed class WorkOrderWidgets : IWorkOrderWidgets
{
    private readonly AppDbContext _db;
    public WorkOrderWidgets(AppDbContext db) => _db = db;

    /// <summary>فهرست صریح کلیدها — نه پیشوند. کلید ناشناخته هرگز به اینجا نمی‌رسد.</summary>
    /// <summary>کلیدهای پشتیبانی‌شده — عمومی تا تست بتواند تطابق کاتالوگ و سرویس را بررسی کند.</summary>
    public static readonly IReadOnlySet<string> Keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // ویجت‌های موجود (اصلاح‌شده)
        "wo-open", "wo-overdue", "wo-by-status",
        // خانوادهٔ A — میز کار من
        "wo-my-inbox", "wo-my-unseen", "wo-due-today", "wo-due-week", "wo-my-next",
        // خانوادهٔ B — سلامت جریان
        "wo-ontime-rate", "wo-outcome-mix", "wo-closed-nodone", "wo-open-age",
        "wo-created-vs-closed", "wo-first-response",
        // خانوادهٔ C — تیم
        "wo-pending-approval", "wo-pending-approval-list", "wo-team-load",
        "wo-team-scoreboard", "wo-unanswered",
        // خانوادهٔ D/E — کیفیت و پروژه
        "wo-extension-hot", "wo-checklist-rate", "wo-source-mix", "wp-my-progress"
    };

    public bool Handles(string key) => Keys.Contains(key);

    // =====================================================================
    //  ورودی
    // =====================================================================

    public async Task<WidgetDataDto> GetAsync(WidgetDefDto def, int userId, DashChartType? chartType,
        DashRange range, IReadOnlyDictionary<string, string> config, CancellationToken ct = default)
    {
        var now = TehranClock.Now();
        var res = new WidgetDataDto
        {
            Key = def.Key,
            Kind = def.Kind,
            Title = def.Title,
            Unit = string.IsNullOrEmpty(def.Unit) ? null : def.Unit,
            Drilldown = string.IsNullOrEmpty(def.Drilldown) ? null : def.Drilldown,
            ChartType = chartType ?? def.ChartTypes.FirstOrDefault(),
            GeneratedAt = now
        };

        try
        {
            var scope = ScopeOf(config);
            var limit = Int(config, "limit", 10);
            var companyId = _db.ActiveCompanyId;
            var (from, to) = Bounds(range, 0, now);

            // fail closed: کاربری که به هیچ شرکتی دسترسی ندارد (۰)، یا درخواستی که زمینهٔ شرکتی
            // ندارد (null)، نباید داده ببیند. پیام صریح می‌دهیم تا کارت «۰» گمراه‌کننده نشان ندهد.
            // داشبورد همیشه درخواست HTTP کاربر واردشده است؛ پس null اینجا یعنی اشکال در میان‌افزار،
            // نه اجرای پس‌زمینه — و در آن حالت هم نباید دادهٔ همهٔ شرکت‌ها لو برود.
            if (companyId is null or 0)
            {
                res.Error = "شرکت فعال مشخص نیست. از نوار بالا یک شرکت انتخاب کنید.";
                return res;
            }

            switch (def.Key.ToLowerInvariant())
            {
                // ==================== ویجت‌های موجود — با دامنهٔ اصلاح‌شده ====================
                case "wo-open":
                {
                    var rows = await LoadAsync(Orders(userId, companyId.Value, scope), now, ct);
                    var open = rows.Count(r => r.Status == "Open");
                    Kpi(res, open, rows.Count, "کل دستورهای من");
                    res.Unit = "دستور";
                    break;
                }
                case "wo-overdue":
                {
                    var rows = await LoadAsync(Orders(userId, companyId.Value, scope), now, ct);
                    var late = rows.Count(r => r.Tone == WorkOrderTone.Late);
                    var open = rows.Count(r => r.Status == "Open");
                    Kpi(res, late, open, "دستور باز");
                    res.Unit = "دستور";
                    break;
                }
                case "wo-by-status":
                {
                    var rows = await LoadAsync(Orders(userId, companyId.Value, scope), now, ct);
                    var ser = new DashSeriesDto { Name = "دستور کار" };
                    foreach (var s in new[] { "Open", "Closed" })
                    {
                        ser.Labels.Add(s == "Open" ? "باز" : "بسته");
                        ser.Values.Add(rows.Count(r => r.Status == s));
                    }
                    res.Series.Add(ser);
                    break;
                }

                // ==================== خانوادهٔ A — میز کار من ====================
                case "wo-my-inbox":
                {
                    var rows = await LoadAsync(Orders(userId, companyId.Value, WoScope.Mine), now, ct);
                    var open = rows.Count(r => r.Status == "Open");
                    Kpi(res, open, rows.Count(r => r.Status == "Open" && r.Tone == WorkOrderTone.Late),
                        "مورد گذشته از مهلت");
                    res.Unit = "دستور";
                    break;
                }
                case "wo-my-unseen":
                {
                    // دستورهایی که به من واگذار شده و هنوز باز نکرده‌ام.
                    var visible = Orders(userId, companyId.Value, WoScope.Mine).Where(w => w.Status == "Open");
                    var cnt = await _db.WorkOrderAssignees.AsNoTracking()
                        .CountAsync(a => a.UserId == userId && a.SeenAt == null
                                         && visible.Any(w => w.Id == a.OrderId), ct);
                    Kpi(res, cnt, null, null);
                    res.Unit = "دستور";
                    break;
                }
                case "wo-due-today":
                {
                    var today = now.Date;
                    var tomorrow = today.AddDays(1);
                    var q = Orders(userId, companyId.Value, scope).Where(w => w.Status == "Open");
                    var cnt = await q.CountAsync(w => w.DueAt >= today && w.DueAt < tomorrow, ct);
                    var late = await q.CountAsync(w => w.DueAt >= today && w.DueAt < tomorrow && w.DueAt < now, ct);
                    Kpi(res, cnt, late, "مورد گذشته از ساعت مقرر");
                    res.Unit = "دستور";
                    break;
                }
                case "wo-due-week":
                {
                    var end = now.Date.AddDays(8);   // فردا تا پایان ۷ روز بعد
                    var start = now.Date.AddDays(1);
                    var cnt = await Orders(userId, companyId.Value, scope)
                        .CountAsync(w => w.Status == "Open" && w.DueAt >= start && w.DueAt < end, ct);
                    Kpi(res, cnt, null, null);
                    res.Unit = "دستور";
                    res.SubLabel = "از فردا تا ۷ روز آینده";
                    break;
                }
                case "wo-my-next":
                {
                    var rows = (await LoadAsync(
                            Orders(userId, companyId.Value, WoScope.Mine).Where(w => w.Status == "Open"), now, ct))
                        .OrderBy(r => r.DueAt).ThenByDescending(r => r.Priority).Take(limit).ToList();

                    res.Columns = new List<string> { "شماره", "عنوان", "مهلت", "مانده", "اولویت" };
                    res.Rows = rows.Select(r =>
                    {
                        var days = (int)Math.Floor((r.DueAt.Date - now.Date).TotalDays);
                        var left = days < 0 ? $"{Fa.Digits((-days).ToString())} روز گذشته"
                                 : days == 0 ? "امروز"
                                 : $"{Fa.Digits(days.ToString())} روز";
                        return new DashRowDto
                        {
                            Href = $"work-orders?open={r.Id}",
                            Tone = days < 0 ? "danger" : days == 0 ? "warning" : null,
                            Cells = new List<string>
                            {
                                r.Number, r.Title, Fa.Digits(PersianDate.ToShort(r.DueAt)),
                                left, WorkOrderPriority.ToFa(r.Priority)
                            }
                        };
                    }).ToList();
                    break;
                }

                // ==================== خانوادهٔ B — سلامت جریان ====================
                case "wo-ontime-rate":
                {
                    var rows = await LoadAsync(
                        Orders(userId, companyId.Value, scope).Where(w => w.CreatedAt >= from && w.CreatedAt < to),
                        now, ct);
                    var (rate, closed, onTime) = WorkOrderTone.OnTimeRate(rows.Select(r => r.Tone));
                    // وقتی هیچ دستور بسته‌ای نیست، مقدار را null می‌گذاریم تا کارت «—» نشان دهد؛
                    // نمایش ۰٪ در این حالت گمراه‌کننده است (انگار عملکرد صفر بوده).
                    res.Value = rate;
                    res.Unit = "٪";
                    res.SubValue = closed;
                    // «تعیین‌تکلیف‌شده» نه «بسته‌شده»: دستوری که همهٔ گیرندگانش «انجام شد» زده‌اند
                    // ولی دستوردهنده هنوز نبسته، طبق قاعدهٔ ماژول جزو نتیجه‌دارها حساب می‌شود.
                    res.SubLabel = closed == 0
                        ? "دستور نتیجه‌دار در این بازه نیست"
                        : $"دستور نتیجه‌دار ({Fa.Digits(onTime.ToString())} به‌موقع)";
                    res.HigherIsBetter = true;   // افزایش = خوب
                    break;
                }
                case "wo-outcome-mix":
                {
                    var rows = await LoadAsync(
                        Orders(userId, companyId.Value, scope).Where(w => w.CreatedAt >= from && w.CreatedAt < to),
                        now, ct);
                    var ser = new DashSeriesDto { Name = "دستور کار" };
                    foreach (var t in WorkOrderTone.Order)
                    {
                        ser.Labels.Add(WorkOrderTone.Fa(t));
                        ser.Values.Add(rows.Count(r => r.Tone == t));
                    }
                    res.Series.Add(ser);
                    break;
                }
                case "wo-closed-nodone":
                {
                    var rows = await LoadAsync(
                        Orders(userId, companyId.Value, scope).Where(w => w.CreatedAt >= from && w.CreatedAt < to),
                        now, ct);
                    var cnt = rows.Count(r => r.Tone == WorkOrderTone.ClosedNoDone);
                    var resolved = rows.Count(r => WorkOrderTone.IsResolved(r.Tone));
                    Kpi(res, cnt, resolved, "از کل نتیجه‌دارها");
                    res.Unit = "دستور";
                    res.HigherIsBetter = false;  // کاهش = خوب
                    break;
                }
                case "wo-open-age":
                {
                    var rows = (await LoadAsync(Orders(userId, companyId.Value, scope).Where(w => w.Status == "Open"),
                            now, ct))
                        .Where(r => r.DueAt < now).ToList();

                    var buckets = new (string Label, Func<int, bool> Match)[]
                    {
                        ("۱ روز", d => d <= 1),
                        ("۲ تا ۷ روز", d => d is >= 2 and <= 7),
                        ("۸ تا ۳۰ روز", d => d is >= 8 and <= 30),
                        ("بیش از ۳۰ روز", d => d > 30)
                    };
                    var ser = new DashSeriesDto { Name = "معوق‌ها" };
                    foreach (var b in buckets)
                    {
                        ser.Labels.Add(b.Label);
                        ser.Values.Add(rows.Count(r => b.Match((int)Math.Floor((now - r.DueAt).TotalDays))));
                    }
                    res.Series.Add(ser);
                    break;
                }
                case "wo-created-vs-closed":
                {
                    var months = LastMonths(12, now);
                    var mFrom = months[0].From;
                    var mTo = months[^1].To;
                    var rows = await Orders(userId, companyId.Value, scope)
                        .Where(w => (w.CreatedAt >= mFrom && w.CreatedAt < mTo)
                                 || (w.ClosedAt != null && w.ClosedAt >= mFrom && w.ClosedAt < mTo))
                        .Select(w => new { w.CreatedAt, w.ClosedAt }).ToListAsync(ct);

                    var created = new DashSeriesDto { Name = "ایجادشده" };
                    var closed = new DashSeriesDto { Name = "بسته‌شده" };
                    foreach (var m in months)
                    {
                        created.Labels.Add(m.Label); closed.Labels.Add(m.Label);
                        created.Values.Add(rows.Count(r => r.CreatedAt >= m.From && r.CreatedAt < m.To));
                        closed.Values.Add(rows.Count(r => r.ClosedAt != null && r.ClosedAt >= m.From && r.ClosedAt < m.To));
                    }
                    res.Series.Add(created);
                    res.Series.Add(closed);
                    break;
                }
                case "wo-first-response":
                {
                    var rows = await LoadAsync(
                        Orders(userId, companyId.Value, scope).Where(w => w.CreatedAt >= from && w.CreatedAt < to),
                        now, ct);
                    // میانگین فاصلهٔ «ثبت دستور» تا «اولین پاسخِ همان دستور» بر حسب ساعت.
                    // تفریق تاریخ در حافظه انجام می‌شود چون ترجمهٔ آن بین SQL Server و SQLite یکسان نیست.
                    var spans = rows
                        .Select(r =>
                        {
                            var first = r.Assignees.Where(a => a.RepliedAt != null)
                                                   .Select(a => a.RepliedAt!.Value)
                                                   .DefaultIfEmpty(DateTime.MinValue).Min();
                            return first == DateTime.MinValue ? (double?)null : (first - r.CreatedAt).TotalHours;
                        })
                        .Where(h => h is >= 0).Select(h => h!.Value).ToList();

                    // بدون هیچ پاسخ‌شدی، «۰ ساعت» یعنی «پاسخ فوری» — که دروغ است. null ⇒ کارت «—».
                    res.Value = spans.Count == 0 ? null : Math.Round((decimal)spans.Average(), 1);
                    res.Unit = "ساعت";
                    res.SubValue = spans.Count;
                    res.SubLabel = "دستور دارای پاسخ";
                    res.HigherIsBetter = false;
                    break;
                }

                // ==================== خانوادهٔ C — تیم ====================
                case "wo-pending-approval":
                {
                    var visible = Orders(userId, companyId.Value, WoScope.Issued);
                    var cnt = await _db.WorkOrderAssignees.AsNoTracking()
                        .CountAsync(a => a.Done != null && a.OwnerDecision == null
                                         && visible.Any(w => w.Id == a.OrderId), ct);
                    Kpi(res, cnt, null, null);
                    res.Unit = "پاسخ";
                    res.SubLabel = "پاسخ‌هایی که هنوز تأیید یا رد نکرده‌ام";
                    break;
                }
                case "wo-pending-approval-list":
                {
                    var rows = (await LoadAsync(Orders(userId, companyId.Value, WoScope.Issued), now, ct))
                        .SelectMany(r => r.Assignees
                            .Where(a => a.Done != null && a.OwnerDecision == null)
                            .Select(a => (Order: r, Asg: a)))
                        .OrderBy(x => x.Order.DueAt).Take(limit).ToList();

                    res.Columns = new List<string> { "شماره", "گیرنده", "نتیجه", "تاریخ پاسخ", "مهلت" };
                    res.Rows = rows.Select(x => new DashRowDto
                    {
                        Href = $"work-orders?open={x.Order.Id}",
                        Tone = x.Order.DueAt < now ? "warning" : null,
                        Cells = new List<string>
                        {
                            x.Order.Number, x.Asg.Name,
                            x.Asg.Done == true ? "انجام شد" : "انجام نشد",
                            x.Asg.RepliedAt is null ? "—" : Fa.Digits(PersianDate.ToShort(x.Asg.RepliedAt.Value)),
                            Fa.Digits(PersianDate.ToShort(x.Order.DueAt))
                        }
                    }).ToList();
                    break;
                }
                case "wo-team-load":
                {
                    var rows = (await LoadAsync(
                            Orders(userId, companyId.Value, WoScope.Issued).Where(w => w.Status == "Open"), now, ct))
                        .SelectMany(r => r.Assignees.Select(a => a.Name))
                        .GroupBy(n => string.IsNullOrWhiteSpace(n) ? "بی‌نام" : n.Trim())
                        .Select(g => new { Name = g.Key, Cnt = g.Count() })
                        .OrderByDescending(x => x.Cnt).Take(limit).ToList();

                    var ser = new DashSeriesDto { Name = "دستور باز" };
                    ser.Labels = rows.Select(r => r.Name).ToList();
                    ser.Values = rows.Select(r => (decimal)r.Cnt).ToList();
                    res.Series.Add(ser);
                    break;
                }
                case "wo-team-scoreboard":
                {
                    var rows = await LoadAsync(
                        Orders(userId, companyId.Value, WoScope.Issued).Where(w => w.CreatedAt >= from && w.CreatedAt < to),
                        now, ct);

                    var stats = rows
                        .SelectMany(r => r.Assignees.Select(a => (r.DueAt, r.Status, A: a)))
                        .GroupBy(x => string.IsNullOrWhiteSpace(x.A.Name) ? "بی‌نام" : x.A.Name.Trim())
                        .Select(g =>
                        {
                            var total = g.Count();
                            var done = g.Count(x => x.A.Done == true);
                            var onTime = g.Count(x => x.A.Done == true && x.A.RepliedAt != null && x.A.RepliedAt <= x.DueAt);
                            var pending = g.Count(x => x.A.RepliedAt == null && x.Status == "Open");
                            return new { Name = g.Key, Total = total, Done = done, OnTime = onTime, Pending = pending };
                        })
                        .OrderByDescending(x => x.Total).ThenBy(x => x.Name).Take(limit).ToList();

                    res.Columns = new List<string> { "گیرنده", "کل", "انجام‌شده", "به‌موقع", "بی‌پاسخ", "٪ به‌موقع" };
                    res.Rows = stats.Select(s =>
                    {
                        var pct = s.Total == 0 ? 0 : (int)Math.Round(s.OnTime * 100m / s.Total);
                        return new DashRowDto
                        {
                            Tone = pct >= 85 ? "success" : pct < 50 ? "danger" : null,
                            Cells = new List<string>
                            {
                                s.Name, Fa.Digits(s.Total.ToString()), Fa.Digits(s.Done.ToString()),
                                Fa.Digits(s.OnTime.ToString()), Fa.Digits(s.Pending.ToString()),
                                Fa.Digits(pct.ToString()) + "٪"
                            }
                        };
                    }).ToList();
                    break;
                }
                case "wo-unanswered":
                {
                    var rows = await LoadAsync(
                        Orders(userId, companyId.Value, WoScope.Issued).Where(w => w.Status == "Open"), now, ct);

                    var stats = rows
                        .SelectMany(r => r.Assignees.Where(a => a.RepliedAt == null)
                                                    .Select(a => (r.DueAt, A: a)))
                        .GroupBy(x => string.IsNullOrWhiteSpace(x.A.Name) ? "بی‌نام" : x.A.Name.Trim())
                        .Select(g => new
                        {
                            Name = g.Key,
                            Cnt = g.Count(),
                            Late = g.Count(x => x.DueAt < now)
                        })
                        .OrderByDescending(x => x.Cnt).ThenBy(x => x.Name).Take(limit).ToList();

                    res.Columns = new List<string> { "گیرنده", "بی‌پاسخ", "گذشته از مهلت" };
                    res.Rows = stats.Select(s => new DashRowDto
                    {
                        Tone = s.Late > 0 ? "danger" : "warning",
                        Cells = new List<string>
                        {
                            s.Name, Fa.Digits(s.Cnt.ToString()), Fa.Digits(s.Late.ToString())
                        }
                    }).ToList();
                    break;
                }

                // ==================== خانوادهٔ D/E — کیفیت ====================
                case "wo-extension-hot":
                {
                    var rows = (await LoadAsync(
                            Orders(userId, companyId.Value, scope).Where(w => w.ExtensionCount >= 3), now, ct))
                        .OrderByDescending(r => r.ExtensionCount).ThenBy(r => r.DueAt).Take(limit).ToList();

                    res.Columns = new List<string> { "شماره", "عنوان", "تمدید", "مهلت فعلی", "وضعیت" };
                    res.Rows = rows.Select(r => new DashRowDto
                    {
                        Href = $"work-orders?open={r.Id}",
                        Tone = r.ExtensionCount >= 5 ? "danger" : "warning",
                        Cells = new List<string>
                        {
                            r.Number, r.Title,
                            $"{Fa.Digits(r.ExtensionCount.ToString())} از ۵",
                            Fa.Digits(PersianDate.ToShort(r.DueAt)),
                            WorkOrderTone.Fa(r.Tone)
                        }
                    }).ToList();
                    break;
                }
                case "wo-checklist-rate":
                {
                    var visible = Orders(userId, companyId.Value, scope).Where(w => w.Status == "Open");
                    var items = await _db.WorkOrderChecklistItems.AsNoTracking()
                        .Where(c => visible.Any(w => w.Id == c.OrderId))
                        .Select(c => new { c.IsDone }).ToListAsync(ct);

                    var total = items.Count;
                    var done = items.Count(i => i.IsDone);
                    // چک‌لیستی وجود ندارد ⇒ «—»، نه «۰٪» (که یعنی همه‌چیز عقب است).
                    res.Value = total == 0 ? null : Math.Round(done * 100m / total, 1);
                    res.Unit = "٪";
                    res.SubValue = total;
                    res.SubLabel = total == 0 ? "چک‌لیستی روی دستورهای باز نیست" : "آیتم چک‌لیست";
                    res.HigherIsBetter = true;
                    break;
                }
                case "wo-source-mix":
                {
                    var rows = await Orders(userId, companyId.Value, scope)
                        .Where(w => w.CreatedAt >= from && w.CreatedAt < to)
                        .Select(w => new { w.SourceModule }).ToListAsync(ct);

                    var groups = rows
                        .GroupBy(r => SourceFa(r.SourceModule))
                        .Select(g => new { Label = g.Key, Cnt = g.Count() })
                        .OrderByDescending(g => g.Cnt).ToList();

                    var ser = new DashSeriesDto { Name = "دستور کار" };
                    ser.Labels = groups.Select(g => g.Label).ToList();
                    ser.Values = groups.Select(g => (decimal)g.Cnt).ToList();
                    res.Series.Add(ser);
                    break;
                }

                // ==================== پروژه کاری ====================
                case "wp-my-progress":
                {
                    // پروژه‌ها هم مثل دستور کار: فقط شرکت فعال، و فقط پروژه‌ای که خودم
                    // مالکش هستم یا عضوش هستم. عضویت در پروژه‌های شرکت دیگر بی‌اثر است.
                    var myProjects = _db.WorkProjectMembers.AsNoTracking()
                        .Where(m => m.UserId == userId).Select(m => m.ProjectId);
                    var projects = await _db.WorkProjects.AsNoTracking()
                        .Where(p => p.DeletedAt == null && p.Status == WorkProjectStatus.Open
                                    && p.CompanyId == companyId.Value
                                    && (p.OwnerUserId == userId || myProjects.Contains(p.Id)))
                        .OrderBy(p => p.DueAt).Take(limit)
                        .Select(p => new { p.Id, p.Number, p.Title, p.DueAt }).ToListAsync(ct);

                    if (projects.Count == 0) break;

                    var pids = projects.Select(p => p.Id).ToList();
                    // دستورهای داخل پروژه هم باید هم‌شرکت و حذف‌نشده باشند. بدون قید شرکت،
                    // یک دستورِ ناسازگار (شرکت دیگر ولی ProjectId مشترک) به آمار پروژه راه پیدا می‌کرد؛
                    // بدون قید حذف، دستور حذف‌شده مخرج پیشرفت را باد می‌کرد.
                    var orders = await _db.WorkOrders.AsNoTracking()
                        .Where(w => w.DeletedAt == null && w.CompanyId == companyId.Value
                                    && w.ProjectId != null && pids.Contains(w.ProjectId.Value))
                        .Select(w => new { w.Id, Pid = w.ProjectId!.Value, w.Status, w.Weight, w.DueAt })
                        .ToListAsync(ct);
                    var oids = orders.Select(o => o.Id).ToList();
                    var checks = oids.Count == 0
                        ? new List<(int OrderId, int Total, int Done)>()
                        : (await _db.WorkOrderChecklistItems.AsNoTracking()
                            .Where(c => oids.Contains(c.OrderId))
                            .GroupBy(c => c.OrderId)
                            .Select(g => new { Id = g.Key, Total = g.Count(), Done = g.Count(x => x.IsDone) })
                            .ToListAsync(ct))
                          .Select(x => (OrderId: x.Id, Total: x.Total, Done: x.Done)).ToList();
                    var cmap = checks.ToDictionary(c => c.OrderId);

                    res.Columns = new List<string> { "پروژه", "مهلت", "پیشرفت", "باز / کل", "معوق" };
                    res.Rows = projects.Select(p =>
                    {
                        var mine = orders.Where(o => o.Pid == p.Id).ToList();
                        var progresses = mine.Select(o =>
                        {
                            var c = cmap.GetValueOrDefault(o.Id);
                            return (o.Weight, WorkProjectProgress.OrderProgress(o.Status == "Closed", c.Total, c.Done));
                        }).ToList();
                        var pct = WorkProjectProgress.ProjectProgress(progresses);
                        var open = mine.Count(o => o.Status == "Open");
                        var late = mine.Count(o => o.Status == "Open" && o.DueAt < now);
                        return new DashRowDto
                        {
                            Href = $"work-projects?open={p.Id}",
                            Tone = late > 0 ? "danger" : p.DueAt < now ? "warning" : null,
                            Cells = new List<string>
                            {
                                p.Title, Fa.Digits(PersianDate.ToShort(p.DueAt)),
                                pct is null ? "—" : Fa.Digits(pct.Value.ToString()) + "٪",
                                $"{Fa.Digits(open.ToString())} / {Fa.Digits(mine.Count.ToString())}",
                                Fa.Digits(late.ToString())
                            }
                        };
                    }).ToList();
                    break;
                }

                default:
                    res.Error = "این ویجت دستور کار پیاده‌سازی نشده است.";
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // درخواست لغو شده (کاربر صفحه را بسته) — چیزی برنمی‌گردانیم.
            throw;
        }
        catch (Exception ex)
        {
            res.Error = "خطا در محاسبهٔ ویجت دستور کار: " + (ex.Message ?? "");
        }

        res.GeneratedAt = TehranClock.Now();
        return res;
    }

    // =====================================================================
    //  دامنه، بازه و بارگذاری
    // =====================================================================

    /// <summary>دامنهٔ دید کاربر. عمداً فقط شخصی — نمای «کل شرکت» مجوز جدا می‌خواهد.</summary>
    private enum WoScope { Mine, Issued, Both }

    private static WoScope ScopeOf(IReadOnlyDictionary<string, string> cfg)
    {
        if (!cfg.TryGetValue("scope", out var v) || string.IsNullOrWhiteSpace(v)) return WoScope.Both;
        v = v.Trim();
        // برچسب فارسی (چون UI مقدار را همان برچسب نشان می‌دهد) + نام انگلیسی برای فراخوانی مستقیم API
        return v switch
        {
            "محول‌شده به من" or "Mine" or "mine" => WoScope.Mine,
            "صادرشده توسط من" or "Issued" or "issued" => WoScope.Issued,
            _ => WoScope.Both
        };
    }

    /// <summary>
    /// کوئری پایهٔ دستورهای قابل دیدن برای این کاربر.
    /// حذف‌شده‌ها بیرون، شرکت فعال اعمال‌شده، و دامنهٔ شخصی برقرار.
    /// </summary>
    /// <summary>
    /// تنها دروازهٔ ورود به دادهٔ دستور کار. هر ویجتی که دستور کار می‌خواند باید از اینجا رد شود.
    ///
    /// <para>
    /// دو قید غیرقابل‌حذف:
    /// <list type="number">
    /// <item><b>شرکت فعال</b> — <paramref name="companyId"/> عمداً <c>int</c> است نه <c>int?</c>؛
    /// یعنی از نظر تایپ هم نمی‌توان فیلتر شرکت را رد کرد. فراخوان باید <c>companyId.Value</c> بدهد.</item>
    /// <item><b>گردش خودِ کاربر</b> — دستور یا صادرشده توسط اوست یا به او محول شده.
    /// هیچ مقداری از <paramref name="scope"/> به «کل شرکت» یا «کل بانک» باز نمی‌شود.</item>
    /// </list>
    /// این همان قاعده‌ای است که <c>WorkOrdersController.Stats</c> دارد؛ پس آمار داشبورد شخصی
    /// و آمار داخل ماژول از یک تعریف می‌آیند و واگرا نمی‌شوند.
    /// </para>
    /// </summary>
    private IQueryable<WorkOrder> Orders(int userId, int companyId, WoScope scope)
    {
        var q = _db.WorkOrders.AsNoTracking()
            .Where(w => w.DeletedAt == null && w.CompanyId == companyId);

        var assigned = _db.WorkOrderAssignees.Where(a => a.UserId == userId).Select(a => a.OrderId);
        return scope switch
        {
            WoScope.Mine => q.Where(w => assigned.Contains(w.Id)),
            WoScope.Issued => q.Where(w => w.OwnerUserId == userId),
            _ => q.Where(w => w.OwnerUserId == userId || assigned.Contains(w.Id))
        };
    }

    private sealed class AsgRow
    {
        public int UserId { get; set; }
        public string Name { get; set; } = "";
        public DateTime? SeenAt { get; set; }
        public DateTime? RepliedAt { get; set; }
        public bool? Done { get; set; }
        public string? OwnerDecision { get; set; }
    }

    private sealed class WoRow
    {
        public int Id { get; set; }
        public string Number { get; set; } = "";
        public string Title { get; set; } = "";
        public int OwnerUserId { get; set; }
        public DateTime DueAt { get; set; }
        public string Status { get; set; } = "Open";
        public DateTime CreatedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public int Priority { get; set; }
        public int ExtensionCount { get; set; }
        public string Tone { get; set; } = WorkOrderTone.Open;
        public List<AsgRow> Assignees { get; set; } = new();
    }

    /// <summary>
    /// فقط ستون‌های لازم را می‌خواند (نه کل موجودیت)، بعد گیرندگان را با یک کوئری می‌آورد
    /// و نتیجهٔ پنج‌حالته را از <see cref="WorkOrderTone"/> می‌گیرد.
    /// </summary>
    private async Task<List<WoRow>> LoadAsync(IQueryable<WorkOrder> q, DateTime now, CancellationToken ct)
    {
        var rows = await q.Select(w => new WoRow
        {
            Id = w.Id,
            Number = w.Number,
            Title = w.Title,
            OwnerUserId = w.OwnerUserId,
            DueAt = w.DueAt,
            Status = w.Status,
            CreatedAt = w.CreatedAt,
            ClosedAt = w.ClosedAt,
            Priority = w.Priority,
            ExtensionCount = w.ExtensionCount
        }).ToListAsync(ct);

        if (rows.Count == 0) return rows;

        var ids = rows.Select(r => r.Id).ToList();
        var asgs = await _db.WorkOrderAssignees.AsNoTracking()
            .Where(a => ids.Contains(a.OrderId))
            .Select(a => new { a.OrderId, a.UserId, a.Name, a.SeenAt, a.RepliedAt, a.Done, a.OwnerDecision })
            .ToListAsync(ct);

        var map = asgs.GroupBy(a => a.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var r in rows)
        {
            if (map.TryGetValue(r.Id, out var list))
                r.Assignees = list.Select(a => new AsgRow
                {
                    UserId = a.UserId,
                    Name = a.Name,
                    SeenAt = a.SeenAt,
                    RepliedAt = a.RepliedAt,
                    Done = a.Done,
                    OwnerDecision = a.OwnerDecision
                }).ToList();

            r.Tone = WorkOrderTone.Of(r.Status, r.DueAt, now,
                r.Assignees.Select(a => (a.Done, a.RepliedAt)).ToList());
        }
        return rows;
    }

    // =====================================================================
    //  کمکی‌ها
    // =====================================================================

    private static void Kpi(WidgetDataDto res, decimal value, decimal? sub, string? subLabel)
    {
        res.Value = value;
        if (sub is not null) { res.SubValue = sub; res.SubLabel = subLabel; }
    }

    private static int Int(IReadOnlyDictionary<string, string> cfg, string key, int def) =>
        cfg.TryGetValue(key, out var v) && int.TryParse(v, out var n) && n > 0 ? Math.Min(n, 50) : def;

    private static string SourceFa(string? module) => module switch
    {
        null or "" => "ثبت دستی",
        "InnerLetter" => "نامه داخلی",
        "OutgoingLetter" => "نامه صادره",
        "IncomingLetter" => "نامه وارده",
        "Document" => "مدرک آرشیو",
        "DevTeam" => "تیم توسعه",
        "ServerAlert" => "هشدار سرور",
        "MeetingMinutes" => "صورتجلسه",
        _ => module!
    };

    private sealed record MonthBucket(DateTime From, DateTime To, string Label);

    /// <summary>ماه‌های شمسی اخیر — سازگار با سطل‌بندی سایر ویجت‌های سامانه.</summary>
    private static List<MonthBucket> LastMonths(int count, DateTime now)
    {
        var (jy, jm, _) = PersianDate.FromGregorian(now);
        var list = new List<MonthBucket>();
        for (var i = count - 1; i >= 0; i--)
        {
            var (y, m) = PersianDate.AddMonths(jy, jm, -i);
            var (ny, nm) = PersianDate.AddMonths(y, m, 1);
            list.Add(new MonthBucket(PersianDate.ToGregorian(y, m, 1),
                                     PersianDate.ToGregorian(ny, nm, 1),
                                     PersianDate.MonthName(m)));
        }
        return list;
    }

    /// <summary>
    /// مرز بازه بر پایهٔ تقویم شمسی. هفته از <b>شنبه</b> شروع می‌شود
    /// (اشکال ویجت‌های قدیمی که از یکشنبه شروع می‌کردند اینجا تکرار نشده).
    /// </summary>
    private static (DateTime From, DateTime To) Bounds(DashRange r, int shift, DateTime now)
    {
        var (jy, jm, _) = PersianDate.FromGregorian(now);
        switch (r)
        {
            case DashRange.Today:
            {
                var d = now.Date.AddDays(shift);
                return (d, d.AddDays(1));
            }
            case DashRange.Week:
            {
                var offset = ((int)now.DayOfWeek + 1) % 7;   // شنبه = ۰
                var start = now.Date.AddDays(-offset).AddDays(7 * shift);
                return (start, start.AddDays(7));
            }
            case DashRange.Month:
            {
                var (y, m) = PersianDate.AddMonths(jy, jm, shift);
                var (ny, nm) = PersianDate.AddMonths(y, m, 1);
                return (PersianDate.ToGregorian(y, m, 1), PersianDate.ToGregorian(ny, nm, 1));
            }
            case DashRange.Quarter:
            {
                var startMonth = ((jm - 1) / 3) * 3 + 1;
                var (y, m) = PersianDate.AddMonths(jy, startMonth, 3 * shift);
                var (ny, nm) = PersianDate.AddMonths(y, m, 3);
                return (PersianDate.ToGregorian(y, m, 1), PersianDate.ToGregorian(ny, nm, 1));
            }
            case DashRange.Year:
            {
                var y = jy + shift;
                return (PersianDate.ToGregorian(y, 1, 1), PersianDate.ToGregorian(y + 1, 1, 1));
            }
            default:
                return (new DateTime(1970, 1, 1), now.Date.AddDays(1));
        }
    }
}
