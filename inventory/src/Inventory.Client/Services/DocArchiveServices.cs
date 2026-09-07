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

    // جستجوی پیشرفته، تمام‌متن (Full-Text) و تگ‌ها
    Task<List<DocumentListDto>> SearchDocumentsAsync(DocSearchFilterDto filter);
    Task<List<DocTagDto>> GetTagsAsync();
    Task<DocTagDto> CreateTagAsync(DocTagSaveDto dto);
    Task UpdateTagAsync(int id, DocTagSaveDto dto);
    Task DeleteTagAsync(int id);
    Task SetDocumentTagsAsync(int docId, List<int> tagIds);
    Task<List<DocExtractedTextDto>> GetExtractedTextsAsync(int docId);
    Task<DocOcrRunResultDto> RunOcrAsync(int attachmentId);
    Task<DocReindexResultDto> ReindexAllAsync();

    // یکپارچه‌سازی با ماژول‌های سامانه ERP و دانلود درختی ZIP
    string GetFolderZipExportUrl(int? folderId = null, bool includeSubfolders = true, bool onlyActiveVersions = true, bool includeManifest = true);
    Task<List<DocEntityLinkDto>> GetLinkedDocumentsAsync(string module, int entityId);
    Task<int> AddEntityLinkAsync(DocEntityLinkSaveDto dto);
    Task RemoveEntityLinkAsync(int linkId);
    Task<List<DocEntityLookupItemDto>> SearchModuleEntitiesAsync(string module, string? q = null);
    Task<(int documentId, int linkId)> QuickCreateLinkedDocAsync(DocQuickCreateLinkedDto dto);
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

    // ---------- جستجوی پیشرفته، تمام‌متن و OCR ----------
    public Task<List<DocumentListDto>> SearchDocumentsAsync(DocSearchFilterDto filter)
        => _api.PostAsync<List<DocumentListDto>>($"{Root}/search", filter);

    public Task<List<DocTagDto>> GetTagsAsync()
        => _api.GetAsync<List<DocTagDto>>($"{Root}/tags");

    public Task<DocTagDto> CreateTagAsync(DocTagSaveDto dto)
        => _api.PostAsync<DocTagDto>($"{Root}/tags", dto);

    public Task UpdateTagAsync(int id, DocTagSaveDto dto)
        => _api.PutAsync<object>($"{Root}/tags/{id}", dto);

    public Task DeleteTagAsync(int id)
        => _api.DeleteAsync($"{Root}/tags/{id}");

    public Task SetDocumentTagsAsync(int docId, List<int> tagIds)
        => _api.PostAsync<object>($"{Root}/documents/{docId}/tags", tagIds);

    public Task<List<DocExtractedTextDto>> GetExtractedTextsAsync(int docId)
        => _api.GetAsync<List<DocExtractedTextDto>>($"{Root}/documents/{docId}/extracted-texts");

    public Task<DocOcrRunResultDto> RunOcrAsync(int attachmentId)
        => _api.PostAsync<DocOcrRunResultDto>($"{Root}/attachments/{attachmentId}/ocr", new { });

    public Task<DocReindexResultDto> ReindexAllAsync()
        => _api.PostAsync<DocReindexResultDto>($"{Root}/reindex", new { });

    // ---------- یکپارچه‌سازی ERP و دانلود درختی ZIP ----------
    public string GetFolderZipExportUrl(int? folderId = null, bool includeSubfolders = true, bool onlyActiveVersions = true, bool includeManifest = true)
    {
        if (folderId.HasValue && folderId.Value > 0)
        {
            return $"api/doc-archive/folders/{folderId.Value}/export-zip?includeSubfolders={includeSubfolders.ToString().ToLower()}&onlyActiveVersions={onlyActiveVersions.ToString().ToLower()}&includeManifest={includeManifest.ToString().ToLower()}";
        }
        return $"api/doc-archive/export-zip?includeSubfolders={includeSubfolders.ToString().ToLower()}&onlyActiveVersions={onlyActiveVersions.ToString().ToLower()}&includeManifest={includeManifest.ToString().ToLower()}";
    }

    public Task<List<DocEntityLinkDto>> GetLinkedDocumentsAsync(string module, int entityId)
        => _api.GetAsync<List<DocEntityLinkDto>>($"{Root}/entity-links/{module}/{entityId}");

    public async Task<int> AddEntityLinkAsync(DocEntityLinkSaveDto dto)
        => (await _api.PostAsync<IdResponse>($"{Root}/entity-links", dto)).Id;

    public Task RemoveEntityLinkAsync(int linkId)
        => _api.DeleteAsync($"{Root}/entity-links/{linkId}");

    public Task<List<DocEntityLookupItemDto>> SearchModuleEntitiesAsync(string module, string? q = null)
        => _api.GetAsync<List<DocEntityLookupItemDto>>($"{Root}/entity-lookups/{module}?q={Uri.EscapeDataString(q ?? "")}");

    public async Task<(int documentId, int linkId)> QuickCreateLinkedDocAsync(DocQuickCreateLinkedDto dto)
    {
        var res = await _api.PostAsync<QuickCreateResponse>($"{Root}/entity-links/quick-create", dto);
        return (res.DocumentId, res.LinkId);
    }

    private class IdResponse { public int Id { get; set; } }
    private class QuickCreateResponse { public int DocumentId { get; set; } public int LinkId { get; set; } }
}
