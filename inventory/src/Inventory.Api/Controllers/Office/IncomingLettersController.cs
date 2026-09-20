using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Office;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// کنترلر مدیریت کامل نامه‌های وارده (Incoming Letters Subsystem)
/// </summary>
[ApiController]
[Route("api/incoming-letters")]
[Authorize]
public class IncomingLettersController : RbacControllerBase
{
    private const string Module = "IncomingLetters";

    private async Task<bool> IsAdminAsync() => await HasAsync(Module, "Delete");

    private async Task<string> MyDisplayNameAsync()
    {
        var u = await Db.Users.AsNoTracking()
            .Where(x => x.Id == MyUserId)
            .Select(x => new { x.FirstName, x.LastName, x.Username })
            .FirstOrDefaultAsync();
        if (u == null) return MyUsername;
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? u.Username : full;
    }
    private readonly IIncomingLetterService _letters;
    private readonly IErjaService _erja;
    private readonly FileStore _store;

    public IncomingLettersController(IIncomingLetterService letters, IErjaService erja, FileStore store, AppDbContext db)
        : base(db)
    {
        _letters = letters;
        _erja = erja;
        _store = store;
    }

    [HttpGet("inbox")]
    public async Task<IActionResult> GetInbox([FromQuery] string? search, [FromQuery] bool? unreadOnly, [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetInboxAsync(MyUserId, search, unreadOnly, page, pageSize));
    }

    [HttpGet("archive")]
    public async Task<IActionResult> GetArchive([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetArchiveAsync(MyUserId, search, page, pageSize));
    }

    [HttpGet("sent")]
    public async Task<IActionResult> GetSent([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetSentAsync(MyUserId, search, page, pageSize));
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetStatsAsync(MyUserId));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDetail(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var detail = await _letters.GetDetailAsync(id, MyUserId, await IsAdminAsync());
        if (detail == null) return NotFound(new { message = "نامه وارده یافت نشد." });
        return Ok(detail);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AddIncomingLetterDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var id = await _letters.AddAsync(dto, MyUserId);
        return Ok(new { id, message = "نامه وارده با موفقیت ثبت شد." });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Edit(int id, [FromBody] EditIncomingLetterDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        try
        {
            await _letters.EditAsync(id, dto, MyUserId, await IsAdminAsync());
            return Ok(new { message = "نامه وارده ویرایش شد." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;
        try
        {
            await _letters.DeleteAsync(id, MyUserId, await IsAdminAsync());
            return Ok(new { message = "نامه وارده حذف شد." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost("erja")]
    public async Task<IActionResult> Erja([FromBody] AddErjaDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Erja") is { } forbid) return forbid;
        await _erja.AddErjaAsync(dto, MyUserId, await MyDisplayNameAsync());
        return Ok(new { message = "ارجاع نامه انجام شد." });
    }

    [HttpGet("{id:int}/gardesh")]
    public async Task<IActionResult> Gardesh(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _erja.GetGardeshTreeAsync(id, MyUserId, await IsAdminAsync()));
    }

    [HttpPost("erja/{erjaId:int}/answer")]
    public async Task<IActionResult> AnswerErja(int erjaId, [FromBody] AnswerErjaDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        await _erja.AnswerAsync(erjaId, dto, MyUserId, await MyDisplayNameAsync());
        return Ok(new { message = "پاسخ ارجاع ثبت شد." });
    }

    [HttpPost("erja/{erjaId:int}/read")]
    public async Task<IActionResult> MarkRead(int erjaId)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        await _erja.MarkReadAsync(erjaId, MyUserId);
        return Ok(new { message = "نامه خوانده شد." });
    }

    [HttpPost("erja/{erjaId:int}/neshan")]
    public async Task<IActionResult> ToggleNeshan(int erjaId)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var isNeshan = await _erja.ToggleNeshanAsync(erjaId, MyUserId);
        return Ok(new { isNeshan });
    }

    [HttpPost("{id:int}/neshan")]
    public async Task<IActionResult> ToggleLetterNeshan(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var isNeshan = await _letters.ToggleLetterNeshanAsync(id, MyUserId);
        return Ok(new { isNeshan });
    }

    [HttpPost("erja/{erjaId:int}/bayegani")]
    public async Task<IActionResult> ToggleBayegani(int erjaId)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var isBayegani = await _erja.ToggleBayeganiAsync(erjaId, MyUserId);
        return Ok(new { isBayegani });
    }

    public class BatchBayeganiDto { public List<int> ErjaIds { get; set; } = new(); public string? Description { get; set; } }

    [HttpPost("erja/batch-bayegani")]
    public async Task<IActionResult> BatchBayegani([FromBody] BatchBayeganiDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var count = await _erja.BatchBayeganiAsync(dto.ErjaIds, MyUserId, dto.Description);
        return Ok(new { count, message = $"{count} نامه با موفقیت بایگانی شد." });
    }

    [HttpGet("{id:int}/attachments")]
    public async Task<IActionResult> Attachments(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var rows = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == "IncomingLetters" && a.RefId == id)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.FilePath, a.Data, a.UploaderName, a.UploaderUserId, a.UploadedAt })
            .ToListAsync();

        var list = rows.Select(a => new LetterAttachmentDto
        {
            Id = a.Id,
            FileName = a.FileName,
            ContentType = a.ContentType,
            Size = a.FilePath is not null ? _store.Size(a.FilePath) : (long)a.Data.Length,
            UploaderName = a.UploaderName,
            UploaderUserId = a.UploaderUserId,
            UploadedAt = a.UploadedAt
        }).ToList();
        return Ok(list);
    }

    [HttpPost("{id:int}/attachments")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile file)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        if (file == null || file.Length <= 0)
            return BadRequest(new { message = "فایل خالی است." });
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { message = "حداکثر حجم هر فایل ۲۰ مگابایت است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        // ذخیره فیزیکی زیر wwwroot/uploads/IncomingLetters/{id}/{guid}_{name}
        var relPath = await _store.SaveAsync("IncomingLetters", id, ms, file.FileName);

        var att = new AppAttachment
        {
            Module = "IncomingLetters",
            RefId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            FilePath = relPath,
            Data = Array.Empty<byte>(),
            UploaderName = await MyDisplayNameAsync(),
            UploaderUserId = MyUserId
        };
        Db.AppAttachments.Add(att);
        await Db.SaveChangesAsync();
        return Ok(new { id = att.Id, message = "پیوست بارگذاری شد." });
    }

    [HttpGet("attachments/{attId:int}/download")]
    public async Task<IActionResult> DownloadAttachment(int attId)
    {
        var a = await Db.AppAttachments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attId && x.Module == "IncomingLetters");
        if (a == null) return NotFound();

        var bytes = (a.FilePath is not null ? _store.ReadBytes(a.FilePath) : null) ?? (a.Data is { Length: > 0 } ? a.Data : null);
        if (bytes == null) return NotFound(new { message = "فایل پیوست پیدا نشد." });
        return File(bytes, a.ContentType, a.FileName);
    }

    [HttpDelete("attachments/{attId:int}")]
    public async Task<IActionResult> DeleteAttachment(int attId)
    {
        var a = await Db.AppAttachments.FirstOrDefaultAsync(x => x.Id == attId && x.Module == "IncomingLetters");
        if (a == null) return NotFound();
        if (a.UploaderUserId != MyUserId && !await IsAdminAsync())
            return StatusCode(403, new { message = "فقط بارگذارنده یا مدیر می‌تواند پیوست را حذف کند." });

        _store.Delete(a.FilePath);
        Db.AppAttachments.Remove(a);
        await Db.SaveChangesAsync();
        return Ok(new { message = "پیوست حذف شد." });
    }

    [HttpGet("pick")]
    public async Task<IActionResult> Pick([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.PickListAsync(MyUserId, search, page, pageSize));
    }

    [HttpGet("reservations")]
    public async Task<IActionResult> GetReservations([FromQuery] int typeForm = 3)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetReservationsAsync(MyUserId, typeForm));
    }

    [HttpPost("reserve-number")]
    public async Task<IActionResult> ReserveNumber([FromBody] ReserveNumberRequestDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var list = await _letters.ReserveNumberAsync(MyUserId, dto.TypeForm, dto.Count);
        return Ok(list);
    }
}
