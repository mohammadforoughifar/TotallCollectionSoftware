using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// اتوماسیون اداری — کارتابل نامه داخلی — ماژول دسترسی: InnerLetters
/// </summary>
[Route("api/letters")]
public class InnerLettersController : RbacControllerBase
{
    private const string Module = "InnerLetters";

    private readonly IInnerLetterService _letters;
    private readonly IErjaService _erja;
    private readonly IPishnevisService _pishnevis;
    private readonly ILetterGroupService _groups;
    private readonly IArchiveService _archive;
    private readonly FileStore _store;

    public InnerLettersController(AppDbContext db, IInnerLetterService letters, IErjaService erja, IPishnevisService pishnevis, ILetterGroupService groups, IArchiveService archive, FileStore store)
        : base(db)
    {
        _letters = letters;
        _erja = erja;
        _pishnevis = pishnevis;
        _groups = groups;
        _archive = archive;
        _store = store;
    }

    private async Task<bool> IsAdminAsync() => await HasAsync(Module, "Delete");

    /// <summary>نام نمایشی کاربر جاری (نام و نام خانوادگی، وگرنه نام کاربری)</summary>
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

    // ==================== کارتابل ====================

    /// <summary>صندوق وارده کاربر جاری</summary>
    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox([FromQuery] string? search, [FromQuery] bool? unreadOnly)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetInboxAsync(MyUserId, search, unreadOnly));
    }

    /// <summary>پوشه بایگانی کاربر جاری</summary>
    [HttpGet("archive")]
    public async Task<IActionResult> Archive([FromQuery] string? search)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetArchiveAsync(MyUserId, search));
    }

    /// <summary>نامه‌های ارسالی کاربر جاری</summary>
    [HttpGet("sent")]
    public async Task<IActionResult> Sent([FromQuery] string? search)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetSentAsync(MyUserId, search));
    }

    /// <summary>آمار کارتابل (شمارنده نخوانده‌ها و…)</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        if (!await HasAsync(Module, "Read")) return Ok(new LetterCartableStatsDto());
        return Ok(await _letters.GetStatsAsync(MyUserId));
    }

    /// <summary>جزئیات نامه — فقط فرستنده/گیرندگان/مدیر</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var dto = await _letters.GetDetailAsync(id, MyUserId, await IsAdminAsync());
        return dto is null
            ? NotFound(new { message = "نامه پیدا نشد یا شما در گردش آن نیستید." })
            : Ok(dto);
    }

    /// <summary>ثبت و ارسال نامه داخلی جدید</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AddInnerLetterDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var id = await _letters.AddInnerLetterAsync(dto, MyUserId, await MyDisplayNameAsync());
        return Ok(new { id, message = "نامه با موفقیت ارسال شد." });
    }

    /// <summary>ویرایش نامه — فرستنده تا قبل از خوانده‌شدن توسط هر گیرنده (مدیر: همیشه)</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Edit(int id, [FromBody] EditInnerLetterDto dto)
    {
        var isAdmin = await IsAdminAsync();
        if (!isAdmin && !await HasAsync(Module, "Create"))
            return StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });
        await _letters.EditAsync(id, dto, MyUserId, isAdmin);
        return Ok(new { message = "نامه ویرایش شد." });
    }

    /// <summary>حذف نرم نامه — فرستنده (تا قبل از خوانده‌شدن) یا مدیر</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var isAdmin = await IsAdminAsync();
        if (!isAdmin && !await HasAsync(Module, "Create"))
            return StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });
        await _letters.DeleteAsync(id, MyUserId, isAdmin);
        return Ok(new { message = "نامه حذف شد." });
    }

    /// <summary>لیست انتخاب نامه برای عطف/پیرو</summary>
    [HttpGet("pick")]
    public async Task<IActionResult> Pick([FromQuery] string? search)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.PickListAsync(MyUserId, search));
    }

    // ==================== گردش / ارجاع ====================

    /// <summary>درخت گردش کامل نامه</summary>
    [HttpGet("{id:int}/gardesh")]
    public async Task<IActionResult> Gardesh(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _erja.GetGardeshTreeAsync(id, MyUserId, await IsAdminAsync()));
    }

    /// <summary>ارجاع نامه به کاربر(ان) دیگر</summary>
    [HttpPost("erja")]
    public async Task<IActionResult> AddErja([FromBody] AddErjaDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Erja") is { } forbid) return forbid;
        await _erja.AddErjaAsync(dto, MyUserId, await MyDisplayNameAsync());
        return Ok(new { message = "ارجاع با موفقیت ثبت شد." });
    }

    /// <summary>پاسخ/اقدام روی ارجاع (+ تایید/رد)</summary>
    [HttpPost("erja/{erjaId:int}/answer")]
    public async Task<IActionResult> Answer(int erjaId, [FromBody] AnswerErjaDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        await _erja.AnswerAsync(erjaId, dto, MyUserId, await MyDisplayNameAsync());
        return Ok(new { message = "پاسخ ثبت شد." });
    }

    /// <summary>ثبت خوانده‌شدن ارجاع</summary>
    [HttpPost("erja/{erjaId:int}/read")]
    public async Task<IActionResult> MarkRead(int erjaId)
    {
        await _erja.MarkReadAsync(erjaId, MyUserId);
        return Ok();
    }

    /// <summary>نشان‌کردن/برداشتن نشان (ستاره) — نامه دریافتی (روی ارجاع کاربر)</summary>
    [HttpPost("erja/{erjaId:int}/neshan")]
    public async Task<IActionResult> ToggleNeshan(int erjaId)
    {
        var isNeshan = await _erja.ToggleNeshanAsync(erjaId, MyUserId);
        return Ok(new { isNeshan });
    }

    /// <summary>نشان‌کردن/برداشتن نشان (ستاره) — نامه ارسالی (روی خود نامه، توسط فرستنده)</summary>
    [HttpPost("{id:int}/neshan")]
    public async Task<IActionResult> ToggleLetterNeshan(int id)
    {
        var isNeshan = await _letters.ToggleLetterNeshanAsync(id, MyUserId);
        return Ok(new { isNeshan });
    }

    /// <summary>بایگانی / خروج از بایگانی نامه دریافتی</summary>
    [HttpPost("erja/{erjaId:int}/bayegani")]
    public async Task<IActionResult> ToggleBayegani(int erjaId)
    {
        var isBayegani = await _erja.ToggleBayeganiAsync(erjaId, MyUserId);
        return Ok(new { isBayegani });
    }

    // ==================== بایگانی درختی ====================

    /// <summary>درخت کامل بایگانی کاربر جاری (پوشه‌ها + نامه‌ها)</summary>
    [HttpGet("bayegani/tree")]
    public async Task<IActionResult> BayeganiTree() => Ok(await _archive.GetTreeAsync(MyUserId));

    /// <summary>ایجاد دسته اصلی بایگانی (ریشه)</summary>
    [HttpPost("bayegani/main-category")]
    public async Task<IActionResult> AddMainCategory([FromBody] SaveBayeganiFolderDto dto)
        => Ok(await _archive.AddMainCategoryAsync(MyUserId, dto));

    /// <summary>ایجاد زیرپوشه بایگانی</summary>
    [HttpPost("bayegani/sub-category")]
    public async Task<IActionResult> AddSubCategory([FromBody] SaveBayeganiFolderDto dto)
        => Ok(await _archive.AddSubCategoryAsync(MyUserId, dto));

    /// <summary>ویرایش عنوان پوشه بایگانی</summary>
    [HttpPut("bayegani/folder/{id:int}")]
    public async Task<IActionResult> EditBayeganiFolder(int id, [FromBody] SaveBayeganiFolderDto dto)
        => Ok(await _archive.EditFolderAsync(id, MyUserId, dto));

    /// <summary>جابجایی پوشه بایگانی به والد جدید (0 = ریشه)</summary>
    [HttpPost("bayegani/move-folder/{id:int}")]
    public async Task<IActionResult> MoveBayeganiFolder(int id, [FromQuery] int newParentId)
        => Ok(await _archive.MoveFolderAsync(id, newParentId, MyUserId));

    /// <summary>بایگانی یک یا چند نامه در پوشه انتخابی</summary>
    [HttpPost("bayegani/letters")]
    public async Task<IActionResult> ArchiveLetters([FromBody] ArchiveLettersDto dto)
    {
        await _archive.AddLettersToArchiveAsync(MyUserId, dto);
        return Ok();
    }

    /// <summary>جابجایی نامه بایگانی‌شده به پوشه دیگر</summary>
    [HttpPost("bayegani/move-letter/{id:int}")]
    public async Task<IActionResult> MoveBayeganiLetter(int id, [FromQuery] int newParentId)
        => Ok(await _archive.MoveLetterAsync(id, newParentId, MyUserId));

    /// <summary>حذف پوشه خالی / خروج نامه از بایگانی</summary>
    [HttpDelete("bayegani/{id:int}")]
    public async Task<IActionResult> DeleteBayegani(int id)
    {
        await _archive.DeleteAsync(id, MyUserId);
        return Ok();
    }

    /// <summary>خروج نامه از بایگانی بر اساس شناسه ارجاع</summary>
    [HttpPost("bayegani/unarchive/{erjaId:int}")]
    public async Task<IActionResult> UnarchiveByErja(int erjaId)
    {
        await _archive.UnarchiveByErjaAsync(erjaId, MyUserId);
        return Ok();
    }

    /// <summary>خروج نامه ارسالی از بایگانی بر اساس شناسه نامه (مسیر فرستنده)</summary>
    [HttpPost("bayegani/unarchive-letter/{letterId:int}")]
    public async Task<IActionResult> UnarchiveByLetter(int letterId)
    {
        await _archive.UnarchiveByLetterAsync(letterId, MyUserId);
        return Ok();
    }

    /// <summary>لیست عملگرهای ارجاع</summary>
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
    public async Task<IActionResult> PishnevisSave([FromBody] PishnevisDto dto)
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

    // ==================== گیرندگان (کاربران فعال) ====================

    /// <summary>لیست کاربران فعال برای انتخاب گیرنده</summary>
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

    // ==================== گروه‌های گیرندگان (پورت Groups کارفرما) ====================

    /// <summary>لیست گروه‌های فعال — با اعضا (برای نمایش در کمبوی گروهی)</summary>
    [HttpGet("groups")]
    public async Task<IActionResult> Groups([FromQuery] bool withMembers = true)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _groups.GetAllAsync(withMembers));
    }

    /// <summary>ایجاد/ویرایش گروه گیرندگان</summary>
    [HttpPost("groups")]
    public async Task<IActionResult> SaveGroup([FromBody] SaveLetterGroupDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var id = await _groups.SaveAsync(dto, MyUserId);
        return Ok(new { id, message = "گروه ذخیره شد." });
    }

    /// <summary>حذف نرم گروه — سازنده یا مدیر</summary>
    [HttpDelete("groups/{id:int}")]
    public async Task<IActionResult> DeleteGroup(int id)
    {
        var isAdmin = await IsAdminAsync();
        if (!isAdmin && !await HasAsync(Module, "Create"))
            return StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });
        await _groups.DeleteAsync(id, MyUserId, isAdmin);
        return Ok(new { message = "گروه حذف شد." });
    }

    // ==================== پیوست‌ها (AppAttachment، Module="InnerLetters") ====================

    private async Task<bool> InFlowAsync(int letterId)
    {
        var letter = await Db.InnerLetters.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) return false;
        if (letter.CreatorUserId == MyUserId) return true;
        return await Db.Erjas.AnyAsync(e => e.SourceId == letterId && e.ReciverUserId == MyUserId && !e.IsDelete);
    }

    /// <summary>لیست پیوست‌های نامه — فقط افراد در گردش یا مدیر</summary>
    [HttpGet("{id:int}/attachments")]
    public async Task<IActionResult> Attachments(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (!await InFlowAsync(id) && !await IsAdminAsync())
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید." });

        var rows = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == Module && a.RefId == id)
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

    /// <summary>
    /// آیا کاربر جاری اجازه افزودن پیوست به این نامه را دارد؟
    /// قاعده مصوب: فقط «فرستنده نامه» (بعد از ارسال) می‌تواند پیوست اضافه کند.
    /// </summary>
    private async Task<bool> CanAttachAsync(int letterId)
    {
        return await Db.InnerLetters.AsNoTracking()
            .AnyAsync(l => l.Id == letterId && !l.IsDelete && l.CreatorUserId == MyUserId);
    }

    /// <summary>
    /// آپلود پیوست — بدون محدودیت تعداد؛ هر فایل حداکثر ۲۰ مگابایت؛ فایل خالی رد می‌شود.
    /// فقط فرستنده نامه می‌تواند پیوست اضافه کند (قاعده مصوب کارفرما).
    /// فایل‌ها در wwwroot/uploads/innerletter/{letterId} ذخیره می‌شوند و در DB فقط اطلاعات + مسیر (FilePath) ثبت می‌شود.
    /// </summary>
    [HttpPost("{id:int}/attachments")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile file)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        if (!await CanAttachAsync(id))
            return StatusCode(403, new { message = "فقط فرستنده نامه می‌تواند پیوست اضافه کند." });

        if (file == null || file.Length <= 0)
            return BadRequest(new { message = "فایل خالی است و قابل بارگذاری نیست." });
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { message = "حداکثر حجم هر فایل ۲۰ مگابایت است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        // فایل روی دیسک در wwwroot/uploads/innerletter/{letterId} ذخیره می‌شود
        var relPath = await _store.SaveAsync("innerletter", id, ms, file.FileName);

        var att = new AppAttachment
        {
            Module = Module,
            RefId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            FilePath = relPath,
            Data = Array.Empty<byte>(),
            UploaderName = await MyDisplayNameAsync(),
            UploaderUserId = MyUserId
        };
        Db.AppAttachments.Add(att);
        try
        {
            await Db.SaveChangesAsync();
        }
        catch
        {
            // اگر ثبت متادیتا شکست خورد، فایل بی‌صاحب روی دیسک باقی نماند.
            _store.Delete(relPath);
            throw;
        }
        return Ok(new { id = att.Id, message = "پیوست بارگذاری شد." });
    }

    /// <summary>دانلود پیوست — فقط افراد در گردش یا مدیر</summary>
    [HttpGet("attachments/{attId:int}/download")]
    public async Task<IActionResult> DownloadAttachment(int attId)
    {
        var (attachment, error) = await FindReadableAttachmentAsync(attId);
        if (error is not null) return error;

        // اول از دیسک خوانده می‌شود؛ در صورت نبود، به blob قدیمی در DB برمی‌گردد.
        var bytes = _store.ReadBytes(attachment!.FilePath)
                    ?? (attachment.Data is { Length: > 0 } ? attachment.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(bytes, attachment.ContentType, attachment.FileName);
    }

    /// <summary>
    /// مشاهده امن پیوست در مرورگر — فقط PDF، تصاویر بی‌خطر و فایل‌های متنی.
    /// این endpoint مانند دانلود به JWT و عضویت کاربر در گردش نامه نیاز دارد؛ مسیر فیزیکی فایل افشا نمی‌شود.
    /// </summary>
    [HttpGet("attachments/{attId:int}/preview")]
    public async Task<IActionResult> PreviewAttachment(int attId)
    {
        var (attachment, error) = await FindReadableAttachmentAsync(attId);
        if (error is not null) return error;

        var bytes = _store.ReadBytes(attachment!.FilePath)
                    ?? (attachment.Data is { Length: > 0 } ? attachment.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });

        var contentType = SafeInlineContentType(attachment.FileName, attachment.ContentType);
        if (contentType is null)
            return BadRequest(new { message = "این نوع فایل پیش‌نمایش مستقیم ندارد؛ آن را دانلود کنید." });

        // فایل HTML/SVG یا Content-Type ارسالی کاربر هرگز با نوع فعال از دامنه برنامه اجرا نمی‌شود.
        Response.Headers["Content-Disposition"] = "inline";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";

        return new FileContentResult(bytes, contentType) { EnableRangeProcessing = true };
    }

    /// <summary>
    /// پیش‌نمایش امن Word جدید، Excel جدید و CSV به HTML پاک‌سازی‌شده؛ فایل اصلی عمومی نمی‌شود.
    /// </summary>
    [HttpGet("attachments/{attId:int}/preview-html")]
    public async Task<IActionResult> PreviewOfficeAttachment(int attId)
    {
        var (attachment, error) = await FindReadableAttachmentAsync(attId);
        if (error is not null) return error;

        var bytes = _store.ReadBytes(attachment!.FilePath)
                    ?? (attachment.Data is { Length: > 0 } ? attachment.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });

        if (!OfficePreviewHtml.TryBuild(bytes, attachment.FileName, attachment.ContentType, out var html, out var previewError))
            return BadRequest(new { message = previewError ?? "پیش‌نمایش این فایل ممکن نیست؛ آن را دانلود کنید." });

        Response.Headers["Content-Disposition"] = "inline";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; style-src 'unsafe-inline'";
        return Content(html, "text/html; charset=utf-8");
    }

    private async Task<(AppAttachment? Attachment, IActionResult? Error)> FindReadableAttachmentAsync(int attId)
    {
        var attachment = await Db.AppAttachments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attId && (x.Module == Module || x.Module == "Pishnevis"));
        if (attachment is null) return (null, NotFound());

        if (attachment.Module == "Pishnevis")
        {
            var owns = await Db.PishnevisLetters.AsNoTracking()
                .AnyAsync(p => p.PishnevisId == attachment.RefId && p.UserId == MyUserId && !p.IsDelete);
            if (!owns && !await IsAdminAsync())
                return (null, StatusCode(403, new { message = "پیش‌نویس متعلق به شما نیست." }));
        }
        else if (!await InFlowAsync(attachment.RefId) && !await IsAdminAsync())
        {
            return (null, StatusCode(403, new { message = "شما در گردش این نامه نیستید." }));
        }

        return (attachment, null);
    }

    private static string? SafeInlineContentType(string fileName, string? storedContentType)
    {
        // نوع نمایش از پسوند مورد اعتماد سامانه تعیین می‌شود، نه Content-Type قابل جعلِ درخواست آپلود.
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" or ".jfif" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            ".avif" => "image/avif",
            ".txt" or ".log" or ".md" or ".csv" or ".json" or ".xml" or ".html" or ".htm" => "text/plain; charset=utf-8",
            _ when string.Equals(storedContentType, "application/pdf", StringComparison.OrdinalIgnoreCase) => "application/pdf",
            _ => null
        };
    }

    /// <summary>لیست پیوست‌های پیش‌نویس کاربر جاری</summary>
    [HttpGet("pishnevis/{id:int}/attachments")]
    public async Task<IActionResult> PishnevisAttachments(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var owns = await Db.PishnevisLetters.AnyAsync(p => p.PishnevisId == id && p.UserId == MyUserId && !p.IsDelete);
        if (!owns) return StatusCode(403, new { message = "پیش‌نویس متعلق به شما نیست." });

        var rows = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == "Pishnevis" && a.RefId == id)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.FilePath, a.Data, a.UploaderName, a.UploaderUserId, a.UploadedAt })
            .ToListAsync();

        var list = rows.Select(a => new LetterAttachmentDto
        {
            Id = a.Id, FileName = a.FileName, ContentType = a.ContentType,
            Size = a.FilePath is not null ? _store.Size(a.FilePath) : (long)a.Data.Length, UploaderName = a.UploaderName,
            UploaderUserId = a.UploaderUserId, UploadedAt = a.UploadedAt
        }).ToList();
        return Ok(list);
    }

    /// <summary>
    /// آپلود پیوست روی پیش‌نویس — همان قواعد ۲۰ مگابایت/فایل خالی.
    /// فایل‌ها در wwwroot/uploads/innerletter/pishnevis/{pishnevisId} ذخیره می‌شوند.
    /// </summary>
    [HttpPost("pishnevis/{id:int}/attachments")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadPishnevisAttachment(int id, IFormFile file)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var owns = await Db.PishnevisLetters.AnyAsync(p => p.PishnevisId == id && p.UserId == MyUserId && !p.IsDelete);
        if (!owns) return StatusCode(403, new { message = "پیش‌نویس متعلق به شما نیست." });

        if (file == null || file.Length <= 0)
            return BadRequest(new { message = "فایل خالی است و قابل بارگذاری نیست." });
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { message = "حداکثر حجم هر فایل ۲۰ مگابایت است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        // فایل پیش‌نویس زیر wwwroot/uploads/innerletter/pishnevis/{pishnevisId} ذخیره می‌شود
        var relPath = await _store.SaveAsync("innerletter", id, ms, file.FileName, "pishnevis");

        Db.AppAttachments.Add(new AppAttachment
        {
            Module = "Pishnevis",
            RefId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            FilePath = relPath,
            Data = Array.Empty<byte>(),
            UploaderName = await MyDisplayNameAsync(),
            UploaderUserId = MyUserId
        });
        try
        {
            await Db.SaveChangesAsync();
        }
        catch
        {
            _store.Delete(relPath);
            throw;
        }
        return Ok(new { message = "پیوست بارگذاری شد." });
    }

    /// <summary>حذف پیوست — فقط آپلودکننده یا مدیر</summary>
    [HttpDelete("attachments/{attId:int}")]
    public async Task<IActionResult> DeleteAttachment(int attId)
    {
        var a = await Db.AppAttachments.FirstOrDefaultAsync(x => x.Id == attId && (x.Module == Module || x.Module == "Pishnevis"));
        if (a == null) return NotFound();
        if (a.UploaderUserId != MyUserId && !await IsAdminAsync())
            return StatusCode(403, new { message = "فقط بارگذارنده یا مدیر می‌تواند پیوست را حذف کند." });

        var filePath = a.FilePath;
        Db.AppAttachments.Remove(a);
        await Db.SaveChangesAsync();
        // ابتدا حذف متادیتا قطعی می‌شود؛ در خطای دیتابیس فایل معتبر از بین نمی‌رود.
        _store.Delete(filePath);
        return Ok(new { message = "پیوست حذف شد." });
    }
}
