using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public interface IOutgoingLetterService
{
    /// <summary>جایگزینی کامل فهرست رونوشت‌گیرندگان نامه صادره</summary>
    Task<List<OutgoingLetterCopyToDto>> ReplaceCopyTosAsync(int letterId, List<SaveOutgoingLetterCopyToDto> items);
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

    /// <summary>نشان‌کردن (ستاره) نامه صادره ارسالی توسط فرستنده — روی خود نامه</summary>
    Task<bool> ToggleLetterNeshanAsync(int letterId);
    Task<bool> ToggleBayeganiAsync(int erjaId);
    Task<int> BatchBayeganiAsync(List<int> erjaIds, string? description = null);
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
    Task<List<DabirkhaneListItemDto>> GetDabirkhaneAsync(DabirkhaneSearchDto? filter = null);
    Task<DabirkhaneStatsDto> GetDabirkhaneStatsAsync();
    Task DabirkhaneRegisterAsync(int letterId, DabirkhaneRegisterDto dto);
    Task<List<LetterCompanyDto>> GetCompaniesAsync();
    Task<List<LetterReciverDto>> GetDabirkhaneCreatorsAsync();

    // ==================== بایگانی دبیرخانه ====================
    Task<List<BayeganiNodeDto>> GetDabirkhaneBayeganiTreeAsync();
    Task<BayeganiNodeDto> AddDabirkhaneMainCategoryAsync(SaveBayeganiFolderDto dto);
    Task<BayeganiNodeDto> AddDabirkhaneSubCategoryAsync(SaveBayeganiFolderDto dto);
    Task<BayeganiNodeDto> EditDabirkhaneFolderAsync(int id, SaveBayeganiFolderDto dto);
    Task<BayeganiNodeDto> MoveDabirkhaneFolderAsync(int id, int newParentId);
    Task DeleteDabirkhaneBayeganiAsync(int id);
    Task ArchiveDabirkhaneLettersAsync(ArchiveOutgoingLettersDto dto);
    Task UnarchiveDabirkhaneLetterAsync(int letterId);
    Task<BayeganiNodeDto> MoveDabirkhaneLetterAsync(MoveArchivedLetterDto dto);

    /// <summary>
    /// دریافت PDF چاپ نامه روی سربرگ — size: A4 یا A5
    /// withCopy: true = نسخهٔ «با رونوشت»، false = نسخهٔ «بدون رونوشت»
    /// </summary>
    Task<byte[]> GetPrintPdfAsync(int letterId, string size, bool withCopy = true);
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

    /// <summary>جایگزینی کامل فهرست رونوشت‌گیرندگان نامه صادره</summary>
    public Task<List<OutgoingLetterCopyToDto>> ReplaceCopyTosAsync(int letterId, List<SaveOutgoingLetterCopyToDto> items) =>
        _api.PutAsync<List<OutgoingLetterCopyToDto>>(
            $"api/outgoing-letters/{letterId}/copy-tos",
            new ReplaceOutgoingLetterCopyTosDto { Items = items ?? new List<SaveOutgoingLetterCopyToDto>() });

    public Task<List<OutgoingLetterListItemDto>> GetInboxAsync(string? search = null, bool? unreadOnly = null)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (unreadOnly == true) qs.Add("unreadOnly=true");
        var q = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return ListOrPaged.GetAsync<OutgoingLetterListItemDto>(_api, $"api/outgoing-letters/inbox{q}");
    }

    public Task<List<OutgoingLetterListItemDto>> GetSentAsync(string? search = null) =>
        ListOrPaged.GetAsync<OutgoingLetterListItemDto>(_api,
            $"api/outgoing-letters/sent{(string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}")}");

    public Task<List<OutgoingLetterListItemDto>> GetArchiveAsync(string? search = null) =>
        ListOrPaged.GetAsync<OutgoingLetterListItemDto>(_api,
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

    /// <summary>نشان‌کردن نامه صادره ارسالی (سمت فرستنده) — مشابه نامه داخلی</summary>
    public async Task<bool> ToggleLetterNeshanAsync(int letterId) =>
        (await _api.PostAsync<NeshanResponse>($"api/outgoing-letters/{letterId}/neshan")).IsNeshan;

    public async Task<bool> ToggleBayeganiAsync(int erjaId) =>
        (await _api.PostAsync<BayeganiResponse>($"api/outgoing-letters/erja/{erjaId}/bayegani")).IsBayegani;

    public async Task<int> BatchBayeganiAsync(List<int> erjaIds, string? description = null)
    {
        var res = await _api.PostAsync<BatchBayeganiRes>("api/outgoing-letters/erja/batch-bayegani", new { erjaIds, description });
        return res?.Count ?? erjaIds.Count;
    }

    private class BatchBayeganiRes { public int Count { get; set; } }

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
        return ListOrPaged.GetAsync<OutgoingLetterListItemDto>(_api, $"api/outgoing-letters/signing-inbox{q}");
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

    /// <summary>لیست دبیرخانه با جستجوی پیشرفته — فیلترها به‌صورت پارامتر کوئری ارسال می‌شوند</summary>
    public Task<List<DabirkhaneListItemDto>> GetDabirkhaneAsync(DabirkhaneSearchDto? filter = null)
    {
        var qs = new List<string>();
        if (filter != null)
        {
            if (!string.IsNullOrWhiteSpace(filter.Text))
                qs.Add($"search={Uri.EscapeDataString(filter.Text)}");
            if (filter.RegisteredOnly != null)
                qs.Add($"registeredOnly={(filter.RegisteredOnly == true ? "true" : "false")}");
            if (filter.ArchivedOnly != null)
                qs.Add($"archivedOnly={(filter.ArchivedOnly == true ? "true" : "false")}");
            if (!string.IsNullOrWhiteSpace(filter.SendMethod))
                qs.Add($"sendMethod={Uri.EscapeDataString(filter.SendMethod)}");
            if (filter.CreatorUserId is > 0)
                qs.Add($"creatorUserId={filter.CreatorUserId.Value}");
            if (filter.CompanyId is > 0)
                qs.Add($"companyId={filter.CompanyId.Value}");
            if (!string.IsNullOrWhiteSpace(filter.ReceiverOrganization))
                qs.Add($"receiverOrganization={Uri.EscapeDataString(filter.ReceiverOrganization)}");
            if (!string.IsNullOrWhiteSpace(filter.Mahramanegi))
                qs.Add($"mahramanegi={Uri.EscapeDataString(filter.Mahramanegi)}");
            if (!string.IsNullOrWhiteSpace(filter.Foriat))
                qs.Add($"foriat={Uri.EscapeDataString(filter.Foriat)}");
            if (filter.HasAttachment != null)
                qs.Add($"hasAttachment={(filter.HasAttachment == true ? "true" : "false")}");
            if (filter.FromDate != null)
                qs.Add($"fromDate={Uri.EscapeDataString(filter.FromDate.Value.ToString("yyyy-MM-ddTHH:mm:ss"))}");
            if (filter.ToDate != null)
                qs.Add($"toDate={Uri.EscapeDataString(filter.ToDate.Value.ToString("yyyy-MM-ddTHH:mm:ss"))}");
        }

        var q = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return _api.GetAsync<List<DabirkhaneListItemDto>>($"api/outgoing-letters/dabirkhane{q}");
    }

    public Task<DabirkhaneStatsDto> GetDabirkhaneStatsAsync() =>
        _api.GetAsync<DabirkhaneStatsDto>("api/outgoing-letters/dabirkhane/stats");

    public Task DabirkhaneRegisterAsync(int letterId, DabirkhaneRegisterDto dto) =>
        _api.PostAsync<object>($"api/outgoing-letters/{letterId}/dabirkhane", dto);

    public Task<List<LetterCompanyDto>> GetCompaniesAsync() =>
        _api.GetAsync<List<LetterCompanyDto>>("api/outgoing-letters/companies");

    public Task<List<LetterReciverDto>> GetDabirkhaneCreatorsAsync() =>
        _api.GetAsync<List<LetterReciverDto>>("api/outgoing-letters/dabirkhane/creators");

    // ==================== بایگانی دبیرخانه ====================

    public Task<List<BayeganiNodeDto>> GetDabirkhaneBayeganiTreeAsync() =>
        _api.GetAsync<List<BayeganiNodeDto>>("api/outgoing-letters/dabirkhane/bayegani/tree");

    public Task<BayeganiNodeDto> AddDabirkhaneMainCategoryAsync(SaveBayeganiFolderDto dto) =>
        _api.PostAsync<BayeganiNodeDto>("api/outgoing-letters/dabirkhane/bayegani/main-category", dto);

    public Task<BayeganiNodeDto> AddDabirkhaneSubCategoryAsync(SaveBayeganiFolderDto dto) =>
        _api.PostAsync<BayeganiNodeDto>("api/outgoing-letters/dabirkhane/bayegani/sub-category", dto);

    public Task<BayeganiNodeDto> EditDabirkhaneFolderAsync(int id, SaveBayeganiFolderDto dto) =>
        _api.PutAsync<BayeganiNodeDto>($"api/outgoing-letters/dabirkhane/bayegani/folder/{id}", dto);

    public Task<BayeganiNodeDto> MoveDabirkhaneFolderAsync(int id, int newParentId) =>
        _api.PostAsync<BayeganiNodeDto>($"api/outgoing-letters/dabirkhane/bayegani/folder/{id}/move?newParentId={newParentId}");

    public Task DeleteDabirkhaneBayeganiAsync(int id) =>
        _api.DeleteAsync($"api/outgoing-letters/dabirkhane/bayegani/{id}");

    public Task ArchiveDabirkhaneLettersAsync(ArchiveOutgoingLettersDto dto) =>
        _api.PostAsync<object>("api/outgoing-letters/dabirkhane/bayegani/letters", dto);

    public Task UnarchiveDabirkhaneLetterAsync(int letterId) =>
        _api.DeleteAsync($"api/outgoing-letters/dabirkhane/bayegani/letter/{letterId}");

    public Task<BayeganiNodeDto> MoveDabirkhaneLetterAsync(MoveArchivedLetterDto dto) =>
        _api.PostAsync<BayeganiNodeDto>("api/outgoing-letters/dabirkhane/bayegani/move-letter", dto);

    /// <summary>دریافت PDF چاپ نامه روی سربرگ شرکت (A4/A5) — با توکن ورود و نسخهٔ چاپ</summary>
    public async Task<byte[]> GetPrintPdfAsync(int letterId, string size, bool withCopy = true)
    {
        var url = $"api/outgoing-letters/{letterId}/print?size={Uri.EscapeDataString(size)}&withCopy={(withCopy ? "true" : "false")}";
        var req = new HttpRequestMessage(HttpMethod.Get, _api.BuildUrl(url));
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
