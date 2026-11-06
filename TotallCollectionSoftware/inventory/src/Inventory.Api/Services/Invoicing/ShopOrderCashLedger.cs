using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

/// <summary>گردش نقدی فاکتور عادی، مستقل از تنظیمات و اسناد ERP.</summary>
public static class ShopOrderCashLedger
{
    private const decimal MaxMoney = 9999999999999999.99m;

    private static void RequireTransaction(AppDbContext db)
    {
        var tx = db.Database.CurrentTransaction ?? throw new InvalidOperationException("گردش نقدی باید در تراکنش فاکتور ثبت شود.");
        if (tx.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("تراکنش گردش نقدی باید Serializable باشد.");
    }

    public static async Task<ShopOrderCashEntry?> VerifyCurrentAsync(AppDbContext db, Transaction order)
    {
        RequireTransaction(db);
        var rows = await db.ShopOrderCashEntries.IgnoreQueryFilters()
            .Where(e => e.TransactionId == order.Id && e.ReversalDate == null).ToListAsync();
        if (!order.ShopCashLedgerEnabled)
        {
            if (rows.Count != 0) throw new InvalidOperationException("گردش نقدی قدیمی فاکتور نیازمند بررسی است.");
            return null;
        }
        if (order.CashAmount == 0 && rows.Count == 0) return null;
        if (rows.Count != 1) throw new InvalidOperationException("گردش نقدی فعال فاکتور یافت نشد یا تکراری است.");
        var e = rows[0];
        if (order.CashAmount <= 0 || e.Amount != order.CashAmount || e.TrsAccountId != order.TrsAccountId ||
            e.OrderType != order.Type || e.CompanyId != order.CompanyId || e.PartyId != order.PartyId ||
            e.OrderNumber != order.Number || e.CashType != order.CashType || e.Reference != order.CashTrackingNumber ||
            e.OperationId == Guid.Empty || e.ReversalOperationId.HasValue || e.Date == default)
            throw new InvalidOperationException("گردش نقدی فعال با فاکتور مطابقت ندارد؛ سابقه باید بررسی شود.");
        await EnsureMatchedVouchersValidAsync(db, rows);
        return e;
    }

    private static bool Matches(ShopOrderCashEntry e, Transaction order) =>
        e.Amount == order.CashAmount && e.TrsAccountId == order.TrsAccountId && e.CashType == order.CashType &&
        e.PartyId == order.PartyId && e.Reference == order.CashTrackingNumber && e.OrderType == order.Type;

    private static DateTime EffectiveDate(DateTime invoiceDate, DateTime? lastDate = null)
    {
        var date = DateTime.Today;
        if (invoiceDate.Date > date) date = invoiceDate.Date;
        if (lastDate.HasValue && lastDate.Value.Date > date) date = lastDate.Value.Date;
        return date;
    }

    public static async Task RecordInitialAsync(AppDbContext db, Transaction order, string? user)
    {
        RequireTransaction(db);
        if (!order.ShopCashLedgerEnabled) throw new InvalidOperationException("دفتر نقدی فاکتور فعال نیست.");
        if (await db.ShopOrderCashEntries.IgnoreQueryFilters().AnyAsync(e => e.TransactionId == order.Id))
            throw new InvalidOperationException("گردش نقدی اولیه فاکتور قبلاً ثبت شده است.");
        if (order.CashAmount > 0)
        {
            await AddAsync(db, order, order.Date.Date, user);
            await EnsurePurchaseBalanceAsync(db, order);
        }
    }

    private static async Task EnsurePurchaseBalanceAsync(AppDbContext db, Transaction order)
    {
        if (order.Type != TransactionType.Purchase || order.CashAmount <= 0 || order.TrsAccountId is not > 0) return;
        var balance = await TreasuryBalanceCalculator.GetAmountAsync(db, order.TrsAccountId.Value);
        if (!balance.HasValue || balance.Value < 0)
            throw new InvalidOperationException("مانده حساب صندوق/بانک برای پرداخت نقدی خرید کافی نیست.");
        var entryDate = await db.ShopOrderCashEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TransactionId == order.Id && e.ReversalDate == null).Select(e => e.Date).SingleAsync();
        await TreasuryBalanceCalculator.EnsureAvailableOnAndAfterAsync(db, order.TrsAccountId.Value, entryDate, 0m);
    }

    public static async Task SyncAfterEditAsync(AppDbContext db, Transaction order, ShopOrderCashEntry? previous, string? user)
    {
        RequireTransaction(db);
        if (!order.ShopCashLedgerEnabled) throw new InvalidOperationException("دفتر نقدی فاکتور فعال نیست.");
        if (previous is null && order.CashAmount == 0) return;
        if (previous is not null && order.CashAmount > 0 && Matches(previous, order)) return;
        var date = EffectiveDate(order.Date, previous?.Date);
        if (previous is not null)
        {
            Reverse(previous, date, "اصلاح پرداخت فاکتور", user);
            // Release the filtered unique active-entry index before inserting a replacement.
            await db.SaveChangesAsync();
        }
        if (order.CashAmount > 0)
        {
            await AddAsync(db, order, date, user);
            if (previous is null || previous.OrderType != TransactionType.Purchase ||
                previous.TrsAccountId != order.TrsAccountId || order.CashAmount > previous.Amount)
                await EnsurePurchaseBalanceAsync(db, order);
        }
        // Editing a sale's original cash can withdraw it from its former account.
        // Check after both compensating and replacement entries so same-day netting is accurate.
        if (previous is not null && previous.OrderType == TransactionType.Sale &&
            (previous.TrsAccountId != order.TrsAccountId || order.CashAmount < previous.Amount))
            await TreasuryBalanceCalculator.EnsureAvailableOnAndAfterAsync(db, previous.TrsAccountId, date, 0m);
    }

    public static async Task ReverseForDeleteAsync(AppDbContext db, Transaction order, ShopOrderCashEntry? previous, string? user)
    {
        RequireTransaction(db);
        if (previous is null) return;
        Reverse(previous, EffectiveDate(order.Date, previous.Date), "حذف فاکتور", user);
        await db.SaveChangesAsync();
    }

    private static void Reverse(ShopOrderCashEntry entry, DateTime date, string reason, string? user)
    {
        if (entry.ReversalDate.HasValue || entry.ReversalOperationId.HasValue)
            throw new InvalidOperationException("این گردش نقدی قبلاً معکوس شده است.");
        entry.ReversalDate = date;
        entry.ReversalOperationId = Guid.NewGuid();
        entry.ReversedAt = DateTime.Now;
        entry.ReversedBy = user;
        entry.ReversalReason = reason;
    }

    public static async Task ReconcileLegacyAsync(AppDbContext db, Transaction order, ShopCashReconciliationCommand cmd, string? user)
    {
        RequireTransaction(db);
        if (!cmd.Confirmed || cmd.OperationId == Guid.Empty || order.Type is not (TransactionType.Sale or TransactionType.Purchase) ||
            cmd.ExpectedCashAmount != order.CashAmount || order.CashAmount < 0 || order.CashAmount > order.SettledAmount ||
            order.Date == default || user?.Length > 120)
            throw new InvalidOperationException("تأیید و مبلغ نقد فاکتور برای تطبیق معتبر نیست؛ فاکتور را دوباره بارگذاری کنید.");
        if (await db.SaleChequeCharges.AnyAsync(c => c.TransactionId == order.Id))
            throw new InvalidOperationException("نقد فاکتور دارای تاخیر تادیه چک از گردش اختصاصی آن بررسی می‌شود.");
        var prior = await db.ShopOrderCashEntries.IgnoreQueryFilters().Include(e => e.MatchedTrsVoucher)
            .FirstOrDefaultAsync(e => e.OperationId == cmd.OperationId);
        if (prior is not null)
        {
            if (prior.CompanyId != order.CompanyId || prior.TransactionId != order.Id || prior.Amount != cmd.ExpectedCashAmount ||
                prior.TrsAccountId != cmd.TrsAccountId || prior.CashType != cmd.CashType ||
                prior.MatchedTrsVoucher?.Number != cmd.ExistingVoucherNumber)
                throw new InvalidOperationException("شناسه عملیات قبلاً برای تطبیق دیگری استفاده شده است.");
            return;
        }
        if (order.ShopCashLedgerEnabled)
        {
            if (order.CashAmount == 0) return;
            throw new InvalidOperationException("گردش نقدی این فاکتور قبلاً فعال شده است.");
        }
        if (await db.ShopOrderCashEntries.IgnoreQueryFilters().AnyAsync(e => e.TransactionId == order.Id))
            throw new InvalidOperationException("سابقه نقدی فاکتور پیش‌تر ثبت شده و نیازمند بررسی است.");
        if (order.CashAmount == 0)
        {
            if (order.PaymentMethod == PaymentMethod.Cash && order.SettledAmount > 0)
                throw new InvalidOperationException("مبلغ نقد این فاکتور قدیمی مشخص نیست؛ پیش از تطبیق باید از روی مدارک بررسی شود.");
            if (cmd.ExistingVoucherNumber.HasValue) throw new InvalidOperationException("فاکتور فاقد مبلغ نقدی است.");
            order.ShopCashLedgerEnabled = true;
            await db.SaveChangesAsync();
            return;
        }
        if (cmd.TrsAccountId is not > 0 || !cmd.CashType.HasValue || !Enum.IsDefined(cmd.CashType.Value) || cmd.ExistingVoucherNumber is <= 0)
            throw new InvalidOperationException("حساب و نوع پرداخت نقدی را مشخص کنید.");
        if (order.TrsAccountId is > 0 && order.TrsAccountId != cmd.TrsAccountId)
            throw new InvalidOperationException("حساب انتخابی با حساب ثبت‌شده روی فاکتور قدیمی مطابقت ندارد.");
        if (order.CashType.HasValue && order.CashType != cmd.CashType)
            throw new InvalidOperationException("نوع نقد با اطلاعات فاکتور قدیمی مطابقت ندارد.");
        order.TrsAccountId = cmd.TrsAccountId;
        order.CashType = cmd.CashType;
        TrsVoucher? voucher = null;
        if (cmd.ExistingVoucherNumber.HasValue)
        {
            var kind = order.Type == TransactionType.Sale ? TreasuryKind.Receipt : TreasuryKind.Payment;
            voucher = await db.TrsVouchers.Include(v => v.Lines).Include(v => v.InvoiceAllocations)
                .FirstOrDefaultAsync(v => v.Kind == kind && v.Number == cmd.ExistingVoucherNumber.Value)
                ?? throw new InvalidOperationException("سند دستی قطعی در شرکت جاری یافت نشد.");
            if (!MatchesLegacyVoucher(voucher, order) || voucher.Date.Date < order.Date.Date || voucher.InvoiceId.HasValue || voucher.InvoiceAllocations.Count != 0 ||
                await db.ShopOrderCashEntries.IgnoreQueryFilters().AnyAsync(e => e.MatchedTrsVoucherId == voucher.Id) ||
                await db.SaleChequeCharges.IgnoreQueryFilters().AnyAsync(c => c.InitialCashTrsVoucherId == voucher.Id) ||
                await db.SaleChequeCancellations.IgnoreQueryFilters().AnyAsync(c => c.RefundTrsVoucherId == voucher.Id) ||
                await db.SaleChequeCreditSettlements.IgnoreQueryFilters().AnyAsync(c => c.TrsVoucherId == voucher.Id))
                throw new InvalidOperationException("سند دستی با مبلغ، طرف حساب، شرکت یا حساب فاکتور مطابقت ندارد یا قبلاً استفاده شده است.");
        }
        order.ShopCashLedgerEnabled = true;
        await AddAsync(db, order, voucher?.Date.Date ?? order.Date.Date, user, voucher?.Id, cmd.OperationId);
    }

    private static bool MatchesMethod(CashType? type, PayMethod method) => type switch
    {
        CashType.Cash => method == PayMethod.Cash,
        CashType.CardReader => method == PayMethod.Card,
        CashType.CardTransfer => method is PayMethod.Card or PayMethod.Transfer,
        _ => false
    };

    public static bool MatchesLegacyVoucher(TrsVoucher voucher, Transaction order) =>
        voucher.CompanyId == order.CompanyId && voucher.Status == TreasuryStatus.Confirmed &&
        voucher.Kind == (order.Type == TransactionType.Sale ? TreasuryKind.Receipt : TreasuryKind.Payment) &&
        voucher.PartyId == order.PartyId && voucher.TotalAmount == order.CashAmount &&
        voucher.CashAmount == order.CashAmount && voucher.ChequeAmount == 0 && voucher.DiscountAmount == 0 &&
        voucher.Lines.Count == 1 && voucher.Lines[0].TrsAccountId == order.TrsAccountId &&
        voucher.Lines[0].Amount == order.CashAmount && voucher.Lines[0].ChequeId == null &&
        MatchesMethod(order.CashType, voucher.Lines[0].Method);

    public static async Task EnsureMatchedVouchersValidAsync(AppDbContext db, IReadOnlyCollection<ShopOrderCashEntry> entries)
    {
        var matched = entries.Where(e => e.MatchedTrsVoucherId.HasValue).ToList();
        if (matched.Count == 0) return;
        var ids = matched.Select(e => e.MatchedTrsVoucherId!.Value).Distinct().ToList();
        if (ids.Count != matched.Count) throw new InvalidOperationException("یک سند دستی به چند گردش نقدی متصل است.");
        var vouchers = await db.TrsVouchers.IgnoreQueryFilters().AsNoTracking().Include(v => v.Lines).Include(v => v.InvoiceAllocations)
            .Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id);
        foreach (var e in matched)
        {
            if (!vouchers.TryGetValue(e.MatchedTrsVoucherId!.Value, out var v) || v.CompanyId != e.CompanyId ||
                v.Status != TreasuryStatus.Confirmed || v.Kind != (e.OrderType == TransactionType.Sale ? TreasuryKind.Receipt : TreasuryKind.Payment) ||
                v.Date.Date != e.Date.Date || v.PartyId != e.PartyId || v.TotalAmount != e.Amount || v.CashAmount != e.Amount ||
                v.ChequeAmount != 0 || v.DiscountAmount != 0 || v.InvoiceId.HasValue || v.InvoiceAllocations.Count != 0 ||
                v.Lines.Count != 1 || v.Lines[0].TrsAccountId != e.TrsAccountId || v.Lines[0].Amount != e.Amount ||
                v.Lines[0].ChequeId.HasValue || !MatchesMethod(e.CashType, v.Lines[0].Method))
                throw new InvalidOperationException("سند دستی تطبیق‌شده با گردش نقدی فاکتور مطابقت ندارد؛ سابقه نیازمند بررسی است.");
        }
    }

    private static async Task AddAsync(AppDbContext db, Transaction order, DateTime date, string? user,
        int? matchedVoucherId = null, Guid? operationId = null)
    {
        if (order.Type is not (TransactionType.Sale or TransactionType.Purchase) || order.PartyId is not > 0 ||
            order.CashAmount <= 0 || order.CashAmount > MaxMoney || decimal.Round(order.CashAmount, 2) != order.CashAmount ||
            order.TrsAccountId is not > 0 || user?.Length > 120)
            throw new InvalidOperationException("مبلغ نقدی، طرف حساب یا حساب صندوق/بانک فاکتور معتبر نیست.");
        var account = await db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == order.TrsAccountId && a.IsActive)
            ?? throw new InvalidOperationException("برای پرداخت نقدی حساب فعال صندوق یا بانک را انتخاب کنید.");
        await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(db, account, order.CompanyId);
        var compatible = order.CashType switch
        {
            CashType.Cash => account.Kind is TreasuryAccountKind.Cash or TreasuryAccountKind.PettyCash,
            CashType.CardReader or CashType.CardTransfer => account.Kind is TreasuryAccountKind.Pos or TreasuryAccountKind.Bank,
            _ => false
        };
        if (!compatible) throw new InvalidOperationException("نوع حساب با روش پرداخت نقدی مطابقت ندارد.");
        var party = await db.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == order.PartyId)
            ?? throw new InvalidOperationException("طرف حساب متعلق به شرکت جاری نیست یا یافت نشد.");
        if (party.CompanyId != order.CompanyId)
            throw new InvalidOperationException("طرف حساب با شرکت فاکتور مطابقت ندارد.");
        db.ShopOrderCashEntries.Add(new ShopOrderCashEntry
        {
            CompanyId = order.CompanyId, TransactionId = order.Id, OrderNumber = order.Number, OrderType = order.Type,
            PartyId = order.PartyId, PartyName = party.Name, IsOpening = order.Description?.Contains("افتتاح") == true,
            TrsAccountId = account.Id, CashType = order.CashType!.Value,
            Reference = order.CashTrackingNumber, Amount = order.CashAmount, Date = date,
            MatchedTrsVoucherId = matchedVoucherId, OperationId = operationId ?? Guid.NewGuid(), CreatedAt = DateTime.Now, CreatedBy = user
        });
        await db.SaveChangesAsync();
    }
}
