using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <summary>
    /// PWA آفلاین آرشیو + امضای موبایل + علاقه‌مندی
    /// - جدول DocFavorites (ستاره)
    /// - ستون‌های امضا روی DocumentApprovers
    /// </summary>
    public partial class DocArchivePwaOffline : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- ============ DocFavorites (اگر از قبل نیست) ============
IF OBJECT_ID(N'dbo.DocFavorites', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DocFavorites](
        [Id] int NOT NULL IDENTITY(1,1),
        [UserId] int NOT NULL,
        [DocumentId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_DocFavorites_CreatedAt] DEFAULT (GETDATE()),
        CONSTRAINT [PK_DocFavorites] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DocFavorites_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents] ([Id]) ON DELETE CASCADE
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DocFavorites_UserId_DocumentId' AND object_id=OBJECT_ID(N'dbo.DocFavorites'))
    CREATE UNIQUE INDEX [IX_DocFavorites_UserId_DocumentId] ON [dbo].[DocFavorites] ([UserId], [DocumentId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DocFavorites_UserId' AND object_id=OBJECT_ID(N'dbo.DocFavorites'))
    CREATE INDEX [IX_DocFavorites_UserId] ON [dbo].[DocFavorites] ([UserId]);

-- ============ DocumentApprovers: ستون‌های امضا ============
IF COL_LENGTH('dbo.DocumentApprovers','SignatureDataUrl') IS NULL
    ALTER TABLE [dbo].[DocumentApprovers] ADD [SignatureDataUrl] nvarchar(max) NULL;
IF COL_LENGTH('dbo.DocumentApprovers','BiometricVerified') IS NULL
    ALTER TABLE [dbo].[DocumentApprovers] ADD [BiometricVerified] bit NOT NULL CONSTRAINT [DF_DocumentApprovers_BiometricVerified] DEFAULT 0;
IF COL_LENGTH('dbo.DocumentApprovers','DeviceInfo') IS NULL
    ALTER TABLE [dbo].[DocumentApprovers] ADD [DeviceInfo] nvarchar(300) NULL;
");

            // SQLite fallback (برای تست با SQLite)
            migrationBuilder.Sql(@"
-- SQLite: اگر provider اس‌کیولایت بود، دستورات بالا نادیده گرفته می‌شوند؛ این‌ها برای SQLite است
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.DocumentApprovers','DeviceInfo') IS NOT NULL ALTER TABLE [dbo].[DocumentApprovers] DROP COLUMN [DeviceInfo];
IF COL_LENGTH('dbo.DocumentApprovers','BiometricVerified') IS NOT NULL ALTER TABLE [dbo].[DocumentApprovers] DROP COLUMN [BiometricVerified];
IF COL_LENGTH('dbo.DocumentApprovers','SignatureDataUrl') IS NOT NULL ALTER TABLE [dbo].[DocumentApprovers] DROP COLUMN [SignatureDataUrl];
IF OBJECT_ID(N'dbo.DocFavorites', N'U') IS NOT NULL DROP TABLE [dbo].[DocFavorites];
");
        }
    }
}
