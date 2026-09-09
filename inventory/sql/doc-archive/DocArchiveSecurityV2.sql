-- ============================================================================
-- موج دوم امکانات امنیتی/مدیریتی آرشیو اسناد (SQL Server)
--   ۱) ستون «واترمارک پیش‌نمایش» روی Documents (per-document، توسط مدیر مدرک)
--   ۲) جدول درخواست دسترسی (DocAccessRequests)
--   ۳) جدول تنظیمات شماره‌گذار خودکار کد مدرک (DocCodeSettings — تک‌ردیف Id=1)
-- این اسکریپت idempotent است و در استارتاپ برنامه (Data/DocArchiveSecuritySchemaV2)
-- به‌صورت خودکار اجرا می‌شود؛ این نسخه صرفاً برای مرور و اجرای دستی نگه‌داری می‌شود.
-- ============================================================================
BEGIN TRANSACTION;
GO

IF COL_LENGTH('dbo.Documents', 'WatermarkPreview') IS NULL
    ALTER TABLE [Documents] ADD [WatermarkPreview] bit NOT NULL CONSTRAINT [DF_Documents_WatermarkPreview] DEFAULT(0);
GO

IF OBJECT_ID(N'dbo.DocAccessRequests', N'U') IS NULL
BEGIN
    CREATE TABLE [DocAccessRequests] (
        [Id] int NOT NULL IDENTITY,
        [DocumentId] int NOT NULL,
        [RequesterUserId] int NOT NULL,
        [RequesterName] nvarchar(150) NOT NULL,
        [RequestedLevel] int NOT NULL,
        [Note] nvarchar(500) NULL,
        [Status] int NOT NULL CONSTRAINT [DF_DocAccessRequests_Status] DEFAULT(0),
        [HandledByUserId] int NULL,
        [HandledByName] nvarchar(150) NULL,
        [HandlerNote] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [HandledAt] datetime2 NULL,
        CONSTRAINT [PK_DocAccessRequests] PRIMARY KEY ([Id])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocAccessRequests_DocumentId_Status' AND object_id = OBJECT_ID(N'dbo.DocAccessRequests'))
    CREATE INDEX [IX_DocAccessRequests_DocumentId_Status] ON [DocAccessRequests] ([DocumentId], [Status]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocAccessRequests_RequesterUserId_Status' AND object_id = OBJECT_ID(N'dbo.DocAccessRequests'))
    CREATE INDEX [IX_DocAccessRequests_RequesterUserId_Status] ON [DocAccessRequests] ([RequesterUserId], [Status]);
GO

IF OBJECT_ID(N'dbo.DocCodeSettings', N'U') IS NULL
BEGIN
    CREATE TABLE [DocCodeSettings] (
        [Id] int NOT NULL,
        [Enabled] bit NOT NULL CONSTRAINT [DF_DocCodeSettings_Enabled] DEFAULT(0),
        [Prefix] nvarchar(10) NOT NULL CONSTRAINT [DF_DocCodeSettings_Prefix] DEFAULT(N'DOC-'),
        [Padding] int NOT NULL CONSTRAINT [DF_DocCodeSettings_Padding] DEFAULT(5),
        [NextNumber] int NOT NULL CONSTRAINT [DF_DocCodeSettings_NextNumber] DEFAULT(1),
        [BackfillRanAt] datetime2 NULL,
        [BackfillRanByName] nvarchar(150) NULL,
        [BackfillAssignedCount] int NOT NULL CONSTRAINT [DF_DocCodeSettings_BackfillAssignedCount] DEFAULT(0),
        CONSTRAINT [PK_DocCodeSettings] PRIMARY KEY ([Id])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM [DocCodeSettings] WHERE [Id] = 1)
    INSERT INTO [DocCodeSettings] ([Id], [Enabled], [Prefix], [Padding], [NextNumber], [BackfillAssignedCount])
    VALUES (1, 0, N'DOC-', 5, 1, 0);
GO

COMMIT;
GO
