using System.Data;
using Inventory.Api.Data;
using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Metrics;
using Inventory.Licensing.Periods;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Licensing.Services;

/// <summary>
/// اجرای اتمیک سهمیه.
///
/// <para>
/// <b>چرا اتمیک؟</b> اگر ابتدا <c>SELECT</c> بزنیم و بعد <c>UPDATE</c>، دو درخواست
/// هم‌زمان می‌توانند هر دو «جا داریم» را ببینند و سقف یکی رد شود. اینجا شرط داخل خودِ
/// <c>UPDATE</c> است و تعداد ردیف‌های تغییرکرده ملاک است:
/// <code>
/// UPDATE LicenseUsages
///    SET Used = Used + @n, [Limit] = @limit, UpdatedAt = @now
///  WHERE Module=@m AND Metric=@met AND PeriodKey=@pk
///    AND Used + @n &lt;= @limit
/// </code>
/// اگر صفر ردیف تغییر کند، یعنی سقف پر است (یا ردیف نیست). پس دو مسیر ممکن است نیاز به
/// ساخت ردیف داشته باشد؛ ساخت ردیف هم با «درج اگر نبود» انجام می‌شود و در صورت مسابقه
/// خطای کلید یکتا گرفته و یک‌بار دیگر تلاش می‌شود.
/// </para>
///
/// <para>
/// <b>Rollback در همان ترنزکشن:</b> فراخوان می‌تواند با
/// <c>QuotaGuard.ExecuteAsync</c> این متد را داخل ترنزکشن خودش صدا بزند؛ در آن حالت اگر
/// ثبت رکورد شکست بخورد، افزایش شمارنده هم برمی‌گردد. (مرحله ۲)
/// </para>
/// </summary>
public sealed class QuotaService
{
    private readonly AppDbContext _db;
    private readonly ILogger<QuotaService> _log;

    public QuotaService(AppDbContext db, ILogger<QuotaService> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>
    /// مصرف یک واحد از متریک.
    /// اگر برای این متریک هیچ سقفی تعریف نشده باشد، بی‌درنگ «مجاز» برمی‌گردد (لایسنس
    /// بدون سهمیه = بدون سقف).
    /// </summary>
    public async Task<QuotaConsumeResult> TryConsumeAsync(EffectiveEntitlement entitlement, string module,
        string metric, int amount = 1, CancellationToken cancellationToken = default)
    {
        if (amount <= 0) return QuotaConsumeResult.Unlimited();

        var targets = MetricCodes.ConsumeTargets(metric);
        foreach (var target in targets)
        {
            var limits = entitlement.LimitsFor(module, target);
            foreach (var limit in limits)
            {
                var result = await ConsumeOneAsync(limit, amount, cancellationToken);
                if (!result.Allowed) return result;
            }
        }

        return QuotaConsumeResult.Unlimited();
    }

    private async Task<QuotaConsumeResult> ConsumeOneAsync(MetricLimit limit, int amount, CancellationToken cancellationToken)
    {
        var periodKey = PersianPeriodKey.Now(limit.Period);
        var now = DateTime.Now;
        var isSqlite = _db.Database.IsSqlite();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            // ---------- ۱) اطمینان از وجود ردیف ----------
            await EnsureRowAsync(limit, periodKey, now, isSqlite, cancellationToken);

            // ---------- ۲) افزایش اتمیک مشروط ----------
            var affected = isSqlite
                ? await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE ""LicenseUsages""
   SET ""Used"" = ""Used"" + {amount},
       ""Limit"" = {limit.Limit},
       ""UpdatedAt"" = {now}
 WHERE ""Module"" = {limit.Module} AND ""Metric"" = {limit.Metric} AND ""PeriodKey"" = {periodKey}
   AND ""Used"" + {amount} <= {limit.Limit}", cancellationToken)
                : await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [dbo].[LicenseUsages]
   SET [Used] = [Used] + {amount},
       [Limit] = {limit.Limit},
       [UpdatedAt] = {now}
 WHERE [Module] = {limit.Module} AND [Metric] = {limit.Metric} AND [PeriodKey] = {periodKey}
   AND [Used] + {amount} <= {limit.Limit}", cancellationToken);

            if (affected > 0)
            {
                var used = await ReadUsedAsync(limit, periodKey, cancellationToken);
                return QuotaConsumeResult.Ok(limit.Metric, used, limit.Limit, periodKey);
            }

            // صفر ردیف: یا سقف پر است، یا ردیف در اثر مسابقه ساخته نشده.
            var current = await ReadUsedAsync(limit, periodKey, cancellationToken);
            if (current is null) continue;   // ردیف نیست → دوباره بساز

            var denied = QuotaConsumeResult.Denied(limit.Metric, current.Value, limit.Limit, periodKey,
                $"سقف «{MetricCodes.TitleOf(limit.Metric)}» ({limit.Limit:N0} {MetricCodes.UnitOf(limit.Metric)}) " +
                $"در دورهٔ «{PersianPeriodKey.Describe(limit.Period, periodKey)}» پر شده است. " +
                "برای افزایش سقف با پشتیبانی تماس بگیرید.");
            _log.LogWarning("[License] سقف سهمیه رد شد: {Metric} {Period} used={Used} limit={Limit}",
                limit.Metric, periodKey, current.Value, limit.Limit);
            return denied;
        }

        return QuotaConsumeResult.Denied(limit.Metric, 0, limit.Limit, periodKey,
            "ثبت سهمیه به دلیل ازدحام هم‌زمان ممکن نشد؛ لطفاً دوباره تلاش کنید.");
    }

    /// <summary>ساخت ردیف مصرف در صورت نبود — ایمن در برابر درخواست هم‌زمان.</summary>
    private async Task EnsureRowAsync(MetricLimit limit, string periodKey, DateTime now, bool isSqlite,
        CancellationToken cancellationToken)
    {
        try
        {
            if (isSqlite)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT OR IGNORE INTO ""LicenseUsages"" (""Module"", ""Metric"", ""PeriodKey"", ""Limit"", ""Used"", ""UpdatedAt"")
VALUES ({limit.Module}, {limit.Metric}, {periodKey}, {limit.Limit}, 0, {now})", cancellationToken);
            }
            else
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
IF NOT EXISTS (SELECT 1 FROM [dbo].[LicenseUsages]
               WHERE [Module] = {limit.Module} AND [Metric] = {limit.Metric} AND [PeriodKey] = {periodKey})
    INSERT INTO [dbo].[LicenseUsages] ([Module], [Metric], [PeriodKey], [Limit], [Used], [UpdatedAt])
    VALUES ({limit.Module}, {limit.Metric}, {periodKey}, {limit.Limit}, 0, {now})", cancellationToken);
            }
        }
        catch (DbUpdateException ex)
        {
            // دو درخواست هم‌زمان خواستند ردیف را بسازند؛ دومی به کلید یکتا می‌خورد.
            // این خطا بی‌خطر است — ردیف ساخته شده و ادامهٔ کار درست است.
            _log.LogDebug(ex, "[License] ساخت هم‌زمان ردیف مصرف (بی‌خطر).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "[License] ساخت ردیف مصرف ناموفق بود: {Metric}", limit.Metric);
        }
    }

    private async Task<long?> ReadUsedAsync(MetricLimit limit, string periodKey, CancellationToken cancellationToken)
    {
        var row = await _db.LicenseUsages.AsNoTracking()
            .Where(u => u.Module == limit.Module && u.Metric == limit.Metric && u.PeriodKey == periodKey)
            .Select(u => (long?)u.Used)
            .FirstOrDefaultAsync(cancellationToken);
        return row;
    }
}

/// <summary>
/// اجراکنندهٔ عملیات دارای سهمیه — الگوی «یک خط» برای نقاط ثبت رکورد.
///
/// <para>
/// نمونهٔ استفاده در ثبت فاکتور:
/// <code>
/// var guard = await QuotaGuard.CheckAsync(licenses, "invoicing", MetricCodes.InvoiceCreate);
/// if (!guard.Allowed) return BadRequest(new { message = guard.Message, code = guard.Code });
/// </code>
/// </para>
///
/// <para>
/// در مرحله ۲، نسخهٔ ترنزکشن‌دار (<c>ExecuteAsync</c>) اضافه می‌شود تا افزایش شمارنده
/// و ثبت رکورد با هم Commit/Rollback شوند.
/// </para>
/// </summary>
public static class QuotaGuard
{
    /// <summary>بررسی/مصرف سهمیه پیش از ثبت رکورد.</summary>
    public static async Task<QuotaConsumeResult> CheckAsync(ILicenseService licenses, string module,
        string metric, int amount = 1, CancellationToken cancellationToken = default)
        => await licenses.TryConsumeAsync(module, metric, amount, cancellationToken);
}
