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
/// <summary>
/// نتیجهٔ رزرو: هم پاسخ سهمیه، هم شناسهٔ ردیف‌های رزرو.
///
/// <para>
/// چرا شناسه لازم است؟ چون «رزرو» حالا یک ردیف با وضعیت و زمان است
/// (<see cref="LicenseReservation"/>). بدون شناسه، تسویه نمی‌داند کدام ردیف را
/// باید <c>Confirmed</c>/<c>Refunded</c> کند و رزروِ نیمه‌کاره برای همیشه
/// در وضعیت <c>Reserved</c> می‌ماند.
/// </para>
/// </summary>
public readonly record struct QuotaReservationResult(QuotaConsumeResult Result, IReadOnlyList<long> ReservationIds)
{
    public static QuotaReservationResult From(QuotaConsumeResult result) => new(result, Array.Empty<long>());
}

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
        // ساعت تهران (نه ساعت سرور): هم کلید دوره و هم زمان ثبت، مبنای ایران دارند.
        var now = PersianPeriodKey.NowIran();
        var isSqlite = _db.Database.IsSqlite();

        // ---------- ۰) بررسی «مصرف مؤثر» برای دوره‌های ماهانه/سالانه ----------
        //
        // ⚠️ چرا این بررسی لازم شد (بازبینی سوم، بند «بخشودگی گذشته»):
        // تا پیش از این، شمارندهٔ مصرف از صفر شروع می‌شد و رکوردهایی که **قبلاً** در همان
        // ماه وجود داشتند هیچ‌وقت در سقف ماهانه حساب نمی‌شدند. نتیجهٔ عملی: مشتری‌ای که
        // ۳۰۰ نامه در ماه جاری ثبت کرده و لایسنس «۱۰۰ در ماه» می‌خرد، می‌توانست ۱۰۰ نامهٔ
        // دیگر هم ثبت کند — یعنی سقف ماهانه عملاً بی‌اثر بود و گزینهٔ «بخشودگی گذشته»
        // هم معنایی نداشت.
        //
        // اکنون برای متریک‌هایی که شمارش واقعی دارند، رکوردهای همان دوره هم دیده می‌شوند:
        //   مصرف مؤثر = max(شمارنده، ردیف‌های همین دوره − مبنا)
        // با «بخشودگی» روشن، مبنا = ردیف‌های لحظهٔ فعال‌سازی ⇒ مصرف از صفر شروع می‌شود.
        //
        // 🔒 فقط دورهٔ ماهانه/سالانه: بازهٔ شمارش کوچک و ایندکس‌شده است. برای دورهٔ دائمی
        // رفتار قبلی (شمارنده + مبنای فعال‌سازی) حفظ می‌شود تا هر ثبت، یک COUNT روی کل
        // جدول نخواهد — سقف دائمی با «مبنا»ی همان لحظه کار می‌کند.
        if (limit.Period != QuotaPeriod.Lifetime && ActiveCountProbes.HasProbe(limit.Metric))
        {
            var liveNow = await ActiveCountProbes.CountAsync(_db, limit.Metric, limit.Period, now, cancellationToken);
            var counterNow = await ReadUsedAsync(limit, periodKey, cancellationToken) ?? 0;
            var grandfatherNow = limit.Grandfather
                ? await ReadGrandfatherBaselineAsync(limit.Module, limit.Metric, periodKey, cancellationToken)
                : 0;

            var effective = ComputeEffective(counterNow, liveNow, grandfatherNow, periodKey);

            if (effective + amount > limit.Limit)
            {
                return QuotaConsumeResult.Denied(limit.Metric, effective, limit.Limit, periodKey,
                    $"سقف «{MetricCodes.TitleOf(limit.Metric)}» ({limit.Limit:N0} {MetricCodes.UnitOf(limit.Metric)}) " +
                    $"در دورهٔ «{PersianPeriodKey.Describe(limit.Period, periodKey)}» پر شده است. " +
                    (grandfatherNow > 0
                        ? $"رکوردهای پیش از فعال‌سازی این دوره ({grandfatherNow:N0} مورد) از شمارش خارج شده‌اند و " +
                          "سهمیهٔ باقی‌مانده مصرف شده است. "
                        : $"از این تعداد، {liveNow:N0} مورد در همین دوره در سیستم ثبت شده است. ") +
                    "برای افزایش سقف با پشتیبانی تماس بگیرید.");
            }
        }

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
        string? entityId, int? userId, CancellationToken cancellationToken, string? reason = null)
    {
        var entry = new LicenseUsageLedger
        {
            Module = limit.Module,
            Metric = limit.Metric,
            PeriodKey = periodKey,
            Delta = delta,
            EntityType = entityType,
            EntityId = entityId,
            Reason = reason ?? (delta >= 0 ? "create" : "delete"),
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
    /// بازگشت مصرف — <b>فقط برای خطای فنی همان عملیات</b>.
    ///
    /// <para>
    /// ⛔ <b>این متد هرگز نباید به «حذف رکورد توسط کاربر» وصل شود.</b> قاعدهٔ سند:
    /// هر رکوردی که یک‌بار شمرده شد، حتی اگر بعداً حذف شود، مصرفش باقی می‌ماند —
    /// نه در دورهٔ ماهانه و نه در دائمی. وگرنه مشتری می‌توانست با «ایجاد و حذف مکرر»
    /// بی‌نهایت رکورد بسازد و هیچ سقفی اعمال نشود.
    /// </para>
    ///
    /// <para>
    /// تنها مورد مجاز: عملیاتی که <b>هرگز کامل نشد</b> و در همان لحظه شکست خورد —
    /// مثل ارسال به مودیان که با خطای شبکه/استثنا برنگشت. در این حالت هم مصرف
    /// باید بلافاصله و در همان درخواست برگردد تا مشتری بابت عملیات ناموفق جریمه نشود.
    /// </para>
    ///
    /// <para>
    /// دورهٔ دائمی (Lifetime) در هر حال بازگردانده نمی‌شود — «یک‌بار مصرف شد، تمام»؛
    /// فقط رخداد <c>LICENSE_QUOTA_NO_REFUND</c> برای ممیزی ثبت می‌شود.
    /// دوره‌های ماهانه/سالانه با همان شرط اتمیک (منفی نشدن) کم می‌شوند.
    /// </para>
    /// </summary>
    /// <param name="reason">دلیل بازگشت — پیش‌فرض <c>refund-technical</c> (خطای فنی همان عملیات).</param>
    public async Task<bool> TryRefundAsync(EffectiveEntitlement entitlement, string module, string metric,
        int amount = 1, string? entityType = null, string? entityId = null,
        string reason = "refund-technical", CancellationToken cancellationToken = default)
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
                    await LedgerAsync(limit, periodKey, -amount, entityType, entityId, null, cancellationToken, reason);
                }
            }
        }

        return refunded;
    }

    // ==================== رزرو برای عملیات شبکه‌ای (مرحله ۲ — بازبینی) ====================

    /// <summary>
    /// <b>رزرو اتمیک یک واحد در تراکنش جدا و کوتاه</b> — برای عملیاتی که بعدش یک تماس
    /// شبکه‌ای می‌آید (ارسال به مودیان).
    ///
    /// <para>
    /// چرا جدا از <see cref="QuotaGuard.ExecuteAsync"/>؟ چون آن متد شمارنده و ثبت
    /// رکورد را در <b>یک</b> تراکنش نگه می‌دارد و تراکنش تا پایان بلوک باز می‌ماند. اگر
    /// تماس شبکه داخل همان بلوک باشد، قفل ردیف شمارنده در طول تماس (که می‌تواند ده‌ها
    /// ثانیه طول بکشد) نگه داشته می‌شود و بقیهٔ درخواست‌ها پشت آن صف می‌بندند.
    /// </para>
    ///
    /// <para>
    /// ترتیب درست: رزرو (تراکنش جدا، بلافاصله Commit) ← تماس شبکه بیرون از هر تراکنش ←
    /// یکی از سه سرنوشت:
    /// <list type="number">
    ///   <item>موفق ⇒ <see cref="ConfirmAsync"/> (فقط مهر تأیید در دفتر؛ شمارنده دست‌نخورده)</item>
    ///   <item>شکست <b>قطعی</b> ⇒ <see cref="RefundTechnicalAsync"/> + رخداد <c>LICENSE_QUOTA_REFUND_TECHNICAL</c></item>
    ///   <item>نتیجهٔ <b>نامعلوم</b> ⇒ <see cref="MarkPendingAsync"/> — <b>بدون</b> بازگشت، چون ممکن است عملیات انجام شده باشد</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// ⛔ اگر تراکنشی از قبل باز باشد، این متد استثنا می‌دهد: رزروی که داخل تراکنش
    /// فراخوان بماند، همان قفل طولانی را می‌سازد که می‌خواهیم از آن پرهیز کنیم.
    /// </para>
    /// </summary>
    public async Task<QuotaReservationResult> ReserveAsync(EffectiveEntitlement entitlement, string module,
        string metric, int amount = 1, string? entityType = null, string? entityId = null,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0) return QuotaReservationResult.From(QuotaConsumeResult.Unlimited());

        if (_db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException(
                "رزرو سهمیه نباید داخل تراکنش باز انجام شود؛ تراکنش باید کوتاه و جدا باشد.");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // دفتر این‌جا با Reason='reserve' نوشته می‌شود تا از لحظهٔ رزرو معلوم باشد
            // چه چیزی در جریان است؛ سرنوشت نهایی با ردیف بعدی (confirm/refund-technical/pending) می‌آید.
            var result = await TryConsumeAsync(entitlement, module, metric, amount, entityType, entityId,
                userId: null, recordLedger: false, cancellationToken: cancellationToken);

            if (!result.Allowed)
            {
                await transaction.RollbackAsync(cancellationToken);
                return QuotaReservationResult.From(result);
            }

            // ============ ردیف‌های رزرو با وضعیت و زمان ============
            //
            // یک ردیف برای هر (ماژول، متریک، دوره) — دقیقاً همان چیزی که در دفتر و در
            // شمارنده اثر می‌گذارد. اگر متریک تجمیعی هم سقف داشته باشد، ردیف خودش را
            // می‌گیرد؛ وگرنه تسویهٔ دستی یکی، دیگری را نیمه‌کاره می‌گذاشت.
            //
            // ⚠️ این ردیف‌ها هم داخل همان تراکنش کوتاه نوشته می‌شوند تا «مصرفِ بی‌رزرو»
            // هیچ‌وقت پیش نیاید (شمارنده بالا رفته ولی ردیفی برای پیگیری وجود نداشته باشد).
            var ids = new List<long>();
            var reservedAt = PersianPeriodKey.NowIran();

            foreach (var target in MetricCodes.ConsumeTargets(metric))
            {
                foreach (var limit in entitlement.LimitsFor(module, target))
                {
                    var periodKey = PersianPeriodKey.Now(limit.Period);

                    await LedgerAsync(limit, periodKey, amount, entityType, entityId,
                        null, cancellationToken, "reserve");

                    var row = new LicenseReservation
                    {
                        Module = limit.Module,
                        Metric = limit.Metric,
                        PeriodKey = periodKey,
                        Amount = amount,
                        EntityType = entityType,
                        EntityId = entityId,
                        State = LicenseReservation.States.Reserved,
                        ReservedAt = reservedAt
                    };
                    row.Hmac = _integrity.ForReservation(row.Module, row.Metric, row.PeriodKey,
                        row.Amount, row.ReservedAt);

                    _db.LicenseReservations.Add(row);
                    await _db.SaveChangesAsync(cancellationToken);
                    ids.Add(row.Id);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return new QuotaReservationResult(result, ids);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// بازگشت مصرف به دلیل <b>خطای فنی قطعیِ همان عملیات</b>.
    ///
    /// <para>
    /// تفاوتش با <see cref="TryRefundAsync"/>: آن یکی قاعدهٔ «حذف کاربر مصرف را برنمی‌گرداند»
    /// را اجرا می‌کند و دورهٔ دائمی را بازنمی‌گرداند. اما این‌جا عملیات <b>هرگز انجام
    /// نشده</b> (تماس شبکه قطعاً شکست خورد، نه اینکه کاربر رکورد را پاک کند)؛ پس
    /// واحد رزروشده باید برگردد — <b>حتی برای دورهٔ دائمی</b> — وگرنه مشتری بابت
    /// عملیاتی که انجام نشده جریمه می‌شود. رخداد <c>LICENSE_QUOTA_REFUND_TECHNICAL</c>
    /// برای ممیزی ثبت می‌شود.
    /// </para>
    /// </summary>
    public async Task<bool> RefundTechnicalAsync(EffectiveEntitlement entitlement, string module, string metric,
        int amount = 1, string? entityType = null, string? entityId = null,
        IReadOnlyList<long>? reservationIds = null,
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
                var now = PersianPeriodKey.NowIran();

                var affected = isSqlite
                    ? await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE ""LicenseUsages"" SET ""Used"" = ""Used"" - {amount}, ""UpdatedAt"" = {now}
 WHERE ""Module"" = {limit.Module} AND ""Metric"" = {limit.Metric} AND ""PeriodKey"" = {periodKey}
   AND ""Used"" - {amount} >= 0", cancellationToken)
                    : await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [dbo].[LicenseUsages] SET [Used] = [Used] - {amount}, [UpdatedAt] = {now}
 WHERE [Module] = {limit.Module} AND [Metric] = {limit.Metric} AND [PeriodKey] = {periodKey}
   AND [Used] - {amount} >= 0", cancellationToken);

                if (affected <= 0) continue;

                refunded = true;
                await StampAsync(limit, periodKey, cancellationToken);
                await LedgerAsync(limit, periodKey, -amount, entityType, entityId, null, cancellationToken,
                    "refund-technical");
            }
        }

        var title = MetricCodes.TitleOf(metric);
        await _events.WriteAsync("LICENSE_QUOTA_REFUND_TECHNICAL",
            refunded
                ? $"عملیات «{title}» با خطای فنی قطعی شکست خورد؛ یک واحد از سهمیهٔ رزروشده بازگردانده شد."
                : $"عملیات «{title}» با خطای فنی قطعی شکست خورد؛ ولی واحد رزروشده‌ای برای بازگشت پیدا نشد.",
            severity: refunded ? "Info" : "Warning");

        // وضعیت ردیف‌های رزرو: Reserved/Pending ⇐ Refunded (حتی اگر شمارنده‌ای برای کاهش نبود،
        // رزرو نباید در وضعیت «در جریان» بماند وگرنه تسویهٔ دستی دوباره روی آن اجرا می‌شود).
        await SettleRowsAsync(reservationIds, LicenseReservation.States.Refunded, "system",
            refunded ? null : "شمارنده‌ای برای کاهش پیدا نشد؛ ردیف رزرو بسته شد.", cancellationToken);

        _log.LogWarning("[License] بازگشت فنی سهمیه «{Metric}»: {Result}", metric, refunded ? "انجام شد" : "ردیفی نبود");
        return refunded;
    }

    /// <summary>
    /// علامت‌زدن رزرو به‌عنوان <b>«در انتظار»</b> — برای وقتی نتیجهٔ عملیات نامعلوم است
    /// (مثلاً Timeout پس از ارسال).
    ///
    /// <para>
    /// این‌جا مصرف <b>برنمی‌گردد</b>: اگر درخواست به سامانه رسیده باشد، عملیات انجام شده و
    /// بازگرداندن واحد یعنی «مجانی» شدن آن. در عوض ردیف دفتر با <c>delta = 0</c> و
    /// <c>reason = pending</c> نوشته می‌شود و رخداد <c>LICENSE_QUOTA_PENDING</c> ثبت
    /// می‌شود تا پشتیبانی بتواند دستی بررسی و در صورت لزوم اصلاح کند.
    /// </para>
    /// </summary>
    public async Task MarkPendingAsync(EffectiveEntitlement entitlement, string module, string metric,
        string? entityType = null, string? entityId = null, string? note = null,
        IReadOnlyList<long>? reservationIds = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var target in MetricCodes.ConsumeTargets(metric))
            foreach (var limit in entitlement.LimitsFor(module, target))
                await LedgerAsync(limit, PersianPeriodKey.Now(limit.Period), 0, entityType, entityId, null,
                    cancellationToken, "pending");

        // وضعیت ردیف‌های رزرو: Reserved ⇐ Pending (مصرف **برنمی‌گردد**؛ فقط پیگیری‌پذیر می‌شود).
        await SettleRowsAsync(reservationIds, LicenseReservation.States.Pending, "system", note, cancellationToken);

        var title = MetricCodes.TitleOf(metric);
        await _events.WriteAsync("LICENSE_QUOTA_PENDING",
            $"نتیجهٔ عملیات «{title}» نامعلوم است (احتمالاً پس از ارسال قطع شد)؛ مصرف رزروشده "
            + "بازگردانده نشد و برای بررسی دستی علامت‌گذاری شد." + (string.IsNullOrWhiteSpace(note) ? "" : $" جزئیات: {note}"),
            severity: "Warning");

        _log.LogWarning("[License] نتیجهٔ نامعلوم برای «{Metric}» — رزرو در وضعیت Pending ماند.", metric);
    }

    /// <summary>ثبت «تأیید» رزرو — فقط برای زنجیرهٔ رویدادها؛ شمارنده تغییر نمی‌کند.</summary>
    public async Task ConfirmAsync(EffectiveEntitlement entitlement, string module, string metric,
        string? entityType = null, string? entityId = null, IReadOnlyList<long>? reservationIds = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var target in MetricCodes.ConsumeTargets(metric))
            foreach (var limit in entitlement.LimitsFor(module, target))
                await LedgerAsync(limit, PersianPeriodKey.Now(limit.Period), 0, entityType, entityId, null,
                    cancellationToken, "confirm");

        // وضعیت ردیف‌های رزرو: Reserved ⇐ Confirmed (مصرف دست‌نخورده می‌ماند).
        await SettleRowsAsync(reservationIds, LicenseReservation.States.Confirmed, "system", null, cancellationToken);
    }

    // ==================== تسویهٔ رزرو ====================

    /// <summary>تغییر وضعیت ردیف‌های رزرو — فقط از حالت‌های باز.</summary>
    private async Task SettleRowsAsync(IReadOnlyList<long>? reservationIds, string state, string settledBy,
        string? note, CancellationToken cancellationToken)
    {
        if (reservationIds is null || reservationIds.Count == 0) return;

        var ids = reservationIds.Distinct().ToList();
        var rows = await _db.LicenseReservations
            .Where(r => ids.Contains(r.Id)
                        && (r.State == LicenseReservation.States.Reserved
                            || r.State == LicenseReservation.States.Pending))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0) return;

        var now = PersianPeriodKey.NowIran();
        foreach (var row in rows)
        {
            row.State = state;
            row.SettledAt = now;
            row.SettledBy = settledBy;
            if (!string.IsNullOrWhiteSpace(note))
                row.Note = note!.Length > 600 ? note[..600] : note;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// <b>جاروی رزروهای کهنه</b> — در راه‌اندازی برنامه و به‌صورت دوره‌ای اجرا می‌شود.
    ///
    /// <para>
    /// <b>مسئله‌ای که حل می‌کند:</b> اگر برنامه <b>بین رزرو و تسویه</b> خاموش/ری‌استارت
    /// شود (برق، کرش، ری‌استارت سرور در وسط ارسال به مودیان)، رزرو در وضعیت
    /// <c>Reserved</c> می‌ماند. تا وقتی این حالت پاک نشود، هیچ‌کس نمی‌داند آن واحد
    /// «مصرف شده» است یا «برگشته» — و پشتیبانی هم راهی برای بستنش ندارد.
    /// </para>
    ///
    /// <para>
    /// <b>قاعدهٔ سند:</b> نتیجهٔ نامعلوم ⇒ <b>بازگشت نکن</b>. پس رزرو کهنه به
    /// <c>Pending</c> می‌رود (نه <c>Refunded</c>): مصرف سرِ جایش می‌ماند و برای
    /// «تسویهٔ دستی مدیر» علامت می‌خورد. اینجا هیچ سهمیه‌ای بخشیده نمی‌شود.
    /// </para>
    /// </summary>
    /// <param name="maxAge">حداکثر عمر مجاز یک رزرو باز. از <c>Licensing:ReservationTimeoutMinutes</c> می‌آید.</param>
    /// <returns>تعداد رزروهایی که به Pending منتقل شدند.</returns>
    public async Task<int> SweepStaleReservationsAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var cutoff = PersianPeriodKey.NowIran() - maxAge;

        var stale = await _db.LicenseReservations
            .Where(r => r.State == LicenseReservation.States.Reserved && r.ReservedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0) return 0;

        var now = PersianPeriodKey.NowIran();
        foreach (var row in stale)
        {
            row.State = LicenseReservation.States.Pending;
            row.Note = $"رزرو بیش از {(int)maxAge.TotalMinutes} دقیقه باز ماند (احتمالاً قطع ناگهانی برنامه)؛ " +
                       "مصرف بازگردانده نشد و نیاز به تسویهٔ دستی دارد.";
            row.SettledAt = null;      // تسویهٔ نهایی هنوز انجام نشده
            row.SettledBy = "system";
        }
        await _db.SaveChangesAsync(cancellationToken);

        await _events.WriteAsync("LICENSE_QUOTA_RESERVATION_STALE",
            $"{stale.Count} رزرو سهمیه بیش از {(int)maxAge.TotalMinutes} دقیقه تسویه نشده بود و به وضعیت «در انتظار» " +
            "منتقل شد. مصرف بازگردانده نشد؛ لطفاً از صفحهٔ رزروها تسویه کنید.",
            severity: "Warning",
            dataJson: System.Text.Json.JsonSerializer.Serialize(stale.Select(x => new { x.Id, x.Metric, x.EntityType, x.EntityId })),
            cancellationToken: cancellationToken);

        _log.LogWarning("[License] {Count} رزرو کهنه به وضعیت Pending منتقل شد.", stale.Count);
        return stale.Count;
    }

    /// <summary>
    /// <b>تسویهٔ دستی یک رزرو «در انتظار»</b> (پشتیبانی/مدیر).
    ///
    /// <para>
    /// دو مسیر:
    /// <list type="bullet">
    ///   <item><b>تأیید (Confirm):</b> بررسی شد که ارسال انجام شده ⇒ مصرف همان‌جا می‌ماند.</item>
    ///   <item><b>بازگشت (Refund):</b> بررسی شد که ارسال انجام <b>نشده</b> ⇒ یک واحد از
    ///   شمارندهٔ همان دوره کسر می‌شود (با شرط «منفی نشود») و ردیف دفتر با
    ///   <c>-1</c> و علت <c>manual-refund</c> نوشته می‌شود.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// هر دو حالت رخداد <c>LICENSE_QUOTA_MANUAL_SETTLE</c> با <b>نام کاربری</b> ثبت
    /// می‌کنند؛ چون این تصمیم انسانی است و باید در ممیزی رد داشته باشد.
    /// </para>
    /// </summary>
    /// <summary>
    /// همان تسویه، اما پاسخ <b>ساختاریافته</b> — تا API بتواند دلیل رد شدن را دقیق بگوید.
    /// </summary>
    public async Task<SettleReservationResult> SettleReservationDetailedAsync(long reservationId, bool confirm,
        string username, string? note, RefundPolicy policy, CancellationToken cancellationToken = default)
    {
        var row = await _db.LicenseReservations.FirstOrDefaultAsync(r => r.Id == reservationId, cancellationToken);
        if (row is null)
            return SettleReservationResult.Fail(SettleFailure.NotFound, "رزرو پیدا نشد.");

        // فقط رزرو باز (Reserved) یا «در انتظار» (Pending) قابل تسویه است.
        if (row.State != LicenseReservation.States.Reserved && row.State != LicenseReservation.States.Pending)
            return SettleReservationResult.Fail(SettleFailure.AlreadySettled,
                $"این رزرو قبلاً تسویه شده است (وضعیت: {row.State}).");

        var now = PersianPeriodKey.NowIran();

        // ==================== قیدهای بازگشت دستی ====================
        //
        // سه قید، هر سه لازم (بازبینی سوم):
        //   ۱) یادداشت اجباری — تصمیم انسانی بدون دلیل، ممیزی بی‌ارزش است.
        //   ۲) سن رزرو — جلوی «رزرو کن، فوراً بازگردان» (ارسال مجانی) را می‌گیرد.
        //   ۳) سقف تعداد در دوره — جلوی آزادسازی تدریجی سهمیه را می‌گیرد.
        if (!confirm)
        {
            if (string.IsNullOrWhiteSpace(note))
                return SettleReservationResult.Fail(SettleFailure.NoteRequired,
                    "برای بازگشت دستی، نوشتن یادداشت دلیل الزامی است.");

            if (!policy.IsOldEnough(row.ReservedAt, now))
            {
                var ageHours = (now - row.ReservedAt).TotalHours;
                return SettleReservationResult.Fail(SettleFailure.TooRecent,
                    $"این رزرو فقط {ageHours:F1} ساعت قدمت دارد؛ بازگشت دستی فقط برای رزروهای قدیمی‌تر از " +
                    $"{policy.MinAgeHours} ساعت مجاز است.");
            }

            var alreadyRefunded = await CountManualRefundsAsync(row.Module, row.Metric, row.PeriodKey,
                cancellationToken);

            var periodLimit = await ReadPeriodLimitAsync(row.Module, row.Metric, row.PeriodKey, cancellationToken);
            var maxRefunds = policy.MaxRefundsFor(periodLimit);

            if (alreadyRefunded >= maxRefunds)
                return SettleReservationResult.Fail(SettleFailure.RefundLimitReached,
                    $"سقف بازگشت دستی این دوره پر شده است ({alreadyRefunded} از {maxRefunds}). " +
                    policy.Describe(periodLimit));
        }

        var refunded = false;

        if (!confirm)
        {
            var isSqlite = _db.Database.IsSqlite();
            var affected = isSqlite
                ? await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE ""LicenseUsages"" SET ""Used"" = ""Used"" - {row.Amount}, ""UpdatedAt"" = {now}
 WHERE ""Module"" = {row.Module} AND ""Metric"" = {row.Metric} AND ""PeriodKey"" = {row.PeriodKey}
   AND ""Used"" - {row.Amount} >= 0", cancellationToken)
                : await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [dbo].[LicenseUsages] SET [Used] = [Used] - {row.Amount}, [UpdatedAt] = {now}
 WHERE [Module] = {row.Module} AND [Metric] = {row.Metric} AND [PeriodKey] = {row.PeriodKey}
   AND [Used] - {row.Amount} >= 0", cancellationToken);

            refunded = affected > 0;

            if (refunded)
            {
                // دفتر: همان ردیف دوره، با علت صریح «بازگشت دستی» تا از مصرف عادی جدا باشد.
                var entry = new LicenseUsageLedger
                {
                    Module = row.Module,
                    Metric = row.Metric,
                    PeriodKey = row.PeriodKey,
                    Delta = -row.Amount,
                    EntityType = row.EntityType,
                    EntityId = row.EntityId,
                    Reason = "manual-refund",
                    At = DateTime.Now
                };
                entry.Hmac = _integrity.ForLedger(entry.Module, entry.Metric, entry.PeriodKey, entry.Delta,
                    entry.EntityType, entry.EntityId, entry.At);
                _db.LicenseUsageLedger.Add(entry);
            }
        }

        row.State = confirm ? LicenseReservation.States.Confirmed : LicenseReservation.States.Refunded;
        row.SettledAt = now;
        row.SettledBy = string.IsNullOrWhiteSpace(username) ? "unknown" : username;
        row.Note = note is { Length: > 600 } ? note[..600] : note;
        await _db.SaveChangesAsync(cancellationToken);

        var title = MetricCodes.TitleOf(row.Metric);
        await _events.WriteAsync("LICENSE_QUOTA_MANUAL_SETTLE",
            confirm
                ? $"رزرو «{title}» (شناسه {row.Id}) توسط «{row.SettledBy}» دستی تأیید شد؛ مصرف {row.Amount} واحد باقی می‌ماند."
                : refunded
                    ? $"رزرو «{title}» (شناسه {row.Id}) توسط «{row.SettledBy}» دستی بازگردانده شد؛ {row.Amount} واحد به سهمیهٔ دورهٔ «{row.PeriodKey}» برگشت."
                    : $"رزرو «{title}» (شناسه {row.Id}) توسط «{row.SettledBy}» دستی بازگردانده شد، ولی ردیف شمارنده‌ای برای کاهش نبود (مقدار مصرف صفر بود).",
            severity: "Warning",
            dataJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                reservationId = row.Id,
                module = row.Module,
                metric = row.Metric,
                periodKey = row.PeriodKey,
                action = confirm ? "confirm" : "refund",
                refunded,
                settledBy = row.SettledBy,
                note = row.Note
            }),
            userId: null,
            cancellationToken: cancellationToken);

        _log.LogWarning("[License] تسویهٔ دستی رزرو {Id}: {Action} توسط {User}",
            row.Id, confirm ? "تأیید" : "بازگشت", row.SettledBy);

        return SettleReservationResult.Ok(row.Id, confirm, refunded);
    }

    /// <summary>
    /// نسخهٔ ساده (سازگاری): با سیاست پیش‌فرض، فقط true/false.
    /// ⚠️ برای مسیر API از <see cref="SettleReservationDetailedAsync"/> استفاده کنید تا دلیل رد مشخص باشد.
    /// </summary>
    public async Task<bool> SettleReservationAsync(long reservationId, bool confirm, string username,
        string? note = null, RefundPolicy? policy = null, CancellationToken cancellationToken = default)
    {
        var result = await SettleReservationDetailedAsync(reservationId, confirm, username, note,
            policy ?? new RefundPolicy(RefundPolicy.DefaultPercentOfLimit, RefundPolicy.DefaultMaxCount,
                RefundPolicy.DefaultMinAgeHours), cancellationToken);
        return result.Success;
    }

    /// <summary>
    /// تعداد بازگشت‌های دستی ثبت‌شده در همین (ماژول، متریک، دوره).
    ///
    /// <para>
    /// ملاک، ردیف‌های دفتر با <c>Reason = manual-refund</c> است — نه ردیف‌های رزرو؛ چون
    /// یک رزرو ممکن است چند واحد باشد و شمارش باید «تعداد واحدهای بازگردانده‌شده»
    /// باشد، نه «تعداد کلیک».
    /// </para>
    /// </summary>
    public async Task<int> CountManualRefundsAsync(string module, string metric, string periodKey,
        CancellationToken cancellationToken = default)
    {
        var sum = await _db.LicenseUsageLedger.AsNoTracking()
            .Where(l => l.Module == module && l.Metric == metric && l.PeriodKey == periodKey
                        && l.Reason == "manual-refund")
            .Select(l => (int?)l.Delta)
            .ToListAsync(cancellationToken);

        // Delta منفی است؛ قدر مطلق جمع را برمی‌گردانیم.
        return Math.Abs(sum.Sum(x => x ?? 0));
    }

    /// <summary>سقف ثبت‌شدهٔ همان دوره (برای محاسبهٔ «۲٪ سقف»).</summary>
    private async Task<long> ReadPeriodLimitAsync(string module, string metric, string periodKey,
        CancellationToken cancellationToken)
        => await _db.LicenseUsages.AsNoTracking()
               .Where(u => u.Module == module && u.Metric == metric && u.PeriodKey == periodKey)
               .Select(u => (long?)u.Limit)
               .FirstOrDefaultAsync(cancellationToken) ?? 0;

    /// <summary>شمارش‌های وضعیت برای نمایش در <c>/api/license/status</c>.</summary>
    public async Task<ReservationSummary> GetReservationSummaryAsync(RefundPolicy policy,
        CancellationToken cancellationToken = default)
    {
        var counts = await _db.LicenseReservations.AsNoTracking()
            .GroupBy(r => r.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Of(string state) => counts.FirstOrDefault(c => c.State == state)?.Count ?? 0;

        var manualRefunds = Math.Abs(await _db.LicenseUsageLedger.AsNoTracking()
            .Where(l => l.Reason == "manual-refund")
            .Select(l => (int?)l.Delta)
            .SumAsync(cancellationToken) ?? 0);

        return new ReservationSummary(
            Pending: Of(LicenseReservation.States.Pending),
            Reserved: Of(LicenseReservation.States.Reserved),
            Confirmed: Of(LicenseReservation.States.Confirmed),
            Refunded: Of(LicenseReservation.States.Refunded),
            ManualRefunds: manualRefunds,
            ManualRefundPolicy: new
            {
                percentOfLimit = policy.PercentOfLimit,
                maxCountPerPeriod = policy.MaxCount,
                minReservationAgeHours = policy.MinAgeHours
            });
    }

    /// <summary>فهرست رزروها برای صفحهٔ مدیریت (مرحله ۵) و API تسویه.</summary>
    public async Task<List<LicenseReservation>> ListReservationsAsync(string? state, int take,
        CancellationToken cancellationToken = default)
    {
        var query = _db.LicenseReservations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state))
            query = query.Where(r => r.State == state);

        return await query
            .OrderByDescending(r => r.ReservedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);
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
        => await GetEffectiveUsageAsync(entitlement, limit,
            liveCount is null ? null : (_, ct) => liveCount(ct), cancellationToken);

    /// <summary>
    /// مصرف مؤثر یک متریک — نسخهٔ <b>بازه‌دار</b> (بازبینی مرحله ۲).
    ///
    /// <para>
    /// برای متریک‌های «تعداد فعال» دو چیز باید با هم دیده شود:
    /// </para>
    /// <list type="number">
    ///   <item><b>دورهٔ ماهانه/سالانه:</b> فقط رکوردهای <b>همین دوره</b> شمرده می‌شوند.
    ///   فرمول: <c>max(شمارنده، رکوردهای این دوره)</c>. مبنا این‌جا لازم نیست؛ چون
    ///   دادهٔ قدیمی تاریخ گذشته دارد و بیرون از پنجره می‌افتد.</item>
    ///   <item><b>دورهٔ دائمی:</b> کل تاریخچه با کسر مبنا.
    ///   فرمول: <c>max(شمارنده، max(0, کل رکوردها − مبنا))</c>.</item>
    /// </list>
    ///
    /// <para>
    /// <b>دامنهٔ اعمال (بازبینی مرحله ۲):</b> این فرمول برای <b>هر متریکی که probe دارد</b>
    /// اعمال می‌شود، نه فقط <c>ActiveCount</c>. ملاک، وجود شمارش واقعی است
    /// (<see cref="ActiveCountProbes.HasProbe"/>).
    /// </para>
    ///
    /// <para>
    /// ⚠️ پیش از این اصلاح، برای سقف ماهانه هم «کل تاریخچه − مبنا» حساب می‌شد؛ نتیجه
    /// اینکه سقف ماهانهٔ تازه، از روز اول پر به‌نظر می‌رسید.
    /// </para>
    /// </summary>
    public async Task<long> GetEffectiveUsageAsync(EffectiveEntitlement entitlement, MetricLimit limit,
        Func<QuotaPeriod, CancellationToken, Task<long>>? liveInPeriod,
        CancellationToken cancellationToken = default)
    {
        var periodKey = PersianPeriodKey.Now(limit.Period);
        var counter = await ReadUsedAsync(limit, periodKey, cancellationToken) ?? 0;

        // آیا برای این متریک «شمارش واقعی» داریم؟ ملاک <b>وجود probe</b> است، نه نوع متریک
        // (بازبینی مرحله ۲: قاعده «مبنا برای همهٔ متریک‌های دارای probe»).
        if (liveInPeriod is null || !ActiveCountProbes.HasProbe(limit.Metric)) return counter;

        var live = await liveInPeriod(limit.Period, cancellationToken);

        // ---------- مبنا ۱: مبناى دائمی (رفتار قبلی) ----------
        // دورهٔ دائمی: مبنا کسر می‌شود. دورهٔ ماهانه/سالانه: پنجره خودش دادهٔ قدیمی را حذف می‌کند.
        var baseline = limit.Period == QuotaPeriod.Lifetime
            ? await ReadBaselineAsync(limit.Module, limit.Metric, cancellationToken)
            : 0;

        // ---------- مبنا ۲: بخشودگی گذشته (بازبینی سوم) ----------
        // اگر آیتم لایسنس گزینهٔ Grandfather را روشن کرده باشد، رکوردهای موجود در
        // «همین دوره» در لحظهٔ فعال‌سازی کسر می‌شوند. برای دوره‌های بعدی ردیفی با آن
        // کلید دوره وجود ندارد ⇒ Naturally صفر ⇒ شمارش از صفر.
        //
        // ⚠️ سقف هرگز کوچک‌تر از «مصرف همین دوره» گزارش نمی‌شود؛ Math.Max در
        // ComputeEffective با شمارندهٔ واقعی این را تضمین می‌کند.
        var grandfather = limit.Grandfather
            ? await ReadGrandfatherBaselineAsync(limit.Module, limit.Metric, periodKey, cancellationToken)
            : 0;

        return ComputeEffective(counter, live, Math.Max(baseline, grandfather), periodKey);
    }

    /// <summary>
    /// بررسی <b>فقط-خواندنی</b> باقی‌ماندهٔ سهمیه — برای عملیاتی که «اول باید انجام شود،
    /// بعد شمرده شود» (مثل ارسال به مودیان که یک تماس شبکه‌ای بیرون دیتابیس است).
    ///
    /// <para>
    /// این متد هیچ ردیفی را قفل یا تغییر نمی‌دهد؛ پس امن است که قبل از یک تماس کند
    /// صدا زده شود. تضمین قطعی نمی‌دهد (بین بررسی و مصرف ممکن است دیگری جلو بزند)؛
    /// اگر چنین مسابقه‌ای رخ دهد، در <see cref="TryConsumeAsync"/> یک رد واقعی می‌گیریم
    /// و آن را لاگ می‌کنیم — عملیاتی که انجام شده دیگر برگشت‌پذیر نیست.
    /// </para>
    /// </summary>
    public async Task<QuotaConsumeResult> PeekAsync(EffectiveEntitlement entitlement, string module,
        string metric, CancellationToken cancellationToken = default)
    {
        foreach (var target in MetricCodes.ConsumeTargets(metric))
        {
            foreach (var limit in entitlement.LimitsFor(module, target))
            {
                var periodKey = PersianPeriodKey.Now(limit.Period);
                var used = await ReadUsedAsync(limit, periodKey, cancellationToken) ?? 0;

                if (used < limit.Limit) continue;

                return QuotaConsumeResult.Denied(limit.Metric, used, limit.Limit, periodKey,
                    $"سقف «{MetricCodes.TitleOf(limit.Metric)}» ({limit.Limit:N0} {MetricCodes.UnitOf(limit.Metric)}) " +
                    $"در دورهٔ «{PersianPeriodKey.Describe(limit.Period, periodKey)}» پر شده است. " +
                    "برای افزایش سقف با پشتیبانی تماس بگیرید.");
            }
        }

        return QuotaConsumeResult.Unlimited();
    }

    /// <summary>
    /// فرمول مصرف مؤثر — یک‌جا و قابل تست:
    /// <code>effective = max(شمارنده, max(0, تعداد واقعی − مبنا))</code>
    /// </summary>
    /// <param name="counter">شمارندهٔ ثبت‌شده در <c>LicenseUsages</c></param>
    /// <param name="live">تعداد واقعی رکوردهای موجود (شامل حذف‌نرم‌ها)</param>
    /// <param name="baseline">تعداد رکوردهای پیش از فعال‌سازی (از جدول مبنا)</param>
    /// <param name="periodKey">
    /// کلید دوره — فقط برای مستندسازی/اشکال‌زدایی. در محاسبه نقشی ندارد؛ منطق
    /// «هر دوره جدا» از این می‌آید که مبنا با کلید همان دوره خوانده می‌شود.
    /// </param>
    public static long ComputeEffective(long counter, long live, long baseline, string? periodKey = null)
        => Math.Max(counter, Math.Max(0, live - baseline));

    /// <summary>
    /// مبنای «بخشودگی گذشته» برای یک (متریک، دوره) — ۰ اگر ثبت نشده باشد.
    ///
    /// <para>
    /// ثبت‌نشده یعنی «همهٔ رکوردهای این دوره شمرده شوند» — یعنی رفتار پیش‌فرضِ امن.
    /// </para>
    /// </summary>
    public async Task<long> ReadGrandfatherBaselineAsync(string module, string metric, string periodKey,
        CancellationToken cancellationToken = default)
        => await _db.LicenseGrandfatherBaselines.AsNoTracking()
               .Where(b => b.Module == module && b.Metric == metric && b.PeriodKey == periodKey)
               .Select(b => (long?)b.BaselineCount)
               .FirstOrDefaultAsync(cancellationToken) ?? 0;

    /// <summary>
    /// ثبت مبنای بخشودگی برای یک دوره — idempotent (دوباره‌نویسی نمی‌کند).
    ///
    /// <para>
    /// ⚠️ <b>چرا idempotent؟</b> اگر هر بار فعال‌سازی/تمدید مبنا را بازنویسی کند، مشتری
    /// می‌تواند با «تازه‌سازی لایسنس» گذشتهٔ دورهٔ جاری را دوباره بخشوده کند و سقف
    /// ماهانه را بی‌نهایت بار تمدید کند.
    /// </para>
    /// </summary>
    /// <returns>true اگر همین حالا ثبت شد؛ false اگر از قبل بود.</returns>
    public async Task<bool> CaptureGrandfatherBaselineAsync(string module, string metric, string periodKey,
        long liveCount, string? serial, CancellationToken cancellationToken = default)
    {
        var exists = await _db.LicenseGrandfatherBaselines
            .AnyAsync(b => b.Module == module && b.Metric == metric && b.PeriodKey == periodKey, cancellationToken);
        if (exists) return false;

        _db.LicenseGrandfatherBaselines.Add(new LicenseGrandfatherBaseline
        {
            Module = module,
            Metric = metric,
            PeriodKey = periodKey,
            BaselineCount = Math.Max(0, liveCount),
            CapturedAt = PersianPeriodKey.NowIran(),
            Serial = serial
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // مسابقهٔ هم‌زمان (دو فعال‌سازی موازی) — ردیف دیگر زودتر ثبت کرد؛ همان قبول است.
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>مبنای ثبت‌شدهٔ یک متریک (۰ اگر ثبت نشده باشد).</summary>
    public async Task<long> ReadBaselineAsync(string module, string metric, CancellationToken cancellationToken = default)
        => await _db.LicenseUsageBaselines.AsNoTracking()
               .Where(b => b.Module == module && b.Metric == metric)
               .Select(b => (long?)b.BaselineCount)
               .FirstOrDefaultAsync(cancellationToken) ?? 0;

    /// <summary>
    /// ثبت مبنا برای یک متریک — یک‌بار و idempotent.
    ///
    /// <para>
    /// اگر قبلاً مبنا ثبت شده باشد، هیچ کاری نمی‌کند (نصب دوبارهٔ لایسنس، مبنا را
    /// جابه‌جا نمی‌کند؛ وگرنه با تمدید سالانه می‌شد سقف را دور زد).
    /// </para>
    /// </summary>
    /// <returns>true اگر همین حالا ثبت شد؛ false اگر از قبل بود.</returns>
    public async Task<bool> CaptureBaselineAsync(string module, string metric, long liveCount,
        string reason = "activation", string? serial = null, CancellationToken cancellationToken = default)
    {
        var exists = await _db.LicenseUsageBaselines
            .AnyAsync(b => b.Module == module && b.Metric == metric, cancellationToken);
        if (exists) return false;

        _db.LicenseUsageBaselines.Add(new LicenseUsageBaseline
        {
            Module = module,
            Metric = metric,
            BaselineCount = Math.Max(0, liveCount),
            CapturedAt = PersianPeriodKey.NowIran(),
            Reason = reason,
            Serial = serial
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            _log.LogInformation("[License] مبنای مصرف «{Metric}» ثبت شد: {Count} رکورد پیش از فعال‌سازی از شمارش کسر می‌شود",
                metric, liveCount);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // دو درخواست هم‌زمان ⇒ یکی به کلید یکتا می‌خورد. بی‌خطر است.
            _log.LogDebug(ex, "[License] ثبت هم‌زمان مبنای مصرف (بی‌خطر).");
            return false;
        }
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
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // دو درخواست هم‌زمان خواستند ردیف را بسازند؛ دومی به کلید یکتا می‌خورد. بی‌خطر است.
            _log.LogDebug(ex, "[License] ساخت هم‌زمان ردیف مصرف (بی‌خطر).");
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            // SQL Server خطای کلید تکراری را به‌صورت SqlException می‌دهد، نه همیشه DbUpdateException.
            _log.LogDebug(ex, "[License] ساخت هم‌زمان ردیف مصرف (کلید تکراری، بی‌خطر).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "[License] ساخت ردیف مصرف ناموفق بود: {Metric}", limit.Metric);
        }
    }

    /// <summary>
    /// آیا استثنا «کلید یکتا تکراری» است؟
    ///
    /// <para>
    /// وقتی ۵۰ درخواست هم‌زمان برسند و <b>هیچ ردیفی وجود نداشته باشد</b>، همه سعی می‌کنند
    /// ردیف را بسازند. <c>INSERT … WHERE NOT EXISTS</c> در SQL Server اتمیک نیست (سطح
    /// READ COMMITTED)، پس یکی برنده و بقیه به کلید یکتا می‌خورند:
    /// SQL Server ⇒ خطای ۲۶۲۷ یا ۲۶۰۱، SQLite ⇒ کد ۱۹ (CONSTRAINT).
    /// این خطا «بی‌خطر» است و نباید مصرف‌کننده را رد کند؛ فقط یعنی ردیف همین حالا ساخته شد.
    /// </para>
    /// </summary>
    private static bool IsDuplicateKey(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Microsoft.Data.Sqlite.SqliteException sqlite
                && sqlite.SqliteErrorCode == 19)   // SQLITE_CONSTRAINT
                return true;

            if (current is Microsoft.Data.SqlClient.SqlException sql)
            {
                if (sql.Number is 2627 or 2601) return true;   // unique/PK violation
                foreach (Microsoft.Data.SqlClient.SqlError error in sql.Errors)
                    if (error.Number is 2627 or 2601) return true;
            }

            if (current is DbUpdateException) continue;
        }

        return false;
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
