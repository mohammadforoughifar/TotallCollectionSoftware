using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Office;
using Inventory.Api.Services.Office.Email;
using Inventory.Api.Services.Office.Outgoing;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.Office;

// ============================================================
//  صورتجلسه — ماژول «فرم‌های متفرقه»
//  مسیر: /api/meeting-minutes
//  دسترسی‌ها (RBAC — فرم‌های متفرقه):
//   View / Create / Update / Delete / Sign / ToInnerLetter / ToOutgoingLetter / SendEmail / Print
//  عملیات‌های تبدیل به نامه/ایمیل علاوه بر مجوز همین ماژول،
//  مجوز خودِ آن ماژول (نامه داخلی/نامه صادره/ایمیل) را هم چک می‌کنند.
// ============================================================

[ApiController]
[Route("api/meeting-minutes")]
[Authorize]
public class MeetingMinutesController : RbacControllerBase
{
    private const string Module = "MeetingMinutes";

    private readonly IMeetingMinutesService _svc;
    private readonly IInnerLetterService _innerLetters;
    private readonly IOutgoingLetterService _outgoingLetters;
    private readonly IEmailService _email;
    private readonly IMeetingMinutesPrintService _print;

    public MeetingMinutesController(
        AppDbContext db,
        IMeetingMinutesService svc,
        IInnerLetterService innerLetters,
        IOutgoingLetterService outgoingLetters,
        IEmailService email,
        IMeetingMinutesPrintService print) : base(db)
    {
        _svc = svc;
        _innerLetters = innerLetters;
        _outgoingLetters = outgoingLetters;
        _email = email;
        _print = print;
    }

    // ================== فهرست ==================

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? status)
    {
        if (await ForbiddenUnlessAsync(Module, "View") is { } f) return f;
        return Ok(await _svc.GetListAsync(search, status));
    }

    // ================== جزئیات ==================

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "View") is { } f) return f;
        var d = await _svc.GetDetailAsync(id);
        return d is null ? NotFound(new { message = "صورتجلسه پیدا نشد." }) : Ok(d);
    }

    // ================== ساخت/ویرایش ==================

    public class SaveDto : SaveMinutesDto { }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveDto dto)
    {
        var need = dto.Id > 0 ? "Update" : "Create";
        if (await ForbiddenUnlessAsync(Module, need) is { } f) return f;
        try
        {
            var id = await _svc.SaveAsync(dto, MyUserId, MyUsername);
            return Ok(new { id });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ================== ارسال به گردش (نوتیف حاضرین/غایبین) ==================

    [HttpPost("{id:int}/submit")]
    public async Task<IActionResult> Submit(int id, [FromBody] SubmitMinutesDto? dto)
    {
        if (await ForbiddenUnlessAsync(Module, "View") is { } f) return f;
        var m = await Db.MeetingMinutes.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (m is null) return NotFound(new { message = "صورتجلسه پیدا نشد." });
        // فقط ثبت‌کننده یا کسی با مجوز Create
        if (m.CreatedByUserId != MyUserId && !await HasAsync(Module, "Create"))
            return StatusCode(403, new { message = "شما نمی‌توانید این صورتجلسه را ارسال کنید." });

        var includeAbsentees = dto?.IncludeAbsentees ?? true;
        await _svc.SubmitAsync(id, includeAbsentees, MyUserId, MyUsername);
        return Ok(new { ok = true });
    }

    // ================== حذف ==================

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } f) return f;
        await _svc.DeleteAsync(id, MyUserId);
        return Ok(new { ok = true });
    }

    // ================== بندها ==================

    [HttpPost("{id:int}/items")]
    public async Task<IActionResult> AddItem(int id, [FromBody] SaveMinutesItemDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } f) return f;
        try
        {
            var item = await _svc.AddItemAsync(id, dto, MyUserId);
            return Ok(item);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("items/{itemId:int}")]
    public async Task<IActionResult> UpdateItem(int itemId, [FromBody] SaveMinutesItemDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } f) return f;
        try
        {
            var item = await _svc.UpdateItemAsync(itemId, dto, MyUserId);
            return Ok(item);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("items/{itemId:int}")]
    public async Task<IActionResult> DeleteItem(int itemId)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } f) return f;
        try
        {
            await _svc.DeleteItemAsync(itemId, MyUserId);
            return Ok(new { ok = true });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ================== تصمیمات بندها ==================

    /// <summary>تایید/بازگشت بند توسط مسئول اجرا</summary>
    [HttpPost("items/{itemId:int}/resp-decision")]
    public async Task<IActionResult> RespDecision(int itemId, [FromBody] MinutesRespDecisionDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "View") is { } f) return f;
        try
        {
            var item = await _svc.RespDecisionAsync(itemId, dto.Approved, dto.Note, MyUserId);
            return Ok(item);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>تصمیم مسئول پیگیری — تایید یا رد + شرح</summary>
    [HttpPost("items/{itemId:int}/followup-decision")]
    public async Task<IActionResult> FollowUpDecision(int itemId, [FromBody] MinutesFollowUpDecisionDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "View") is { } f) return f;
        try
        {
            var item = await _svc.FollowUpDecisionAsync(itemId, dto.Decision, dto.Note, MyUserId);
            return Ok(item);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ================== امضای الکترونیکی ==================

    [HttpPost("{id:int}/sign")]
    public async Task<IActionResult> Sign(int id, [FromBody] MinutesSignDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Sign") is { } f) return f;
        try
        {
            await _svc.SignAsync(id, MyUserId, dto.SignatureBase64);
            return Ok(new { ok = true });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ================== تبدیل به نامه داخلی ==================

    [HttpPost("{id:int}/to-inner-letter")]
    public async Task<IActionResult> ToInnerLetter(int id, [FromBody] MinutesToInnerLetterDto dto)
    {
        // مجوز ماژول صورتجلسه + مجوز ماژول نامه داخلی
        if (await ForbiddenUnlessAsync(Module, "ToInnerLetter") is { } f) return f;
        if (!await HasAsync("InnerLetters", "Create"))
            return StatusCode(403, new { message = "شما مجوز ثبت نامه داخلی ندارید (نقش‌ها و دسترسی‌ها ← نامه داخلی ← ثبت)." });

        var letterTitle = string.IsNullOrWhiteSpace(dto.Title) ? await DefaultTitleAsync(id, dto.ItemId) : dto.Title.Trim();
        var text = string.IsNullOrWhiteSpace(dto.Text)
            ? await _svc.BuildSummaryTextAsync(id, dto.ItemId)
            : dto.Text!;

        var add = new AddInnerLetterDto
        {
            Title = letterTitle,
            Text = text,
            ReciversGirande = dto.ReciversGirande ?? new(),
            ReciversErja = dto.ReciversErja ?? new(),
            ReciversHamesh = dto.ReciversHamesh ?? new()
        };
        try
        {
            var letterId = await _innerLetters.AddInnerLetterAsync(add, MyUserId, MyUsername);
            return Ok(new { letterId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ================== تبدیل به نامه صادره ==================

    [HttpPost("{id:int}/to-outgoing-letter")]
    public async Task<IActionResult> ToOutgoingLetter(int id, [FromBody] MinutesToOutgoingLetterDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "ToOutgoingLetter") is { } f) return f;
        if (!await HasAsync("OutgoingLetters", "Create"))
            return StatusCode(403, new { message = "شما مجوز ثبت نامه صادره ندارید (نقش‌ها و دسترسی‌ها ← نامه صادره ← ثبت)." });

        if (string.IsNullOrWhiteSpace(dto.ReceiverOrganization))
            return BadRequest(new { message = "نام سازمان مقصد الزامی است." });

        var letterTitle = string.IsNullOrWhiteSpace(dto.Title) ? await DefaultTitleAsync(id, 0) : dto.Title.Trim();
        var text = string.IsNullOrWhiteSpace(dto.Text)
            ? await _svc.BuildSummaryTextAsync(id, 0)
            : dto.Text!;

        var add = new AddOutgoingLetterDto
        {
            Title = letterTitle,
            Text = text,
            ReceiverOrganization = dto.ReceiverOrganization.Trim(),
            ReceiverName = dto.ReceiverName,
            ReceiverTitle = dto.ReceiverTitle,
            CopyTo = dto.CopyTo
        };
        try
        {
            var letterId = await _outgoingLetters.AddOutgoingLetterAsync(add, MyUserId, MyUsername);
            return Ok(new { letterId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ================== ارسال با ایمیل سازمانی ==================

    [HttpPost("{id:int}/send-email")]
    public async Task<IActionResult> SendEmail(int id, [FromBody] MinutesSendEmailDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "SendEmail") is { } f) return f;
        if (!await HasAsync("Email", "Create"))
            return StatusCode(403, new { message = "شما مجوز ارسال ایمیل سازمانی ندارید (نقش‌ها و دسترسی‌ها ← ایمیل سازمانی ← ثبت)." });

        if (string.IsNullOrWhiteSpace(dto.To))
            return BadRequest(new { message = "مقصد ایمیل الزامی است." });
        if (string.IsNullOrWhiteSpace(dto.Subject))
            dto.Subject = await DefaultTitleAsync(id, 0);

        var body = string.IsNullOrWhiteSpace(dto.Body)
            ? await _svc.BuildSummaryTextAsync(id, 0)
            : dto.Body!;

        // متن ساده → HTML ساده (پاراگراف‌ها)
        var html = System.Net.WebUtility.HtmlEncode(body)
            .Replace("\r\n", "<br>").Replace("\n", "<br>");

        try
        {
            var emailId = await _email.SendAsync(new EmailComposeDto
            {
                EmailAccountId = dto.EmailAccountId,
                To = dto.To,
                Cc = dto.Cc,
                Subject = dto.Subject,
                Body = html
            }, MyUserId, false, Array.Empty<(string, string, byte[])>());
            return Ok(new { emailId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ================== قالب چاپی — سربرگ اختیاری است ==================

    [HttpGet("{id:int}/print")]
    [HttpGet("{id:int}/print.pdf")]
    public async Task<IActionResult> Print(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Print") is { } f) return f;
        try
        {
            var pdf = await _print.GeneratePdfAsync(id);
            if (pdf is null) return NotFound(new { message = "صورتجلسه پیدا نشد." });
            var fileName = $"صورتجلسه-{id}.pdf";
            return File(pdf, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            var msg = ex.Message ?? "خطای ناشناخته";
            if (msg.Length > 280) msg = msg[..280] + "…";
            return BadRequest(new { message = "ساخت PDF صورتجلسه ناموفق بود: " + msg });
        }
    }

    // ================== ابزار ==================

    private async Task<string> DefaultTitleAsync(int minutesId, int itemId)
    {
        var m = await Db.MeetingMinutes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == minutesId && !x.IsDeleted);
        if (m is null) return "صورتجلسه";
        return itemId > 0 ? $"بند صورتجلسه «{m.Title}»" : $"صورتجلسه «{m.Title}»";
    }
}
