-- ============================================================================
-- امکانات امنیتی آرشیو اسناد — نسخه امنیت فایل‌ها (SQL Server)
--   ۱) جدول لاگ مشاهده/دانلود پیوست‌ها (AppAttachmentAccessLogs)
--   ۲) ستون «محرمانه» روی مدارک (RequireDownloadConfirm) — تایید مجدد رمز برای دانلود/مشاهده فایل
--   ۳) ستون RoleId روی دسترسی‌های پوشه/مدرک (دسترسی گروهی/نقش‌محور RBAC)
--      و بازسازی ایندکس یکتا به‌شکل سه‌ستونی (…, UserId, RoleId)
-- این اسکریپت idempotent است و در استارتاپ برنامه (Data/DocArchiveSecuritySchemaV1)
-- به‌صورت خودکار اجرا می‌شود؛ این نسخه صرفاً برای مرور و اجرای دستی نگه‌داری می‌شود.
-- ============================================================================
BEGIN TRANSACTION;
GO

IF COL_LENGTH('dbo.Documents', 'RequireDownloadConfirm') IS NULL
    ALTER TABLE [Documents] ADD [RequireDownloadConfirm] bit NOT NULL CONSTRAINT [DF_Documents_RequireDownloadConfirm] DEFAULT(0);
GO

IF COL_LENGTH('dbo.DocFolderPermissions', 'RoleId') IS NULL
    ALTER TABLE [DocFolderPermissions] ADD [RoleId] int NOT NULL CONSTRAINT [DF_DocFolderPermissions_RoleId] DEFAULT(0);
GO

IF COL_LENGTH('dbo.DocumentPermissions', 'RoleId') IS NULL
    ALTER TABLE [DocumentPermissions] ADD [RoleId] int NOT NULL CONSTRAINT [DF_DocumentPermissions_RoleId] DEFAULT(0);
GO

IF OBJECT_ID(N'dbo.AppAttachmentAccessLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [AppAttachmentAccessLogs] (
        [Id] int NOT NULL IDENTITY,
        [AttachmentId] int NOT NULL,
        [Module] nvarchar(50) NOT NULL,
        [RefId] int NOT NULL,
        [FileName] nvarchar(150) NOT NULL,
        [Action] nvarchar(20) NOT NULL,
        [UserId] int NOT NULL,
        [UserName] nvarchar(150) NOT NULL,
        [Ip] nvarchar(60) NULL,
        [At] datetime2 NOT NULL,
        CONSTRAINT [PK_AppAttachmentAccessLogs] PRIMARY KEY ([Id])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AppAttachmentAccessLogs_Module_RefId' AND object_id = OBJECT_ID(N'dbo.AppAttachmentAccessLogs'))
    CREATE INDEX [IX_AppAttachmentAccessLogs_Module_RefId] ON [AppAttachmentAccessLogs] ([Module], [RefId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AppAttachmentAccessLogs_AttachmentId' AND object_id = OBJECT_ID(N'dbo.AppAttachmentAccessLogs'))
    CREATE INDEX [IX_AppAttachmentAccessLogs_AttachmentId] ON [AppAttachmentAccessLogs] ([AttachmentId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AppAttachmentAccessLogs_At' AND object_id = OBJECT_ID(N'dbo.AppAttachmentAccessLogs'))
    CREATE INDEX [IX_AppAttachmentAccessLogs_At] ON [AppAttachmentAccessLogs] ([At]);
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocFolderPermissions_FolderId_UserId' AND object_id = OBJECT_ID(N'dbo.DocFolderPermissions'))
    DROP INDEX [IX_DocFolderPermissions_FolderId_UserId] ON [DocFolderPermissions];
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocFolderPermissions_FolderId_UserId_RoleId' AND object_id = OBJECT_ID(N'dbo.DocFolderPermissions'))
    CREATE UNIQUE INDEX [IX_DocFolderPermissions_FolderId_UserId_RoleId] ON [DocFolderPermissions] ([FolderId], [UserId], [RoleId]);
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentPermissions_DocumentId_UserId' AND object_id = OBJECT_ID(N'dbo.DocumentPermissions'))
    DROP INDEX [IX_DocumentPermissions_DocumentId_UserId] ON [DocumentPermissions];
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentPermissions_DocumentId_UserId_RoleId' AND object_id = OBJECT_ID(N'dbo.DocumentPermissions'))
    CREATE UNIQUE INDEX [IX_DocumentPermissions_DocumentId_UserId_RoleId] ON [DocumentPermissions] ([DocumentId], [UserId], [RoleId]);
GO

COMMIT;
GO
