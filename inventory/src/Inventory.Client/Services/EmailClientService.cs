using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>فایل پیوست ارسال ایمیل</summary>
public record EmailOutAttachment(string FileName, string ContentType, byte[] Data);

/// <summary>کلاینت ایمیل سازمانی — حساب‌ها، صندوق، ارسال/دریافت، بایگانی</summary>
public interface IEmailClientService
{
    // ---------- حساب‌ها ----------
    Task<List<EmailAccountDto>> GetAccountsAsync();
    Task<List<EmailAccountDto>> GetDabirkhaneAccountsAsync();
    Task<int> SaveAccountAsync(SaveEmailAccountDto dto);
    Task DeleteAccountAsync(int emailId);
    Task<EmailTestResultDto> TestAccountAsync(SaveEmailAccountDto dto);

    // ---------- پیام‌ها ----------
    Task<EmailSyncResultDto> SyncAsync(int emailId);
    Task<List<EmailMessageListItemDto>> GetInboxAsync(int? emailId = null, string? search = null, bool? unreadOnly = null, int? folderId = null);
    Task<List<EmailMessageListItemDto>> GetSentAsync(int? emailId = null, string? search = null, int? folderId = null);
    Task<EmailMessageDetailDto> GetMessageAsync(string box, int id);
    Task<bool> ToggleNeshanAsync(string box, int id);
    Task ArchiveAsync(string box, int id, int folderId);
    Task SendAsync(int emailAccountId, string to, string? cc, string subject, string body, List<EmailOutAttachment> attachments);
    string AttachmentDownloadUrl(int attachmentId);
    Task<(byte[] Data, string FileName)> DownloadAttachmentAsync(int attachmentId);

    // ---------- پوشه‌ها ----------
    Task<List<EmailFolderDto>> GetFoldersAsync();
    Task<int> SaveFolderAsync(SaveEmailFolderDto dto);
    Task DeleteFolderAsync(int folderId);
}

public class EmailClientService : IEmailClientService
{
    private readonly IApiClient _api;
    private readonly HttpClient _http;
    private readonly ApiOptions _opts;
    private readonly IAuthState _auth;

    public EmailClientService(IApiClient api, HttpClient http, ApiOptions opts, IAuthState auth)
    {
        _api = api;
        _http = http;
        _opts = opts;
        _auth = auth;
    }

    /// <summary>ارسال ایمیل با پیوست — multipart/form-data</summary>
    public async Task SendAsync(int emailAccountId, string to, string? cc, string subject, string body, List<EmailOutAttachment> attachments)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(emailAccountId.ToString()), "emailAccountId");
        content.Add(new StringContent(to ?? ""), "to");
        content.Add(new StringContent(cc ?? ""), "cc");
        content.Add(new StringContent(subject ?? ""), "subject");
        content.Add(new StringContent(body ?? ""), "body");
        foreach (var a in attachments)
        {
            var bc = new ByteArrayContent(a.Data);
            bc.Headers.ContentType = MediaTypeHeaderValue.TryParse(a.ContentType, out var mime)
                ? mime : new MediaTypeHeaderValue("application/octet-stream");
            content.Add(bc, "files", a.FileName);
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl.TrimEnd('/')}/api/email/send") { Content = content };
        if (!string.IsNullOrEmpty(_auth.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);

        using var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            var msg = "ارسال ایمیل ناموفق بود.";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.String)
                    msg = m.GetString() ?? msg;
            }
            catch { }
            throw new ApiException(msg);
        }
    }

    private class IdResponse { public int Id { get; set; } }
    private class MsgResponse { public string? Message { get; set; } }
    private class NeshanResponse { public bool IsNeshan { get; set; } }

    // ==================== حساب‌ها ====================

    public Task<List<EmailAccountDto>> GetAccountsAsync() =>
        _api.GetAsync<List<EmailAccountDto>>("api/email/accounts");

    public Task<List<EmailAccountDto>> GetDabirkhaneAccountsAsync() =>
        _api.GetAsync<List<EmailAccountDto>>("api/email/dabirkhane-accounts");

    public async Task<int> SaveAccountAsync(SaveEmailAccountDto dto) =>
        (await _api.PostAsync<IdResponse>("api/email/accounts", dto)).Id;

    public Task DeleteAccountAsync(int emailId) =>
        _api.DeleteAsync($"api/email/accounts/{emailId}");

    public Task<EmailTestResultDto> TestAccountAsync(SaveEmailAccountDto dto) =>
        _api.PostAsync<EmailTestResultDto>("api/email/test", dto);

    // ==================== پیام‌ها ====================

    public Task<EmailSyncResultDto> SyncAsync(int emailId) =>
        _api.PostAsync<EmailSyncResultDto>($"api/email/accounts/{emailId}/sync");

    public Task<List<EmailMessageListItemDto>> GetInboxAsync(int? emailId = null, string? search = null, bool? unreadOnly = null, int? folderId = null)
    {
        var q = new List<string>();
        if (emailId is > 0) q.Add($"emailId={emailId}");
        if (!string.IsNullOrWhiteSpace(search)) q.Add($"search={Uri.EscapeDataString(search)}");
        if (unreadOnly == true) q.Add("unreadOnly=true");
        if (folderId != null) q.Add($"folderId={folderId.Value}"); // 0=بایگانی‌نشده، -1=بایگانی‌شده، n=پوشه n
        return _api.GetAsync<List<EmailMessageListItemDto>>($"api/email/inbox?{string.Join("&", q)}");
    }

    public Task<List<EmailMessageListItemDto>> GetSentAsync(int? emailId = null, string? search = null, int? folderId = null)
    {
        var q = new List<string>();
        if (emailId is > 0) q.Add($"emailId={emailId}");
        if (!string.IsNullOrWhiteSpace(search)) q.Add($"search={Uri.EscapeDataString(search)}");
        if (folderId != null) q.Add($"folderId={folderId.Value}"); // 0=بایگانی‌نشده، -1=بایگانی‌شده، n=پوشه n
        return _api.GetAsync<List<EmailMessageListItemDto>>($"api/email/sent?{string.Join("&", q)}");
    }

    public Task<EmailMessageDetailDto> GetMessageAsync(string box, int id) =>
        _api.GetAsync<EmailMessageDetailDto>($"api/email/message/{box}/{id}");

    public async Task<bool> ToggleNeshanAsync(string box, int id) =>
        (await _api.PostAsync<NeshanResponse>($"api/email/message/{box}/{id}/neshan")).IsNeshan;

    public Task ArchiveAsync(string box, int id, int folderId) =>
        _api.PostAsync<MsgResponse>("api/email/archive", new EmailArchiveDto { Box = box, Id = id, FolderId = folderId });

    public string AttachmentDownloadUrl(int attachmentId) =>
        _api.BuildUrl($"api/email/attachments/{attachmentId}/download");

    public async Task<(byte[] Data, string FileName)> DownloadAttachmentAsync(int attachmentId)
    {
        var (data, name, _) = await _api.GetFileAsync($"api/email/attachments/{attachmentId}/download");
        return (data, string.IsNullOrWhiteSpace(name) ? "attachment" : name);
    }

    // ==================== پوشه‌ها ====================

    public Task<List<EmailFolderDto>> GetFoldersAsync() =>
        _api.GetAsync<List<EmailFolderDto>>("api/email/folders");

    public async Task<int> SaveFolderAsync(SaveEmailFolderDto dto) =>
        (await _api.PostAsync<IdResponse>("api/email/folders", dto)).Id;

    public Task DeleteFolderAsync(int folderId) =>
        _api.DeleteAsync($"api/email/folders/{folderId}");
}
