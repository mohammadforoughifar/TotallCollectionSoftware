using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations;

/// <summary>
/// ماژول حسابداری: سال مالی، کدینگ حساب‌ها، اسناد حسابداری و قواعد سند خودکار انبار.
/// نکته: عملیات به‌صورت SQL محافظت‌شده نوشته شده تا روی دیتابیس‌هایی که بخشی از این
/// جداول را از قبل دارند (اختلاف Snapshot با اسکیمای واقعی) بدون خطا اجرا شود.
/// </summary>
public partial class AddAccountingModule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[AccAccounts]', N'U') IS NULL
BEGIN
    CREATE TABLE [AccAccounts] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(180) NOT NULL,
        [EnName] nvarchar(180) NULL,
        [ParentId] int NULL,
        [Level] int NOT NULL,
        [Type] int NOT NULL,
        [Nature] int NOT NULL,
        [IsPermanent] bit NOT NULL,
        [IsPostable] bit NOT NULL,
        [RequiresParty] bit NOT NULL,
        [IsSystem] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [Description] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AccAccounts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AccAccounts_AccAccounts_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [AccAccounts] ([Id]) ON DELETE NO ACTION
    );
END

IF OBJECT_ID(N'[AccFiscalYears]', N'U') IS NULL
BEGIN
    CREATE TABLE [AccFiscalYears] (
        [Id] int NOT NULL IDENTITY,
        [Title] nvarchar(120) NOT NULL,
        [Code] nvarchar(20) NULL,
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NOT NULL,
        [IsCurrent] bit NOT NULL,
        [IsClosed] bit NOT NULL,
        [Description] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AccFiscalYears] PRIMARY KEY ([Id])
    );
END

IF OBJECT_ID(N'[AccInvRules]', N'U') IS NULL
BEGIN
    CREATE TABLE [AccInvRules] (
        [Id] int NOT NULL IDENTITY,
        [DocTypeId] int NOT NULL,
        [InventoryAccountId] int NULL,
        [CounterAccountId] int NULL,
        [UseCostValue] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(500) NULL,
        CONSTRAINT [PK_AccInvRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AccInvRules_InvDocTypes_DocTypeId] FOREIGN KEY ([DocTypeId]) REFERENCES [InvDocTypes] ([Id]) ON DELETE CASCADE
    );
END

IF OBJECT_ID(N'[AccVouchers]', N'U') IS NULL
BEGIN
    CREATE TABLE [AccVouchers] (
        [Id] int NOT NULL IDENTITY,
        [Number] int NOT NULL,
        [RefNumber] nvarchar(60) NULL,
        [FiscalYearId] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [Description] nvarchar(600) NULL,
        [Status] int NOT NULL,
        [Source] int NOT NULL,
        [SourceId] int NULL,
        [SourceTitle] nvarchar(200) NULL,
        [TotalDebit] decimal(18,2) NOT NULL,
        [TotalCredit] decimal(18,2) NOT NULL,
        [CreatedBy] nvarchar(120) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ConfirmedBy] nvarchar(120) NULL,
        [ConfirmedAt] datetime2 NULL,
        CONSTRAINT [PK_AccVouchers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AccVouchers_AccFiscalYears_FiscalYearId] FOREIGN KEY ([FiscalYearId]) REFERENCES [AccFiscalYears] ([Id]) ON DELETE NO ACTION
    );
END

IF OBJECT_ID(N'[AccVoucherLines]', N'U') IS NULL
BEGIN
    CREATE TABLE [AccVoucherLines] (
        [Id] int NOT NULL IDENTITY,
        [VoucherId] int NOT NULL,
        [RowNo] int NOT NULL,
        [AccountId] int NOT NULL,
        [PartyId] int NULL,
        [ProjectId] int NULL,
        [Description] nvarchar(600) NULL,
        [RefNumber] nvarchar(60) NULL,
        [Debit] decimal(18,2) NOT NULL,
        [Credit] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_AccVoucherLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AccVoucherLines_AccAccounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [AccAccounts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AccVoucherLines_AccVouchers_VoucherId] FOREIGN KEY ([VoucherId]) REFERENCES [AccVouchers] ([Id]) ON DELETE CASCADE
    );
END

IF OBJECT_ID(N'[AccAccounts]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccAccounts_Code' AND [object_id] = OBJECT_ID(N'[AccAccounts]'))
BEGIN
    CREATE UNIQUE INDEX [IX_AccAccounts_Code] ON [AccAccounts] ([Code]);
END

IF OBJECT_ID(N'[AccAccounts]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccAccounts_Level' AND [object_id] = OBJECT_ID(N'[AccAccounts]'))
BEGIN
    CREATE INDEX [IX_AccAccounts_Level] ON [AccAccounts] ([Level]);
END

IF OBJECT_ID(N'[AccAccounts]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccAccounts_ParentId' AND [object_id] = OBJECT_ID(N'[AccAccounts]'))
BEGIN
    CREATE INDEX [IX_AccAccounts_ParentId] ON [AccAccounts] ([ParentId]);
END

IF OBJECT_ID(N'[AccFiscalYears]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccFiscalYears_IsCurrent' AND [object_id] = OBJECT_ID(N'[AccFiscalYears]'))
BEGIN
    CREATE INDEX [IX_AccFiscalYears_IsCurrent] ON [AccFiscalYears] ([IsCurrent]);
END

IF OBJECT_ID(N'[AccFiscalYears]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccFiscalYears_Title' AND [object_id] = OBJECT_ID(N'[AccFiscalYears]'))
BEGIN
    CREATE UNIQUE INDEX [IX_AccFiscalYears_Title] ON [AccFiscalYears] ([Title]);
END

IF OBJECT_ID(N'[AccInvRules]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccInvRules_DocTypeId' AND [object_id] = OBJECT_ID(N'[AccInvRules]'))
BEGIN
    CREATE UNIQUE INDEX [IX_AccInvRules_DocTypeId] ON [AccInvRules] ([DocTypeId]);
END

IF OBJECT_ID(N'[AccVoucherLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccVoucherLines_AccountId' AND [object_id] = OBJECT_ID(N'[AccVoucherLines]'))
BEGIN
    CREATE INDEX [IX_AccVoucherLines_AccountId] ON [AccVoucherLines] ([AccountId]);
END

IF OBJECT_ID(N'[AccVoucherLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccVoucherLines_PartyId' AND [object_id] = OBJECT_ID(N'[AccVoucherLines]'))
BEGIN
    CREATE INDEX [IX_AccVoucherLines_PartyId] ON [AccVoucherLines] ([PartyId]);
END

IF OBJECT_ID(N'[AccVoucherLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccVoucherLines_VoucherId' AND [object_id] = OBJECT_ID(N'[AccVoucherLines]'))
BEGIN
    CREATE INDEX [IX_AccVoucherLines_VoucherId] ON [AccVoucherLines] ([VoucherId]);
END

IF OBJECT_ID(N'[AccVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccVouchers_Date' AND [object_id] = OBJECT_ID(N'[AccVouchers]'))
BEGIN
    CREATE INDEX [IX_AccVouchers_Date] ON [AccVouchers] ([Date]);
END

IF OBJECT_ID(N'[AccVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccVouchers_FiscalYearId_Number' AND [object_id] = OBJECT_ID(N'[AccVouchers]'))
BEGIN
    CREATE UNIQUE INDEX [IX_AccVouchers_FiscalYearId_Number] ON [AccVouchers] ([FiscalYearId], [Number]);
END

IF OBJECT_ID(N'[AccVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccVouchers_Source_SourceId' AND [object_id] = OBJECT_ID(N'[AccVouchers]'))
BEGIN
    CREATE INDEX [IX_AccVouchers_Source_SourceId] ON [AccVouchers] ([Source], [SourceId]);
END

IF OBJECT_ID(N'[AccVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccVouchers_Status' AND [object_id] = OBJECT_ID(N'[AccVouchers]'))
BEGIN
    CREATE INDEX [IX_AccVouchers_Status] ON [AccVouchers] ([Status]);
END
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[AccVoucherLines]', N'U') IS NOT NULL DROP TABLE [AccVoucherLines];

IF OBJECT_ID(N'[AccVouchers]', N'U') IS NOT NULL DROP TABLE [AccVouchers];

IF OBJECT_ID(N'[AccInvRules]', N'U') IS NOT NULL DROP TABLE [AccInvRules];

IF OBJECT_ID(N'[AccAccounts]', N'U') IS NOT NULL DROP TABLE [AccAccounts];

IF OBJECT_ID(N'[AccFiscalYears]', N'U') IS NOT NULL DROP TABLE [AccFiscalYears];
");
    }
}
