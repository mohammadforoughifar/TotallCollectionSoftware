namespace Inventory.Api.Services.ItAssets;

/// <summary>
/// «نتیجهٔ دستور کار» — تنها منبع حقیقتِ وضعیت پنج‌حالته.
///
/// <para>
/// تا پیش از این، همین منطق در سه جا تکرار شده بود: <c>WorkOrdersController.Stats</c>،
/// <c>WorkOrdersController.Calendar</c> و <c>WorkOrders.razor</c> (تابع <c>Tone</c>).
/// سه پیاده‌سازی = سه رفتار؛ مثلاً کلاینت «مهلت امروزِ نگذشته» را <c>today</c> می‌دانست
/// و سرور <c>open</c>. این کلاس آن واگرایی را می‌بندد.
/// </para>
///
/// <para>
/// <b>قاعدهٔ کسب‌وکار:</b> ملاک زمان، <b>زمان پاسخ گیرنده</b> (<c>WorkOrderAssignee.RepliedAt</c>)
/// است، نه زمان بستن دستور توسط دستوردهنده (<c>ClosedAt</c>). این همان قاعده‌ای است که در
/// <c>WORKORDERS-DASHBOARD-METRICS-FA.md</c> مستند شده و نباید تغییر کند.
/// </para>
///
/// <para>خالص و بدون وابستگی به دیتابیس — قابل تست واحد.</para>
/// </summary>
public static class WorkOrderTone
{
    /// <summary>همه انجام داده‌اند و آخرین پاسخ تا مهلت بوده.</summary>
    public const string OnTime = "ontime";

    /// <summary>همه انجام داده‌اند ولی آخرین پاسخ بعد از مهلت بوده.</summary>
    public const string LateDone = "latedone";

    /// <summary>دستور بسته شده ولی همهٔ گیرندگان «انجام شد» نزده‌اند.</summary>
    public const string ClosedNoDone = "closednodone";

    /// <summary>باز است و از مهلت گذشته.</summary>
    public const string Late = "late";

    /// <summary>باز است و هنوز به مهلت نرسیده.</summary>
    public const string Open = "open";

    /// <summary>ترتیب نمایش در نمودارها — ثابت تا رنگ‌ها بین ویجت‌ها یکسان بماند.</summary>
    public static readonly string[] Order = { Open, Late, OnTime, LateDone, ClosedNoDone };

    /// <summary>
    /// نتیجهٔ یک دستور کار. <paramref name="assignees"/> باید همهٔ گیرندگان همان دستور باشند.
    /// دستورِ بدون گیرنده «انجام‌شده» تلقی نمی‌شود (سازگار با رفتار فعلی سرور).
    /// </summary>
    public static string Of(string? status, DateTime dueAt, DateTime now,
                            IReadOnlyCollection<(bool? Done, DateTime? RepliedAt)> assignees)
    {
        var allDone = assignees.Count > 0 && assignees.All(a => a.Done == true);
        if (allDone)
        {
            var last = assignees.Max(a => a.RepliedAt) ?? DateTime.MaxValue;
            return last <= dueAt ? OnTime : LateDone;
        }
        if (status == "Closed") return ClosedNoDone;
        return dueAt < now ? Late : Open;
    }

    /// <summary>
    /// آیا دستور «نتیجه‌دار» است؟ (مخرجِ نرخ انجام به‌موقع)
    /// <para>
    /// نکتهٔ مهم: دستوری که همهٔ گیرندگانش «انجام شد» زده‌اند ولی دستوردهنده هنوز آن را نبسته،
    /// از نظر این قاعده نتیجه‌دار است — دقیقاً مثل <c>WorkOrdersController.Stats</c>.
    /// اگر این را به «Status == Closed» تغییر دهیم، آمار داشبورد ماژول و داشبورد شخصی از هم واگرا می‌شوند.
    /// </para>
    /// </summary>
    public static bool IsResolved(string tone) => tone is OnTime or LateDone or ClosedNoDone;

    /// <summary>برچسب فارسی.</summary>
    public static string Fa(string tone) => tone switch
    {
        OnTime => "انجام به‌موقع",
        LateDone => "انجام با تأخیر",
        ClosedNoDone => "بسته بدون انجام",
        Late => "گذشته از مهلت",
        _ => "باز / در جریان"
    };

    /// <summary>
    /// رنگ معنایی برای <c>DashRowDto.Tone</c> — با CSS موجود کارت داشبورد هم‌خوان است
    /// (<c>tone-success</c> / <c>tone-warning</c> / <c>tone-danger</c>).
    /// </summary>
    public static string? Color(string tone) => tone switch
    {
        OnTime => "success",
        LateDone => "warning",
        ClosedNoDone => "danger",
        Late => "danger",
        _ => null
    };

    /// <summary>
    /// نرخ انجام به‌موقع (درصد) روی مجموعه‌ای از نتیجه‌ها.
    /// مخرج = <b>نتیجه‌دارها</b> (به‌موقع + با تأخیر + بسته بدون انجام)، نه کل دستورها —
    /// همان تعریف <c>WorkOrdersController.Stats</c>.
    /// اگر هیچ دستور نتیجه‌داری نباشد، <c>null</c> برمی‌گردد تا «۰٪» گمراه‌کننده نشان داده نشود.
    /// </summary>
    public static (decimal? Rate, int Closed, int OnTimeCount) OnTimeRate(IEnumerable<string> tones)
    {
        int closed = 0, onTime = 0;
        foreach (var t in tones)
        {
            if (!IsResolved(t)) continue;
            closed++;
            if (t == OnTime) onTime++;
        }
        return (closed == 0 ? null : Math.Round(onTime * 100m / closed, 1), closed, onTime);
    }
}
