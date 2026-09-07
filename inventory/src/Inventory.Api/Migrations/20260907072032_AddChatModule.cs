using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations;

/// <summary>
/// ثبت رسمی جداول چت در زنجیرهٔ EF؛ سازگار با نصب‌های دارای اسکریپت دستی قدیمی.
/// Up فقط اشیای مفقود را می‌سازد و هیچ داده‌ای را بازنویسی یا حذف نمی‌کند.
/// </summary>
public partial class AddChatModule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            migrationBuilder.Sql(ChatSchemaV1.SqliteCreateSql);
            return;
        }

        if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
            throw new NotSupportedException("Chat schema supports SQL Server and SQLite only.");

        migrationBuilder.Sql("""
                -- =========================================================================
                -- مایگریشن ساخت جداول ماژول پیام‌رسان سازمانی (Chat Module) مشابه تلگرام
                -- =========================================================================
                
                IF OBJECT_ID(N'[dbo].[ChatConversations]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ChatConversations] (
                        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [Title] NVARCHAR(200) NOT NULL,
                        [Type] INT NOT NULL DEFAULT 1,
                        [Description] NVARCHAR(500) NULL,
                        [AvatarUrl] NVARCHAR(500) NULL,
                        [CreatedByUserId] INT NOT NULL,
                        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        [LastMessageAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        [LastMessageSnippet] NVARCHAR(500) NULL,
                        [LastMessageSenderId] INT NULL,
                        [LastMessageSenderName] NVARCHAR(100) NULL,
                        [PinnedMessageId] INT NULL,
                        [IsArchived] BIT NOT NULL DEFAULT 0
                    );
                
                END;
                
                IF OBJECT_ID(N'[dbo].[ChatMembers]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ChatMembers] (
                        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [ConversationId] INT NOT NULL,
                        [UserId] INT NOT NULL,
                        [UserName] NVARCHAR(100) NOT NULL,
                        [UserDisplayName] NVARCHAR(150) NOT NULL,
                        [UserAvatarUrl] NVARCHAR(500) NULL,
                        [Role] INT NOT NULL DEFAULT 3,
                        [JoinedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        [LastReadMessageId] INT NOT NULL DEFAULT 0,
                        [UnreadCount] INT NOT NULL DEFAULT 0,
                        [IsMuted] BIT NOT NULL DEFAULT 0,
                        [IsPinned] BIT NOT NULL DEFAULT 0,
                        [IsArchived] BIT NOT NULL DEFAULT 0,
                        CONSTRAINT [FK_ChatMembers_ChatConversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [dbo].[ChatConversations] ([Id]) ON DELETE CASCADE
                    );
                
                
                END;
                
                IF OBJECT_ID(N'[dbo].[ChatMessages]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ChatMessages] (
                        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [ConversationId] INT NOT NULL,
                        [SenderUserId] INT NOT NULL,
                        [SenderName] NVARCHAR(150) NOT NULL,
                        [SenderAvatarUrl] NVARCHAR(500) NULL,
                        [Text] NVARCHAR(MAX) NULL,
                        [MessageType] INT NOT NULL DEFAULT 1,
                        [FileUrl] NVARCHAR(500) NULL,
                        [FileName] NVARCHAR(250) NULL,
                        [FileSizeBytes] BIGINT NULL,
                        [FileContentType] NVARCHAR(100) NULL,
                        [ReplyToMessageId] INT NULL,
                        [ReplyToSenderName] NVARCHAR(150) NULL,
                        [ReplyToSnippet] NVARCHAR(300) NULL,
                        [ForwardFromMessageId] INT NULL,
                        [ForwardFromSenderName] NVARCHAR(150) NULL,
                        [ErpModule] NVARCHAR(50) NULL,
                        [ErpEntityId] NVARCHAR(50) NULL,
                        [ErpEntityTitle] NVARCHAR(200) NULL,
                        [ErpEntitySummary] NVARCHAR(500) NULL,
                        [IsEdited] BIT NOT NULL DEFAULT 0,
                        [EditedAt] DATETIME2 NULL,
                        [IsDeleted] BIT NOT NULL DEFAULT 0,
                        [DeletedAt] DATETIME2 NULL,
                        [IsPinned] BIT NOT NULL DEFAULT 0,
                        [ReactionsJson] NVARCHAR(MAX) NULL,
                        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        CONSTRAINT [FK_ChatMessages_ChatConversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [dbo].[ChatConversations] ([Id]) ON DELETE CASCADE
                    );
                
                
                END;
                
                -- ایندکس‌ها حتی در نصب‌هایی که جدول‌ها را قبلاً دستی ساخته‌اند بررسی می‌شوند.
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ChatConversations]') AND name = N'IX_ChatConversations_LastMessageAt')
                    CREATE INDEX [IX_ChatConversations_LastMessageAt] ON [dbo].[ChatConversations] ([LastMessageAt]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ChatMembers]') AND name = N'IX_ChatMembers_ConversationId_UserId')
                    CREATE UNIQUE INDEX [IX_ChatMembers_ConversationId_UserId] ON [dbo].[ChatMembers] ([ConversationId], [UserId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ChatMembers]') AND name = N'IX_ChatMembers_UserId')
                    CREATE INDEX [IX_ChatMembers_UserId] ON [dbo].[ChatMembers] ([UserId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ChatMessages]') AND name = N'IX_ChatMessages_ConversationId_CreatedAt')
                    CREATE INDEX [IX_ChatMessages_ConversationId_CreatedAt] ON [dbo].[ChatMessages] ([ConversationId], [CreatedAt]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ChatMessages]') AND name = N'IX_ChatMessages_SenderUserId')
                    CREATE INDEX [IX_ChatMessages_SenderUserId] ON [dbo].[ChatMessages] ([SenderUserId]);
                """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // ممکن است جداول قبل از این مایگریشن به‌صورت دستی ساخته و پر شده باشند.
        // حذف خودکار آن‌ها در rollback باعث از دست رفتن گفتگوهای واقعی می‌شود.
        throw new NotSupportedException(
            "Automatic rollback of the chat schema is disabled to protect existing conversations. Restore a reviewed backup if required.");
    }
}
