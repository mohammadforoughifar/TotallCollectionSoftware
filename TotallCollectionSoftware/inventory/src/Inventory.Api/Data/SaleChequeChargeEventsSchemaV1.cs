using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>پیوند افزایشی، یکتا و حذف‌نشدنی رویداد درآمد به سند حسابداری.</summary>
public static class SaleChequeChargeEventsSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.SaleChequeCharges', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.AccVouchers', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SaleChequeChargeEvents', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SaleChequeChargeEvents] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SaleChequeChargeEvents] PRIMARY KEY,
        [CompanyId] int NULL,
        [OperationId] uniqueidentifier NOT NULL,
        [SaleChequeChargeId] int NOT NULL,
        [AccVoucherId] int NOT NULL,
        [Kind] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [IncomeDelta] decimal(18,2) NOT NULL,
        CONSTRAINT [FK_SaleChequeChargeEvents_Charge] FOREIGN KEY ([SaleChequeChargeId]) REFERENCES [dbo].[SaleChequeCharges] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeChargeEvents_Voucher] FOREIGN KEY ([AccVoucherId]) REFERENCES [dbo].[AccVouchers] ([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_SaleChequeChargeEvents_OperationId] ON [dbo].[SaleChequeChargeEvents] ([OperationId]);
    CREATE UNIQUE INDEX [IX_SaleChequeChargeEvents_AccVoucherId] ON [dbo].[SaleChequeChargeEvents] ([AccVoucherId]);
    CREATE INDEX [IX_SaleChequeChargeEvents_SaleChequeChargeId_Date] ON [dbo].[SaleChequeChargeEvents] ([SaleChequeChargeId], [Date]);
END";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS ""SaleChequeChargeEvents"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SaleChequeChargeEvents"" PRIMARY KEY AUTOINCREMENT,
    ""CompanyId"" INTEGER NULL,
    ""OperationId"" TEXT NOT NULL,
    ""SaleChequeChargeId"" INTEGER NOT NULL,
    ""AccVoucherId"" INTEGER NOT NULL,
    ""Kind"" INTEGER NOT NULL,
    ""Date"" TEXT NOT NULL,
    ""IncomeDelta"" TEXT NOT NULL,
    CONSTRAINT ""FK_SaleChequeChargeEvents_Charge"" FOREIGN KEY (""SaleChequeChargeId"") REFERENCES ""SaleChequeCharges"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeChargeEvents_Voucher"" FOREIGN KEY (""AccVoucherId"") REFERENCES ""AccVouchers"" (""Id"") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeChargeEvents_OperationId"" ON ""SaleChequeChargeEvents"" (""OperationId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeChargeEvents_AccVoucherId"" ON ""SaleChequeChargeEvents"" (""AccVoucherId"");
CREATE INDEX IF NOT EXISTS ""IX_SaleChequeChargeEvents_SaleChequeChargeId_Date"" ON ""SaleChequeChargeEvents"" (""SaleChequeChargeId"", ""Date"");";

    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
