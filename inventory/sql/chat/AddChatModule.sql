-- =========================================================================
-- مایگریشن ساخت جداول ماژول پیام‌رسان سازمانی (Chat Module) مشابه تلگرام
-- =========================================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChatConversations')
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

    CREATE INDEX [IX_ChatConversations_LastMessageAt] ON [dbo].[ChatConversations] ([LastMessageAt]);
END;
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChatMembers')
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
        CONSTRAINT [FK_ChatMembers_ChatConversations] FOREIGN KEY ([ConversationId]) REFERENCES [dbo].[ChatConversations] ([Id]) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX [IX_ChatMembers_ConversationId_UserId] ON [dbo].[ChatMembers] ([ConversationId], [UserId]);
    CREATE INDEX [IX_ChatMembers_UserId] ON [dbo].[ChatMembers] ([UserId]);
END;
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChatMessages')
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
        CONSTRAINT [FK_ChatMessages_ChatConversations] FOREIGN KEY ([ConversationId]) REFERENCES [dbo].[ChatConversations] ([Id]) ON DELETE CASCADE
    );

    CREATE INDEX [IX_ChatMessages_ConversationId_CreatedAt] ON [dbo].[ChatMessages] ([ConversationId], [CreatedAt]);
    CREATE INDEX [IX_ChatMessages_SenderUserId] ON [dbo].[ChatMessages] ([SenderUserId]);
END;
GO
