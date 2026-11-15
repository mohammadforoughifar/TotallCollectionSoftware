using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Data;

/// <summary>
/// بازیابی شرکت گردش‌های قدیمی فقط از رکورد والد قطعی آن‌ها. مقدار غیر null هرگز
/// بازنویسی نمی‌شود؛ بدون والد معتبر نیز شرکت جدیدی حدس زده نمی‌شود.
/// بعد از انتساب شرکت به فاکتورها توسط SalesCompanySchemaV1 اجرا شود.
/// </summary>
public static class SaleChequeCompanyOwnershipSchemaV1
{
    private static readonly (string Child, string Key, string Parent)[] Links =
    {
        ("SaleChequeCharges", "TransactionId", "Transactions"),
        ("SaleChequeChargeEvents", "SaleChequeChargeId", "SaleChequeCharges"),
        ("SaleChequeReschedules", "SaleChequeChargeId", "SaleChequeCharges"),
        ("SaleChequeReceipts", "TransactionId", "Transactions"),
        ("SaleChequeStatusEvents", "ChequeId", "Cheques"),
        ("SaleChequeReturns", "SaleTransactionId", "Transactions"),
        ("SaleChequeReturnLines", "SaleChequeReturnId", "SaleChequeReturns"),
        ("SaleChequeReplacements", "TransactionId", "Transactions"),
        ("SaleChequeCreditSettlements", "SaleTransactionId", "Transactions"),
        ("SaleChequeInstallmentReceipts", "TransactionId", "Transactions"),
        ("ShopOrderCreditReceipts", "TransactionId", "Transactions"),
        ("SaleChequeCancellations", "SaleTransactionId", "Transactions")
    };

    public static async Task EnsureAsync(AppDbContext db)
    {
        var ownTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownTransaction ? await db.Database.BeginTransactionAsync() : null;
        foreach (var (child, key, parent) in Links)
        {
            if (db.Database.IsSqlite())
            {
                // Old/partial databases may not contain every financial table yet.
                var connection = db.Database.GetDbConnection();
                await using var exists = connection.CreateCommand();
                exists.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
                exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN (@child, @parent)";
                var childParam = exists.CreateParameter(); childParam.ParameterName = "@child"; childParam.Value = child;
                var parentParam = exists.CreateParameter(); parentParam.ParameterName = "@parent"; parentParam.Value = parent;
                exists.Parameters.Add(childParam); exists.Parameters.Add(parentParam);
                if (Convert.ToInt64(await exists.ExecuteScalarAsync()) != 2) continue;
                // All identifiers come from the fixed Links table above, never from user input.
                await db.Database.ExecuteSqlRawAsync($@"UPDATE [{child}] SET [CompanyId] =
                    (SELECT [CompanyId] FROM [{parent}] WHERE [Id] = [{child}].[{key}])
                    WHERE [CompanyId] IS NULL AND EXISTS
                    (SELECT 1 FROM [{parent}] WHERE [Id] = [{child}].[{key}] AND [CompanyId] IS NOT NULL);");
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync($@"IF OBJECT_ID(N'dbo.{child}', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.{parent}', N'U') IS NOT NULL
                    EXEC(N'UPDATE c SET [CompanyId]=p.[CompanyId] FROM [dbo].[{child}] c JOIN [dbo].[{parent}] p ON p.[Id]=c.[{key}]
                    WHERE c.[CompanyId] IS NULL AND p.[CompanyId] IS NOT NULL;');");
            }
        }
        if (transaction is not null) await transaction.CommitAsync();
    }
}
