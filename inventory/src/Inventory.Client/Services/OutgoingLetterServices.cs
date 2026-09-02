using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public interface IOutgoingLetterService
{
    Task<List<OutgoingLetterListItemDto>> GetInboxAsync(string? search = null, bool? unreadOnly = null);
    Task<List<OutgoingLetterListItemDto>> GetSentAsync(string? search = null);
    Task<List<OutgoingLetterListItemDto>> GetArchiveAsync(string? search = null);
    Task<OutgoingLetterCartableStatsDto> GetStatsAsync();
    Task<OutgoingLetterDetailDto> GetDetailAsync(int letterId);
    Task<int> SendAsync(AddOutgoingLetterDto dto);
    Task DeleteAsync(int letterId);
    Task<List<OutgoingLetterPickDto>> PickAsync(string? search = null);
    Task UpdateStatusAsync(int letterId, int status);
    Task<List<ErjaTreeNodeDto>> GetGardeshAsync(int letterId);
    Task AddErjaAsync(AddErjaDto dto);
    Task AnswerAsync(int erjaId, AnswerErjaDto dto);
    Task MarkReadAsync(int erjaId);
    Task<bool> ToggleNeshanAsync(int erjaId);
    Task<bool> ToggleBayeganiAsync(int erjaId);
    Task<List<AmalgarDto>> GetAmalgarsAsync();
    Task<List<OutgoingPishnevisDto>> GetPishnevisListAsync(string? search = null);
    Task<OutgoingPishnevisDto> GetPishnevisAsync(int id);
    Task<int> SavePishnevisAsync(OutgoingPishnevisDto dto);
    Task DeletePishnevisAsync(int id);
    Task<List<LetterReciverDto>> GetReciversAsync();
    Task EditAsync(int letterId, EditOutgoingLetterDto dto);
    Task<List<LetterGroupDto>> GetGroupsAsync();
    Task<List<OutgoingLetterListItemDto>> GetSigningInboxAsync(string? search = null, bool? unsignedOnly = null);
    Task<List<OutgoingSignerDto>> GetSignersAsync(int letterId);
    Task SignAsync(int letterId, string? note = null);
    Task<List<LetterReciverDto>> GetAvailableSignersAsync(string? search = null);
    Task<List<LetterAttachmentDto>> GetAttachmentsAsync(int letterId);
    Task UploadAttachmentAsync(int letterId, Stream stream, string fileName, string contentType);
    Task<List<LetterAttachmentDto>> GetPishnevisAttachmentsAsync(int pishnevisId);
    Task UploadPishnevisAttachmentAsync(int pishnevisId, Stream stream, string fileName, string contentType);
    Task DeleteAttachmentAsync(int attachmentId);
    string AttachmentDownloadUrl(int attachmentId);

    // ==================== دبیرخانه نامه صادره ====================
    Task<List<DabirkhaneListItemDto>> GetDabirkhaneAsync(string? search = null, bool? registeredOnly = null);
    Task<DabirkhaneStatsDto> GetDabirkhaneStatsAsync();
    Task DabirkhaneRegisterAsync(int letterId, DabirkhaneRegisterDto dto);
    Task<List<LetterCompanyDto>> GetCompaniesAsync();

    /// <summary>دریافت PDF چاپ نامه روی سربرگ — size: A4 یا A5</summary>
    Task<byte[]> GetPrintPdfAsync(int letterId, string size);
}

public class OutgoingLetterService : IOutgoingLetterService
{
    private readonly IApiClient _api;
    private readonly HttpClient _http;
    private readonly IAuthState _auth;

    public OutgoingLetterService(IApiClient api, HttpClient http, IAuthState auth)
    {
        _api = api;
        _http = http;
        _auth = auth;
    }

    private class IdResponse { public int Id { get; set; } }
    private class NeshanResponse { public bool IsNeshan { get; set; } }
    private class BayeganiResponse { public bool IsBayegani { get; set; } }

    public Task<List<OutgoingLetterListItemDto>> GetInboxAsync(string? search = null, bool? unreadOnly = null)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (unreadOnly == true) qs.Add("unreadOnly=true");
        var q = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return _api.GetAsync<List<OutgoingLetterListItemDto>>($"api/outgoing-letters/inbox{q}");
    }

    public Task<List<OutgoingLetterListItemDto>> GetSentAsync(string? search = null) =>
        _api.GetAsync<List<OutgoingLetterListItemDto>>(
            $"api/outgoing-letters/sent{(string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}")}");

    public Task<List<OutgoingLetterListItemDto>> GetArchiveAsync(string? search = null) =>
        _api.GetAsync<List<OutgoingLetterListItemDto>>(
            $"api/outgoing-letters/archive{(string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}")}");

    public Task<OutgoingLetterCartableStatsDto> GetStatsAsync() =>
        _api.GetAsync<OutgoingLetterCartableStatsDto>("api/outgoing-letters/stats");

    public Task<OutgoingLetterDetailDto> GetDetailAsync(int letterId) =>
        _api.GetAsync<OutgoingLetterDetailDto>($"api/outgoing-letters/{letterId}");

    public async Task<int> SendAsync(AddOutgoingLetterDto dto) =>
        (await _api.PostAsync<IdResponse>("api/outgoing-letters", dto)).Id;

    public Task DeleteAsync(int letterId) => _api.DeleteAsync($"api/outgoing-letters/{letterId}");

    public Task UpdateStatusAsync(int letterId, int status) =>
        _api.PostAsync<object>($"api/outgoing-letters/{letterId}/status", new { status });

    public Task<List<OutgoingLetterPickDto>> PickAsync(string? search = null) =>
        _api.GetAsync<List<OutgoingLetterPickDto>>(
            $"api/outgoing-letters/pick{(string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}")}");

    public Task<List<ErjaTreeNodeDto>> GetGardeshAsync(int letterId) =>
        _api.GetAsync<List<ErjaTreeNodeDto>>($"api/outgoing-letters/{letterId}/gardesh");

    public Task AddErjaAsync(AddErjaDto dto) => _api.PostAsync<object>("api/outgoing-letters/erja", dto);

    public Task AnswerAsync(int erjaId, AnswerErjaDto dto) =>
        _api.PostAsync<object>($"api/outgoing-letters/erja/{erjaId}/answer", dto);

    public Task MarkReadAsync(int erjaId) =>
        _api.PostAsync<object>($"api/outgoing-letters/erja/{erjaId}/read");

    public async Task<bool> ToggleNeshanAsync(int erjaId) =>
        (await _api.PostAsync<NeshanResponse>($"api/outgoing-letters/erja/{erjaId}/neshan")).IsNeshan;

    public async Task<bool> ToggleBayeganiAsync(int erjaId) =>
        (await _api.PostAsync<BayeganiResponse>($"api/outgoing-letters/erja/{erjaId}/bayegani")).IsBayegani;

    public Task<List<AmalgarDto>> GetAmalgarsAsync() =>
        _api.GetAsync<List<AmalgarDto>>("api/outgoing-letters/amalgars");

    public Task<List<OutgoingPishnevisDto>> GetPishnevisListAsync(string? search = null) =>
        _api.GetAsync<List<OutgoingPishnevisDto>>(
            $"api/outgoing-letters/pishnevis{(string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}")}");

    public Task<OutgoingPishnevisDto> GetPishnevisAsync(int id) =>
        _api.GetAsync<OutgoingPishnevisDto>($"api/outgoing-letters/pishnevis/{id}");

    public async Task<int> SavePishnevisAsync(OutgoingPishnevisDto dto) =>
        (await _api.PostAsync<IdResponse>("api/outgoing-letters/pishnevis", dto)).Id;

    public Task DeletePishnevisAsync(int id) => _api.DeleteAsync($"api/outgoing-letters/pishnevis/{id}");

    public Task<List<LetterReciverDto>> GetReciversAsync() =>
        _api.GetAsync<List<LetterReciverDto>>("api/outgoing-letters/recivers");

    public Task EditAsync(int letterId, EditOutgoingLetterDto dto) =>
        _api.PutAsync<object>($"api/outgoing-letters/{letterId}", dto);

    public Task<List<LetterGroupDto>> GetGroupsAsync() =>
        _api.GetAsync<List<LetterGroupDto>>("api/outgoing-letters/groups?withMembers=true");

    public Task<List<OutgoingLetterListItemDto>> GetSigningInboxAsync(string? search = null, bool? unsignedOnly = null)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (unsignedOnly == true) qs.Add("unsignedOnly=true");
        var q = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return _api.GetAsync<List<OutgoingLetterListItemDto>>($"api/outgoing-letters/signing-inbox{q}");
    }

    public Task<List<OutgoingSignerDto>> GetSignersAsync(int letterId) =>
        _api.GetAsync<List<OutgoingSignerDto>>($"api/outgoing-letters/{letterId}/signers");

    public Task SignAsync(int letterId, string? note = null) =>
        _api.PostAsync<object>($"api/outgoing-letters/{letterId}/sign", new { signNote = note });

    public Task<List<LetterReciverDto>> GetAvailableSignersAsync(string? search = null) =>
        _api.GetAsync<List<LetterReciverDto>>(
            $"api/outgoing-letters/available-signers{(string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}")}");

    public Task<List<LetterAttachmentDto>> GetAttachmentsAsync(int letterId) =>
        _api.GetAsync<List<LetterAttachmentDto>>($"api/outgoing-letters/{letterId}/attachments");

    public Task UploadAttachmentAsync(int letterId, Stream stream, string fileName, string contentType) =>
        UploadCoreAsync($"api/outgoing-letters/{letterId}/attachments", stream, fileName, contentType);

    public Task<List<LetterAttachmentDto>> GetPishnevisAttachmentsAsync(int pishnevisId) =>
        _api.GetAsync<List<LetterAttachmentDto>>($"api/outgoing-letters/pishnevis/{pishnevisId}/attachments");

    public Task UploadPishnevisAttachmentAsync(int pishnevisId, Stream stream, string fileName, string contentType) =>
        UploadCoreAsync($"api/outgoing-letters/pishnevis/{pishnevisId}/attachments", stream, fileName, contentType);

    private async Task UploadCoreAsync(string path, Stream stream, string fileName, string contentType)
    {
        using var content = new MultipartFormDataContent();
        var sc = new StreamContent(stream);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        content.Add(sc, "file", fileName);

        var req = new HttpRequestMessage(HttpMethod.Post, _api.BuildUrl(path))
        { Content = content };
        if (!string.IsNullOrWhiteSpace(_auth.Token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _auth.Token);

        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync();
            string msg = "بارگذاری پیوست ناموفق بود.";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("message", out var m)) msg = m.GetString() ?? msg;
            }
            catch { }
            throw new ApiException(msg);
        }
    }

    public Task DeleteAttachmentAsync(int attachmentId) =>
        _api.DeleteAsync($"api/outgoing-letters/attachments/{attachmentId}");

    public string AttachmentDownloadUrl(int attachmentId) =>
        _api.BuildUrl($"api/outgoing-letters/attachments/{attachmentId}/download");

    // ==================== دبیرخانه نامه صادره ====================

    public Task<List<DabirkhaneListItemDto>> GetDabirkhaneAsync(string? search = null, bool? registeredOnly = null)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (registeredOnly != null) qs.Add($"registeredOnly={(registeredOnly == true ? "true" : "false")}");
        var q = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return _api.GetAsync<List<DabirkhaneListItemDto>>($"api/outgoing-letters/dabirkhane{q}");
    }

    public Task<DabirkhaneStatsDto> GetDabirkhaneStatsAsync() =>
        _api.GetAsync<DabirkhaneStatsDto>("api/outgoing-letters/dabirkhane/stats");

    public Task DabirkhaneRegisterAsync(int letterId, DabirkhaneRegisterDto dto) =>
        _api.PostAsync<object>($"api/outgoing-letters/{letterId}/dabirkhane", dto);

    public Task<List<LetterCompanyDto>> GetCompaniesAsync() =>
        _api.GetAsync<List<LetterCompanyDto>>("api/outgoing-letters/companies");

    /// <summary>دریافت PDF چاپ نامه روی سربرگ شرکت (A4/A5) — با توکن ورود</summary>
    public async Task<byte[]> GetPrintPdfAsync(int letterId, string size)
    {
        var req = new HttpRequestMessage(HttpMethod.Get,
            _api.BuildUrl($"api/outgoing-letters/{letterId}/print?size={Uri.EscapeDataString(size)}"));
        if (!string.IsNullOrWhiteSpace(_auth.Token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _auth.Token);

        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync();
            string msg = "دریافت فایل چاپ ناموفق بود.";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("message", out var m)) msg = m.GetString() ?? msg;
            }
            catch { }
            throw new ApiException(msg);
        }
        return await resp.Content.ReadAsByteArrayAsync();
    }
}
