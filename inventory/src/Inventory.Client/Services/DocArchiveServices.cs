using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
//  سرویس‌های ماژول آرشیو اسناد و مدارک (سمت کلاینت)
// =====================================================================

public interface IDocArchiveService
{
    // پوشه‌ها
    Task<List<DocFolderDto>> GetFoldersAsync();
    Task<DocFolderDto> GetFolderAsync(int id);
    Task<int> CreateFolderAsync(DocFolderDto dto);
    Task UpdateFolderAsync(int id, DocFolderDto dto);
    Task SaveFolderPermissionsAsync(int id, DocPermissionsSaveDto dto);
    Task DeleteFolderAsync(int id);

    // مدارک
    Task<List<DocumentListDto>> GetDocumentsAsync(int? folderId = null, string? search = null,
        bool onlyExpiring = false, string status = "active", string? expiry = null);

    /// <summary>شمارش مدارک منقضی‌شده و رو به انقضا برای بج‌های درخت.</summary>
    Task<DocExpirySummaryDto> GetExpirySummaryAsync();

    /// <summary>اجرای دستی بررسی انقضا (نیازمند دسترسی مدیریت).</summary>
    Task<string> RunExpiryCheckAsync();

    /// <summary>مقایسه دو ورژن یک مدرک.</summary>
    Task<DocVersionCompareDto> CompareVersionsAsync(int documentId, int from, int to);

    /// <summary>بازگرداندن مدرک از سطل بازیافت.</summary>
    Task<string> RestoreDocumentAsync(int id);

    /// <summary>حذف قطعی یک مدرک از سطل بازیافت.</summary>
    Task<string> PurgeDocumentAsync(int id);

    /// <summary>خالی کردن کل سطل بازیافت.</summary>
    Task<string> EmptyTrashAsync();
    Task<DocumentDto> GetDocumentAsync(int id);
    Task<int> CreateDocumentAsync(DocumentDto dto);
    Task UpdateDocumentAsync(int id, DocumentDto dto);
    Task SaveDocumentPermissionsAsync(int id, DocPermissionsSaveDto dto);
    Task DeleteDocumentAsync(int id);

    // ورژن و گردش
    Task<int> CreateVersionAsync(DocVersionCreateDto dto);
    Task SetVersionActiveAsync(int versionId, bool active);
    Task ApprovalAsync(DocApprovalActionDto dto);

    // لینک
    Task LinkAsync(int documentId, int linkedDocumentId, string? note);
    /// <summary>لینک چندتایی مدارک مرتبط</summary>
    Task LinkManyAsync(DocLinkSaveDto dto);
    Task UnlinkAsync(int documentId, int linkedDocumentId);

    /// <summary>فعال/غیرفعال کردن مدرک</summary>
    Task SetDocumentActiveAsync(int id, DocSetActiveDto dto);

    // کارتابل و داده کمکی
    Task<List<DocCartableItemDto>> GetCartableAsync(bool includeDone = false);
    Task CloseTaskAsync(int id);
    Task<DocArchiveLookups> GetLookupsAsync();
}

public class DocArchiveService : IDocArchiveService
{
    private readonly IApiClient _api;
    public DocArchiveService(IApiClient api) => _api = api;

    private const string Root = "api/doc-archive";

    // ---------- پوشه‌ها ----------
    public Task<List<DocFolderDto>> GetFoldersAsync()
        => _api.GetAsync<List<DocFolderDto>>($"{Root}/folders");

    public Task<DocFolderDto> GetFolderAsync(int id)
        => _api.GetAsync<DocFolderDto>($"{Root}/folders/{id}");

    public async Task<int> CreateFolderAsync(DocFolderDto dto)
        => (await _api.PostAsync<IdResponse>($"{Root}/folders", dto)).Id;

    public Task UpdateFolderAsync(int id, DocFolderDto dto)
        => _api.PutAsync<object>($"{Root}/folders/{id}", dto);

    public Task SaveFolderPermissionsAsync(int id, DocPermissionsSaveDto dto)
        => _api.PutAsync<object>($"{Root}/folders/{id}/permissions", dto);

    public Task DeleteFolderAsync(int id)
        => _api.DeleteAsync($"{Root}/folders/{id}");

    // ---------- مدارک ----------
    public Task<List<DocumentListDto>> GetDocumentsAsync(int? folderId = null, string? search = null,
        bool onlyExpiring = false, string status = "active", string? expiry = null)
    {
        var url = $"{Root}/documents?search={Uri.EscapeDataString(search ?? "")}&status={status}";
        if (folderId is > 0) url += $"&folderId={folderId}";
        if (onlyExpiring) url += "&onlyExpiring=true";
        if (!string.IsNullOrEmpty(expiry)) url += $"&expiry={expiry}";
        return _api.GetAsync<List<DocumentListDto>>(url);
    }

    public Task<DocExpirySummaryDto> GetExpirySummaryAsync()
        => _api.GetAsync<DocExpirySummaryDto>("api/doc-archive/expiry-summary");

    public async Task<string> RunExpiryCheckAsync()
    {
        var r = await _api.PostAsync<DocExpiryRunResultDto>("api/doc-archive/expiry-check", new { });
        return r?.Message ?? "انجام شد.";
    }

    public Task<DocVersionCompareDto> CompareVersionsAsync(int documentId, int from, int to) =>
        _api.GetAsync<DocVersionCompareDto>($"{Root}/documents/{documentId}/compare?from={from}&to={to}");

    public async Task<string> RestoreDocumentAsync(int id)
    {
        var r = await _api.PutAsync<DocMessageDto>($"{Root}/documents/{id}/restore", new { });
        return r?.Message ?? "مدرک بازگردانده شد.";
    }

    public async Task<string> PurgeDocumentAsync(int id)
    {
        await _api.DeleteAsync($"{Root}/documents/{id}/purge");
        return "مدرک برای همیشه حذف شد.";
    }

    public async Task<string> EmptyTrashAsync()
    {
        await _api.DeleteAsync("api/doc-archive/trash");
        return "سطل بازیافت خالی شد.";
    }

    public Task<DocumentDto> GetDocumentAsync(int id)
        => _api.GetAsync<DocumentDto>($"{Root}/documents/{id}");

    public async Task<int> CreateDocumentAsync(DocumentDto dto)
        => (await _api.PostAsync<IdResponse>($"{Root}/documents", dto)).Id;

    public Task UpdateDocumentAsync(int id, DocumentDto dto)
        => _api.PutAsync<object>($"{Root}/documents/{id}", dto);

    public Task SaveDocumentPermissionsAsync(int id, DocPermissionsSaveDto dto)
        => _api.PutAsync<object>($"{Root}/documents/{id}/permissions", dto);

    public Task DeleteDocumentAsync(int id)
        => _api.DeleteAsync($"{Root}/documents/{id}");

    // ---------- ورژن و گردش ----------
    public async Task<int> CreateVersionAsync(DocVersionCreateDto dto)
        => (await _api.PostAsync<IdResponse>($"{Root}/documents/versions", dto)).Id;

    public Task SetVersionActiveAsync(int versionId, bool active)
        => _api.PutAsync<object>($"{Root}/documents/versions/{versionId}/active?active={active.ToString().ToLower()}");

    public Task ApprovalAsync(DocApprovalActionDto dto)
        => _api.PostAsync<object>($"{Root}/documents/approval", dto);

    // ---------- لینک ----------
    public Task LinkAsync(int documentId, int linkedDocumentId, string? note)
        => _api.PostAsync<object>($"{Root}/documents/links",
            new { documentId, linkedDocumentId, note });

    public Task LinkManyAsync(DocLinkSaveDto dto)
        => _api.PostAsync<object>($"{Root}/documents/links/many", dto);

    public Task SetDocumentActiveAsync(int id, DocSetActiveDto dto)
        => _api.PutAsync<object>($"{Root}/documents/{id}/active", dto);

    public Task UnlinkAsync(int documentId, int linkedDocumentId)
        => _api.DeleteAsync($"{Root}/documents/links?documentId={documentId}&linkedDocumentId={linkedDocumentId}");

    // ---------- کارتابل ----------
    public Task<List<DocCartableItemDto>> GetCartableAsync(bool includeDone = false)
        => _api.GetAsync<List<DocCartableItemDto>>($"{Root}/cartable?includeDone={includeDone.ToString().ToLower()}");

    public Task CloseTaskAsync(int id)
        => _api.PutAsync<object>($"{Root}/cartable/{id}/done");

    public Task<DocArchiveLookups> GetLookupsAsync()
        => _api.GetAsync<DocArchiveLookups>($"{Root}/lookups");

    private class IdResponse { public int Id { get; set; } }
}
