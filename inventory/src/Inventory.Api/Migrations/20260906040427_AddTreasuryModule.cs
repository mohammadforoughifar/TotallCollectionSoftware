using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <summary>
    /// ماژول خزانه‌داری: صندوق/بانک، اسناد دریافت و پرداخت، چک و قواعد سند خودکار.
    ///
    /// این مهاجرت به‌جای عملیات EF از T-SQL خام و «گاردشده» استفاده می‌کند تا روی
    /// دیتابیس‌های واقعی که با ModelSnapshot مخزن کاملاً هم‌خوان نیستند هم بدون خطا اجرا شود.
    /// </summary>
    /// <inheritdoc />
    public partial class AddTreasuryModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF OBJECT_ID(N'[TrsAccounts]', N'U') IS NULL
BEGIN
CREATE TABLE [TrsAccounts] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(40) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Kind] int NOT NULL,
    [AccountId] int NULL,
    [BankName] nvarchar(120) NULL,
    [BranchName] nvarchar(120) NULL,
    [BranchCode] nvarchar(30) NULL,
    [AccountNumber] nvarchar(40) NULL,
    [Iban] nvarchar(34) NULL,
    [CardNumber] nvarchar(20) NULL,
    [OpeningBalance] decimal(18,2) NOT NULL,
    [IsDefault] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [Description] nvarchar(500) NULL,
    [SortOrder] int NOT NULL,
    CONSTRAINT [PK_TrsAccounts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TrsAccounts_AccAccounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [AccAccounts] ([Id]) ON DELETE SET NULL
);
END;

IF OBJECT_ID(N'[TrsRules]', N'U') IS NULL
BEGIN
CREATE TABLE [TrsRules] (
    [Id] int NOT NULL IDENTITY,
    [Kind] int NOT NULL,
    [PartyAccountId] int NULL,
    [ChequeAccountId] int NULL,
    [CollectionAccountId] int NULL,
    [DiscountAccountId] int NULL,
    [FeeAccountId] int NULL,
    [AutoVoucher] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [Description] nvarchar(500) NULL,
    CONSTRAINT [PK_TrsRules] PRIMARY KEY ([Id])
);
END;

IF OBJECT_ID(N'[TrsCheques]', N'U') IS NULL
BEGIN
CREATE TABLE [TrsCheques] (
    [Id] int NOT NULL IDENTITY,
    [Kind] int NOT NULL,
    [Number] nvarchar(40) NOT NULL,
    [SayadId] nvarchar(20) NULL,
    [Amount] decimal(18,2) NOT NULL,
    [IssueDate] datetime2 NOT NULL,
    [DueDate] datetime2 NOT NULL,
    [BankName] nvarchar(120) NULL,
    [BranchName] nvarchar(120) NULL,
    [AccountNumber] nvarchar(40) NULL,
    [OwnerName] nvarchar(200) NULL,
    [PartyId] int NULL,
    [TrsAccountId] int NULL,
    [Status] int NOT NULL,
    [StatusDate] datetime2 NULL,
    [Description] nvarchar(500) NULL,
    [TrsVoucherId] int NULL,
    [CreatedBy] nvarchar(120) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_TrsCheques] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TrsCheques_Parties_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [Parties] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TrsCheques_TrsAccounts_TrsAccountId] FOREIGN KEY ([TrsAccountId]) REFERENCES [TrsAccounts] ([Id]) ON DELETE NO ACTION
);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NULL
BEGIN
CREATE TABLE [TrsVouchers] (
    [Id] int NOT NULL IDENTITY,
    [Number] int NOT NULL,
    [Kind] int NOT NULL,
    [Date] datetime2 NOT NULL,
    [PartyId] int NULL,
    [Description] nvarchar(600) NULL,
    [RefNumber] nvarchar(60) NULL,
    [Status] int NOT NULL,
    [FromAccountId] int NULL,
    [ToAccountId] int NULL,
    [FeeAmount] decimal(18,2) NOT NULL,
    [InvoiceId] int NULL,
    [TotalAmount] decimal(18,2) NOT NULL,
    [CashAmount] decimal(18,2) NOT NULL,
    [ChequeAmount] decimal(18,2) NOT NULL,
    [DiscountAmount] decimal(18,2) NOT NULL,
    [VoucherId] int NULL,
    [CreatedBy] nvarchar(120) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ConfirmedBy] nvarchar(120) NULL,
    [ConfirmedAt] datetime2 NULL,
    CONSTRAINT [PK_TrsVouchers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TrsVouchers_Parties_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [Parties] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TrsVouchers_TrsAccounts_FromAccountId] FOREIGN KEY ([FromAccountId]) REFERENCES [TrsAccounts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TrsVouchers_TrsAccounts_ToAccountId] FOREIGN KEY ([ToAccountId]) REFERENCES [TrsAccounts] ([Id]) ON DELETE NO ACTION
);
END;

IF OBJECT_ID(N'[TrsChequeActions]', N'U') IS NULL
BEGIN
CREATE TABLE [TrsChequeActions] (
    [Id] int NOT NULL IDENTITY,
    [ChequeId] int NOT NULL,
    [Status] int NOT NULL,
    [Date] datetime2 NOT NULL,
    [TrsAccountId] int NULL,
    [Description] nvarchar(500) NULL,
    [VoucherId] int NULL,
    [CreatedBy] nvarchar(120) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_TrsChequeActions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TrsChequeActions_TrsCheques_ChequeId] FOREIGN KEY ([ChequeId]) REFERENCES [TrsCheques] ([Id]) ON DELETE CASCADE
);
END;

IF OBJECT_ID(N'[TrsVoucherLines]', N'U') IS NULL
BEGIN
CREATE TABLE [TrsVoucherLines] (
    [Id] int NOT NULL IDENTITY,
    [TrsVoucherId] int NOT NULL,
    [RowNo] int NOT NULL,
    [Method] int NOT NULL,
    [TrsAccountId] int NULL,
    [Amount] decimal(18,2) NOT NULL,
    [RefNumber] nvarchar(60) NULL,
    [Description] nvarchar(500) NULL,
    [ChequeId] int NULL,
    CONSTRAINT [PK_TrsVoucherLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TrsVoucherLines_TrsAccounts_TrsAccountId] FOREIGN KEY ([TrsAccountId]) REFERENCES [TrsAccounts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TrsVoucherLines_TrsCheques_ChequeId] FOREIGN KEY ([ChequeId]) REFERENCES [TrsCheques] ([Id]) ON DELETE SET NULL,
    CONSTRAINT [FK_TrsVoucherLines_TrsVouchers_TrsVoucherId] FOREIGN KEY ([TrsVoucherId]) REFERENCES [TrsVouchers] ([Id]) ON DELETE CASCADE
);
END;

IF OBJECT_ID(N'[TrsAccounts]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsAccounts_AccountId' AND [object_id] = OBJECT_ID(N'[TrsAccounts]'))
BEGIN
CREATE INDEX [IX_TrsAccounts_AccountId] ON [TrsAccounts] ([AccountId]);
END;

IF OBJECT_ID(N'[TrsAccounts]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsAccounts_Code' AND [object_id] = OBJECT_ID(N'[TrsAccounts]'))
BEGIN
CREATE UNIQUE INDEX [IX_TrsAccounts_Code] ON [TrsAccounts] ([Code]);
END;

IF OBJECT_ID(N'[TrsAccounts]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsAccounts_Kind' AND [object_id] = OBJECT_ID(N'[TrsAccounts]'))
BEGIN
CREATE INDEX [IX_TrsAccounts_Kind] ON [TrsAccounts] ([Kind]);
END;

IF OBJECT_ID(N'[TrsChequeActions]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsChequeActions_ChequeId' AND [object_id] = OBJECT_ID(N'[TrsChequeActions]'))
BEGIN
CREATE INDEX [IX_TrsChequeActions_ChequeId] ON [TrsChequeActions] ([ChequeId]);
END;

IF OBJECT_ID(N'[TrsCheques]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsCheques_DueDate' AND [object_id] = OBJECT_ID(N'[TrsCheques]'))
BEGIN
CREATE INDEX [IX_TrsCheques_DueDate] ON [TrsCheques] ([DueDate]);
END;

IF OBJECT_ID(N'[TrsCheques]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsCheques_Kind_Number' AND [object_id] = OBJECT_ID(N'[TrsCheques]'))
BEGIN
CREATE INDEX [IX_TrsCheques_Kind_Number] ON [TrsCheques] ([Kind], [Number]);
END;

IF OBJECT_ID(N'[TrsCheques]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsCheques_PartyId' AND [object_id] = OBJECT_ID(N'[TrsCheques]'))
BEGIN
CREATE INDEX [IX_TrsCheques_PartyId] ON [TrsCheques] ([PartyId]);
END;

IF OBJECT_ID(N'[TrsCheques]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsCheques_Status' AND [object_id] = OBJECT_ID(N'[TrsCheques]'))
BEGIN
CREATE INDEX [IX_TrsCheques_Status] ON [TrsCheques] ([Status]);
END;

IF OBJECT_ID(N'[TrsCheques]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsCheques_TrsAccountId' AND [object_id] = OBJECT_ID(N'[TrsCheques]'))
BEGIN
CREATE INDEX [IX_TrsCheques_TrsAccountId] ON [TrsCheques] ([TrsAccountId]);
END;

IF OBJECT_ID(N'[TrsCheques]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsCheques_TrsVoucherId' AND [object_id] = OBJECT_ID(N'[TrsCheques]'))
BEGIN
CREATE INDEX [IX_TrsCheques_TrsVoucherId] ON [TrsCheques] ([TrsVoucherId]);
END;

IF OBJECT_ID(N'[TrsRules]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsRules_Kind' AND [object_id] = OBJECT_ID(N'[TrsRules]'))
BEGIN
CREATE UNIQUE INDEX [IX_TrsRules_Kind] ON [TrsRules] ([Kind]);
END;

IF OBJECT_ID(N'[TrsVoucherLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVoucherLines_ChequeId' AND [object_id] = OBJECT_ID(N'[TrsVoucherLines]'))
BEGIN
CREATE INDEX [IX_TrsVoucherLines_ChequeId] ON [TrsVoucherLines] ([ChequeId]);
END;

IF OBJECT_ID(N'[TrsVoucherLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVoucherLines_TrsAccountId' AND [object_id] = OBJECT_ID(N'[TrsVoucherLines]'))
BEGIN
CREATE INDEX [IX_TrsVoucherLines_TrsAccountId] ON [TrsVoucherLines] ([TrsAccountId]);
END;

IF OBJECT_ID(N'[TrsVoucherLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVoucherLines_TrsVoucherId' AND [object_id] = OBJECT_ID(N'[TrsVoucherLines]'))
BEGIN
CREATE INDEX [IX_TrsVoucherLines_TrsVoucherId] ON [TrsVoucherLines] ([TrsVoucherId]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_Date' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE INDEX [IX_TrsVouchers_Date] ON [TrsVouchers] ([Date]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_FromAccountId' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE INDEX [IX_TrsVouchers_FromAccountId] ON [TrsVouchers] ([FromAccountId]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_InvoiceId' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE INDEX [IX_TrsVouchers_InvoiceId] ON [TrsVouchers] ([InvoiceId]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_Kind_Number' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE UNIQUE INDEX [IX_TrsVouchers_Kind_Number] ON [TrsVouchers] ([Kind], [Number]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_PartyId' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE INDEX [IX_TrsVouchers_PartyId] ON [TrsVouchers] ([PartyId]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_Status' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE INDEX [IX_TrsVouchers_Status] ON [TrsVouchers] ([Status]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_ToAccountId' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE INDEX [IX_TrsVouchers_ToAccountId] ON [TrsVouchers] ([ToAccountId]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_VoucherId' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE INDEX [IX_TrsVouchers_VoucherId] ON [TrsVouchers] ([VoucherId]);
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[TrsChequeActions]', N'U') IS NOT NULL DROP TABLE [TrsChequeActions];
IF OBJECT_ID(N'[TrsVoucherLines]', N'U') IS NOT NULL DROP TABLE [TrsVoucherLines];
IF OBJECT_ID(N'[TrsCheques]', N'U') IS NOT NULL DROP TABLE [TrsCheques];
IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL DROP TABLE [TrsVouchers];
IF OBJECT_ID(N'[TrsAccounts]', N'U') IS NOT NULL DROP TABLE [TrsAccounts];
IF OBJECT_ID(N'[TrsRules]', N'U') IS NOT NULL DROP TABLE [TrsRules];
");
        }
    }
}
