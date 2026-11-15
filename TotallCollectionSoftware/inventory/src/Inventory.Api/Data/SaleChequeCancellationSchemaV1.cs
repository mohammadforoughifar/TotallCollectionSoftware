using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>جدول سابقه ابطال فاکتورهای دارای تاخیر تادیه چک.</summary>
public static class SaleChequeCancellationSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SaleChequeCharges', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.AccVouchers', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SaleChequeCancellations', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SaleChequeCancellations] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SaleChequeCancellations] PRIMARY KEY,
        [CompanyId] int NULL, [OperationId] uniqueidentifier NOT NULL,
        [SaleTransactionId] int NOT NULL, [SaleChequeChargeId] int NOT NULL, [ReturnTransactionId] int NULL,
        [AccVoucherId] int NOT NULL, [SaleChequeChargeEventId] int NULL, [RefundTrsVoucherId] int NULL,
        [SalesAccountId] int NOT NULL, [VatAccountId] int NULL, [IncomeAccountId] int NOT NULL,
        [PartyAccountId] int NOT NULL, [CashLedgerAccountId] int NULL,
        [Date] datetime2 NOT NULL, [ReversedGoodsAmount] decimal(18,2) NOT NULL, [ReversedVatAmount] decimal(18,2) NOT NULL,
        [ReversedIncomeDelta] decimal(18,2) NOT NULL, [RefundedCashAmount] decimal(18,2) NOT NULL,
        [Reason] nvarchar(200) NULL, [CreatedBy] nvarchar(120) NULL, [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [FK_SaleChequeCancellations_Sale] FOREIGN KEY ([SaleTransactionId]) REFERENCES [dbo].[Transactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeCancellations_Charge] FOREIGN KEY ([SaleChequeChargeId]) REFERENCES [dbo].[SaleChequeCharges] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeCancellations_ReturnTxn] FOREIGN KEY ([ReturnTransactionId]) REFERENCES [dbo].[Transactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeCancellations_Voucher] FOREIGN KEY ([AccVoucherId]) REFERENCES [dbo].[AccVouchers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeCancellations_Event] FOREIGN KEY ([SaleChequeChargeEventId]) REFERENCES [dbo].[SaleChequeChargeEvents] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeCancellations_TrsVoucher] FOREIGN KEY ([RefundTrsVoucherId]) REFERENCES [dbo].[TrsVouchers] ([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_SaleChequeCancellations_OperationId] ON [dbo].[SaleChequeCancellations] ([OperationId]);
    CREATE UNIQUE INDEX [IX_SaleChequeCancellations_AccVoucherId] ON [dbo].[SaleChequeCancellations] ([AccVoucherId]);
    CREATE UNIQUE INDEX [IX_SaleChequeCancellations_EventId] ON [dbo].[SaleChequeCancellations] ([SaleChequeChargeEventId]) WHERE [SaleChequeChargeEventId] IS NOT NULL;
    CREATE UNIQUE INDEX [IX_SaleChequeCancellations_RefundTrsVoucherId] ON [dbo].[SaleChequeCancellations] ([RefundTrsVoucherId]) WHERE [RefundTrsVoucherId] IS NOT NULL;
    CREATE INDEX [IX_SaleChequeCancellations_SaleTransactionId] ON [dbo].[SaleChequeCancellations] ([SaleTransactionId]);
END";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS ""SaleChequeCancellations"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SaleChequeCancellations"" PRIMARY KEY AUTOINCREMENT,
    ""CompanyId"" INTEGER NULL, ""OperationId"" TEXT NOT NULL,
    ""SaleTransactionId"" INTEGER NOT NULL, ""SaleChequeChargeId"" INTEGER NOT NULL, ""ReturnTransactionId"" INTEGER NULL,
    ""AccVoucherId"" INTEGER NOT NULL, ""SaleChequeChargeEventId"" INTEGER NULL, ""RefundTrsVoucherId"" INTEGER NULL,
    ""SalesAccountId"" INTEGER NOT NULL, ""VatAccountId"" INTEGER NULL, ""IncomeAccountId"" INTEGER NOT NULL,
    ""PartyAccountId"" INTEGER NOT NULL, ""CashLedgerAccountId"" INTEGER NULL,
    ""Date"" TEXT NOT NULL, ""ReversedGoodsAmount"" TEXT NOT NULL, ""ReversedVatAmount"" TEXT NOT NULL,
    ""ReversedIncomeDelta"" TEXT NOT NULL, ""RefundedCashAmount"" TEXT NOT NULL,
    ""Reason"" TEXT NULL, ""CreatedBy"" TEXT NULL, ""CreatedAt"" TEXT NOT NULL,
    CONSTRAINT ""FK_SaleChequeCancellations_Sale"" FOREIGN KEY (""SaleTransactionId"") REFERENCES ""Transactions"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeCancellations_Charge"" FOREIGN KEY (""SaleChequeChargeId"") REFERENCES ""SaleChequeCharges"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeCancellations_ReturnTxn"" FOREIGN KEY (""ReturnTransactionId"") REFERENCES ""Transactions"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeCancellations_Voucher"" FOREIGN KEY (""AccVoucherId"") REFERENCES ""AccVouchers"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeCancellations_Event"" FOREIGN KEY (""SaleChequeChargeEventId"") REFERENCES ""SaleChequeChargeEvents"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeCancellations_TrsVoucher"" FOREIGN KEY (""RefundTrsVoucherId"") REFERENCES ""TrsVouchers"" (""Id"") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeCancellations_OperationId"" ON ""SaleChequeCancellations"" (""OperationId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeCancellations_AccVoucherId"" ON ""SaleChequeCancellations"" (""AccVoucherId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeCancellations_EventId"" ON ""SaleChequeCancellations"" (""SaleChequeChargeEventId"") WHERE ""SaleChequeChargeEventId"" IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeCancellations_RefundTrsVoucherId"" ON ""SaleChequeCancellations"" (""RefundTrsVoucherId"") WHERE ""RefundTrsVoucherId"" IS NOT NULL;
CREATE INDEX IF NOT EXISTS ""IX_SaleChequeCancellations_SaleTransactionId"" ON ""SaleChequeCancellations"" (""SaleTransactionId"");";

    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
