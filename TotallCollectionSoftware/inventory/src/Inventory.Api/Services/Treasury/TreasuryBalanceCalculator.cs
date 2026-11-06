using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Services.Invoicing;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
        if (db.ActiveCompanyId == 0)
            throw new InvalidOperationException("شرکت فعال برای محاسبه مانده حساب مشخص نیست.");
        var accounts = await db.TrsAccounts.AsNoTracking().Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.CompanyId, a.OpeningBalance }).ToListAsync();
        var lines = await db.TrsVoucherLines.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.TrsAccountId != null && ids.Contains(l.TrsAccountId.Value) &&
                        l.TrsVoucher!.Status == TreasuryStatus.Confirmed &&
                        (l.TrsVoucher.Kind == TreasuryKind.Receipt || l.TrsVoucher.Kind == TreasuryKind.Payment) &&
                        l.Method != PayMethod.Cheque && l.Method != PayMethod.Discount)
            .Select(l => new { AccountId = l.TrsAccountId!.Value, l.TrsVoucherId, l.TrsVoucher!.Kind, l.TrsVoucher.CompanyId, l.Amount }).ToListAsync();
        var transfers = await db.TrsVouchers.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.Kind == TreasuryKind.Transfer && v.Status == TreasuryStatus.Confirmed &&
                        ((v.FromAccountId != null && ids.Contains(v.FromAccountId.Value)) ||
                         (v.ToAccountId != null && ids.Contains(v.ToAccountId.Value))))
            .Select(v => new { v.FromAccountId, v.ToAccountId, v.TotalAmount, v.FeeAmount, v.CompanyId,
                FromCompany = v.FromAccount != null ? v.FromAccount.CompanyId : null,
                ToCompany = v.ToAccount != null ? v.ToAccount.CompanyId : null }).ToListAsync();
        var cheques = await db.TrsCheques.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Status == ChequeStatus.Cleared && c.TrsAccountId != null && ids.Contains(c.TrsAccountId.Value))
            .Select(c => new { AccountId = c.TrsAccountId!.Value, c.Kind, c.CompanyId, c.Amount }).ToListAsync();
        var orderCash = await db.ShopOrderCashEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => ids.Contains(e.TrsAccountId)).ToListAsync();
        await ShopOrderCashLedger.EnsureMatchedVouchersValidAsync(db, orderCash);
        var trackedOrders = await db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.ShopCashLedgerEnabled && t.CashAmount > 0 && t.TrsAccountId.HasValue && ids.Contains(t.TrsAccountId.Value)).ToListAsync();
        if (trackedOrders.Any(t => orderCash.Count(e => e.TransactionId == t.Id && !e.ReversalDate.HasValue &&
                e.CompanyId == t.CompanyId && e.Amount == t.CashAmount && e.TrsAccountId == t.TrsAccountId) != 1 || t.CashAmount > t.SettledAmount))
            throw new InvalidOperationException("گردش نقدی فعال فاکتور در مانده حساب یافت نشد یا با فاکتور مطابقت ندارد.");
        var activeOrderIds = orderCash.Where(e => e.ReversalDate == null).Select(e => e.TransactionId).Distinct().ToList();
        var activeOrders = activeOrderIds.Count == 0 ? new List<Transaction>() :
            await db.Transactions.IgnoreQueryFilters().AsNoTracking().Where(t => activeOrderIds.Contains(t.Id)).ToListAsync();
        var ordersById = activeOrders.ToDictionary(t => t.Id);
        if (orderCash.Any(e => e.OperationId == Guid.Empty || e.Amount <= 0 || e.Date == default ||
            e.OrderType is not (TransactionType.Sale or TransactionType.Purchase) ||
            e.ReversalDate.HasValue != e.ReversalOperationId.HasValue || e.ReversalOperationId == Guid.Empty ||
            (e.ReversalDate.HasValue && e.ReversalDate.Value.Date < e.Date.Date) ||
            (!e.ReversalDate.HasValue && (!ordersById.TryGetValue(e.TransactionId, out var order) ||
                !order.ShopCashLedgerEnabled || order.CompanyId != e.CompanyId || order.Type != e.OrderType ||
                order.Number != e.OrderNumber || order.PartyId != e.PartyId || order.CashAmount != e.Amount ||
                order.TrsAccountId != e.TrsAccountId || order.CashType != e.CashType || order.CashTrackingNumber != e.Reference))))
            throw new InvalidOperationException("تاریخچه نقدی فاکتور با گردش صندوق/بانک مطابقت ندارد.");
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
        var creditReceipts = await db.ShopOrderCreditReceipts.IgnoreQueryFilters().AsNoTracking()
            .Where(r => ids.Contains(r.TrsAccountId))
            .Select(r => new { r.TrsAccountId, r.CompanyId, r.OperationId, r.Amount, r.Date, r.ReversalDate,
                r.ReversalOperationId, r.ReversedAt, r.OrderType, r.OrderNumber, r.PartyId,
                ParentCompany = r.Transaction.CompanyId, ParentType = r.Transaction.Type,
                ParentNumber = r.Transaction.Number, ParentParty = r.Transaction.PartyId, ParentDate = r.Transaction.Date }).ToListAsync();
        // A bank's balance must include *all* movements on that bank, not only
        // rows visible through the current invoice query filter.  If a historical
        // movement contradicts explicit multi-company ownership, fail closed.
        if (db.ActiveCompanyId is > 0)
        {
            var owners = accounts.ToDictionary(a => a.Id, a => a.CompanyId);
            var active = db.ActiveCompanyId.Value;
            var multi = await db.SystemCompanies.CountAsync() > 1;
            bool Matches(int? accountOwner, int? documentOwner) => documentOwner == active &&
                (accountOwner == active || (!multi && accountOwner == null));
            if (accounts.Count != ids.Count || accounts.Any(a => a.CompanyId != active && (multi || a.CompanyId.HasValue)))
                throw new InvalidOperationException("حساب صندوق/بانک متعلق به شرکت جاری نیست یا مالک آن مشخص نیست.");
            if (lines.Any(l => !owners.TryGetValue(l.AccountId, out var owner) || !Matches(owner, l.CompanyId)) ||
                cheques.Any(c => !owners.TryGetValue(c.AccountId, out var owner) || !Matches(owner, c.CompanyId)) ||
                transfers.Any(t => t.FromAccountId is not > 0 || t.ToAccountId is not > 0 ||
                    !Matches(t.FromCompany, t.CompanyId) || !Matches(t.ToCompany, t.CompanyId)) ||
                orderCash.Any(e => !owners.TryGetValue(e.TrsAccountId, out var owner) || !Matches(owner, e.CompanyId)))
                throw new InvalidOperationException("مالک بخشی از گردش خزانه مشخص نیست یا با شرکت حساب مطابقت ندارد؛ پیش از محاسبه مانده تاریخچه را تطبیق دهید.");
            if (saleReceipts.Any(r => !owners.TryGetValue(r.TrsAccountId, out var owner) || !Matches(owner, r.CompanyId)) ||
                installments.Any(r => !owners.TryGetValue(r.TrsAccountId, out var owner) || !Matches(owner, r.CompanyId)) ||
                creditReceipts.Any(r => !owners.TryGetValue(r.TrsAccountId, out var owner) || !Matches(owner, r.CompanyId)))
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
        if (saleReceipts.Any(r => r.OperationId == Guid.Empty || r.Amount <= 0 || r.SaleType is not (TransactionType.Sale or TransactionType.Purchase) ||
            r.CompanyId != r.SaleCompany || r.ChequeCompany != r.SaleCompany || r.ChequeTransaction != r.TransactionId ||
            r.Date.Date < r.SaleDate.Date || r.ReversalDate.HasValue != r.ReversalOperationId.HasValue ||
            (r.ReversalDate.HasValue && r.ReversalDate.Value.Date < r.Date.Date) || r.ReversalOperationId == Guid.Empty ||
            (!r.ReversalDate.HasValue && (!r.IsCleared || r.ChequeStatus != ChequeStatus.Cleared || r.ChequeAmount != r.Amount || r.ChequeAccount != r.TrsAccountId))) ||
            installments.Any(r => r.OperationId == Guid.Empty || r.Amount <= 0 || r.SaleType is not (TransactionType.Sale or TransactionType.Purchase) ||
            r.CompanyId != r.SaleCompany || r.InstallmentCompany != r.SaleCompany || r.InstallmentTransaction != r.TransactionId ||
            r.Date.Date < r.SaleDate.Date || r.ReversalDate.HasValue != r.ReversalOperationId.HasValue ||
            (r.ReversalDate.HasValue && r.ReversalDate.Value.Date < r.Date.Date) || r.ReversalOperationId == Guid.Empty ||
            (!r.ReversalDate.HasValue && (!r.IsPaid || r.InstallmentAmount != r.Amount))))
            throw new InvalidOperationException("تاریخچه وصول یا لغو وصول با گردش صندوق/بانک مطابقت ندارد.");

        if (creditReceipts.Any(r => r.OperationId == Guid.Empty || r.Amount <= 0 ||
            r.OrderType is not (TransactionType.Sale or TransactionType.Purchase) || r.CompanyId != r.ParentCompany ||
            r.OrderType != r.ParentType || r.OrderNumber != r.ParentNumber || r.PartyId != r.ParentParty ||
            r.Date == default || r.Date.Date < r.ParentDate.Date ||
            r.ReversalDate.HasValue != r.ReversalOperationId.HasValue || r.ReversalDate.HasValue != r.ReversedAt.HasValue ||
            r.ReversalOperationId == Guid.Empty || (r.ReversalDate.HasValue && r.ReversalDate.Value.Date < r.Date.Date)))
            throw new InvalidOperationException("تاریخچه نسیه با فاکتور یا گردش صندوق/بانک مطابقت ندارد.");

        var result = new Dictionary<int, Balance>();
        foreach (var account in accounts)
        {
            var mine = lines.Where(l => l.AccountId == account.Id).ToList();
            var myCheques = cheques.Where(c => c.AccountId == account.Id).ToList();
            var myReceipts = saleReceipts.Where(r => r.TrsAccountId == account.Id).ToList();
            var myInstallments = installments.Where(r => r.TrsAccountId == account.Id).ToList();
            var myOrderCash = orderCash.Where(e => e.TrsAccountId == account.Id).ToList();
            var myCredit = creditReceipts.Where(r => r.TrsAccountId == account.Id).ToList();
            var totalIn = mine.Where(l => l.Kind == TreasuryKind.Receipt).Sum(l => l.Amount)
                + transfers.Where(t => t.ToAccountId == account.Id).Sum(t => t.TotalAmount)
                + myCheques.Where(c => c.Kind == ChequeKind.Received).Sum(c => c.Amount)
                + myReceipts.Where(r => r.SaleType == TransactionType.Sale).Sum(r => r.Amount)
                + myReceipts.Where(r => r.SaleType == TransactionType.Purchase && r.ReversalDate.HasValue).Sum(r => r.Amount)
                + myInstallments.Where(r => r.SaleType == TransactionType.Sale).Sum(r => r.Amount)
                + myInstallments.Where(r => r.SaleType == TransactionType.Purchase && r.ReversalDate.HasValue).Sum(r => r.Amount)
                + myCredit.Where(r => r.OrderType == TransactionType.Sale).Sum(r => r.Amount)
                + myCredit.Where(r => r.OrderType == TransactionType.Purchase && r.ReversalDate.HasValue).Sum(r => r.Amount)
                + myOrderCash.Where(e => e.OrderType == TransactionType.Sale && !e.MatchedTrsVoucherId.HasValue).Sum(e => e.Amount)
                + myOrderCash.Where(e => e.OrderType == TransactionType.Purchase && e.ReversalDate.HasValue).Sum(e => e.Amount);
            var totalOut = mine.Where(l => l.Kind == TreasuryKind.Payment).Sum(l => l.Amount)
                + transfers.Where(t => t.FromAccountId == account.Id).Sum(t => t.TotalAmount + t.FeeAmount)
                + myCheques.Where(c => c.Kind == ChequeKind.Issued).Sum(c => c.Amount)
                + myReceipts.Where(r => r.SaleType == TransactionType.Sale && r.ReversalDate.HasValue).Sum(r => r.Amount)
                + myReceipts.Where(r => r.SaleType == TransactionType.Purchase).Sum(r => r.Amount)
                + myInstallments.Where(r => r.SaleType == TransactionType.Sale && r.ReversalDate.HasValue).Sum(r => r.Amount)
                + myInstallments.Where(r => r.SaleType == TransactionType.Purchase).Sum(r => r.Amount)
                + myCredit.Where(r => r.OrderType == TransactionType.Purchase).Sum(r => r.Amount)
                + myCredit.Where(r => r.OrderType == TransactionType.Sale && r.ReversalDate.HasValue).Sum(r => r.Amount)
                + myOrderCash.Where(e => e.OrderType == TransactionType.Purchase && !e.MatchedTrsVoucherId.HasValue).Sum(e => e.Amount)
                + myOrderCash.Where(e => e.OrderType == TransactionType.Sale && e.ReversalDate.HasValue).Sum(e => e.Amount);
            var count = mine.Count + myCheques.Count + myReceipts.Count + myReceipts.Count(r => r.ReversalDate.HasValue)
                + myInstallments.Count + myInstallments.Count(r => r.ReversalDate.HasValue)
                + myCredit.Count + myCredit.Count(r => r.ReversalDate.HasValue)
                + transfers.Count(t => t.FromAccountId == account.Id || t.ToAccountId == account.Id)
                + myOrderCash.Count(e => !e.MatchedTrsVoucherId.HasValue) + myOrderCash.Count(e => e.ReversalDate.HasValue);
            result.Add(account.Id, new Balance(account.OpeningBalance, totalIn, totalOut, count));
        }
        return result;
    }

    /// <summary>
    /// یک خروجی تازه نباید مانده را در روز خودش یا هیچ روز ثبت‌شده بعدی منفی کند.
    /// مبلغ صفر برای کنترل خروجی‌ای است که همین حالا داخل تراکنش ذخیره شده است.
    /// </summary>
    public static async Task EnsureAvailableOnAndAfterAsync(AppDbContext db, int accountId, DateTime date, decimal newDebit)
    {
        var transaction = db.Database.CurrentTransaction;
        if (transaction is null || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable ||
            accountId <= 0 || date == default || newDebit < 0)
            throw new InvalidOperationException("کنترل تاریخ‌دار مانده باید در تراکنش معتبر فروشگاه انجام شود.");
        var balances = await CalculateAsync(db, new[] { accountId });
        if (!balances.TryGetValue(accountId, out var balance))
            throw new InvalidOperationException("حساب صندوق/بانک برای کنترل مانده یافت نشد.");
        // A historic status-only treasury cheque has no trustworthy clearing date.
        // Never substitute its due date when deciding whether a backdated payment is safe.
        if (await db.TrsCheques.IgnoreQueryFilters().AnyAsync(c => c.TrsAccountId == accountId &&
                c.Status == ChequeStatus.Cleared && c.StatusDate == null))
            throw new InvalidOperationException("تاریخ وصول بخشی از گردش قدیمی حساب مشخص نیست؛ پیش از پرداخت تاریخچه را بررسی کنید.");
        var rows = await TreasuryService.CollectFlowAsync(db, accountId);
        if (rows.Any(r => r.Date == default) || balance.Opening + rows.Sum(r => r.In - r.Out) != balance.Amount)
            throw new InvalidOperationException("گردش تاریخ‌دار حساب با مانده صندوق/بانک مطابقت ندارد.");
        var day = date.Date;
        var running = balance.Opening + rows.Where(r => r.Date.Date <= day).Sum(r => r.In - r.Out);
        if (running < newDebit)
            throw new InvalidOperationException("مانده صندوق/بانک در تاریخ عملیات برای این پرداخت کافی نیست.");
        foreach (var group in rows.Where(r => r.Date.Date > day).GroupBy(r => r.Date.Date).OrderBy(g => g.Key))
        {
            running += group.Sum(r => r.In - r.Out);
            if (running < newDebit)
                throw new InvalidOperationException("این پرداخت باعث منفی‌شدن مانده صندوق/بانک در گردش‌های بعدی می‌شود.");
        }
    }

    public static async Task<decimal?> GetAmountAsync(AppDbContext db, int accountId)
    {
        var balances = await CalculateAsync(db, new[] { accountId });
        return balances.TryGetValue(accountId, out var balance) ? balance.Amount : null;
    }
}
