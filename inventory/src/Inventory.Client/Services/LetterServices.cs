using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// سرویس کلاینت ماژول اتوماسیون اداری — کارتابل نامه داخلی
// =====================================================================

public interface ILetterService
{
    // کارتابل
    Task<List<InnerLetterListItemDto>> GetInboxAsync(string? search = null, bool? unreadOnly = null);
    Task<List<InnerLetterListItemDto>> GetSentAsync(string? search = null);
    Task<List<InnerLetterListItemDto>> GetArchiveAsync(string? search = null);
    Task<LetterCartableStatsDto> GetStatsAsync();
    Task<InnerLetterDetailDto> GetDetailAsync(int letterId);
    Task<int> SendAsync(AddInnerLetterDto dto);
    Task DeleteAsync(int letterId);
    Task<List<LetterPickDto>> PickAsync(string? search = null);

    // گردش / ارجاع
    Task<List<ErjaTreeNodeDto>> GetGardeshAsync(int letterId);
    Task AddErjaAsync(AddErjaDto dto);
    Task AnswerAsync(int erjaId, AnswerErjaDto dto);
    Task MarkReadAsync(int erjaId);
    Task<bool> ToggleNeshanAsync(int erjaId);
    Task<bool> ToggleLetterNeshanAsync(int letterId);
    Task<bool> ToggleBayeganiAsync(int erjaId);

    // بایگانی درختی
    Task<List<BayeganiNodeDto>> GetBayeganiTreeAsync();
    Task<BayeganiNodeDto> AddBayeganiMainCategoryAsync(SaveBayeganiFolderDto dto);
    Task<BayeganiNodeDto> AddBayeganiSubCategoryAsync(SaveBayeganiFolderDto dto);
    Task<BayeganiNodeDto> EditBayeganiFolderAsync(int id, SaveBayeganiFolderDto dto);
    Task<BayeganiNodeDto> MoveBayeganiFolderAsync(int id, int newParentId);
    Task ArchiveLettersAsync(ArchiveLettersDto dto);
    Task<BayeganiNodeDto> MoveBayeganiLetterAsync(int id, int newParentId);
    Task DeleteBayeganiAsync(int id);
    Task UnarchiveByErjaAsync(int erjaId);
    Task UnarchiveByLetterAsync(int letterId);
    Task<List<AmalgarDto>> GetAmalgarsAsync();

    // پیش‌نویس
    Task<List<PishnevisDto>> GetPishnevisListAsync(string? search = null);
    Task<PishnevisDto> GetPishnevisAsync(int id);
    Task<int> SavePishnevisAsync(PishnevisDto dto);
    Task DeletePishnevisAsync(int id);

    // گیرندگان
    Task<List<LetterReciverDto>> GetReciversAsync();

    // ویرایش نامه (فقط قبل از خوانده‌شدن)
    Task EditAsync(int letterId, EditInnerLetterDto dto);

    // گروه‌های گیرندگان
    Task<List<LetterGroupDto>> GetGroupsAsync();
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

    public Task<List<InnerLetterListItemDto>> GetInboxAsync(string? search = null, bool? unreadOnly = null)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (unreadOnly == true) qs.Add("unreadOnly=true");
        var q = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return _api.GetAsync<List<InnerLetterListItemDto>>($"api/letters/inbox{q}");
    }

    public Task<List<InnerLetterListItemDto>> GetSentAsync(string? search = null) =>
        _api.GetAsync<List<InnerLetterListItemDto>>(
            $"api/letters/sent{(string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}")}");

    public Task<List<InnerLetterListItemDto>> GetArchiveAsync(string? search = null) =>
        _api.GetAsync<List<InnerLetterListItemDto>>(
            $"api/letters/archive{(string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}")}");

    public Task<LetterCartableStatsDto> GetStatsAsync() =>
        _api.GetAsync<LetterCartableStatsDto>("api/letters/stats");

    public Task<InnerLetterDetailDto> GetDetailAsync(int letterId) =>
        _api.GetAsync<InnerLetterDetailDto>($"api/letters/{letterId}");

    public async Task<int> SendAsync(AddInnerLetterDto dto) =>
        (await _api.PostAsync<IdResponse>("api/letters", dto)).Id;

    public Task DeleteAsync(int letterId) => _api.DeleteAsync($"api/letters/{letterId}");

    public Task<List<LetterPickDto>> PickAsync(string? search = null) =>
        _api.GetAsync<List<LetterPickDto>>(
            $"api/letters/pick{(string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}")}");

    public Task<List<ErjaTreeNodeDto>> GetGardeshAsync(int letterId) =>
        _api.GetAsync<List<ErjaTreeNodeDto>>($"api/letters/{letterId}/gardesh");

    public Task AddErjaAsync(AddErjaDto dto) => _api.PostAsync<object>("api/letters/erja", dto);

    public Task AnswerAsync(int erjaId, AnswerErjaDto dto) =>
        _api.PostAsync<object>($"api/letters/erja/{erjaId}/answer", dto);

    public Task MarkReadAsync(int erjaId) =>
        _api.PostAsync<object>($"api/letters/erja/{erjaId}/read");

    public async Task<bool> ToggleNeshanAsync(int erjaId) =>
        (await _api.PostAsync<NeshanResponse>($"api/letters/erja/{erjaId}/neshan")).IsNeshan;

    public async Task<bool> ToggleLetterNeshanAsync(int letterId) =>
        (await _api.PostAsync<NeshanResponse>($"api/letters/{letterId}/neshan")).IsNeshan;

    private class BayeganiResponse { public bool IsBayegani { get; set; } }

    public async Task<bool> ToggleBayeganiAsync(int erjaId) =>
        (await _api.PostAsync<BayeganiResponse>($"api/letters/erja/{erjaId}/bayegani")).IsBayegani;

    // ==================== بایگانی درختی ====================

    public Task<List<BayeganiNodeDto>> GetBayeganiTreeAsync() =>
        _api.GetAsync<List<BayeganiNodeDto>>("api/letters/bayegani/tree");

    public Task<BayeganiNodeDto> AddBayeganiMainCategoryAsync(SaveBayeganiFolderDto dto) =>
        _api.PostAsync<BayeganiNodeDto>("api/letters/bayegani/main-category", dto);

    public Task<BayeganiNodeDto> AddBayeganiSubCategoryAsync(SaveBayeganiFolderDto dto) =>
        _api.PostAsync<BayeganiNodeDto>("api/letters/bayegani/sub-category", dto);

    public Task<BayeganiNodeDto> EditBayeganiFolderAsync(int id, SaveBayeganiFolderDto dto) =>
        _api.PutAsync<BayeganiNodeDto>($"api/letters/bayegani/folder/{id}", dto);

    public Task<BayeganiNodeDto> MoveBayeganiFolderAsync(int id, int newParentId) =>
        _api.PostAsync<BayeganiNodeDto>($"api/letters/bayegani/move-folder/{id}?newParentId={newParentId}");

    public async Task ArchiveLettersAsync(ArchiveLettersDto dto) =>
        await _api.PostAsync<object>("api/letters/bayegani/letters", dto);

    public Task<BayeganiNodeDto> MoveBayeganiLetterAsync(int id, int newParentId) =>
        _api.PostAsync<BayeganiNodeDto>($"api/letters/bayegani/move-letter/{id}?newParentId={newParentId}");

    public Task DeleteBayeganiAsync(int id) =>
        _api.DeleteAsync($"api/letters/bayegani/{id}");

    public async Task UnarchiveByErjaAsync(int erjaId) =>
        await _api.PostAsync<object>($"api/letters/bayegani/unarchive/{erjaId}");

    public async Task UnarchiveByLetterAsync(int letterId) =>
        await _api.PostAsync<object>($"api/letters/bayegani/unarchive-letter/{letterId}");

    public Task<List<AmalgarDto>> GetAmalgarsAsync() =>
        _api.GetAsync<List<AmalgarDto>>("api/letters/amalgars");

    public Task<List<PishnevisDto>> GetPishnevisListAsync(string? search = null) =>
        _api.GetAsync<List<PishnevisDto>>(
            $"api/letters/pishnevis{(string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}")}");

    public Task<PishnevisDto> GetPishnevisAsync(int id) =>
        _api.GetAsync<PishnevisDto>($"api/letters/pishnevis/{id}");

    public async Task<int> SavePishnevisAsync(PishnevisDto dto) =>
        (await _api.PostAsync<IdResponse>("api/letters/pishnevis", dto)).Id;

    public Task DeletePishnevisAsync(int id) => _api.DeleteAsync($"api/letters/pishnevis/{id}");

    public Task<List<LetterReciverDto>> GetReciversAsync() =>
        _api.GetAsync<List<LetterReciverDto>>("api/letters/recivers");

    // ==================== ویرایش نامه ====================

    public Task EditAsync(int letterId, EditInnerLetterDto dto) =>
        _api.PutAsync<object>($"api/letters/{letterId}", dto);

    // ==================== گروه‌های گیرندگان ====================

    public Task<List<LetterGroupDto>> GetGroupsAsync() =>
        _api.GetAsync<List<LetterGroupDto>>("api/letters/groups?withMembers=true");

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

    public string AttachmentDownloadUrl(int attachmentId)
    {
        // لینک مستقیم <a> هدر Authorization ندارد — توکن در query string ارسال می‌شود
        var url = _api.BuildUrl($"api/letters/attachments/{attachmentId}/download");
        return string.IsNullOrWhiteSpace(_auth.Token)
            ? url
            : $"{url}?access_token={Uri.EscapeDataString(_auth.Token)}";
    }
}
