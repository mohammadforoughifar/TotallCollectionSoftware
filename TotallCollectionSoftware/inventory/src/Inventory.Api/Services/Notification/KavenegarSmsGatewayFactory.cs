using Inventory.Api.Data;
using Inventory.Api.Services.ItAssets;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services;

public interface IKavenegarSmsGatewayFactory
{
    /// <summary>A snapshot for one staging/dispatch operation, or null if the account could not be read.</summary>
    Task<IKavenegarSmsGateway?> CreateAsync(CancellationToken ct = default);
}

public sealed class KavenegarSmsGatewayFactory(AppDbContext db, IOptions<KavenegarSmsOptions> options,
    ILogger<KavenegarSmsGatewayFactory> log) : IKavenegarSmsGatewayFactory
{
    public async Task<IKavenegarSmsGateway?> CreateAsync(CancellationToken ct = default)
    {
        var file = options.Value;
        try
        {
            // Use the same ordered row as GET/POST settings. At staging this query uses
            // the business transaction; a worker uses its own context and needs no HTTP user.
            var row = await db.GlobalSettings.AsNoTracking()
                .Select(s => new { s.KavenegarApiKey, s.KavenegarSender, s.KavenegarSdkSendEnabled })
                .FirstOrDefaultAsync(ct);
            var effective = row is null || string.IsNullOrWhiteSpace(row.KavenegarApiKey) ? file
                : file.WithAccount(row.KavenegarApiKey.Trim(), row.KavenegarSender?.Trim() ?? "", row.KavenegarSdkSendEnabled);
            return new KavenegarSmsGateway(effective);
        }
        catch (Exception ex) when (!string.IsNullOrWhiteSpace(file.ApiKey) && MissingLegacyKeyColumn(ex))
        {
            // A pre-upgrade schema cannot have a stored account. Preserve explicitly
            // configured file-only installations, but never fall back on an arbitrary error.
            log.LogWarning("[SMS] AppSettings lacks the Kavenegar key column; using legacy server configuration. Run update-database.");
            return new KavenegarSmsGateway(file);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A timeout, permission error, partial schema, etc. is NOT evidence that
            // sending is disabled (or that a different, file-based account may send).
            int? code = ex switch { SqlException sql => sql.Number, SqliteException lite => lite.SqliteErrorCode, _ => null };
            log.LogWarning("[SMS] Cannot read Kavenegar settings from AppSettings ({ErrorType}, code {Code}); no file fallback.",
                ex.GetType().Name, code);
            return null;
        }
    }

    private static bool MissingLegacyKeyColumn(Exception ex) =>
        (ex is SqlException { Number: 207 } || ex is SqliteException { SqliteErrorCode: 1 } &&
            ex.Message.Contains("no such column:", StringComparison.OrdinalIgnoreCase)) &&
        ex.Message.Contains(nameof(AppSetting.KavenegarApiKey), StringComparison.OrdinalIgnoreCase);
}
