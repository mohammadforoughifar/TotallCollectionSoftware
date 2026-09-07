using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.SignalR;

namespace Inventory.Api.Hubs;

/// <summary>
/// هاب بلادرنگ پیام‌رسان سازمانی مشابه تلگرام
/// </summary>
public class ChatHub : Hub
{
    // ذخیره وضعیت آنلاین کاربران (UserId -> Set of ConnectionIds)
    private static readonly ConcurrentDictionary<int, HashSet<string>> _onlineUsers = new();
    private static readonly ConcurrentDictionary<int, DateTime> _lastSeenTimes = new();

    public static bool IsUserOnline(int userId)
    {
        return _onlineUsers.TryGetValue(userId, out var conns) && conns.Count > 0;
    }

    public static DateTime? GetLastSeen(int userId)
    {
        return _lastSeenTimes.TryGetValue(userId, out var dt) ? dt : null;
    }

    public static List<int> GetOnlineUserIds()
    {
        return _onlineUsers.Where(kvp => kvp.Value.Count > 0).Select(kvp => kvp.Key).ToList();
    }

    public override async Task OnConnectedAsync()
    {
        var uid = GetUserId();
        if (uid > 0)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Chat_User_{uid}");

            bool becameOnline = false;
            _onlineUsers.AddOrUpdate(uid,
                _ => { becameOnline = true; return new HashSet<string> { Context.ConnectionId }; },
                (_, set) => {
                    lock (set) {
                        if (set.Count == 0) becameOnline = true;
                        set.Add(Context.ConnectionId);
                    }
                    return set;
                });

            _lastSeenTimes[uid] = DateTime.UtcNow;

            if (becameOnline)
            {
                await Clients.Others.SendAsync("UserStatusChanged", uid, true, DateTime.UtcNow);
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var uid = GetUserId();
        if (uid > 0)
        {
            bool becameOffline = false;
            if (_onlineUsers.TryGetValue(uid, out var set))
            {
                lock (set)
                {
                    set.Remove(Context.ConnectionId);
                    if (set.Count == 0)
                    {
                        becameOffline = true;
                    }
                }
            }

            _lastSeenTimes[uid] = DateTime.UtcNow;

            if (becameOffline)
            {
                await Clients.Others.SendAsync("UserStatusChanged", uid, false, DateTime.UtcNow);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>پیوستن به اتاق گفتگوی فعال</summary>
    public async Task JoinConversation(int conversationId)
    {
        if (conversationId > 0)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Chat_Room_{conversationId}");
        }
    }

    /// <summary>خروج از اتاق گفتگوی فعال</summary>
    public async Task LeaveConversation(int conversationId)
    {
        if (conversationId > 0)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Chat_Room_{conversationId}");
        }
    }

    /// <summary>ارسال وضعیت در حال تایپ (Typing...)</summary>
    public async Task SendTyping(int conversationId)
    {
        var uid = GetUserId();
        var userName = Context.User?.Identity?.Name ?? "کاربر";
        if (uid > 0 && conversationId > 0)
        {
            await Clients.Group($"Chat_Room_{conversationId}").SendAsync("UserTyping", conversationId, uid, userName);
        }
    }

    private int GetUserId()
    {
        if (int.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id1) && id1 > 0)
            return id1;

        var q = Context.GetHttpContext()?.Request.Query["userId"].ToString();
        if (int.TryParse(q, out var id2) && id2 > 0)
            return id2;

        return 0;
    }
}

/// <summary>رابط ارسال پیام‌های بلادرنگ چت به کلاینت‌ها</summary>
public interface IChatRealtimeNotifier
{
    Task NotifyMessageReceivedAsync(int conversationId, IEnumerable<int> recipientUserIds, ChatMessageDto message);
    Task NotifyMessageUpdatedAsync(int conversationId, IEnumerable<int> recipientUserIds, ChatMessageDto message);
    Task NotifyMessageDeletedAsync(int conversationId, IEnumerable<int> recipientUserIds, int messageId);
    Task NotifyReactionAsync(int conversationId, IEnumerable<int> recipientUserIds, int messageId, Dictionary<string, List<ChatReactionUserDto>> reactions);
    Task NotifyMessagesReadAsync(int conversationId, int readerUserId, int lastReadMessageId, IEnumerable<int> peerUserIds);
    Task NotifyConversationUpdatedAsync(IEnumerable<int> recipientUserIds, ChatConversationDto conversation);
}

public class ChatRealtimeNotifier : IChatRealtimeNotifier
{
    private readonly IHubContext<ChatHub> _hub;

    public ChatRealtimeNotifier(IHubContext<ChatHub> hub)
    {
        _hub = hub;
    }

    public async Task NotifyMessageReceivedAsync(int conversationId, IEnumerable<int> recipientUserIds, ChatMessageDto message)
    {
        // ارسال به اتاق فعال
        await _hub.Clients.Group($"Chat_Room_{conversationId}").SendAsync("ReceiveMessage", message);

        // ارسال به چنل خصوصی هر کاربر برای بروزرسانی badge و لیست دیالوگ‌ها
        foreach (var uid in recipientUserIds.Distinct())
        {
            await _hub.Clients.Group($"Chat_User_{uid}").SendAsync("NewMessageNotification", conversationId, message);
        }
    }

    public async Task NotifyMessageUpdatedAsync(int conversationId, IEnumerable<int> recipientUserIds, ChatMessageDto message)
    {
        await _hub.Clients.Group($"Chat_Room_{conversationId}").SendAsync("MessageUpdated", message);
        foreach (var uid in recipientUserIds.Distinct())
        {
            await _hub.Clients.Group($"Chat_User_{uid}").SendAsync("MessageUpdated", message);
        }
    }

    public async Task NotifyMessageDeletedAsync(int conversationId, IEnumerable<int> recipientUserIds, int messageId)
    {
        await _hub.Clients.Group($"Chat_Room_{conversationId}").SendAsync("MessageDeleted", conversationId, messageId);
        foreach (var uid in recipientUserIds.Distinct())
        {
            await _hub.Clients.Group($"Chat_User_{uid}").SendAsync("MessageDeleted", conversationId, messageId);
        }
    }

    public async Task NotifyReactionAsync(int conversationId, IEnumerable<int> recipientUserIds, int messageId, Dictionary<string, List<ChatReactionUserDto>> reactions)
    {
        await _hub.Clients.Group($"Chat_Room_{conversationId}").SendAsync("MessageReactionUpdated", conversationId, messageId, reactions);
    }

    public async Task NotifyMessagesReadAsync(int conversationId, int readerUserId, int lastReadMessageId, IEnumerable<int> peerUserIds)
    {
        await _hub.Clients.Group($"Chat_Room_{conversationId}").SendAsync("MessagesRead", conversationId, readerUserId, lastReadMessageId);
        foreach (var uid in peerUserIds.Distinct())
        {
            await _hub.Clients.Group($"Chat_User_{uid}").SendAsync("MessagesRead", conversationId, readerUserId, lastReadMessageId);
        }
    }

    public async Task NotifyConversationUpdatedAsync(IEnumerable<int> recipientUserIds, ChatConversationDto conversation)
    {
        foreach (var uid in recipientUserIds.Distinct())
        {
            await _hub.Clients.Group($"Chat_User_{uid}").SendAsync("ConversationUpdated", conversation);
        }
    }
}
