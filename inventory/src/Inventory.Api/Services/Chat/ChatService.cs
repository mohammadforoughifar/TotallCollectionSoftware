using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Inventory.Api.Data;
using Inventory.Api.Entities.Chat;
using Inventory.Api.Hubs;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.Services.Chat;

public interface IChatService
{
    Task<List<ChatConversationDto>> GetConversationsAsync(int currentUserId, string? search = null, ChatTypeDto? typeFilter = null, bool onlyUnread = false);
    Task<ChatConversationDto> GetConversationByIdAsync(int currentUserId, int conversationId);
    Task<ChatConversationDto> GetOrCreateDirectConversationAsync(int currentUserId, string currentUserName, int targetUserId);
    Task<ChatConversationDto> CreateGroupConversationAsync(int currentUserId, string currentUserName, CreateGroupChatRequest request);
    Task<List<ChatMessageDto>> GetMessagesAsync(int currentUserId, int conversationId, int? beforeMessageId = null, int pageSize = 50, string? search = null);
    Task<ChatMessageDto> SendMessageAsync(int currentUserId, string currentUserName, string? currentUserAvatar, SendChatMessageRequest request);
    Task<ChatMessageDto> EditMessageAsync(int currentUserId, int messageId, string newText);
    Task DeleteMessageAsync(int currentUserId, int messageId);
    Task<Dictionary<string, List<ChatReactionUserDto>>> ToggleReactionAsync(int currentUserId, string currentUserName, int messageId, string emoji);
    Task MarkConversationAsReadAsync(int currentUserId, int conversationId, int? lastReadMessageId = null);
    Task<ChatMessageDto> TogglePinMessageAsync(int currentUserId, int messageId);
    Task<List<ChatUserDto>> GetSoftwareUsersForChatAsync(int currentUserId, string? search = null);
    Task<ChatSummaryDto> GetChatSummaryAsync(int currentUserId);
    Task<List<ChatMemberDto>> GetGroupMembersAsync(int currentUserId, int conversationId);
    Task AddMembersToGroupAsync(int currentUserId, string currentUserName, int conversationId, List<int> newUserIds);
    Task RemoveMemberFromGroupAsync(int currentUserId, string currentUserName, int conversationId, int targetUserId);
    Task TogglePinConversationAsync(int currentUserId, int conversationId);
    Task ToggleMuteConversationAsync(int currentUserId, int conversationId);
}

public class ChatService : IChatService
{
    private readonly AppDbContext _db;
    private readonly IChatRealtimeNotifier _notifier;
    private readonly ILogger<ChatService> _logger;
    private readonly ChatAttachmentService _files;

    public ChatService(AppDbContext db, IChatRealtimeNotifier notifier, ILogger<ChatService> logger, ChatAttachmentService files)
    {
        _db = db;
        _notifier = notifier;
        _logger = logger;
        _files = files;
    }

    public async Task<List<ChatConversationDto>> GetConversationsAsync(int currentUserId, string? search = null, ChatTypeDto? typeFilter = null, bool onlyUnread = false)
    {
        var myMemberships = await _db.ChatMembers
            .AsNoTracking()
            .Where(m => m.UserId == currentUserId && !m.IsArchived)
            .ToListAsync();

        if (myMemberships.Count == 0) return new List<ChatConversationDto>();

        var conversationIds = myMemberships.Select(m => m.ConversationId).ToList();

        var q = _db.ChatConversations
            .AsNoTracking()
            .Where(c => conversationIds.Contains(c.Id));

        if (typeFilter.HasValue)
        {
            q = q.Where(c => c.Type == typeFilter.Value);
        }

        var list = await q
            .OrderByDescending(c => c.LastMessageAt)
            .ToListAsync();

        // واکشی اعضای تمامی این مکالمات جهت استخراج اطلاعات چت‌های دونفره
        var allMembers = await _db.ChatMembers
            .AsNoTracking()
            .Where(m => conversationIds.Contains(m.ConversationId))
            .ToListAsync();

        var unreadCounts = await UnreadMessagesFor(currentUserId)
            .Where(m => conversationIds.Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .Select(g => new { ConversationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ConversationId, g => g.Count);

        var membershipMap = myMemberships.ToDictionary(m => m.ConversationId);
        var membersGrouped = allMembers.GroupBy(m => m.ConversationId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<ChatConversationDto>();
        var normalizedSearch = ChatSearchText.Normalize(search);

        foreach (var conv in list)
        {
            membershipMap.TryGetValue(conv.Id, out var myMem);
            if (myMem == null) continue;

            if (onlyUnread && unreadCounts.GetValueOrDefault(conv.Id) == 0) continue;

            membersGrouped.TryGetValue(conv.Id, out var members);
            members ??= new List<ChatMember>();

            var dto = new ChatConversationDto
            {
                Id = conv.Id,
                Title = conv.Title,
                Type = conv.Type,
                Description = conv.Description,
                AvatarUrl = conv.AvatarUrl,
                CreatedByUserId = conv.CreatedByUserId,
                CreatedAt = conv.CreatedAt,
                LastMessageAt = conv.LastMessageAt,
                LastMessageSnippet = conv.LastMessageSnippet,
                LastMessageSenderId = conv.LastMessageSenderId,
                LastMessageSenderName = conv.LastMessageSenderName,
                PinnedMessageId = conv.PinnedMessageId,
                UnreadCount = unreadCounts.GetValueOrDefault(conv.Id),
                IsPinned = myMem.IsPinned,
                IsMuted = myMem.IsMuted,
                IsArchived = myMem.IsArchived,
                LastReadMessageId = myMem.LastReadMessageId,
                MyRole = myMem.Role,
                MembersCount = members.Count
            };

            // در چت‌های خصوصی، عنوان و آواتار از کاربر مقابل خوانده می‌شود
            if (conv.Type == ChatTypeDto.Direct)
            {
                var peer = members.FirstOrDefault(m => m.UserId != currentUserId) ?? myMem;
                dto.DirectPeerUserId = peer.UserId;
                dto.DirectPeerName = string.IsNullOrWhiteSpace(peer.UserDisplayName) ? peer.UserName : peer.UserDisplayName;
                dto.DirectPeerAvatarUrl = peer.UserAvatarUrl;
                dto.Title = dto.DirectPeerName;
                dto.AvatarUrl = peer.UserAvatarUrl;
                dto.DirectPeerIsOnline = ChatHub.IsUserOnline(peer.UserId);
                dto.DirectPeerLastSeen = ChatHub.GetLastSeen(peer.UserId);
            }

            if (normalizedSearch.Length > 0)
            {
                bool matches = ChatSearchText.Normalize(dto.Title).Contains(normalizedSearch) ||
                               ChatSearchText.Normalize(dto.LastMessageSnippet).Contains(normalizedSearch);
                if (!matches) continue;
            }

            result.Add(dto);
        }

        // مرتب‌سازی: اول پین‌شده‌ها، سپس جدیدترین پیام
        return result
            .OrderByDescending(c => c.IsPinned)
            .ThenByDescending(c => c.LastMessageAt)
            .ToList();
    }

    public async Task<ChatConversationDto> GetConversationByIdAsync(int currentUserId, int conversationId)
    {
        var conv = await _db.ChatConversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conv == null) throw new KeyNotFoundException("گفتگو یافت نشد.");

        var myMem = await _db.ChatMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == currentUserId);

        if (myMem == null) throw new UnauthorizedAccessException("شما عضو این گفتگو نیستید.");

        var members = await _db.ChatMembers
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .ToListAsync();

        var dto = new ChatConversationDto
        {
            Id = conv.Id,
            Title = conv.Title,
            Type = conv.Type,
            Description = conv.Description,
            AvatarUrl = conv.AvatarUrl,
            CreatedByUserId = conv.CreatedByUserId,
            CreatedAt = conv.CreatedAt,
            LastMessageAt = conv.LastMessageAt,
            LastMessageSnippet = conv.LastMessageSnippet,
            LastMessageSenderId = conv.LastMessageSenderId,
            LastMessageSenderName = conv.LastMessageSenderName,
            PinnedMessageId = conv.PinnedMessageId,
            UnreadCount = await UnreadMessagesFor(currentUserId).CountAsync(m => m.ConversationId == conversationId),
            IsPinned = myMem.IsPinned,
            IsMuted = myMem.IsMuted,
            IsArchived = myMem.IsArchived,
            LastReadMessageId = myMem.LastReadMessageId,
            MyRole = myMem.Role,
            MembersCount = members.Count
        };

        if (conv.Type == ChatTypeDto.Direct)
        {
            var peer = members.FirstOrDefault(m => m.UserId != currentUserId) ?? myMem;
            dto.DirectPeerUserId = peer.UserId;
            dto.DirectPeerName = string.IsNullOrWhiteSpace(peer.UserDisplayName) ? peer.UserName : peer.UserDisplayName;
            dto.DirectPeerAvatarUrl = peer.UserAvatarUrl;
            dto.Title = dto.DirectPeerName;
            dto.AvatarUrl = peer.UserAvatarUrl;
            dto.DirectPeerIsOnline = ChatHub.IsUserOnline(peer.UserId);
            dto.DirectPeerLastSeen = ChatHub.GetLastSeen(peer.UserId);
        }

        return dto;
    }

    public async Task<ChatConversationDto> GetOrCreateDirectConversationAsync(int currentUserId, string currentUserName, int targetUserId)
    {
        if (targetUserId <= 0) throw new ArgumentException("شناسه مخاطب نامعتبر است.");

        // مخاطب باید کاربر معتبر و فعال در نرم‌افزار باشد
        var targetUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == targetUserId && u.IsActive);
        if (targetUser == null) throw new KeyNotFoundException("کاربر مقصد در سیستم یافت نشد یا غیرفعال است.");

        var currentUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
        var currentDisplayName = $"{currentUser?.FirstName} {currentUser?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(currentDisplayName)) currentDisplayName = currentUserName;

        var targetDisplayName = $"{targetUser.FirstName} {targetUser.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(targetDisplayName)) targetDisplayName = targetUser.Username;

        // بررسی اینکه آیا قبلاً بین این دو کاربر گفتگوی خصوصی شکل گرفته است یا نه
        var existingConvId = await (from m1 in _db.ChatMembers.AsNoTracking()
                                    join m2 in _db.ChatMembers.AsNoTracking() on m1.ConversationId equals m2.ConversationId
                                    join c in _db.ChatConversations.AsNoTracking() on m1.ConversationId equals c.Id
                                    where c.Type == ChatTypeDto.Direct && m1.UserId == currentUserId && m2.UserId == targetUserId
                                    select c.Id).FirstOrDefaultAsync();

        if (existingConvId > 0)
        {
            return await GetConversationByIdAsync(currentUserId, existingConvId);
        }

        // ایجاد گفتگوی خصوصی جدید
        var now = DateTime.UtcNow;
        var conv = new ChatConversation
        {
            Title = targetDisplayName,
            Type = ChatTypeDto.Direct,
            CreatedByUserId = currentUserId,
            CreatedAt = now,
            LastMessageAt = now,
            LastMessageSnippet = "گفتگو آغاز شد"
        };
        _db.ChatConversations.Add(conv);
        await _db.SaveChangesAsync();

        var mMe = new ChatMember
        {
            ConversationId = conv.Id,
            UserId = currentUserId,
            UserName = currentUserName,
            UserDisplayName = currentDisplayName,
            UserAvatarUrl = currentUser?.PhotoPath != null ? $"/uploads/{currentUser.PhotoPath}" : null,
            Role = ChatMemberRoleDto.Owner,
            JoinedAt = now
        };

        var mTarget = new ChatMember
        {
            ConversationId = conv.Id,
            UserId = targetUserId,
            UserName = targetUser.Username,
            UserDisplayName = targetDisplayName,
            UserAvatarUrl = targetUser.PhotoPath != null ? $"/uploads/{targetUser.PhotoPath}" : null,
            Role = ChatMemberRoleDto.Member,
            JoinedAt = now
        };

        _db.ChatMembers.AddRange(mMe, mTarget);
        await _db.SaveChangesAsync();

        var dto = await GetConversationByIdAsync(currentUserId, conv.Id);
        await _notifier.NotifyConversationUpdatedAsync(new[] { currentUserId, targetUserId }, dto);

        return dto;
    }

    public async Task<ChatConversationDto> CreateGroupConversationAsync(int currentUserId, string currentUserName, CreateGroupChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("نام گروه اجباری است.");

        var currentUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
        var currentDisplayName = $"{currentUser?.FirstName} {currentUser?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(currentDisplayName)) currentDisplayName = currentUserName;

        var now = DateTime.UtcNow;
        var conv = new ChatConversation
        {
            Title = request.Title.Trim(),
            Type = request.Type,
            Description = request.Description?.Trim(),
            AvatarUrl = request.AvatarUrl,
            CreatedByUserId = currentUserId,
            CreatedAt = now,
            LastMessageAt = now,
            LastMessageSnippet = "گروه ایجاد شد"
        };
        _db.ChatConversations.Add(conv);
        await _db.SaveChangesAsync();

        var members = new List<ChatMember>
        {
            new ChatMember
            {
                ConversationId = conv.Id,
                UserId = currentUserId,
                UserName = currentUserName,
                UserDisplayName = currentDisplayName,
                UserAvatarUrl = currentUser?.PhotoPath != null ? $"/uploads/{currentUser.PhotoPath}" : null,
                Role = ChatMemberRoleDto.Owner,
                JoinedAt = now
            }
        };

        // افزودن اعضای انتخاب شده از میان کاربران نرم‌افزار
        var targetUserIds = request.MemberUserIds.Where(id => id != currentUserId).Distinct().ToList();
        if (targetUserIds.Count > 0)
        {
            var targetUsers = await _db.Users
                .AsNoTracking()
                .Where(u => targetUserIds.Contains(u.Id) && u.IsActive)
                .ToListAsync();

            foreach (var u in targetUsers)
            {
                var disp = $"{u.FirstName} {u.LastName}".Trim();
                if (string.IsNullOrWhiteSpace(disp)) disp = u.Username;

                members.Add(new ChatMember
                {
                    ConversationId = conv.Id,
                    UserId = u.Id,
                    UserName = u.Username,
                    UserDisplayName = disp,
                    UserAvatarUrl = u.PhotoPath != null ? $"/uploads/{u.PhotoPath}" : null,
                    Role = ChatMemberRoleDto.Member,
                    JoinedAt = now
                });
            }
        }

        _db.ChatMembers.AddRange(members);

        // پیام سیستمی ایجاد گروه
        var sysMsg = new ChatMessage
        {
            ConversationId = conv.Id,
            SenderUserId = currentUserId,
            SenderName = currentDisplayName,
            Text = $"«{currentDisplayName}» گروه را ایجاد کرد.",
            MessageType = ChatMessageTypeDto.System,
            CreatedAt = now
        };
        _db.ChatMessages.Add(sysMsg);
        await _db.SaveChangesAsync();

        var dto = await GetConversationByIdAsync(currentUserId, conv.Id);
        var allUserIds = members.Select(m => m.UserId).ToList();
        await _notifier.NotifyConversationUpdatedAsync(allUserIds, dto);

        return dto;
    }

    public async Task<List<ChatMessageDto>> GetMessagesAsync(int currentUserId, int conversationId, int? beforeMessageId = null, int pageSize = 50, string? search = null)
    {
        var myMem = await _db.ChatMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == currentUserId);

        if (myMem == null) throw new UnauthorizedAccessException("شما عضو این گفتگو نیستید.");

        // بررسی آخرین پیام خوانده‌شده توسط مخاطب در چت خصوصی (جهت تیک دوم خوانده‌شدن)
        var peerLastReadId = await _db.ChatMembers
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.UserId != currentUserId)
            .Select(m => (int?)m.LastReadMessageId)
            .MaxAsync() ?? 0;

        var q = _db.ChatMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && !m.IsDeleted);

        if (beforeMessageId.HasValue && beforeMessageId.Value > 0)
        {
            q = q.Where(m => m.Id < beforeMessageId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            q = q.Where(m => (m.Text != null && m.Text.ToLower().Contains(s)) ||
                             (m.FileName != null && m.FileName.ToLower().Contains(s)) ||
                             (m.ErpEntityTitle != null && m.ErpEntityTitle.ToLower().Contains(s)));
        }

        var messages = await q
            .OrderByDescending(m => m.CreatedAt)
            .Take(Math.Min(pageSize, 100))
            .ToListAsync();

        messages.Reverse(); // به ترتیب زمانی صعودی برای چت

        return messages.Select(m => MapToMessageDto(m, currentUserId, peerLastReadId)).ToList();
    }

    public async Task<ChatMessageDto> SendMessageAsync(int currentUserId, string currentUserName, string? currentUserAvatar, SendChatMessageRequest request)
    {
        if (request.ConversationId <= 0) throw new ArgumentException("شناسه گفتگو نامعتبر است.");
        if (!await _db.Users.AsNoTracking().AnyAsync(u => u.Id == currentUserId && u.IsActive))
            throw new UnauthorizedAccessException("حساب کاربری فعال نیست.");
        var myMem = await _db.ChatMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ConversationId == request.ConversationId && m.UserId == currentUserId);
        if (myMem == null) throw new UnauthorizedAccessException("شما عضو این گفتگو نیستید.");

        await _files.ApplyToMessageAsync(currentUserId, request);
        if (string.IsNullOrWhiteSpace(request.Text) && request.FileUrl == null && string.IsNullOrWhiteSpace(request.ErpModule))
            throw new ArgumentException("متن یا فایل پیام را وارد کنید.");

        var now = DateTime.UtcNow;
        var msg = new ChatMessage
        {
            ConversationId = request.ConversationId, SenderUserId = currentUserId,
            SenderName = string.IsNullOrWhiteSpace(myMem.UserDisplayName) ? currentUserName : myMem.UserDisplayName,
            SenderAvatarUrl = currentUserAvatar ?? myMem.UserAvatarUrl, Text = request.Text?.Trim(),
            MessageType = request.MessageType, FileUrl = request.FileUrl, FileName = request.FileName,
            FileSizeBytes = request.FileSizeBytes, FileContentType = request.FileContentType,
            ReplyToMessageId = request.ReplyToMessageId, ForwardFromMessageId = request.ForwardFromMessageId,
            ErpModule = request.ErpModule, ErpEntityId = request.ErpEntityId,
            ErpEntityTitle = request.ErpEntityTitle, ErpEntitySummary = request.ErpEntitySummary, CreatedAt = now
        };
        if (request.ReplyToMessageId is > 0)
        {
            var reply = await _db.ChatMessages.AsNoTracking().FirstOrDefaultAsync(m =>
                m.Id == request.ReplyToMessageId && m.ConversationId == request.ConversationId && !m.IsDeleted)
                ?? throw new ArgumentException("پیام مورد پاسخ در این گفتگو یافت نشد.");
            msg.ReplyToSenderName = reply.SenderName;
            msg.ReplyToSnippet = string.IsNullOrWhiteSpace(reply.Text) ? reply.FileName ?? "پیوست"
                : reply.Text.Length > 50 ? reply.Text[..50] + "..." : reply.Text;
        }

        var snippet = !string.IsNullOrWhiteSpace(msg.Text)
            ? (msg.Text.Length > 60 ? msg.Text[..60] + "..." : msg.Text)
            : msg.FileName ?? msg.ErpEntityTitle ?? "پیام رسانه‌ای";

        // پیام، اتصال فایل، نشانگر خواندن و متاداده همگی در یک تراکنش ثبت می‌شوند.
        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            _db.ChatMessages.Add(msg);
            await _db.SaveChangesAsync();
            await _db.ChatConversations.Where(c => c.Id == request.ConversationId && c.LastMessageAt <= now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.LastMessageAt, now).SetProperty(c => c.LastMessageSnippet, snippet)
                    .SetProperty(c => c.LastMessageSenderId, currentUserId).SetProperty(c => c.LastMessageSenderName, msg.SenderName));
            await _db.ChatMembers.Where(m => m.ConversationId == request.ConversationId && m.UserId != currentUserId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.UnreadCount, m => m.UnreadCount + 1));
            await _db.ChatMembers.Where(m => m.Id == myMem.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.LastReadMessageId, m => m.LastReadMessageId < msg.Id ? msg.Id : m.LastReadMessageId)
                    .SetProperty(m => m.UnreadCount, 0));
            await transaction.CommitAsync();
        }

        var recipients = await _db.ChatMembers.AsNoTracking().Where(m => m.ConversationId == request.ConversationId)
            .Select(m => m.UserId).ToListAsync();
        var dto = MapToMessageDto(msg, currentUserId, 0);
        try { await _notifier.NotifyMessageReceivedAsync(request.ConversationId, recipients, dto); }
        catch (Exception ex) { _logger.LogWarning(ex, "Message {MessageId} saved, but realtime delivery failed.", msg.Id); }
        return dto;
    }

    public async Task<ChatMessageDto> EditMessageAsync(int currentUserId, int messageId, string newText)
    {
        var msg = await _db.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (msg == null || msg.IsDeleted) throw new KeyNotFoundException("پیام یافت نشد.");

        if (msg.SenderUserId != currentUserId)
            throw new UnauthorizedAccessException("تنها فرستنده پیام می‌تواند آن را ویرایش کند.");

        if (string.IsNullOrWhiteSpace(newText))
            throw new ArgumentException("متن پیام نمی‌تواند خالی باشد.");

        msg.Text = newText.Trim();
        msg.IsEdited = true;
        msg.EditedAt = DateTime.UtcNow;

        // اگر آخرین پیام مکالمه بود، snippet را آپدیت کن
        var conv = await _db.ChatConversations.FirstOrDefaultAsync(c => c.Id == msg.ConversationId);
        if (conv != null && conv.LastMessageSenderId == currentUserId)
        {
            conv.LastMessageSnippet = msg.Text.Length > 60 ? msg.Text.Substring(0, 60) + "..." : msg.Text;
        }

        await _db.SaveChangesAsync();

        var memberUserIds = await _db.ChatMembers
            .Where(m => m.ConversationId == msg.ConversationId)
            .Select(m => m.UserId)
            .ToListAsync();

        var dto = MapToMessageDto(msg, currentUserId, 0);
        await _notifier.NotifyMessageUpdatedAsync(msg.ConversationId, memberUserIds, dto);

        return dto;
    }

    public async Task DeleteMessageAsync(int currentUserId, int messageId)
    {
        var msg = await _db.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (msg == null || msg.IsDeleted) return;

        var myMem = await _db.ChatMembers
            .FirstOrDefaultAsync(m => m.ConversationId == msg.ConversationId && m.UserId == currentUserId);

        if (myMem == null) throw new UnauthorizedAccessException("شما عضو این گفتگو نیستید.");

        // فقط فرستنده یا ادمین/سازنده گروه مجاز به حذف است
        bool canDelete = msg.SenderUserId == currentUserId ||
                         myMem.Role == ChatMemberRoleDto.Owner ||
                         myMem.Role == ChatMemberRoleDto.Admin;

        if (!canDelete) throw new UnauthorizedAccessException("مجوز حذف این پیام را ندارید.");

        msg.IsDeleted = true;
        msg.DeletedAt = DateTime.UtcNow;
        msg.Text = "این پیام حذف شد.";
        msg.FileUrl = null;

        await _db.SaveChangesAsync();

        var memberUserIds = await _db.ChatMembers
            .Where(m => m.ConversationId == msg.ConversationId)
            .Select(m => m.UserId)
            .ToListAsync();

        await _notifier.NotifyMessageDeletedAsync(msg.ConversationId, memberUserIds, messageId);
    }

    public async Task<Dictionary<string, List<ChatReactionUserDto>>> ToggleReactionAsync(int currentUserId, string currentUserName, int messageId, string emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) throw new ArgumentException("ایموجی نامعتبر است.");

        var msg = await _db.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (msg == null || msg.IsDeleted) throw new KeyNotFoundException("پیام یافت نشد.");

        if (!await _db.ChatMembers.AnyAsync(m => m.ConversationId == msg.ConversationId && m.UserId == currentUserId))
            throw new UnauthorizedAccessException("شما عضو این گفتگو نیستید.");

        var reactions = DeserializeReactions(msg.ReactionsJson);

        if (!reactions.TryGetValue(emoji, out var usersList))
        {
            usersList = new List<ChatReactionUserDto>();
            reactions[emoji] = usersList;
        }

        var existing = usersList.FirstOrDefault(u => u.UserId == currentUserId);
        if (existing != null)
        {
            usersList.Remove(existing);
            if (usersList.Count == 0) reactions.Remove(emoji);
        }
        else
        {
            usersList.Add(new ChatReactionUserDto { UserId = currentUserId, UserName = currentUserName });
        }

        msg.ReactionsJson = JsonSerializer.Serialize(reactions);
        await _db.SaveChangesAsync();

        var memberUserIds = await _db.ChatMembers
            .Where(m => m.ConversationId == msg.ConversationId)
            .Select(m => m.UserId)
            .ToListAsync();

        await _notifier.NotifyReactionAsync(msg.ConversationId, memberUserIds, messageId, reactions);

        return reactions;
    }

    public async Task MarkConversationAsReadAsync(int currentUserId, int conversationId, int? lastReadMessageId = null)
    {
        var member = await _db.ChatMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == currentUserId)
            ?? throw new UnauthorizedAccessException("شما عضو این گفتگو نیستید.");
        var lastId = await _db.ChatMessages.Where(m => m.ConversationId == conversationId)
            .Select(m => (int?)m.Id).MaxAsync() ?? 0;
        var readThrough = Math.Clamp(lastReadMessageId ?? lastId, 0, lastId);
        var changed = await _db.ChatMembers.Where(m => m.Id == member.Id && m.LastReadMessageId < readThrough)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.LastReadMessageId, readThrough));
        if (changed == 0) return; // نشانگر هیچ‌وقت عقب نمی‌رود؛ از حلقهٔ اعلانِ خواندن هم جلوگیری می‌شود.
        var actualRead = await _db.ChatMembers.Where(m => m.Id == member.Id).Select(m => m.LastReadMessageId).SingleAsync();
        var remaining = await UnreadMessagesFor(currentUserId).CountAsync(m => m.ConversationId == conversationId);
        await _db.ChatMembers.Where(m => m.Id == member.Id && m.LastReadMessageId == actualRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.UnreadCount, remaining));
        var peers = await _db.ChatMembers.AsNoTracking().Where(m => m.ConversationId == conversationId && m.UserId != currentUserId)
            .Select(m => m.UserId).ToListAsync();
        await _notifier.NotifyMessagesReadAsync(conversationId, currentUserId, actualRead, peers);
    }

    private IQueryable<ChatMessage> UnreadMessagesFor(int userId) =>
        from message in _db.ChatMessages.AsNoTracking()
        join member in _db.ChatMembers.AsNoTracking() on message.ConversationId equals member.ConversationId
        where member.UserId == userId && !member.IsArchived && !message.IsDeleted &&
              message.SenderUserId != userId && message.Id > member.LastReadMessageId
        select message;

    public async Task<ChatMessageDto> TogglePinMessageAsync(int currentUserId, int messageId)
    {
        var msg = await _db.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (msg == null || msg.IsDeleted) throw new KeyNotFoundException("پیام یافت نشد.");

        var conv = await _db.ChatConversations.FirstOrDefaultAsync(c => c.Id == msg.ConversationId);
        if (conv == null) throw new KeyNotFoundException("گفتگو یافت نشد.");

        var myMem = await _db.ChatMembers
            .FirstOrDefaultAsync(m => m.ConversationId == msg.ConversationId && m.UserId == currentUserId);

        if (myMem == null) throw new UnauthorizedAccessException("شما عضو این گفتگو نیستید.");

        msg.IsPinned = !msg.IsPinned;
        if (msg.IsPinned)
        {
            conv.PinnedMessageId = msg.Id;
        }
        else if (conv.PinnedMessageId == msg.Id)
        {
            conv.PinnedMessageId = null;
        }

        await _db.SaveChangesAsync();

        var memberUserIds = await _db.ChatMembers
            .Where(m => m.ConversationId == msg.ConversationId)
            .Select(m => m.UserId)
            .ToListAsync();

        var dto = MapToMessageDto(msg, currentUserId, 0);
        await _notifier.NotifyMessageUpdatedAsync(msg.ConversationId, memberUserIds, dto);

        return dto;
    }

    public async Task<List<ChatUserDto>> GetSoftwareUsersForChatAsync(int currentUserId, string? search = null)
    {
        // شناسهٔ چت همیشه از حساب ورود (Users) است، نه شناسهٔ مستقل SystemUsers.
        // کاربر فعال حتی بدون گفتگوی قبلی و در حالت آفلاین باید قابل پیدا شدن باشد.
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.Id != currentUserId)
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ThenBy(u => u.Username)
            .Select(u => new { u.Id, u.Username, u.FirstName, u.LastName, u.PhotoPath, u.Role })
            .ToListAsync();

        if (users.Count == 0) return new();

        // واحد سازمانی در پروفایل پرسنلی نگهداری می‌شود؛ اتصال فقط با نام کاربری،
        // نه با Id (دو جدول شناسه‌های متفاوت دارند). این اتصال اختیاری است.
        var departments = await (from profile in _db.SystemUsers.AsNoTracking()
                                 join department in _db.SystemDepartments.AsNoTracking()
                                     on profile.DepartmentId equals (int?)department.Id
                                 where profile.IsActive && department.IsActive && profile.Username != ""
                                 orderby profile.Id
                                 select new { profile.Username, department.Name }).ToListAsync();
        var departmentMap = departments
            .GroupBy(p => p.Username.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        // نرمال‌سازی بعد از واکشی ستون‌های عمومی انجام می‌شود تا رفتار جست‌وجوی
        // نام کامل و حروف فارسی/عربی در SQL Server و SQLite یکسان باشد.
        var normalizedSearch = ChatSearchText.Normalize(search);
        var result = users.Select(u =>
        {
            var displayName = $"{u.FirstName} {u.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(displayName)) displayName = u.Username;
            departmentMap.TryGetValue(u.Username.Trim(), out var department);

            return new ChatUserDto
            {
                Id = u.Id,
                Username = u.Username,
                DisplayName = displayName,
                AvatarUrl = u.PhotoPath != null ? $"/uploads/{u.PhotoPath}" : null,
                Department = department,
                Role = u.Role,
                IsOnline = ChatHub.IsUserOnline(u.Id),
                LastSeen = ChatHub.GetLastSeen(u.Id)
            };
        }).Where(u => normalizedSearch.Length == 0 ||
                      ChatSearchText.Normalize(u.DisplayName).Contains(normalizedSearch) ||
                      ChatSearchText.Normalize(u.Username).Contains(normalizedSearch) ||
                      ChatSearchText.Normalize(u.Department).Contains(normalizedSearch)).ToList();

        // نتیجهٔ خالی واقعاً خالی است؛ هرگز به کل فهرست کاربران fallback نمی‌کنیم.
        if (result.Count == 0) return result;

        var userIds = result.Select(u => u.Id).ToList();
        var existingDirects = await (from me in _db.ChatMembers.AsNoTracking()
                                     join peer in _db.ChatMembers.AsNoTracking()
                                         on me.ConversationId equals peer.ConversationId
                                     join conversation in _db.ChatConversations.AsNoTracking()
                                         on me.ConversationId equals conversation.Id
                                     where conversation.Type == ChatTypeDto.Direct && me.UserId == currentUserId
                                           && peer.UserId != currentUserId && userIds.Contains(peer.UserId)
                                     select new { peer.UserId, conversation.Id }).ToListAsync();
        var directMap = existingDirects.GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Min(x => x.Id));
        foreach (var user in result)
        {
            if (directMap.TryGetValue(user.Id, out var conversationId))
                user.ExistingConversationId = conversationId;
        }
        return result;
    }

    public async Task<ChatSummaryDto> GetChatSummaryAsync(int currentUserId)
    {
        // شمارش از پیام‌ها + نشانگر خواندن؛ از افزایش دوباره یا stale شدن کش شمارنده مستقل است.
        var counts = await UnreadMessagesFor(currentUserId).GroupBy(m => m.ConversationId)
            .Select(g => g.Count()).ToListAsync();
        return new ChatSummaryDto { TotalUnreadMessages = counts.Sum(), UnreadConversationsCount = counts.Count };
    }

    public async Task<List<ChatMemberDto>> GetGroupMembersAsync(int currentUserId, int conversationId)
    {
        var isMember = await _db.ChatMembers.AnyAsync(m => m.ConversationId == conversationId && m.UserId == currentUserId);
        if (!isMember) throw new UnauthorizedAccessException("شما عضو این گفتگو نیستید.");

        var members = await _db.ChatMembers
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.UserDisplayName)
            .ToListAsync();

        return members.Select(m => new ChatMemberDto
        {
            Id = m.Id,
            ConversationId = m.ConversationId,
            UserId = m.UserId,
            UserName = m.UserName,
            UserDisplayName = m.UserDisplayName,
            UserAvatarUrl = m.UserAvatarUrl,
            Role = m.Role,
            JoinedAt = m.JoinedAt,
            IsOnline = ChatHub.IsUserOnline(m.UserId),
            LastSeen = ChatHub.GetLastSeen(m.UserId)
        }).ToList();
    }

    public async Task AddMembersToGroupAsync(int currentUserId, string currentUserName, int conversationId, List<int> newUserIds)
    {
        var conv = await _db.ChatConversations.FirstOrDefaultAsync(c => c.Id == conversationId);
        if (conv == null || conv.Type == ChatTypeDto.Direct) throw new InvalidOperationException("عملیات فقط روی گروه‌ها مجاز است.");

        var myMem = await _db.ChatMembers.FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == currentUserId);
        if (myMem == null || myMem.Role == ChatMemberRoleDto.Member)
            throw new UnauthorizedAccessException("تنها مدیران گروه امکان افزودن عضو جدید دارند.");

        var existingMemberIds = await _db.ChatMembers
            .Where(m => m.ConversationId == conversationId)
            .Select(m => m.UserId)
            .ToListAsync();

        var toAddIds = newUserIds.Except(existingMemberIds).Distinct().ToList();
        if (toAddIds.Count == 0) return;

        var users = await _db.Users.AsNoTracking().Where(u => toAddIds.Contains(u.Id) && u.IsActive).ToListAsync();
        var now = DateTime.UtcNow;

        var addedMembers = new List<ChatMember>();
        foreach (var u in users)
        {
            var disp = $"{u.FirstName} {u.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(disp)) disp = u.Username;

            addedMembers.Add(new ChatMember
            {
                ConversationId = conversationId,
                UserId = u.Id,
                UserName = u.Username,
                UserDisplayName = disp,
                UserAvatarUrl = u.PhotoPath != null ? $"/uploads/{u.PhotoPath}" : null,
                Role = ChatMemberRoleDto.Member,
                JoinedAt = now
            });
        }

        _db.ChatMembers.AddRange(addedMembers);

        var names = string.Join("، ", addedMembers.Select(m => m.UserDisplayName));
        _db.ChatMessages.Add(new ChatMessage
        {
            ConversationId = conversationId,
            SenderUserId = currentUserId,
            SenderName = currentUserName,
            Text = $"{currentUserName} کاربر(ان) «{names}» را به گروه اضافه کرد.",
            MessageType = ChatMessageTypeDto.System,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        var allMemberUserIds = await _db.ChatMembers.Where(m => m.ConversationId == conversationId).Select(m => m.UserId).ToListAsync();
        var convDto = await GetConversationByIdAsync(currentUserId, conversationId);
        await _notifier.NotifyConversationUpdatedAsync(allMemberUserIds, convDto);
    }

    public async Task RemoveMemberFromGroupAsync(int currentUserId, string currentUserName, int conversationId, int targetUserId)
    {
        var conv = await _db.ChatConversations.FirstOrDefaultAsync(c => c.Id == conversationId);
        if (conv == null || conv.Type == ChatTypeDto.Direct) throw new InvalidOperationException("عملیات فقط روی گروه‌ها مجاز است.");

        var myMem = await _db.ChatMembers.FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == currentUserId);
        if (myMem == null) throw new UnauthorizedAccessException("شما عضو این گروه نیستید.");

        var targetMem = await _db.ChatMembers.FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == targetUserId);
        if (targetMem == null) return;

        // خروج خود کاربر (Leave) یا اخراج توسط ادمین/سازنده
        bool isSelfLeave = currentUserId == targetUserId;
        bool isAuthorizedKick = myMem.Role == ChatMemberRoleDto.Owner || (myMem.Role == ChatMemberRoleDto.Admin && targetMem.Role == ChatMemberRoleDto.Member);

        if (!isSelfLeave && !isAuthorizedKick)
            throw new UnauthorizedAccessException("شما مجوز اخراج این عضو را ندارید.");

        _db.ChatMembers.Remove(targetMem);

        var now = DateTime.UtcNow;
        var msgText = isSelfLeave
            ? $"«{targetMem.UserDisplayName}» گروه را ترک کرد."
            : $"«{targetMem.UserDisplayName}» توسط {currentUserName} از گروه حذف شد.";

        _db.ChatMessages.Add(new ChatMessage
        {
            ConversationId = conversationId,
            SenderUserId = currentUserId,
            SenderName = currentUserName,
            Text = msgText,
            MessageType = ChatMessageTypeDto.System,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        var remainingMemberUserIds = await _db.ChatMembers.Where(m => m.ConversationId == conversationId).Select(m => m.UserId).ToListAsync();
        var convDto = await GetConversationByIdAsync(currentUserId, conversationId);
        await _notifier.NotifyConversationUpdatedAsync(remainingMemberUserIds.Concat(new[] { targetUserId }), convDto);
    }

    public async Task TogglePinConversationAsync(int currentUserId, int conversationId)
    {
        var myMem = await _db.ChatMembers.FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == currentUserId);
        if (myMem == null) return;

        myMem.IsPinned = !myMem.IsPinned;
        await _db.SaveChangesAsync();
    }

    public async Task ToggleMuteConversationAsync(int currentUserId, int conversationId)
    {
        var myMem = await _db.ChatMembers.FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == currentUserId);
        if (myMem == null) return;

        myMem.IsMuted = !myMem.IsMuted;
        await _db.SaveChangesAsync();
    }

    private static ChatMessageDto MapToMessageDto(ChatMessage m, int currentUserId, int peerLastReadId)
    {
        return new ChatMessageDto
        {
            Id = m.Id,
            ConversationId = m.ConversationId,
            SenderUserId = m.SenderUserId,
            SenderName = m.SenderName,
            SenderAvatarUrl = m.SenderAvatarUrl,
            Text = m.Text,
            MessageType = m.MessageType,
            FileUrl = m.FileUrl,
            FileName = m.FileName,
            FileSizeBytes = m.FileSizeBytes,
            FileContentType = m.FileContentType,
            ReplyToMessageId = m.ReplyToMessageId,
            ReplyToSenderName = m.ReplyToSenderName,
            ReplyToSnippet = m.ReplyToSnippet,
            ForwardFromMessageId = m.ForwardFromMessageId,
            ForwardFromSenderName = m.ForwardFromSenderName,
            ErpModule = m.ErpModule,
            ErpEntityId = m.ErpEntityId,
            ErpEntityTitle = m.ErpEntityTitle,
            ErpEntitySummary = m.ErpEntitySummary,
            IsEdited = m.IsEdited,
            EditedAt = m.EditedAt,
            IsDeleted = m.IsDeleted,
            IsPinned = m.IsPinned,
            Reactions = DeserializeReactions(m.ReactionsJson),
            CreatedAt = m.CreatedAt,
            IsOutgoing = m.SenderUserId == currentUserId,
            IsReadByPeer = peerLastReadId >= m.Id
        };
    }

    private static Dictionary<string, List<ChatReactionUserDto>> DeserializeReactions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, List<ChatReactionUserDto>>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<ChatReactionUserDto>>>(json) ?? new();
        }
        catch
        {
            return new Dictionary<string, List<ChatReactionUserDto>>();
        }
    }
}
