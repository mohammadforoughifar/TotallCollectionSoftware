using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>
/// ================== یکپارچه‌سازی آرشیو اسناد با سایر ماژول‌های سامانه ERP ==================
/// اتصال اسناد آرشیو به پروژه‌ها، پرسنل، اموال و تجهیزات IT، فاکتورها، طرف‌حساب‌ها و ...
/// </summary>
[Route("api/doc-archive/entity-links")]
public class DocEntityLinksController : RbacControllerBase
{
    private readonly IDocAccessService _access;
    private readonly IDocumentService _svc;

    public DocEntityLinksController(AppDbContext db, IDocAccessService access, IDocumentService svc) : base(db)
    {
        _access = access;
        _svc = svc;
    }

    private const string Mod = "DocArchive";
    private Task<bool> IsManagerAsync() => HasAsync(Mod, "Manage");

    /// <summary>
    /// دریافت تمامی مدارک آرشیو لینک‌شده به یک موجودیت در ماژول‌های سامانه (مثلاً یک پروژه، پرسنل یا دارایی IT).
    /// </summary>
    [HttpGet("{module}/{entityId:int}")]
    public async Task<IActionResult> GetLinkedDocuments(string module, int entityId)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();

        var links = await Db.DocEntityLinks.AsNoTracking()
            .Where(l => l.Module.ToLower() == module.ToLower() && l.EntityId == entityId)
            .OrderByDescending(l => l.Id)
            .ToListAsync();

        if (links.Count == 0) return Ok(new List<DocEntityLinkDto>());

        var docIds = links.Select(l => l.DocumentId).Distinct().ToList();

        var docs = await Db.Documents.AsNoTracking()
            .Where(d => docIds.Contains(d.Id) && !d.IsDeleted)
            .ToListAsync();

        var docMap = docs.ToDictionary(d => d.Id);
        var folderIds = docs.Select(d => d.FolderId).Distinct().ToList();
        var folders = await Db.DocFolders.AsNoTracking()
            .Where(f => folderIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Name);

        // تگ‌ها
        var docTags = await (from dt in Db.DocumentTags.AsNoTracking()
                             where docIds.Contains(dt.DocumentId)
                             join t in Db.DocTags.AsNoTracking() on dt.TagId equals t.Id
                             select new { dt.DocumentId, Tag = new DocTagDto
                             {
                                 Id = t.Id, Name = t.Name, Color = t.Color, Description = t.Description
                             }}).ToListAsync();
        var tagMap = docTags.GroupBy(x => x.DocumentId).ToDictionary(g => g.Key, g => g.Select(x => x.Tag).ToList());

        // نسخه‌ها و پیوست‌ها
        var versions = await Db.DocumentVersions.AsNoTracking()
            .Where(v => docIds.Contains(v.DocumentId))
            .OrderByDescending(v => v.VersionNo)
            .ToListAsync();

        var versionMap = versions.GroupBy(v => v.DocumentId).ToDictionary(g => g.Key, g => g.ToList());
        var versionIds = versions.Select(v => v.Id).ToList();

        var attachments = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == "DocVersion" && versionIds.Contains(a.RefId))
            .ToListAsync();

        var attMap = attachments.GroupBy(a => a.RefId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<DocEntityLinkDto>();

        foreach (var link in links)
        {
            if (!docMap.TryGetValue(link.DocumentId, out var d)) continue;

            var (lvl, dl) = await _access.DocumentAccessAsync(MyUserId, manager, d.Id);
            if (lvl <= DocAccessLevel.None) continue;

            var dVersions = versionMap.TryGetValue(d.Id, out var vl) ? vl : new List<DocumentVersion>();
            var activeV = dVersions.FirstOrDefault(v => v.IsActive) ?? dVersions.FirstOrDefault();

            var attSummary = new List<DocAttachmentSummaryDto>();
            if (activeV != null && attMap.TryGetValue(activeV.Id, out var attList))
            {
                foreach (var a in attList)
                {
                    attSummary.Add(new DocAttachmentSummaryDto
                    {
                        Id = a.Id,
                        FileName = a.FileName,
                        ContentType = a.ContentType,
                        Size = a.Data.Length,
                        CanPreview = true,
                        CanDownload = dl,
                        VersionNo = activeV.VersionNo
                    });
                }
            }

            result.Add(new DocEntityLinkDto
            {
                Id = link.Id,
                DocumentId = d.Id,
                DocumentCode = d.Code,
                DocumentTitle = d.Title,
                FolderId = d.FolderId,
                FolderName = folders.TryGetValue(d.FolderId, out var fn) ? fn : "",
                ActiveVersionNo = activeV?.VersionNo ?? 1,
                IsActive = d.IsActive,
                ExpireDate = d.ExpireDate,
                IsExpired = d.ExpireDate.HasValue && d.ExpireDate.Value.Date < DateTime.Today,
                Module = link.Module,
                ModuleTitle = GetModuleTitle(link.Module),
                EntityId = link.EntityId,
                EntityCode = link.EntityCode,
                EntityTitle = link.EntityTitle,
                EntityUrl = GetEntityUrl(link.Module, link.EntityId),
                Note = link.Note,
                CreatedByName = link.CreatedByName,
                CreatedAt = link.CreatedAt,
                MyLevel = (DocAccessLevelDto)(int)lvl,
                MyCanDownload = dl,
                Tags = tagMap.TryGetValue(d.Id, out var tl) ? tl : new(),
                Attachments = attSummary
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// اتصال یک سند آرشیو به رکورد در ماژول ERP.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> AddLink([FromBody] DocEntityLinkSaveDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        if (dto.DocumentId <= 0) return BadRequest(new { message = "سند آرشیو نامعتبر است." });
        if (string.IsNullOrWhiteSpace(dto.Module)) return BadRequest(new { message = "نام ماژول اجباری است." });
        if (dto.EntityId <= 0) return BadRequest(new { message = "شناسه موجودیت نامعتبر است." });

        var doc = await Db.Documents.FirstOrDefaultAsync(d => d.Id == dto.DocumentId && !d.IsDeleted);
        if (doc == null) return NotFound(new { message = "سند در آرشیو یافت نشد." });

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, dto.DocumentId);
        if (lvl < DocAccessLevel.Write && !manager)
            return StatusCode(403, new { message = "برای اتصال سند، دسترسی نوشتن روی سند لازم است." });

        // بررسی تکراری نبودن
        var exists = await Db.DocEntityLinks.AnyAsync(l =>
            l.DocumentId == dto.DocumentId &&
            l.Module.ToLower() == dto.Module.Trim().ToLower() &&
            l.EntityId == dto.EntityId);

        if (exists) return BadRequest(new { message = "این سند قبلاً به این موجودیت متصل شده است." });

        var entity = new DocEntityLink
        {
            DocumentId = dto.DocumentId,
            Module = dto.Module.Trim(),
            EntityId = dto.EntityId,
            EntityCode = dto.EntityCode?.Trim(),
            EntityTitle = string.IsNullOrWhiteSpace(dto.EntityTitle) ? $"مورد #{dto.EntityId}" : dto.EntityTitle.Trim(),
            Note = dto.Note?.Trim(),
            CreatedByUserId = MyUserId,
            CreatedByName = MyUsername,
            CreatedAt = DateTime.Now
        };

        Db.DocEntityLinks.Add(entity);

        // ثبت لاگ
        Db.DocumentLogs.Add(new DocumentLog
        {
            DocumentId = doc.Id,
            Action = "ERPLinkAdded",
            Detail = $"اتصال به ماژول {GetModuleTitle(entity.Module)}: «{entity.EntityTitle}»",
            UserId = MyUserId,
            UserName = MyUsername,
            CreatedAt = DateTime.Now
        });

        await Db.SaveChangesAsync();
        return Ok(new { id = entity.Id });
    }

    /// <summary>
    /// حذف اتصال بین سند آرشیو و موجودیت ماژول ERP.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> RemoveLink(int id)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var link = await Db.DocEntityLinks.FirstOrDefaultAsync(l => l.Id == id);
        if (link == null) return NotFound(new { message = "پیوند یافت نشد." });

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, link.DocumentId);
        if (lvl < DocAccessLevel.Write && !manager && link.CreatedByUserId != MyUserId)
            return StatusCode(403, new { message = "شما اجازه حذف این اتصال را ندارید." });

        Db.DocumentLogs.Add(new DocumentLog
        {
            DocumentId = link.DocumentId,
            Action = "ERPLinkRemoved",
            Detail = $"حذف اتصال از ماژول {GetModuleTitle(link.Module)}: «{link.EntityTitle}»",
            UserId = MyUserId,
            UserName = MyUsername,
            CreatedAt = DateTime.Now
        });

        Db.DocEntityLinks.Remove(link);
        await Db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// ایجاد سریع یک سند آرشیو و سنجاق خودکار آن به موجودیت ERP.
    /// </summary>
    [HttpPost("quick-create")]
    public async Task<IActionResult> QuickCreateLinked([FromBody] DocQuickCreateLinkedDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Create") is { } f) return f;

        if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { message = "عنوان مدرک اجباری است." });
        if (string.IsNullOrWhiteSpace(dto.Code)) return BadRequest(new { message = "کد مدرک اجباری است." });
        if (dto.FolderId <= 0) return BadRequest(new { message = "پوشه مقصد نامعتبر است." });
        if (dto.EntityId <= 0 || string.IsNullOrWhiteSpace(dto.Module)) return BadRequest(new { message = "اطلاعات ماژول مبدأ ناقص است." });

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.FolderAccessAsync(MyUserId, manager, dto.FolderId);
        if (lvl < DocAccessLevel.Write)
            return StatusCode(403, new { message = "برای ایجاد مدرک در این پوشه، دسترسی نوشتن لازم است." });

        var docDto = new DocumentDto
        {
            FolderId = dto.FolderId,
            Title = dto.Title.Trim(),
            Code = dto.Code.Trim(),
            CustomerCode = dto.CustomerCode?.Trim(),
            Description = dto.Description?.Trim(),
            ExpireDate = dto.ExpireDate,
            TagIds = dto.TagIds,
            FirstVersionActive = true,
            IsActive = true
        };

        var docId = await _svc.CreateDocumentAsync(docDto, MyUserId, MyUsername);

        // ایجاد اتصال
        var link = new DocEntityLink
        {
            DocumentId = docId,
            Module = dto.Module.Trim(),
            EntityId = dto.EntityId,
            EntityCode = dto.EntityCode?.Trim(),
            EntityTitle = string.IsNullOrWhiteSpace(dto.EntityTitle) ? $"مورد #{dto.EntityId}" : dto.EntityTitle.Trim(),
            Note = dto.LinkNote?.Trim(),
            CreatedByUserId = MyUserId,
            CreatedByName = MyUsername,
            CreatedAt = DateTime.Now
        };

        Db.DocEntityLinks.Add(link);
        await Db.SaveChangesAsync();

        return Ok(new { documentId = docId, linkId = link.Id });
    }

    /// <summary>
    /// سرچ و لوک‌آپ موجودیت‌ها در سایر ماژول‌های ERP جهت اتصال به سند آرشیو.
    /// </summary>
    [HttpGet("/api/doc-archive/entity-lookups/{module}")]
    public async Task<IActionResult> SearchEntities(string module, [FromQuery] string? q = null)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var term = (q ?? "").Trim();
        var results = new List<DocEntityLookupItemDto>();

        switch (module.ToLowerInvariant())
        {
            case "projects":
                var pq = Db.ProjectEntryExits.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(term))
                    pq = pq.Where(p => p.ProjectName.Contains(term) || p.CodeProject.Contains(term) || p.SerialNumber.Contains(term));
                results = await pq.OrderByDescending(p => p.Id).Take(25)
                    .Select(p => new DocEntityLookupItemDto
                    {
                        Id = p.Id,
                        Code = p.CodeProject,
                        Title = p.ProjectName,
                        Subtitle = $"سریال: {p.SerialNumber}",
                        Module = "Projects"
                    }).ToListAsync();
                break;

            case "hr":
            case "personnel":
                var uq = Db.Users.AsNoTracking().Where(u => u.IsActive);
                if (!string.IsNullOrWhiteSpace(term))
                    uq = uq.Where(u => u.Username.Contains(term) || (u.FirstName ?? "").Contains(term) || (u.LastName ?? "").Contains(term) || (u.Mobile ?? "").Contains(term));
                results = await uq.OrderBy(u => u.FirstName).Take(25)
                    .Select(u => new DocEntityLookupItemDto
                    {
                        Id = u.Id,
                        Code = u.Username,
                        Title = (u.FirstName + " " + u.LastName).Trim().Length > 0 ? (u.FirstName + " " + u.LastName).Trim() : u.Username,
                        Subtitle = $"موبایل: {u.Mobile ?? "—"} | نقش: {u.Role}",
                        Module = "Hr"
                    }).ToListAsync();
                break;

            case "itassets":
                var iq = Db.SystemInfos.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(term))
                    iq = iq.Where(s => s.AgentId.Contains(term) || (s.OsName ?? "").Contains(term) || (s.Cpu ?? "").Contains(term));
                results = await iq.OrderByDescending(s => s.Id).Take(25)
                    .Select(s => new DocEntityLookupItemDto
                    {
                        Id = s.Id,
                        Code = s.AgentId,
                        Title = $"سیستم {s.AgentId} ({s.OsName ?? "ویندوز"})",
                        Subtitle = $"پردازنده: {s.Cpu ?? "—"} | رم: {s.TotalRamGb} GB",
                        Module = "ItAssets"
                    }).ToListAsync();
                break;

            case "invoicing":
                var fq = Db.FacInvoices.AsNoTracking().Include(i => i.Party).AsQueryable();
                if (!string.IsNullOrWhiteSpace(term))
                    fq = fq.Where(i => i.Number.ToString().Contains(term) || (i.Party != null && i.Party.Name.Contains(term)));
                results = await fq.OrderByDescending(i => i.Id).Take(25)
                    .Select(i => new DocEntityLookupItemDto
                    {
                        Id = i.Id,
                        Code = i.Number.ToString(),
                        Title = "فاکتور " + i.Number + " — " + (i.Party != null ? i.Party.Name : "بدون طرف‌حساب"),
                        Subtitle = $"نوع: {i.Kind} | تاریخ: {i.Date:yyyy/MM/dd}",
                        Module = "Invoicing"
                    }).ToListAsync();
                break;

            case "catalog":
            case "party":
            case "karfarma":
                var kq = Db.KarFarmas.AsNoTracking().Where(k => !k.IsDelete);
                if (!string.IsNullOrWhiteSpace(term))
                    kq = kq.Where(k => k.Name.Contains(term) || (k.Telephone ?? "").Contains(term));
                results = await kq.OrderBy(k => k.Name).Take(25)
                    .Select(k => new DocEntityLookupItemDto
                    {
                        Id = k.Id,
                        Code = k.Id.ToString(),
                        Title = k.Name,
                        Subtitle = $"تلفن: {k.Telephone ?? "—"} | ثبت: {k.ShomareSabt ?? "—"}",
                        Module = "KarFarma"
                    }).ToListAsync();
                break;

            case "repairs":
                var rq = Db.RepairOrders.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(term))
                    rq = rq.Where(r => r.Number.Contains(term) || (r.DeviceModel ?? "").Contains(term) || (r.DeviceType ?? "").Contains(term));
                results = await rq.OrderByDescending(r => r.Id).Take(25)
                    .Select(r => new DocEntityLookupItemDto
                    {
                        Id = r.Id,
                        Code = r.Number,
                        Title = $"سفارش {r.Number} — {r.DeviceType} {r.DeviceModel}",
                        Subtitle = $"سریال: {r.SerialNumber ?? "—"}",
                        Module = "Repairs"
                    }).ToListAsync();
                break;

            case "warehousing":
                var wq = Db.InvDocs.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(term))
                    wq = wq.Where(d => d.Number.Contains(term) || (d.Description ?? "").Contains(term));
                results = await wq.OrderByDescending(d => d.Id).Take(25)
                    .Select(d => new DocEntityLookupItemDto
                    {
                        Id = d.Id,
                        Code = d.Number,
                        Title = $"سند انبار {d.Number}",
                        Subtitle = d.Description ?? "—",
                        Module = "Warehousing"
                    }).ToListAsync();
                break;

            default:
                break;
        }

        return Ok(results);
    }

    private static string GetModuleTitle(string module) => module.ToLowerInvariant() switch
    {
        "projects" => "پروژه‌ها",
        "hr" or "personnel" => "منابع انسانی / پرسنلی",
        "itassets" => "اموال و تجهیزات IT",
        "invoicing" => "فاکتور و فروش",
        "catalog" or "party" or "karfarma" => "کارفرما و طرف‌حساب",
        "repairs" => "تعمیرات",
        "office" => "دبیرخانه و مکاتبات",
        "sales" => "فروش و قراردادها",
        "warehousing" => "انبارداری",
        _ => module
    };

    private static string? GetEntityUrl(string module, int entityId) => module.ToLowerInvariant() switch
    {
        "projects" => $"/projects?open={entityId}",
        "hr" or "personnel" => $"/hr/panel?userId={entityId}",
        "itassets" => $"/it-assets/systems?open={entityId}",
        "invoicing" => $"/invoices?open={entityId}",
        "catalog" or "party" or "karfarma" => $"/karfarmas",
        "repairs" => $"/repairs?open={entityId}",
        _ => null
    };
}
