using System.Security.Cryptography;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Backup;

public interface ICentralBackupStorageService
{
    Task<object> ReceiveAsync(CentralBackupCompany company, IFormFile file, string externalId, string databaseName, string reason, string sha256, string? sqlVersion, string? sqlEdition, CancellationToken ct);
    Task CleanupAsync(CentralBackupCompany company);
    string RootPath { get; }
}

public sealed class CentralBackupStorageService : ICentralBackupStorageService
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<CentralBackupStorageService> _log;

    public CentralBackupStorageService(AppDbContext db, IWebHostEnvironment env, ILogger<CentralBackupStorageService> log)
    { _db = db; _env = env; _log = log; }

    public string RootPath => Path.Combine(_env.ContentRootPath, "App_Data", "CentralBackups");

    public async Task<object> ReceiveAsync(CentralBackupCompany company, IFormFile file, string externalId, string databaseName, string reason, string sha256, string? sqlVersion, string? sqlEdition, CancellationToken ct)
    {
        if (file.Length <= 0) throw new InvalidOperationException("فایل بک‌آپ خالی است.");
        if (string.IsNullOrWhiteSpace(externalId) || externalId.Length > 32) throw new InvalidOperationException("شناسه بک‌آپ معتبر نیست.");
        if (string.IsNullOrWhiteSpace(sha256) || sha256.Length != 64) throw new InvalidOperationException("هش SHA-256 معتبر نیست.");

        var existing = await _db.CentralBackupReceipts.FirstOrDefaultAsync(x => x.CompanyId == company.Id && x.ExternalId == externalId, ct);
        if (existing != null)
            return new { ok = true, duplicate = true, receiptId = existing.Id, message = "این بک‌آپ قبلاً دریافت شده است." };

        var safeCode = new string(company.Code.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        var dir = Path.Combine(RootPath, safeCode);
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(file.FileName);
        if (ext != ".bak" && ext != ".zip") ext = ".bak";
        var fileName = $"{externalId}_{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
        var fullPath = Path.Combine(dir, fileName);
        var tempPath = fullPath + ".tmp";

        await using (var fs = File.Create(tempPath))
        {
            await file.CopyToAsync(fs, ct);
            await fs.FlushAsync(ct);
        }

        var hash = await Sha256FileAsync(tempPath, ct);
        if (!string.Equals(hash, sha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(tempPath);
            throw new InvalidOperationException("هش فایل دریافتی با هش اعلام‌شده همخوانی ندارد.");
        }

        var usedMb = await _db.CentralBackupReceipts.Where(x => x.CompanyId == company.Id).SumAsync(x => (long?)x.SizeBytes, ct) ?? 0;
        if (company.MaxStorageMb > 0 && usedMb / (1024 * 1024) + file.Length / (1024 * 1024) > company.MaxStorageMb)
        {
            TryDelete(tempPath);
            throw new InvalidOperationException("سقف فضای ذخیره‌سازی این شرکت در سرور مرکزی تکمیل شده است.");
        }

        File.Move(tempPath, fullPath, true);
        var receipt = new CentralBackupReceipt
        {
            CompanyId = company.Id,
            ExternalId = externalId,
            DatabaseName = databaseName,
            FileName = fileName,
            StoredPath = fullPath,
            SizeBytes = file.Length,
            Sha256 = hash,
            Reason = reason,
            SqlVersion = sqlVersion,
            SqlEdition = sqlEdition,
            ReceivedAt = DateTime.Now,
            Status = "Stored"
        };
        _db.CentralBackupReceipts.Add(receipt);
        _db.CentralBackupCompanies.Attach(company);
        _db.Entry(company).Property(x => x.LastSeenAt).IsModified = true;
        company.LastSeenAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        await CleanupAsync(company);
        return new { ok = true, duplicate = false, receiptId = receipt.Id, message = "بک‌آپ با موفقیت دریافت شد." };
    }

    public async Task CleanupAsync(CentralBackupCompany company)
    {
        if (company.RetentionDays <= 0) return;
        var cutoff = DateTime.Now.AddDays(-company.RetentionDays);
        var old = await _db.CentralBackupReceipts.Where(x => x.CompanyId == company.Id && x.ReceivedAt < cutoff).ToListAsync();
        foreach (var r in old)
        {
            TryDelete(r.StoredPath);
            _db.CentralBackupReceipts.Remove(r);
        }
        if (old.Count > 0) await _db.SaveChangesAsync();
    }

    private static async Task<string> Sha256FileAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(fs, ct)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
