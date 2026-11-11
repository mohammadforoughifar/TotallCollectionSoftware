using System.Security.Cryptography;
using System.Text;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Backup;

public sealed class CentralBackupAuthService
{
    public const string HeaderName = "X-Backup-Api-Key";
    public const string CodeHeaderName = "X-Backup-Company-Code";
    private readonly AppDbContext _db;
    public CentralBackupAuthService(AppDbContext db) => _db = db;

    public static string NewKey() => "bak_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    public static string HashKey(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw.Trim())));
    public static string Hint(string raw) => raw.Length <= 8 ? raw : raw[..8];

    public async Task<(CentralBackupCompany? Company, string? Error)> AuthenticateAsync(string? rawKey, string? codeHint)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return (null, "کلید API ارسال نشده است.");
        var hash = HashKey(rawKey);
        var companies = await _db.CentralBackupCompanies.AsNoTracking().Where(x => x.IsActive).ToListAsync();
        var match = companies.FirstOrDefault(c => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(c.ApiKeyHash), Encoding.UTF8.GetBytes(hash)));
        if (match == null) return (null, "کلید API نامعتبر یا غیرفعال است.");
        if (!string.IsNullOrWhiteSpace(codeHint) && !string.Equals(match.Code, codeHint.Trim(), StringComparison.OrdinalIgnoreCase))
            return (null, "کلید API با کد شرکت همخوانی ندارد.");
        return (match, null);
    }
}
