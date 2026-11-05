using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Treasury;

/// <summary>
/// مانده عملیاتی صندوق/بانک از گردش‌های واقعی، بدون مراجعه به کدینگ یا سال مالی ERP.
/// هم صفحه صندوق و هم کنترل پرداخت‌های فروشگاهی از این منبع واحد استفاده می‌کنند.
/// </summary>
public static class TreasuryBalanceCalculator
{
    public sealed record Balance(decimal Opening, decimal TotalIn, decimal TotalOut, int MoveCount)
    {
        public decimal Amount => Opening + TotalIn - TotalOut;
    }

    public static async Task<Dictionary<int, Balance>> CalculateAsync(AppDbContext db, IReadOnlyCollection<int> accountIds)
    {
        var ids = accountIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var accounts = await db.TrsAccounts.AsNoTracking().Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.CompanyId, a.OpeningBalance }).ToListAsync();
        var lines = await db.TrsVoucherLines.AsNoTracking()
            .Where(l => l.TrsAccountId != null && ids.Contains(l.TrsAccountId.Value) &&
                        l.TrsVoucher!.Status == TreasuryStatus.Confirmed &&
                        (l.TrsVoucher.Kind == TreasuryKind.Receipt || l.TrsVoucher.Kind == TreasuryKind.Payment) &&
                        l.Method != PayMethod.Cheque && l.Method != PayMethod.Discount)
            .Select(l => new { AccountId = l.TrsAccountId!.Value, l.TrsVoucherId, l.TrsVoucher!.Kind, l.Amount }).ToListAsync();
        var transfers = await db.TrsVouchers.AsNoTracking()
            .Where(v => v.Kind == TreasuryKind.Transfer && v.Status == TreasuryStatus.Confirmed &&
                        ((v.FromAccountId != null && ids.Contains(v.FromAccountId.Value)) ||
                         (v.ToAccountId != null && ids.Contains(v.ToAccountId.Value))))
            .Select(v => new { v.FromAccountId, v.ToAccountId, v.TotalAmount, v.FeeAmount }).ToListAsync();
        var cheques = await db.TrsCheques.AsNoTracking()
            .Where(c => c.Status == ChequeStatus.Cleared && c.TrsAccountId != null && ids.Contains(c.TrsAccountId.Value))
            .Select(c => new { AccountId = c.TrsAccountId!.Value, c.Kind, c.Amount }).ToListAsync();
        var saleReceipts = await db.SaleChequeReceipts.IgnoreQueryFilters().AsNoTracking()
            .Where(r => ids.Contains(r.TrsAccountId))
            .Select(r => new { r.TrsAccountId, r.CompanyId, r.OperationId, r.Amount, r.Date, r.ReversalDate, r.ReversalOperationId,
                r.TransactionId, SaleCompany = r.Transaction.CompanyId, SaleType = r.Transaction.Type, SaleDate = r.Transaction.Date,
                ChequeCompany = r.Cheque.CompanyId, ChequeTransaction = r.Cheque.TransactionId,
                ChequeAmount = r.Cheque.Amount, r.Cheque.IsCleared, ChequeStatus = r.Cheque.Status, ChequeAccount = r.Cheque.TrsAccountId }).ToListAsync();
        var installments = await db.SaleChequeInstallmentReceipts.IgnoreQueryFilters().AsNoTracking()
            .Where(r => ids.Contains(r.TrsAccountId))
            .Select(r => new { r.TrsAccountId, r.CompanyId, r.OperationId, r.Amount, r.Date, r.ReversalDate, r.ReversalOperationId,
                r.TransactionId, SaleCompany = r.Transaction.CompanyId, SaleType = r.Transaction.Type, SaleDate = r.Transaction.Date,
                InstallmentCompany = r.Installment.CompanyId, InstallmentTransaction = r.Installment.TransactionId,
                InstallmentAmount = r.Installment.Amount, r.Installment.IsPaid }).ToListAsync();
        // A bank's balance must include *all* movements on that bank, not only
        // rows visible through the current invoice query filter.  If a historical
        // movement contradicts explicit multi-company ownership, fail closed.
        if (db.ActiveCompanyId is > 0)
        {
            var owners = accounts.ToDictionary(a => a.Id, a => a.CompanyId);
            if (saleReceipts.Any(r => owners.TryGetValue(r.TrsAccountId, out var owner) &&
                    ((owner.HasValue && owner != r.CompanyId) || (!owner.HasValue && r.CompanyId != db.ActiveCompanyId))) ||
                installments.Any(r => owners.TryGetValue(r.TrsAccountId, out var owner) &&
                    ((owner.HasValue && owner != r.CompanyId) || (!owner.HasValue && r.CompanyId != db.ActiveCompanyId))))
                throw new InvalidOperationException("شرکت گردش وصول با مالک حساب صندوق/بانک مطابقت ندارد؛ تاریخچه نیازمند بررسی است.");
            // Cash/refund vouchers used by the independent shop are linked to the
            // sale as well. Never include another company's linked cash silently.
            var voucherIds = lines.Select(l => l.TrsVoucherId).Distinct().ToList();
            if (voucherIds.Count > 0)
            {
                var cashLinks = await db.SaleChequeCharges.IgnoreQueryFilters().AsNoTracking()
                    .Where(c => c.InitialCashTrsVoucherId.HasValue && voucherIds.Contains(c.InitialCashTrsVoucherId.Value))
                    .Select(c => new { VoucherId = c.InitialCashTrsVoucherId!.Value, c.CompanyId, ParentCompany = c.Transaction.CompanyId }).ToListAsync();
                var refundLinks = await db.SaleChequeCreditSettlements.IgnoreQueryFilters().AsNoTracking()
                    .Where(c => c.TrsVoucherId.HasValue && voucherIds.Contains(c.TrsVoucherId.Value))
                    .Select(c => new { VoucherId = c.TrsVoucherId!.Value, c.CompanyId, ParentCompany = c.SaleTransaction.CompanyId }).ToListAsync();
                var cancelLinks = await db.SaleChequeCancellations.IgnoreQueryFilters().AsNoTracking()
                    .Where(c => c.RefundTrsVoucherId.HasValue && voucherIds.Contains(c.RefundTrsVoucherId.Value))
                    .Select(c => new { VoucherId = c.RefundTrsVoucherId!.Value, c.CompanyId, ParentCompany = c.SaleTransaction.CompanyId }).ToListAsync();
                foreach (var link in cashLinks.Concat(refundLinks).Concat(cancelLinks))
                {
                    if (link.CompanyId != link.ParentCompany || lines.Where(l => l.TrsVoucherId == link.VoucherId)
                        .Any(l => owners.TryGetValue(l.AccountId, out var owner) &&
                            ((owner.HasValue && owner != link.CompanyId) || (!owner.HasValue && link.CompanyId != db.ActiveCompanyId))))
                        throw new InvalidOperationException("شرکت گردش نقدی با مالک حساب صندوق/بانک مطابقت ندارد؛ تاریخچه نیازمند بررسی است.");
                }
            }
        }
        if (saleReceipts.Any(r => r.OperationId == Guid.Empty || r.Amount <= 0 || r.SaleType != TransactionType.Sale ||
            r.CompanyId != r.SaleCompany || r.ChequeCompany != r.SaleCompany || r.ChequeTransaction != r.TransactionId ||
            r.Date.Date < r.SaleDate.Date || r.ReversalDate.HasValue != r.ReversalOperationId.HasValue ||
            (r.ReversalDate.HasValue && r.ReversalDate.Value.Date < r.Date.Date) || r.ReversalOperationId == Guid.Empty ||
            (!r.ReversalDate.HasValue && (!r.IsCleared || r.ChequeStatus != ChequeStatus.Cleared || r.ChequeAmount != r.Amount || r.ChequeAccount != r.TrsAccountId))) ||
            installments.Any(r => r.OperationId == Guid.Empty || r.Amount <= 0 || r.SaleType != TransactionType.Sale ||
            r.CompanyId != r.SaleCompany || r.InstallmentCompany != r.SaleCompany || r.InstallmentTransaction != r.TransactionId ||
            r.Date.Date < r.SaleDate.Date || r.ReversalDate.HasValue != r.ReversalOperationId.HasValue ||
            (r.ReversalDate.HasValue && r.ReversalDate.Value.Date < r.Date.Date) || r.ReversalOperationId == Guid.Empty ||
            (!r.ReversalDate.HasValue && (!r.IsPaid || r.InstallmentAmount != r.Amount))))
            throw new InvalidOperationException("تاریخچه وصول یا لغو وصول با گردش صندوق/بانک مطابقت ندارد.");

        var result = new Dictionary<int, Balance>();
        foreach (var account in accounts)
        {
            var mine = lines.Where(l => l.AccountId == account.Id).ToList();
            var myCheques = cheques.Where(c => c.AccountId == account.Id).ToList();
            var myReceipts = saleReceipts.Where(r => r.TrsAccountId == account.Id).ToList();
            var myInstallments = installments.Where(r => r.TrsAccountId == account.Id).ToList();
            var totalIn = mine.Where(l => l.Kind == TreasuryKind.Receipt).Sum(l => l.Amount)
                + transfers.Where(t => t.ToAccountId == account.Id).Sum(t => t.TotalAmount)
                + myCheques.Where(c => c.Kind == ChequeKind.Received).Sum(c => c.Amount)
                + myReceipts.Sum(r => r.Amount) + myInstallments.Sum(r => r.Amount);
            var totalOut = mine.Where(l => l.Kind == TreasuryKind.Payment).Sum(l => l.Amount)
                + transfers.Where(t => t.FromAccountId == account.Id).Sum(t => t.TotalAmount + t.FeeAmount)
                + myCheques.Where(c => c.Kind == ChequeKind.Issued).Sum(c => c.Amount)
                + myReceipts.Where(r => r.ReversalDate.HasValue).Sum(r => r.Amount)
                + myInstallments.Where(r => r.ReversalDate.HasValue).Sum(r => r.Amount);
            var count = mine.Count + myCheques.Count + myReceipts.Count + myReceipts.Count(r => r.ReversalDate.HasValue)
                + myInstallments.Count + myInstallments.Count(r => r.ReversalDate.HasValue)
                + transfers.Count(t => t.FromAccountId == account.Id || t.ToAccountId == account.Id);
            result.Add(account.Id, new Balance(account.OpeningBalance, totalIn, totalOut, count));
        }
        return result;
    }

    public static async Task<decimal?> GetAmountAsync(AppDbContext db, int accountId)
    {
        var balances = await CalculateAsync(db, new[] { accountId });
        return balances.TryGetValue(accountId, out var balance) ? balance.Amount : null;
    }
}
