using System.Diagnostics;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.Reports;

// =====================================================================
//  موتور اجرای گزارش
//
//  ۱) فیلترهای قابل ترجمه → WHERE در SQL (به‌همراه projection فقط
//     ستون‌های لازم). ستون‌های decimal در حافظه فیلتر می‌شوند چون
//     SQLite مقایسهٔ decimal را پشتیبانی نمی‌کند.
//  ۲) خواندن حداکثر MaxScan ردیف.
//  ۳) گروه‌بندی/تجمیع/مرتب‌سازی در حافظه.
//  ۴) قالب‌بندی خروجی به WidgetDataDto (همان رندرر ویجت داشبورد).
// =====================================================================

public static class ReportExecutor
{
    private const int SlotS = 10, SlotM = 10, SlotI = 10, SlotR = 5, SlotD = 5, SlotB = 4;

    private sealed class MeasureRef<T>
    {
        public ReportMeasureDto Dto { get; init; } = null!;
        public ReportColumn<T> Col { get; init; } = null!;
    }

    private sealed class GroupRow
    {
        public string Label { get; set; } = "";
        public List<string> DimVals { get; } = new();
        public List<RawRow> Rows { get; } = new();
        public int Count => Rows.Count;
    }

    public static async Task<ReportRunResultDto> ExecuteAsync<T>(
        IQueryable<T> source,
        IReadOnlyDictionary<string, ReportColumn<T>> cols,
        IReportDataset meta,
        ReportQueryDto q) where T : class
    {
        var sw = Stopwatch.StartNew();
        var res = new ReportRunResultDto
        {
            Data = new WidgetDataDto
            {
                Key = meta.Key,
                Title = string.IsNullOrWhiteSpace(q.Title) ? meta.Title : q.Title!
            }
        };

        try
        {
            var take = Math.Clamp(q.Take <= 0 ? 50 : q.Take, 1, 500);

            var dimKeys = (q.Columns ?? new List<string>()).Distinct().Where(cols.ContainsKey).ToList();
            var measures = (q.Measures ?? new List<ReportMeasureDto>())
                .Where(m => cols.ContainsKey(m.Field))
                .Select(m => new MeasureRef<T> { Dto = m, Col = cols[m.Field] })
                .ToList();

            if (dimKeys.Count == 0 && measures.Count == 0)
            {
                res.Data.Error = "حداقل یک ستون یا یک سنجه را انتخاب کنید.";
                return Finish(res, sw);
            }

            var filters = (q.Filters ?? new List<ReportFilterDto>())
                .Where(f => !string.IsNullOrEmpty(f.Field) && cols.ContainsKey(f.Field))
                .Select(f => (Col: cols[f.Field], F: f))
                .ToList();
            var sqlFilters = filters.Where(x => x.Col.ServerFilterable).ToList();
            var memFilters = filters.Where(x => !x.Col.ServerFilterable).ToList();

            // ---------- ستون‌های موردنیاز projection ----------
            var used = new List<string>();
            foreach (var k in dimKeys) if (!used.Contains(k)) used.Add(k);
            foreach (var m in measures) if (!used.Contains(m.Dto.Field)) used.Add(m.Dto.Field);
            foreach (var f in memFilters) if (!used.Contains(f.Col.Key)) used.Add(f.Col.Key);

            var slotIdx = new Dictionary<ReportSlot, int>();
            var slotOf = new Dictionary<string, (ReportSlot Slot, int Idx)>();
            foreach (var k in used)
            {
                var slot = cols[k].Slot;
                slotIdx.TryGetValue(slot, out var i);
                var cap = slot switch
                {
                    ReportSlot.S => SlotS, ReportSlot.M => SlotM, ReportSlot.I => SlotI,
                    ReportSlot.R => SlotR, ReportSlot.D => SlotD, _ => SlotB
                };
                if (i >= cap)
                    throw new InvalidOperationException(
                        $"تعداد ستون‌های انتخابی بیش از ظرفیت است (نوع {slot}: حداکثر {cap}). ستون‌ها را کمتر کنید.");
                slotOf[k] = (slot, i);
                slotIdx[slot] = i + 1;
            }

            var getters = used.ToDictionary(k => k, k => BuildGetter(slotOf[k].Slot, slotOf[k].Idx));
            var projection = BuildProjection(cols, used, slotOf);

            var truncated = false;
            var scanned = 0;

            async Task<List<RawRow>> LoadAsync(int shift)
            {
                var pred = BuildPredicate(sqlFilters, shift);
                var query = pred is null ? source : source.Where(pred);
                var rows = await query.Select(projection).Take(meta.MaxScan + 1).ToListAsync();
                truncated = rows.Count > meta.MaxScan;
                if (truncated) rows = rows.Take(meta.MaxScan).ToList();
                if (memFilters.Count > 0)
                {
                    var memPred = BuildMemoryPredicate(memFilters, getters, shift);
                    rows = rows.Where(memPred).ToList();
                }
                scanned = rows.Count;
                return rows;
            }

            var data = await LoadAsync(0);
            var scannedMain = scanned;
            var truncatedMain = truncated;
            var fmt = new ReportFormatter();

            switch (q.Output)
            {
                case ReportOutput.Kpi:
                    BuildKpi(res, data, measures, getters, fmt);
                    if (q.ComparePrev && measures.Count > 0 && filters.Any(f => f.F.Preset != ReportDatePreset.None))
                    {
                        var prevRows = await LoadAsync(-1);
                        var prev = AggregateAll(prevRows, measures[0].Col, measures[0].Dto.Agg, getters);
                        FillDelta(res.Data, res.Data.Value ?? 0, prev, "نسبت به بازهٔ قبل");
                    }
                    break;

                case ReportOutput.Chart:
                    BuildChart(res, data, dimKeys, measures, getters, cols, q, fmt, take);
                    break;

                default:
                    if (q.Aggregate)
                        BuildAggTable(res, data, dimKeys, measures, getters, cols, q, fmt, take);
                    else
                        BuildDetailTable(res, data, dimKeys, getters, cols, q, fmt, take);
                    break;
            }

            res.Truncated = truncatedMain;
            res.ScannedRows = scannedMain;
            res.ResultRows = res.Data.Rows.Count > 0
                ? res.Data.Rows.Count
                : res.Data.Series.Count > 0
                    ? res.Data.Series[0].Labels.Count
                    : res.Data.Value is null ? 0 : 1;
        }
        catch (Exception ex)
        {
            res.Data.Error = "خطا در اجرای گزارش: " + ex.Message;
        }

        return Finish(res, sw);
    }

    private static ReportRunResultDto Finish(ReportRunResultDto res, Stopwatch sw)
    {
        res.ElapsedMs = sw.ElapsedMilliseconds;
        res.Data.GeneratedAt = DateTime.Now;
        return res;
    }

    // =================================================================
    //  خروجی‌ها
    // =================================================================

    private static void BuildKpi<T>(ReportRunResultDto res, List<RawRow> rows,
        List<MeasureRef<T>> measures, Dictionary<string, Func<RawRow, object?>> getters,
        ReportFormatter fmt) where T : class
    {
        res.Data.Kind = DashWidgetKind.Kpi;
        if (measures.Count == 0)
        {
            res.Data.Value = rows.Count;
            res.Data.Unit = "ردیف";
            return;
        }

        var m = measures[0];
        var val = AggregateAll(rows, m.Col, m.Dto.Agg, getters);
        res.Data.Value = Math.Round(val, Math.Clamp(m.Col.Decimals, 0, 4));
        res.Data.Unit = m.Col.Unit;
        res.Data.SubValue = rows.Count;
        res.Data.SubLabel = "ردیف داده";

        if (measures.Count > 1)
        {
            var second = AggregateAll(rows, measures[1].Col, measures[1].Dto.Agg, getters);
            res.Data.DeltaText = MeasureLabel(measures[1]) + ": " + fmt.Num(second, measures[1].Col);
        }
    }

    private static void BuildChart<T>(ReportRunResultDto res, List<RawRow> rows, List<string> dimKeys,
        List<MeasureRef<T>> measures, Dictionary<string, Func<RawRow, object?>> getters,
        IReadOnlyDictionary<string, ReportColumn<T>> cols, ReportQueryDto q,
        ReportFormatter fmt, int take) where T : class
    {
        res.Data.Kind = DashWidgetKind.Chart;
        res.Data.ChartType = q.ChartType;

        if (dimKeys.Count == 0)
        {
            res.Data.Error = "برای نمودار حداقل یک ستون (گروه‌بندی) لازم است.";
            return;
        }

        var groups = SortGroups(Group(rows, dimKeys, getters, cols, fmt), q, dimKeys, measures, getters, cols);
        var shown = groups.Take(take).ToList();

        if (measures.Count == 0)
        {
            var ser = new DashSeriesDto { Name = "تعداد" };
            foreach (var g in shown) { ser.Labels.Add(g.Label); ser.Values.Add(g.Count); }
            res.Data.Series.Add(ser);
            return;
        }

        foreach (var m in measures)
        {
            var s = new DashSeriesDto { Name = MeasureLabel(m) };
            foreach (var g in shown)
            {
                s.Labels.Add(g.Label);
                s.Values.Add(Math.Round(AggOf(g, m, getters), Math.Clamp(m.Col.Decimals, 0, 4)));
            }
            res.Data.Series.Add(s);
        }
    }

    private static void BuildAggTable<T>(ReportRunResultDto res, List<RawRow> rows, List<string> dimKeys,
        List<MeasureRef<T>> measures, Dictionary<string, Func<RawRow, object?>> getters,
        IReadOnlyDictionary<string, ReportColumn<T>> cols, ReportQueryDto q,
        ReportFormatter fmt, int take) where T : class
    {
        res.Data.Kind = DashWidgetKind.Table;
        res.Data.Columns = dimKeys.Select(k => cols[k].Label).ToList();
        res.Data.Columns.AddRange(measures.Select(MeasureLabel));

        var groups = SortGroups(Group(rows, dimKeys, getters, cols, fmt), q, dimKeys, measures, getters, cols);
        foreach (var g in groups.Take(take))
        {
            var cells = new List<string>(g.DimVals);
            cells.AddRange(measures.Select(m => fmt.Num(AggOf(g, m, getters), m.Col)));
            res.Data.Rows.Add(new DashRowDto { Cells = cells });
        }
    }

    private static void BuildDetailTable<T>(ReportRunResultDto res, List<RawRow> rows, List<string> dimKeys,
        Dictionary<string, Func<RawRow, object?>> getters, IReadOnlyDictionary<string, ReportColumn<T>> cols,
        ReportQueryDto q, ReportFormatter fmt, int take) where T : class
    {
        res.Data.Kind = DashWidgetKind.Table;
        if (dimKeys.Count == 0)
        {
            res.Data.Error = "برای گزارش ریز، ستون‌ها را انتخاب کنید.";
            return;
        }
        res.Data.Columns = dimKeys.Select(k => cols[k].Label).ToList();
        foreach (var r in SortRows(rows, q, dimKeys, cols, getters).Take(take))
            res.Data.Rows.Add(new DashRowDto
            { Cells = dimKeys.Select(k => fmt.Value(getters[k](r), cols[k])).ToList() });
    }

    // =================================================================
    //  گروه‌بندی / تجمیع / مرتب‌سازی
    // =================================================================

    private static List<GroupRow> Group<T>(List<RawRow> rows, List<string> dimKeys,
        Dictionary<string, Func<RawRow, object?>> getters,
        IReadOnlyDictionary<string, ReportColumn<T>> cols, ReportFormatter fmt) where T : class
    {
        var list = new List<GroupRow>();
        var index = new Dictionary<string, GroupRow>();
        foreach (var r in rows)
        {
            var raw = dimKeys.Select(k => getters[k](r)).ToList();
            var key = string.Join("\u0001", raw.Select(v => v?.ToString() ?? ""));
            if (!index.TryGetValue(key, out var g))
            {
                g = new GroupRow();
                for (var i = 0; i < raw.Count; i++)
                    g.DimVals.Add(fmt.Value(raw[i], cols[dimKeys[i]]));
                g.Label = string.Join(" · ", g.DimVals);
                index[key] = g;
                list.Add(g);
            }
            g.Rows.Add(r);
        }
        return list;
    }

    private static decimal AggOf<T>(GroupRow g, MeasureRef<T> m, Dictionary<string, Func<RawRow, object?>> getters)
        where T : class => Aggregate(m.Col, m.Dto.Agg, g.Rows, getters[m.Col.Key]);

    private static decimal AggregateAll<T>(List<RawRow> rows, ReportColumn<T> col, ReportAgg agg,
        Dictionary<string, Func<RawRow, object?>> getters) where T : class =>
        Aggregate(col, agg, rows, getters.TryGetValue(col.Key, out var g) ? g : _ => null);

    private static decimal Aggregate<T>(ReportColumn<T> col, ReportAgg agg, List<RawRow> rows,
        Func<RawRow, object?> get) where T : class
    {
        switch (agg)
        {
            case ReportAgg.Count: return rows.Count;
            case ReportAgg.CountDistinct:
                return rows.Select(get).Where(v => v is not null)
                           .Select(v => v!.ToString()!).Distinct().Count();
        }

        var nums = new List<decimal>();
        foreach (var r in rows)
        {
            var n = NumOf(get(r));
            if (n is not null) nums.Add(n.Value);
        }
        if (nums.Count == 0) return 0;
        return agg switch
        {
            ReportAgg.Sum => nums.Sum(),
            ReportAgg.Avg => nums.Average(),
            ReportAgg.Min => nums.Min(),
            ReportAgg.Max => nums.Max(),
            _ => 0
        };
    }

    private static decimal? NumOf(object? v) => v switch
    {
        null => null,
        decimal d => d,
        int i => i,
        double dd => (decimal)dd,
        long l => l,
        float f => (decimal)f,
        _ => null
    };

    private static string MeasureLabel<T>(MeasureRef<T> m) where T : class
    {
        var label = string.IsNullOrWhiteSpace(m.Dto.Label)
            ? $"{AggFa(m.Dto.Agg)} {m.Col.Label}"
            : m.Dto.Label!;
        return string.IsNullOrEmpty(m.Col.Unit) ? label : $"{label} ({m.Col.Unit})";
    }

    private static string AggFa(ReportAgg a) => a switch
    {
        ReportAgg.Sum => "مجموع",
        ReportAgg.Count => "تعداد",
        ReportAgg.Avg => "میانگین",
        ReportAgg.Min => "کمترین",
        ReportAgg.Max => "بیشترین",
        ReportAgg.CountDistinct => "تعداد یکتا",
        _ => ""
    };

    private static List<GroupRow> SortGroups<T>(List<GroupRow> groups, ReportQueryDto q,
        List<string> dimKeys, List<MeasureRef<T>> measures,
        Dictionary<string, Func<RawRow, object?>> getters,
        IReadOnlyDictionary<string, ReportColumn<T>> cols) where T : class
    {
        var sorts = (q.Sorts ?? new List<ReportSortDto>()).Where(s => !string.IsNullOrEmpty(s.Field)).ToList();
        if (sorts.Count == 0)
        {
            return measures.Count > 0
                ? groups.OrderByDescending(g => AggOf(g, measures[0], getters)).ToList()
                : groups.OrderByDescending(g => g.Count).ThenBy(g => g.Label).ToList();
        }

        IEnumerable<GroupRow> ordered = groups;
        var first = true;
        foreach (var s in sorts)
        {
            IOrderedEnumerable<GroupRow>? next = null;
            if (s.Field.StartsWith("m:"))
            {
                if (int.TryParse(s.Field[2..], out var mi) && mi >= 0 && mi < measures.Count)
                {
                    var m = measures[mi];
                    Func<GroupRow, decimal> key = g => AggOf(g, m, getters);
                    next = first ? (s.Desc ? ordered.OrderByDescending(key) : ordered.OrderBy(key))
                                 : (s.Desc ? ((IOrderedEnumerable<GroupRow>)ordered).ThenByDescending(key)
                                           : ((IOrderedEnumerable<GroupRow>)ordered).ThenBy(key));
                }
            }
            else if (dimKeys.Contains(s.Field))
            {
                var di = dimKeys.IndexOf(s.Field);
                var col = cols[s.Field];
                if (col.DataType is ReportDataType.Number or ReportDataType.Enum)
                {
                    Func<GroupRow, decimal> key = g => ParseFa(g.DimVals.Count > di ? g.DimVals[di] : "");
                    next = first ? (s.Desc ? ordered.OrderByDescending(key) : ordered.OrderBy(key))
                                 : (s.Desc ? ((IOrderedEnumerable<GroupRow>)ordered).ThenByDescending(key)
                                           : ((IOrderedEnumerable<GroupRow>)ordered).ThenBy(key));
                }
                else
                {
                    Func<GroupRow, string> key = g => g.DimVals.Count > di ? g.DimVals[di] : "";
                    next = first ? (s.Desc ? ordered.OrderByDescending(key) : ordered.OrderBy(key))
                                 : (s.Desc ? ((IOrderedEnumerable<GroupRow>)ordered).ThenByDescending(key)
                                           : ((IOrderedEnumerable<GroupRow>)ordered).ThenBy(key));
                }
            }
            if (next is null) continue;
            ordered = next;
            first = false;
        }
        return ordered.ToList();
    }

    private static List<RawRow> SortRows<T>(List<RawRow> rows, ReportQueryDto q, List<string> dimKeys,
        IReadOnlyDictionary<string, ReportColumn<T>> cols,
        Dictionary<string, Func<RawRow, object?>> getters) where T : class
    {
        var sorts = (q.Sorts ?? new List<ReportSortDto>()).Where(s => dimKeys.Contains(s.Field)).ToList();
        if (sorts.Count == 0)
        {
            var anchor = dimKeys.FirstOrDefault(k => cols[k].DataType == ReportDataType.Date);
            if (anchor is not null)
                return rows.OrderByDescending(r => getters[anchor](r) as DateTime?).ToList();
            return rows;
        }

        IEnumerable<RawRow> ordered = rows;
        var first = true;
        foreach (var s in sorts)
        {
            var col = cols[s.Field];
            var get = getters[s.Field];
            var next = ApplyRowSort(ordered, get, col.DataType, s.Desc, first);
            ordered = next;
            first = false;
        }
        return ordered.ToList();
    }

    private static IEnumerable<RawRow> ApplyRowSort(IEnumerable<RawRow> src, Func<RawRow, object?> get,
        ReportDataType type, bool desc, bool first)
    {
        switch (type)
        {
            case ReportDataType.Date:
            {
                Func<RawRow, DateTime?> k = r => get(r) as DateTime?;
                return first ? (desc ? src.OrderByDescending(k) : src.OrderBy(k))
                             : (desc ? ((IOrderedEnumerable<RawRow>)src).ThenByDescending(k)
                                     : ((IOrderedEnumerable<RawRow>)src).ThenBy(k));
            }
            case ReportDataType.Number:
            case ReportDataType.Enum:
            {
                Func<RawRow, decimal> k = r => NumOf(get(r)) ?? 0;
                return first ? (desc ? src.OrderByDescending(k) : src.OrderBy(k))
                             : (desc ? ((IOrderedEnumerable<RawRow>)src).ThenByDescending(k)
                                     : ((IOrderedEnumerable<RawRow>)src).ThenBy(k));
            }
            default:
            {
                Func<RawRow, string> k = r => get(r)?.ToString() ?? "";
                return first ? (desc ? src.OrderByDescending(k) : src.OrderBy(k))
                             : (desc ? ((IOrderedEnumerable<RawRow>)src).ThenByDescending(k)
                                     : ((IOrderedEnumerable<RawRow>)src).ThenBy(k));
            }
        }
    }

    private static void FillDelta(WidgetDataDto data, decimal value, decimal prev, string label)
    {
        if (prev == 0)
        {
            data.DeltaText = value == 0 ? "بدون تغییر" : "جدید (دورهٔ قبل صفر)";
            data.DeltaUp = value > 0;
            return;
        }
        var pct = (value - prev) / Math.Abs(prev) * 100m;
        data.DeltaUp = pct >= 0;
        data.DeltaText = Fa.Digits($"{(pct >= 0 ? "+" : "")}{pct:F0}٪") + " " + label;
    }

    internal static decimal ParseFa(string s) =>
        decimal.TryParse(NormalizeDigits(s).Replace(",", ""), out var v) ? v : 0;

    internal static string NormalizeDigits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch >= '۰' && ch <= '۹') sb.Append((char)('0' + (ch - '۰')));
            else if (ch >= '٠' && ch <= '٩') sb.Append((char)('0' + (ch - '٠')));
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    // =================================================================
    //  projection / predicate
    // =================================================================

    private static Func<RawRow, object?> BuildGetter(ReportSlot slot, int idx)
    {
        var name = slot.ToString() + idx;
        var field = typeof(RawRow).GetField(name)
                    ?? throw new InvalidOperationException($"خانهٔ {name} در RawRow وجود ندارد.");
        var p = Expression.Parameter(typeof(RawRow), "r");
        var body = Expression.Convert(Expression.Field(p, field), typeof(object));
        return Expression.Lambda<Func<RawRow, object?>>(body, p).Compile();
    }

    private static Expression<Func<T, RawRow>> BuildProjection<T>(
        IReadOnlyDictionary<string, ReportColumn<T>> cols, List<string> used,
        Dictionary<string, (ReportSlot Slot, int Idx)> slotOf) where T : class
    {
        var p = Expression.Parameter(typeof(T), "x");
        var bindings = new List<MemberBinding>();
        foreach (var k in used)
        {
            var col = cols[k];
            var (slot, idx) = slotOf[k];
            var field = typeof(RawRow).GetField(slot.ToString() + idx)
                        ?? throw new InvalidOperationException($"خانهٔ {slot}{idx} در RawRow وجود ندارد.");
            var acc = col.Accessor ?? throw new InvalidOperationException($"ستون {k} دسترسی‌کننده ندارد.");
            var body = new ParameterReplacer(acc.Parameters[0], p).Visit(acc.Body)!;
            bindings.Add(Expression.Bind(field, Expression.Convert(body, field.FieldType)));
        }
        return Expression.Lambda<Func<T, RawRow>>(
            Expression.MemberInit(Expression.New(typeof(RawRow)), bindings), p);
    }

    private static Expression<Func<T, bool>>? BuildPredicate<T>(
        List<(ReportColumn<T> Col, ReportFilterDto F)> filters, int shift) where T : class
    {
        if (filters.Count == 0) return null;
        var p = Expression.Parameter(typeof(T), "x");
        Expression? all = null;
        foreach (var (col, f) in filters)
        {
            var one = BuildOne(col, f, p, shift);
            if (one is null) continue;
            all = all is null ? one : Expression.AndAlso(all, one);
        }
        return all is null ? null : Expression.Lambda<Func<T, bool>>(all, p);
    }

    private static Func<RawRow, bool> BuildMemoryPredicate<T>(
        List<(ReportColumn<T> Col, ReportFilterDto F)> filters,
        Dictionary<string, Func<RawRow, object?>> getters, int shift) where T : class
    {
        return row =>
        {
            foreach (var (col, f) in filters)
            {
                if (!getters.TryGetValue(col.Key, out var get)) continue;
                if (!MemoryMatch(f, get(row))) return false;
            }
            return true;
        };
    }

    private static bool MemoryMatch(ReportFilterDto f, object? v)
    {
        var num = ParseNum(f.Value);
        var num2 = ParseNum(f.Value2);
        var cur = NumOf(v);

        switch (f.Op)
        {
            case ReportOp.IsNull: return cur is null;
            case ReportOp.NotNull: return cur is not null;
        }
        if (cur is null || num is null) return false;
        return f.Op switch
        {
            ReportOp.Eq => cur == (decimal)num,
            ReportOp.Ne => cur != (decimal)num,
            ReportOp.Gt => cur > (decimal)num,
            ReportOp.Gte => cur >= (decimal)num,
            ReportOp.Lt => cur < (decimal)num,
            ReportOp.Lte => cur <= (decimal)num,
            ReportOp.Between => num2 is not null && cur >= (decimal)num && cur <= (decimal)num2,
            _ => true
        };
    }

    private static Expression? BuildOne<T>(ReportColumn<T> col, ReportFilterDto f,
        ParameterExpression p, int shift) where T : class
    {
        var acc = col.Accessor;
        if (acc is null) return null;
        var body = new ParameterReplacer(acc.Parameters[0], p).Visit(acc.Body)!;
        var t = body.Type;

        if (col.DataType == ReportDataType.Date && f.Preset != ReportDatePreset.None)
        {
            var (from, to) = Bounds(f.Preset, shift);
            return DateRange(body, from, to);
        }

        switch (col.DataType)
        {
            case ReportDataType.Text:
            {
                if (f.Op is ReportOp.IsNull) return IsNull(body, t);
                if (f.Op is ReportOp.NotNull) return Not(IsNull(body, t));

                var raw = Unlabel(col.TextLabels, NormalizeDigits(f.Value ?? "").Trim());
                var sv = body.Type == typeof(string) ? body : Expression.Convert(body, typeof(string));

                switch (f.Op)
                {
                    case ReportOp.Eq: return Expression.Equal(sv, Expression.Constant(raw));
                    case ReportOp.Ne: return Expression.NotEqual(sv, Expression.Constant(raw));
                    case ReportOp.Contains when raw.Length > 0:
                        return AndNotNull(sv, Expression.Call(sv, StrContains, Expression.Constant(raw)));
                    case ReportOp.StartsWith when raw.Length > 0:
                        return AndNotNull(sv, Expression.Call(sv, StrStarts, Expression.Constant(raw)));
                    case ReportOp.In:
                    {
                        var parts = SplitList(f.Value ?? "").Select(x => Unlabel(col.TextLabels, x)).ToList();
                        if (parts.Count == 0) return null;
                        var arr = Expression.Constant(parts.ToArray(), typeof(IEnumerable<string>));
                        return AndNotNull(sv, Expression.Call(null, ContainsStr, arr, sv));
                    }
                    default: return null;
                }
            }

            case ReportDataType.Bool:
            {
                bool? want = f.Op switch
                {
                    ReportOp.True => true,
                    ReportOp.False => false,
                    ReportOp.Eq => IsTrue(f.Value),
                    ReportOp.Ne => !IsTrue(f.Value),
                    _ => null
                };
                if (want is null) return null;
                var left = body.Type == typeof(bool?) ? body : Expression.Convert(body, typeof(bool?));
                return Expression.Equal(left, Expression.Constant(want, typeof(bool?)));
            }

            case ReportDataType.Date:
            {
                if (f.Op is ReportOp.IsNull) return IsNull(body, t);
                if (f.Op is ReportOp.NotNull) return Not(IsNull(body, t));
                var d1 = ParseDate(f.Value);
                var d2 = ParseDate(f.Value2);
                if (d1 is null) return null;
                return f.Op switch
                {
                    ReportOp.Eq => DateRange(body, d1.Value.Date, d1.Value.Date.AddDays(1)),
                    ReportOp.Gt => DateCmp(body, d1.Value, ExpressionType.GreaterThan),
                    ReportOp.Gte => DateCmp(body, d1.Value, ExpressionType.GreaterThanOrEqual),
                    ReportOp.Lt => DateCmp(body, d1.Value, ExpressionType.LessThan),
                    ReportOp.Lte => DateCmp(body, d1.Value, ExpressionType.LessThanOrEqual),
                    ReportOp.Between when d2 is not null => DateRange(body, d1.Value.Date, d2.Value.Date.AddDays(1)),
                    _ => null
                };
            }

            case ReportDataType.Enum:
            {
                if (f.Op is ReportOp.In)
                {
                    var vals = SplitList(f.Value ?? "")
                        .Select(x => ParseEnumValue(col, x))
                        .Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
                    if (vals.Count == 0) return null;
                    var left = body.Type == typeof(int?) ? body : Expression.Convert(body, typeof(int?));
                    var arr = Expression.Constant(vals.ToArray(), typeof(IEnumerable<int>));
                    return Expression.AndAlso(
                        Expression.NotEqual(left, Expression.Constant(null, typeof(int?))),
                        Expression.Call(null, ContainsInt, arr, Expression.Convert(left, typeof(int))));
                }
                var iv = ParseEnumValue(col, f.Value ?? "");
                if (iv is null) return null;
                var li = body.Type == typeof(int?) ? body : Expression.Convert(body, typeof(int?));
                var right = Expression.Constant((int?)iv, typeof(int?));
                return f.Op == ReportOp.Ne ? Expression.NotEqual(li, right) : Expression.Equal(li, right);
            }

            case ReportDataType.Number:
            {
                if (f.Op is ReportOp.IsNull) return IsNull(body, t);
                if (f.Op is ReportOp.NotNull) return Not(IsNull(body, t));
                var v1 = ParseNum(f.Value);
                var v2 = ParseNum(f.Value2);
                if (v1 is null) return null;
                var isInt = body.Type == typeof(int?) || body.Type == typeof(int);
                var target = isInt ? typeof(int?) : typeof(double?);
                var under = isInt ? typeof(int) : typeof(double);
                var left = body.Type == target ? body : Expression.Convert(body, target);
                object cv1 = Convert.ChangeType(v1.Value, under);

                Expression? cmp(ExpressionType op, object val) =>
                    Expression.MakeBinary(op, left, Expression.Constant(Convert.ChangeType(val, under), target));

                switch (f.Op)
                {
                    case ReportOp.Eq: return cmp(ExpressionType.Equal, cv1);
                    case ReportOp.Ne: return cmp(ExpressionType.NotEqual, cv1);
                    case ReportOp.Gt: return cmp(ExpressionType.GreaterThan, cv1);
                    case ReportOp.Gte: return cmp(ExpressionType.GreaterThanOrEqual, cv1);
                    case ReportOp.Lt: return cmp(ExpressionType.LessThan, cv1);
                    case ReportOp.Lte: return cmp(ExpressionType.LessThanOrEqual, cv1);
                    case ReportOp.Between when v2 is not null:
                        return Expression.AndAlso(
                            cmp(ExpressionType.GreaterThanOrEqual, cv1)!,
                            cmp(ExpressionType.LessThanOrEqual, Convert.ChangeType(v2.Value, under))!);
                    default: return null;
                }
            }
        }
        return null;
    }

    private static readonly System.Reflection.MethodInfo StrContains =
        typeof(string).GetMethod("Contains", new[] { typeof(string) })!;
    private static readonly System.Reflection.MethodInfo StrStarts =
        typeof(string).GetMethod("StartsWith", new[] { typeof(string) })!;
    private static readonly System.Reflection.MethodInfo ContainsStr =
        typeof(Enumerable).GetMethods().First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(string));
    private static readonly System.Reflection.MethodInfo ContainsInt =
        typeof(Enumerable).GetMethods().First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(int));

    private static Expression AndNotNull(Expression strExpr, Expression op) =>
        Expression.AndAlso(Expression.NotEqual(strExpr, Expression.Constant(null, typeof(string))), op);

    private static Expression Not(Expression e) => e is ConstantExpression c ? Expression.Constant(!(bool)c.Value!) : Expression.Not(e);

    private static Expression IsNull(Expression body, Type t)
    {
        if (t.IsValueType && Nullable.GetUnderlyingType(t) is null) return Expression.Constant(false);
        var nt = Nullable.GetUnderlyingType(t) is null ? t : t;
        return Expression.Equal(Expression.Convert(body, nt), Expression.Constant(null, nt));
    }

    private static Expression DateRange(Expression body, DateTime from, DateTime to)
    {
        var left = body.Type == typeof(DateTime?) ? body : Expression.Convert(body, typeof(DateTime?));
        return Expression.AndAlso(
            Expression.GreaterThanOrEqual(left, Expression.Constant((DateTime?)from, typeof(DateTime?))),
            Expression.LessThan(left, Expression.Constant((DateTime?)to, typeof(DateTime?))));
    }

    private static Expression DateCmp(Expression body, DateTime v, ExpressionType op)
    {
        var left = body.Type == typeof(DateTime?) ? body : Expression.Convert(body, typeof(DateTime?));
        return Expression.MakeBinary(op, left, Expression.Constant((DateTime?)v, typeof(DateTime?)));
    }

    /// <summary>اگر کاربر برچسب فارسی را فرستاده باشد، به مقدار خام دیتابیس برمی‌گردانیم.</summary>
    private static string Unlabel(Dictionary<string, string>? labels, string v)
    {
        if (labels is null || v.Length == 0) return v;
        if (labels.ContainsKey(v)) return v;
        var norm = v.Replace("‌", "").Replace("ي", "ی").Replace("ك", "ک");
        foreach (var kv in labels)
            if (kv.Value.Replace("‌", "").Replace("ي", "ی").Replace("ك", "ک") == norm) return kv.Key;
        return v;
    }

    private static List<string> SplitList(string raw) =>
        raw.Split(new[] { ',', '،', '|', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Select(NormalizeDigits).Where(x => x.Length > 0).Distinct().ToList();

    private static int? ParseEnumValue<T>(ReportColumn<T> col, string raw) where T : class
    {
        var v = NormalizeDigits(raw).Trim();
        if (v.Length == 0) return null;
        if (int.TryParse(v, out var n)) return n;
        if (col.Labels is not null)
        {
            var norm = v.Replace("‌", "").Replace("ي", "ی").Replace("ك", "ک");
            var hit = col.Labels.FirstOrDefault(kv =>
                kv.Value.Replace("‌", "").Replace("ي", "ی").Replace("ك", "ک") == norm);
            if (hit.Value is not null) return hit.Key;
        }
        return null;
    }

    private static double? ParseNum(string? raw)
    {
        var v = NormalizeDigits(raw ?? "").Replace(",", "").Trim();
        return double.TryParse(v, out var d) ? d : null;
    }

    private static DateTime? ParseDate(string? raw)
    {
        var v = NormalizeDigits(raw ?? "").Trim();
        if (v.Length == 0) return null;

        // اول سه‌بخشی‌ها: «۱۴۰۴/۰۶/۰۱» باید شمسی تفسیر شود.
        // اگر اول DateTime.TryParse صدا زده شود، «1404-01-01» به‌اشتباه میلادیِ سال ۱۴۰۴ خوانده می‌شود.
        var parts = v.Split('/', '-', '.');
        if (parts.Length == 3 &&
            int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b) && int.TryParse(parts[2], out var c))
        {
            try { return a > 1500 ? new DateTime(a, b, c) : PersianDate.ToGregorian(a, b, c); }
            catch { return null; }
        }

        // قالب‌های دیگر (ISO با زمان، نام ماه میلادی و…)
        return DateTime.TryParse(v, out var dt) ? dt : null;
    }

    private static bool IsTrue(string? v)
    {
        var s = NormalizeDigits(v ?? "").Trim().ToLowerInvariant();
        return s is "1" or "true" or "yes" or "y" or "بله";
    }

    /// <summary>بازهٔ زمانی آماده بر اساس تقویم شمسی. shift=-1 یعنی بازهٔ قبل.</summary>
    public static (DateTime From, DateTime To) Bounds(ReportDatePreset preset, int shift)
    {
        var today = DateTime.Now.Date;
        var (jy, jm, _) = PersianDate.FromGregorian(today);

        switch (preset)
        {
            case ReportDatePreset.Today:
            {
                var d = today.AddDays(shift);
                return (d, d.AddDays(1));
            }
            case ReportDatePreset.Yesterday:
            {
                var d = today.AddDays(-1 + shift);
                return (d, d.AddDays(1));
            }
            case ReportDatePreset.ThisWeek:
            case ReportDatePreset.LastWeek:
            {
                var off = preset == ReportDatePreset.LastWeek ? -1 : 0;
                var dow = ((int)today.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
                var start = today.AddDays(-dow + 7 * (off + shift));
                return (start, start.AddDays(7));
            }
            case ReportDatePreset.ThisMonth:
            case ReportDatePreset.LastMonth:
            {
                var delta = (preset == ReportDatePreset.LastMonth ? -1 : 0) + shift;
                var (y1, m1) = PersianDate.AddMonths(jy, jm, delta);
                var (y2, m2) = PersianDate.AddMonths(y1, m1, 1);
                return (PersianDate.ToGregorian(y1, m1, 1), PersianDate.ToGregorian(y2, m2, 1));
            }
            case ReportDatePreset.ThisYear:
            case ReportDatePreset.LastYear:
            {
                var y = jy + (preset == ReportDatePreset.LastYear ? -1 : 0) + shift;
                return (PersianDate.ToGregorian(y, 1, 1), PersianDate.ToGregorian(y + 1, 1, 1));
            }
            case ReportDatePreset.Last7Days: return Rolling(today, 7, shift);
            case ReportDatePreset.Last30Days: return Rolling(today, 30, shift);
            case ReportDatePreset.Last90Days: return Rolling(today, 90, shift);
            case ReportDatePreset.Last12Months:
            {
                var (y1, m1) = PersianDate.AddMonths(jy, jm, -11 + 12 * shift);
                var (y2, m2) = PersianDate.AddMonths(jy, jm, 1 + 12 * shift);
                return (PersianDate.ToGregorian(y1, m1, 1), PersianDate.ToGregorian(y2, m2, 1));
            }
            default: return (DateTime.MinValue, DateTime.MaxValue);
        }
    }

    private static (DateTime, DateTime) Rolling(DateTime today, int days, int shift)
    {
        var to = today.AddDays(1 + days * shift);
        return (to.AddDays(-days), to);
    }

    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _from, _to;
        public ParameterReplacer(ParameterExpression from, ParameterExpression to) { _from = from; _to = to; }
        protected override Expression VisitParameter(ParameterExpression node) => node == _from ? _to : base.VisitParameter(node);
    }
}

/// <summary>قالب‌بندی مقدارها برای نمایش (ارقام فارسی، تاریخ شمسی، برچسب enum).</summary>
public sealed class ReportFormatter
{
    public string Num<T>(decimal v, ReportColumn<T> col) where T : class
    {
        var dec = Math.Clamp(col.Decimals, 0, 4);
        return Fa.Digits(Math.Round(v, dec).ToString("N" + dec));
    }

    public string Value<T>(object? v, ReportColumn<T> col) where T : class
    {
        if (v is null) return "—";
        switch (col.DataType)
        {
            case ReportDataType.Date:
                return v is DateTime dt ? Fa.Digits(PersianDate.ToShort(dt)) : "—";
            case ReportDataType.Bool:
                return v is true ? "بله" : v is false ? "خیر" : "—";
            case ReportDataType.Enum:
            {
                var n = NumOfInt(v);
                if (n is null) return v.ToString() ?? "—";
                return col.Labels is not null && col.Labels.TryGetValue(n.Value, out var label)
                    ? label
                    : n.Value.ToString();
            }
            case ReportDataType.Number:
            {
                var d = v switch
                {
                    decimal dm => dm,
                    int i => i,
                    double dd => (decimal)dd,
                    long l => l,
                    _ => 0m
                };
                var dec = Math.Clamp(col.Decimals, 0, 4);
                return Fa.Digits(d.ToString("N" + dec));
            }
            default:
            {
                var s = v.ToString();
                if (string.IsNullOrEmpty(s)) return "—";
                return col.TextLabels is not null && col.TextLabels.TryGetValue(s, out var fa) ? fa : s;
            }
        }
    }

    private static int? NumOfInt(object v) => v switch
    {
        int i => i,
        double d => (int)d,
        decimal m => (int)m,
        long l => (int)l,
        _ => null
    };
}
