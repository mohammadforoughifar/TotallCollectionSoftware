using System.IO;
using Inventory.Api.Data;
using Inventory.Api.Entities.Chat;
using Inventory.Api.Hubs;
using Inventory.Api.Services.Chat;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Inventory.Chat.Tests;

public sealed class ChatFileTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TestChatEnvironment _env = new();
    private AppDbContext _db = null!;
    private ChatAttachmentService _files = null!;
    private ChatAttachmentService CreateFiles(Dictionary<string, string?> values) =>
        new(_db, _env, new ConfigurationBuilder().AddInMemoryCollection(values!).Build(), NullLogger<ChatAttachmentService>.Instance);
    private ChatService _chat = null!;
    private int _conversationId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _db.Users.AddRange(new User { Id = 1, Username = "sender" }, new User { Id = 2, Username = "recipient" }, new User { Id = 3, Username = "outsider" });
        await _db.SaveChangesAsync();
        _files = CreateFiles(new());
        _chat = new ChatService(_db, new NoopNotifier(), NullLogger<ChatService>.Instance, _files);
        _conversationId = (await _chat.GetOrCreateDirectConversationAsync(1, "sender", 2)).Id;
    }

    private static FormFile MakeFile(byte[] bytes, string name = "پرونده فارسی.txt") => new(new MemoryStream(bytes), 0, bytes.Length, "file", name)
    {
        Headers = new HeaderDictionary(), ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    [Fact]
    public async Task File_metadata_and_bytes_round_trip_through_private_storage()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("محتوای فایل آزمایشی\n123");
        var upload = await _files.UploadAsync(1, _conversationId, MakeFile(bytes));
        Assert.Equal("text/plain", upload.FileContentType);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _files.GetAttachmentAsync(2, upload.Id));
        var message = await _chat.SendMessageAsync(1, "sender", null, new SendChatMessageRequest
        {
            ConversationId = _conversationId, FileUrl = upload.FileUrl, FileName = "fake.exe", FileContentType = "text/html", FileSizeBytes = 9999
        });
        Assert.Equal(upload.FileName, message.FileName);
        Assert.Equal(bytes.Length, message.FileSizeBytes);
        var saved = await _files.GetMessageFileAsync(2, message.Id);
        Assert.StartsWith(Path.Combine(_env.ContentRootPath, "App_Data", "chat"), saved.Path);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(saved.Path));
        Assert.Equal("text/plain", saved.ContentType);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _files.GetMessageFileAsync(3, message.Id));
        await _chat.DeleteMessageAsync(1, message.Id);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _files.GetMessageFileAsync(2, message.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _files.GetAttachmentAsync(2, upload.Id));
    }

    [Fact]
    public async Task Cannot_upload_to_or_reuse_a_file_in_the_wrong_conversation()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _files.UploadAsync(3, _conversationId, MakeFile(new byte[] { 1 })));
        var upload = await _files.UploadAsync(1, _conversationId, MakeFile(new byte[] { 1 }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _files.ApplyToMessageAsync(2, new SendChatMessageRequest { ConversationId = _conversationId, FileUrl = upload.FileUrl }));
        var second = await _chat.GetOrCreateDirectConversationAsync(1, "sender", 3);
        await Assert.ThrowsAsync<ArgumentException>(() => _files.ApplyToMessageAsync(1, new SendChatMessageRequest { ConversationId = second.Id, FileUrl = upload.FileUrl }));
        await Assert.ThrowsAsync<ArgumentException>(() => _files.ApplyToMessageAsync(1, new SendChatMessageRequest { ConversationId = _conversationId, FileUrl = "https://external.invalid/file" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _files.ApplyToMessageAsync(1, new SendChatMessageRequest { ConversationId = _conversationId, FileUrl = "/uploads/chat/guess.txt" }));
    }

    [Fact]
    public async Task Legacy_upload_endpoint_binds_ownership_on_first_send()
    {
        var upload = await _files.UploadAsync(1, null, MakeFile(new byte[] { 1, 2 }));
        Assert.NotNull(await _files.GetAttachmentAsync(1, upload.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _files.GetAttachmentAsync(2, upload.Id));
        var message = await _chat.SendMessageAsync(1, "sender", null, new SendChatMessageRequest { ConversationId = _conversationId, FileUrl = upload.FileUrl });
        Assert.NotNull(await _files.GetMessageFileAsync(2, message.Id));
    }

    [Fact]
    public async Task Empty_oversized_and_truncated_uploads_are_not_saved()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _files.UploadAsync(1, _conversationId, MakeFile(Array.Empty<byte>())));
        var oversized = new FormFile(new MemoryStream(new byte[] { 1 }), 0, ChatFileLimits.MaxFileBytes + 1, "file", "large.bin");
        await Assert.ThrowsAsync<ArgumentException>(() => _files.UploadAsync(1, _conversationId, oversized));
        var truncated = new FormFile(new MemoryStream(new byte[] { 1 }), 0, 10, "file", "truncated.bin");
        await Assert.ThrowsAnyAsync<IOException>(() => _files.UploadAsync(1, _conversationId, truncated));
        Assert.Empty(await _db.ChatAttachments.ToListAsync());
        var root = Path.Combine(_env.ContentRootPath, "App_Data", "chat");
        if (Directory.Exists(root)) Assert.Empty(Directory.GetFiles(root));
    }

    [Fact]
    public async Task Existing_legacy_messages_remain_downloadable_but_paths_are_confined()
    {
        var directory = Path.Combine(_env.WebRootPath, "uploads", "chat");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "old.bin"), new byte[] { 8, 9 });
        var message = new ChatMessage { ConversationId = _conversationId, SenderUserId = 1, SenderName = "sender", FileUrl = "/uploads/chat/old.bin", FileName = "old.txt" };
        _db.ChatMessages.Add(message);
        await _db.SaveChangesAsync();
        Assert.Equal("text/plain", (await _files.GetMessageFileAsync(2, message.Id)).ContentType);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _files.GetMessageFileAsync(3, message.Id));
        message.FileUrl = "/uploads/chat/%2e%2e%2fsecret";
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _files.GetMessageFileAsync(2, message.Id));
    }

    [Fact]
    public async Task Repeated_schema_upgrade_preserves_file_metadata_and_bytes()
    {
        await _db.Database.ExecuteSqlRawAsync("DROP TABLE ChatAttachments;");
        await ChatAttachmentSchemaV1.EnsureSqliteAsync(_db);
        var upload = await _files.UploadAsync(1, _conversationId, MakeFile(new byte[] { 1, 2, 3 }));
        await ChatAttachmentSchemaV1.EnsureSqliteAsync(_db);
        Assert.Equal(upload.Id, (await _db.ChatAttachments.SingleAsync()).Id);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync((await _files.GetAttachmentAsync(1, upload.Id)).Path));
    }

    [Theory]
    [InlineData("image/png", ChatMessageTypeDto.Image)]
    [InlineData("audio/mpeg", ChatMessageTypeDto.Audio)]
    [InlineData("video/mp4", ChatMessageTypeDto.Video)]
    [InlineData("image/svg+xml", ChatMessageTypeDto.File)]
    [InlineData("text/html", ChatMessageTypeDto.File)]
    public void Only_safe_media_types_are_previewed_inline(string mime, ChatMessageTypeDto expected) =>
        Assert.Equal(expected, ChatAttachmentService.MessageTypeFor(mime));

    [Theory]
    [InlineData("/uploads/chat/a.txt", true)]
    [InlineData("/uploads//chat/a.txt", true)]
    [InlineData("/uploads/%2fchat/a.txt", true)]
    [InlineData("/uploads/./chat/a.txt", true)]
    [InlineData("/public/../uploads/chat/a.txt", true)]
    [InlineData("/UPLOADS/CHAT/a.txt", true)]
    [InlineData("/uploads./chat./a.txt", true)]
    [InlineData("/uploads/users/photo.png", false)]
    [InlineData("/uploads/chat/../users/photo.png", false)]
    [InlineData("/api/chat/messages/1/download", false)]
    public void Static_path_aliases_cannot_bypass_chat_file_protection(string path, bool blocked) =>
        Assert.Equal(blocked, ChatAttachmentService.IsLegacyPublicPath(path));

    [Fact]
    public async Task Storage_root_is_configurable_and_reported()
    {
        var altRoot = Path.Combine(_env.ContentRootPath, "RootA");
        var service = CreateFiles(new Dictionary<string, string?> { ["Files:ChatRoot"] = altRoot });
        var chat = new ChatService(_db, new NoopNotifier(), NullLogger<ChatService>.Instance, service);
        var bytes = System.Text.Encoding.UTF8.GetBytes("محتوا");
        var upload = await service.UploadAsync(1, _conversationId, MakeFile(bytes));
        Assert.True(File.Exists(Path.Combine(altRoot, upload.Id.ToString("N"))));
        Assert.False(File.Exists(Path.Combine(_env.ContentRootPath, "App_Data", "chat", upload.Id.ToString("N"))));
        var message = await chat.SendMessageAsync(1, "sender", null, new SendChatMessageRequest { ConversationId = _conversationId, FileUrl = upload.FileUrl });
        Assert.Equal(bytes, await File.ReadAllBytesAsync((await service.GetMessageFileAsync(2, message.Id)).Path));
    }

    [Fact]
    public async Task Download_falls_back_to_content_root_when_configured_root_was_wiped()
    {
        // Simulates: deployment once stored files under App_Data, the directory was recreated (files lost),
        // and the operator now points Files:ChatRoot at a fresh empty folder while a backup restore
        // placed the payload back under the standard content-root location.
        var bytes = new byte[] { 9, 8, 7, 6 };
        var upload = await _files.UploadAsync(1, _conversationId, MakeFile(bytes));
        var message = await _chat.SendMessageAsync(1, "sender", null, new SendChatMessageRequest { ConversationId = _conversationId, FileUrl = upload.FileUrl });
        var originalPath = (await _files.GetMessageFileAsync(2, message.Id)).Path;
        File.Delete(originalPath); // App_Data wiped by redeploy
        var reader = CreateFiles(new Dictionary<string, string?> { ["Files:ChatRoot"] = Path.Combine(_env.ContentRootPath, "RootEmpty") });
        // Files outside the allowed candidate roots are never served (confinement).
        var stranger = Path.Combine(_env.ContentRootPath, "RootA", upload.Id.ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(stranger)!);
        await File.WriteAllBytesAsync(stranger, bytes);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => reader.GetMessageFileAsync(2, message.Id));
        // Restored payload under {contentRoot}/App_Data/chat is served via fallback.
        Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
        await File.WriteAllBytesAsync(originalPath, bytes);
        Assert.Equal(bytes, await File.ReadAllBytesAsync((await reader.GetMessageFileAsync(2, message.Id)).Path));
    }

    [Fact]
    public async Task Empty_or_missing_config_keeps_the_default_content_root_store()
    {
        var service = CreateFiles(new Dictionary<string, string?> { ["Files:ChatRoot"] = "", ["Files:ChatLegacyRoot"] = " " });
        var bytes = new byte[] { 4, 2 };
        var upload = await service.UploadAsync(1, _conversationId, MakeFile(bytes));
        Assert.True(File.Exists(Path.Combine(_env.ContentRootPath, "App_Data", "chat", upload.Id.ToString("N"))));
    }

    [Fact]
    public async Task Legacy_files_are_also_resolved_from_fallback_roots()
    {
        var legacyAlt = Path.Combine(_env.ContentRootPath, "LegacyA");
        var service = CreateFiles(new Dictionary<string, string?> { ["Files:ChatLegacyRoot"] = legacyAlt });
        var contentRootLegacy = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads", "chat");
        Directory.CreateDirectory(contentRootLegacy);
        var bytes = new byte[] { 1, 2, 3 };
        await File.WriteAllBytesAsync(Path.Combine(contentRootLegacy, "legacy.bin"), bytes);
        var message = new ChatMessage { ConversationId = _conversationId, SenderUserId = 1, SenderName = "sender", FileUrl = "/uploads/chat/legacy.bin", FileName = "legacy.bin" };
        _db.ChatMessages.Add(message);
        await _db.SaveChangesAsync();
        Assert.Equal(bytes, await File.ReadAllBytesAsync((await service.GetMessageFileAsync(2, message.Id)).Path));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
        _env.Dispose();
    }

    private sealed class NoopNotifier : IChatRealtimeNotifier
    {
        public Task NotifyMessageReceivedAsync(int id, IEnumerable<int> users, ChatMessageDto message) => Task.CompletedTask;
        public Task NotifyMessageUpdatedAsync(int id, IEnumerable<int> users, ChatMessageDto message) => Task.CompletedTask;
        public Task NotifyMessageDeletedAsync(int id, IEnumerable<int> users, int message) => Task.CompletedTask;
        public Task NotifyReactionAsync(int id, IEnumerable<int> users, int message, Dictionary<string, List<ChatReactionUserDto>> reactions) => Task.CompletedTask;
        public Task NotifyMessagesReadAsync(int id, int reader, int message, IEnumerable<int> users) => Task.CompletedTask;
        public Task NotifyConversationUpdatedAsync(IEnumerable<int> users, ChatConversationDto conversation) => Task.CompletedTask;
    }
}
