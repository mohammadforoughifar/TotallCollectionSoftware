using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Office.Email;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.Office;

/// <summary>
/// ایمیل سازمانی (پست الکترونیک) — ماژول دسترسی: Email
/// حساب‌های شخصی هر کاربر + حساب‌های رسمی دبیرخانه (جدا از شخصی)
/// • حساب‌ها: CRUD + تست اتصال (SMTP/IMAP)
/// • ارسال با SMTP | دریافت با IMAP | بایگانی پوشه‌ای | نشان‌کردن
/// </summary>
[Route("api/email")]
public class EmailController : RbacControllerBase
{
    private const string Module = "Email";
    private readonly IEmailService _email;

    public EmailController(AppDbContext db, IEmailService email) : base(db)
    {
        _email = email;
    }

    private async Task<bool> IsAdminAsync() => await HasAsync(Module, "Delete");

    /// <summary>دسترسی دبیرخانه صادره — برای مدیریت حساب‌های رسمی دبیرخانه</summary>
    private async Task<bool> IsDabirkhaneAsync() =>
        await HasAsync("OutgoingLetters", "Dabirkhane") || await HasAsync(Module, "Delete");

    // ==================== حساب‌های ایمیل ====================

    /// <summary>حساب‌های من (شخصی) + حساب‌های دبیرخانه در صورت داشتن دسترسی</summary>
    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts()
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _email.GetMyAccountsAsync(MyUserId, await IsDabirkhaneAsync()));
    }

    /// <summary>حساب‌های فعال دبیرخانه — برای انتخاب در ثبت دبیرخانه نامه صادره</summary>
    [HttpGet("dabirkhane-accounts")]
    public async Task<IActionResult> DabirkhaneAccounts()
    {
        if (!await IsDabirkhaneAsync())
            return StatusCode(403, new { message = "شما به حساب‌های دبیرخانه دسترسی ندارید." });
        return Ok(await _email.GetDabirkhaneAccountsAsync());
    }

    [HttpPost("accounts")]
    public async Task<IActionResult> SaveAccount([FromBody] SaveEmailAccountDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var id = await _email.SaveAccountAsync(dto, MyUserId);
        return Ok(new { id, message = "حساب ایمیل ذخیره شد." });
    }

    [HttpDelete("accounts/{emailId:int}")]
    public async Task<IActionResult> DeleteAccount(int emailId)
    {
        await _email.DeleteAccountAsync(emailId, MyUserId);
        return Ok(new { message = "حساب ایمیل حذف شد." });
    }

    /// <summary>تست اتصال SMTP و IMAP — بدون ذخیره حساب</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] SaveEmailAccountDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        return Ok(await _email.TestAccountAsync(dto));
    }

    // ==================== ارسال و دریافت ====================

    /// <summary>ارسال ایمیل از حساب کاربر (یا حساب دبیرخانه) — multipart با پیوست</summary>
    [HttpPost("send")]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Send([FromForm] int emailAccountId, [FromForm] string to, [FromForm] string? cc,
        [FromForm] string subject, [FromForm] string? body, List<IFormFile> files)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;

        var attachments = new List<(string, string, byte[])>();
        foreach (var f in files ?? new List<IFormFile>())
        {
            if (f == null || f.Length <= 0) continue;
            if (f.Length > 20 * 1024 * 1024)
                return BadRequest(new { message = $"حجم فایل {f.FileName} بیش از ۲۰ مگابایت است." });
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms);
            attachments.Add((f.FileName, f.ContentType ?? "application/octet-stream", ms.ToArray()));
        }

        await _email.SendAsync(new EmailComposeDto
        {
            EmailAccountId = emailAccountId,
            To = to,
            Cc = cc,
            Subject = subject,
            Body = body ?? ""
        }, MyUserId, await IsDabirkhaneAsync(), attachments.ToArray());
        return Ok(new { message = "ایمیل ارسال شد." });
    }

    /// <summary>همگام‌سازی صندوق دریافت (IMAP) — آخرین ۵۰ پیام سرور بررسی می‌شود</summary>
    [HttpPost("accounts/{emailId:int}/sync")]
    public async Task<IActionResult> Sync(int emailId)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var r = await _email.SyncAsync(emailId, MyUserId, await IsDabirkhaneAsync());
        return Ok(r);
    }

    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox([FromQuery] int? emailId, [FromQuery] string? search, [FromQuery] bool? unreadOnly, [FromQuery] int? folderId)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var list = await _email.GetInboxAsync(MyUserId, emailId, search, unreadOnly, await IsDabirkhaneAsync());
        // folderId = 0 → بایگانی‌نشده | -1 → بایگانی‌شده در هر پوشه‌ای | n → پوشه n | خالی → همه
        if (folderId is > 0) list = list.Where(x => x.IsInFolder == folderId.Value).ToList();
        else if (folderId == 0) list = list.Where(x => x.IsInFolder == 0).ToList();
        else if (folderId == -1) list = list.Where(x => x.IsInFolder > 0).ToList();
        else if (folderId == -1) list = list.Where(x => x.IsInFolder > 0).ToList();
        return Ok(list);
    }

    [HttpGet("sent")]
    public async Task<IActionResult> Sent([FromQuery] int? emailId, [FromQuery] string? search, [FromQuery] int? folderId)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var list = await _email.GetSentAsync(MyUserId, emailId, search, await IsDabirkhaneAsync());
        if (folderId is > 0) list = list.Where(x => x.IsInFolder == folderId.Value).ToList();
        else if (folderId == 0) list = list.Where(x => x.IsInFolder == 0).ToList();
        else if (folderId == -1) list = list.Where(x => x.IsInFolder > 0).ToList();
        return Ok(list);
    }

    /// <summary>جزئیات ایمیل — box: Inbox یا Sent (دریافتی خودکار خوانده‌شده می‌شود)</summary>
    [HttpGet("message/{box}/{id:int}")]
    public async Task<IActionResult> Message(string box, int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _email.GetMessageAsync(box, id, MyUserId, markRead: true, await IsDabirkhaneAsync()));
    }

    [HttpPost("message/{box}/{id:int}/read")]
    public async Task<IActionResult> MarkRead(string box, int id)
    {
        await _email.MarkReadAsync(box, id, MyUserId, await IsDabirkhaneAsync());
        return Ok();
    }

    /// <summary>نشان‌کردن (ستاره) ایمیل</summary>
    [HttpPost("message/{box}/{id:int}/neshan")]
    public async Task<IActionResult> ToggleNeshan(string box, int id)
    {
        var isNeshan = await _email.ToggleNeshanAsync(box, id, MyUserId, await IsDabirkhaneAsync());
        return Ok(new { isNeshan });
    }

    /// <summary>بایگانی ایمیل در پوشه — FolderId=0 یعنی خروج از بایگانی</summary>
    [HttpPost("archive")]
    public async Task<IActionResult> Archive([FromBody] EmailArchiveDto dto)
    {
        await _email.ArchiveAsync(dto, MyUserId, await IsDabirkhaneAsync());
        return Ok(new { message = dto.FolderId > 0 ? "ایمیل بایگانی شد." : "از بایگانی خارج شد." });
    }

    /// <summary>دانلود پیوست ایمیل — فقط صاحب حساب (یا دبیرخانه برای حساب رسمی)</summary>
    [HttpGet("attachments/{attachmentId:int}/download")]
    public async Task<IActionResult> DownloadAttachment(int attachmentId)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var bytes = await _email.ReadAttachmentAsync(attachmentId, MyUserId, await IsDabirkhaneAsync());
        if (bytes == null) return NotFound(new { message = "فایل پیوست پیدا نشد." });
        var att = await Db.OtoEmailAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.AttachmentId == attachmentId);
        return File(bytes, "application/octet-stream", att?.AttachmentRealName ?? "attachment");
    }

    // ==================== پوشه‌های بایگانی ====================

    [HttpGet("folders")]
    public async Task<IActionResult> Folders()
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _email.GetFoldersAsync(MyUserId));
    }

    [HttpPost("folders")]
    public async Task<IActionResult> SaveFolder([FromBody] SaveEmailFolderDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var id = await _email.SaveFolderAsync(dto, MyUserId);
        return Ok(new { id, message = "پوشه ذخیره شد." });
    }

    [HttpDelete("folders/{folderId:int}")]
    public async Task<IActionResult> DeleteFolder(int folderId)
    {
        await _email.DeleteFolderAsync(folderId, MyUserId);
        return Ok(new { message = "پوشه حذف شد." });
    }
}
