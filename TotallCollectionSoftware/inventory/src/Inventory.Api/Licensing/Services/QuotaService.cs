using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Licensing;
using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Metrics;
using Inventory.Licensing.Model;
using Inventory.Licensing.Periods;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Licensing.Services;

/// <summary>
/// اجرای اتمیک سهمیه — مرحله ۲.
///
/// <para>
/// <b>چرا اتمیک؟</b> اگر ابتدا <c>SELECT</c> بزنیم و بعد <c>UPDATE</c>، دو درخواست
/// هم‌زمان می‌توانند هر دو «جا داریم» را ببینند و سقف یکی رد شود. اینجا شرط داخل خودِ
/// <c>UPDATE</c> است و تعداد ردیف‌های تغییرکرده ملاک است:
/// <code>
/// UPDATE LicenseUsages
///    SET Used = Used + @n, [Limit] = @limit, UpdatedAt = @now, Hmac = @hmac
///  WHERE Module=@m AND Metric=@met AND PeriodKey=@pk
///    AND Used + @n &lt;= @limit
/// </code>
/// اگر صفر ردیف تغییر کند، یعنی سقف پر است (یا ردیف نیست). تعداد ردیف‌های تغییرکرده
/// همان «rowcount»ی است که تصمیم را می‌گیرد — هیچ پنجرهٔ مسابقه‌ای باقی نمی‌ماند.
/// </para>
///
/// <para>
/// <b>تراکنش:</b> فراخوان باید با <see cref="QuotaGuard.ExecuteAsync"/> صدا بزند تا افزایش
/// شمارنده و ثبت رکورد کسب‌وکار در <b>یک تراکنش</b> باشند؛ اگر ثبت رکورد شکست بخورد،
/// افزایش شمارنده هم برمی‌گردد.
/// </para>
/// </summary>
public sealed class QuotaService
{
    private readonly AppDbContext _db;
    private readonly QuotaIntegrity _integrity;
    private readonly LicenseEventLogger _events;
    private readonly ILogger<QuotaService> _log;

    public QuotaService(AppDbContext db, QuotaIntegrity integrity, LicenseEventLogger events,
        ILogger<QuotaService> log)
    {
        _db = db;
        _integrity = integrity;
        _events = events;
        _log = log;
    }

    /// <summary>
    /// مصرف یک واحد از متریک (و متریک‌های تجمیعیِ دربرگیرندهٔ آن).
    /// اگر برای این متریک هیچ سقفی تعریف نشده باشد، بی‌درنگ «مجاز» برمی‌گردد.
    /// </summary>
    public async Task<QuotaConsumeResult> TryConsumeAsync(EffectiveEntitlement entitlement, string module,
        string metric, int amount = 1, string? entityType = null, string? entityId = null,
        int? userId = null, bool recordLedger = true, CancellationToken cancellationToken = default)
    {
        if (amount <= 0) return QuotaConsumeResult.Unlimited();

        var targets = MetricCodes.ConsumeTargets(metric);
        QuotaConsumeResult? last = null;
        foreach (var target in targets)
        {
            foreach (var limit in entitlement.LimitsFor(module, target))
            {
                var result = await ConsumeOneAsync(limit, amount, entityType, entityId, userId, recordLedger,
                    cancellationToken);
                if (!result.Allowed) return result;

                // نتیجهٔ «مصرف‌شده» را نگه می‌داریم تا Used/Limit واقعی به فراخوان برسد
                // (هشدار ۸۰٪ و نمایش «۳ از ۱۰۰» به همین عدد وابسته است).
                last = result;
            }
        }

        // متریک‌های تجمیعی بعد از متریک اختصاصی می‌آیند؛ پس آخرین نتیجه همان سقف
        // دربرگیرنده است که برای هشدار ۸۰٪ معنادارتر است.
        return last ?? QuotaConsumeResult.Unlimited();
    }

    /// <summary>آیا این متریک برای این ماژول سقفی دارد؟ (برای پرهیز از تراکنش بی‌مورد)</summary>
    public static bool HasLimit(EffectiveEntitlement entitlement, string module, string metric)
        => MetricCodes.ConsumeTargets(metric).Any(t => entitlement.LimitsFor(module, t).Count > 0);

    private async Task<QuotaConsumeResult> ConsumeOneAsync(MetricLimit limit, int amount, string? entityType,
        string? entityId, int? userId, bool recordLedger, CancellationToken cancellationToken)
    {
        var periodKey = PersianPeriodKey.Now(limit.Period);
        var now = DateTime.Now;
        var isSqlite = _db.Database.IsSqlite();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            // ---------- ۱) اطمینان از وجود ردیف ----------
            await EnsureRowAsync(limit, periodKey, now, isSqlite, cancellationToken);

            // ---------- ۲) افزایش اتمیک مشروط ----------
            var affected = await IncrementAsync(limit, periodKey, amount, now, isSqlite, cancellationToken);

            if (affected > 0)
            {
                // HMAC پس از افزایش نوشته می‌شود (چون به مقدار نهایی Used وابسته است).
                await StampAsync(limit, periodKey, cancellationToken);
                if (recordLedger)
                    await LedgerAsync(limit, periodKey, amount, entityType, entityId, userId, cancellationToken);

                var used = await ReadUsedAsync(limit, periodKey, cancellationToken) ?? amount;
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

    /// <summary>
    /// افزایش اتمیک مشروط — تعداد ردیف‌های تغییرکرده برمی‌گردد.
    ///
    /// <para>
    /// در حالت SQLite (محیط توسعه/تست) ممکن است نوشتن هم‌زمان به <c>SQLITE_BUSY</c> بخورد؛
    /// چون تراکنش کوتاه است، چند تلاش کوتاه با فاصلهٔ تصادفی مشکل را حل می‌کند و پیام
    /// «دیتابیس قفل است» به کاربر نمی‌رسد. روی SQL Server این مسیر اجرا نمی‌شود
    /// (قفل‌ها خودشان مدیریت می‌شوند و UPDATE شرطی اتمیک است).
    /// </para>
    /// </summary>
    private async Task<int> IncrementAsync(MetricLimit limit, string periodKey, int amount, DateTime now,
        bool isSqlite, CancellationToken cancellationToken)
    {
        if (isSqlite)
        {
            for (var busy = 0; ; busy++)
            {
                try { return await IncrementSqliteAsync(limit, periodKey, amount, now, cancellationToken); }
                catch (Microsoft.Data.Sqlite.SqliteException ex)
                    when ((ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6) && busy < 8)
                {
                    await Task.Delay(Random.Shared.Next(10, 40 + busy * 20), cancellationToken);
                }
            }
        }

        return await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [dbo].[LicenseUsages]
   SET [Used] = [Used] + {amount},
       [Limit] = {limit.Limit},
       [UpdatedAt] = {now}
 WHERE [Module] = {limit.Module} AND [Metric] = {limit.Metric} AND [PeriodKey] = {periodKey}
   AND [Used] + {amount} <= {limit.Limit}", cancellationToken);
    }

    private async Task<int> IncrementSqliteAsync(MetricLimit limit, string periodKey, int amount, DateTime now,
        CancellationToken cancellationToken)
    {
        {
            return await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE ""LicenseUsages""
   SET ""Used"" = ""Used"" + {amount},
       ""Limit"" = {limit.Limit},
       ""UpdatedAt"" = {now}
 WHERE ""Module"" = {limit.Module} AND ""Metric"" = {limit.Metric} AND ""PeriodKey"" = {periodKey}
   AND ""Used"" + {amount} <= {limit.Limit}", cancellationToken);
        }
    }

    /// <summary>
    /// نوشتن HMAC ردیف پس از تغییر مقدار — به‌صورت <b>شرطی</b>.
    ///
    /// <para>
    /// HMAC به مقدار نهایی <c>Used</c> و <c>Limit</c> وابسته است؛ اگر ابتدا بخوانیم و بعد
    /// با <c>SaveChanges</c> بنویسیم، در بار هم‌زمان یک درخواست می‌تواند مقدار کهنه را مهر
    /// بزند و ردیفِ درست، «دست‌کاری‌شده» به نظر برسد (تست بخش ۷ همین را گرفت). پس مهر
    /// فقط وقتی می‌نشیند که مقدار خوانده‌شده همان لحظه هم معتبر باشد:
    /// <code>UPDATE … SET Hmac=&lt;h&gt; WHERE … AND Used=&lt;u&gt; AND Limit=&lt;l&gt;</code>
    /// اگر صفر ردیف تغییر کند، یعنی بین خواندن و نوشتن دیگری جلو افتاده؛ دوباره می‌خوانیم.
    /// پس آخرین نویسنده، مهر متناسب با مقدار نهایی را می‌گذارد.
    /// </para>
    /// </summary>
    private async Task StampAsync(MetricLimit limit, string periodKey, CancellationToken cancellationToken)
    {
        if (!_integrity.Enabled) return;

        var isSqlite = _db.Database.IsSqlite();

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var snapshot = await _db.LicenseUsages.AsNoTracking()
                .Where(u => u.Module == limit.Module && u.Metric == limit.Metric && u.PeriodKey == periodKey)
                .Select(u => new { u.Limit, u.Used })
                .FirstOrDefaultAsync(cancellationToken);
            if (snapshot is null) return;

            var hmac = _integrity.ForUsage(limit.Module, limit.Metric, periodKey, snapshot.Limit, snapshot.Used);

            var rows = isSqlite
                ? await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE ""LicenseUsages"" SET ""Hmac"" = {hmac}
 WHERE ""Module"" = {limit.Module} AND ""Metric"" = {limit.Metric} AND ""PeriodKey"" = {periodKey}
   AND ""Used"" = {snapshot.Used} AND ""Limit"" = {snapshot.Limit}", cancellationToken)
                : await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [dbo].[LicenseUsages] SET [Hmac] = {hmac}
 WHERE [Module] = {limit.Module} AND [Metric] = {limit.Metric} AND [PeriodKey] = {periodKey}
   AND [Used] = {snapshot.Used} AND [Limit] = {snapshot.Limit}", cancellationToken);

            if (rows > 0) return;

            await Task.Delay(Random.Shared.Next(5, 25), cancellationToken);
        }

        _log.LogDebug("[License] مهر HMAC ردیف مصرف پس از چند تلاش به‌روز نشد: {Metric}", limit.Metric);
    }

    /// <summary>ثبت ردیف در دفتر الحاقی مصرف.</summary>
    private async Task LedgerAsync(MetricLimit limit, string periodKey, int delta, string? entityType,
        string? entityId, int? userId, CancellationToken cancellationToken)
    {
        var entry = new LicenseUsageLedger
        {
            Module = limit.Module,
            Metric = limit.Metric,
            PeriodKey = periodKey,
            Delta = delta,
            EntityType = entityType,
            EntityId = entityId,
            Reason = delta >= 0 ? "create" : "delete",
            At = DateTime.Now,
            UserId = userId
        };
        entry.Hmac = _integrity.ForLedger(entry.Module, entry.Metric, entry.PeriodKey, entry.Delta,
            entry.EntityType, entry.EntityId, entry.At);

        _db.LicenseUsageLedger.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// ثبت ردیف دفتر مصرف با شناسهٔ <b>واقعیِ</b> رکورد.
    ///
    /// <para>
    /// شناسهٔ فاکتور/نامه تا پیش از <c>SaveChanges</c> وجود ندارد؛ اگر همان لحظهٔ مصرف
    /// ثبت شود، دفتر مصرف شناسهٔ ۰ می‌گیرد و «لاگ حذف» به رکورد واقعی وصل نمی‌شود.
    /// پس ترتیبی که استفاده می‌شود این است: مصرف اتمیک → ثبت رکورد → ثبت دفتر با
    /// شناسهٔ واقعی، همه داخل یک تراکنش (توسط <see cref="QuotaGuard.ExecuteAsync"/>).
    /// </para>
    /// </summary>
    public async Task RecordUsageAsync(EffectiveEntitlement entitlement, string module, string metric,
        int amount = 1, string? entityType = null, string? entityId = null, int? userId = null,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0) return;

        foreach (var target in MetricCodes.ConsumeTargets(metric))
            foreach (var limit in entitlement.LimitsFor(module, target))
                await LedgerAsync(limit, PersianPeriodKey.Now(limit.Period), amount, entityType, entityId, userId,
                    cancellationToken);
    }

    // ==================== بازگشت مصرف (حذف رکورد) ====================

    /// <summary>
    /// بازگشت مصرف هنگام حذف یک رکورد.
    ///
    /// <para>
    /// قاعدهٔ سند: <b>دورهٔ دائمی (Lifetime) بازگردانده نمی‌شود</b> — «یک‌بار مصرف شد، تمام».
    /// ولی رخداد حذف در <c>LicenseEvents</c> ثبت می‌شود تا ممیزی از دست نرود.
    /// دوره‌های ماهانه/سالانه با همان شرط اتمیک (منفی نشدن) کم می‌شوند.
    /// </para>
    /// </summary>
    public async Task<bool> TryRefundAsync(EffectiveEntitlement entitlement, string module, string metric,
        int amount = 1, string? entityType = null, string? entityId = null,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0) return false;

        var isSqlite = _db.Database.IsSqlite();
        var refunded = false;

        foreach (var target in MetricCodes.ConsumeTargets(metric))
        {
            foreach (var limit in entitlement.LimitsFor(module, target))
            {
                var periodKey = PersianPeriodKey.Now(limit.Period);

                if (limit.Period == QuotaPeriod.Lifetime)
                {
                    await _events.WriteAsync("LICENSE_QUOTA_NO_REFUND",
                        $"رکورد {entityType}#{entityId} حذف شد، ولی سهمیهٔ دائمی «{MetricCodes.TitleOf(limit.Metric)}» " +
                        "بازگردانده نشد (قاعدهٔ «یک‌بار مصرف، بدون بازگشت»).",
                        severity: "Info");
                    continue;
                }

                var affected = isSqlite
                    ? await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE ""LicenseUsages"" SET ""Used"" = ""Used"" - {amount}, ""UpdatedAt"" = {DateTime.Now}
 WHERE ""Module"" = {limit.Module} AND ""Metric"" = {limit.Metric} AND ""PeriodKey"" = {periodKey}
   AND ""Used"" - {amount} >= 0", cancellationToken)
                    : await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [dbo].[LicenseUsages] SET [Used] = [Used] - {amount}, [UpdatedAt] = {DateTime.Now}
 WHERE [Module] = {limit.Module} AND [Metric] = {limit.Metric} AND [PeriodKey] = {periodKey}
   AND [Used] - {amount} >= 0", cancellationToken);

                if (affected > 0)
                {
                    refunded = true;
                    await StampAsync(limit, periodKey, cancellationToken);
                    await LedgerAsync(limit, periodKey, -amount, entityType, entityId, null, cancellationToken);
                }
            }
        }

        return refunded;
    }

    // ==================== خواندن مصرف مؤثر ====================

    /// <summary>
    /// مصرف مؤثر یک متریک برای نمایش و مقایسه.
    ///
    /// <para>
    /// برای متریک‌های نوع «تعداد فعال» (<see cref="MetricKind.ActiveCount"/>)، عدد واقعی
    /// ردیف‌های موجود هم شمرده می‌شود و <b>بیشترین</b> مقدار ملاک است؛ چون شمارنده ممکن است
    /// از قبل کمتر یا بیشتر باشد (مثلاً رکوردهایی که قبل از نصب سیستم لایسنس ساخته شده‌اند).
    /// </para>
    /// </summary>
    public async Task<long> GetEffectiveUsageAsync(EffectiveEntitlement entitlement, MetricLimit limit,
        Func<CancellationToken, Task<long>>? liveCount, CancellationToken cancellationToken = default)
    {
        var periodKey = PersianPeriodKey.Now(limit.Period);
        var counter = await ReadUsedAsync(limit, periodKey, cancellationToken) ?? 0;

        var definition = MetricCodes.Find(limit.Metric);
        if (definition?.Kind != MetricKind.ActiveCount || liveCount is null) return counter;

        var live = await liveCount(cancellationToken);
        return Math.Max(counter, live);
    }

    // ==================== بررسی دست‌کاری ====================

    /// <summary>
    /// بازبینی یکپارچگی همهٔ ردیف‌های مصرف. هر ردیفی که HMAC نخواند، رخداد ثبت می‌شود.
    /// </summary>
    public async Task<int> VerifyIntegrityAsync(CancellationToken cancellationToken = default)
    {
        if (!_integrity.Enabled) return 0;

        var rows = await _db.LicenseUsages.AsNoTracking().ToListAsync(cancellationToken);
        var tampered = 0;

        foreach (var row in rows)
        {
            var expected = _integrity.ForUsage(row.Module, row.Metric, row.PeriodKey, row.Limit, row.Used);
            if (_integrity.Matches(expected, row.Hmac)) continue;

            tampered++;
            await _events.WriteAsync("LICENSE_QUOTA_TAMPERED",
                $"ردیف مصرف «{row.Metric}» دورهٔ «{row.PeriodKey}» خارج از برنامه تغییر کرده است " +
                $"(مقدار فعلی {row.Used} از {row.Limit}). بررسی دستی لازم است.",
                severity: "Warning", serial: null);
        }

        if (tampered > 0)
            _log.LogWarning("[License] {Count} ردیف مصرف سهمیه دست‌کاری‌شده پیدا شد.", tampered);

        return tampered;
    }

    // ==================== کمکی‌ها ====================

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
            // دو درخواست هم‌زمان خواستند ردیف را بسازند؛ دومی به کلید یکتا می‌خورد. بی‌خطر است.
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
/// اجراکنندهٔ عملیات دارای سهمیه — «یک خط» برای نقاط ثبت رکورد.
///
/// <para>
/// نمونهٔ استفاده در ثبت فاکتور:
/// <code>
/// await using var tx = await _db.Database.BeginTransactionAsync();
/// var guard = await QuotaGuard.CheckAsync(licenses, "invoicing", MetricCodes.InvoiceCreate);
/// if (!guard.Allowed) return BadRequest(...);
/// ... ثبت رکورد ...
/// await tx.CommitAsync();          ← اگر این‌جا خطا بیاید، شمارنده هم برمی‌گردد
/// </code>
/// </para>
/// </summary>
public static class QuotaGuard
{
    /// <summary>بررسی/مصرف سهمیه (بدون مالکیت تراکنش).</summary>
    public static async Task<QuotaConsumeResult> CheckAsync(ILicenseService licenses, string module,
        string metric, int amount = 1, string? entityType = null, string? entityId = null,
        CancellationToken cancellationToken = default)
        => await licenses.TryConsumeAsync(module, metric, amount, entityType, entityId,
            cancellationToken: cancellationToken);

    /// <summary>
    /// اجرای یک عملیات داخل تراکنش، همراه با مصرف سهمیه.
    ///
    /// <para>
    /// اگر عملیات استثنا بدهد، تراکنش Rollback می‌شود و شمارندهٔ سهمیه هم برمی‌گردد —
    /// یعنی «فاکتور ثبت نشد ولی سهمیه کم شد» هرگز رخ نمی‌دهد.
    /// </para>
    /// </summary>
    public static async Task<QuotaTransactionResult> ExecuteAsync(ILicenseService licenses, AppDbContext db,
        string module, string metric, Func<Task> operation, int amount = 1,
        string? entityType = null, string? entityId = null,
        Func<string?>? entityIdFactory = null, CancellationToken cancellationToken = default)
    {
        var entitlement = await licenses.GetEffectiveEntitlementAsync(cancellationToken);

        // اگر سقفی نیست، تراکنش اضافه باز نمی‌کنیم.
        if (!QuotaService.HasLimit(entitlement, module, metric))
        {
            var plain = await licenses.TryConsumeAsync(module, metric, amount,
                entityType, entityId, userId: null, cancellationToken: cancellationToken);
            if (!plain.Allowed)
            {
                DetachPendingAdds(db);
                return QuotaTransactionResult.Denied(plain.Message);
            }

            await operation();
            return QuotaTransactionResult.Success(plain);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // دفتر مصرف این‌جا نوشته می‌شود نه داخل TryConsumeAsync؛ چون شناسهٔ رکورد
            // تازه پس از SaveChanges ساخته می‌شود و دفتر باید به همان شناسه وصل باشد.
            var consume = await licenses.TryConsumeAsync(module, metric, amount,
                entityType, entityId, userId: null, recordLedger: false, cancellationToken: cancellationToken);
            if (!consume.Allowed)
            {
                await transaction.RollbackAsync(cancellationToken);
                DetachPendingAdds(db);
                return QuotaTransactionResult.Denied(consume.Message);
            }

            await operation();

            var resolvedId = entityIdFactory is null ? entityId : entityIdFactory();
            await licenses.RecordUsageAsync(module, metric, amount, entityType, resolvedId, userId: null,
                cancellationToken: cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return QuotaTransactionResult.Success(consume);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            DetachPendingAdds(db);
            throw;
        }
    }

    /// <summary>
    /// وقتی سهمیه رد می‌شود، رکوردی که فراخوان قبل از گارد به Context اضافه کرده
    /// (مثل فاکتور تازه‌ساخته) نباید در حافظه باقی بماند؛ وگرنه هر <c>SaveChanges</c>
    /// بعدی در همان درخواست — مثلاً ثبت رخداد خطا — آن را ناخواسته ذخیره می‌کند.
    /// این دقیقاً در تست زنده دید شد: فاکتورِ ردشده با ۴۰۰ برگشت ولی در دیتابیس بود.
    /// </summary>
    private static void DetachPendingAdds(AppDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
            entry.State = EntityState.Detached;
    }
}

/// <summary>نتیجهٔ اجرای عملیات دارای سهمیه.</summary>
public sealed class QuotaTransactionResult
{
    public bool Allowed { get; init; }
    public string Message { get; init; } = "";
    public QuotaConsumeResult? Quota { get; init; }

    public static QuotaTransactionResult Success(QuotaConsumeResult quota) => new() { Allowed = true, Quota = quota };
    public static QuotaTransactionResult Denied(string message) => new() { Allowed = false, Message = message };
}
