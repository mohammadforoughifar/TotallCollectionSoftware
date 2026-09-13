using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// سرویس کلاینت ماژول اتوماسیون اداری — کارتابل نامه داخلی
// =====================================================================

public interface ILetterService
{
    // کارتابل — نسخهٔ لیستی (همهٔ صفحه‌ها از API صفحه‌بندی‌شده جمع می‌شود)
    Task<List<InnerLetterListItemDto>> GetInboxAsync(string? search = null, bool? unreadOnly = null);
    Task<List<InnerLetterListItemDto>> GetSentAsync(string? search = null);
    Task<List<InnerLetterListItemDto>> GetArchiveAsync(string? search = null);

    // کارتابل — نسخهٔ صفحه‌بندی‌شده (خروجی استاندارد GetAll در API)
    Task<PagedResult<InnerLetterListItemDto>> GetInboxPagedAsync(string? search = null, bool? unreadOnly = null, int page = 1, int pageSize = 20);
    Task<PagedResult<InnerLetterListItemDto>> GetSentPagedAsync(string? search = null, int page = 1, int pageSize = 20);
    Task<PagedResult<InnerLetterListItemDto>> GetArchivePagedAsync(string? search = null, int page = 1, int pageSize = 20);

    Task<LetterCartableStatsDto> GetStatsAsync();
    Task<InnerLetterDetailDto> GetDetailAsync(int letterId);
    Task<int> SendAsync(AddInnerLetterDto dto);
    Task DeleteAsync(int letterId);
    Task<List<LetterPickDto>> PickAsync(string? search = null);
    Task<PagedResult<LetterPickDto>> PickPagedAsync(string? search = null, int page = 1, int pageSize = 20);

    // گردش / ارجاع
    Task<List<ErjaTreeNodeDto>> GetGardeshAsync(int letterId);
    Task AddErjaAsync(AddErjaDto dto);
    Task AnswerAsync(int erjaId, AnswerErjaDto dto);
    Task MarkReadAsync(int erjaId);
    Task<bool> ToggleNeshanAsync(int erjaId);
    Task<bool> ToggleBayeganiAsync(int erjaId);
    Task<List<AmalgarDto>> GetAmalgarsAsync();
    Task<PagedResult<AmalgarDto>> GetAmalgarsPagedAsync(int page = 1, int pageSize = 20);

    // پیش‌نویس
    Task<List<PishnevisDto>> GetPishnevisListAsync(string? search = null);
    Task<PagedResult<PishnevisDto>> GetPishnevisPagedAsync(string? search = null, int page = 1, int pageSize = 20);
    Task<PishnevisDto> GetPishnevisAsync(int id);
    Task<int> SavePishnevisAsync(PishnevisDto dto);
    Task DeletePishnevisAsync(int id);

    // گیرندگان
    Task<List<LetterReciverDto>> GetReciversAsync();
    Task<PagedResult<LetterReciverDto>> GetReciversPagedAsync(int page = 1, int pageSize = 200);

    // ویرایش نامه (فقط قبل از خوانده‌شدن)
    Task EditAsync(int letterId, EditInnerLetterDto dto);

    // گروه‌های گیرندگان
    Task<List<LetterGroupDto>> GetGroupsAsync();
    Task<PagedResult<LetterGroupDto>> GetGroupsPagedAsync(bool withMembers = true, int page = 1, int pageSize = 20);
    Task<int> SaveGroupAsync(SaveLetterGroupDto dto);
    Task DeleteGroupAsync(int groupId);

    // پیوست‌ها
    Task<List<LetterAttachmentDto>> GetAttachmentsAsync(int letterId);
    Task UploadAttachmentAsync(int letterId, Stream stream, string fileName, string contentType);
    Task<List<LetterAttachmentDto>> GetPishnevisAttachmentsAsync(int pishnevisId);
    Task UploadPishnevisAttachmentAsync(int pishnevisId, Stream stream, string fileName, string contentType);
    Task DeleteAttachmentAsync(int attachmentId);
    string AttachmentDownloadUrl(int attachmentId);
}

public class LetterService : ILetterService
{
    private readonly IApiClient _api;
    private readonly HttpClient _http;
    private readonly IAuthState _auth;

    public LetterService(IApiClient api, HttpClient http, IAuthState auth)
    {
        _api = api;
        _http = http;
        _auth = auth;
    }

    private class IdResponse { public int Id { get; set; } }
    private class NeshanResponse { public bool IsNeshan { get; set; } }

    /// <summary>ساخت کوئری‌استرینگ مشترک کارتابل (جستجو/فیلتر + صفحه‌بندی).</summary>
    private static string CartableQuery(string? search, bool? unreadOnly, int page, int pageSize)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (unreadOnly == true) qs.Add("unreadOnly=true");
        return "?" + string.Join("&", qs);
    }

    public Task<List<InnerLetterListItemDto>> GetInboxAsync(string? search = null, bool? unreadOnly = null)
        => PagedFetch.AllAsync<InnerLetterListItemDto>((page, size) =>
            _api.GetAsync<PagedResult<InnerLetterListItemDto>>($"api/letters/inbox{CartableQuery(search, unreadOnly, page, size)}"));

    public Task<PagedResult<InnerLetterListItemDto>> GetInboxPagedAsync(string? search = null, bool? unreadOnly = null, int page = 1, int pageSize = 20)
        => _api.GetAsync<PagedResult<InnerLetterListItemDto>>($"api/letters/inbox{CartableQuery(search, unreadOnly, page, pageSize)}");

    public Task<List<InnerLetterListItemDto>> GetSentAsync(string? search = null)
        => PagedFetch.AllAsync<InnerLetterListItemDto>((page, size) =>
            _api.GetAsync<PagedResult<InnerLetterListItemDto>>($"api/letters/sent{CartableQuery(search, null, page, size)}"));

    public Task<PagedResult<InnerLetterListItemDto>> GetSentPagedAsync(string? search = null, int page = 1, int pageSize = 20)
        => _api.GetAsync<PagedResult<InnerLetterListItemDto>>($"api/letters/sent{CartableQuery(search, null, page, pageSize)}");

    public Task<List<InnerLetterListItemDto>> GetArchiveAsync(string? search = null)
        => PagedFetch.AllAsync<InnerLetterListItemDto>((page, size) =>
            _api.GetAsync<PagedResult<InnerLetterListItemDto>>($"api/letters/archive{CartableQuery(search, null, page, size)}"));

    public Task<PagedResult<InnerLetterListItemDto>> GetArchivePagedAsync(string? search = null, int page = 1, int pageSize = 20)
        => _api.GetAsync<PagedResult<InnerLetterListItemDto>>($"api/letters/archive{CartableQuery(search, null, page, pageSize)}");

    public Task<LetterCartableStatsDto> GetStatsAsync() =>
        _api.GetAsync<LetterCartableStatsDto>("api/letters/stats");

    public Task<InnerLetterDetailDto> GetDetailAsync(int letterId) =>
        _api.GetAsync<InnerLetterDetailDto>($"api/letters/{letterId}");

    public async Task<int> SendAsync(AddInnerLetterDto dto) =>
        (await _api.PostAsync<IdResponse>("api/letters", dto)).Id;

    public Task DeleteAsync(int letterId) => _api.DeleteAsync($"api/letters/{letterId}");

    /// <summary>حداکثر تعداد نتیجه در کمبوی انتخاب نامه (عطف/پیرو) — همان سقف قبلی سمت سرور.</summary>
    private const int PickComboLimit = 30;

    private static string PickUrl(string? search, int page, int pageSize) =>
        $"api/letters/pick?page={page}&pageSize={pageSize}" +
        (string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}");

    /// <summary>فهرست انتخاب نامه برای کمبوی عطف/پیرو — فقط اولین صفحه (۳۰ مورد آخر).</summary>
    public async Task<List<LetterPickDto>> PickAsync(string? search = null)
        => (await _api.GetAsync<PagedResult<LetterPickDto>>(PickUrl(search, 1, PickComboLimit))).Items;

    public Task<PagedResult<LetterPickDto>> PickPagedAsync(string? search = null, int page = 1, int pageSize = 20)
        => _api.GetAsync<PagedResult<LetterPickDto>>(PickUrl(search, page, pageSize));

    public Task<List<ErjaTreeNodeDto>> GetGardeshAsync(int letterId) =>
        _api.GetAsync<List<ErjaTreeNodeDto>>($"api/letters/{letterId}/gardesh");

    public Task AddErjaAsync(AddErjaDto dto) => _api.PostAsync<object>("api/letters/erja", dto);

    public Task AnswerAsync(int erjaId, AnswerErjaDto dto) =>
        _api.PostAsync<object>($"api/letters/erja/{erjaId}/answer", dto);

    public Task MarkReadAsync(int erjaId) =>
        _api.PostAsync<object>($"api/letters/erja/{erjaId}/read");

    public async Task<bool> ToggleNeshanAsync(int erjaId) =>
        (await _api.PostAsync<NeshanResponse>($"api/letters/erja/{erjaId}/neshan")).IsNeshan;

    private class BayeganiResponse { public bool IsBayegani { get; set; } }

    public async Task<bool> ToggleBayeganiAsync(int erjaId) =>
        (await _api.PostAsync<BayeganiResponse>($"api/letters/erja/{erjaId}/bayegani")).IsBayegani;

    public Task<List<AmalgarDto>> GetAmalgarsAsync() =>
        PagedFetch.AllAsync<AmalgarDto>((page, size) =>
            _api.GetAsync<PagedResult<AmalgarDto>>($"api/letters/amalgars?page={page}&pageSize={size}"));

    public Task<PagedResult<AmalgarDto>> GetAmalgarsPagedAsync(int page = 1, int pageSize = 20) =>
        _api.GetAsync<PagedResult<AmalgarDto>>($"api/letters/amalgars?page={page}&pageSize={pageSize}");

    public Task<List<PishnevisDto>> GetPishnevisListAsync(string? search = null) =>
        PagedFetch.AllAsync<PishnevisDto>((page, size) =>
            _api.GetAsync<PagedResult<PishnevisDto>>(
                $"api/letters/pishnevis?page={page}&pageSize={size}{(string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}")}"));

    public Task<PagedResult<PishnevisDto>> GetPishnevisPagedAsync(string? search = null, int page = 1, int pageSize = 20) =>
        _api.GetAsync<PagedResult<PishnevisDto>>(
            $"api/letters/pishnevis?page={page}&pageSize={pageSize}{(string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}")}");

    public Task<PishnevisDto> GetPishnevisAsync(int id) =>
        _api.GetAsync<PishnevisDto>($"api/letters/pishnevis/{id}");

    public async Task<int> SavePishnevisAsync(PishnevisDto dto) =>
        (await _api.PostAsync<IdResponse>("api/letters/pishnevis", dto)).Id;

    public Task DeletePishnevisAsync(int id) => _api.DeleteAsync($"api/letters/pishnevis/{id}");

    public Task<List<LetterReciverDto>> GetReciversAsync() =>
        PagedFetch.AllAsync<LetterReciverDto>((page, size) =>
            _api.GetAsync<PagedResult<LetterReciverDto>>($"api/letters/recivers?page={page}&pageSize={size}"));

    public Task<PagedResult<LetterReciverDto>> GetReciversPagedAsync(int page = 1, int pageSize = 200) =>
        _api.GetAsync<PagedResult<LetterReciverDto>>($"api/letters/recivers?page={page}&pageSize={pageSize}");

    // ==================== ویرایش نامه ====================

    public Task EditAsync(int letterId, EditInnerLetterDto dto) =>
        _api.PutAsync<object>($"api/letters/{letterId}", dto);

    // ==================== گروه‌های گیرندگان ====================

    public Task<List<LetterGroupDto>> GetGroupsAsync() =>
        PagedFetch.AllAsync<LetterGroupDto>((page, size) =>
            _api.GetAsync<PagedResult<LetterGroupDto>>($"api/letters/groups?withMembers=true&page={page}&pageSize={size}"));

    public Task<PagedResult<LetterGroupDto>> GetGroupsPagedAsync(bool withMembers = true, int page = 1, int pageSize = 20) =>
        _api.GetAsync<PagedResult<LetterGroupDto>>($"api/letters/groups?withMembers={withMembers}&page={page}&pageSize={pageSize}");

    public async Task<int> SaveGroupAsync(SaveLetterGroupDto dto) =>
        (await _api.PostAsync<IdResponse>("api/letters/groups", dto)).Id;

    public Task DeleteGroupAsync(int groupId) => _api.DeleteAsync($"api/letters/groups/{groupId}");

    // ==================== پیوست‌ها ====================

    public Task<List<LetterAttachmentDto>> GetAttachmentsAsync(int letterId) =>
        _api.GetAsync<List<LetterAttachmentDto>>($"api/letters/{letterId}/attachments");

    public Task UploadAttachmentAsync(int letterId, Stream stream, string fileName, string contentType) =>
        UploadCoreAsync($"api/letters/{letterId}/attachments", stream, fileName, contentType);

    public Task<List<LetterAttachmentDto>> GetPishnevisAttachmentsAsync(int pishnevisId) =>
        _api.GetAsync<List<LetterAttachmentDto>>($"api/letters/pishnevis/{pishnevisId}/attachments");

    public Task UploadPishnevisAttachmentAsync(int pishnevisId, Stream stream, string fileName, string contentType) =>
        UploadCoreAsync($"api/letters/pishnevis/{pishnevisId}/attachments", stream, fileName, contentType);

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
        _api.DeleteAsync($"api/letters/attachments/{attachmentId}");

    public string AttachmentDownloadUrl(int attachmentId) =>
        _api.BuildUrl($"api/letters/attachments/{attachmentId}/download");
}
