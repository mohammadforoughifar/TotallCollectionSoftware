using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>ثبت برگشت و اجزای آن بدون حذف فاکتور، چک یا سند اولیه.</summary>
public static class SaleChequeReturnSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TransactionLines', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.SaleChequeCharges', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.AccVouchers', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.SaleChequeReturns', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SaleChequeReturns] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SaleChequeReturns] PRIMARY KEY,
        [CompanyId] int NULL, [OperationId] uniqueidentifier NOT NULL,
        [SaleTransactionId] int NOT NULL, [ReturnTransactionId] int NOT NULL, [SaleChequeChargeId] int NOT NULL,
        [AccVoucherId] int NOT NULL, [SaleChequeChargeEventId] int NULL,
        [SalesAccountId] int NOT NULL, [VatAccountId] int NULL, [IncomeAccountId] int NOT NULL, [PartyAccountId] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [GoodsAmount] decimal(18,2) NOT NULL, [VatAmount] decimal(18,2) NOT NULL, [IncomeAmount] decimal(18,2) NOT NULL,
        [EpochRescheduleId] int NULL, [EpochBase] decimal(18,2) NOT NULL, [EpochIncome] decimal(18,2) NOT NULL,
        [PreviouslyReturnedBaseInEpoch] decimal(18,2) NOT NULL, [BalanceAfter] decimal(18,2) NOT NULL,
        [UncollectedChequeExcess] decimal(18,2) NOT NULL, [Decision] int NOT NULL,
        [CreatedBy] nvarchar(120) NULL, [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [FK_SaleChequeReturns_Sale] FOREIGN KEY ([SaleTransactionId]) REFERENCES [dbo].[Transactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeReturns_Return] FOREIGN KEY ([ReturnTransactionId]) REFERENCES [dbo].[Transactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeReturns_Charge] FOREIGN KEY ([SaleChequeChargeId]) REFERENCES [dbo].[SaleChequeCharges] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeReturns_Voucher] FOREIGN KEY ([AccVoucherId]) REFERENCES [dbo].[AccVouchers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeReturns_Event] FOREIGN KEY ([SaleChequeChargeEventId]) REFERENCES [dbo].[SaleChequeChargeEvents] ([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_SaleChequeReturns_OperationId] ON [dbo].[SaleChequeReturns] ([OperationId]);
    CREATE UNIQUE INDEX [IX_SaleChequeReturns_ReturnTransactionId] ON [dbo].[SaleChequeReturns] ([ReturnTransactionId]);
    CREATE UNIQUE INDEX [IX_SaleChequeReturns_AccVoucherId] ON [dbo].[SaleChequeReturns] ([AccVoucherId]);
    CREATE UNIQUE INDEX [IX_SaleChequeReturns_EventId] ON [dbo].[SaleChequeReturns] ([SaleChequeChargeEventId]) WHERE [SaleChequeChargeEventId] IS NOT NULL;
    CREATE INDEX [IX_SaleChequeReturns_SaleTransactionId] ON [dbo].[SaleChequeReturns] ([SaleTransactionId]);
    CREATE INDEX [IX_SaleChequeReturns_SaleChequeChargeId] ON [dbo].[SaleChequeReturns] ([SaleChequeChargeId]);
END
IF OBJECT_ID(N'dbo.SaleChequeReturns', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SaleChequeReturnLines', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SaleChequeReturnLines] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SaleChequeReturnLines] PRIMARY KEY,
        [CompanyId] int NULL, [SaleChequeReturnId] int NOT NULL, [OriginalLineId] int NOT NULL, [ReturnLineId] int NOT NULL,
        [Quantity] decimal(18,2) NOT NULL, [GoodsAmount] decimal(18,2) NOT NULL, [VatAmount] decimal(18,2) NOT NULL,
        CONSTRAINT [FK_SaleChequeReturnLines_Return] FOREIGN KEY ([SaleChequeReturnId]) REFERENCES [dbo].[SaleChequeReturns] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeReturnLines_OriginalLine] FOREIGN KEY ([OriginalLineId]) REFERENCES [dbo].[TransactionLines] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeReturnLines_ReturnLine] FOREIGN KEY ([ReturnLineId]) REFERENCES [dbo].[TransactionLines] ([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_SaleChequeReturnLines_ReturnLineId] ON [dbo].[SaleChequeReturnLines] ([ReturnLineId]);
    CREATE UNIQUE INDEX [IX_SaleChequeReturnLines_Return_Original] ON [dbo].[SaleChequeReturnLines] ([SaleChequeReturnId], [OriginalLineId]);
    CREATE INDEX [IX_SaleChequeReturnLines_OriginalLineId] ON [dbo].[SaleChequeReturnLines] ([OriginalLineId]);
END";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS ""SaleChequeReturns"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SaleChequeReturns"" PRIMARY KEY AUTOINCREMENT,
    ""CompanyId"" INTEGER NULL, ""OperationId"" TEXT NOT NULL,
    ""SaleTransactionId"" INTEGER NOT NULL, ""ReturnTransactionId"" INTEGER NOT NULL, ""SaleChequeChargeId"" INTEGER NOT NULL,
    ""AccVoucherId"" INTEGER NOT NULL, ""SaleChequeChargeEventId"" INTEGER NULL,
    ""SalesAccountId"" INTEGER NOT NULL, ""VatAccountId"" INTEGER NULL, ""IncomeAccountId"" INTEGER NOT NULL, ""PartyAccountId"" INTEGER NOT NULL,
    ""Date"" TEXT NOT NULL,
    ""GoodsAmount"" TEXT NOT NULL, ""VatAmount"" TEXT NOT NULL, ""IncomeAmount"" TEXT NOT NULL,
    ""EpochRescheduleId"" INTEGER NULL, ""EpochBase"" TEXT NOT NULL, ""EpochIncome"" TEXT NOT NULL,
    ""PreviouslyReturnedBaseInEpoch"" TEXT NOT NULL, ""BalanceAfter"" TEXT NOT NULL,
    ""UncollectedChequeExcess"" TEXT NOT NULL, ""Decision"" INTEGER NOT NULL,
    ""CreatedBy"" TEXT NULL, ""CreatedAt"" TEXT NOT NULL,
    CONSTRAINT ""FK_SaleChequeReturns_Sale"" FOREIGN KEY (""SaleTransactionId"") REFERENCES ""Transactions"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeReturns_Return"" FOREIGN KEY (""ReturnTransactionId"") REFERENCES ""Transactions"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeReturns_Charge"" FOREIGN KEY (""SaleChequeChargeId"") REFERENCES ""SaleChequeCharges"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeReturns_Voucher"" FOREIGN KEY (""AccVoucherId"") REFERENCES ""AccVouchers"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeReturns_Event"" FOREIGN KEY (""SaleChequeChargeEventId"") REFERENCES ""SaleChequeChargeEvents"" (""Id"") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReturns_OperationId"" ON ""SaleChequeReturns"" (""OperationId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReturns_ReturnTransactionId"" ON ""SaleChequeReturns"" (""ReturnTransactionId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReturns_AccVoucherId"" ON ""SaleChequeReturns"" (""AccVoucherId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReturns_EventId"" ON ""SaleChequeReturns"" (""SaleChequeChargeEventId"") WHERE ""SaleChequeChargeEventId"" IS NOT NULL;
CREATE INDEX IF NOT EXISTS ""IX_SaleChequeReturns_SaleTransactionId"" ON ""SaleChequeReturns"" (""SaleTransactionId"");
CREATE INDEX IF NOT EXISTS ""IX_SaleChequeReturns_SaleChequeChargeId"" ON ""SaleChequeReturns"" (""SaleChequeChargeId"");
CREATE TABLE IF NOT EXISTS ""SaleChequeReturnLines"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SaleChequeReturnLines"" PRIMARY KEY AUTOINCREMENT,
    ""CompanyId"" INTEGER NULL, ""SaleChequeReturnId"" INTEGER NOT NULL, ""OriginalLineId"" INTEGER NOT NULL, ""ReturnLineId"" INTEGER NOT NULL,
    ""Quantity"" TEXT NOT NULL, ""GoodsAmount"" TEXT NOT NULL, ""VatAmount"" TEXT NOT NULL,
    CONSTRAINT ""FK_SaleChequeReturnLines_Return"" FOREIGN KEY (""SaleChequeReturnId"") REFERENCES ""SaleChequeReturns"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeReturnLines_OriginalLine"" FOREIGN KEY (""OriginalLineId"") REFERENCES ""TransactionLines"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeReturnLines_ReturnLine"" FOREIGN KEY (""ReturnLineId"") REFERENCES ""TransactionLines"" (""Id"") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReturnLines_ReturnLineId"" ON ""SaleChequeReturnLines"" (""ReturnLineId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReturnLines_Return_Original"" ON ""SaleChequeReturnLines"" (""SaleChequeReturnId"", ""OriginalLineId"");
CREATE INDEX IF NOT EXISTS ""IX_SaleChequeReturnLines_OriginalLineId"" ON ""SaleChequeReturnLines"" (""OriginalLineId"");";

    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
