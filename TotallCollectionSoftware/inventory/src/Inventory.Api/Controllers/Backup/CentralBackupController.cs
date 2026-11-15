using Inventory.Api.Data;
using Inventory.Api.Services.Backup;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Licensing.Infrastructure;

namespace Inventory.Api.Controllers.Backup;

[ApiController]
[Route("api/central-backup")]
public sealed class CentralBackupController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CentralBackupAuthService _auth;
    private readonly ICentralBackupStorageService _storage;

    public CentralBackupController(AppDbContext db, CentralBackupAuthService auth, ICentralBackupStorageService storage)
    { _db = db; _auth = auth; _storage = storage; }

    private async Task<(CentralBackupCompany? Company, IActionResult? Error)> AuthExternalAsync()
    {
        var key = Request.Headers[CentralBackupAuthService.HeaderName].FirstOrDefault();
        var code = Request.Headers[CentralBackupAuthService.CodeHeaderName].FirstOrDefault();
        var (company, error) = await _auth.AuthenticateAsync(key, code);
        if (company == null) return (null, Unauthorized(new { message = error }));
        return (company, null);
    }

    [HttpGet("ping")]
    [AllowAnonymous]
    public async Task<IActionResult> Ping()
    {
        var (company, error) = await AuthExternalAsync();
        if (company == null) return error!;
        _db.CentralBackupCompanies.Attach(company);
        _db.Entry(company).Property(x => x.LastSeenAt).IsModified = true;
        company.LastSeenAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, companyCode = company.Code, companyName = company.Name, message = "اتصال بک‌آپ مرکزی برقرار است." });
    }

    [AllowInReadOnly]
    [HttpPost("upload")]
    [AllowAnonymous]
    [RequestSizeLimit(10L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] string externalId, [FromForm] string databaseName, [FromForm] string reason, [FromForm] string sha256, [FromForm] string? sqlVersion, [FromForm] string? sqlEdition, CancellationToken ct)
    {
        var (company, error) = await AuthExternalAsync();
        if (company == null) return error!;
        var result = await _storage.ReceiveAsync(company, file, externalId, databaseName, reason, sha256, sqlVersion, sqlEdition, ct);
        return Ok(result);
    }

    [HttpGet("companies")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<CentralBackupCompanyDto>>> Companies()
    {
        var list = await _db.CentralBackupCompanies.OrderBy(x => x.Code).ToListAsync();
        return Ok(list.Select(x => new CentralBackupCompanyDto
        {
            Id = x.Id,
            Code = x.Code,
            Name = x.Name,
            IsActive = x.IsActive,
            RetentionDays = x.RetentionDays,
            MaxStorageMb = x.MaxStorageMb,
            Note = x.Note,
            ApiKeyHint = x.ApiKeyHint,
            LastSeenAt = x.LastSeenAt
        }).ToList());
    }

    [AllowInReadOnly]
    [HttpPost("companies")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SaveCompany([FromBody] CentralBackupCompanyDto dto)
    {
        var code = (dto.Code ?? "").Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 50 || !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z0-9_-]+$"))
            return BadRequest(new { message = "کد شرکت باید ۲ تا ۵۰ کاراکتر و فقط شامل حروف انگلیسی بزرگ، عدد، خط تیره و زیرخط باشد." });
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "نام شرکت الزامی است." });
        if (await _db.CentralBackupCompanies.AnyAsync(x => x.Code == code && x.Id != dto.Id))
            return BadRequest(new { message = "این کد قبلاً ثبت شده است." });

        CentralBackupCompany c;
        string? plainKey = null;
        if (dto.Id > 0)
        {
            c = await _db.CentralBackupCompanies.FindAsync(dto.Id) ?? throw new InvalidOperationException("شرکت یافت نشد.");
        }
        else
        {
            plainKey = CentralBackupAuthService.NewKey();
            c = new CentralBackupCompany { ApiKeyHash = CentralBackupAuthService.HashKey(plainKey), ApiKeyHint = CentralBackupAuthService.Hint(plainKey), KeyRotatedAt = DateTime.Now };
            _db.CentralBackupCompanies.Add(c);
        }
        c.Code = code;
        c.Name = dto.Name.Trim();
        c.IsActive = dto.IsActive;
        c.RetentionDays = Math.Clamp(dto.RetentionDays, 1, 3650);
        c.MaxStorageMb = Math.Clamp(dto.MaxStorageMb, 0, 10_000_000);
        c.Note = dto.Note?.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { c.Id, c.Code, c.ApiKeyHint, apiKey = plainKey });
    }

    [AllowInReadOnly]
    [HttpPost("companies/{id:int}/rotate-key")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RotateKey(int id)
    {
        var c = await _db.CentralBackupCompanies.FindAsync(id);
        if (c == null) return NotFound();
        var plain = CentralBackupAuthService.NewKey();
        c.ApiKeyHash = CentralBackupAuthService.HashKey(plain);
        c.ApiKeyHint = CentralBackupAuthService.Hint(plain);
        c.KeyRotatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { apiKey = plain, c.ApiKeyHint });
    }

    [AllowInReadOnly]
    [HttpDelete("companies/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteCompany(int id)
    {
        var c = await _db.CentralBackupCompanies.FindAsync(id);
        if (c == null) return NotFound();
        if (await _db.CentralBackupReceipts.AnyAsync(x => x.CompanyId == id))
            return BadRequest(new { message = "این شرکت بک‌آپ دریافت‌شده دارد؛ به‌جای حذف، آن را غیرفعال کنید." });
        _db.CentralBackupCompanies.Remove(c);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpGet("receipts")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<CentralBackupReceiptDto>>> Receipts([FromQuery] int? companyId, [FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 1, 500);
        var q = _db.CentralBackupReceipts.AsQueryable();
        if (companyId is > 0) q = q.Where(x => x.CompanyId == companyId);
        var list = await q.OrderByDescending(x => x.ReceivedAt).Take(take).ToListAsync();
        var companies = await _db.CentralBackupCompanies.ToDictionaryAsync(x => x.Id);
        return Ok(list.Select(r => new CentralBackupReceiptDto
        {
            Id = r.Id,
            CompanyId = r.CompanyId,
            CompanyCode = companies.TryGetValue(r.CompanyId, out var c) ? c.Code : "",
            CompanyName = companies.TryGetValue(r.CompanyId, out var c2) ? c2.Name : "",
            ExternalId = r.ExternalId,
            DatabaseName = r.DatabaseName,
            FileName = r.FileName,
            SizeBytes = r.SizeBytes,
            Sha256 = r.Sha256,
            Reason = r.Reason,
            SqlVersion = r.SqlVersion,
            SqlEdition = r.SqlEdition,
            ReceivedAt = r.ReceivedAt
        }).ToList());
    }

    [HttpGet("receipts/{id:int}/download")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Download(int id)
    {
        var r = await _db.CentralBackupReceipts.FindAsync(id);
        if (r == null || !System.IO.File.Exists(r.StoredPath)) return NotFound();
        var stream = System.IO.File.OpenRead(r.StoredPath);
        return File(stream, "application/octet-stream", r.FileName);
    }

    [AllowInReadOnly]
    [HttpDelete("receipts/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteReceipt(int id)
    {
        var r = await _db.CentralBackupReceipts.FindAsync(id);
        if (r == null) return NotFound();
        try { if (System.IO.File.Exists(r.StoredPath)) System.IO.File.Delete(r.StoredPath); } catch { }
        _db.CentralBackupReceipts.Remove(r);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
