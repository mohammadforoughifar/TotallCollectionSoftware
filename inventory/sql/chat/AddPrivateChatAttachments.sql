BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907081538_AddPrivateChatAttachments'
)
BEGIN
    CREATE TABLE [ChatAttachments] (
        [Id] uniqueidentifier NOT NULL,
        [UploadedByUserId] int NOT NULL,
        [ConversationId] int NULL,
        [FileName] nvarchar(250) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ChatAttachments] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907081538_AddPrivateChatAttachments'
)
BEGIN
    CREATE INDEX [IX_ChatAttachments_ConversationId] ON [ChatAttachments] ([ConversationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907081538_AddPrivateChatAttachments'
)
BEGIN
    CREATE INDEX [IX_ChatAttachments_UploadedByUserId] ON [ChatAttachments] ([UploadedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907081538_AddPrivateChatAttachments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260907081538_AddPrivateChatAttachments', N'8.0.1');
END;
GO

COMMIT;
GO

