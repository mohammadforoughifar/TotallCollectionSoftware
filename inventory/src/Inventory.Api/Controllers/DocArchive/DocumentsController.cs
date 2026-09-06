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
    private readonly DocExpiryWatcher _expiry;

    public DocumentsController(AppDbContext db, IDocAccessService access, IDocumentService svc,
        DocExpiryWatcher expiry) : base(db)
    { _access = access; _svc = svc; _expiry = expiry; }

    private const string Mod = "DocArchive";
    private Task<bool> IsManagerAsync() => HasAsync(Mod, "Manage");

    // ---------------------------- لیست ----------------------------

    [HttpGet]
    /// <param name="status">وضعیت مدرک: active (پیش‌فرض) | inactive | all</param>
    public async Task<IActionResult> List(int? folderId = null, string? search = null,
        bool onlyExpiring = false, string status = "active",
        string? expiry = null, int expiringDays = 60)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var folderMap = await _access.FolderAccessMapAsync(MyUserId, manager);

        var q = Db.Documents.AsNoTracking();

        // فیلتر وضعیت — «مدارک غیرفعال» و «سطل بازیافت» زیرمنوی جداگانه دارند
        q = status?.ToLowerInvariant() switch
        {
            "deleted" => q.Where(d => d.IsDeleted),
            "inactive" => q.Where(d => !d.IsDeleted && !d.IsActive),
            "all" => q.Where(d => !d.IsDeleted),
            _ => q.Where(d => !d.IsDeleted && d.IsActive)
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
            int? daysLeft = d.ExpireDate.HasValue ? (d.ExpireDate.Value.Date - DateTime.Today).Days : null;
            var expiringSoon = daysLeft is >= 0 && daysLeft <= expiringDays;

            // فیلتر انقضا: expiring = رو به انقضا | expired = منقضی‌شده | all = هر دو
            var expiryFilter = expiry?.ToLowerInvariant();
            if (onlyExpiring && expiryFilter is null) expiryFilter = "expiring"; // سازگاری با پارامتر قدیمی
            if (expiryFilter is "expiring" && !expiringSoon) continue;
            if (expiryFilter is "expired" && !expired) continue;
            if (expiryFilter is "all" && !(expired || expiringSoon)) continue;

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
        var newItems = dto.Items.Where(x => x.UserId > 0).DistinctBy(x => x.UserId).ToList();
        foreach (var p in newItems)
            Db.DocumentPermissions.Add(new DocumentPermission
            {
                DocumentId = id, UserId = p.UserId,
                Level = (DocAccessLevel)(int)p.Level, CanDownload = p.CanDownload
            });

        Db.DocumentLogs.Add(new DocumentLog
        {
            DocumentId = id,
            Action = "Permissions",
            Detail = $"دسترسی‌ها به‌روزرسانی شد: {newItems.Count} کاربر" +
                     (dto.IsPublic ? " — نمایش برای همه: فعال" : "") +
                     (old.Count != newItems.Count ? $" (قبلاً {old.Count} کاربر)" : ""),
            UserId = MyUserId, UserName = MyUsername
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
        doc.DeletedAt = DateTime.Now;
        doc.DeletedByName = MyUsername;

        // کارهای باز کارتابل این مدرک بسته می‌شوند
        var openTasks = await Db.DocCartableTasks.Where(t => t.DocumentId == id && t.Status == 0).ToListAsync();
        foreach (var t in openTasks) { t.Status = 1; t.DoneAt = DateTime.Now; }

        Db.DocumentLogs.Add(new DocumentLog { DocumentId = id, Action = "Delete", Detail = "مدرک به سطل بازیافت منتقل شد.", UserId = MyUserId, UserName = MyUsername });
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

    // ---------------------------- مقایسه دو ورژن ----------------------------

    /// <summary>مقایسه دو ورژن یک مدرک — تفاوت فیلدها، نفرات گردش، پیوست‌ها و رویدادهای بین آن دو.</summary>
    [HttpGet("{id:int}/compare")]
    public async Task<IActionResult> Compare(int id, [FromQuery] int from, [FromQuery] int to)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Read)
            return StatusCode(403, new { message = "مشاهده مقایسه نیازمند دسترسی خواندن است." });

        var doc = await Db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });

        if (from == to)
            return BadRequest(new { message = "دو ورژن متفاوت انتخاب کنید." });

        var vs = await Db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == id && (v.Id == from || v.Id == to)).ToListAsync();
        var vL = vs.FirstOrDefault(v => v.Id == from);
        var vR = vs.FirstOrDefault(v => v.Id == to);
        if (vL == null || vR == null)
            return NotFound(new { message = "یکی از ورژن‌های انتخاب‌شده متعلق به این مدرک نیست." });

        // همیشه قدیمی‌تر سمت راست (مبدأ) و جدیدتر سمت چپ نمایش داده می‌شود
        if (vL.VersionNo > vR.VersionNo) (vL, vR) = (vR, vL);

        var apprs = await Db.DocumentApprovers.AsNoTracking()
            .Where(a => a.DocumentId == id && (a.VersionId == vL.Id || a.VersionId == vR.Id)).ToListAsync();
        var atts = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == "DocVersion" && (a.RefId == vL.Id || a.RefId == vR.Id))
            .Select(a => new { a.RefId, a.FileName }).ToListAsync();

        var res = new DocVersionCompareDto
        {
            DocumentId = id,
            DocumentCode = doc.Code,
            DocumentTitle = doc.Title,
            Left = MapVersion(vL, apprs, atts.Count(a => a.RefId == vL.Id)),
            Right = MapVersion(vR, apprs, atts.Count(a => a.RefId == vR.Id))
        };

        // ---------- فیلدهای اصلی ----------
        void Row(List<DocVersionDiffRow> list, string field, string? l, string? r)
        {
            var kind = (string.IsNullOrWhiteSpace(l), string.IsNullOrWhiteSpace(r)) switch
            {
                (true, true) => "same",
                (true, false) => "added",
                (false, true) => "removed",
                _ => l == r ? "same" : "changed"
            };
            list.Add(new DocVersionDiffRow { Field = field, Left = l, Right = r, Kind = kind });
        }

        Row(res.Fields, "عنوان", vL.Title, vR.Title);
        Row(res.Fields, "شرح تغییر", vL.ChangeNote, vR.ChangeNote);
        Row(res.Fields, "تاریخ انقضا",
            vL.ExpireDate?.ToString("yyyy/MM/dd"), vR.ExpireDate?.ToString("yyyy/MM/dd"));
        Row(res.Fields, "وضعیت", StatusName(vL.Status), StatusName(vR.Status));
        Row(res.Fields, "ورژن فعال", vL.IsActive ? "بله" : "خیر", vR.IsActive ? "بله" : "خیر");
        Row(res.Fields, "فریز شده", vL.IsFrozen ? "بله" : "خیر", vR.IsFrozen ? "بله" : "خیر");
        Row(res.Fields, "فعال‌سازی خودکار پس از تایید",
            vL.ActivateOnApprove ? "بله" : "خیر", vR.ActivateOnApprove ? "بله" : "خیر");
        Row(res.Fields, "ثبت‌کننده", vL.CreatedByName, vR.CreatedByName);
        Row(res.Fields, "تاریخ ثبت",
            vL.CreatedAt.ToString("yyyy/MM/dd HH:mm"), vR.CreatedAt.ToString("yyyy/MM/dd HH:mm"));
        Row(res.Fields, "تاریخ تایید",
            vL.ApprovedAt?.ToString("yyyy/MM/dd HH:mm"), vR.ApprovedAt?.ToString("yyyy/MM/dd HH:mm"));

        // ---------- نفرات گردش ----------
        var apL = apprs.Where(a => a.VersionId == vL.Id).ToList();
        var apR = apprs.Where(a => a.VersionId == vR.Id).ToList();
        foreach (var uid in apL.Select(a => a.UserId).Union(apR.Select(a => a.UserId)).Distinct())
        {
            var a1 = apL.FirstOrDefault(a => a.UserId == uid);
            var a2 = apR.FirstOrDefault(a => a.UserId == uid);
            var name = a1?.UserName ?? a2?.UserName ?? uid.ToString();
            res.Approvers.Add(new DocVersionDiffRow
            {
                Field = name,
                Left = a1 == null ? null : ApproverState(a1.Status),
                Right = a2 == null ? null : ApproverState(a2.Status),
                Kind = a1 == null ? "added" : a2 == null ? "removed"
                       : a1.Status == a2.Status ? "same" : "changed"
            });
        }

        // ---------- پیوست‌ها ----------
        var atL = atts.Where(a => a.RefId == vL.Id).Select(a => a.FileName).ToList();
        var atR = atts.Where(a => a.RefId == vR.Id).Select(a => a.FileName).ToList();
        foreach (var name in atL.Union(atR).Distinct())
        {
            var inL = atL.Contains(name);
            var inR = atR.Contains(name);
            res.Attachments.Add(new DocVersionDiffRow
            {
                Field = name,
                Left = inL ? "دارد" : null,
                Right = inR ? "دارد" : null,
                Kind = inL && inR ? "same" : inL ? "added" : "removed"
            });
        }

        // ---------- رویدادهای بین دو ورژن ----------
        var t1 = vL.CreatedAt < vR.CreatedAt ? vL.CreatedAt : vR.CreatedAt;
        var t2 = vL.CreatedAt < vR.CreatedAt ? vR.CreatedAt : vL.CreatedAt;
        res.Between = await Db.DocumentLogs.AsNoTracking()
            .Where(l => l.DocumentId == id && l.CreatedAt >= t1 && l.CreatedAt <= t2)
            .OrderBy(l => l.Id)
            .Select(l => new DocLogDto
            {
                Id = l.Id, Action = l.Action, Detail = l.Detail,
                UserName = l.UserName, CreatedAt = l.CreatedAt, VersionId = l.VersionId
            }).ToListAsync();

        res.ChangeCount = res.Fields.Count(x => x.Kind != "same")
                        + res.Approvers.Count(x => x.Kind != "same")
                        + res.Attachments.Count(x => x.Kind != "same");
        return Ok(res);
    }

    private static string StatusName(DocVersionStatus s) => s switch
    {
        DocVersionStatus.Draft => "پیش‌نویس",
        DocVersionStatus.InReview => "در گردش تایید",
        DocVersionStatus.Approved => "تایید شده",
        DocVersionStatus.Rejected => "رد شده",
        DocVersionStatus.Archived => "بایگانی",
        _ => s.ToString()
    };

    private static string ApproverState(int st) => st switch
    {
        1 => "تایید کرد",
        2 => "رد کرد",
        _ => "در انتظار"
    };

    private DocVersionDto MapVersion(DocumentVersion v,
        List<DocumentApprover> apprs, int attachCount) => new()
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
        AttachmentCount = attachCount,
        Approvers = apprs.Where(a => a.VersionId == v.Id).OrderBy(a => a.Order)
            .Select(a => new DocApproverDto
            {
                Id = a.Id, UserId = a.UserId, UserName = a.UserName,
                Order = a.Order, Status = a.Status, Comment = a.Comment, ActedAt = a.ActedAt
            }).ToList()
    };

    // ---------------------------- سطل بازیافت ----------------------------

    /// <summary>بازگرداندن مدرک حذف‌شده — نیازمند دسترسی کامل.</summary>
    [HttpPut("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Full)
            return StatusCode(403, new { message = "بازگرداندن مدرک نیازمند دسترسی کامل است." });

        var doc = await Db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });
        if (!doc.IsDeleted) return BadRequest(new { message = "این مدرک حذف نشده است." });

        // پوشه مقصد هنوز هست؟ اگر نه، به ریشه برگردان
        var folderExists = await Db.DocFolders.AnyAsync(x => x.Id == doc.FolderId);
        var note = "";
        if (!folderExists)
        {
            var root = await Db.DocFolders.OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (root == null) return BadRequest(new { message = "پوشه‌ای برای بازگرداندن مدرک وجود ندارد." });
            doc.FolderId = root.Id;
            note = $" (پوشه اصلی حذف شده بود؛ به «{root.Name}» منتقل شد)";
        }

        // کد مدرک نباید با مدرک فعال دیگری تداخل داشته باشد
        if (await Db.Documents.AnyAsync(d => d.Id != id && !d.IsDeleted && d.Code == doc.Code))
            return BadRequest(new { message = $"کد «{doc.Code}» اکنون توسط مدرک دیگری استفاده می‌شود؛ ابتدا آن را تغییر دهید." });

        doc.IsDeleted = false;
        doc.DeletedAt = null;
        doc.DeletedByName = null;

        Db.DocumentLogs.Add(new DocumentLog
        {
            DocumentId = id, Action = "Restore",
            Detail = "مدرک از سطل بازیافت بازگردانده شد." + note,
            UserId = MyUserId, UserName = MyUsername
        });
        await Db.SaveChangesAsync();
        return Ok(new { message = "مدرک بازگردانده شد." + note });
    }

    /// <summary>حذف قطعی مدرک و همه وابسته‌هایش — فقط مدیر ماژول.</summary>
    [HttpDelete("{id:int}/purge")]
    public async Task<IActionResult> Purge(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;

        var doc = await Db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });
        if (!doc.IsDeleted)
            return BadRequest(new { message = "فقط مدارک داخل سطل بازیافت قابل حذف قطعی هستند." });

        var versionIds = await Db.DocumentVersions.Where(v => v.DocumentId == id).Select(v => v.Id).ToListAsync();

        // پیوست‌های ورژن‌ها
        var attaches = await Db.AppAttachments
            .Where(a => a.Module == "DocVersion" && versionIds.Contains(a.RefId)).ToListAsync();
        Db.AppAttachments.RemoveRange(attaches);

        Db.DocumentApprovers.RemoveRange(await Db.DocumentApprovers.Where(a => a.DocumentId == id).ToListAsync());
        Db.DocumentVersions.RemoveRange(await Db.DocumentVersions.Where(v => v.DocumentId == id).ToListAsync());
        Db.DocumentPermissions.RemoveRange(await Db.DocumentPermissions.Where(p => p.DocumentId == id).ToListAsync());
        Db.DocumentLinks.RemoveRange(await Db.DocumentLinks
            .Where(l => l.DocumentId == id || l.LinkedDocumentId == id).ToListAsync());
        Db.DocCartableTasks.RemoveRange(await Db.DocCartableTasks
            .Where(t => t.DocumentId == id || t.SourceDocumentId == id).ToListAsync());
        Db.DocExpiryAlerts.RemoveRange(await Db.DocExpiryAlerts.Where(a => a.DocumentId == id).ToListAsync());
        Db.DocumentLogs.RemoveRange(await Db.DocumentLogs.Where(l => l.DocumentId == id).ToListAsync());
        Db.Documents.Remove(doc);

        await Db.SaveChangesAsync();
        return Ok(new { message = $"مدرک {doc.Code} برای همیشه حذف شد." });
    }

    /// <summary>خالی کردن کامل سطل بازیافت — فقط مدیر ماژول.</summary>
    [HttpDelete("/api/doc-archive/trash")]
    public async Task<IActionResult> EmptyTrash()
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;

        var ids = await Db.Documents.Where(d => d.IsDeleted).Select(d => d.Id).ToListAsync();
        foreach (var id in ids) await Purge(id);
        return Ok(new { message = $"{ids.Count} مدرک برای همیشه حذف شد.", count = ids.Count });
    }

    // ---------------------------- انقضا ----------------------------

    /// <summary>شمارش مدارک منقضی‌شده و رو به انقضا (برای بج‌های کنار درخت).</summary>
    [HttpGet("/api/doc-archive/expiry-summary")]
    public async Task<IActionResult> ExpirySummary(int expiringDays = 60)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var folderMap = await _access.FolderAccessMapAsync(MyUserId, manager);
        var today = DateTime.Today;
        var horizon = today.AddDays(expiringDays);

        var docs = await Db.Documents.AsNoTracking()
            .Where(d => d.IsActive && !d.IsDeleted && d.ExpireDate != null && d.ExpireDate.Value.Date <= horizon)
            .Select(d => new { d.Id, d.FolderId, d.CreatedByUserId, d.IsPublic, d.ExpireDate })
            .ToListAsync();

        var ids = docs.Select(d => d.Id).ToList();
        var perms = await Db.DocumentPermissions.AsNoTracking()
            .Where(p => ids.Contains(p.DocumentId) && p.UserId == MyUserId).ToListAsync();

        int expired = 0, soon = 0;
        foreach (var d in docs)
        {
            var visible = manager || d.CreatedByUserId == MyUserId || d.IsPublic
                || perms.Any(p => p.DocumentId == d.Id && p.Level > DocAccessLevel.None)
                || (folderMap.TryGetValue(d.FolderId, out var fa) && fa.Item1 > DocAccessLevel.None);
            if (!visible) continue;

            if (d.ExpireDate!.Value.Date < today) expired++; else soon++;
        }

        return Ok(new { expired, expiringSoon = soon, expiringDays });
    }

    /// <summary>اجرای دستی بررسی انقضا (فقط مدیر ماژول) — برای تست و اجرای فوری.</summary>
    [HttpPost("/api/doc-archive/expiry-check")]
    public async Task<IActionResult> RunExpiryCheck()
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        var n = await _expiry.RunOnceAsync(HttpContext.RequestAborted);
        return Ok(new { created = n, message = n == 0 ? "مدرکی در آستانه انقضا نبود یا قبلاً هشدار داده شده." : $"{n} کار کارتابل ساخته شد." });
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
