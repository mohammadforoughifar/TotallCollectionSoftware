using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services.Chat;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Inventory.Chat.Tests;

// Each test has a fresh, real SQLite database; no production connection is used.
public sealed class ChatSearchTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private AppDbContext _db = null!;
    private ChatService _chat = null!;
    private readonly TestChatEnvironment _environment = new();

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _db.Users.AddRange(
            new User { Id = 100, Username = "viewer", FirstName = "کاربر", LastName = "جاری", Role = "Operator" },
            new User { Id = 200, Username = "ali.karimi123", FirstName = "علي", LastName = "كريمي", Role = "Operator" },
            new User { Id = 300, Username = "sara", FirstName = null, LastName = null, Role = "Operator" },
            new User { Id = 400, Username = "ali.inactive", FirstName = "علی", LastName = "کریمی", IsActive = false });
        _db.SystemDepartments.AddRange(
            new SystemDepartment { Id = 7, Name = "فناوري اطلاعات" },
            new SystemDepartment { Id = 8, Name = "فروش" });
        _db.SystemUsers.AddRange(
            new SystemUser { Id = 99, Username = "ali.karimi123", FirstName = "علی", DepartmentId = 7 },
            // A personnel-only record deliberately has the same Id as a real login.
            new SystemUser { Id = 200, Username = "personnel_only", FirstName = "فقط پرسنل", DepartmentId = 8 });
        await _db.SaveChangesAsync();
        _chat = new ChatService(_db, new NoopNotifier(), NullLogger<ChatService>.Instance, new ChatAttachmentService(_db, _environment, new ConfigurationBuilder().Build(), NullLogger<ChatAttachmentService>.Instance));
    }

    [Theory]
    [InlineData("علي")]
    [InlineData("علی")]
    [InlineData("علی کریمی")]
    [InlineData("علي كريمي")]
    [InlineData("  علی   کریمی  ")]
    [InlineData("علی‌کریمی")]
    [InlineData("علیکریمی")]
    [InlineData("عَلِيّ كَرِيمِي")]
    [InlineData("ALI.KARIMI123")]
    [InlineData("ALI.KARIMI۱۲۳")]
    [InlineData("ali.karimi١٢٣")]
    [InlineData("فناوری اطلاعات")]
    public async Task Finds_active_login_by_normalized_name_username_or_department(string query)
    {
        var result = await _chat.GetSoftwareUsersForChatAsync(100, query);
        var user = Assert.Single(result);
        Assert.Equal(200, user.Id);
        Assert.Equal("علي كريمي", user.DisplayName); // Stored text is never rewritten.
        Assert.Equal("فناوري اطلاعات", user.Department);
        Assert.Null(user.ExistingConversationId);
        Assert.False(user.IsOnline); // Being online is not required to find a colleague.
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_search_lists_other_active_logins_only(string? query)
    {
        var result = await _chat.GetSoftwareUsersForChatAsync(100, query);
        Assert.Equal(new[] { 200, 300 }, result.Select(u => u.Id).OrderBy(id => id));
    }

    [Theory]
    [InlineData("definitely-not-a-user")]
    [InlineData("viewer")]
    [InlineData("ali.inactive")]
    [InlineData("personnel_only")]
    public async Task No_match_self_inactive_and_personnel_only_do_not_fall_back_to_everyone(string query)
    {
        Assert.Empty(await _chat.GetSoftwareUsersForChatAsync(100, query));
    }

    [Fact]
    public async Task Missing_names_fall_back_to_username_without_throwing()
    {
        var user = Assert.Single(await _chat.GetSoftwareUsersForChatAsync(100, "SARA"));
        Assert.Equal(300, user.Id);
        Assert.Equal("sara", user.DisplayName);
        Assert.Null(user.Department);
    }

    [Fact]
    public async Task Colleague_is_found_before_any_conversation_and_direct_chat_is_reused()
    {
        Assert.Empty(await _chat.GetConversationsAsync(100, "علی کریمی"));
        Assert.Single(await _chat.GetSoftwareUsersForChatAsync(100, "علی کریمی"));
        var conversation = await _chat.GetOrCreateDirectConversationAsync(100, "viewer", 200);
        var user = Assert.Single(await _chat.GetSoftwareUsersForChatAsync(100, "علی کریمی"));
        Assert.Equal(conversation.Id, user.ExistingConversationId);
        var reopened = await _chat.GetOrCreateDirectConversationAsync(100, "viewer", user.Id);
        Assert.Equal(conversation.Id, reopened.Id);
        Assert.Equal(2, await _db.ChatMembers.CountAsync());
        Assert.Single(await _chat.GetConversationsAsync(100, "علی کریمی"));
    }

    [Fact]
    public async Task Contacts_do_not_disclose_other_peoples_direct_conversations()
    {
        await _chat.GetOrCreateDirectConversationAsync(200, "ali.karimi123", 300);
        var user = Assert.Single(await _chat.GetSoftwareUsersForChatAsync(100, "علی"));
        Assert.Null(user.ExistingConversationId);
    }

    [Fact]
    public async Task Upgrade_creates_missing_chat_tables_without_changing_users()
    {
        await _db.Database.ExecuteSqlRawAsync("DROP TABLE ChatMessages; DROP TABLE ChatMembers; DROP TABLE ChatConversations;");
        await ChatSchemaV1.EnsureSqliteAsync(_db);
        Assert.Equal(2, (await _chat.GetSoftwareUsersForChatAsync(100)).Count);
        Assert.Equal(4, await _db.Users.CountAsync());
        var conversation = await _chat.GetOrCreateDirectConversationAsync(100, "viewer", 200);
        var message = await _chat.SendMessageAsync(100, "viewer", null,
            new SendChatMessageRequest { ConversationId = conversation.Id, Text = "پیام تست ارتقا" });
        Assert.True(message.Id > 0);
    }

    [Fact]
    public async Task Repeated_upgrade_preserves_messages_members_and_conversation_ids()
    {
        var conversation = await _chat.GetOrCreateDirectConversationAsync(100, "viewer", 200);
        var message = await _chat.SendMessageAsync(100, "viewer", null,
            new SendChatMessageRequest { ConversationId = conversation.Id, Text = "پیام حفظ شود" });
        await ChatSchemaV1.EnsureSqliteAsync(_db);
        await ChatSchemaV1.EnsureSqliteAsync(_db);
        Assert.Equal(conversation.Id, (await _db.ChatConversations.SingleAsync()).Id);
        Assert.Equal(2, await _db.ChatMembers.CountAsync());
        var stored = await _db.ChatMessages.SingleAsync();
        Assert.Equal(message.Id, stored.Id);
        Assert.Equal("پیام حفظ شود", stored.Text);
    }

    [Fact]
    public async Task Partial_installation_can_add_messages_without_recreating_existing_members()
    {
        var conversation = await _chat.GetOrCreateDirectConversationAsync(100, "viewer", 200);
        await _db.Database.ExecuteSqlRawAsync("DROP TABLE ChatMessages;");
        await ChatSchemaV1.EnsureSqliteAsync(_db);
        Assert.Equal(conversation.Id, (await _db.ChatConversations.SingleAsync()).Id);
        Assert.Equal(2, await _db.ChatMembers.CountAsync());
        Assert.Empty(await _db.ChatMessages.ToListAsync());
    }

    [Fact]
    public async Task Unread_counts_use_real_messages_and_bounded_monotonic_read_markers()
    {
        var conversation = await _chat.GetOrCreateDirectConversationAsync(100, "viewer", 200);
        var first = await _chat.SendMessageAsync(100, "viewer", null, new SendChatMessageRequest { ConversationId = conversation.Id, Text = "اول" });
        var second = await _chat.SendMessageAsync(100, "viewer", null, new SendChatMessageRequest { ConversationId = conversation.Id, Text = "دوم" });
        Assert.Equal(2, (await _chat.GetChatSummaryAsync(200)).TotalUnreadMessages);
        Assert.Equal(0, (await _chat.GetChatSummaryAsync(100)).TotalUnreadMessages);
        // Corrupt the legacy cached counter deliberately; displayed counts must still be correct.
        await _db.ChatMembers.Where(m => m.UserId == 200).ExecuteUpdateAsync(s => s.SetProperty(m => m.UnreadCount, 999));
        Assert.Equal(2, (await _chat.GetConversationByIdAsync(200, conversation.Id)).UnreadCount);
        await _chat.MarkConversationAsReadAsync(200, conversation.Id, first.Id);
        Assert.Equal(1, (await _chat.GetChatSummaryAsync(200)).TotalUnreadMessages);
        await _chat.MarkConversationAsReadAsync(200, conversation.Id, 0);
        Assert.Equal(1, (await _chat.GetChatSummaryAsync(200)).TotalUnreadMessages);
        await _chat.MarkConversationAsReadAsync(200, conversation.Id, second.Id);
        Assert.Equal(0, (await _chat.GetChatSummaryAsync(200)).TotalUnreadMessages);
        Assert.Empty(await _chat.GetConversationsAsync(200, onlyUnread: true));
        Assert.All(await _chat.GetMessagesAsync(100, conversation.Id), m => Assert.True(m.IsReadByPeer));
        Assert.All(await _chat.GetMessagesAsync(200, conversation.Id), m => Assert.False(m.IsOutgoing));
    }

    [Fact]
    public async Task Deleted_messages_do_not_contribute_to_unread_counts()
    {
        var conversation = await _chat.GetOrCreateDirectConversationAsync(100, "viewer", 200);
        var message = await _chat.SendMessageAsync(100, "viewer", null, new SendChatMessageRequest { ConversationId = conversation.Id, Text = "حذف شود" });
        await _chat.DeleteMessageAsync(100, message.Id);
        Assert.Equal(0, (await _chat.GetChatSummaryAsync(200)).TotalUnreadMessages);
    }

    [Fact]
    public async Task Foreign_read_and_typing_related_membership_remain_private()
    {
        var conversation = await _chat.GetOrCreateDirectConversationAsync(100, "viewer", 200);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _chat.MarkConversationAsReadAsync(300, conversation.Id));
        var message = await _chat.SendMessageAsync(100, "viewer", null, new SendChatMessageRequest { ConversationId = conversation.Id, Text = "خصوصی" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _chat.ToggleReactionAsync(300, "sara", message.Id, "👍"));
        await Assert.ThrowsAsync<ArgumentException>(() => _chat.SendMessageAsync(100, "viewer", null, new SendChatMessageRequest { ConversationId = conversation.Id }));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
        _environment.Dispose();
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

public sealed class ChatMigrationTests
{
    [Fact]
    public void Sql_server_upgrade_is_guarded_and_snapshot_matches_model()
    {
        // SQL generation only: no SQL Server is contacted by this test.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=unused;Database=ChatMigrationTest;Trusted_Connection=True;TrustServerCertificate=True").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript(
            "20260906185126_AddDocArchiveOcrTagsAndErpLinks", "20260907072032_AddChatModule",
            MigrationsSqlGenerationOptions.Idempotent);
        foreach (var table in new[] { "ChatConversations", "ChatMembers", "ChatMessages" })
            Assert.Contains($"IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NULL", sql);
        Assert.Contains("sys.indexes", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("DELETE FROM", sql);
    }

    [Theory]
    [InlineData(" عَلي‌ كريمي ", "علیکریمی")]
    [InlineData("USER_۱۲٣", "user_123")]
    [InlineData(null, "")]
    public void Normalization_is_stable_and_null_safe(string? value, string expected)
    {
        var normalized = ChatSearchText.Normalize(value);
        Assert.Equal(expected, normalized);
        Assert.Equal(normalized, ChatSearchText.Normalize(normalized));
    }
}
