using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

/// <summary>دریافت/پرداخت نسیه با حساب واقعی و رویداد معکوس؛ بدون سند یا تنظیمات ERP.</summary>
public static class ShopOrderCreditPosting
{
    private const decimal MaxMoney = 9999999999999999.99m;

    private static void RequireSerializable(AppDbContext db)
    {
        var tx = db.Database.CurrentTransaction ?? throw new InvalidOperationException("عملیات نسیه باید در تراکنش فاکتور انجام شود.");
        if (tx.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("تراکنش نسیه باید سطح Serializable داشته باشد.");
    }

    private static async Task<Transaction> LoadOrderAsync(AppDbContext db, int id)
    {
        var order = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new InvalidOperationException("فاکتور یافت نشد.");
        if (order.Type is not (TransactionType.Sale or TransactionType.Purchase) || order.PartyId is not > 0 ||
            db.ActiveCompanyId == 0 || (db.ActiveCompanyId is > 0 && order.CompanyId != db.ActiveCompanyId))
            throw new InvalidOperationException("نوع، طرف حساب یا شرکت فاکتور معتبر نیست.");
        return order;
    }

    private static async Task EnsureOrderAsync(AppDbContext db, Transaction order)
    {
        var charge = await db.SaleChequeCharges.AsNoTracking().FirstOrDefaultAsync(c => c.TransactionId == order.Id);
        if (charge is null && order.ShopCashLedgerEnabled) await ShopOrderCashLedger.VerifyCurrentAsync(db, order);
        if (charge is null && !order.ShopCashLedgerEnabled && order.CashAmount > 0)
            throw new InvalidOperationException("نقد فاکتور قدیمی ابتدا باید به‌صورت صریح تطبیق شود؛ تاریخ یا حساب آن حدس زده نمی‌شود.");
        await SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync(db, order);
    }

    public static async Task<bool> HasLaterEventsAsync(AppDbContext db, int id, DateTime date)
        => await db.SaleChequeChargeEvents.AnyAsync(e => e.SaleChequeCharge.TransactionId == id && e.Date > date) ||
           await db.SaleChequeReturns.AnyAsync(r => r.SaleTransactionId == id && r.Date > date) ||
           await db.SaleChequeCancellations.AnyAsync(r => r.SaleTransactionId == id && r.Date > date) ||
           await db.SaleChequeReplacements.AnyAsync(r => r.TransactionId == id && r.Date > date) ||
           await db.SaleChequeCreditSettlements.AnyAsync(s => (s.SaleTransactionId == id || s.TargetSaleTransactionId == id) && s.Date > date) ||
           await db.SaleChequeReceipts.AnyAsync(r => r.TransactionId == id && (r.Date > date || r.ReversalDate > date)) ||
           await db.SaleChequeInstallmentReceipts.AnyAsync(r => r.TransactionId == id && (r.Date > date || r.ReversalDate > date)) ||
           await db.ShopOrderCreditReceipts.AnyAsync(r => r.TransactionId == id && (r.Date > date || r.ReversalDate > date)) ||
           await db.ShopTradeInUses.AnyAsync(u => u.TargetSaleId == id && u.Date > date) ||
           await db.ShopExpenseCheques.AnyAsync(c => c.SourceTransactionId == id && (c.HandedAt > date || c.SettledAt > date));

    private static async Task<(decimal Outstanding, decimal Cheques, decimal Installments, decimal Available)> CalculateAsync(AppDbContext db, Transaction order)
    {
        var cheques = await db.Cheques.IgnoreQueryFilters().AsNoTracking().Where(c => c.TransactionId == order.Id).ToListAsync();
        var installments = await db.Installments.IgnoreQueryFilters().AsNoTracking().Where(i => i.TransactionId == order.Id).ToListAsync();
        if (cheques.Any(c => c.CompanyId != order.CompanyId || c.Amount <= 0 ||
                c.IsCleared != (c.Status == ChequeStatus.Cleared)) ||
            installments.Any(i => i.CompanyId != order.CompanyId || i.Amount <= 0))
            throw new InvalidOperationException("شرکت یا وضعیت چک‌ها و اقساط فاکتور نیازمند بررسی است.");
        var externallyCleared = await db.ShopExpenseCheques.AsNoTracking().Where(c => c.SourceTransactionId == order.Id &&
            c.Kind == ExpenseChequeKind.Received && c.State == ExpenseChequeState.Cleared && c.SourceChequeId.HasValue)
            .Select(c => c.SourceChequeId!.Value).ToListAsync();
        var chequeAmount = cheques.Where(c => !c.IsCleared && c.Status != ChequeStatus.Cancelled && !externallyCleared.Contains(c.Id)).Sum(c => c.Amount);
        var installmentAmount = installments.Where(i => !i.IsPaid).Sum(i => i.Amount);
        var outstanding = await SaleChequeSettlementLedger.UnpaidSaleBalanceAsync(db, order);
        return (outstanding, chequeAmount, installmentAmount, Math.Max(0m, outstanding - chequeAmount - installmentAmount));
    }

    public static async Task<ShopOrderCreditState> GetStateAsync(AppDbContext db, int orderId)
    {
        RequireSerializable(db);
        var order = await LoadOrderAsync(db, orderId);
        await EnsureOrderAsync(db, order);
        var cancelled = await db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == order.Id);
        var amounts = await CalculateAsync(db, order);
        var receipts = await db.ShopOrderCreditReceipts.AsNoTracking().Include(r => r.TrsAccount)
            .Where(r => r.TransactionId == order.Id).OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToListAsync();
        return new ShopOrderCreditState
        {
            IsCancelled = cancelled,
            Outstanding = amounts.Outstanding,
            ReservedForCheques = amounts.Cheques,
            ReservedForInstallments = amounts.Installments,
            AvailableCredit = cancelled ? 0m : amounts.Available,
            Receipts = receipts.Select(r => new ShopOrderCreditReceiptDto
            {
                Id = r.Id, Amount = r.Amount, Date = r.Date, TrsAccountId = r.TrsAccountId,
                AccountName = r.TrsAccount.Name, CreatedBy = r.CreatedBy,
                ReversalDate = r.ReversalDate, ReversedBy = r.ReversedBy
            }).ToList()
        };
    }

    public static async Task ReceiveAsync(AppDbContext db, int orderId, ShopOrderCreditReceiptCommand cmd, string? user)
    {
        RequireSerializable(db);
        if (cmd.OperationId == Guid.Empty || cmd.TrsAccountId <= 0 || cmd.Date == default ||
            cmd.Amount <= 0 || cmd.Amount > MaxMoney || decimal.Round(cmd.Amount, 2) != cmd.Amount || user?.Length > 120)
            throw new InvalidOperationException("شناسه عملیات، مبلغ، تاریخ یا حساب نسیه معتبر نیست.");
        var date = cmd.Date.Date;
        // Ignore filters for globally unique IDs, but never reveal or reuse another company's operation.
        var prior = await db.ShopOrderCreditReceipts.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.OperationId == cmd.OperationId);
        if (prior is not null)
        {
            var existingOrder = await LoadOrderAsync(db, orderId);
            if (prior.CompanyId != existingOrder.CompanyId || prior.TransactionId != orderId || prior.TrsAccountId != cmd.TrsAccountId ||
                prior.Amount != cmd.Amount || prior.Date.Date != date)
                throw new InvalidOperationException("شناسه عملیات قبلاً با اطلاعات دیگری استفاده شده است.");
            return;
        }
        if (await db.ShopOrderCreditReceipts.IgnoreQueryFilters().AnyAsync(r => r.ReversalOperationId == cmd.OperationId))
            throw new InvalidOperationException("شناسه عملیات قبلاً برای لغو استفاده شده است.");

        var order = await LoadOrderAsync(db, orderId);
        if (date < order.Date.Date || await HasLaterEventsAsync(db, order.Id, date))
            throw new InvalidOperationException("تاریخ نسیه نمی‌تواند پیش از فاکتور یا آخرین گردش ثبت‌شده آن باشد.");
        if (await db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == order.Id))
            throw new InvalidOperationException("فاکتور ابطال شده است.");
        await EnsureOrderAsync(db, order);
        var amounts = await CalculateAsync(db, order);
        if (cmd.Amount > amounts.Available)
            throw new InvalidOperationException("مبلغ از مانده نسیه آزاد بیشتر است؛ چک‌ها و اقساط پرداخت‌نشده جداگانه تسویه می‌شوند.");
        if (order.SettledAmount + cmd.Amount > MaxMoney)
            throw new InvalidOperationException("جمع تسویه از محدوده مجاز بیشتر است.");

        var account = await db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cmd.TrsAccountId && a.IsActive)
            ?? throw new InvalidOperationException("حساب صندوق/بانک فعال یافت نشد.");
        await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(db, account, order.CompanyId);
        if (order.Type == TransactionType.Purchase)
        {
            var balance = await TreasuryBalanceCalculator.GetAmountAsync(db, account.Id);
            if (!balance.HasValue || balance.Value < cmd.Amount)
                throw new InvalidOperationException("مانده صندوق/بانک برای پرداخت نسیه خرید کافی نیست.");
            await TreasuryBalanceCalculator.EnsureAvailableOnAndAfterAsync(db, account.Id, date, cmd.Amount);
        }
        var party = await db.Parties.AsNoTracking().Where(p => p.Id == order.PartyId)
            .Select(p => new { p.Name, p.CompanyId }).FirstOrDefaultAsync();
        if (party is null || party.CompanyId != order.CompanyId)
            throw new InvalidOperationException("طرف حساب فاکتور یافت نشد یا متعلق به شرکت آن نیست.");
        db.ShopOrderCreditReceipts.Add(new ShopOrderCreditReceipt
        {
            CompanyId = order.CompanyId, OperationId = cmd.OperationId, TransactionId = order.Id,
            OrderNumber = order.Number, OrderType = order.Type, PartyId = order.PartyId!.Value, PartyName = party.Name,
            TrsAccountId = account.Id, Amount = cmd.Amount, Date = date, CreatedBy = user, CreatedAt = DateTime.Now
        });
        order.SettledAmount += cmd.Amount;
        await db.SaveChangesAsync();
    }

    public static async Task ReverseAsync(AppDbContext db, int orderId, int receiptId, ShopOrderCreditReversalCommand cmd, string? user)
    {
        RequireSerializable(db);
        if (cmd.OperationId == Guid.Empty || cmd.Date == default || user?.Length > 120)
            throw new InvalidOperationException("شناسه یا تاریخ لغو معتبر نیست.");
        var date = cmd.Date.Date;
        var prior = await db.ShopOrderCreditReceipts.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.ReversalOperationId == cmd.OperationId);
        if (prior is not null)
        {
            var existingOrder = await LoadOrderAsync(db, orderId);
            if (prior.CompanyId != existingOrder.CompanyId || prior.TransactionId != orderId || prior.Id != receiptId || prior.ReversalDate != date)
                throw new InvalidOperationException("شناسه لغو قبلاً با اطلاعات دیگری استفاده شده است.");
            return;
        }
        if (await db.ShopOrderCreditReceipts.IgnoreQueryFilters().AnyAsync(r => r.OperationId == cmd.OperationId))
            throw new InvalidOperationException("شناسه عملیات قبلاً برای دریافت استفاده شده است.");
        var order = await LoadOrderAsync(db, orderId);
        var receipt = await db.ShopOrderCreditReceipts.FirstOrDefaultAsync(r => r.Id == receiptId && r.TransactionId == orderId)
            ?? throw new InvalidOperationException("گردش نسیه یافت نشد.");
        if (receipt.ReversalDate.HasValue) throw new InvalidOperationException("این گردش قبلاً لغو شده است.");
        if (date < receipt.Date.Date || await HasLaterEventsAsync(db, order.Id, date))
            throw new InvalidOperationException("تاریخ لغو نمی‌تواند پیش از دریافت یا آخرین گردش ثبت‌شده فاکتور باشد.");
        if (await db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == order.Id))
            throw new InvalidOperationException("فاکتور ابطال شده است.");
        await EnsureOrderAsync(db, order);
        if (order.SettledAmount < receipt.Amount) throw new InvalidOperationException("تاریخچه تسویه فاکتور ناسازگار است.");

        // A previously consumed/refunded customer credit must not be invalidated by reversing its source.
        var outbound = (await db.SaleChequeCreditSettlements.AsNoTracking()
            .Where(s => s.SaleTransactionId == order.Id).Select(s => s.Amount).ToListAsync()).Sum();
        if (outbound > 0)
        {
            var charge = await db.SaleChequeCharges.AsNoTracking().FirstOrDefaultAsync(c => c.TransactionId == order.Id)
                ?? throw new InvalidOperationException("گردش بستانکاری فاکتور نامعتبر است.");
            var signedNow = await SaleChequeSettlementLedger.SignedChargedSaleBalanceAsync(db, order, charge);
            var rawCreditAfterReversal = Math.Max(0m, -(signedNow - outbound + receipt.Amount));
            if (outbound > rawCreditAfterReversal)
                throw new InvalidOperationException("از محل این دریافت قبلاً بستانکاری مشتری پرداخت یا مصرف شده است و لغو آن مجاز نیست.");
        }

        var account = await db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == receipt.TrsAccountId && a.IsActive)
            ?? throw new InvalidOperationException("حساب اولیه گردش فعال نیست یا تغییر کرده است.");
        await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(db, account, order.CompanyId);
        if (order.Type == TransactionType.Sale)
        {
            var balance = await TreasuryBalanceCalculator.GetAmountAsync(db, account.Id);
            if (!balance.HasValue || balance.Value < receipt.Amount)
                throw new InvalidOperationException("مانده صندوق/بانک برای لغو دریافت نسیه کافی نیست.");
            await TreasuryBalanceCalculator.EnsureAvailableOnAndAfterAsync(db, account.Id, date, receipt.Amount);
        }
        receipt.ReversalOperationId = cmd.OperationId;
        receipt.ReversalDate = date;
        receipt.ReversedAt = DateTime.Now;
        receipt.ReversedBy = user;
        order.SettledAmount -= receipt.Amount;
        await db.SaveChangesAsync();
    }
}
