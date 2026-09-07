using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Inventory.Api.Data;
using Inventory.Api.Entities.Chat;
using Inventory.Api.Hubs;
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
    Task MarkConversationAsReadAsync(int currentUserId, int conversationId);
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

    public ChatService(AppDbContext db, IChatRealtimeNotifier notifier, ILogger<ChatService> logger)
    {
        _db = db;
        _notifier = notifier;
        _logger = logger;
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

        var membershipMap = myMemberships.ToDictionary(m => m.ConversationId);
        var membersGrouped = allMembers.GroupBy(m => m.ConversationId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<ChatConversationDto>();

        foreach (var conv in list)
        {
            membershipMap.TryGetValue(conv.Id, out var myMem);
            if (myMem == null) continue;

            if (onlyUnread && myMem.UnreadCount == 0) continue;

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
                UnreadCount = myMem.UnreadCount,
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

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLowerInvariant();
                bool matches = dto.Title.ToLowerInvariant().Contains(s) ||
                               (dto.LastMessageSnippet != null && dto.LastMessageSnippet.ToLowerInvariant().Contains(s));
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
            UnreadCount = myMem.UnreadCount,
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

        var conv = await _db.ChatConversations.FirstOrDefaultAsync(c => c.Id == request.ConversationId);
        if (conv == null) throw new KeyNotFoundException("گفتگو یافت نشد.");

        var myMem = await _db.ChatMembers.FirstOrDefaultAsync(m => m.ConversationId == request.ConversationId && m.UserId == currentUserId);
        if (myMem == null) throw new UnauthorizedAccessException("شما عضو این گفتگو نیستید.");

        var now = DateTime.UtcNow;
        var msg = new ChatMessage
        {
            ConversationId = request.ConversationId,
            SenderUserId = currentUserId,
            SenderName = string.IsNullOrWhiteSpace(myMem.UserDisplayName) ? currentUserName : myMem.UserDisplayName,
            SenderAvatarUrl = currentUserAvatar ?? myMem.UserAvatarUrl,
            Text = request.Text?.Trim(),
            MessageType = request.MessageType,
            FileUrl = request.FileUrl,
            FileName = request.FileName,
            FileSizeBytes = request.FileSizeBytes,
            FileContentType = request.FileContentType,
            ReplyToMessageId = request.ReplyToMessageId,
            ForwardFromMessageId = request.ForwardFromMessageId,
            ErpModule = request.ErpModule,
            ErpEntityId = request.ErpEntityId,
            ErpEntityTitle = request.ErpEntityTitle,
            ErpEntitySummary = request.ErpEntitySummary,
            CreatedAt = now
        };

        // اطلاعات پیام ریپلای‌شده
        if (request.ReplyToMessageId.HasValue && request.ReplyToMessageId.Value > 0)
        {
            var repMsg = await _db.ChatMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == request.ReplyToMessageId.Value);

            if (repMsg != null)
            {
                msg.ReplyToSenderName = repMsg.SenderName;
                msg.ReplyToSnippet = !string.IsNullOrWhiteSpace(repMsg.Text)
                    ? (repMsg.Text.Length > 50 ? repMsg.Text.Substring(0, 50) + "..." : repMsg.Text)
                    : (repMsg.FileName ?? "پیوست");
            }
        }

        _db.ChatMessages.Add(msg);
        await _db.SaveChangesAsync();

        // به‌روزرسانی متاداده‌های مکالمه
        string snippet = !string.IsNullOrWhiteSpace(msg.Text)
            ? (msg.Text.Length > 60 ? msg.Text.Substring(0, 60) + "..." : msg.Text)
            : (msg.FileName ?? (msg.ErpEntityTitle ?? "پیام رسانه‌ای"));

        conv.LastMessageAt = now;
        conv.LastMessageSnippet = snippet;
        conv.LastMessageSenderId = currentUserId;
        conv.LastMessageSenderName = msg.SenderName;

        // افزایش شمارنده خوانده‌نشده برای سایر اعضا
        var otherMembers = await _db.ChatMembers
            .Where(m => m.ConversationId == request.ConversationId && m.UserId != currentUserId)
            .ToListAsync();

        foreach (var m in otherMembers)
        {
            m.UnreadCount++;
        }

        // برای خود فرستنده، آخرین پیام خوانده شده برابر این پیام است
        myMem.LastReadMessageId = msg.Id;
        myMem.UnreadCount = 0;

        await _db.SaveChangesAsync();

        var allMemberUserIds = otherMembers.Select(m => m.UserId).Concat(new[] { currentUserId }).ToList();
        var msgDto = MapToMessageDto(msg, currentUserId, 0);

        // اعلان بلادرنگ پیام جدید
        await _notifier.NotifyMessageReceivedAsync(request.ConversationId, allMemberUserIds, msgDto);

        return msgDto;
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

    public async Task MarkConversationAsReadAsync(int currentUserId, int conversationId)
    {
        var myMem = await _db.ChatMembers
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == currentUserId);

        if (myMem == null) return;

        var maxMessageId = await _db.ChatMessages
            .Where(m => m.ConversationId == conversationId)
            .Select(m => (int?)m.Id)
            .MaxAsync() ?? 0;

        myMem.LastReadMessageId = maxMessageId;
        myMem.UnreadCount = 0;
        await _db.SaveChangesAsync();

        var peerUserIds = await _db.ChatMembers
            .Where(m => m.ConversationId == conversationId && m.UserId != currentUserId)
            .Select(m => m.UserId)
            .ToListAsync();

        await _notifier.NotifyMessagesReadAsync(conversationId, currentUserId, maxMessageId, peerUserIds);
    }

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
        // فهرست تمامی کاربران فعال درون نرم‌افزار جهت چت
        var q = _db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.Id != currentUserId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            q = q.Where(u => u.Username.ToLower().Contains(s) ||
                             (u.FirstName != null && u.FirstName.ToLower().Contains(s)) ||
                             (u.LastName != null && u.LastName.ToLower().Contains(s)));
        }

        var users = await q
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToListAsync();

        // بررسی مکالمات خصوصی موجود با هر کاربر
        var existingDirects = await (from m1 in _db.ChatMembers.AsNoTracking()
                                     join m2 in _db.ChatMembers.AsNoTracking() on m1.ConversationId equals m2.ConversationId
                                     join c in _db.ChatConversations.AsNoTracking() on m1.ConversationId equals c.Id
                                     where c.Type == ChatTypeDto.Direct && m1.UserId == currentUserId
                                     select new { TargetUserId = m2.UserId, c.Id }).ToListAsync();

        var directMap = existingDirects.GroupBy(x => x.TargetUserId).ToDictionary(g => g.Key, g => g.First().Id);

        return users.Select(u =>
        {
            var disp = $"{u.FirstName} {u.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(disp)) disp = u.Username;

            directMap.TryGetValue(u.Id, out var existingConvId);

            return new ChatUserDto
            {
                Id = u.Id,
                Username = u.Username,
                DisplayName = disp,
                AvatarUrl = u.PhotoPath != null ? $"/uploads/{u.PhotoPath}" : null,
                Department = null,
                Role = u.Role,
                IsOnline = ChatHub.IsUserOnline(u.Id),
                LastSeen = ChatHub.GetLastSeen(u.Id),
                ExistingConversationId = existingConvId > 0 ? existingConvId : null
            };
        }).ToList();
    }

    public async Task<ChatSummaryDto> GetChatSummaryAsync(int currentUserId)
    {
        var myMemberships = await _db.ChatMembers
            .AsNoTracking()
            .Where(m => m.UserId == currentUserId && !m.IsArchived)
            .ToListAsync();

        return new ChatSummaryDto
        {
            TotalUnreadMessages = myMemberships.Sum(m => m.UnreadCount),
            UnreadConversationsCount = myMemberships.Count(m => m.UnreadCount > 0)
        };
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
