using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// Add ownership to treasury documents without assigning ambiguous multi-company history.
/// A link from a shop sale is authoritative only when every linked sale agrees and
/// the existing party/accounts do not contradict that ownership. Standalone legacy
/// documents are assigned only when exactly one active company exists.
/// </summary>
public static class TreasuryCompanyScopeSchemaV1
{
    public const string SqlServerColumns = @"
IF OBJECT_ID(N'dbo.TrsVouchers', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.TrsVouchers', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[TrsVouchers] ADD [CompanyId] int NULL;
IF OBJECT_ID(N'dbo.TrsCheques', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.TrsCheques', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[TrsCheques] ADD [CompanyId] int NULL;
IF OBJECT_ID(N'dbo.TrsVouchers', N'U') IS NOT NULL AND NOT EXISTS
    (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'dbo.TrsVouchers') AND [name] = N'IX_TrsVouchers_CompanyId')
    EXEC(N'CREATE INDEX [IX_TrsVouchers_CompanyId] ON [dbo].[TrsVouchers] ([CompanyId]);');
IF OBJECT_ID(N'dbo.TrsCheques', N'U') IS NOT NULL AND NOT EXISTS
    (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'dbo.TrsCheques') AND [name] = N'IX_TrsCheques_CompanyId')
    EXEC(N'CREATE INDEX [IX_TrsCheques_CompanyId] ON [dbo].[TrsCheques] ([CompanyId]);');";

    private sealed record ParentLink(int VoucherId, int? CompanyId, int? ParentCompanyId, int? PartyId);

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            foreach (var table in new[] { "TrsVouchers", "TrsCheques" })
            {
                var connection = db.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open) await connection.OpenAsync();
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info([{table}]);";
                var names = new List<string>();
                await using (var reader = await cmd.ExecuteReaderAsync())
                    while (await reader.ReadAsync()) names.Add(reader.GetString(1));
                if (names.Count == 0)
                    throw new InvalidOperationException($"جدول خزانه {table} یافت نشد.");
                if (!names.Contains("CompanyId", StringComparer.OrdinalIgnoreCase))
                    await db.Database.ExecuteSqlRawAsync($"ALTER TABLE [{table}] ADD COLUMN [CompanyId] INTEGER NULL;");
                await db.Database.ExecuteSqlRawAsync($"CREATE INDEX IF NOT EXISTS [IX_{table}_CompanyId] ON [{table}] ([CompanyId]);");
            }
        }
        else await db.Database.ExecuteSqlRawAsync(SqlServerColumns);
        await BackfillAsync(db);
    }

    /// <summary>Null-only, conflict-checked repair; safe to run again after creating the first company.</summary>
    public static async Task BackfillAsync(AppDbContext db)
    {
        var ownTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownTransaction ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;
        var companies = await db.SystemCompanies.AsNoTracking().Select(c => new { c.Id, c.IsActive }).ToListAsync();
        int? soleCompany = companies.Count == 1 && companies[0].IsActive ? companies[0].Id : null;
        var multi = companies.Count > 1;
        var links = new List<ParentLink>();
        var charges = await db.SaleChequeCharges.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.InitialCashTrsVoucherId.HasValue)
            .Select(c => new { VoucherId = c.InitialCashTrsVoucherId!.Value, c.CompanyId, Parent = c.Transaction.CompanyId, c.Transaction.PartyId }).ToListAsync();
        links.AddRange(charges.Select(c => new ParentLink(c.VoucherId, c.CompanyId, c.Parent, c.PartyId)));
        var refunds = await db.SaleChequeCreditSettlements.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.TrsVoucherId.HasValue)
            .Select(c => new { VoucherId = c.TrsVoucherId!.Value, c.CompanyId, Parent = c.SaleTransaction.CompanyId, c.SaleTransaction.PartyId }).ToListAsync();
        links.AddRange(refunds.Select(c => new ParentLink(c.VoucherId, c.CompanyId, c.Parent, c.PartyId)));
        var cancellations = await db.SaleChequeCancellations.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.RefundTrsVoucherId.HasValue)
            .Select(c => new { VoucherId = c.RefundTrsVoucherId!.Value, c.CompanyId, Parent = c.SaleTransaction.CompanyId, c.SaleTransaction.PartyId }).ToListAsync();
        links.AddRange(cancellations.Select(c => new ParentLink(c.VoucherId, c.CompanyId, c.Parent, c.PartyId)));
        var matchedCash = await db.ShopOrderCashEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.MatchedTrsVoucherId.HasValue)
            .Select(c => new { VoucherId = c.MatchedTrsVoucherId!.Value, c.CompanyId, c.PartyId }).ToListAsync();
        links.AddRange(matchedCash.Select(c => new ParentLink(c.VoucherId, c.CompanyId, c.CompanyId, c.PartyId)));
        var byVoucher = links.ToLookup(x => x.VoucherId);
        var linkedIds = links.Select(x => x.VoucherId).Distinct().ToList();
        var query = db.TrsVouchers.IgnoreQueryFilters().AsNoTracking().Where(v => v.CompanyId == null);
        // EF 8.0.1 cannot translate an empty inline collection on all providers.
        var vouchers = !soleCompany.HasValue && linkedIds.Count == 0
            ? new List<TrsVoucher>()
            : await (soleCompany.HasValue ? query : query.Where(v => linkedIds.Contains(v.Id))).Include(v => v.Lines).ToListAsync();
        var accountOwners = await db.TrsAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.CompanyId);
        var partyOwners = await db.Parties.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.CompanyId);
        var chequeOwners = await db.TrsCheques.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.CompanyId);
        bool AccountMatches(int id, int company) => accountOwners.TryGetValue(id, out var owner) &&
            (owner == company || (!multi && owner == null));
        bool PartyMatches(int id, int company) => partyOwners.TryGetValue(id, out var owner) &&
            (owner == company || (!multi && owner == null));

        foreach (var voucher in vouchers)
        {
            var parents = byVoucher[voucher.Id].ToList();
            int? candidate = parents.Count == 0 ? soleCompany : parents[0].ParentCompanyId;
            if (candidate is not > 0 || !companies.Any(c => c.Id == candidate)) continue;
            if (parents.Any(p => p.CompanyId != candidate || p.ParentCompanyId != candidate || p.PartyId != voucher.PartyId)) continue;
            if (voucher.PartyId is > 0 && !PartyMatches(voucher.PartyId.Value, candidate.Value)) continue;
            var accounts = voucher.Lines.Where(l => l.TrsAccountId is > 0).Select(l => l.TrsAccountId!.Value)
                .Concat(new[] { voucher.FromAccountId, voucher.ToAccountId }.Where(x => x is > 0).Select(x => x!.Value));
            if (accounts.Any(id => !AccountMatches(id, candidate.Value))) continue;
            if (voucher.Lines.Any(l => l.ChequeId is > 0 &&
                (!chequeOwners.TryGetValue(l.ChequeId.Value, out var owner) || (owner.HasValue && owner != candidate)))) continue;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE TrsVouchers SET CompanyId = {candidate} WHERE Id = {voucher.Id} AND CompanyId IS NULL");
        }

        // A linked cheque is assigned only if *all* voucher links agree. A
        // standalone cheque remains unowned in a multi-company database.
        var allVouchers = await db.TrsVouchers.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(v => v.Id, v => v.CompanyId);
        var chequeLinks = (await db.TrsVoucherLines.IgnoreQueryFilters().AsNoTracking().Where(l => l.ChequeId != null)
            .Select(l => new { ChequeId = l.ChequeId!.Value, l.TrsVoucherId }).ToListAsync()).ToLookup(x => x.ChequeId);
        var cheques = await db.TrsCheques.IgnoreQueryFilters().AsNoTracking().Where(c => c.CompanyId == null).ToListAsync();
        foreach (var cheque in cheques)
        {
            var ids = chequeLinks[cheque.Id].Select(x => x.TrsVoucherId)
                .Concat(cheque.TrsVoucherId.HasValue ? new[] { cheque.TrsVoucherId.Value } : Array.Empty<int>()).Distinct().ToList();
            int? candidate = ids.Count == 0 ? soleCompany :
                allVouchers.TryGetValue(ids[0], out var owner) ? owner : null;
            if (candidate is not > 0 || ids.Any(id => !allVouchers.TryGetValue(id, out var linkedOwner) || linkedOwner != candidate)) continue;
            if (cheque.PartyId is > 0 && !PartyMatches(cheque.PartyId.Value, candidate.Value)) continue;
            if (cheque.TrsAccountId is > 0 && !AccountMatches(cheque.TrsAccountId.Value, candidate.Value)) continue;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE TrsCheques SET CompanyId = {candidate} WHERE Id = {cheque.Id} AND CompanyId IS NULL");
        }
        if (transaction is not null) await transaction.CommitAsync();
    }
}
