using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <summary>
    /// ماژول انبارگردانی و بارکد.
    ///
    /// این مهاجرت به‌جای دستورهای تولیدشده‌ی EF، T-SQL محافظت‌شده اجرا می‌کند تا
    /// روی پایگاه‌داده‌ی واقعی — که ممکن است بخشی از این اشیا را از قبل داشته باشد —
    /// بدون خطا و به‌صورت قابل اجرای مکرر عمل کند.
    /// </summary>
    public partial class AddStocktakingModule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF OBJECT_ID(N'[BcdBarcodes]', N'U') IS NULL
BEGIN
    CREATE TABLE [BcdBarcodes] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [Code] nvarchar(60) NOT NULL,
        [Type] int NOT NULL,
        [Unit] nvarchar(50) NULL,
        [PackQty] decimal(18,3) NOT NULL,
        [IsPrimary] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(300) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_BcdBarcodes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BcdBarcodes_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'[StkSessions]', N'U') IS NULL
BEGIN
    CREATE TABLE [StkSessions] (
        [Id] int NOT NULL IDENTITY,
        [Number] int NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [WarehouseId] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [Scope] int NOT NULL,
        [CategoryId] int NULL,
        [Status] int NOT NULL,
        [TreatUncountedAsZero] bit NOT NULL,
        [Description] nvarchar(600) NULL,
        [SurplusDocId] int NULL,
        [ShortageDocId] int NULL,
        [CreatedBy] nvarchar(120) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [AppliedBy] nvarchar(120) NULL,
        [AppliedAt] datetime2 NULL,
        CONSTRAINT [PK_StkSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StkSessions_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;

IF OBJECT_ID(N'[StkLines]', N'U') IS NULL
BEGIN
    CREATE TABLE [StkLines] (
        [Id] int NOT NULL IDENTITY,
        [SessionId] int NOT NULL,
        [RowNo] int NOT NULL,
        [ProductId] int NOT NULL,
        [SystemQty] decimal(18,3) NOT NULL,
        [CountedQty] decimal(18,3) NOT NULL,
        [IsCounted] bit NOT NULL,
        [UnitCost] decimal(18,2) NOT NULL,
        [Note] nvarchar(300) NULL,
        [CountedBy] nvarchar(120) NULL,
        [CountedAt] datetime2 NULL,
        [ScanCount] int NOT NULL,
        CONSTRAINT [PK_StkLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StkLines_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StkLines_StkSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [StkSessions] ([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'[StkScans]', N'U') IS NULL
BEGIN
    CREATE TABLE [StkScans] (
        [Id] bigint NOT NULL IDENTITY,
        [SessionId] int NOT NULL,
        [LineId] int NOT NULL,
        [ProductId] int NOT NULL,
        [Barcode] nvarchar(60) NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [Accumulated] bit NOT NULL,
        [ScannedBy] nvarchar(120) NULL,
        [ScannedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StkScans] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StkScans_StkSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [StkSessions] ([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'[BcdBarcodes]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_BcdBarcodes_Code' AND [object_id] = OBJECT_ID(N'[BcdBarcodes]'))
    CREATE UNIQUE INDEX [IX_BcdBarcodes_Code] ON [BcdBarcodes] ([Code]);

IF OBJECT_ID(N'[BcdBarcodes]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_BcdBarcodes_ProductId' AND [object_id] = OBJECT_ID(N'[BcdBarcodes]'))
    CREATE INDEX [IX_BcdBarcodes_ProductId] ON [BcdBarcodes] ([ProductId]);

IF OBJECT_ID(N'[StkLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkLines_ProductId' AND [object_id] = OBJECT_ID(N'[StkLines]'))
    CREATE INDEX [IX_StkLines_ProductId] ON [StkLines] ([ProductId]);

IF OBJECT_ID(N'[StkLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkLines_SessionId' AND [object_id] = OBJECT_ID(N'[StkLines]'))
    CREATE INDEX [IX_StkLines_SessionId] ON [StkLines] ([SessionId]);

IF OBJECT_ID(N'[StkLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkLines_SessionId_ProductId' AND [object_id] = OBJECT_ID(N'[StkLines]'))
    CREATE UNIQUE INDEX [IX_StkLines_SessionId_ProductId] ON [StkLines] ([SessionId], [ProductId]);

IF OBJECT_ID(N'[StkScans]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkScans_LineId' AND [object_id] = OBJECT_ID(N'[StkScans]'))
    CREATE INDEX [IX_StkScans_LineId] ON [StkScans] ([LineId]);

IF OBJECT_ID(N'[StkScans]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkScans_SessionId' AND [object_id] = OBJECT_ID(N'[StkScans]'))
    CREATE INDEX [IX_StkScans_SessionId] ON [StkScans] ([SessionId]);

IF OBJECT_ID(N'[StkSessions]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkSessions_Date' AND [object_id] = OBJECT_ID(N'[StkSessions]'))
    CREATE INDEX [IX_StkSessions_Date] ON [StkSessions] ([Date]);

IF OBJECT_ID(N'[StkSessions]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkSessions_Number' AND [object_id] = OBJECT_ID(N'[StkSessions]'))
    CREATE UNIQUE INDEX [IX_StkSessions_Number] ON [StkSessions] ([Number]);

IF OBJECT_ID(N'[StkSessions]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkSessions_Status' AND [object_id] = OBJECT_ID(N'[StkSessions]'))
    CREATE INDEX [IX_StkSessions_Status] ON [StkSessions] ([Status]);

IF OBJECT_ID(N'[StkSessions]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkSessions_WarehouseId' AND [object_id] = OBJECT_ID(N'[StkSessions]'))
    CREATE INDEX [IX_StkSessions_WarehouseId] ON [StkSessions] ([WarehouseId]);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[StkScans]', N'U') IS NOT NULL DROP TABLE [StkScans];
IF OBJECT_ID(N'[StkLines]', N'U') IS NOT NULL DROP TABLE [StkLines];
IF OBJECT_ID(N'[StkSessions]', N'U') IS NOT NULL DROP TABLE [StkSessions];
IF OBJECT_ID(N'[BcdBarcodes]', N'U') IS NOT NULL DROP TABLE [BcdBarcodes];
");
        }
    }
}
