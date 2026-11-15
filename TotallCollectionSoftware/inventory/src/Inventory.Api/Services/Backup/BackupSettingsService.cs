using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Backup;

public interface IBackupSettingsService
{
    Task<BackupSetting> GetEntityAsync();
    Task<BackupSettingsDto> GetDtoAsync();
    Task<BackupSettingsDto> SaveAsync(BackupSettingsDto dto);
    string? UnprotectKey(BackupSetting entity);
    Task<object> TestCentralAsync();
}

public sealed class BackupSettingsService : IBackupSettingsService
{
    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _http;

    public BackupSettingsService(AppDbContext db, IDataProtectionProvider dp, IHttpClientFactory http)
    {
        _db = db;
        _protector = dp.CreateProtector("Inventory.Backup.ApiKey.v1");
        _http = http;
    }

    public async Task<BackupSetting> GetEntityAsync()
    {
        var e = await _db.BackupSettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (e != null) return e;
        e = new BackupSetting { Id = 1 };
        _db.BackupSettings.Add(e);
        await _db.SaveChangesAsync();
        return e;
    }

    public async Task<BackupSettingsDto> GetDtoAsync() => ToDto(await GetEntityAsync());

    public async Task<BackupSettingsDto> SaveAsync(BackupSettingsDto dto)
    {
        var e = await GetEntityAsync();
        var url = dto.CentralServerUrl?.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http"))
                throw new InvalidOperationException("آدرس سرور مرکزی معتبر نیست.");
            if (u.Scheme != "https" && !u.IsLoopback)
                throw new InvalidOperationException("برای ارسال بک‌آپ، آدرس سرور مرکزی باید HTTPS باشد.");
        }
        e.CentralServerUrl = string.IsNullOrWhiteSpace(url) ? null : url;
        e.CompanyCode = dto.CompanyCode?.Trim().ToUpperInvariant();
        e.CompanyName = dto.CompanyName?.Trim();
        e.Enabled = dto.Enabled;
        e.TriggerOnLastSession = dto.TriggerOnLastSession;
        e.TriggerDaily = dto.TriggerDaily;
        e.DailyTime = NormalizeTime(dto.DailyTime);
        e.LastSessionGraceSeconds = Math.Clamp(dto.LastSessionGraceSeconds, 30, 3600);
        e.CooldownMinutes = Math.Clamp(dto.CooldownMinutes, 1, 1440);
        e.LocalBackupPath = dto.LocalBackupPath?.Trim();
        e.LocalKeepCount = Math.Clamp(dto.LocalKeepCount, 1, 100);
        if (dto.ClearKey) e.ApiKeyProtected = null;
        if (!string.IsNullOrWhiteSpace(dto.ApiKey)) e.ApiKeyProtected = _protector.Protect(dto.ApiKey.Trim());
        e.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public string? UnprotectKey(BackupSetting e)
    {
        if (string.IsNullOrWhiteSpace(e.ApiKeyProtected)) return null;
        try { return _protector.Unprotect(e.ApiKeyProtected); }
        catch { return null; }
    }

    public async Task<object> TestCentralAsync()
    {
        var e = await GetEntityAsync();
        var key = UnprotectKey(e);
        if (string.IsNullOrWhiteSpace(e.CentralServerUrl) || string.IsNullOrWhiteSpace(key))
            return new { ok = false, message = "آدرس سرور مرکزی یا کلید API تنظیم نشده است." };
        try
        {
            var client = _http.CreateClient("backup");
            using var req = new HttpRequestMessage(HttpMethod.Get, e.CentralServerUrl.TrimEnd('/') + "/api/central-backup/ping");
            req.Headers.Add(CentralBackupAuthService.HeaderName, key);
            if (!string.IsNullOrWhiteSpace(e.CompanyCode)) req.Headers.Add(CentralBackupAuthService.CodeHeaderName, e.CompanyCode);
            using var resp = await client.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            return new { ok = resp.IsSuccessStatusCode, status = (int)resp.StatusCode, message = resp.IsSuccessStatusCode ? "اتصال برقرار است." : body };
        }
        catch (Exception ex)
        {
            return new { ok = false, message = ex.Message };
        }
    }

    private BackupSettingsDto ToDto(BackupSetting e) => new()
    {
        Enabled = e.Enabled,
        CentralServerUrl = e.CentralServerUrl,
        CompanyCode = e.CompanyCode,
        CompanyName = e.CompanyName,
        HasKey = !string.IsNullOrWhiteSpace(e.ApiKeyProtected),
        TriggerOnLastSession = e.TriggerOnLastSession,
        TriggerDaily = e.TriggerDaily,
        DailyTime = e.DailyTime,
        LastSessionGraceSeconds = e.LastSessionGraceSeconds,
        CooldownMinutes = e.CooldownMinutes,
        LocalBackupPath = e.LocalBackupPath,
        LocalKeepCount = e.LocalKeepCount
    };

    private static string NormalizeTime(string? time)
    {
        if (TimeSpan.TryParse(time, out var ts) && ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1))
            return $"{ts.Hours:00}:{ts.Minutes:00}";
        return "23:00";
    }
}
