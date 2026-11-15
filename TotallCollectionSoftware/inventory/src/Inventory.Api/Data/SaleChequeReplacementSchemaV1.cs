using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>سابقه تعویض چک بدون حذف یا تغییر خودکار چک قبلی.</summary>
public static class SaleChequeReplacementSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Cheques', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.SaleChequeCharges', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SaleChequeReplacements', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SaleChequeReplacements] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SaleChequeReplacements] PRIMARY KEY,
        [CompanyId] int NULL, [OperationId] uniqueidentifier NOT NULL,
        [TransactionId] int NOT NULL, [SaleChequeChargeId] int NOT NULL,
        [OldChequeId] int NOT NULL, [NewChequeId] int NOT NULL,
        [Date] datetime2 NOT NULL, [OldAmount] decimal(18,2) NOT NULL, [NewAmount] decimal(18,2) NOT NULL,
        [Reason] nvarchar(200) NULL, [CreatedBy] nvarchar(120) NULL, [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [FK_SaleChequeReplacements_Transaction] FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[Transactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeReplacements_Charge] FOREIGN KEY ([SaleChequeChargeId]) REFERENCES [dbo].[SaleChequeCharges] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeReplacements_OldCheque] FOREIGN KEY ([OldChequeId]) REFERENCES [dbo].[Cheques] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeReplacements_NewCheque] FOREIGN KEY ([NewChequeId]) REFERENCES [dbo].[Cheques] ([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_SaleChequeReplacements_OperationId] ON [dbo].[SaleChequeReplacements] ([OperationId]);
    CREATE UNIQUE INDEX [IX_SaleChequeReplacements_OldChequeId] ON [dbo].[SaleChequeReplacements] ([OldChequeId]);
    CREATE UNIQUE INDEX [IX_SaleChequeReplacements_NewChequeId] ON [dbo].[SaleChequeReplacements] ([NewChequeId]);
    CREATE INDEX [IX_SaleChequeReplacements_TransactionId] ON [dbo].[SaleChequeReplacements] ([TransactionId]);
END";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS ""SaleChequeReplacements"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SaleChequeReplacements"" PRIMARY KEY AUTOINCREMENT,
    ""CompanyId"" INTEGER NULL, ""OperationId"" TEXT NOT NULL,
    ""TransactionId"" INTEGER NOT NULL, ""SaleChequeChargeId"" INTEGER NOT NULL,
    ""OldChequeId"" INTEGER NOT NULL, ""NewChequeId"" INTEGER NOT NULL,
    ""Date"" TEXT NOT NULL, ""OldAmount"" TEXT NOT NULL, ""NewAmount"" TEXT NOT NULL,
    ""Reason"" TEXT NULL, ""CreatedBy"" TEXT NULL, ""CreatedAt"" TEXT NOT NULL,
    CONSTRAINT ""FK_SaleChequeReplacements_Transaction"" FOREIGN KEY (""TransactionId"") REFERENCES ""Transactions"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeReplacements_Charge"" FOREIGN KEY (""SaleChequeChargeId"") REFERENCES ""SaleChequeCharges"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeReplacements_OldCheque"" FOREIGN KEY (""OldChequeId"") REFERENCES ""Cheques"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeReplacements_NewCheque"" FOREIGN KEY (""NewChequeId"") REFERENCES ""Cheques"" (""Id"") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReplacements_OperationId"" ON ""SaleChequeReplacements"" (""OperationId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReplacements_OldChequeId"" ON ""SaleChequeReplacements"" (""OldChequeId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReplacements_NewChequeId"" ON ""SaleChequeReplacements"" (""NewChequeId"");
CREATE INDEX IF NOT EXISTS ""IX_SaleChequeReplacements_TransactionId"" ON ""SaleChequeReplacements"" (""TransactionId"");";

    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
