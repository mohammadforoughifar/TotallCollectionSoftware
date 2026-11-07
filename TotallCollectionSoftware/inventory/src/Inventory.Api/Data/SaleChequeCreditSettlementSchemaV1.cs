using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>جدول ثبت پرداخت یا مصرف بستانکاری مشتری بدون دوباره‌شماری وصول چک.</summary>
public static class SaleChequeCreditSettlementSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SaleChequeCharges', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.AccVouchers', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SaleChequeCreditSettlements', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SaleChequeCreditSettlements] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SaleChequeCreditSettlements] PRIMARY KEY,
        [CompanyId] int NULL, [OperationId] uniqueidentifier NOT NULL, [Kind] int NOT NULL,
        [SaleTransactionId] int NOT NULL, [TargetSaleTransactionId] int NULL, [SaleChequeChargeId] int NOT NULL,
        [TrsAccountId] int NULL, [TreasuryLedgerAccountId] int NULL,
        [PartyAccountId] int NOT NULL, [TargetPartyAccountId] int NULL,
        [AccVoucherId] int NOT NULL, [TrsVoucherId] int NULL,
        [Date] datetime2 NOT NULL, [Amount] decimal(18,2) NOT NULL,
        [Note] nvarchar(200) NULL, [CreatedBy] nvarchar(120) NULL, [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [FK_SaleChequeCreditSettlements_Sale] FOREIGN KEY ([SaleTransactionId]) REFERENCES [dbo].[Transactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeCreditSettlements_TargetSale] FOREIGN KEY ([TargetSaleTransactionId]) REFERENCES [dbo].[Transactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeCreditSettlements_Charge] FOREIGN KEY ([SaleChequeChargeId]) REFERENCES [dbo].[SaleChequeCharges] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeCreditSettlements_TrsAccount] FOREIGN KEY ([TrsAccountId]) REFERENCES [dbo].[TrsAccounts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeCreditSettlements_Voucher] FOREIGN KEY ([AccVoucherId]) REFERENCES [dbo].[AccVouchers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeCreditSettlements_TrsVoucher] FOREIGN KEY ([TrsVoucherId]) REFERENCES [dbo].[TrsVouchers] ([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_SaleChequeCreditSettlements_OperationId] ON [dbo].[SaleChequeCreditSettlements] ([OperationId]);
    CREATE UNIQUE INDEX [IX_SaleChequeCreditSettlements_AccVoucherId] ON [dbo].[SaleChequeCreditSettlements] ([AccVoucherId]);
    CREATE UNIQUE INDEX [IX_SaleChequeCreditSettlements_TrsVoucherId] ON [dbo].[SaleChequeCreditSettlements] ([TrsVoucherId]) WHERE [TrsVoucherId] IS NOT NULL;
    CREATE INDEX [IX_SaleChequeCreditSettlements_SaleTransactionId] ON [dbo].[SaleChequeCreditSettlements] ([SaleTransactionId]);
    CREATE INDEX [IX_SaleChequeCreditSettlements_TargetSaleTransactionId] ON [dbo].[SaleChequeCreditSettlements] ([TargetSaleTransactionId]);
END";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS ""SaleChequeCreditSettlements"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SaleChequeCreditSettlements"" PRIMARY KEY AUTOINCREMENT,
    ""CompanyId"" INTEGER NULL, ""OperationId"" TEXT NOT NULL, ""Kind"" INTEGER NOT NULL,
    ""SaleTransactionId"" INTEGER NOT NULL, ""TargetSaleTransactionId"" INTEGER NULL, ""SaleChequeChargeId"" INTEGER NOT NULL,
    ""TrsAccountId"" INTEGER NULL, ""TreasuryLedgerAccountId"" INTEGER NULL,
    ""PartyAccountId"" INTEGER NOT NULL, ""TargetPartyAccountId"" INTEGER NULL,
    ""AccVoucherId"" INTEGER NOT NULL, ""TrsVoucherId"" INTEGER NULL,
    ""Date"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL,
    ""Note"" TEXT NULL, ""CreatedBy"" TEXT NULL, ""CreatedAt"" TEXT NOT NULL,
    CONSTRAINT ""FK_SaleChequeCreditSettlements_Sale"" FOREIGN KEY (""SaleTransactionId"") REFERENCES ""Transactions"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeCreditSettlements_TargetSale"" FOREIGN KEY (""TargetSaleTransactionId"") REFERENCES ""Transactions"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeCreditSettlements_Charge"" FOREIGN KEY (""SaleChequeChargeId"") REFERENCES ""SaleChequeCharges"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeCreditSettlements_TrsAccount"" FOREIGN KEY (""TrsAccountId"") REFERENCES ""TrsAccounts"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeCreditSettlements_Voucher"" FOREIGN KEY (""AccVoucherId"") REFERENCES ""AccVouchers"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeCreditSettlements_TrsVoucher"" FOREIGN KEY (""TrsVoucherId"") REFERENCES ""TrsVouchers"" (""Id"") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeCreditSettlements_OperationId"" ON ""SaleChequeCreditSettlements"" (""OperationId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeCreditSettlements_AccVoucherId"" ON ""SaleChequeCreditSettlements"" (""AccVoucherId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeCreditSettlements_TrsVoucherId"" ON ""SaleChequeCreditSettlements"" (""TrsVoucherId"") WHERE ""TrsVoucherId"" IS NOT NULL;
CREATE INDEX IF NOT EXISTS ""IX_SaleChequeCreditSettlements_SaleTransactionId"" ON ""SaleChequeCreditSettlements"" (""SaleTransactionId"");
CREATE INDEX IF NOT EXISTS ""IX_SaleChequeCreditSettlements_TargetSaleTransactionId"" ON ""SaleChequeCreditSettlements"" (""TargetSaleTransactionId"");";

    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
