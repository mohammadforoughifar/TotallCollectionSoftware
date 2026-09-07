BEGIN TRANSACTION;
GO

CREATE TABLE [DocEntityLinks] (
    [Id] int NOT NULL IDENTITY,
    [DocumentId] int NOT NULL,
    [Module] nvarchar(60) NOT NULL,
    [EntityId] int NOT NULL,
    [EntityCode] nvarchar(100) NULL,
    [EntityTitle] nvarchar(250) NOT NULL,
    [Note] nvarchar(500) NULL,
    [CreatedByUserId] int NOT NULL,
    [CreatedByName] nvarchar(150) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_DocEntityLinks] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DocEntityLinks_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [DocExtractedTexts] (
    [Id] int NOT NULL IDENTITY,
    [DocumentId] int NOT NULL,
    [VersionId] int NOT NULL,
    [AttachmentId] int NOT NULL,
    [FileName] nvarchar(255) NOT NULL,
    [ContentType] nvarchar(100) NOT NULL,
    [SourceType] nvarchar(30) NOT NULL,
    [ExtractedText] nvarchar(max) NOT NULL,
    [NormalizedText] nvarchar(max) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [ErrorMessage] nvarchar(500) NULL,
    [CharacterCount] int NOT NULL,
    [IndexedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_DocExtractedTexts] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [DocTags] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(60) NOT NULL,
    [Color] nvarchar(30) NOT NULL,
    [Description] nvarchar(250) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_DocTags] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [DocumentTags] (
    [Id] int NOT NULL IDENTITY,
    [DocumentId] int NOT NULL,
    [TagId] int NOT NULL,
    CONSTRAINT [PK_DocumentTags] PRIMARY KEY ([Id])
);
GO

CREATE INDEX [IX_DocEntityLinks_DocumentId_Module_EntityId] ON [DocEntityLinks] ([DocumentId], [Module], [EntityId]);
GO

CREATE INDEX [IX_DocEntityLinks_Module_EntityId] ON [DocEntityLinks] ([Module], [EntityId]);
GO

CREATE INDEX [IX_DocExtractedTexts_AttachmentId] ON [DocExtractedTexts] ([AttachmentId]);
GO

CREATE INDEX [IX_DocExtractedTexts_DocumentId_AttachmentId] ON [DocExtractedTexts] ([DocumentId], [AttachmentId]);
GO

CREATE UNIQUE INDEX [IX_DocTags_Name] ON [DocTags] ([Name]);
GO

CREATE UNIQUE INDEX [IX_DocumentTags_DocumentId_TagId] ON [DocumentTags] ([DocumentId], [TagId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260906185126_AddDocArchiveOcrTagsAndErpLinks', N'8.0.1');
GO

COMMIT;
GO

