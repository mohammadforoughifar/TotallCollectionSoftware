using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.SignalR.Client;

namespace Inventory.Client.Services;

public interface IChatClientService
{
    Task<List<ChatConversationDto>> GetConversationsAsync(string? search = null, ChatTypeDto? type = null, bool onlyUnread = false);
    Task<ChatConversationDto> GetConversationAsync(int id);
    Task<ChatConversationDto> CreateDirectAsync(int targetUserId);
    Task<ChatConversationDto> CreateGroupAsync(CreateGroupChatRequest req);
    Task<List<ChatMessageDto>> GetMessagesAsync(int conversationId, int? beforeId = null, int pageSize = 50, string? search = null);
    Task<ChatMessageDto> SendMessageAsync(int conversationId, SendChatMessageRequest req);
    Task<ChatMessageDto> EditMessageAsync(int messageId, string newText);
    Task DeleteMessageAsync(int messageId);
    Task<Dictionary<string, List<ChatReactionUserDto>>> ReactAsync(int messageId, string emoji);
    Task<ChatMessageDto> TogglePinMessageAsync(int messageId);
    Task MarkReadAsync(int conversationId);
    Task TogglePinConversationAsync(int conversationId);
    Task ToggleMuteConversationAsync(int conversationId);
    Task<List<ChatMemberDto>> GetMembersAsync(int conversationId);
    Task AddMembersAsync(int conversationId, List<int> userIds);
    Task RemoveMemberAsync(int conversationId, int userId);
    Task<List<ChatUserDto>> GetSoftwareUsersAsync(string? search = null);
    Task<ChatSummaryDto> GetSummaryAsync();
    Task<ChatUploadResultDto> UploadAttachmentAsync(IBrowserFile file);

    // اتصال SignalR بلادرنگ
    Task EnsureHubConnectedAsync();
    Task JoinConversationHubAsync(int conversationId);
    Task LeaveConversationHubAsync(int conversationId);
    Task SendTypingHubAsync(int conversationId);

    // رویدادهای زنده
    event Action<ChatMessageDto>? OnMessageReceived;
    event Action<ChatMessageDto>? OnMessageUpdated;
    event Action<int, int>? OnMessageDeleted;
    event Action<int, int, Dictionary<string, List<ChatReactionUserDto>>>? OnReactionUpdated;
    event Action<int, int, int>? OnMessagesRead;
    event Action<int, int, string>? OnUserTyping;
    event Action<int, bool, DateTime>? OnUserStatusChanged;
    event Action<ChatConversationDto>? OnConversationUpdated;
}

public class ChatUploadResultDto
{
    public string FileUrl { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string FileContentType { get; set; } = "";
    public ChatMessageTypeDto MessageType { get; set; } = ChatMessageTypeDto.File;
}

public class ChatClientService : IChatClientService, IAsyncDisposable
{
    private readonly IApiClient _api;
    private readonly IAuthState _auth;
    private readonly ApiOptions _opts;
    private readonly HttpClient _http;
    private HubConnection? _hub;

    public event Action<ChatMessageDto>? OnMessageReceived;
    public event Action<ChatMessageDto>? OnMessageUpdated;
    public event Action<int, int>? OnMessageDeleted;
    public event Action<int, int, Dictionary<string, List<ChatReactionUserDto>>>? OnReactionUpdated;
    public event Action<int, int, int>? OnMessagesRead;
    public event Action<int, int, string>? OnUserTyping;
    public event Action<int, bool, DateTime>? OnUserStatusChanged;
    public event Action<ChatConversationDto>? OnConversationUpdated;

    public ChatClientService(IApiClient api, IAuthState auth, ApiOptions opts, HttpClient http)
    {
        _api = api;
        _auth = auth;
        _opts = opts;
        _http = http;
    }

    public async Task<List<ChatConversationDto>> GetConversationsAsync(string? search = null, ChatTypeDto? type = null, bool onlyUnread = false)
    {
        var url = $"api/chat/conversations?onlyUnread={onlyUnread}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (type.HasValue) url += $"&type={(int)type.Value}";
        return await _api.GetAsync<List<ChatConversationDto>>(url) ?? new();
    }

    public async Task<ChatConversationDto> GetConversationAsync(int id)
    {
        return await _api.GetAsync<ChatConversationDto>($"api/chat/conversations/{id}");
    }

    public async Task<ChatConversationDto> CreateDirectAsync(int targetUserId)
    {
        return await _api.PostAsync<ChatConversationDto>("api/chat/conversations/direct", new CreateDirectChatRequest { TargetUserId = targetUserId });
    }

    public async Task<ChatConversationDto> CreateGroupAsync(CreateGroupChatRequest req)
    {
        return await _api.PostAsync<ChatConversationDto>("api/chat/conversations/group", req);
    }

    public async Task<List<ChatMessageDto>> GetMessagesAsync(int conversationId, int? beforeId = null, int pageSize = 50, string? search = null)
    {
        var url = $"api/chat/conversations/{conversationId}/messages?pageSize={pageSize}";
        if (beforeId.HasValue) url += $"&beforeId={beforeId.Value}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        return await _api.GetAsync<List<ChatMessageDto>>(url) ?? new();
    }

    public async Task<ChatMessageDto> SendMessageAsync(int conversationId, SendChatMessageRequest req)
    {
        return await _api.PostAsync<ChatMessageDto>($"api/chat/conversations/{conversationId}/messages", req);
    }

    public async Task<ChatMessageDto> EditMessageAsync(int messageId, string newText)
    {
        return await _api.PutAsync<ChatMessageDto>($"api/chat/messages/{messageId}", new EditChatMessageRequest { NewText = newText });
    }

    public async Task DeleteMessageAsync(int messageId)
    {
        await _api.DeleteAsync($"api/chat/messages/{messageId}");
    }

    public async Task<Dictionary<string, List<ChatReactionUserDto>>> ReactAsync(int messageId, string emoji)
    {
        return await _api.PostAsync<Dictionary<string, List<ChatReactionUserDto>>>($"api/chat/messages/{messageId}/react", new ReactChatMessageRequest { Emoji = emoji });
    }

    public async Task<ChatMessageDto> TogglePinMessageAsync(int messageId)
    {
        return await _api.PostAsync<ChatMessageDto>($"api/chat/messages/{messageId}/pin", new { });
    }

    public async Task MarkReadAsync(int conversationId)
    {
        await _api.PostAsync<object>($"api/chat/conversations/{conversationId}/read", new { });
    }

    public async Task TogglePinConversationAsync(int conversationId)
    {
        await _api.PostAsync<object>($"api/chat/conversations/{conversationId}/pin", new { });
    }

    public async Task ToggleMuteConversationAsync(int conversationId)
    {
        await _api.PostAsync<object>($"api/chat/conversations/{conversationId}/mute", new { });
    }

    public async Task<List<ChatMemberDto>> GetMembersAsync(int conversationId)
    {
        return await _api.GetAsync<List<ChatMemberDto>>($"api/chat/conversations/{conversationId}/members") ?? new();
    }

    public async Task AddMembersAsync(int conversationId, List<int> userIds)
    {
        await _api.PostAsync<object>($"api/chat/conversations/{conversationId}/members", userIds);
    }

    public async Task RemoveMemberAsync(int conversationId, int userId)
    {
        await _api.DeleteAsync($"api/chat/conversations/{conversationId}/members/{userId}");
    }

    public async Task<List<ChatUserDto>> GetSoftwareUsersAsync(string? search = null)
    {
        var url = "api/chat/users";
        if (!string.IsNullOrWhiteSpace(search)) url += $"?search={Uri.EscapeDataString(search)}";
        return await _api.GetAsync<List<ChatUserDto>>(url) ?? new();
    }

    public async Task<ChatSummaryDto> GetSummaryAsync()
    {
        return await _api.GetAsync<ChatSummaryDto>("api/chat/summary") ?? new();
    }

    public async Task<ChatUploadResultDto> UploadAttachmentAsync(IBrowserFile file)
    {
        using var stream = file.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024);
        return await _api.PostFileAsync<ChatUploadResultDto>("api/chat/upload", stream, file.Name);
    }

    public async Task EnsureHubConnectedAsync()
    {
        if (string.IsNullOrEmpty(_auth.Token)) return;

        if (_hub != null && _hub.State == HubConnectionState.Connected)
            return;

        var hubUrl = $"{_opts.BaseUrl.TrimEnd('/')}/hubs/chat?access_token={_auth.Token}&userId={_auth.UserId}";

        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.AccessTokenProvider = () => Task.FromResult<string?>(_auth.Token);
            })
            .WithAutomaticReconnect()
            .Build();

        _hub.On<ChatMessageDto>("ReceiveMessage", msg => OnMessageReceived?.Invoke(msg));
        _hub.On<int, ChatMessageDto>("NewMessageNotification", (cid, msg) => OnMessageReceived?.Invoke(msg));
        _hub.On<ChatMessageDto>("MessageUpdated", msg => OnMessageUpdated?.Invoke(msg));
        _hub.On<int, int>("MessageDeleted", (cid, mid) => OnMessageDeleted?.Invoke(cid, mid));
        _hub.On<int, int, Dictionary<string, List<ChatReactionUserDto>>>("MessageReactionUpdated", (cid, mid, r) => OnReactionUpdated?.Invoke(cid, mid, r));
        _hub.On<int, int, int>("MessagesRead", (cid, uid, lrid) => OnMessagesRead?.Invoke(cid, uid, lrid));
        _hub.On<int, int, string>("UserTyping", (cid, uid, uname) => OnUserTyping?.Invoke(cid, uid, uname));
        _hub.On<int, bool, DateTime>("UserStatusChanged", (uid, online, seen) => OnUserStatusChanged?.Invoke(uid, online, seen));
        _hub.On<ChatConversationDto>("ConversationUpdated", c => OnConversationUpdated?.Invoke(c));

        try
        {
            await _hub.StartAsync();
        }
        catch { /* تلاش مجدد خودکار */ }
    }

    public async Task JoinConversationHubAsync(int conversationId)
    {
        if (_hub != null && _hub.State == HubConnectionState.Connected)
        {
            try { await _hub.InvokeAsync("JoinConversation", conversationId); } catch { }
        }
    }

    public async Task LeaveConversationHubAsync(int conversationId)
    {
        if (_hub != null && _hub.State == HubConnectionState.Connected)
        {
            try { await _hub.InvokeAsync("LeaveConversation", conversationId); } catch { }
        }
    }

    public async Task SendTypingHubAsync(int conversationId)
    {
        if (_hub != null && _hub.State == HubConnectionState.Connected)
        {
            try { await _hub.InvokeAsync("SendTyping", conversationId); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub != null)
        {
            await _hub.DisposeAsync();
        }
    }
}
