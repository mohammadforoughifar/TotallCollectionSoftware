using Inventory.Api.Data;
using Inventory.Api.Services.Backup;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Licensing.Infrastructure;

namespace Inventory.Api.Controllers.Backup;

[ApiController]
[Route("api/backup")]
[Authorize(Roles = "Admin")]
public sealed class BackupController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IBackupSettingsService _settings;
    private readonly IBackupSessionService _sessions;
    private readonly ISqlServerBackupService _sql;

    public BackupController(AppDbContext db, IBackupSettingsService settings, IBackupSessionService sessions, ISqlServerBackupService sql)
    { _db = db; _settings = settings; _sessions = sessions; _sql = sql; }

    [HttpGet("settings")]
    public async Task<ActionResult<BackupSettingsDto>> GetSettings() => Ok(await _settings.GetDtoAsync());

    [AllowInReadOnly]
    [HttpPut("settings")]
    public async Task<ActionResult<BackupSettingsDto>> SaveSettings([FromBody] BackupSettingsDto dto) => Ok(await _settings.SaveAsync(dto));

    [AllowInReadOnly]
    [HttpPost("test")]
    public async Task<IActionResult> Test() => Ok(await _settings.TestCentralAsync());

    [HttpGet("status")]
    public async Task<ActionResult<BackupStatusDto>> Status()
    {
        var settings = await _settings.GetDtoAsync();
        SqlServerInfo info;
        try { info = await _sql.DetectAsync(); }
        catch { info = new SqlServerInfo(_db.Database.IsSqlite() ? "Sqlite" : "SqlServer", null, null, false, null); }
        var jobs = await _db.BackupJobs.OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync();
        return Ok(new BackupStatusDto
        {
            Settings = settings,
            ActiveSessions = await _sessions.ActiveCountAsync(settings.LastSessionGraceSeconds),
            PendingJobs = jobs.Count(x => x.Status is "Pending" or "Running" or "Uploading"),
            FailedJobs = jobs.Count(x => x.Status == "Failed"),
            LastJob = jobs.FirstOrDefault() is { } last ? ToDto(last) : null,
            LastSentJob = jobs.FirstOrDefault(x => x.Status == "Sent") is { } sent ? ToDto(sent) : null,
            Provider = info.Provider,
            SqlVersion = info.Version,
            SqlEdition = info.Edition,
            NativeCompressionSupported = info.NativeCompressionSupported
        });
    }

    [HttpGet("jobs")]
    public async Task<ActionResult<List<BackupJobDto>>> Jobs()
    {
        var jobs = await _db.BackupJobs.OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync();
        return Ok(jobs.Select(ToDto).ToList());
    }

    [AllowInReadOnly]
    [HttpPost("run")]
    public async Task<IActionResult> Run()
    {
        var id = Guid.NewGuid().ToString("N");
        _db.BackupJobs.Add(new BackupJob
        {
            Id = id,
            ExternalId = id,
            Reason = "Manual",
            Status = "Pending",
            CreatedAt = DateTime.Now,
            NextAttemptAt = DateTime.Now
        });
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, jobId = id });
    }

    [AllowInReadOnly]
    [HttpPost("jobs/{id}/retry")]
    public async Task<IActionResult> Retry(string id)
    {
        var job = await _db.BackupJobs.FirstOrDefaultAsync(x => x.Id == id);
        if (job == null) return NotFound();
        if (job.Status == "Sent") return BadRequest(new { message = "این بک‌آپ قبلاً ارسال شده است." });
        job.Status = "Failed";
        job.NextAttemptAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static BackupJobDto ToDto(BackupJob j) => new()
    {
        Id = j.Id,
        Reason = j.Reason,
        Status = j.Status,
        DatabaseName = j.DatabaseName,
        FileName = j.FileName,
        SizeBytes = j.SizeBytes,
        Sha256 = j.Sha256,
        SqlVersion = j.SqlVersion,
        SqlEdition = j.SqlEdition,
        Compression = j.Compression,
        CreatedAt = j.CreatedAt,
        SentAt = j.SentAt,
        Attempts = j.Attempts,
        LastError = j.LastError
    };
}
