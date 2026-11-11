using System.Text;
using System.Text.Json;

namespace Inventory.Api.Licensing.Services;

/// <summary>
/// دفتر محلی ورودهای «حالت تخریب‌شده» — بند ۲ صورت‌جلسه.
///
/// <para>
/// وقتی دیتابیس در دسترس نیست (خطای گذرا) و ورود جدید پذیرفته می‌شود، رخداد در همین
/// لحظه جایی برای نوشتن ندارد. پس در یک فایل JSONL کنار فایل لایسنس نوشته می‌شود و بعد
/// از برگشت دیتابیس، به <c>LicenseEvent</c> منتقل (و فایل خالی) می‌شود.
/// </para>
///
/// <para>
/// مسیر: <c>&lt;پوشهٔ لایسنس&gt;/seat-degraded.jsonl</c> — بیرون پوشهٔ انتشار، تا آپدیت
/// فایل‌ها آن را پاک نکند.
/// </para>
/// </summary>
public sealed class SeatDegradedJournal
{
    private readonly LicensePaths _paths;
    private readonly ILogger<SeatDegradedJournal> _log;
    private readonly object _gate = new();

    public SeatDegradedJournal(LicensePaths paths, ILogger<SeatDegradedJournal> log)
    {
        _paths = paths;
        _log = log;
    }

    /// <summary>مسیر فایل دفتر.</summary>
    public string FilePath => Path.Combine(_paths.Root, "seat-degraded.jsonl");

    /// <summary>تعداد ردیف‌های منتقل‌نشده.</summary>
    public int PendingCount
    {
        get
        {
            try
            {
                lock (_gate)
                    return File.Exists(FilePath)
                        ? File.ReadLines(FilePath).Count(l => !string.IsNullOrWhiteSpace(l))
                        : 0;
            }
            catch { return 0; }
        }
    }

    /// <summary>ثبت یک ورود در حالت تخریب‌شده (همان لحظه، مستقل از دیتابیس).</summary>
    public void Append(string kind, int userId, string? username, string? note)
    {
        try
        {
            var line = JsonSerializer.Serialize(new
            {
                at = Inventory.Api.Licensing.ClockAnchor.Now().ToString("O"),
                kind,
                userId,
                username,
                note
            });

            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            // ثبت محلی هم نشد: هیچ کاری نمی‌شود کرد جز لاگ. ورود مسدود نمی‌شود چون
            // قاعدهٔ «نصب نمی‌خوابد» مهم‌تر است.
            _log.LogWarning(ex, "[License] ثبت محلی ورود تخریب‌شده ممکن نشد.");
        }
    }

    /// <summary>
    /// انتقال ردیف‌های دفتر به <c>LicenseEvent</c> پس از برگشت دیتابیس.
    /// در صورت موفقیت، فایل خالی می‌شود؛ در صورت خطا، ردیف‌ها می‌مانند و بعداً دوباره تلاش می‌شود.
    /// </summary>
    public async Task<int> FlushAsync(Data.AppDbContext db, CancellationToken cancellationToken = default)
    {
        List<string> lines;
        lock (_gate)
        {
            if (!File.Exists(FilePath)) return 0;
            lines = File.ReadLines(FilePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        }

        if (lines.Count == 0) return 0;

        foreach (var line in lines)
        {
            int? userId = null;
            string? username = null, kind = "SEAT_DEGRADED_LOGIN", note = null;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("kind", out var k)) kind = k.GetString() ?? kind;
                if (root.TryGetProperty("userId", out var u) && u.TryGetInt32(out var uid)) userId = uid;
                if (root.TryGetProperty("username", out var n)) username = n.GetString();
                if (root.TryGetProperty("note", out var t)) note = t.GetString();
            }
            catch { /* ردیف خراب: با همان متن خام ثبت می‌شود */ }

            db.LicenseEvents.Add(new Data.LicenseEvent
            {
                At = Inventory.Api.Licensing.ClockAnchor.Now(),
                Kind = kind,
                Severity = "Warning",
                Message = "ورود در حالت تخریب‌شده (دیتابیس در دسترس نبود) — از دفتر محلی منتقل شد.",
                UserId = userId,
                DataJson = JsonSerializer.Serialize(new { username, note, raw = line })
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        lock (_gate)
        {
            try { File.WriteAllText(FilePath, string.Empty, Encoding.UTF8); } catch { }
        }

        _log.LogInformation("[License] {Count} ورود تخریب‌شده به LicenseEvent منتقل شد.", lines.Count);
        return lines.Count;
    }
}
