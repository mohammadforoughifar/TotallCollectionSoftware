using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Office.Outgoing;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.Office;

/// <summary>
/// اتوماسیون اداری — کارتابل نامه صادره — ماژول دسترسی: OutgoingLetters + Sign
/// شامل: sent/received (received شامل امضایی‌ها)، gardesh، امضا با SadereNumber
/// </summary>
[Route("api/outgoing-letters")]
public class OutgoingLettersController : RbacControllerBase
{
    private const string Module = "OutgoingLetters";
    private const string AttachmentModule = "OutgoingLetters";
    private const string PishnevisAttachmentModule = "OutgoingPishnevis";

    private readonly IOutgoingLetterService _letters;
    private readonly IOutgoingPishnevisService _pishnevis;
    private readonly IErjaService _erja;
    private readonly ILetterGroupService _groups;
    private readonly IOutgoingLetterPrintService _print;

    public OutgoingLettersController(
        AppDbContext db,
        IOutgoingLetterService letters,
        IOutgoingPishnevisService pishnevis,
        IErjaService erja,
        ILetterGroupService groups,
        IOutgoingLetterPrintService print) : base(db)
    {
        _letters = letters;
        _pishnevis = pishnevis;
        _erja = erja;
        _groups = groups;
        _print = print;
    }

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

    // ==================== کارتابل — دریافتی (شامل امضا) و ارسالی ====================

    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox([FromQuery] string? search, [FromQuery] bool? unreadOnly)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetInboxAsync(MyUserId, search, unreadOnly));
    }

    [HttpGet("signing-inbox")]
    public async Task<IActionResult> SigningInbox([FromQuery] string? search, [FromQuery] bool? unsignedOnly)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetSigningInboxAsync(MyUserId, search, unsignedOnly));
    }

    [HttpGet("archive")]
    public async Task<IActionResult> Archive([FromQuery] string? search)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetArchiveAsync(MyUserId, search));
    }

    [HttpGet("sent")]
    public async Task<IActionResult> Sent([FromQuery] string? search)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetSentAsync(MyUserId, search));
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        if (!await HasAsync(Module, "Read")) return Ok(new OutgoingLetterCartableStatsDto());
        return Ok(await _letters.GetStatsAsync(MyUserId));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var dto = await _letters.GetDetailAsync(id, MyUserId, await IsAdminAsync());
        return dto is null
            ? NotFound(new { message = "نامه پیدا نشد یا شما در گردش آن نیستید." })
            : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AddOutgoingLetterDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var id = await _letters.AddOutgoingLetterAsync(dto, MyUserId, await MyDisplayNameAsync());
        return Ok(new { id, message = "نامه صادره با موفقیت ثبت شد." });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Edit(int id, [FromBody] EditOutgoingLetterDto dto)
    {
        var isAdmin = await IsAdminAsync();
        if (!isAdmin && !await HasAsync(Module, "Create"))
            return StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });
        await _letters.EditAsync(id, dto, MyUserId, isAdmin);
        return Ok(new { message = "نامه صادره ویرایش شد." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var isAdmin = await IsAdminAsync();
        if (!isAdmin && !await HasAsync(Module, "Create"))
            return StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });
        await _letters.DeleteAsync(id, MyUserId, isAdmin);
        return Ok(new { message = "نامه صادره حذف شد." });
    }

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusDto dto)
    {
        var isAdmin = await IsAdminAsync();
        if (!isAdmin && !await HasAsync(Module, "Create"))
            return StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });
        await _letters.UpdateStatusAsync(id, dto.Status, MyUserId, isAdmin);
        return Ok(new { message = "وضعیت نامه به‌روزرسانی شد." });
    }

    public class UpdateStatusDto { public int Status { get; set; } }

    [HttpGet("pick")]
    public async Task<IActionResult> Pick([FromQuery] string? search)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.PickListAsync(MyUserId, search));
    }

    // ==================== امضا کنندگان — بر اساس دسترسی OutgoingLetters.Sign ====================

    [HttpGet("{id:int}/signers")]
    public async Task<IActionResult> Signers(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (!await InFlowAsync(id) && !await IsAdminAsync())
        {
            var isSigner = await Db.OutgoingLetterSigners.AnyAsync(s => s.SourceId == id && s.UserId == MyUserId && !s.IsDelete);
            if (!isSigner) return StatusCode(403, new { message = "شما در گردش این نامه نیستید." });
        }
        return Ok(await _letters.GetSignersAsync(id));
    }

    [HttpPost("{id:int}/sign")]
    public async Task<IActionResult> Sign(int id, [FromBody] SignOutgoingLetterDto? dto)
    {
        // اگر کاربر امضا کننده باشد حتی بدون پرمیشن Sign اجازه بده (انعطاف برای ادمین)
        var isSigner = await Db.OutgoingLetterSigners.AnyAsync(s => s.SourceId == id && s.UserId == MyUserId && !s.IsDelete);
        if (!isSigner && await ForbiddenUnlessAsync(Module, "Sign") is { } forbid) return forbid;
        if (!isSigner) return BadRequest(new { message = "شما جزو امضا کنندگان این نامه نیستید." });

        await _letters.SignAsync(id, MyUserId, dto?.SignNote);
        return Ok(new { message = "نامه با موفقیت امضا شد و شماره صادره تخصیص یافت." });
    }

    [HttpGet("available-signers")]
    public async Task<IActionResult> AvailableSigners([FromQuery] string? search)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetAvailableSignersAsync(search));
    }

    // ==================== دبیرخانه نامه صادره ====================
    // فقط نامه‌های امضا شده (SadereNumber دار) وارد دبیرخانه می‌شوند.

    private async Task<bool> HasDabirkhaneAsync() =>
        await HasAsync(Module, "Dabirkhane") || await IsAdminAsync();

    [HttpGet("dabirkhane")]
    public async Task<IActionResult> Dabirkhane([FromQuery] string? search, [FromQuery] bool? registeredOnly)
    {
        if (!await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به دبیرخانه نامه صادره دسترسی ندارید." });
        return Ok(await _letters.GetDabirkhaneAsync(search, registeredOnly));
    }

    [HttpGet("dabirkhane/stats")]
    public async Task<IActionResult> DabirkhaneStats()
    {
        if (!await HasDabirkhaneAsync()) return Ok(new DabirkhaneStatsDto());
        return Ok(await _letters.GetDabirkhaneStatsAsync());
    }

    /// <summary>ثبت دبیرخانه: شماره ثبت مقصد + روش ارسال + توضیح</summary>
    [HttpPost("{id:int}/dabirkhane")]
    public async Task<IActionResult> DabirkhaneRegister(int id, [FromBody] DabirkhaneRegisterDto dto)
    {
        if (!await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به دبیرخانه نامه صادره دسترسی ندارید." });
        await _letters.DabirkhaneRegisterAsync(id, dto, MyUserId, await MyDisplayNameAsync());
        return Ok(new { message = "نامه در دبیرخانه ثبت شد." });
    }

    /// <summary>شرکت‌های فعال — برای انتخاب سربرگ نامه صادره</summary>
    [HttpGet("companies")]
    public async Task<IActionResult> Companies()
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetCompaniesAsync());
    }

    // ==================== چاپ نامه روی سربرگ شرکت (A4 / A5) ====================

    /// <summary>چاپ نامه صادره — خروجی PDF روی سربرگ شرکت (فایل سربرگ از مسیر روت API)</summary>
    [HttpGet("{id:int}/print")]
    public async Task<IActionResult> Print(int id, [FromQuery] string size = "A4")
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (!await InFlowAsync(id) && !await IsAdminAsync() && !await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید." });

        if (!string.Equals(size, "A4", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "سایز چاپ فقط A4 یا A5 است." });

        var pdf = await _print.GeneratePdfAsync(id, size);
        if (pdf == null) return NotFound(new { message = "نامه پیدا نشد." });

        var letter = await Db.OutgoingLetters.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        var number = letter?.SadereNumber ?? letter?.LetterNumber ?? id.ToString();
        var fileName = $"letter-{number.Replace('/', '-')}-{size.ToUpper()}.pdf";
        return File(pdf, "application/pdf", fileName);
    }

    // ==================== گردش / ارجاع ====================

    [HttpGet("{id:int}/gardesh")]
    public async Task<IActionResult> Gardesh(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _erja.GetGardeshTreeAsync(id, MyUserId, await IsAdminAsync()));
    }

    [HttpPost("erja")]
    public async Task<IActionResult> AddErja([FromBody] AddErjaDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Erja") is { } forbid) return forbid;
        await _erja.AddErjaAsync(dto, MyUserId, await MyDisplayNameAsync());
        return Ok(new { message = "ارجاع با موفقیت ثبت شد." });
    }

    [HttpPost("erja/{erjaId:int}/answer")]
    public async Task<IActionResult> Answer(int erjaId, [FromBody] AnswerErjaDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        await _erja.AnswerAsync(erjaId, dto, MyUserId, await MyDisplayNameAsync());
        return Ok(new { message = "پاسخ ثبت شد." });
    }

    [HttpPost("erja/{erjaId:int}/read")]
    public async Task<IActionResult> MarkRead(int erjaId)
    {
        await _erja.MarkReadAsync(erjaId, MyUserId);
        return Ok();
    }

    [HttpPost("erja/{erjaId:int}/neshan")]
    public async Task<IActionResult> ToggleNeshan(int erjaId)
    {
        var isNeshan = await _erja.ToggleNeshanAsync(erjaId, MyUserId);
        return Ok(new { isNeshan });
    }

    [HttpPost("erja/{erjaId:int}/bayegani")]
    public async Task<IActionResult> ToggleBayegani(int erjaId)
    {
        var isBayegani = await _erja.ToggleBayeganiAsync(erjaId, MyUserId);
        return Ok(new { isBayegani });
    }

    [HttpGet("amalgars")]
    public async Task<IActionResult> Amalgars() => Ok(await _erja.GetAmalgarsAsync());

    // ==================== پیش‌نویس ====================

    [HttpGet("pishnevis")]
    public async Task<IActionResult> PishnevisList([FromQuery] string? search)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _pishnevis.GetAllAsync(MyUserId, search));
    }

    [HttpGet("pishnevis/{id:int}")]
    public async Task<IActionResult> PishnevisGet(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var p = await _pishnevis.GetByIdAsync(id, MyUserId);
        return p is null ? NotFound(new { message = "پیش‌نویس پیدا نشد." }) : Ok(p);
    }

    [HttpPost("pishnevis")]
    public async Task<IActionResult> PishnevisSave([FromBody] OutgoingPishnevisDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        if (dto.PishnevisId > 0)
        {
            await _pishnevis.EditAsync(dto, MyUserId);
            return Ok(new { id = dto.PishnevisId, message = "پیش‌نویس ویرایش شد." });
        }
        var id = await _pishnevis.AddAsync(dto, MyUserId);
        return Ok(new { id, message = "پیش‌نویس ذخیره شد." });
    }

    [HttpDelete("pishnevis/{id:int}")]
    public async Task<IActionResult> PishnevisDelete(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        await _pishnevis.DeleteAsync(id, MyUserId);
        return Ok(new { message = "پیش‌نویس حذف شد." });
    }

    // ==================== گیرندگان داخلی ====================

    [HttpGet("recivers")]
    public async Task<IActionResult> Recivers()
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var users = await Db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Id != MyUserId)
            .OrderBy(u => u.FirstName).ThenBy(u => u.Username)
            .Select(u => new LetterReciverDto
            {
                UserId = u.Id,
                FullName = string.IsNullOrEmpty(u.FirstName + u.LastName)
                    ? u.Username
                    : (u.FirstName + " " + u.LastName).Trim()
            })
            .ToListAsync();
        return Ok(users);
    }

    // ==================== گروه‌های گیرندگان ====================

    [HttpGet("groups")]
    public async Task<IActionResult> Groups([FromQuery] bool withMembers = true)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _groups.GetAllAsync(withMembers));
    }

    // ==================== پیوست‌ها ====================

    private async Task<bool> InFlowAsync(int letterId)
    {
        var letter = await Db.OutgoingLetters.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) return false;
        if (letter.CreatorUserId == MyUserId) return true;
        if (await Db.Erjas.AnyAsync(e => e.SourceId == letterId && e.ReciverUserId == MyUserId && !e.IsDelete)) return true;
        if (await Db.OutgoingLetterSigners.AnyAsync(s => s.SourceId == letterId && s.UserId == MyUserId && !s.IsDelete)) return true;
        return false;
    }

    [HttpGet("{id:int}/attachments")]
    public async Task<IActionResult> Attachments(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (!await InFlowAsync(id) && !await IsAdminAsync())
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید." });

        var list = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == AttachmentModule && a.RefId == id)
            .OrderBy(a => a.Id)
            .Select(a => new LetterAttachmentDto
            {
                Id = a.Id,
                FileName = a.FileName,
                ContentType = a.ContentType,
                Size = a.Data.Length,
                UploaderName = a.UploaderName,
                UploaderUserId = a.UploaderUserId,
                UploadedAt = a.UploadedAt
            })
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("{id:int}/attachments")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile file)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        if (!await InFlowAsync(id) && !await IsAdminAsync())
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید." });

        if (file == null || file.Length <= 0)
            return BadRequest(new { message = "فایل خالی است و قابل بارگذاری نیست." });
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { message = "حداکثر حجم هر فایل ۲۰ مگابایت است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var att = new AppAttachment
        {
            Module = AttachmentModule,
            RefId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            Data = ms.ToArray(),
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
            .FirstOrDefaultAsync(x => x.Id == attId && (x.Module == AttachmentModule || x.Module == PishnevisAttachmentModule));
        if (a == null) return NotFound();
        if (a.Module == PishnevisAttachmentModule)
        {
            var owns = await Db.OutgoingPishnevisLetters.AnyAsync(p => p.PishnevisId == a.RefId && p.UserId == MyUserId);
            if (!owns && !await IsAdminAsync())
                return StatusCode(403, new { message = "پیش‌نویس متعلق به شما نیست." });
        }
        else if (!await InFlowAsync(a.RefId) && !await IsAdminAsync())
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید." });
        return File(a.Data, a.ContentType, a.FileName);
    }

    [HttpGet("pishnevis/{id:int}/attachments")]
    public async Task<IActionResult> PishnevisAttachments(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var owns = await Db.OutgoingPishnevisLetters.AnyAsync(p => p.PishnevisId == id && p.UserId == MyUserId && !p.IsDelete);
        if (!owns) return StatusCode(403, new { message = "پیش‌نویس متعلق به شما نیست." });

        var list = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == PishnevisAttachmentModule && a.RefId == id)
            .OrderBy(a => a.Id)
            .Select(a => new LetterAttachmentDto
            {
                Id = a.Id,
                FileName = a.FileName,
                ContentType = a.ContentType,
                Size = a.Data.Length,
                UploaderName = a.UploaderName,
                UploaderUserId = a.UploaderUserId,
                UploadedAt = a.UploadedAt
            })
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("pishnevis/{id:int}/attachments")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadPishnevisAttachment(int id, IFormFile file)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var owns = await Db.OutgoingPishnevisLetters.AnyAsync(p => p.PishnevisId == id && p.UserId == MyUserId && !p.IsDelete);
        if (!owns) return StatusCode(403, new { message = "پیش‌نویس متعلق به شما نیست." });

        if (file == null || file.Length <= 0)
            return BadRequest(new { message = "فایل خالی است و قابل بارگذاری نیست." });
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { message = "حداکثر حجم هر فایل ۲۰ مگابایت است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        Db.AppAttachments.Add(new AppAttachment
        {
            Module = PishnevisAttachmentModule,
            RefId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            Data = ms.ToArray(),
            UploaderName = await MyDisplayNameAsync(),
            UploaderUserId = MyUserId
        });
        await Db.SaveChangesAsync();
        return Ok(new { message = "پیوست بارگذاری شد." });
    }

    [HttpDelete("attachments/{attId:int}")]
    public async Task<IActionResult> DeleteAttachment(int attId)
    {
        var a = await Db.AppAttachments.FirstOrDefaultAsync(x => x.Id == attId && (x.Module == AttachmentModule || x.Module == PishnevisAttachmentModule));
        if (a == null) return NotFound();
        if (a.UploaderUserId != MyUserId && !await IsAdminAsync())
            return StatusCode(403, new { message = "فقط بارگذارنده یا مدیر می‌تواند پیوست را حذف کند." });
        Db.AppAttachments.Remove(a);
        await Db.SaveChangesAsync();
        return Ok(new { message = "پیوست حذف شد." });
    }
}
