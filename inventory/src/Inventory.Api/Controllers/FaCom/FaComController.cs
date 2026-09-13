using Inventory.Api.Data;
using Inventory.Api.Services.FaCom;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.FaCom;

/// <summary>
/// ================== ارتباطات داخلی فروغ آریا (ماژول جدید و مستقل) ==================
/// اطلاعیه‌ها، تیکت HR، توزیع چندکاناله، یادآوری تولد.
/// مجوزها: FaCom.Read (مشاهده/تیکت خود) / FaCom.Manage (مدیریت کامل)
/// </summary>
[Route("api/fa-com")]
public class FaComController : RbacControllerBase
{
    private const string Mod = "FaCom";
    private readonly IFaComService _svc;

    public FaComController(AppDbContext db, IFaComService svc) : base(db) => _svc = svc;

    // ------------------- اطلاعیه‌ها -------------------

    [HttpGet("announcements/feed")]
    public async Task<IActionResult> Feed()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.GetFeedAsync(MyUserId));
    }

    [HttpGet("announcements")]
    public async Task<IActionResult> Announcements()
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.ListAnnouncementsAsync());
    }

    [HttpPost("announcements")]
    public async Task<IActionResult> CreateAnnouncement([FromBody] FaComAnnouncementSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveAnnouncementAsync(null, dto, MyUserId, MyUsername));
    }

    [HttpPut("announcements/{id:int}")]
    public async Task<IActionResult> UpdateAnnouncement(int id, [FromBody] FaComAnnouncementSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveAnnouncementAsync(id, dto, MyUserId, MyUsername));
    }

    [HttpDelete("announcements/{id:int}")]
    public async Task<IActionResult> DeleteAnnouncement(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteAnnouncementAsync(id);
        return Ok(new { ok = true });
    }

    [HttpPost("announcements/{id:int}/broadcast")]
    public async Task<IActionResult> Broadcast(int id, [FromQuery] bool email, [FromQuery] bool push, [FromQuery] bool sms)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(new { count = await _svc.BroadcastAsync(id, email, push, sms, MyUserId) });
    }

    [HttpPost("announcements/{id:int}/publish-now")]
    public async Task<IActionResult> PublishNow(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { return Ok(new { count = await _svc.PublishNowAsync(id, MyUserId) }); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("announcements/check-due")]
    public async Task<IActionResult> CheckDue()
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(new { count = await _svc.CheckDueAnnouncementsAsync() });
    }

    // ------------------- صندوق پیشنهادها -------------------

    [HttpGet("suggestions/my")]
    public async Task<IActionResult> MySuggestions()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.MySuggestionsAsync(MyUserId));
    }

    [HttpPost("suggestions")]
    public async Task<IActionResult> CreateSuggestion([FromBody] FaComSuggestionSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        try { return Ok(await _svc.CreateSuggestionAsync(MyUserId, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet("suggestions")]
    public async Task<IActionResult> Suggestions([FromQuery] int? status, [FromQuery] int? category)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.ListSuggestionsAsync(status, category));
    }

    [HttpPost("suggestions/{id:int}/respond")]
    public async Task<IActionResult> RespondSuggestion(int id, [FromBody] FaComSuggestionRespondDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { return Ok(await _svc.RespondSuggestionAsync(id, dto, MyUsername)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("suggestions/{id:int}")]
    public async Task<IActionResult> DeleteSuggestion(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { await _svc.DeleteSuggestionAsync(id); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    // ------------------- نظرسنجی‌ها -------------------

    [HttpGet("polls")]
    public async Task<IActionResult> Polls()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.ListPollsAsync(MyUserId));
    }

    [HttpGet("polls/manage")]
    public async Task<IActionResult> ManagePolls()
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.ManagePollsAsync());
    }

    [HttpGet("polls/{id:int}")]
    public async Task<IActionResult> Poll(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        try { return Ok(await _svc.GetPollAsync(id, MyUserId)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("polls")]
    public async Task<IActionResult> CreatePoll([FromBody] FaComPollSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { return Ok(await _svc.SavePollAsync(null, dto, MyUserId, MyUsername)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("polls/{id:int}")]
    public async Task<IActionResult> UpdatePoll(int id, [FromBody] FaComPollSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { return Ok(await _svc.SavePollAsync(id, dto, MyUserId, MyUsername)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("polls/{id:int}")]
    public async Task<IActionResult> DeletePoll(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { await _svc.DeletePollAsync(id); return Ok(new { ok = true }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("polls/{id:int}/vote")]
    public async Task<IActionResult> Vote(int id, [FromQuery] int optionId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        try { return Ok(await _svc.VoteAsync(id, optionId, MyUserId)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ------------------- تیکت‌های من -------------------

    [HttpGet("tickets/my")]
    public async Task<IActionResult> MyTickets()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.GetMyTicketsAsync(MyUserId));
    }

    [HttpGet("tickets/my/{id:int}")]
    public async Task<IActionResult> MyTicket(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.GetMyTicketAsync(id, MyUserId));
    }

    [HttpPost("tickets/my")]
    public async Task<IActionResult> CreateMyTicket([FromBody] FaComTicketSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.CreateTicketAsync(MyUserId, dto, false));
    }

    [HttpPost("tickets/my/reply")]
    public async Task<IActionResult> ReplyMy([FromBody] FaComReplySaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.ReplyMyAsync(MyUserId, MyUsername, dto));
    }

    // ------------------- کارتابل تیکت HR -------------------

    [HttpGet("tickets")]
    public async Task<IActionResult> Tickets([FromQuery] int? status, [FromQuery] int? category)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.ListTicketsAsync(status, category));
    }

    [HttpGet("tickets/{id:int}")]
    public async Task<IActionResult> Ticket(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.GetTicketAsync(id));
    }

    [HttpPost("tickets")]
    public async Task<IActionResult> CreateTicket([FromBody] FaComTicketSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.CreateTicketAsync(MyUserId, dto, true));
    }

    [HttpPost("tickets/reply")]
    public async Task<IActionResult> ReplyHr([FromBody] FaComReplySaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.ReplyHrAsync(dto, MyUserId, MyUsername));
    }

    [HttpPost("tickets/{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromQuery] int status)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SetStatusAsync(id, status, MyUsername));
    }

    [HttpDelete("tickets/{id:int}")]
    public async Task<IActionResult> DeleteTicket(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteTicketAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- تولدها -------------------

    [HttpPost("birthdays/check")]
    public async Task<IActionResult> CheckBirthdays()
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(new { count = await _svc.CheckBirthdaysAsync() });
    }
}
