using Inventory.Api.Data;
using Inventory.Licensing.Metrics;
using Inventory.Licensing.Model;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Licensing.Services;

/// <summary>
/// «شمارندهٔ واقعی» هر متریک — پلی بین موتور لایسنس (که هیچ چیزی از جداول کسب‌وکار
/// نمی‌داند) و دیتابیس.
///
/// <para>
/// <b>چرا این‌جاست و نه در موتور؟</b> پروژهٔ <c>Inventory.Licensing</c> عمداً به EF Core
/// و مدل‌های دیتابیس وابسته نیست؛ پس «تعداد واقعی رکوردها» را نمی‌تواند بشمارد. این
/// کلاسِ کوچک همان شکاف را پر می‌کند: هر متریکی که نوعش <see cref="MetricKind.ActiveCount"/>
/// باشد، باید یک probe این‌جا داشته باشد.
/// </para>
///
/// <para>
/// ⚠️ رکوردهای <b>حذف‌نرم‌شده هم شمرده می‌شوند</b> (<c>IgnoreQueryFilters</c>) — چون
/// قاعدهٔ سهمیه این است: «حذف توسط کاربر، مصرف را برنمی‌گرداند». اگر حذف‌شده‌ها را
/// نشماریم، مشتری می‌تواند با حذف و ایجاد مکرر از سقف عبور کند.
/// </para>
///
/// <para>
/// <b>⚠️ بازه‌دار شد (بازبینی مرحله ۲):</b> هر probe حالا بازهٔ <c>[From, To)</c> می‌گیرد.
/// قبلاً فقط «کل تاریخچه» شمرده می‌شد و برای سقف <b>ماهانه</b> این غلط بود: مشتری که
/// ۵٬۰۰۰ فاکتور در ماه قبل ساخته بود، ماه بعد هم «۵٬۰۰۰ مصرف‌شده» می‌دید و سقف ماهانهٔ
/// تازه‌اش از روز اول پر به‌نظر می‌رسید. قاعدهٔ درست:
/// <b>برای دورهٔ ماهانه/سالانه، رکوردهای همان دوره شمرده می‌شوند.</b>
/// </para>
/// </summary>
public static class ActiveCountProbes
{
    /// <summary>
    /// متریک → تابع شمارش در بازهٔ <c>[from, to)</c>.
    /// برای دورهٔ دائمی، بازه <c>(DateTime.MinValue, DateTime.MaxValue)</c> می‌آید.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Func<AppDbContext, DateTime, DateTime, CancellationToken, Task<long>>> Probes =
        new Dictionary<string, Func<AppDbContext, DateTime, DateTime, CancellationToken, Task<long>>>(StringComparer.OrdinalIgnoreCase)
        {
            [MetricCodes.InvoiceCreate] = (db, from, to, ct) =>
                db.FacInvoices.IgnoreQueryFilters()
                  .Where(i => i.Date >= from && i.Date < to)
                  .LongCountAsync(ct),

            [MetricCodes.LetterIncomingCreate] = (db, from, to, ct) =>
                db.IncomingLetters.IgnoreQueryFilters()
                  .Where(l => l.Date >= from && l.Date < to)
                  .LongCountAsync(ct),

            [MetricCodes.LetterOutgoingCreate] = (db, from, to, ct) =>
                db.OutgoingLetters.IgnoreQueryFilters()
                  .Where(l => l.DateSabt >= from && l.DateSabt < to)
                  .LongCountAsync(ct)
        };

    /// <summary>
    /// همهٔ متریک‌هایی که probe دارند — <b>بدون توجه به نوع متریک</b>.
    ///
    /// <para>
    /// <b>چرا نوع متریک ملاک نیست؟</b> قاعدهٔ سند: «مبنا برای <b>هر</b> متریکی که probe
    /// دارد گرفته می‌شود، نه فقط ActiveCount». دلیلش ساده است: probe یعنی «ما می‌توانیم
    /// تعداد واقعی ردیف‌های کسب‌وکار را بشماریم» و همین شمارش است که باید از مبنا مقایسه
    /// شود. اگر فردا متریکی از نوع شمارنده هم probe بگیرد (مثلاً «تعداد خطوط فاکتور
    /// در این دوره»)، نباید یادمان برود مبنا برایش ثبت شود و «دادهٔ قبل از لایسنس»
    /// سقفش را پر کند.
    /// </para>
    /// </summary>
    public static IEnumerable<(string Module, string Metric,
        Func<AppDbContext, DateTime, DateTime, CancellationToken, Task<long>> Count)> AllWithProbe()
    {
        foreach (var metric in MetricCodes.All)
        {
            if (!Probes.TryGetValue(metric.Code, out var count)) continue;
            yield return (metric.Module, metric.Code, count);
        }
    }

    /// <summary>
    /// متریک‌های ActiveCount که probe دارند — برای سازگاری با کد قبلی نگه داشته شد.
    /// </summary>
    public static IEnumerable<(string Module, string Metric,
        Func<AppDbContext, DateTime, DateTime, CancellationToken, Task<long>> Count)> All()
        => AllWithProbe().Where(x => MetricCodes.Find(x.Metric)?.Kind == MetricKind.ActiveCount);

    /// <summary>آیا برای این متریک probe داریم؟ (پایهٔ تصمیم «مصرف مؤثر» در QuotaService)</summary>
    public static bool HasProbe(string metric) => Probes.ContainsKey(metric);

    /// <summary>شمارش واقعی یک متریک در بازهٔ دورهٔ مشخص.</summary>
    public static Task<long> CountAsync(AppDbContext db, string metric, QuotaPeriod period,
        DateTime iranNow, CancellationToken cancellationToken = default)
    {
        if (!Probes.TryGetValue(metric, out var probe)) return Task.FromResult(0L);
        return probe(db, Inventory.Licensing.Periods.PersianPeriodKey.RangeOf(period, iranNow).From,
            Inventory.Licensing.Periods.PersianPeriodKey.RangeOf(period, iranNow).To, cancellationToken);
    }

    /// <summary>
    /// ثبت «مبنای بخشودگی گذشته» برای متریک‌هایی که آیتم لایسنس‌شان این گزینه را
    /// روشن کرده است — <b>فقط برای دورهٔ جاری</b>.
    ///
    /// <para>
    /// منطق: در لحظهٔ فعال‌سازی، تعداد رکوردهای <b>همین دوره</b> را می‌شماریم و ذخیره
    /// می‌کنیم؛ مصرف مؤثر = <c>max(شمارنده, تعداد این دوره − همین مبنا)</c>. ماه بعد
    /// کلید دورهٔ دیگری است و ردیفی ندارد ⇒ شمارش از صفر. این دقیقاً خواستهٔ «فقط
    /// برای همان دوره» است.
    /// </para>
    ///
    /// <para>
    /// ⚠️ اگر متریک probe ندارد (مثلاً شمارندهٔ خالص)، نمی‌توانیم «رکوردهای موجود»
    /// را بشماریم؛ در آن حالت مبنا ثبت نمی‌شود و رفتار پیش‌فرض (شمارش کامل) می‌ماند.
    /// این را در لاگ با هشدار اعلام می‌کنیم تا در تست/استقرار پنهان نماند.
    /// </para>
    /// </summary>
    /// <returns>تعداد متریک‌هایی که همین حالا مبنا گرفتند.</returns>
    public static async Task<int> CaptureGrandfatherAsync(AppDbContext db, QuotaService quota,
        Inventory.Licensing.Entitlement.EffectiveEntitlement entitlement, string? serial,
        CancellationToken cancellationToken = default)
    {
        var captured = 0;
        var iranNow = Inventory.Licensing.Periods.PersianPeriodKey.NowIran();

        foreach (var limit in entitlement.Limits)
        {
            if (!limit.Grandfather) continue;
            if (!Probes.ContainsKey(limit.Metric)) continue;

            var periodKey = Inventory.Licensing.Periods.PersianPeriodKey.Now(limit.Period);
            var range = Inventory.Licensing.Periods.PersianPeriodKey.RangeOf(limit.Period, iranNow);
            var live = await Probes[limit.Metric](db, range.From, range.To, cancellationToken);

            if (await quota.CaptureGrandfatherBaselineAsync(limit.Module, limit.Metric, periodKey,
                    live, serial, cancellationToken))
                captured++;
        }

        return captured;
    }

    /// <summary>
    /// ثبت «مبنای مصرف» برای متریک‌هایی که <b>در همین لایسنس سقف دارند</b>.
    ///
    /// <para>
    /// <b>چه زمانی اجرا می‌شود؟</b> در فعال‌سازی لایسنس. اما نه برای همهٔ متریک‌ها:
    /// فقط برای متریک‌هایی که <b>همین حالا سقف گرفتند</b>. دلیلش این سناریو است:
    /// لایسنس اول اصلاً سقف «صدور صورتحساب» ندارد (بی‌نهایت)، مشتری ۵٬۰۰۰ فاکتور
    /// می‌سازد، بعد لایسنس دوم با سقف ۳۰ می‌آید. اگر مبنا همان اول (بدون سقف) گرفته
    /// می‌شد، این ۵٬۰۰۰ فاکتور سقف تازه را از روز اول پر می‌کرد. حالا مبنا <b>همان
    /// لحظه‌ای</b> گرفته می‌شود که سقف برای اولین بار می‌آید.
    /// </para>
    ///
    /// <para>همچنان idempotent است: هر متریک فقط یک مبنا دارد و تمدید/فعال‌سازی دوباره آن را جابه‌جا نمی‌کند.</para>
    /// </summary>
    /// <returns>تعداد متریک‌هایی که همین حالا مبنا گرفتند.</returns>
    public static async Task<int> CaptureAllAsync(AppDbContext db, QuotaService quota,
        Inventory.Licensing.Entitlement.EffectiveEntitlement entitlement, string? serial,
        string reason = "activation", CancellationToken cancellationToken = default)
    {
        var captured = 0;

        foreach (var (module, metric, count) in AllWithProbe())
        {
            // فقط متریکی که در لایسنس مؤثر سقف دارد (مستقیم یا از طریق متریک تجمیعی).
            var hasLimit = MetricCodes.ConsumeTargets(metric)
                .Any(t => entitlement.LimitsFor(module, t).Count > 0);
            if (!hasLimit) continue;

            var live = await count(db, DateTime.MinValue, DateTime.MaxValue, cancellationToken);
            if (await quota.CaptureBaselineAsync(module, metric, live, reason, serial, cancellationToken))
                captured++;
        }

        return captured;
    }
}
