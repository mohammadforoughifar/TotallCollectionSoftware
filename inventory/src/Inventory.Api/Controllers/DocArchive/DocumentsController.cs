using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>
/// ================== آرشیو اسناد — مدارک، ورژن‌ها، گردش و لینک ==================
/// </summary>
[Route("api/doc-archive/documents")]
public class DocumentsController : RbacControllerBase
{
    private readonly IDocAccessService _access;
    private readonly IDocumentService _svc;

    public DocumentsController(AppDbContext db, IDocAccessService access, IDocumentService svc) : base(db)
    { _access = access; _svc = svc; }

    private const string Mod = "DocArchive";
    private Task<bool> IsManagerAsync() => HasAsync(Mod, "Manage");

    // ---------------------------- لیست ----------------------------

    [HttpGet]
    /// <param name="status">وضعیت مدرک: active (پیش‌فرض) | inactive | all</param>
    public async Task<IActionResult> List(int? folderId = null, string? search = null,
        bool onlyExpiring = false, string status = "active")
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var folderMap = await _access.FolderAccessMapAsync(MyUserId, manager);

        var q = Db.Documents.AsNoTracking().Where(d => !d.IsDeleted);

        // فیلتر وضعیت — «مدارک غیرفعال» زیرمنوی جداگانه دارد
        q = status?.ToLowerInvariant() switch
        {
            "inactive" => q.Where(d => !d.IsActive),
            "all" => q,
            _ => q.Where(d => d.IsActive)
        };

        if (folderId is > 0) q = q.Where(d => d.FolderId == folderId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(d => d.Title.Contains(s) || d.Code.Contains(s)
                          || (d.CustomerCode ?? "").Contains(s) || (d.Description ?? "").Contains(s));
        }

        var docs = await q.OrderByDescending(d => d.Id).ToListAsync();
        var ids = docs.Select(d => d.Id).ToList();

        var folders = await Db.DocFolders.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
        var directPerms = await Db.DocumentPermissions.AsNoTracking()
            .Where(p => ids.Contains(p.DocumentId) && p.UserId == MyUserId).ToListAsync();
        var versions = await Db.DocumentVersions.AsNoTracking().Where(v => ids.Contains(v.DocumentId)).ToListAsync();
        var links = await Db.DocumentLinks.AsNoTracking().Where(l => ids.Contains(l.DocumentId))
            .GroupBy(l => l.DocumentId).Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C);

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

            if (onlyExpiring && !(d.ExpireDate.HasValue && d.ExpireDate.Value.Date <= DateTime.Today.AddDays(30)))
                continue;

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
                AllowMultipleActiveVersions = d.AllowMultipleActiveVersions,
                IsPublic = d.IsPublic,
                CreatedByName = d.CreatedByName,
                CreatedAt = d.CreatedAt,
                IsActive = d.IsActive,
                DeactivatedAt = d.DeactivatedAt,
                DeactivatedByName = d.DeactivatedByName,
                DeactivateReason = d.DeactivateReason,
                VersionCount = vs.Count,
                ActiveVersionNo = activeV?.VersionNo ?? 0,
                LastVersionStatus = (DocVersionStatusDto)(int)(last?.Status ?? DocVersionStatus.Draft),
                LinkCount = links.TryGetValue(d.Id, out var lc) ? lc : 0,
                MyLevel = (DocAccessLevelDto)(int)level,
                MyCanDownload = dl
            });
        }

        return Ok(result);
    }

    // ---------------------------- جزئیات ----------------------------

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (level, dl) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (level == DocAccessLevel.None) return StatusCode(403, new { message = "به این مدرک دسترسی ندارید." });

        var d = await Db.Documents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (d == null) return NotFound(new { message = "مدرک یافت نشد." });

        var dto = new DocumentDto
        {
            Id = d.Id,
            FolderId = d.FolderId,
            FolderName = await Db.DocFolders.Where(x => x.Id == d.FolderId).Select(x => x.Name).FirstOrDefaultAsync() ?? "",
            Title = d.Title,
            Code = d.Code,
            CustomerCode = d.CustomerCode,
            Description = d.Description,
            ExpireDate = d.ExpireDate,
            AllowMultipleActiveVersions = d.AllowMultipleActiveVersions,
            IsPublic = d.IsPublic,
            PublicCanDownload = d.PublicCanDownload,
            CreatedByName = d.CreatedByName,
            CreatedAt = d.CreatedAt,
            IsActive = d.IsActive,
            DeactivatedAt = d.DeactivatedAt,
            DeactivatedByName = d.DeactivatedByName,
            DeactivateReason = d.DeactivateReason,
            MyLevel = (DocAccessLevelDto)(int)level,
            MyCanDownload = dl
        };

        // سطح «مشاهده» فقط سرفصل را می‌بیند
        if (level == DocAccessLevel.View) return Ok(dto);

        var approversAll = await Db.DocumentApprovers.AsNoTracking().Where(a => a.DocumentId == id).ToListAsync();
        var users = await Db.Users.AsNoTracking()
            .ToDictionaryAsync(u => u.Id, u => new { Name = string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : (u.FirstName + " " + u.LastName).Trim(), u.PhotoPath });

        DocApproverDto ToDto(DocumentApprover a) => new()
        {
            Id = a.Id,
            UserId = a.UserId,
            UserName = users.TryGetValue(a.UserId, out var u) ? u.Name : a.UserName,
            AvatarUrl = users.TryGetValue(a.UserId, out var u2) && u2.PhotoPath != null ? "/uploads/" + u2.PhotoPath : null,
            Order = a.Order,
            Status = a.Status,
            Comment = a.Comment,
            ActedAt = a.ActedAt
        };

        dto.Approvers = approversAll.Where(a => a.VersionId == null).OrderBy(a => a.Order).Select(ToDto).ToList();

        var attCounts = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == "DocVersion")
            .GroupBy(a => a.RefId).Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C);

        dto.Versions = (await Db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == id).OrderByDescending(v => v.VersionNo).ToListAsync())
            .Select(v => new DocVersionDto
            {
                Id = v.Id,
                DocumentId = v.DocumentId,
                VersionNo = v.VersionNo,
                Title = v.Title,
                ChangeNote = v.ChangeNote,
                ExpireDate = v.ExpireDate,
                Status = (DocVersionStatusDto)(int)v.Status,
                IsActive = v.IsActive,
                IsFrozen = v.IsFrozen,
                ActivateOnApprove = v.ActivateOnApprove,
                CreatedByName = v.CreatedByName,
                CreatedAt = v.CreatedAt,
                ApprovedAt = v.ApprovedAt,
                AttachmentCount = attCounts.TryGetValue(v.Id, out var c) ? c : 0,
                Approvers = approversAll.Where(a => a.VersionId == v.Id).OrderBy(a => a.Order).Select(ToDto).ToList(),
                IsMyTurn = approversAll.Any(a => a.VersionId == v.Id && a.UserId == MyUserId && a.Status == 0)
                           && v.Status == DocVersionStatus.InReview
            }).ToList();

        dto.Links = await Db.DocumentLinks.AsNoTracking().Where(l => l.DocumentId == id)
            .Join(Db.Documents, l => l.LinkedDocumentId, x => x.Id, (l, x) => new DocLinkDto
            {
                Id = l.Id,
                LinkedDocumentId = x.Id,
                Code = x.Code,
                CustomerCode = x.CustomerCode,
                Title = x.Title,
                Note = l.Note,
                IsActive = x.IsActive,
                ActiveVersionNo = Db.DocumentVersions.Where(v => v.DocumentId == x.Id && v.IsActive)
                                    .Select(v => v.VersionNo).OrderByDescending(n => n).FirstOrDefault()
            }).ToListAsync();

        if (level >= DocAccessLevel.Full)
        {
            dto.Permissions = await Db.DocumentPermissions.AsNoTracking().Where(p => p.DocumentId == id)
                .Select(p => new DocPermissionDto
                {
                    Id = p.Id,
                    UserId = p.UserId,
                    UserName = "",
                    Level = (DocAccessLevelDto)(int)p.Level,
                    CanDownload = p.CanDownload
                }).ToListAsync();
            foreach (var p in dto.Permissions)
                if (users.TryGetValue(p.UserId, out var u)) p.UserName = u.Name;
        }

        dto.Logs = await Db.DocumentLogs.AsNoTracking().Where(l => l.DocumentId == id)
            .OrderByDescending(l => l.Id).Take(100)
            .Select(l => new DocLogDto
            {
                Id = l.Id, Action = l.Action, Detail = l.Detail,
                UserName = l.UserName, CreatedAt = l.CreatedAt, VersionId = l.VersionId
            }).ToListAsync();

        return Ok(dto);
    }

    // ---------------------------- ثبت / ویرایش ----------------------------

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DocumentDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.FolderAccessAsync(MyUserId, manager, dto.FolderId);
        if (lvl < DocAccessLevel.Write)
            return StatusCode(403, new { message = "برای ثبت مدرک در این پوشه، دسترسی نوشتن لازم است." });

        var id = await _svc.CreateDocumentAsync(dto, MyUserId, MyUsername);
        return Ok(new { id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] DocumentDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Write)
            return StatusCode(403, new { message = "ویرایش مدرک نیازمند دسترسی نوشتن است." });

        await _svc.UpdateDocumentAsync(id, dto, MyUserId, MyUsername);
        return Ok();
    }

    [HttpPut("{id:int}/permissions")]
    public async Task<IActionResult> SavePermissions(int id, [FromBody] DocPermissionsSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Full)
            return StatusCode(403, new { message = "مدیریت دسترسی نیازمند دسترسی کامل است." });

        var doc = await Db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });

        doc.IsPublic = dto.IsPublic;
        doc.PublicCanDownload = dto.PublicCanDownload;

        var old = await Db.DocumentPermissions.Where(p => p.DocumentId == id).ToListAsync();
        Db.DocumentPermissions.RemoveRange(old);
        foreach (var p in dto.Items.Where(x => x.UserId > 0).DistinctBy(x => x.UserId))
            Db.DocumentPermissions.Add(new DocumentPermission
            {
                DocumentId = id, UserId = p.UserId,
                Level = (DocAccessLevel)(int)p.Level, CanDownload = p.CanDownload
            });

        await Db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Full) return StatusCode(403, new { message = "حذف مدرک نیازمند دسترسی کامل است." });

        var doc = await Db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });
        if (await Db.DocumentVersions.AnyAsync(v => v.DocumentId == id && v.Status == DocVersionStatus.InReview))
            return BadRequest(new { message = "ورژنی از این مدرک در گردش تایید است؛ حذف ممکن نیست." });

        doc.IsDeleted = true;
        Db.DocumentLogs.Add(new DocumentLog { DocumentId = id, Action = "Delete", Detail = "مدرک حذف شد.", UserId = MyUserId, UserName = MyUsername });
        await Db.SaveChangesAsync();
        return Ok();
    }

    // ---------------------------- ورژن ----------------------------

    [HttpPost("versions")]
    public async Task<IActionResult> CreateVersion([FromBody] DocVersionCreateDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, dto.DocumentId);
        if (lvl < DocAccessLevel.Write)
            return StatusCode(403, new { message = "ثبت ورژن نیازمند دسترسی نوشتن است." });

        var id = await _svc.CreateVersionAsync(dto, MyUserId, MyUsername);
        return Ok(new { id });
    }

    [HttpPut("versions/{versionId:int}/active")]
    public async Task<IActionResult> SetActive(int versionId, [FromQuery] bool active)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var ver = await Db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId);
        if (ver == null) return NotFound(new { message = "ورژن یافت نشد." });

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, ver.DocumentId);
        if (lvl < DocAccessLevel.Full) return StatusCode(403, new { message = "تغییر وضعیت ورژن نیازمند دسترسی کامل است." });

        await _svc.SetVersionActiveAsync(versionId, active, MyUserId, MyUsername);
        return Ok();
    }

    // ---------------------------- گردش تایید ----------------------------

    [HttpPost("approval")]
    public async Task<IActionResult> Approval([FromBody] DocApprovalActionDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        await _svc.ActApprovalAsync(dto, MyUserId, MyUsername);
        return Ok();
    }

    // ---------------------------- لینک مدارک ----------------------------

    public class LinkRequest { public int DocumentId { get; set; } public int LinkedDocumentId { get; set; } public string? Note { get; set; } }

    [HttpPost("links")]
    public async Task<IActionResult> Link([FromBody] LinkRequest req)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, req.DocumentId);
        if (lvl < DocAccessLevel.Write) return StatusCode(403, new { message = "لینک کردن نیازمند دسترسی نوشتن است." });

        await _svc.LinkAsync(req.DocumentId, req.LinkedDocumentId, req.Note, MyUserId, MyUsername);
        return Ok();
    }

    /// <summary>لینک چندتایی مدارک مرتبط (کمبوی چندانتخابی سرچ‌دار در فرم).</summary>
    [HttpPost("links/many")]
    public async Task<IActionResult> LinkMany([FromBody] DocLinkSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, dto.DocumentId);
        if (lvl < DocAccessLevel.Write) return StatusCode(403, new { message = "لینک کردن نیازمند دسترسی نوشتن است." });

        await _svc.LinkManyAsync(dto, MyUserId, MyUsername);
        return Ok();
    }

    // ---------------------------- فعال / غیرفعال کردن مدرک ----------------------------

    /// <summary>تغییر وضعیت مدرک به فعال/غیرفعال — نیازمند دسترسی کامل.</summary>
    [HttpPut("{id:int}/active")]
    public async Task<IActionResult> SetDocumentActive(int id, [FromBody] DocSetActiveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Full)
            return StatusCode(403, new { message = "تغییر وضعیت مدرک نیازمند دسترسی کامل است." });

        await _svc.SetDocumentActiveAsync(id, dto, MyUserId, MyUsername);
        return Ok();
    }

    [HttpDelete("links")]
    public async Task<IActionResult> Unlink([FromQuery] int documentId, [FromQuery] int linkedDocumentId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, documentId);
        if (lvl < DocAccessLevel.Write) return StatusCode(403, new { message = "حذف لینک نیازمند دسترسی نوشتن است." });

        await _svc.UnlinkAsync(documentId, linkedDocumentId, MyUserId);
        return Ok();
    }

    // ---------------------------- کارتابل آرشیو ----------------------------

    [HttpGet("/api/doc-archive/cartable")]
    public async Task<IActionResult> Cartable(bool includeDone = false)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var q = Db.DocCartableTasks.AsNoTracking().Where(t => t.UserId == MyUserId);
        if (!includeDone) q = q.Where(t => t.Status == 0);

        var rows = await q.OrderByDescending(t => t.Id).Take(300).ToListAsync();
        var docIds = rows.Select(t => t.DocumentId)
            .Concat(rows.Where(t => t.SourceDocumentId != null).Select(t => t.SourceDocumentId!.Value))
            .Distinct().ToList();

        var docs = await Db.Documents.AsNoTracking().Where(d => docIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => new { d.Code, d.Title });
        var vers = await Db.DocumentVersions.AsNoTracking()
            .Where(v => rows.Select(r => r.VersionId).Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.VersionNo);

        var result = rows.Select(t => new DocCartableItemDto
        {
            Id = t.Id,
            Kind = t.Kind,
            DocumentId = t.DocumentId,
            VersionId = t.VersionId,
            DocumentCode = docs.TryGetValue(t.DocumentId, out var d) ? d.Code : "",
            DocumentTitle = docs.TryGetValue(t.DocumentId, out var d2) ? d2.Title : "",
            VersionNo = t.VersionId is int vid && vers.TryGetValue(vid, out var n) ? n : 0,
            SourceDocumentCode = t.SourceDocumentId is int sid && docs.TryGetValue(sid, out var sd) ? sd.Code : null,
            Title = t.Title,
            Status = t.Status,
            CreatedAt = t.CreatedAt
        }).ToList();

        return Ok(result);
    }

    /// <summary>بستن دستی یک کار کارتابل (اعلان ورژن / بررسی مدرک مرتبط).</summary>
    [HttpPut("/api/doc-archive/cartable/{id:int}/done")]
    public async Task<IActionResult> CloseTask(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var t = await Db.DocCartableTasks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == MyUserId);
        if (t == null) return NotFound(new { message = "کار یافت نشد." });
        if (t.Kind == "Approval") return BadRequest(new { message = "کار تایید باید از طریق دکمه تایید/رد بسته شود." });

        t.Status = 1; t.DoneAt = DateTime.Now;
        await Db.SaveChangesAsync();
        return Ok();
    }
}
