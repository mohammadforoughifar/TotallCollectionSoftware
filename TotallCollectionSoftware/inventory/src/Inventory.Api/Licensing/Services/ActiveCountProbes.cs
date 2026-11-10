using Inventory.Api.Data;
using Inventory.Licensing.Metrics;
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
/// </summary>
public static class ActiveCountProbes
{
    /// <summary>متریک → تابع شمارش رکوردهای همین لحظه (با احتساب حذف‌نرم‌ها).</summary>
    public static readonly IReadOnlyDictionary<string, Func<AppDbContext, CancellationToken, Task<long>>> Probes =
        new Dictionary<string, Func<AppDbContext, CancellationToken, Task<long>>>(StringComparer.OrdinalIgnoreCase)
        {
            [MetricCodes.InvoiceCreate] = (db, ct) =>
                db.FacInvoices.IgnoreQueryFilters().LongCountAsync(ct),

            [MetricCodes.LetterIncomingCreate] = (db, ct) =>
                db.IncomingLetters.IgnoreQueryFilters().LongCountAsync(ct),

            [MetricCodes.LetterOutgoingCreate] = (db, ct) =>
                db.OutgoingLetters.IgnoreQueryFilters().LongCountAsync(ct)
        };

    /// <summary>همهٔ متریک‌های ActiveCount که probe دارند.</summary>
    public static IEnumerable<(string Module, string Metric,
        Func<AppDbContext, CancellationToken, Task<long>> Count)> All()
    {
        foreach (var metric in MetricCodes.All)
        {
            if (metric.Kind != MetricKind.ActiveCount) continue;
            if (!Probes.TryGetValue(metric.Code, out var count)) continue;
            yield return (metric.Module, metric.Code, count);
        }
    }

    /// <summary>
    /// ثبت مبنای مصرف برای همهٔ متریک‌های ActiveCount که تازه فعال شده‌اند.
    /// اگر هیچ متریکی از این نوع نباشد، هیچ کاری نمی‌کند (وضعیت امروز).
    /// </summary>
    /// <returns>تعداد متریک‌هایی که همین حالا مبنا گرفتند.</returns>
    public static async Task<int> CaptureAllAsync(AppDbContext db, QuotaService quota, string? serial,
        string reason = "activation", CancellationToken cancellationToken = default)
    {
        var captured = 0;

        foreach (var (module, metric, count) in All())
        {
            var live = await count(db, cancellationToken);
            if (await quota.CaptureBaselineAsync(module, metric, live, reason, serial, cancellationToken))
                captured++;
        }

        return captured;
    }
}
