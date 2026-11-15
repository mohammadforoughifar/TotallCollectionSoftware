using System.Security.Cryptography;
using System.Text;
using LicenseSigner.Data;
using Microsoft.EntityFrameworkCore;

namespace LicenseSigner.Services;

/// <summary>نتیجهٔ بازبینی زنجیره.</summary>
public sealed record AuditVerification(bool Ok, int RowCount, long? BrokenAtId, string Message);

/// <summary>
/// ممیزی فقط‌افزودی با زنجیرهٔ هش:
/// <c>Hash = SHA256(PrevHash | At | UserName | Action | Entity | EntityId | DataJson)</c>
/// و <c>PrevHash</c> هش ردیف قبلی است. تغییر/حذف ردیف میانی ⇒ زنجیرهٔ شکسته.
/// </summary>
public sealed class AuditChainService(SupportDbContext db, ILogger<AuditChainService> log)
{
    public const string Genesis = "GENESIS";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<long> AppendAsync(string userName, string action, string entity, string? entityId = null,
        string? dataJson = null, int? userId = null, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var last = await db.AuditLogs.AsNoTracking().OrderByDescending(a => a.Id).FirstOrDefaultAsync(ct);
            var entry = new AuditLog
            {
                At = DateTime.UtcNow,
                UserId = userId,
                UserName = string.IsNullOrWhiteSpace(userName) ? "system" : Cap(userName, 120),
                Action = Cap(action, 40),
                Entity = Cap(entity, 60),
                EntityId = entityId,
                DataJson = dataJson,
                PrevHash = last?.Hash ?? Genesis
            };
            entry.Hash = ComputeHash(entry);

            db.AuditLogs.Add(entry);
            await db.SaveChangesAsync(ct);
            return entry.Id;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<AuditVerification> VerifyAsync(CancellationToken ct = default)
    {
        var rows = await db.AuditLogs.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct);
        var expectedPrev = Genesis;

        foreach (var row in rows)
        {
            if (!string.Equals(row.PrevHash, expectedPrev, StringComparison.Ordinal))
            {
                log.LogWarning("زنجیرهٔ ممیزی در ردیف {RowId} پاره شده است.", row.Id);
                return new AuditVerification(false, rows.Count, row.Id,
                    $"ردیف #{row.Id}: زنجیره پاره شده است (PrevHash با هش ردیف قبلی یکی نیست).");
            }

            var recomputed = ComputeHash(row);
            if (!string.Equals(recomputed, row.Hash, StringComparison.Ordinal))
                return new AuditVerification(false, rows.Count, row.Id,
                    $"ردیف #{row.Id}: محتوای ردیف تغییر کرده است (هش بازمحاسبه‌شده نمی‌خواند).");

            expectedPrev = row.Hash;
        }

        log.LogDebug("زنجیرهٔ ممیزی بررسی شد: {Rows} ردیف سالم.", rows.Count);
        return new AuditVerification(true, rows.Count, null, "زنجیرهٔ ممیزی سالم است.");
    }

    /// <summary>
    /// محاسبهٔ هش یک ردیف — <c>public static</c> تا تست بتواند دست‌کاری بسازد.
    /// قالب تاریخ عمداً بی‌منطقه است: SQLite نوع <c>DateTimeKind</c> را نگه نمی‌دارد و
    /// با «O» هشِ خوانده‌شده با هشِ نوشته‌شده یکی نمی‌شد (false negative).
    /// </summary>
    public static string ComputeHash(AuditLog row)
    {
        var payload = string.Join('|',
            row.PrevHash,
            row.At.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture),
            row.UserName,
            row.Action,
            row.Entity,
            row.EntityId ?? "",
            row.DataJson ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string Cap(string value, int max)
        => string.IsNullOrEmpty(value) ? "" : (value.Length <= max ? value : value[..max]);
}
