using System.Security.Claims;
using Inventory.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// ================== بایگانی جامع — یک API برای همه ماژول‌ها ==================
/// هر کاربر فقط بایگانی خودش را می‌بیند (خصوصی). پوشه/زیرپوشه نامحدود.
/// افزودن رکورد هر ماژول: POST items با { module, refId, title, link }
/// </summary>
[ApiController]
[Route("api/archive")]
[Authorize]
public class ArchiveController : ControllerBase
{
    private readonly AppDbContext _db;
    public ArchiveController(AppDbContext db) => _db = db;

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    // ---------- پوشه‌ها ----------
    [HttpGet("folders")]
    public async Task<IActionResult> Folders() =>
        Ok(await _db.ArchiveFolders.Where(f => f.OwnerUserId == MyUserId)
            .OrderBy(f => f.Name)
            .Select(f => new { f.Id, f.ParentId, f.Name, f.CreatedAt,
                ItemCount = _db.ArchiveItems.Count(i => i.FolderId == f.Id) })
            .ToListAsync());

    public class FolderDto { public string Name { get; set; } = ""; public int? ParentId { get; set; } }

    [HttpPost("folders")]
    public async Task<IActionResult> CreateFolder([FromBody] FolderDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "نام پوشه را وارد کنید." });
        if (dto.ParentId is > 0 &&
            !await _db.ArchiveFolders.AnyAsync(f => f.Id == dto.ParentId && f.OwnerUserId == MyUserId))
            return BadRequest(new { message = "پوشه والد نامعتبر است." });

        var f = new ArchiveFolder { OwnerUserId = MyUserId, ParentId = dto.ParentId, Name = dto.Name.Trim() };
        _db.ArchiveFolders.Add(f);
        await _db.SaveChangesAsync();
        return Ok(new { id = f.Id });
    }

    [HttpPut("folders/{id:int}")]
    public async Task<IActionResult> RenameFolder(int id, [FromBody] FolderDto dto)
    {
        var f = await _db.ArchiveFolders.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == MyUserId);
        if (f == null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "نام پوشه را وارد کنید." });
        f.Name = dto.Name.Trim();
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("folders/{id:int}")]
    public async Task<IActionResult> DeleteFolder(int id)
    {
        var f = await _db.ArchiveFolders.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == MyUserId);
        if (f == null) return NotFound();

        // حذف بازگشتی زیرپوشه‌ها و آیتم‌ها
        var all = await _db.ArchiveFolders.Where(x => x.OwnerUserId == MyUserId).ToListAsync();
        var toDelete = new List<int> { id };
        void Collect(int pid)
        {
            foreach (var c in all.Where(x => x.ParentId == pid)) { toDelete.Add(c.Id); Collect(c.Id); }
        }
        Collect(id);

        _db.ArchiveItems.RemoveRange(_db.ArchiveItems.Where(i => toDelete.Contains(i.FolderId)));
        _db.ArchiveFolders.RemoveRange(all.Where(x => toDelete.Contains(x.Id)));
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ---------- آیتم‌ها ----------
    [HttpGet("items/{folderId:int}")]
    public async Task<IActionResult> Items(int folderId) =>
        Ok(await _db.ArchiveItems
            .Where(i => i.OwnerUserId == MyUserId && i.FolderId == folderId)
            .OrderByDescending(i => i.Id)
            .Select(i => new { i.Id, i.Module, i.RefId, i.Title, i.Link, i.Note, i.CreatedAt })
            .ToListAsync());

    public class ItemDto
    {
        public int FolderId { get; set; }
        public string Module { get; set; } = "";
        public int RefId { get; set; }
        public string Title { get; set; } = "";
        public string? Link { get; set; }
        public string? Note { get; set; }
    }

    [HttpPost("items")]
    public async Task<IActionResult> AddItem([FromBody] ItemDto dto)
    {
        if (!await _db.ArchiveFolders.AnyAsync(f => f.Id == dto.FolderId && f.OwnerUserId == MyUserId))
            return BadRequest(new { message = "پوشه نامعتبر است." });

        // جلوگیری از تکرار همان رکورد در همان پوشه
        if (await _db.ArchiveItems.AnyAsync(i => i.OwnerUserId == MyUserId && i.FolderId == dto.FolderId
                && i.Module == dto.Module && i.RefId == dto.RefId))
            return BadRequest(new { message = "این مورد قبلاً در این پوشه بایگانی شده است." });

        _db.ArchiveItems.Add(new ArchiveItem
        {
            OwnerUserId = MyUserId, FolderId = dto.FolderId, Module = dto.Module.Trim(),
            RefId = dto.RefId, Title = dto.Title.Trim(), Link = dto.Link, Note = dto.Note?.Trim()
        });
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("items/{id:int}")]
    public async Task<IActionResult> DeleteItem(int id)
    {
        var i = await _db.ArchiveItems.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == MyUserId);
        if (i == null) return NotFound();
        _db.ArchiveItems.Remove(i);
        await _db.SaveChangesAsync();
        return Ok();
    }
}

/// <summary>
/// ================== پیوست جامع — یک API برای همه فرم‌ها ==================
/// آپلود/لیست/دانلود/حذف با (module + refId) — فرم جدید = صفر کد بک‌اند.
/// </summary>
[ApiController]
[Route("api/attachments")]
[Authorize]
public class AttachmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly FileStore _store;
    private readonly IAttachmentGuard _guard;
    public AttachmentsController(AppDbContext db, FileStore store, IAttachmentGuard guard)
    { _db = db; _store = store; _guard = guard; }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyUsername => User.FindFirstValue(ClaimTypes.Name) ?? "";
    private bool IsAdmin => User.IsInRole("Admin");

    /// <summary>فرمت‌هایی که داخل مرورگر قابل نمایش‌اند (پیش‌نمایش بدون دانلود).</summary>
    private static readonly HashSet<string> InlineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/png", "image/jpeg", "image/jpg",
        "image/gif", "image/webp", "image/bmp", "image/svg+xml", "text/plain"
    };

    private static string GuessContentType(string fileName, string stored)
    {
        if (!string.IsNullOrWhiteSpace(stored) && stored != "application/octet-stream") return stored;
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".txt" => "text/plain",
            _ => stored ?? "application/octet-stream"
        };
    }

    [HttpGet("{module}/{refId:int}")]
    public async Task<IActionResult> List(string module, int refId)
    {
        var access = await _guard.CheckAsync(module, refId, MyUserId, IsAdmin);
        if (access == AttachmentAccess.None)
            return StatusCode(403, new { message = "شما به پیوست‌های این مورد دسترسی ندارید." });

        var rows = await _db.AppAttachments.Where(a => a.Module == module && a.RefId == refId)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.UploaderName, a.UploaderUserId, a.UploadedAt, a.FilePath, a.Data })
            .ToListAsync();

        return Ok(rows.Select(a =>
        {
            var ct = GuessContentType(a.FileName, a.ContentType);
            return new
            {
                a.Id, a.FileName, a.UploaderName, a.UploaderUserId, a.UploadedAt,
                Size = a.FilePath is not null ? _store.Size(a.FilePath) : (long)a.Data.Length,
                ContentType = ct,
                CanPreview = InlineTypes.Contains(ct),
                CanDownload = access == AttachmentAccess.Download
            };
        }));
    }

    [HttpPost("{module}/{refId:int}")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> Upload(string module, int refId, IFormFile file)
    {
        var access = await _guard.CheckAsync(module, refId, MyUserId, IsAdmin);
        if (access != AttachmentAccess.Download)
            return StatusCode(403, new { message = "شما اجازه افزودن پیوست به این مورد را ندارید." });

        if (file == null || file.Length == 0) return BadRequest(new { message = "فایلی انتخاب نشده است." });
        if (file.Length > 10 * 1024 * 1024) return BadRequest(new { message = "حداکثر حجم فایل ۱۰ مگابایت است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        var relPath = await _store.SaveAsync(module, refId, ms, file.FileName);
        _db.AppAttachments.Add(new AppAttachment
        {
            Module = module, RefId = refId,
            FileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType ?? "application/octet-stream",
            FilePath = relPath,
            Data = Array.Empty<byte>(),
            UploaderName = MyUsername, UploaderUserId = MyUserId
        });
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>دانلود پیوست — نیازمند احراز هویت و دسترسی دانلود روی رکورد صاحبِ پیوست.</summary>
    [HttpGet("download/{id:int}")]
    public async Task<IActionResult> Download(int id)
    {
        var a = await _db.AppAttachments.FindAsync(id);
        if (a == null) return NotFound();

        var access = await _guard.CheckAsync(a.Module, a.RefId, MyUserId, IsAdmin);
        if (access == AttachmentAccess.None)
            return StatusCode(403, new { message = "شما به این فایل دسترسی ندارید." });
        if (access == AttachmentAccess.PreviewOnly)
            return StatusCode(403, new { message = "شما اجازه دانلود این فایل را ندارید؛ فقط امکان مشاهده دارید." });

        var bytes = _store.ReadBytes(a.FilePath) ?? (a.Data is { Length: > 0 } ? a.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });
        return File(bytes, GuessContentType(a.FileName, a.ContentType), a.FileName);
    }

    /// <summary>
    /// پیش‌نمایش داخل برنامه — فایل به‌صورت inline برگردانده می‌شود (بدون هدر دانلود).
    /// برای کاربرانی که فقط حق «مشاهده» دارند هم کار می‌کند.
    /// </summary>
    [HttpGet("preview/{id:int}")]
    public async Task<IActionResult> Preview(int id)
    {
        var a = await _db.AppAttachments.FindAsync(id);
        if (a == null) return NotFound();

        var access = await _guard.CheckAsync(a.Module, a.RefId, MyUserId, IsAdmin);
        if (access == AttachmentAccess.None)
            return StatusCode(403, new { message = "شما به این فایل دسترسی ندارید." });

        var ct = GuessContentType(a.FileName, a.ContentType);
        if (!InlineTypes.Contains(ct))
            return BadRequest(new { message = "این نوع فایل قابل پیش‌نمایش نیست." });

        var bytes = _store.ReadBytes(a.FilePath) ?? (a.Data is { Length: > 0 } ? a.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });

        // inline تا مرورگر داخل iframe/img نمایش دهد و پنجره دانلود باز نشود
        Response.Headers["Content-Disposition"] = "inline; filename=\"preview\"";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(bytes, ct);
    }

    /// <summary>حذف — فقط آپلودکننده یا مدیر.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var a = await _db.AppAttachments.FindAsync(id);
        if (a == null) return NotFound();

        var access = await _guard.CheckAsync(a.Module, a.RefId, MyUserId, IsAdmin);
        if (access != AttachmentAccess.Download)
            return StatusCode(403, new { message = "شما اجازه حذف پیوست این مورد را ندارید." });
        if (a.UploaderUserId != MyUserId && !IsAdmin)
            return StatusCode(403, new { message = "فقط آپلودکننده یا مدیر می‌تواند این پیوست را حذف کند." });
        _store.Delete(a.FilePath);
        _db.AppAttachments.Remove(a);
        await _db.SaveChangesAsync();
        return Ok();
    }
}
