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
    private readonly IArchiveService _archive;
    private readonly FileStore _store;
    private readonly ILogger<OutgoingLettersController> _logger;

    /// <summary>پوشه پیوست‌های نامه صادره در wwwroot/uploads/office/outgoingletter</summary>
    private const string SadereFolder = "office/outgoingletter";

    public OutgoingLettersController(
        AppDbContext db,
        IOutgoingLetterService letters,
        IOutgoingPishnevisService pishnevis,
        IErjaService erja,
        ILetterGroupService groups,
        IOutgoingLetterPrintService print,
        IArchiveService archive,
        FileStore store,
        ILogger<OutgoingLettersController> logger) : base(db)
    {
        _letters = letters;
        _pishnevis = pishnevis;
        _erja = erja;
        _groups = groups;
        _print = print;
        _archive = archive;
        _store = store;
        _logger = logger;
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
    public async Task<IActionResult> Inbox([FromQuery] string? search, [FromQuery] bool? unreadOnly, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetInboxAsync(MyUserId, search, unreadOnly, page, pageSize));
    }

    [HttpGet("signing-inbox")]
    public async Task<IActionResult> SigningInbox([FromQuery] string? search, [FromQuery] bool? unsignedOnly, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetSigningInboxAsync(MyUserId, search, unsignedOnly, page, pageSize));
    }

    [HttpGet("archive")]
    public async Task<IActionResult> Archive([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetArchiveAsync(MyUserId, search, page, pageSize));
    }

    [HttpGet("sent")]
    public async Task<IActionResult> Sent([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetSentAsync(MyUserId, search, page, pageSize));
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
        var isAdmin = await IsAdminAsync();
        var hasDabirkhane = await HasDabirkhaneAsync();

        if (!hasDabirkhane && !isAdmin && await ForbiddenUnlessAsync(Module, "Read") is { } forbid)
            return forbid;

        // کاربر دبیرخانه مجاز به مشاهدهٔ نامه است حتی اگر در گردش آن نباشد
        try
        {
            var dto = await _letters.GetDetailAsync(id, MyUserId, isAdmin || hasDabirkhane);
            return dto is null
                ? NotFound(new { message = "نامه پیدا نشد یا شما در گردش آن نیستید." })
                : Ok(dto);
        }
        catch (Exception ex)
        {
            // از تبدیل خطاهای CreatorId/اسکیما به 400 بدون توضیح جلوگیری می‌کند.
            return BadRequest(new { message = ex.Message, detail = "جزئیات نامه صادره از دیتابیس خوانده نشد؛ اسکیمای OutgoingLetters و CreatorId را بررسی کنید." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AddOutgoingLetterDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        try
        {
            var id = await _letters.AddOutgoingLetterAsync(dto, MyUserId, await MyDisplayNameAsync());
            return Ok(new { id, message = "نامه صادره با موفقیت ثبت شد." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ثبت نامه صادره ناموفق بود. UserId={UserId}, CompanyId={CompanyId}, SignerCount={SignerCount}",
                MyUserId, dto.CompanyId, (dto.SignerUserIds?.Count ?? 0) + (dto.SignerGroupIds?.Count ?? 0));
            return BadRequest(new
            {
                message = ex.GetBaseException().Message,
                detail = "ثبت نامه صادره انجام نشد؛ جزئیات کامل در لاگ Inventory.Api ثبت شد."
            });
        }
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
        // وضعیت «صادر شده» (3) فقط با امضای همه امضاکنندگان ثبت می‌شود — از دراپ‌داون/منو حذف شده است
        if (dto.Status == 3)
            return BadRequest(new { message = "وضعیت «صادر شده» به‌صورت دستی قابل انتخاب نیست و فقط با امضای نامه ثبت می‌شود." });
        var isAdmin = await IsAdminAsync();
        if (!isAdmin && !await HasAsync(Module, "Create"))
            return StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });
        await _letters.UpdateStatusAsync(id, dto.Status, MyUserId, isAdmin);
        return Ok(new { message = "وضعیت نامه به‌روزرسانی شد." });
    }

    public class UpdateStatusDto { public int Status { get; set; } }

    [HttpGet("pick")]
    public async Task<IActionResult> Pick([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.PickListAsync(MyUserId, search, page, pageSize));
    }

    // ==================== امضا کنندگان — بر اساس دسترسی OutgoingLetters.Sign ====================

    [HttpGet("{id:int}/signers")]
    public async Task<IActionResult> Signers(int id)
    {
        var isAdmin = await IsAdminAsync();
        var hasDabirkhane = await HasDabirkhaneAsync();

        if (!hasDabirkhane && !isAdmin && await ForbiddenUnlessAsync(Module, "Read") is { } forbid)
            return forbid;

        // دبیرخانه برای مشاهدهٔ گردش نامه باید امضا کنندگان را هم ببیند
        if (!hasDabirkhane && !isAdmin && !await InFlowAsync(id))
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

    // ==================== رونوشت‌گیرندگان (جدول مستقل) ====================

    /// <summary>فهرست رونوشت‌گیرندگان نامه — منبع اصلی برای فرم و چاپ «با رونوشت»</summary>
    [HttpGet("{id:int}/copy-tos")]
    public async Task<IActionResult> GetCopyTos(int id)
    {
        var isAdmin = await IsAdminAsync();
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        try
        {
            return Ok(await _letters.GetCopyTosAsync(id, MyUserId, isAdmin));
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// جایگزینی کامل فهرست رونوشت‌ها — کاربر در فرم آن‌ها را یکی‌یکی با دکمهٔ + می‌سازد
    /// و اینجا کل فهرست یکجا ذخیره می‌شود (افزوده/ویرایش/حذف بر اساس شناسه تشخیص داده می‌شود).
    /// </summary>
    [HttpPut("{id:int}/copy-tos")]
    public async Task<IActionResult> ReplaceCopyTos(int id, [FromBody] ReplaceOutgoingLetterCopyTosDto? dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        try
        {
            var items = await _letters.ReplaceCopyTosAsync(
                id, dto?.Items ?? new List<SaveOutgoingLetterCopyToDto>(), MyUserId, await IsAdminAsync());
            return Ok(new { items, message = "رونوشت‌گیرندگان ذخیره شدند." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
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

    /// <summary>
    /// لیست دبیرخانه با جستجوی پیشرفته — عبارت متنی + فیلترهای وضعیت،
    /// روش ارسال، فرستنده، شرکت، مقصد، بازهٔ تاریخ و پیوست.
    /// </summary>
    [HttpGet("dabirkhane")]
    public async Task<IActionResult> Dabirkhane(
        [FromQuery] string? search,
        [FromQuery] bool? registeredOnly,
        [FromQuery] bool? archivedOnly,
        [FromQuery] string? sendMethod,
        [FromQuery] int? creatorUserId,
        [FromQuery] int? companyId,
        [FromQuery] string? receiverOrganization,
        [FromQuery] string? mahramanegi,
        [FromQuery] string? foriat,
        [FromQuery] bool? hasAttachment,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        if (!await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به دبیرخانه نامه صادره دسترسی ندارید." });

        var filter = new DabirkhaneSearchDto
        {
            Text = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            RegisteredOnly = registeredOnly,
            ArchivedOnly = archivedOnly,
            SendMethod = string.IsNullOrWhiteSpace(sendMethod) ? null : sendMethod.Trim(),
            CreatorUserId = creatorUserId,
            CompanyId = companyId,
            ReceiverOrganization = string.IsNullOrWhiteSpace(receiverOrganization) ? null : receiverOrganization.Trim(),
            Mahramanegi = string.IsNullOrWhiteSpace(mahramanegi) ? null : mahramanegi.Trim(),
            Foriat = string.IsNullOrWhiteSpace(foriat) ? null : foriat.Trim(),
            HasAttachment = hasAttachment,
            FromDate = fromDate,
            ToDate = toDate
        };

        return Ok(await _letters.GetDabirkhaneAsync(filter));
    }

    /// <summary>فهرست ثبت‌کنندگان نامه — برای فیلتر «فرستنده» در جستجوی پیشرفته</summary>
    [HttpGet("dabirkhane/creators")]
    public async Task<IActionResult> DabirkhaneCreators()
    {
        if (!await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به دبیرخانه نامه صادره دسترسی ندارید." });
        return Ok(await _letters.GetDabirkhaneCreatorsAsync());
    }

    // ==================== بایگانی دبیرخانه (نامه صادره) ====================
    // درخت پوشه‌های مستقل از بایگانی شخصی نامه‌های داخلی.

    /// <summary>درخت بایگانی دبیرخانه — پوشه‌ها + نامه‌های صادرهٔ بایگانی‌شده</summary>
    [HttpGet("dabirkhane/bayegani/tree")]
    public async Task<IActionResult> DabirkhaneBayeganiTree()
    {
        if (!await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به دبیرخانه نامه صادره دسترسی ندارید." });
        return Ok(await _archive.GetOutgoingTreeAsync(MyUserId));
    }

    /// <summary>ایجاد دسته اصلی در ریشهٔ بایگانی دبیرخانه</summary>
    [HttpPost("dabirkhane/bayegani/main-category")]
    public async Task<IActionResult> AddDabirkhaneMainCategory([FromBody] SaveBayeganiFolderDto dto)
    {
        if (!await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به دبیرخانه نامه صادره دسترسی ندارید." });
        return Ok(await _archive.AddOutgoingMainCategoryAsync(MyUserId, dto));
    }

    /// <summary>ایجاد زیرپوشه در بایگانی دبیرخانه</summary>
    [HttpPost("dabirkhane/bayegani/sub-category")]
    public async Task<IActionResult> AddDabirkhaneSubCategory([FromBody] SaveBayeganiFolderDto dto)
    {
        if (!await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به دبیرخانه نامه صادره دسترسی ندارید." });
        return Ok(await _archive.AddOutgoingSubCategoryAsync(MyUserId, dto));
    }

    /// <summary>ویرایش عنوان پوشهٔ بایگانی دبیرخانه</summary>
    [HttpPut("dabirkhane/bayegani/folder/{id:int}")]
    public async Task<IActionResult> EditDabirkhaneFolder(int id, [FromBody] SaveBayeganiFolderDto dto)
    {
        if (!await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به دبیرخانه نامه صادره دسترسی ندارید." });
        return Ok(await _archive.EditOutgoingFolderAsync(id, MyUserId, dto));
    }

    /// <summary>جابجایی پوشه در بایگانی دبیرخانه</summary>
    [HttpPost("dabirkhane/bayegani/folder/{id:int}/move")]
    public async Task<IActionResult> MoveDabirkhaneFolder(int id, [FromQuery] int newParentId)
    {
        if (!await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به دبیرخانه نامه صادره دسترسی ندارید." });
        return Ok(await _archive.MoveOutgoingFolderAsync(id, newParentId, MyUserId));
    }

    /// <summary>حذف پوشه/نامه از بایگانی دبیرخانه</summary>
    [HttpDelete("dabirkhane/bayegani/{id:int}")]
    public async Task<IActionResult> DeleteDabirkhaneBayegani(int id)
    {
        if (!await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به دبیرخانه نامه صادره دسترسی ندارید." });
        await _archive.DeleteOutgoingAsync(id, MyUserId);
        return Ok(new { message = "از بایگانی دبیرخانه حذف شد." });
    }

    /// <summary>افزودن یک یا چند نامه صادره به بایگانی دبیرخانه</summary>
    [HttpPost("dabirkhane/bayegani/letters")]
    public async Task<IActionResult> ArchiveDabirkhaneLetters([FromBody] ArchiveOutgoingLettersDto dto)
    {
        if (!await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به دبیرخانه نامه صادره دسترسی ندارید." });

        try
        {
            await _archive.ArchiveOutgoingLettersAsync(MyUserId, dto);
            return Ok(new { message = dto.LetterIds.Count > 1 ? "نامه‌ها به بایگانی دبیرخانه اضافه شدند." : "نامه به بایگانی دبیرخانه اضافه شد." });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception) { return BadRequest(new { message = "بایگانی نامه صادره انجام نشد؛ نامه یا پوشه انتخابی را بررسی کنید." }); }
    }

    /// <summary>خروج نامه صادره از بایگانی دبیرخانه</summary>
    [HttpDelete("dabirkhane/bayegani/letter/{letterId:int}")]
    public async Task<IActionResult> UnarchiveDabirkhaneLetter(int letterId)
    {
        if (!await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به دبیرخانه نامه صادره دسترسی ندارید." });
        await _archive.UnarchiveOutgoingLetterAsync(letterId, MyUserId);
        return Ok(new { message = "نامه از بایگانی دبیرخانه خارج شد." });
    }

    /// <summary>جابجایی نامه بایگانی‌شده به پوشه‌ای دیگر</summary>
    [HttpPost("dabirkhane/bayegani/move-letter")]
    public async Task<IActionResult> MoveDabirkhaneLetter([FromBody] MoveArchivedLetterDto dto)
    {
        if (!await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به دبیرخانه نامه صادره دسترسی ندارید." });
        return Ok(await _archive.MoveArchivedLetterAsync(dto, MyUserId));
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

    /// <summary>
    /// چاپ نامه صادره — خروجی PDF روی سربرگ شرکت (فایل سربرگ از مسیر روت API)
    /// در دو نسخه قابل چاپ است:
    /// • withCopy=true  → «با رونوشت» (بلوک رونوشت در انتهای نامه چاپ می‌شود)
    /// • withCopy=false → «بدون رونوشت» (نسخه‌ای که تحویل سازمان مقصد می‌شود)
    /// </summary>
    [HttpGet("{id:int}/print")]
    public async Task<IActionResult> Print(int id, [FromQuery] string size = "A4", [FromQuery] bool withCopy = true)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (!await InFlowAsync(id) && !await IsAdminAsync() && !await HasDabirkhaneAsync())
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید." });

        if (!string.Equals(size, "A4", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "سایز چاپ فقط A4 یا A5 است." });

        var pdf = await _print.GeneratePdfAsync(id, size, withCopy);
        if (pdf == null) return NotFound(new { message = "نامه پیدا نشد." });

        var letter = await Db.OutgoingLetters.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        var number = letter?.SadereNumber ?? letter?.LetterNumber ?? id.ToString();
        var version = withCopy ? "با-رونوشت" : "بدون-رونوشت";
        var fileName = $"letter-{number.Replace('/', '-')}-{size.ToUpper()}-{version}.pdf";
        return File(pdf, "application/pdf", fileName);
    }

    // ==================== گردش / ارجاع ====================

    /// <summary>
    /// گردش نامه صادره (درخت ارجاع‌ها) — علاوه بر افراد در گردش،
    /// کاربران دبیرخانه هم می‌توانند گردش نامه را ببینند.
    /// </summary>
    [HttpGet("{id:int}/gardesh")]
    public async Task<IActionResult> Gardesh(int id)
    {
        var isAdmin = await IsAdminAsync();
        var hasDabirkhane = await HasDabirkhaneAsync();

        if (!hasDabirkhane && !isAdmin && await ForbiddenUnlessAsync(Module, "Read") is { } forbid)
            return forbid;

        return Ok(await _erja.GetGardeshTreeAsync(id, MyUserId, isAdmin || hasDabirkhane));
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
        try
        {
            await _erja.AnswerAsync(erjaId, dto, MyUserId, await MyDisplayNameAsync());
            return Ok(new { message = "پاسخ ثبت شد." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message, detail = "پاسخ ثبت نشد؛ فقط گیرنده ارجاع می‌تواند پاسخ دهد." });
        }
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

    /// <summary>نشان‌کردن/برداشتن نشان (ستاره) — نامه صادره ارسالی توسط فرستنده (مشابه نامه داخلی)</summary>
    [HttpPost("{id:int}/neshan")]
    public async Task<IActionResult> ToggleLetterNeshan(int id)
    {
        var isNeshan = await _letters.ToggleLetterNeshanAsync(id, MyUserId);
        return Ok(new { isNeshan });
    }

    [HttpPost("erja/{erjaId:int}/bayegani")]
    public async Task<IActionResult> ToggleBayegani(int erjaId)
    {
        var isBayegani = await _erja.ToggleBayeganiAsync(erjaId, MyUserId);
        return Ok(new { isBayegani });
    }

    [HttpPost("erja/batch-bayegani")]
    public async Task<IActionResult> BatchBayegani([FromBody] BatchBayeganiRequest dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (dto == null || dto.ErjaIds == null || dto.ErjaIds.Count == 0)
            return BadRequest(new { message = "هیچ نامه‌ای برای بایگانی انتخاب نشده است." });

        var count = await _erja.BatchBayeganiAsync(dto.ErjaIds, MyUserId, dto.Description);
        return Ok(new { count, message = $"{count} نامه با موفقیت بایگانی شدند." });
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
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile file)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        if (!await InFlowAsync(id) && !await IsAdminAsync())
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید." });

        if (file == null || file.Length <= 0)
            return BadRequest(new { message = "فایل خالی است و قابل بارگذاری نیست." });
        if (file.Length > long.MaxValue)
            return BadRequest(new { message = "حداکثر حجم هر فایل ۲۰ مگابایت است." });

        // پیوست‌های صادره روی دیسک در «wwwroot/فایل های صادره/{شناسه نامه}/» ذخیره می‌شوند
        // (مشابه نامه داخلی؛ دانلود فقط از مسیر مجاز API)
        await using var stream = file.OpenReadStream();
        var relPath = await _store.SaveWebRootAsync(SadereFolder, id, stream, file.FileName);
        var att = new AppAttachment
        {
            Module = AttachmentModule,
            RefId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            Data = Array.Empty<byte>(),
            FilePath = relPath,
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
        else
        {
            var hasReadPerm = await ForbiddenUnlessAsync(Module, "Read") == null;
            var canAccess = hasReadPerm || await InFlowAsync(a.RefId) || await IsAdminAsync() || await HasDabirkhaneAsync();
            if (!canAccess)
                return StatusCode(403, new { message = "شما در گردش این نامه نیستید." });
        }

        // فایل از دیسک خوانده می‌شود (پشتیبانی کامل از هر دو مسیر جدید wwwroot/uploads و قدیمی)
        var bytes = (a.FilePath is not null ? _store.ReadBytes(a.FilePath) : null) ?? (a.Data is { Length: > 0 } ? a.Data : null);
        bytes ??= a.FilePath is not null ? _store.ReadWebRoot(a.FilePath) : null;
        bytes ??= a.Data is { Length: > 0 } ? a.Data : null;
        if (bytes == null) return NotFound(new { message = "فایل پیوست پیدا نشد." });
        return File(bytes, a.ContentType, a.FileName);
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
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadPishnevisAttachment(int id, IFormFile file)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var owns = await Db.OutgoingPishnevisLetters.AnyAsync(p => p.PishnevisId == id && p.UserId == MyUserId && !p.IsDelete);
        if (!owns) return StatusCode(403, new { message = "پیش‌نویس متعلق به شما نیست." });

        if (file == null || file.Length <= 0)
            return BadRequest(new { message = "فایل خالی است و قابل بارگذاری نیست." });
        if (file.Length > long.MaxValue)
            return BadRequest(new { message = "حداکثر حجم هر فایل ۲۰ مگابایت است." });

        // پیوست پیش‌نویس صادره هم در «فایل های صادره» ذخیره می‌شود و بعد از ارسال،
        // همراه رکورد پیوست به خود نامه منتقل می‌شود (مسیر فایل تغییر نمی‌کند).
        await using var stream = file.OpenReadStream();
        var relPath = await _store.SaveWebRootAsync(SadereFolder, id, stream, file.FileName);
        Db.AppAttachments.Add(new AppAttachment
        {
            Module = PishnevisAttachmentModule,
            RefId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            Data = Array.Empty<byte>(),
            FilePath = relPath,
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
        if (a.FilePath is not null) _store.DeleteWebRoot(a.FilePath);
        Db.AppAttachments.Remove(a);
        await Db.SaveChangesAsync();
        return Ok(new { message = "پیوست حذف شد." });
    }
}
