using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// راستی‌آزمایی مستقل سوابق فروشگاهی؛ اسناد ERP قدیمی فقط برای نگهداری سابقه
/// باقی می‌مانند و برای عملیات فروشگاه خوانده یا تغییر داده نمی‌شوند.
/// </summary>
public static class SaleChequeShopLedgerIntegrity
{
    public static async Task EnsureAsync(AppDbContext db, Transaction sale, SaleChequeCharge charge, bool allowUnpostedLegacy = false)
    {
        if (sale.Type != TransactionType.Sale || charge.TransactionId != sale.Id || charge.CompanyId != sale.CompanyId ||
            charge.InitialAmount < 0 || charge.Amount < 0 || charge.CustomerCredit < 0 ||
            (charge.Amount > 0 && charge.CustomerCredit > 0) || sale.Amount != charge.BaseAmount + charge.Amount)
            throw new InvalidOperationException("مبالغ گردش فروشگاهی با فاکتور مطابقت ندارد.");
        var events = await db.SaleChequeChargeEvents.AsNoTracking()
            .Where(e => e.SaleChequeChargeId == charge.Id).ToListAsync();
        var initial = events.Where(e => e.Kind == SaleChequeChargeEventKind.Initial).ToList();
        // Old databases can contain an unchanged charge before event posting existed.
        // It remains visible on statements, but writes require an auditable initial event.
        var unchangedLegacy = allowUnpostedLegacy && events.Count == 0 &&
                              charge.InitialAmount == charge.Amount && charge.CustomerCredit == 0;
        if (!unchangedLegacy && (charge.InitialAmount <= 0 || initial.Count != 1 ||
            initial[0].IncomeDelta != charge.InitialAmount || initial[0].Date.Date != sale.Date.Date ||
            events.Any(e => e.CompanyId != sale.CompanyId || e.OperationId == Guid.Empty || e.Date.Date < sale.Date.Date ||
                e.Kind is not (SaleChequeChargeEventKind.Initial or SaleChequeChargeEventKind.DueDateAdjustment or SaleChequeChargeEventKind.GoodsReturn or SaleChequeChargeEventKind.Cancellation)) ||
            events.Sum(e => e.IncomeDelta) != charge.Amount - charge.CustomerCredit))
            throw new InvalidOperationException("تاریخچه درآمد چک با فاکتور مطابقت ندارد.");

        var adjustments = await db.SaleChequeReschedules.AsNoTracking().Where(r => r.SaleChequeChargeId == charge.Id).ToListAsync();
        var returns = await db.SaleChequeReturns.AsNoTracking().Include(r => r.ReturnTransaction)
            .Include(r => r.Lines).ThenInclude(l => l.OriginalLine)
            .Include(r => r.Lines).ThenInclude(l => l.ReturnLine)
            .Where(r => r.SaleChequeChargeId == charge.Id).ToListAsync();
        var cancellations = await db.SaleChequeCancellations.AsNoTracking().Include(c => c.ReturnTransaction)
            .Include(c => c.RefundTrsVoucher).ThenInclude(v => v!.Lines)
            .Where(c => c.SaleChequeChargeId == charge.Id).ToListAsync();
        bool Matches(int? eventId, Guid operationId, DateTime date, SaleChequeChargeEventKind kind, decimal delta)
        {
            if (delta == 0) return !eventId.HasValue;
            var match = events.SingleOrDefault(e => e.Id == eventId);
            return match is not null && match.Kind == kind && match.OperationId == operationId &&
                   match.Date.Date == date.Date && match.IncomeDelta == delta;
        }
        if (adjustments.Any(r => r.CompanyId != sale.CompanyId || !Matches(r.SaleChequeChargeEventId, r.OperationId, r.Date, SaleChequeChargeEventKind.DueDateAdjustment, r.IncomeDelta)) ||
            returns.Any(r => r.CompanyId != sale.CompanyId || r.SaleTransactionId != sale.Id || r.GoodsAmount < 0 || r.VatAmount < 0 || r.IncomeAmount < 0 ||
                !Matches(r.SaleChequeChargeEventId, r.OperationId, r.Date, SaleChequeChargeEventKind.GoodsReturn, -r.IncomeAmount)) ||
            cancellations.Count > 1 || cancellations.Any(c => c.CompanyId != sale.CompanyId || c.SaleTransactionId != sale.Id ||
                !Matches(c.SaleChequeChargeEventId, c.OperationId, c.Date, SaleChequeChargeEventKind.Cancellation, c.ReversedIncomeDelta)) ||
            events.Count(e => e.Kind == SaleChequeChargeEventKind.DueDateAdjustment) != adjustments.Count(r => r.IncomeDelta != 0) ||
            events.Count(e => e.Kind == SaleChequeChargeEventKind.GoodsReturn) != returns.Count(r => r.IncomeAmount != 0) ||
            events.Count(e => e.Kind == SaleChequeChargeEventKind.Cancellation) != cancellations.Count(c => c.ReversedIncomeDelta != 0))
            throw new InvalidOperationException("رویدادهای درآمد با تعدیل، برگشت یا ابطال فروشگاهی مطابقت ندارند.");

        foreach (var ret in returns)
        {
            if (ret.ReturnTransaction.Type != TransactionType.SaleReturn || ret.ReturnTransaction.CompanyId != sale.CompanyId ||
                ret.ReturnTransaction.PartyId != sale.PartyId || ret.ReturnTransaction.WarehouseId != sale.WarehouseId ||
                ret.ReturnTransaction.Date.Date != ret.Date.Date || ret.ReturnTransaction.Amount != ret.GoodsAmount + ret.VatAmount ||
                ret.Lines.Count == 0 || ret.Lines.Sum(l => l.GoodsAmount) != ret.GoodsAmount || ret.Lines.Sum(l => l.VatAmount) != ret.VatAmount ||
                ret.Lines.Any(l => l.CompanyId != sale.CompanyId || l.Quantity <= 0 || l.GoodsAmount < 0 || l.VatAmount < 0 ||
                    l.OriginalLine.TransactionId != sale.Id || l.ReturnLine.TransactionId != ret.ReturnTransactionId ||
                    l.OriginalLine.ProductId != l.ReturnLine.ProductId || l.ReturnLine.Quantity != l.Quantity ||
                    l.ReturnLine.GrossAmount != l.GoodsAmount || l.ReturnLine.VatAmount != l.VatAmount))
                throw new InvalidOperationException("اقلام و مبلغ برگشت فروش با فاکتور مطابقت ندارند.");
        }
        if (returns.SelectMany(r => r.Lines).GroupBy(l => l.OriginalLineId)
            .Any(g => g.Sum(l => l.Quantity) > g.First().OriginalLine.Quantity))
            throw new InvalidOperationException("مقدار برگشت‌شده از مقدار اولیه کالا بیشتر است.");
        foreach (var cancel in cancellations)
        {
            if (cancel.RefundedCashAmount != sale.CashAmount || cancel.ReversedGoodsAmount < 0 || cancel.ReversedVatAmount < 0 ||
                (cancel.ReturnTransaction is not null && (cancel.ReturnTransaction.Type != TransactionType.SaleReturn ||
                    cancel.ReturnTransaction.CompanyId != sale.CompanyId || cancel.ReturnTransaction.PartyId != sale.PartyId ||
                    cancel.ReturnTransaction.WarehouseId != sale.WarehouseId || cancel.ReturnTransaction.Date.Date != cancel.Date.Date ||
                    cancel.ReturnTransaction.Amount != cancel.ReversedGoodsAmount + cancel.ReversedVatAmount)) ||
                (cancel.ReturnTransaction is null && cancel.ReversedGoodsAmount + cancel.ReversedVatAmount != 0) ||
                (cancel.RefundedCashAmount == 0 && cancel.RefundTrsVoucherId.HasValue) ||
                (cancel.RefundedCashAmount > 0 && !MatchesPayment(cancel.RefundTrsVoucher, sale, sale.TrsAccountId, cancel.Date, cancel.RefundedCashAmount)))
                throw new InvalidOperationException("ابطال فاکتور با برگشت کالا یا گردش صندوق/بانک مطابقت ندارد.");
        }
        var settlements = await db.SaleChequeCreditSettlements.AsNoTracking().Include(s => s.TargetSaleTransaction)
            .Include(s => s.TrsVoucher).ThenInclude(v => v!.Lines).Where(s => s.SaleTransactionId == sale.Id).ToListAsync();
        foreach (var settlement in settlements)
        {
            if (settlement.CompanyId != sale.CompanyId || settlement.SaleChequeChargeId != charge.Id ||
                settlement.OperationId == Guid.Empty || settlement.Amount <= 0 || settlement.Date.Date < sale.Date.Date)
                throw new InvalidOperationException("تسویه بستانکاری فاکتور نامعتبر است.");
            if (settlement.Kind == SaleChequeCreditSettlementKind.RefundPayment)
            {
                if (settlement.TargetSaleTransactionId.HasValue ||
                    !MatchesPayment(settlement.TrsVoucher, sale, settlement.TrsAccountId, settlement.Date, settlement.Amount))
                    throw new InvalidOperationException("پرداخت بستانکاری با گردش صندوق/بانک مطابقت ندارد.");
            }
            else if (settlement.Kind == SaleChequeCreditSettlementKind.ApplyToSale)
            {
                var target = settlement.TargetSaleTransaction;
                if (settlement.TrsVoucherId.HasValue || settlement.TrsAccountId.HasValue || target is null || target.Id == sale.Id ||
                    target.Type != TransactionType.Sale || target.CompanyId != sale.CompanyId || target.PartyId != sale.PartyId ||
                    target.Date.Date > settlement.Date.Date)
                    throw new InvalidOperationException("انتقال بستانکاری به فاکتور دیگر نامعتبر است.");
            }
            else throw new InvalidOperationException("نوع تسویه بستانکاری نامعتبر است.");
        }

        if (sale.CashAmount > 0)
        {
            var cash = charge.InitialCashTrsVoucherId is > 0
                ? await db.TrsVouchers.AsNoTracking().Include(v => v.Lines).FirstOrDefaultAsync(v => v.Id == charge.InitialCashTrsVoucherId)
                : null;
            if (cash is null || cash.CompanyId != sale.CompanyId || cash.Kind != TreasuryKind.Receipt || cash.Status != TreasuryStatus.Confirmed ||
                cash.Date.Date != sale.Date.Date || cash.PartyId != sale.PartyId || cash.TotalAmount != sale.CashAmount ||
                cash.Lines.Count != 1 || cash.Lines[0].TrsAccountId != sale.TrsAccountId || cash.Lines[0].Amount != sale.CashAmount ||
                cash.Lines[0].Method != (sale.CashType == CashType.Cash ? PayMethod.Cash : PayMethod.Card))
                throw new InvalidOperationException("دریافت نقدی اولیه با گردش فروشگاهی صندوق/بانک مطابقت ندارد.");
        }
        else if (charge.InitialCashTrsVoucherId.HasValue)
            throw new InvalidOperationException("گردش نقدی اولیه بدون مبلغ نقدی ثبت شده است.");
    }

    private static bool MatchesPayment(TrsVoucher? voucher, Transaction sale, int? accountId, DateTime date, decimal amount)
        => accountId is > 0 && voucher is not null && voucher.CompanyId == sale.CompanyId && voucher.Status == TreasuryStatus.Confirmed &&
           voucher.Kind == TreasuryKind.Payment && voucher.Date.Date == date.Date && voucher.PartyId == sale.PartyId &&
           voucher.TotalAmount == amount && voucher.Lines.Count == 1 && voucher.Lines[0].TrsAccountId == accountId &&
           voucher.Lines[0].Amount == amount && voucher.Lines[0].Method is PayMethod.Cash or PayMethod.Card;
}
