using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Inventory.Api.Services.Chat;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IWebHostEnvironment _env;

    public ChatController(IChatService chatService, IWebHostEnvironment env)
    {
        _chatService = chatService;
        _env = env;
    }

    private int CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private string CurrentUserName =>
        User.Identity?.Name ?? "کاربر";

    /// <summary>فهرست گفتگوهای کاربر جاری</summary>
    [HttpGet("conversations")]
    public async Task<ActionResult<List<ChatConversationDto>>> GetConversations([FromQuery] string? search, [FromQuery] ChatTypeDto? type, [FromQuery] bool onlyUnread = false)
    {
        var list = await _chatService.GetConversationsAsync(CurrentUserId, search, type, onlyUnread);
        return Ok(list);
    }

    /// <summary>جزئیات یک گفتگو</summary>
    [HttpGet("conversations/{id}")]
    public async Task<ActionResult<ChatConversationDto>> GetConversation(int id)
    {
        var conv = await _chatService.GetConversationByIdAsync(CurrentUserId, id);
        return Ok(conv);
    }

    /// <summary>شروع یا دریافت گفتگوی خصوصی با یکی از کاربران نرم‌افزار</summary>
    [HttpPost("conversations/direct")]
    public async Task<ActionResult<ChatConversationDto>> CreateDirect([FromBody] CreateDirectChatRequest req)
    {
        var conv = await _chatService.GetOrCreateDirectConversationAsync(CurrentUserId, CurrentUserName, req.TargetUserId);
        return Ok(conv);
    }

    /// <summary>ایجاد گروه جدید با کاربران انتخابی</summary>
    [HttpPost("conversations/group")]
    public async Task<ActionResult<ChatConversationDto>> CreateGroup([FromBody] CreateGroupChatRequest req)
    {
        var conv = await _chatService.CreateGroupConversationAsync(CurrentUserId, CurrentUserName, req);
        return Ok(conv);
    }

    /// <summary>دریافت پیام‌های یک گفتگو با صفحه‌بندی</summary>
    [HttpGet("conversations/{id}/messages")]
    public async Task<ActionResult<List<ChatMessageDto>>> GetMessages(int id, [FromQuery] int? beforeId, [FromQuery] int pageSize = 50, [FromQuery] string? search = null)
    {
        var messages = await _chatService.GetMessagesAsync(CurrentUserId, id, beforeId, pageSize, search);
        return Ok(messages);
    }

    /// <summary>ارسال پیام جدید در گفتگو</summary>
    [HttpPost("conversations/{id}/messages")]
    public async Task<ActionResult<ChatMessageDto>> SendMessage(int id, [FromBody] SendChatMessageRequest req)
    {
        req.ConversationId = id;
        var msg = await _chatService.SendMessageAsync(CurrentUserId, CurrentUserName, null, req);
        return Ok(msg);
    }

    /// <summary>ویرایش پیام</summary>
    [HttpPut("messages/{id}")]
    public async Task<ActionResult<ChatMessageDto>> EditMessage(int id, [FromBody] EditChatMessageRequest req)
    {
        var msg = await _chatService.EditMessageAsync(CurrentUserId, id, req.NewText);
        return Ok(msg);
    }

    /// <summary>حذف پیام</summary>
    [HttpDelete("messages/{id}")]
    public async Task<IActionResult> DeleteMessage(int id)
    {
        await _chatService.DeleteMessageAsync(CurrentUserId, id);
        return Ok(new { message = "پیام با موفقیت حذف شد." });
    }

    /// <summary>ثبت یا تغییر واکنش ایموجی</summary>
    [HttpPost("messages/{id}/react")]
    public async Task<ActionResult<Dictionary<string, List<ChatReactionUserDto>>>> React(int id, [FromBody] ReactChatMessageRequest req)
    {
        var reactions = await _chatService.ToggleReactionAsync(CurrentUserId, CurrentUserName, id, req.Emoji);
        return Ok(reactions);
    }

    /// <summary>پین یا آن‌پین کردن پیام در گفتگو</summary>
    [HttpPost("messages/{id}/pin")]
    public async Task<ActionResult<ChatMessageDto>> TogglePinMessage(int id)
    {
        var msg = await _chatService.TogglePinMessageAsync(CurrentUserId, id);
        return Ok(msg);
    }

    /// <summary>علامت‌گذاری گفتگو به عنوان خوانده شده</summary>
    [HttpPost("conversations/{id}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        await _chatService.MarkConversationAsReadAsync(CurrentUserId, id);
        return Ok(new { success = true });
    }

    /// <summary>پین یا آن‌پین کردن گفتگو در لیست دیالوگ‌ها</summary>
    [HttpPost("conversations/{id}/pin")]
    public async Task<IActionResult> TogglePinConversation(int id)
    {
        await _chatService.TogglePinConversationAsync(CurrentUserId, id);
        return Ok(new { success = true });
    }

    /// <summary>بی‌صدا کردن یا فعال‌سازی صدای گفتگو</summary>
    [HttpPost("conversations/{id}/mute")]
    public async Task<IActionResult> ToggleMuteConversation(int id)
    {
        await _chatService.ToggleMuteConversationAsync(CurrentUserId, id);
        return Ok(new { success = true });
    }

    /// <summary>فهرست اعضای گروه</summary>
    [HttpGet("conversations/{id}/members")]
    public async Task<ActionResult<List<ChatMemberDto>>> GetMembers(int id)
    {
        var members = await _chatService.GetGroupMembersAsync(CurrentUserId, id);
        return Ok(members);
    }

    /// <summary>افزودن عضو جدید به گروه</summary>
    [HttpPost("conversations/{id}/members")]
    public async Task<IActionResult> AddMembers(int id, [FromBody] List<int> userIds)
    {
        await _chatService.AddMembersToGroupAsync(CurrentUserId, CurrentUserName, id, userIds);
        return Ok(new { success = true });
    }

    /// <summary>حذف عضو از گروه یا خروج</summary>
    [HttpDelete("conversations/{id}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(int id, int userId)
    {
        await _chatService.RemoveMemberFromGroupAsync(CurrentUserId, CurrentUserName, id, userId);
        return Ok(new { success = true });
    }

    /// <summary>فهرست تمامی کاربران نرم‌افزار جهت شروع چت</summary>
    [HttpGet("users")]
    public async Task<ActionResult<List<ChatUserDto>>> GetUsers([FromQuery] string? search)
    {
        var users = await _chatService.GetSoftwareUsersForChatAsync(CurrentUserId, search);
        return Ok(users);
    }

    /// <summary>خلاصه اعلان‌های چت برای هدر نرم‌افزار</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ChatSummaryDto>> GetSummary()
    {
        var summary = await _chatService.GetChatSummaryAsync(CurrentUserId);
        return Ok(summary);
    }

    /// <summary>آپلود فایل و رسانه برای ارسال در چت</summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadAttachment(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "فایلی ارسال نشده است." });

        if (file.Length > 50 * 1024 * 1024) // حداکثر ۵۰ مگابایت
            return BadRequest(new { message = "حجم فایل نباید بیش از ۵۰ مگابایت باشد." });

        var uploadsFolder = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads", "chat");
        if (!Directory.Exists(uploadsFolder))
            Directory.CreateDirectory(uploadsFolder);

        var ext = Path.GetExtension(file.FileName);
        var uniqueName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(uploadsFolder, uniqueName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var fileUrl = $"/uploads/chat/{uniqueName}";
        var messageType = ChatMessageTypeDto.File;

        var lowerExt = ext.ToLowerInvariant();
        if (lowerExt is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif")
            messageType = ChatMessageTypeDto.Image;
        else if (lowerExt is ".mp3" or ".wav" or ".ogg" or ".m4a" or ".aac")
            messageType = ChatMessageTypeDto.Audio;
        else if (lowerExt is ".mp4" or ".webm" or ".mov" or ".avi")
            messageType = ChatMessageTypeDto.Video;

        return Ok(new
        {
            fileUrl,
            fileName = file.FileName,
            fileSizeBytes = file.Length,
            fileContentType = file.ContentType,
            messageType
        });
    }
}
