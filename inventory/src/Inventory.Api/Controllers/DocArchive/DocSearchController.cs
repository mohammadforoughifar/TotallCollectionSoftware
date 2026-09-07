using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>
/// ================== جستجوی پیشرفته، تمام‌متن (Full-Text) و پردازش OCR ==================
/// </summary>
[Route("api/doc-archive")]
public class DocSearchController : RbacControllerBase
{
    private readonly IDocAccessService _access;
    private readonly IDocIndexService _indexService;
    private readonly IDocTextExtractorService _extractor;

    private const string Mod = "DocArchive";

    public DocSearchController(
        AppDbContext db,
        IDocAccessService access,
        IDocIndexService indexService,
        IDocTextExtractorService extractor) : base(db)
    {
        _access = access;
        _indexService = indexService;
        _extractor = extractor;
    }

    private Task<bool> IsManagerAsync() => HasAsync(Mod, "Manage");

    // =========================================================================
    // جستجوی ترکیبی و تمام‌متن
    // =========================================================================

    [HttpPost("search")]
    public async Task<IActionResult> AdvancedSearch([FromBody] DocSearchFilterDto filter)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var folderMap = await _access.FolderAccessMapAsync(MyUserId, manager);

        var q = Db.Documents.AsNoTracking();

        // 1) فیلتر وضعیت مدرک
        q = filter.Status?.ToLowerInvariant() switch
        {
            "deleted" => q.Where(d => d.IsDeleted),
            "inactive" => q.Where(d => !d.IsDeleted && !d.IsActive),
            "all" => q.Where(d => !d.IsDeleted),
            _ => q.Where(d => !d.IsDeleted && d.IsActive)
        };

        // 2) فیلتر پوشه و زیرپوشه‌ها
        if (filter.FolderId is > 0)
        {
            if (filter.IncludeSubfolders)
            {
                var targetFolderIds = await GetFolderAndDescendantIdsAsync(filter.FolderId.Value);
                q = q.Where(d => targetFolderIds.Contains(d.FolderId));
            }
            else
            {
                q = q.Where(d => d.FolderId == filter.FolderId.Value);
            }
        }

        // 3) جستجوی هوشمند یکپارچه در عنوان، کد، توضیحات، محتوای فایل‌ها و نتایج OCR
        Dictionary<int, (string Snippet, string FileName, string SourceType)> contentMatches = new();

        var searchStr = filter.Search?.Trim();
        var contentSearchStr = filter.ContentSearch?.Trim();

        // الف) اگر جستجوی اختصاصی محتوا (ContentSearch) مقدار دارد:
        if (!string.IsNullOrWhiteSpace(contentSearchStr))
        {
            var rawTerm = contentSearchStr;
            var normTerm = _extractor.Normalize(rawTerm);
            var tokens = _extractor.Tokenize(rawTerm);

            var extractedRows = await Db.DocExtractedTexts.AsNoTracking()
                .Where(e => e.Status == "Indexed")
                .Select(e => new { e.DocumentId, e.FileName, e.SourceType, e.ExtractedText, e.NormalizedText })
                .ToListAsync();

            var matchedDocIds = new HashSet<int>();
            foreach (var r in extractedRows)
            {
                var isMatch = r.NormalizedText.Contains(normTerm, StringComparison.OrdinalIgnoreCase)
                              || r.ExtractedText.Contains(rawTerm, StringComparison.OrdinalIgnoreCase)
                              || (tokens.Count > 0 && tokens.All(t => r.NormalizedText.Contains(t, StringComparison.OrdinalIgnoreCase)));

                if (isMatch)
                {
                    matchedDocIds.Add(r.DocumentId);
                    if (!contentMatches.ContainsKey(r.DocumentId))
                    {
                        var snippet = _extractor.MakeSnippet(r.ExtractedText, rawTerm) ?? "";
                        contentMatches[r.DocumentId] = (snippet, r.FileName, r.SourceType);
                    }
                }
            }

            q = q.Where(d => matchedDocIds.Contains(d.Id));
        }

        // ب) اگر جستجوی عمومی (Search) در کادر جستجوی اصلی وارد شده است:
        if (!string.IsNullOrWhiteSpace(searchStr))
        {
            var rawTerm = searchStr;
            var normTerm = _extractor.Normalize(rawTerm);
            var tokens = _extractor.Tokenize(rawTerm);

            // جستجو در محتوای متنی فایل‌ها و OCR
            var extractedRows = await Db.DocExtractedTexts.AsNoTracking()
                .Where(e => e.Status == "Indexed")
                .Select(e => new { e.DocumentId, e.FileName, e.SourceType, e.ExtractedText, e.NormalizedText })
                .ToListAsync();

            var matchedDocIdsFromContent = new HashSet<int>();
            foreach (var r in extractedRows)
            {
                var isMatch = r.NormalizedText.Contains(normTerm, StringComparison.OrdinalIgnoreCase)
                              || r.ExtractedText.Contains(rawTerm, StringComparison.OrdinalIgnoreCase)
                              || (tokens.Count > 0 && tokens.All(t => r.NormalizedText.Contains(t, StringComparison.OrdinalIgnoreCase)));

                if (isMatch)
                {
                    matchedDocIdsFromContent.Add(r.DocumentId);
                    if (!contentMatches.ContainsKey(r.DocumentId))
                    {
                        var snippet = _extractor.MakeSnippet(r.ExtractedText, rawTerm) ?? "";
                        contentMatches[r.DocumentId] = (snippet, r.FileName, r.SourceType);
                    }
                }
            }

            // ترکیب تطابق در متادیتا (عنوان، کد، مشتری، توضیحات) یا در محتوای فایل/OCR
            q = q.Where(d => d.Title.Contains(rawTerm)
                          || d.Code.Contains(rawTerm)
                          || (d.CustomerCode != null && d.CustomerCode.Contains(rawTerm))
                          || (d.Description != null && d.Description.Contains(rawTerm))
                          || matchedDocIdsFromContent.Contains(d.Id));
        }

        // 10) فیلتر بر اساس نوع فایل (FileTypes: pdf, word, excel, image, text)
        if (filter.FileTypes is { Count: > 0 })
        {
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ft in filter.FileTypes)
            {
                switch (ft.ToLowerInvariant())
                {
                    case "pdf": exts.Add(".pdf"); break;
                    case "word": exts.Add(".docx"); exts.Add(".doc"); break;
                    case "excel": exts.Add(".xlsx"); exts.Add(".xls"); exts.Add(".csv"); break;
                    case "image": exts.Add(".png"); exts.Add(".jpg"); exts.Add(".jpeg"); exts.Add(".webp"); exts.Add(".bmp"); break;
                    case "text": exts.Add(".txt"); exts.Add(".json"); exts.Add(".xml"); exts.Add(".md"); break;
                }
            }

            if (exts.Count > 0)
            {
                var allDocAtts = await (from att in Db.AppAttachments.AsNoTracking()
                                        where att.Module == "DocVersion"
                                        join v in Db.DocumentVersions.AsNoTracking() on att.RefId equals v.Id
                                        select new { att.FileName, v.DocumentId }).ToListAsync();

                var docIdsWithFileType = allDocAtts
                    .Where(a => exts.Any(ext => a.FileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    .Select(a => a.DocumentId)
                    .Distinct()
                    .ToList();

                q = q.Where(d => docIdsWithFileType.Contains(d.Id));
            }
        }

        // 11) فیلتر بر اساس ارتباط با ماژول ERP
        if (!string.IsNullOrWhiteSpace(filter.LinkedModule))
        {
            var mod = filter.LinkedModule.Trim().ToLowerInvariant();
            var eq = Db.DocEntityLinks.AsNoTracking().Where(l => l.Module.ToLower() == mod);
            if (filter.LinkedEntityId is > 0)
            {
                eq = eq.Where(l => l.EntityId == filter.LinkedEntityId.Value);
            }
            var linkedDocIds = await eq.Select(l => l.DocumentId).Distinct().ToListAsync();
            q = q.Where(d => linkedDocIds.Contains(d.Id));
        }

        var docs = await q.OrderByDescending(d => d.Id).Take(300).ToListAsync();
        var ids = docs.Select(d => d.Id).ToList();

        var folders = await Db.DocFolders.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
        var directPerms = await Db.DocumentPermissions.AsNoTracking()
            .Where(p => ids.Contains(p.DocumentId) && p.UserId == MyUserId).ToListAsync();
        var versions = await Db.DocumentVersions.AsNoTracking().Where(v => ids.Contains(v.DocumentId)).ToListAsync();
        var links = await Db.DocumentLinks.AsNoTracking().Where(l => ids.Contains(l.DocumentId))
            .GroupBy(l => l.DocumentId).Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C);

        // Fetch tags for these documents
        var allDocTags = await (from dt in Db.DocumentTags.AsNoTracking()
                                where ids.Contains(dt.DocumentId)
                                join t in Db.DocTags.AsNoTracking() on dt.TagId equals t.Id
                                select new { dt.DocumentId, Tag = new DocTagDto
                                {
                                    Id = t.Id, Name = t.Name, Color = t.Color, Description = t.Description, CreatedAt = t.CreatedAt
                                }}).ToListAsync();
        var docTagMap = allDocTags.GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Tag).ToList());

        // Check which docs have indexed content
        var indexedDocIds = (await Db.DocExtractedTexts.AsNoTracking()
            .Where(e => ids.Contains(e.DocumentId) && e.Status == "Indexed")
            .Select(e => e.DocumentId)
            .Distinct().ToListAsync()).ToHashSet();

        var entityLinks = await Db.DocEntityLinks.AsNoTracking()
            .Where(l => ids.Contains(l.DocumentId))
            .ToListAsync();
        var entityLinkMap = entityLinks.GroupBy(l => l.DocumentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<DocumentListDto>();
        foreach (var d in docs)
        {
            var level = DocAccessLevel.None; var dl = false;
            if (manager) { level = DocAccessLevel.Full; dl = true; }
            else
            {
                if (d.CreatedByUserId == MyUserId) { level = DocAccessLevel.Full; dl = true; }
                if (d.IsPublic && level < DocAccessLevel.Read) { level = DocAccessLevel.Read; dl |= d.PublicCanDownload; }
                foreach (var p in directPerms.Where(p => p.DocumentId == d.Id))
                { if (p.Level > level) level = p.Level; dl |= p.CanDownload; }
                if (folderMap.TryGetValue(d.FolderId, out var fa))
                { if (fa.Level > level) level = fa.Level; dl |= fa.Download; }
            }
            if (level == DocAccessLevel.None) continue;

            var vs = versions.Where(v => v.DocumentId == d.Id).ToList();
            var last = vs.OrderByDescending(v => v.VersionNo).FirstOrDefault();
            var activeV = vs.Where(v => v.IsActive).OrderByDescending(v => v.VersionNo).FirstOrDefault();
            var expired = d.ExpireDate.HasValue && d.ExpireDate.Value.Date < DateTime.Today;
            int? daysLeft = d.ExpireDate.HasValue ? (d.ExpireDate.Value.Date - DateTime.Today).Days : null;
            var expiringSoon = daysLeft is >= 0 && daysLeft <= 60;

            // ExpiryStatus filter
            if (filter.ExpiryStatus == "expiring" && !expiringSoon) continue;
            if (filter.ExpiryStatus == "expired" && !expired) continue;
            if (filter.ExpiryStatus == "valid" && (expired || !d.ExpireDate.HasValue)) continue;

            string? snippet = null;
            string? matchFileName = null;
            string? matchSource = null;
            if (contentMatches.TryGetValue(d.Id, out var matchInfo))
            {
                snippet = matchInfo.Snippet;
                matchFileName = matchInfo.FileName;
                matchSource = matchInfo.SourceType;
            }

            result.Add(new DocumentListDto
            {
                Id = d.Id,
                FolderId = d.FolderId,
                FolderName = folders.TryGetValue(d.FolderId, out var fn) ? fn : "",
                Title = d.Title,
                Code = d.Code,
                CustomerCode = d.CustomerCode,
                ExpireDate = d.ExpireDate,
                IsExpired = expired,
                DaysToExpire = daysLeft,
                IsExpiringSoon = expiringSoon,
                AllowMultipleActiveVersions = d.AllowMultipleActiveVersions,
                IsPublic = d.IsPublic,
                CreatedByName = d.CreatedByName,
                CreatedAt = d.CreatedAt,
                IsActive = d.IsActive,
                DeactivatedAt = d.DeactivatedAt,
                DeactivatedByName = d.DeactivatedByName,
                DeactivateReason = d.DeactivateReason,
                IsDeleted = d.IsDeleted,
                DeletedAt = d.DeletedAt,
                DeletedByName = d.DeletedByName,
                VersionCount = vs.Count,
                ActiveVersionNo = activeV?.VersionNo ?? 0,
                LastVersionStatus = (DocVersionStatusDto)(int)(last?.Status ?? DocVersionStatus.Draft),
                LinkCount = links.TryGetValue(d.Id, out var lc) ? lc : 0,
                MyLevel = (DocAccessLevelDto)(int)level,
                MyCanDownload = dl,
                Tags = docTagMap.TryGetValue(d.Id, out var tlist) ? tlist : new(),
                HasIndexedContent = indexedDocIds.Contains(d.Id),
                ContentSnippet = snippet,
                MatchedAttachmentFileName = matchFileName,
                MatchedSourceType = matchSource,
                EntityLinkCount = entityLinkMap.TryGetValue(d.Id, out var elist) ? elist.Count : 0,
                LinkedModules = entityLinkMap.TryGetValue(d.Id, out var elist2) ? elist2.Select(x => x.Module).Distinct().ToList() : new()
            });
        }

        return Ok(result);
    }

    // =========================================================================
    // مشاهده متن‌های استخراج‌شده و نتایج OCR یک مدرک
    // =========================================================================

    [HttpGet("documents/{id:int}/extracted-texts")]
    public async Task<IActionResult> GetExtractedTexts(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Read)
            return StatusCode(403, new { message = "مشاهده محتوای استخراج‌شده نیازمند دسترسی خواندن است." });

        var texts = await Db.DocExtractedTexts.AsNoTracking()
            .Where(e => e.DocumentId == id)
            .OrderByDescending(e => e.IndexedAt)
            .Select(e => new DocExtractedTextDto
            {
                Id = e.Id,
                DocumentId = e.DocumentId,
                VersionId = e.VersionId,
                AttachmentId = e.AttachmentId,
                FileName = e.FileName,
                ContentType = e.ContentType,
                SourceType = e.SourceType,
                ExtractedText = e.ExtractedText,
                Status = e.Status,
                ErrorMessage = e.ErrorMessage,
                CharacterCount = e.CharacterCount,
                IndexedAt = e.IndexedAt
            })
            .ToListAsync();

        return Ok(texts);
    }

    // =========================================================================
    // اجرای دستی OCR و بازایندکس یک پیوست مشخص
    // =========================================================================

    [HttpPost("attachments/{attachmentId:int}/ocr")]
    public async Task<IActionResult> RunOcrOnAttachment(int attachmentId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var att = await Db.AppAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.Module == "DocVersion");
        if (att == null) return NotFound(new { message = "پیوست یافت نشد." });

        var version = await Db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == att.RefId);
        if (version == null) return NotFound(new { message = "ورژن مدرک یافت نشد." });

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, version.DocumentId);
        if (lvl < DocAccessLevel.Write)
            return StatusCode(403, new { message = "اجرای OCR نیازمند دسترسی نوشتن است." });

        var res = await _indexService.IndexAttachmentAsync(attachmentId, force: true);

        return Ok(new DocOcrRunResultDto
        {
            Success = res.Success,
            Message = res.Success
                ? $"استخراج متن و OCR با موفقیت انجام شد ({res.CharacterCount} کاراکتر)."
                : $"خطا در استخراج متن: {res.ErrorMessage}",
            CharacterCount = res.CharacterCount,
            SourceType = res.SourceType,
            ExtractedSnippet = res.ExtractedText.Length > 200 ? res.ExtractedText[..200] + "…" : res.ExtractedText
        });
    }

    // =========================================================================
    // بازایندکس کلیه اسناد (فقط مدیر آرشیو)
    // =========================================================================

    [HttpPost("reindex")]
    public async Task<IActionResult> ReindexAll()
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;

        var result = await _indexService.ReindexAllAsync();
        return Ok(result);
    }

    // =========================================================================
    // متد کمکی دریافت شناسه پوشه و همه زیرپوشه‌ها
    // =========================================================================

    private async Task<HashSet<int>> GetFolderAndDescendantIdsAsync(int rootFolderId)
    {
        var all = await Db.DocFolders.AsNoTracking().Select(f => new { f.Id, f.ParentId }).ToListAsync();
        var set = new HashSet<int> { rootFolderId };

        void Collect(int pid)
        {
            foreach (var c in all.Where(x => x.ParentId == pid))
            {
                if (set.Add(c.Id)) Collect(c.Id);
            }
        }
        Collect(rootFolderId);
        return set;
    }
}
