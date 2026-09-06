using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <summary>
    /// ماژول انبارداری — نسخه‌ی «امن در برابر ناهماهنگی اسکیما».
    ///
    /// چرا به‌جای عملیات معمولِ EF از SQL خام استفاده شده؟
    /// اسنپ‌شات مدل مخزن با دیتابیس‌های واقعی همگام نبود؛ به همین دلیل EF عملیاتی
    /// (مثل DROP INDEX روی PushSubscriptions) تولید کرده بود که روی دیتابیس مقصد وجود نداشت
    /// و مایگریشن با خطای «Cannot drop the index … because it does not exist» متوقف می‌شد.
    ///
    /// اینجا هر دستور با یک شرط وجود/عدم وجود محافظت شده است؛ بنابراین:
    ///   • اگر شیء از قبل باشد، آن دستور بی‌اثر رد می‌شود (بدون خطا).
    ///   • اگر نباشد، ساخته می‌شود.
    ///   • مایگریشن چندبار قابل اجراست (idempotent).
    ///
    /// توجه: این مایگریشن مخصوص SQL Server است. مسیر Sqlite در DbInitializer
    /// از EnsureCreated استفاده می‌کند و اصلاً مایگریشن اجرا نمی‌شود.
    /// نسخه‌ی اصلی و تولیدشده توسط EF در sql/warehousing/AddWarehousingModule.original.cs.txt نگهداری می‌شود.
    /// </summary>
    public partial class AddWarehousingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_PushSubscriptions_UserId' AND [object_id] = OBJECT_ID(N'[PushSubscriptions]'))
BEGIN
    DROP INDEX [IX_PushSubscriptions_UserId] ON [PushSubscriptions];
END;");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_PushSubscriptions_UserId_Endpoint' AND [object_id] = OBJECT_ID(N'[PushSubscriptions]'))
BEGIN
    DROP INDEX [IX_PushSubscriptions_UserId_Endpoint] ON [PushSubscriptions];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'AllowNegative') IS NULL
BEGIN
    ALTER TABLE [Warehouses] ADD [AllowNegative] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'Code') IS NULL
BEGIN
    ALTER TABLE [Warehouses] ADD [Code] nvarchar(30) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'IsDefault') IS NULL
BEGIN
    ALTER TABLE [Warehouses] ADD [IsDefault] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'KeeperName') IS NULL
BEGIN
    ALTER TABLE [Warehouses] ADD [KeeperName] nvarchar(120) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'Kind') IS NULL
BEGIN
    ALTER TABLE [Warehouses] ADD [Kind] int NOT NULL DEFAULT 0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ShiftGroups]', N'EndTime2') IS NULL
BEGIN
    ALTER TABLE [ShiftGroups] ADD [EndTime2] time NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ShiftGroups]', N'StartTime2') IS NULL
BEGIN
    ALTER TABLE [ShiftGroups] ADD [StartTime2] time NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Brand') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Brand] nvarchar(100) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'CategoryId') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [CategoryId] int NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'CountryOfOrigin') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [CountryOfOrigin] nvarchar(80) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'CustomsCode') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [CustomsCode] nvarchar(30) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'DutyRate') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [DutyRate] decimal(6,2) NOT NULL DEFAULT 0.0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'EnName') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [EnName] nvarchar(200) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Height') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Height] decimal(18,3) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'ImageUrl') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [ImageUrl] nvarchar(300) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'IsVatIncluded') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [IsVatIncluded] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Length') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Length] decimal(18,3) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'MinOrderQty') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [MinOrderQty] decimal(18,3) NOT NULL DEFAULT 0.0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Model') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Model] nvarchar(100) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Note') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Note] nvarchar(max) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'OtherTaxRate') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [OtherTaxRate] decimal(6,2) NOT NULL DEFAULT 0.0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'PartNumber') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [PartNumber] nvarchar(100) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'SalePrice2') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [SalePrice2] decimal(18,2) NOT NULL DEFAULT 0.0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'SecondUnit') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [SecondUnit] nvarchar(50) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'ShelfCode') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [ShelfCode] nvarchar(50) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'ShelfLifeDays') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [ShelfLifeDays] int NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TaxCode') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [TaxCode] nvarchar(20) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TaxUnitCode') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [TaxUnitCode] nvarchar(20) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TrackBatch') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [TrackBatch] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TrackExpiry') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [TrackExpiry] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TrackSerial') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [TrackSerial] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'UnitFactor') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [UnitFactor] decimal(18,4) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Valuation') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Valuation] int NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'VatRate') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [VatRate] decimal(6,2) NOT NULL DEFAULT 0.0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Weight') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Weight] decimal(18,3) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Width') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Width] decimal(18,3) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ProductCategories]', N'Code') IS NULL
BEGIN
    ALTER TABLE [ProductCategories] ADD [Code] nvarchar(30) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ProductCategories]', N'SortOrder') IS NULL
BEGIN
    ALTER TABLE [ProductCategories] ADD [SortOrder] int NOT NULL DEFAULT 0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ProductCategories]', N'Valuation') IS NULL
BEGIN
    ALTER TABLE [ProductCategories] ADD [Valuation] int NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[OutgoingLetterSigners]', N'OutgoingLetterId') IS NULL
BEGIN
    ALTER TABLE [OutgoingLetterSigners] ADD [OutgoingLetterId] int NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[CompanyHolidays]', N'IsOfficial') IS NULL
BEGIN
    ALTER TABLE [CompanyHolidays] ADD [IsOfficial] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[AttendanceSegments]', N'EnterDevice') IS NULL
BEGIN
    ALTER TABLE [AttendanceSegments] ADD [EnterDevice] nvarchar(250) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[AttendanceSegments]', N'IsUnauthorized') IS NULL
BEGIN
    ALTER TABLE [AttendanceSegments] ADD [IsUnauthorized] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[AttendanceSegments]', N'OvertimeMinutes') IS NULL
BEGIN
    ALTER TABLE [AttendanceSegments] ADD [OvertimeMinutes] int NOT NULL DEFAULT 0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[AttendanceRecords]', N'OvertimeMinutes') IS NULL
BEGIN
    ALTER TABLE [AttendanceRecords] ADD [OvertimeMinutes] int NOT NULL DEFAULT 0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[AttendanceRecords]', N'UnauthorizedMinutes') IS NULL
BEGIN
    ALTER TABLE [AttendanceRecords] ADD [UnauthorizedMinutes] int NOT NULL DEFAULT 0;
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[AuditLogs]', N'U') IS NULL
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [At] datetime2 NOT NULL,
        [UserId] int NULL,
        [Username] nvarchar(100) NULL,
        [Module] nvarchar(80) NOT NULL,
        [Action] nvarchar(80) NOT NULL,
        [HttpMethod] nvarchar(10) NOT NULL,
        [Path] nvarchar(300) NULL,
        [Summary] nvarchar(200) NULL,
        [Payload] nvarchar(4000) NULL,
        [Ip] nvarchar(64) NULL,
        [Device] nvarchar(250) NULL,
        [StatusCode] int NOT NULL,
        [DurationMs] int NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocs]', N'U') IS NULL
BEGIN
    CREATE TABLE [InvDocs] (
        [Id] int NOT NULL IDENTITY,
        [Number] nvarchar(30) NOT NULL,
        [DocTypeId] int NOT NULL,
        [WarehouseId] int NOT NULL,
        [CounterWarehouseId] int NULL,
        [PartyId] int NULL,
        [Date] datetime2 NOT NULL,
        [RefNumber] nvarchar(50) NULL,
        [Description] nvarchar(max) NULL,
        [Status] int NOT NULL,
        [TotalQuantity] decimal(18,3) NOT NULL,
        [TotalValue] decimal(18,2) NOT NULL,
        [CreatedBy] nvarchar(80) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ConfirmedBy] nvarchar(80) NULL,
        [ConfirmedAt] datetime2 NULL,
        CONSTRAINT [PK_InvDocs] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocTypes]', N'U') IS NULL
BEGIN
    CREATE TABLE [InvDocTypes] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Nature] int NOT NULL,
        [IsTransfer] bit NOT NULL,
        [RequiresParty] bit NOT NULL,
        [RequiresPrice] bit NOT NULL,
        [NumberPrefix] nvarchar(10) NULL,
        [Color] nvarchar(30) NULL,
        [Icon] nvarchar(50) NULL,
        [IsSystem] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [Description] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_InvDocTypes] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvLedger]', N'U') IS NULL
BEGIN
    CREATE TABLE [InvLedger] (
        [Id] bigint NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [WarehouseId] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [DocId] int NOT NULL,
        [DocLineId] int NOT NULL,
        [DocTypeId] int NOT NULL,
        [Nature] int NOT NULL,
        [Number] nvarchar(30) NOT NULL,
        [Description] nvarchar(300) NULL,
        [QtyIn] decimal(18,3) NOT NULL,
        [QtyOut] decimal(18,3) NOT NULL,
        [UnitCost] decimal(18,4) NOT NULL,
        [ValueIn] decimal(18,2) NOT NULL,
        [ValueOut] decimal(18,2) NOT NULL,
        [BalanceQty] decimal(18,3) NOT NULL,
        [BalanceValue] decimal(18,2) NOT NULL,
        [RemainingQty] decimal(18,3) NOT NULL,
        [Seq] int NOT NULL,
        CONSTRAINT [PK_InvLedger] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvStocks]', N'U') IS NULL
BEGIN
    CREATE TABLE [InvStocks] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [WarehouseId] int NOT NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [Value] decimal(18,2) NOT NULL,
        [AvgCost] decimal(18,4) NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_InvStocks] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductAttributeDefs]', N'U') IS NULL
BEGIN
    CREATE TABLE [ProductAttributeDefs] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(120) NOT NULL,
        [Code] nvarchar(50) NULL,
        [ValueType] int NOT NULL,
        [Unit] nvarchar(50) NULL,
        [CategoryId] int NULL,
        [IsRequired] bit NOT NULL,
        [ShowInList] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ProductAttributeDefs] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductAttributeOptions]', N'U') IS NULL
BEGIN
    CREATE TABLE [ProductAttributeOptions] (
        [Id] int NOT NULL IDENTITY,
        [AttributeId] int NOT NULL,
        [Title] nvarchar(150) NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_ProductAttributeOptions] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductAttributeValues]', N'U') IS NULL
BEGIN
    CREATE TABLE [ProductAttributeValues] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [AttributeId] int NOT NULL,
        [OptionId] int NULL,
        [TextValue] nvarchar(500) NULL,
        [NumberValue] decimal(18,4) NULL,
        [BoolValue] bit NULL,
        [DateValue] datetime2 NULL,
        CONSTRAINT [PK_ProductAttributeValues] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[WorkCalendarDays]', N'U') IS NULL
BEGIN
    CREATE TABLE [WorkCalendarDays] (
        [Id] int NOT NULL IDENTITY,
        [Date] datetime2 NOT NULL,
        [IsWorkday] bit NOT NULL,
        [StartTime] time NULL,
        [EndTime] time NULL,
        [GraceMinutes] int NOT NULL,
        [OvertimeHours] float NOT NULL,
        [OvertimeMode] int NOT NULL,
        [OvertimeStart] time NULL,
        [OvertimeEnd] time NULL,
        [Note] nvarchar(100) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_WorkCalendarDays] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[WorkCalendarSettings]', N'U') IS NULL
BEGIN
    CREATE TABLE [WorkCalendarSettings] (
        [Id] int NOT NULL IDENTITY,
        [DefaultStart] time NOT NULL,
        [DefaultEnd] time NOT NULL,
        [GraceMinutes] int NOT NULL,
        [RestDayFlags] int NOT NULL,
        [ApplyOfficialHolidays] bit NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkCalendarSettings] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocLines]', N'U') IS NULL
BEGIN
    CREATE TABLE [InvDocLines] (
        [Id] int NOT NULL IDENTITY,
        [DocId] int NOT NULL,
        [RowNo] int NOT NULL,
        [ProductId] int NOT NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [Discount] decimal(18,2) NOT NULL,
        [BatchNo] nvarchar(50) NULL,
        [SerialNo] nvarchar(80) NULL,
        [ExpiryDate] datetime2 NULL,
        [Description] nvarchar(300) NULL,
        [OutCost] decimal(18,4) NULL,
        CONSTRAINT [PK_InvDocLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InvDocLines_InvDocs_DocId] FOREIGN KEY ([DocId]) REFERENCES [InvDocs] ([Id]) ON DELETE CASCADE
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[Warehouses]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Warehouses_Code' AND [object_id] = OBJECT_ID(N'[Warehouses]'))
BEGIN
    CREATE INDEX [IX_Warehouses_Code] ON [Warehouses] ([Code]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[Products]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Products_CategoryId' AND [object_id] = OBJECT_ID(N'[Products]'))
BEGIN
    CREATE INDEX [IX_Products_CategoryId] ON [Products] ([CategoryId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[Products]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Products_TaxCode' AND [object_id] = OBJECT_ID(N'[Products]'))
BEGIN
    CREATE INDEX [IX_Products_TaxCode] ON [Products] ([TaxCode]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductCategories]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductCategories_ParentId' AND [object_id] = OBJECT_ID(N'[ProductCategories]'))
BEGIN
    CREATE INDEX [IX_ProductCategories_ParentId] ON [ProductCategories] ([ParentId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[OutgoingLetterSigners]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_OutgoingLetterSigners_OutgoingLetterId' AND [object_id] = OBJECT_ID(N'[OutgoingLetterSigners]'))
BEGIN
    CREATE INDEX [IX_OutgoingLetterSigners_OutgoingLetterId] ON [OutgoingLetterSigners] ([OutgoingLetterId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[AuditLogs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AuditLogs_At' AND [object_id] = OBJECT_ID(N'[AuditLogs]'))
BEGIN
    CREATE INDEX [IX_AuditLogs_At] ON [AuditLogs] ([At]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[AuditLogs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AuditLogs_UserId' AND [object_id] = OBJECT_ID(N'[AuditLogs]'))
BEGIN
    CREATE INDEX [IX_AuditLogs_UserId] ON [AuditLogs] ([UserId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocLines_DocId' AND [object_id] = OBJECT_ID(N'[InvDocLines]'))
BEGIN
    CREATE INDEX [IX_InvDocLines_DocId] ON [InvDocLines] ([DocId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocLines_ProductId' AND [object_id] = OBJECT_ID(N'[InvDocLines]'))
BEGIN
    CREATE INDEX [IX_InvDocLines_ProductId] ON [InvDocLines] ([ProductId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_Date' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
BEGIN
    CREATE INDEX [IX_InvDocs_Date] ON [InvDocs] ([Date]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_DocTypeId' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
BEGIN
    CREATE INDEX [IX_InvDocs_DocTypeId] ON [InvDocs] ([DocTypeId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_Number' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
BEGIN
    CREATE UNIQUE INDEX [IX_InvDocs_Number] ON [InvDocs] ([Number]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_Status' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
BEGIN
    CREATE INDEX [IX_InvDocs_Status] ON [InvDocs] ([Status]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_WarehouseId' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
BEGIN
    CREATE INDEX [IX_InvDocs_WarehouseId] ON [InvDocs] ([WarehouseId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocTypes]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocTypes_Code' AND [object_id] = OBJECT_ID(N'[InvDocTypes]'))
BEGIN
    CREATE UNIQUE INDEX [IX_InvDocTypes_Code] ON [InvDocTypes] ([Code]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocTypes]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocTypes_Nature' AND [object_id] = OBJECT_ID(N'[InvDocTypes]'))
BEGIN
    CREATE INDEX [IX_InvDocTypes_Nature] ON [InvDocTypes] ([Nature]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvLedger]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvLedger_DocId' AND [object_id] = OBJECT_ID(N'[InvLedger]'))
BEGIN
    CREATE INDEX [IX_InvLedger_DocId] ON [InvLedger] ([DocId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvLedger]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvLedger_ProductId_WarehouseId_Date' AND [object_id] = OBJECT_ID(N'[InvLedger]'))
BEGIN
    CREATE INDEX [IX_InvLedger_ProductId_WarehouseId_Date] ON [InvLedger] ([ProductId], [WarehouseId], [Date]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvStocks]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvStocks_ProductId_WarehouseId' AND [object_id] = OBJECT_ID(N'[InvStocks]'))
BEGIN
    CREATE UNIQUE INDEX [IX_InvStocks_ProductId_WarehouseId] ON [InvStocks] ([ProductId], [WarehouseId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductAttributeDefs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductAttributeDefs_CategoryId' AND [object_id] = OBJECT_ID(N'[ProductAttributeDefs]'))
BEGIN
    CREATE INDEX [IX_ProductAttributeDefs_CategoryId] ON [ProductAttributeDefs] ([CategoryId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductAttributeDefs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductAttributeDefs_Name' AND [object_id] = OBJECT_ID(N'[ProductAttributeDefs]'))
BEGIN
    CREATE INDEX [IX_ProductAttributeDefs_Name] ON [ProductAttributeDefs] ([Name]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductAttributeOptions]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductAttributeOptions_AttributeId' AND [object_id] = OBJECT_ID(N'[ProductAttributeOptions]'))
BEGIN
    CREATE INDEX [IX_ProductAttributeOptions_AttributeId] ON [ProductAttributeOptions] ([AttributeId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductAttributeValues]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductAttributeValues_ProductId_AttributeId' AND [object_id] = OBJECT_ID(N'[ProductAttributeValues]'))
BEGIN
    CREATE UNIQUE INDEX [IX_ProductAttributeValues_ProductId_AttributeId] ON [ProductAttributeValues] ([ProductId], [AttributeId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[WorkCalendarDays]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_WorkCalendarDays_Date' AND [object_id] = OBJECT_ID(N'[WorkCalendarDays]'))
BEGIN
    CREATE UNIQUE INDEX [IX_WorkCalendarDays_Date] ON [WorkCalendarDays] ([Date]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[WorkCalendarSettings]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_WorkCalendarSettings_Id' AND [object_id] = OBJECT_ID(N'[WorkCalendarSettings]'))
BEGIN
    CREATE UNIQUE INDEX [IX_WorkCalendarSettings_Id] ON [WorkCalendarSettings] ([Id]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[FK_OutgoingLetterSigners_OutgoingLetters_OutgoingLetterId]', N'F') IS NULL
BEGIN
    ALTER TABLE [OutgoingLetterSigners] ADD CONSTRAINT [FK_OutgoingLetterSigners_OutgoingLetters_OutgoingLetterId] FOREIGN KEY ([OutgoingLetterId]) REFERENCES [OutgoingLetters] ([Id]);
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocLines]', N'U') IS NOT NULL DROP TABLE [InvDocLines];");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocTypes]', N'U') IS NOT NULL DROP TABLE [InvDocTypes];");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvLedger]', N'U') IS NOT NULL DROP TABLE [InvLedger];");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvStocks]', N'U') IS NOT NULL DROP TABLE [InvStocks];");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL DROP TABLE [InvDocs];");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Warehouses_Code' AND [object_id] = OBJECT_ID(N'[Warehouses]'))
    DROP INDEX [IX_Warehouses_Code] ON [Warehouses];");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Products_CategoryId' AND [object_id] = OBJECT_ID(N'[Products]'))
    DROP INDEX [IX_Products_CategoryId] ON [Products];");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Products_TaxCode' AND [object_id] = OBJECT_ID(N'[Products]'))
    DROP INDEX [IX_Products_TaxCode] ON [Products];");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductCategories_ParentId' AND [object_id] = OBJECT_ID(N'[ProductCategories]'))
    DROP INDEX [IX_ProductCategories_ParentId] ON [ProductCategories];");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'AllowNegative') IS NOT NULL
BEGIN
    DECLARE @df_Warehouses_AllowNegative sysname;
    SELECT @df_Warehouses_AllowNegative = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Warehouses]') AND c.[name] = N'AllowNegative';
    IF @df_Warehouses_AllowNegative IS NOT NULL EXEC(N'ALTER TABLE [Warehouses] DROP CONSTRAINT [' + @df_Warehouses_AllowNegative + N']');
    ALTER TABLE [Warehouses] DROP COLUMN [AllowNegative];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'Code') IS NOT NULL
BEGIN
    DECLARE @df_Warehouses_Code sysname;
    SELECT @df_Warehouses_Code = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Warehouses]') AND c.[name] = N'Code';
    IF @df_Warehouses_Code IS NOT NULL EXEC(N'ALTER TABLE [Warehouses] DROP CONSTRAINT [' + @df_Warehouses_Code + N']');
    ALTER TABLE [Warehouses] DROP COLUMN [Code];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'IsDefault') IS NOT NULL
BEGIN
    DECLARE @df_Warehouses_IsDefault sysname;
    SELECT @df_Warehouses_IsDefault = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Warehouses]') AND c.[name] = N'IsDefault';
    IF @df_Warehouses_IsDefault IS NOT NULL EXEC(N'ALTER TABLE [Warehouses] DROP CONSTRAINT [' + @df_Warehouses_IsDefault + N']');
    ALTER TABLE [Warehouses] DROP COLUMN [IsDefault];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'KeeperName') IS NOT NULL
BEGIN
    DECLARE @df_Warehouses_KeeperName sysname;
    SELECT @df_Warehouses_KeeperName = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Warehouses]') AND c.[name] = N'KeeperName';
    IF @df_Warehouses_KeeperName IS NOT NULL EXEC(N'ALTER TABLE [Warehouses] DROP CONSTRAINT [' + @df_Warehouses_KeeperName + N']');
    ALTER TABLE [Warehouses] DROP COLUMN [KeeperName];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'Kind') IS NOT NULL
BEGIN
    DECLARE @df_Warehouses_Kind sysname;
    SELECT @df_Warehouses_Kind = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Warehouses]') AND c.[name] = N'Kind';
    IF @df_Warehouses_Kind IS NOT NULL EXEC(N'ALTER TABLE [Warehouses] DROP CONSTRAINT [' + @df_Warehouses_Kind + N']');
    ALTER TABLE [Warehouses] DROP COLUMN [Kind];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Brand') IS NOT NULL
BEGIN
    DECLARE @df_Products_Brand sysname;
    SELECT @df_Products_Brand = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Brand';
    IF @df_Products_Brand IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Brand + N']');
    ALTER TABLE [Products] DROP COLUMN [Brand];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'CategoryId') IS NOT NULL
BEGIN
    DECLARE @df_Products_CategoryId sysname;
    SELECT @df_Products_CategoryId = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'CategoryId';
    IF @df_Products_CategoryId IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_CategoryId + N']');
    ALTER TABLE [Products] DROP COLUMN [CategoryId];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'CountryOfOrigin') IS NOT NULL
BEGIN
    DECLARE @df_Products_CountryOfOrigin sysname;
    SELECT @df_Products_CountryOfOrigin = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'CountryOfOrigin';
    IF @df_Products_CountryOfOrigin IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_CountryOfOrigin + N']');
    ALTER TABLE [Products] DROP COLUMN [CountryOfOrigin];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'CustomsCode') IS NOT NULL
BEGIN
    DECLARE @df_Products_CustomsCode sysname;
    SELECT @df_Products_CustomsCode = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'CustomsCode';
    IF @df_Products_CustomsCode IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_CustomsCode + N']');
    ALTER TABLE [Products] DROP COLUMN [CustomsCode];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'DutyRate') IS NOT NULL
BEGIN
    DECLARE @df_Products_DutyRate sysname;
    SELECT @df_Products_DutyRate = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'DutyRate';
    IF @df_Products_DutyRate IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_DutyRate + N']');
    ALTER TABLE [Products] DROP COLUMN [DutyRate];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'EnName') IS NOT NULL
BEGIN
    DECLARE @df_Products_EnName sysname;
    SELECT @df_Products_EnName = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'EnName';
    IF @df_Products_EnName IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_EnName + N']');
    ALTER TABLE [Products] DROP COLUMN [EnName];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Height') IS NOT NULL
BEGIN
    DECLARE @df_Products_Height sysname;
    SELECT @df_Products_Height = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Height';
    IF @df_Products_Height IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Height + N']');
    ALTER TABLE [Products] DROP COLUMN [Height];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'ImageUrl') IS NOT NULL
BEGIN
    DECLARE @df_Products_ImageUrl sysname;
    SELECT @df_Products_ImageUrl = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'ImageUrl';
    IF @df_Products_ImageUrl IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_ImageUrl + N']');
    ALTER TABLE [Products] DROP COLUMN [ImageUrl];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'IsVatIncluded') IS NOT NULL
BEGIN
    DECLARE @df_Products_IsVatIncluded sysname;
    SELECT @df_Products_IsVatIncluded = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'IsVatIncluded';
    IF @df_Products_IsVatIncluded IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_IsVatIncluded + N']');
    ALTER TABLE [Products] DROP COLUMN [IsVatIncluded];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Length') IS NOT NULL
BEGIN
    DECLARE @df_Products_Length sysname;
    SELECT @df_Products_Length = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Length';
    IF @df_Products_Length IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Length + N']');
    ALTER TABLE [Products] DROP COLUMN [Length];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'MinOrderQty') IS NOT NULL
BEGIN
    DECLARE @df_Products_MinOrderQty sysname;
    SELECT @df_Products_MinOrderQty = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'MinOrderQty';
    IF @df_Products_MinOrderQty IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_MinOrderQty + N']');
    ALTER TABLE [Products] DROP COLUMN [MinOrderQty];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Model') IS NOT NULL
BEGIN
    DECLARE @df_Products_Model sysname;
    SELECT @df_Products_Model = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Model';
    IF @df_Products_Model IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Model + N']');
    ALTER TABLE [Products] DROP COLUMN [Model];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Note') IS NOT NULL
BEGIN
    DECLARE @df_Products_Note sysname;
    SELECT @df_Products_Note = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Note';
    IF @df_Products_Note IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Note + N']');
    ALTER TABLE [Products] DROP COLUMN [Note];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'OtherTaxRate') IS NOT NULL
BEGIN
    DECLARE @df_Products_OtherTaxRate sysname;
    SELECT @df_Products_OtherTaxRate = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'OtherTaxRate';
    IF @df_Products_OtherTaxRate IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_OtherTaxRate + N']');
    ALTER TABLE [Products] DROP COLUMN [OtherTaxRate];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'PartNumber') IS NOT NULL
BEGIN
    DECLARE @df_Products_PartNumber sysname;
    SELECT @df_Products_PartNumber = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'PartNumber';
    IF @df_Products_PartNumber IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_PartNumber + N']');
    ALTER TABLE [Products] DROP COLUMN [PartNumber];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'SalePrice2') IS NOT NULL
BEGIN
    DECLARE @df_Products_SalePrice2 sysname;
    SELECT @df_Products_SalePrice2 = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'SalePrice2';
    IF @df_Products_SalePrice2 IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_SalePrice2 + N']');
    ALTER TABLE [Products] DROP COLUMN [SalePrice2];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'SecondUnit') IS NOT NULL
BEGIN
    DECLARE @df_Products_SecondUnit sysname;
    SELECT @df_Products_SecondUnit = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'SecondUnit';
    IF @df_Products_SecondUnit IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_SecondUnit + N']');
    ALTER TABLE [Products] DROP COLUMN [SecondUnit];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'ShelfCode') IS NOT NULL
BEGIN
    DECLARE @df_Products_ShelfCode sysname;
    SELECT @df_Products_ShelfCode = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'ShelfCode';
    IF @df_Products_ShelfCode IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_ShelfCode + N']');
    ALTER TABLE [Products] DROP COLUMN [ShelfCode];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'ShelfLifeDays') IS NOT NULL
BEGIN
    DECLARE @df_Products_ShelfLifeDays sysname;
    SELECT @df_Products_ShelfLifeDays = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'ShelfLifeDays';
    IF @df_Products_ShelfLifeDays IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_ShelfLifeDays + N']');
    ALTER TABLE [Products] DROP COLUMN [ShelfLifeDays];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TaxCode') IS NOT NULL
BEGIN
    DECLARE @df_Products_TaxCode sysname;
    SELECT @df_Products_TaxCode = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'TaxCode';
    IF @df_Products_TaxCode IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_TaxCode + N']');
    ALTER TABLE [Products] DROP COLUMN [TaxCode];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TaxUnitCode') IS NOT NULL
BEGIN
    DECLARE @df_Products_TaxUnitCode sysname;
    SELECT @df_Products_TaxUnitCode = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'TaxUnitCode';
    IF @df_Products_TaxUnitCode IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_TaxUnitCode + N']');
    ALTER TABLE [Products] DROP COLUMN [TaxUnitCode];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TrackBatch') IS NOT NULL
BEGIN
    DECLARE @df_Products_TrackBatch sysname;
    SELECT @df_Products_TrackBatch = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'TrackBatch';
    IF @df_Products_TrackBatch IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_TrackBatch + N']');
    ALTER TABLE [Products] DROP COLUMN [TrackBatch];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TrackExpiry') IS NOT NULL
BEGIN
    DECLARE @df_Products_TrackExpiry sysname;
    SELECT @df_Products_TrackExpiry = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'TrackExpiry';
    IF @df_Products_TrackExpiry IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_TrackExpiry + N']');
    ALTER TABLE [Products] DROP COLUMN [TrackExpiry];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TrackSerial') IS NOT NULL
BEGIN
    DECLARE @df_Products_TrackSerial sysname;
    SELECT @df_Products_TrackSerial = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'TrackSerial';
    IF @df_Products_TrackSerial IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_TrackSerial + N']');
    ALTER TABLE [Products] DROP COLUMN [TrackSerial];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'UnitFactor') IS NOT NULL
BEGIN
    DECLARE @df_Products_UnitFactor sysname;
    SELECT @df_Products_UnitFactor = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'UnitFactor';
    IF @df_Products_UnitFactor IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_UnitFactor + N']');
    ALTER TABLE [Products] DROP COLUMN [UnitFactor];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Valuation') IS NOT NULL
BEGIN
    DECLARE @df_Products_Valuation sysname;
    SELECT @df_Products_Valuation = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Valuation';
    IF @df_Products_Valuation IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Valuation + N']');
    ALTER TABLE [Products] DROP COLUMN [Valuation];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'VatRate') IS NOT NULL
BEGIN
    DECLARE @df_Products_VatRate sysname;
    SELECT @df_Products_VatRate = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'VatRate';
    IF @df_Products_VatRate IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_VatRate + N']');
    ALTER TABLE [Products] DROP COLUMN [VatRate];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Weight') IS NOT NULL
BEGIN
    DECLARE @df_Products_Weight sysname;
    SELECT @df_Products_Weight = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Weight';
    IF @df_Products_Weight IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Weight + N']');
    ALTER TABLE [Products] DROP COLUMN [Weight];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Width') IS NOT NULL
BEGIN
    DECLARE @df_Products_Width sysname;
    SELECT @df_Products_Width = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Width';
    IF @df_Products_Width IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Width + N']');
    ALTER TABLE [Products] DROP COLUMN [Width];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ProductCategories]', N'Code') IS NOT NULL
BEGIN
    DECLARE @df_ProductCategories_Code sysname;
    SELECT @df_ProductCategories_Code = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[ProductCategories]') AND c.[name] = N'Code';
    IF @df_ProductCategories_Code IS NOT NULL EXEC(N'ALTER TABLE [ProductCategories] DROP CONSTRAINT [' + @df_ProductCategories_Code + N']');
    ALTER TABLE [ProductCategories] DROP COLUMN [Code];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ProductCategories]', N'SortOrder') IS NOT NULL
BEGIN
    DECLARE @df_ProductCategories_SortOrder sysname;
    SELECT @df_ProductCategories_SortOrder = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[ProductCategories]') AND c.[name] = N'SortOrder';
    IF @df_ProductCategories_SortOrder IS NOT NULL EXEC(N'ALTER TABLE [ProductCategories] DROP CONSTRAINT [' + @df_ProductCategories_SortOrder + N']');
    ALTER TABLE [ProductCategories] DROP COLUMN [SortOrder];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ProductCategories]', N'Valuation') IS NOT NULL
BEGIN
    DECLARE @df_ProductCategories_Valuation sysname;
    SELECT @df_ProductCategories_Valuation = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[ProductCategories]') AND c.[name] = N'Valuation';
    IF @df_ProductCategories_Valuation IS NOT NULL EXEC(N'ALTER TABLE [ProductCategories] DROP CONSTRAINT [' + @df_ProductCategories_Valuation + N']');
    ALTER TABLE [ProductCategories] DROP COLUMN [Valuation];
END;");
        }
    }
}
