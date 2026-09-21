using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public interface IIncomingLetterService
{
    Task<PagedResult<IncomingLetterListItemDto>> GetInboxAsync(string? search = null, bool? unreadOnly = null, int page = 1, int pageSize = 15);
    Task<PagedResult<IncomingLetterListItemDto>> GetSentAsync(string? search = null, int page = 1, int pageSize = 15);
    Task<PagedResult<IncomingLetterListItemDto>> GetArchiveAsync(string? search = null, int page = 1, int pageSize = 15);
    Task<IncomingLetterCartableStatsDto> GetStatsAsync();
    Task<IncomingLetterDetailDto> GetDetailAsync(int letterId);
    Task<int> AddAsync(AddIncomingLetterDto dto);
    Task EditAsync(int letterId, EditIncomingLetterDto dto);
    Task DeleteAsync(int letterId);
    Task<bool> ToggleLetterNeshanAsync(int letterId);
    Task<bool> ToggleNeshanAsync(int erjaId);
    Task<bool> ToggleBayeganiAsync(int erjaId);
    Task<int> BatchBayeganiAsync(List<int> erjaIds, string? description = null);
    Task AddErjaAsync(AddErjaDto dto);
    Task AnswerAsync(int erjaId, AnswerErjaDto dto);
    Task MarkReadAsync(int erjaId);
    Task<List<ErjaTreeNodeDto>> GetGardeshAsync(int letterId);
    Task<PagedResult<IncomingLetterPickDto>> PickAsync(string? search = null, int page = 1, int pageSize = 15);
    Task<List<LetterNumberReservationDto>> GetReservationsAsync(int typeForm = 3);
    Task<List<LetterNumberReservationDto>> ReserveNumberAsync(int typeForm = 3, int count = 1);

    /// <summary>کاربران فعال برای انتخاب گیرنده ارجاع (کمبوی مشترک اتوماسیون)</summary>
    Task<List<LetterReciverDto>> GetReciversAsync();

    /// <summary>گروه‌های گیرندگان فعال (انتخاب گروهی در ارجاع)</summary>
    Task<List<LetterGroupDto>> GetGroupsAsync();

    /// <summary>عملگرهای ارجاع</summary>
    Task<List<AmalgarDto>> GetAmalgarsAsync();
    Task<List<LetterAttachmentDto>> GetAttachmentsAsync(int letterId);
    Task UploadAttachmentAsync(int letterId, Stream stream, string fileName, string contentType);
    Task DeleteAttachmentAsync(int attachmentId);
    string AttachmentDownloadUrl(int attachmentId);
}

public class IncomingLetterService : IIncomingLetterService
{
    private readonly IApiClient _api;
    private readonly HttpClient _http;
    private readonly IAuthState _auth;

    public IncomingLetterService(IApiClient api, HttpClient http, IAuthState auth)
    {
        _api = api;
        _http = http;
        _auth = auth;
    }

    private class IdResponse { public int Id { get; set; } }
    private class NeshanResponse { public bool IsNeshan { get; set; } }
    private class BayeganiResponse { public bool IsBayegani { get; set; } }
    private class BatchBayeganiRes { public int Count { get; set; } }

    public Task<PagedResult<IncomingLetterListItemDto>> GetInboxAsync(string? search = null, bool? unreadOnly = null, int page = 1, int pageSize = 15)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (unreadOnly == true) qs.Add("unreadOnly=true");
        return ListOrPaged.GetPagedAsync<IncomingLetterListItemDto>(_api, $"api/incoming-letters/inbox?{string.Join("&", qs)}");
    }

    public Task<PagedResult<IncomingLetterListItemDto>> GetSentAsync(string? search = null, int page = 1, int pageSize = 15)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        return ListOrPaged.GetPagedAsync<IncomingLetterListItemDto>(_api, $"api/incoming-letters/sent?{string.Join("&", qs)}");
    }

    public Task<PagedResult<IncomingLetterListItemDto>> GetArchiveAsync(string? search = null, int page = 1, int pageSize = 15)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        return ListOrPaged.GetPagedAsync<IncomingLetterListItemDto>(_api, $"api/incoming-letters/archive?{string.Join("&", qs)}");
    }

    public Task<IncomingLetterCartableStatsDto> GetStatsAsync() =>
        _api.GetAsync<IncomingLetterCartableStatsDto>("api/incoming-letters/stats");

    public Task<IncomingLetterDetailDto> GetDetailAsync(int letterId) =>
        _api.GetAsync<IncomingLetterDetailDto>($"api/incoming-letters/{letterId}");

    public async Task<int> AddAsync(AddIncomingLetterDto dto) =>
        (await _api.PostAsync<IdResponse>("api/incoming-letters", dto)).Id;

    public Task EditAsync(int letterId, EditIncomingLetterDto dto) =>
        _api.PutAsync<object>($"api/incoming-letters/{letterId}", dto);

    public Task DeleteAsync(int letterId) =>
        _api.DeleteAsync($"api/incoming-letters/{letterId}");

    public async Task<bool> ToggleLetterNeshanAsync(int letterId) =>
        (await _api.PostAsync<NeshanResponse>($"api/incoming-letters/{letterId}/neshan")).IsNeshan;

    public async Task<bool> ToggleNeshanAsync(int erjaId) =>
        (await _api.PostAsync<NeshanResponse>($"api/incoming-letters/erja/{erjaId}/neshan")).IsNeshan;

    public async Task<bool> ToggleBayeganiAsync(int erjaId) =>
        (await _api.PostAsync<BayeganiResponse>($"api/incoming-letters/erja/{erjaId}/bayegani")).IsBayegani;

    public async Task<int> BatchBayeganiAsync(List<int> erjaIds, string? description = null)
    {
        var res = await _api.PostAsync<BatchBayeganiRes>("api/incoming-letters/erja/batch-bayegani", new { erjaIds, description });
        return res?.Count ?? erjaIds.Count;
    }

    public Task AddErjaAsync(AddErjaDto dto) =>
        _api.PostAsync<object>("api/incoming-letters/erja", dto);

    public Task AnswerAsync(int erjaId, AnswerErjaDto dto) =>
        _api.PostAsync<object>($"api/incoming-letters/erja/{erjaId}/answer", dto);

    public Task MarkReadAsync(int erjaId) =>
        _api.PostAsync<object>($"api/incoming-letters/erja/{erjaId}/read");

    public Task<List<ErjaTreeNodeDto>> GetGardeshAsync(int letterId) =>
        ListOrPaged.GetAsync<ErjaTreeNodeDto>(_api, $"api/incoming-letters/{letterId}/gardesh");

    public Task<PagedResult<IncomingLetterPickDto>> PickAsync(string? search = null, int page = 1, int pageSize = 15)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        return ListOrPaged.GetPagedAsync<IncomingLetterPickDto>(_api, $"api/incoming-letters/pick?{string.Join("&", qs)}");
    }

    public Task<List<LetterNumberReservationDto>> GetReservationsAsync(int typeForm = 3) =>
        ListOrPaged.GetAsync<LetterNumberReservationDto>(_api, $"api/incoming-letters/reservations?typeForm={typeForm}");

    public Task<List<LetterNumberReservationDto>> ReserveNumberAsync(int typeForm = 3, int count = 1) =>
        _api.PostAsync<List<LetterNumberReservationDto>>("api/incoming-letters/reserve-number", new { typeForm, count });

    public Task<List<LetterReciverDto>> GetReciversAsync() =>
        ListOrPaged.GetAsync<LetterReciverDto>(_api, "api/incoming-letters/recivers");

    public Task<List<LetterGroupDto>> GetGroupsAsync() =>
        ListOrPaged.GetAsync<LetterGroupDto>(_api, "api/incoming-letters/groups?withMembers=true");

    public Task<List<AmalgarDto>> GetAmalgarsAsync() =>
        ListOrPaged.GetAsync<AmalgarDto>(_api, "api/incoming-letters/amalgars");

    public Task<List<LetterAttachmentDto>> GetAttachmentsAsync(int letterId) =>
        ListOrPaged.GetAsync<LetterAttachmentDto>(_api, $"api/incoming-letters/{letterId}/attachments");

    public Task UploadAttachmentAsync(int letterId, Stream stream, string fileName, string contentType) =>
        UploadCoreAsync($"api/incoming-letters/{letterId}/attachments", stream, fileName, contentType);

    public Task DeleteAttachmentAsync(int attachmentId) =>
        _api.DeleteAsync($"api/incoming-letters/attachments/{attachmentId}");

    public string AttachmentDownloadUrl(int attachmentId)
    {
        var url = _api.BuildUrl($"api/incoming-letters/attachments/{attachmentId}/download");
        return string.IsNullOrWhiteSpace(_auth.Token)
            ? url
            : $"{url}?access_token={Uri.EscapeDataString(_auth.Token)}";
    }

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
}
