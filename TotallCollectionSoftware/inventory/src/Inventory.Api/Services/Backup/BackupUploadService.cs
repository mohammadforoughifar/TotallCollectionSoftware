using System.Net.Http.Headers;
using Inventory.Api.Data;

namespace Inventory.Api.Services.Backup;

public interface IBackupUploadService
{
    Task<bool> UploadAsync(BackupJob job, BackupSetting settings, string apiKey, CancellationToken ct);
}

public sealed class BackupUploadService : IBackupUploadService
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<BackupUploadService> _log;
    public BackupUploadService(IHttpClientFactory http, ILogger<BackupUploadService> log) { _http = http; _log = log; }

    public async Task<bool> UploadAsync(BackupJob job, BackupSetting settings, string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.FilePath) || !File.Exists(job.FilePath))
            throw new FileNotFoundException("فایل بک‌آپ محلی پیدا نشد.", job.FilePath);
        var client = _http.CreateClient("backup");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(job.ExternalId), "externalId");
        form.Add(new StringContent(job.DatabaseName), "databaseName");
        form.Add(new StringContent(job.Reason), "reason");
        form.Add(new StringContent(job.Sha256 ?? ""), "sha256");
        form.Add(new StringContent(job.SqlVersion ?? ""), "sqlVersion");
        form.Add(new StringContent(job.SqlEdition ?? ""), "sqlEdition");
        var file = new StreamContent(File.OpenRead(job.FilePath));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", job.FileName ?? Path.GetFileName(job.FilePath));
        using var req = new HttpRequestMessage(HttpMethod.Post, settings.CentralServerUrl!.TrimEnd('/') + "/api/central-backup/upload");
        req.Headers.Add(CentralBackupAuthService.HeaderName, apiKey);
        if (!string.IsNullOrWhiteSpace(settings.CompanyCode)) req.Headers.Add(CentralBackupAuthService.CodeHeaderName, settings.CompanyCode);
        req.Content = form;
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.IsSuccessStatusCode) return true;
        var body = await resp.Content.ReadAsStringAsync(ct);
        _log.LogWarning("ارسال بک‌آپ ناموفق: {Status} {Body}", (int)resp.StatusCode, body);
        throw new InvalidOperationException($"سرور مرکزی پاسخ ناموفق داد: {(int)resp.StatusCode}");
    }
}
