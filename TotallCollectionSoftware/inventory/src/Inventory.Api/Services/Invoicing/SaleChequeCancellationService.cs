using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// ابطال اتمیک فاکتور دارای تاخیر تادیه چک با سند حسابداری معکوس، بازگشت موجودی کالا،
/// استرداد پیش‌دریافت نقدی اولیه و ابطال چک‌های وصول‌نشده بدون حذف اسناد تاریخی.
/// </summary>
public static class SaleChequeCancellationService
{
    public static async Task<SaleChequeCancellation> VoidAsync(
        AppDbContext db, int saleId, Guid operationId, DateTime operationDate, string? reason, string? user)
    {
        var tx = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("ابطال فاکتور باید در تراکنش فروشگاهی انجام شود.");
        try
        {
            if (tx.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("تراکنش ابطال فاکتور باید سطح Serializable داشته باشد.");
            return await VoidInTransactionAsync(db, saleId, operationId, operationDate, reason, user);
        }
        catch
        {
            try { await tx.RollbackAsync(); }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    public static async Task<SaleChequeCancellation> VoidInTransactionAsync(
        AppDbContext db, int saleId, Guid operationId, DateTime operationDate, string? reason, string? user)
    {
        var cleanReason = string.IsNullOrWhiteSpace(reason) ? "ابطال فاکتور فروش" : reason.Trim();
        if (operationId == Guid.Empty || operationDate == default || cleanReason.Length > 200 || user?.Length > 120)
            throw new InvalidOperationException("مشخصات ابطال فاکتور معتبر نیست.");
        operationDate = operationDate.Date;

        var prior = await db.SaleChequeCancellations.FirstOrDefaultAsync(c => c.OperationId == operationId);
        if (prior is not null)
        {
            if (prior.SaleTransactionId != saleId || prior.Date.Date != operationDate)
                throw new InvalidOperationException("شناسه عملیات قبلاً برای ابطال دیگری استفاده شده است.");
            return prior;
        }

        var sale = await db.Transactions.Include(t => t.Lines).Include(t => t.Cheques).Include(t => t.Installments)
            .FirstOrDefaultAsync(t => t.Id == saleId)
            ?? throw new InvalidOperationException("فاکتور فروش یافت نشد.");
        if (sale.Type != TransactionType.Sale || sale.PartyId is not > 0 || operationDate < sale.Date.Date)
            throw new InvalidOperationException("فاکتور فروش یا تاریخ ابطال معتبر نیست.");
        if (await db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == sale.Id))
            throw new InvalidOperationException("این فاکتور قبلاً ابطال شده است.");

        var charge = await db.SaleChequeCharges.FirstOrDefaultAsync(c => c.TransactionId == sale.Id)
            ?? throw new InvalidOperationException("ردیف تاخیر تادیه چک یافت نشد.");
        if (charge.CompanyId != sale.CompanyId || charge.Amount < 0 || charge.CustomerCredit < 0 || (charge.Amount > 0 && charge.CustomerCredit > 0) ||
            sale.Amount != charge.BaseAmount + charge.Amount)
            throw new InvalidOperationException("اطلاعات مالی فاکتور ناسازگار است؛ ابطال متوقف شد.");

        if (sale.Cheques.Any(c => c.IsCleared || c.Status == ChequeStatus.Cleared) ||
            await db.SaleChequeReceipts.AnyAsync(r => r.TransactionId == sale.Id && r.ReversalDate == null))
            throw new InvalidOperationException("چک وصول‌شده فعال روی این فاکتور وجود دارد؛ ابتدا وصول چک را لغو کنید.");
        if (sale.Installments.Any(i => i.IsPaid) ||
            await db.SaleChequeInstallmentReceipts.AnyAsync(r => r.TransactionId == sale.Id && r.ReversalDate == null))
            throw new InvalidOperationException("قسط پرداخت‌شده فعال روی این فاکتور وجود دارد؛ ابتدا دریافت قسط را لغو کنید.");
        if (await db.ShopOrderCreditReceipts.AnyAsync(r => r.TransactionId == sale.Id && r.ReversalDate == null))
            throw new InvalidOperationException("تسویه نسیه فعالی روی این فاکتور وجود دارد؛ ابتدا آن را لغو کنید.");
        if (await db.SaleChequeCreditSettlements.AnyAsync(s => s.SaleTransactionId == sale.Id || s.TargetSaleTransactionId == sale.Id))
            throw new InvalidOperationException("روی این فاکتور تسویه یا انتقال بستانکاری ثبت شده است؛ ابطال خودکار مجاز نیست.");

        var events = await db.SaleChequeChargeEvents.AsNoTracking()
            .Where(e => e.SaleChequeChargeId == charge.Id).ToListAsync();
        var priorReturns = await db.SaleChequeReturns.AsNoTracking().Include(r => r.Lines)
            .Where(r => r.SaleTransactionId == sale.Id).OrderBy(r => r.Id).ToListAsync();
        var reschedules = await db.SaleChequeReschedules.AsNoTracking()
            .Where(r => r.SaleChequeChargeId == charge.Id).ToListAsync();
        var receipts = await db.SaleChequeReceipts.AsNoTracking().Where(r => r.TransactionId == sale.Id).ToListAsync();
        var instReceipts = await db.SaleChequeInstallmentReceipts.AsNoTracking().Where(r => r.TransactionId == sale.Id).ToListAsync();

        if (events.Count(e => e.Kind == SaleChequeChargeEventKind.Initial) != 1 ||
            events.Any(e => e.Date.Date > operationDate) ||
            events.Sum(e => e.IncomeDelta) != charge.Amount - charge.CustomerCredit ||
            priorReturns.Any(r => r.Date.Date > operationDate) ||
            reschedules.Any(r => r.Date.Date > operationDate) ||
            receipts.Any(r => r.Date.Date > operationDate || (r.ReversalDate.HasValue && r.ReversalDate.Value.Date > operationDate)) ||
            await db.ShopOrderCreditReceipts.AnyAsync(r => r.TransactionId == sale.Id && (r.Date > operationDate || r.ReversalDate > operationDate)) ||
            instReceipts.Any(r => r.Date.Date > operationDate || (r.ReversalDate.HasValue && r.ReversalDate.Value.Date > operationDate)))
            throw new InvalidOperationException("تاریخ ابطال نمی‌تواند پیش از عملیات ثبت‌شده قبلی فاکتور باشد.");

        await SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync(db, sale);
        if (sale.SettledAmount != sale.CashAmount)
            throw new InvalidOperationException("مانده تسویه فاکتور با پیش‌دریافت نقدی اولیه مطابقت ندارد.");

        var origGoods = sale.Lines.Sum(l => Math.Max(0m, (l.GrossAmount ?? OrderLineAmounts.Gross(l.Quantity, l.Price)) - l.DiscountAmount));
        var origVat = sale.Lines.Sum(l => l.VatAmount);
        TrsAccount? cashTrsAccount = null;
        if (sale.CashAmount > 0)
        {
            if (sale.TrsAccountId is not int trsId || trsId <= 0)
                throw new InvalidOperationException("حساب صندوق/بانک پیش‌دریافت نقدی اولیه مشخص نیست.");
            cashTrsAccount = await db.TrsAccounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == trsId && a.IsActive)
                ?? throw new InvalidOperationException("حساب صندوق/بانک پیش‌دریافت نقدی اولیه فعال نیست یا تغییر کرده است.");
            await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(db, cashTrsAccount, sale.CompanyId);
            var trsBalance = await TreasuryBalanceCalculator.GetAmountAsync(db, cashTrsAccount.Id);
            if (!trsBalance.HasValue || trsBalance.Value < sale.CashAmount)
                throw new InvalidOperationException("مانده حساب صندوق/بانک برای استرداد پیش‌دریافت نقدی فاکتور کافی نیست.");
            await TreasuryBalanceCalculator.EnsureAvailableOnAndAfterAsync(db, cashTrsAccount.Id, operationDate, sale.CashAmount);
        }

        var remGoods = origGoods - priorReturns.Sum(r => r.GoodsAmount);
        var remVat = origVat - priorReturns.Sum(r => r.VatAmount);
        var netIncomeNow = charge.Amount - charge.CustomerCredit;
        var reversedIncomeDelta = -netIncomeNow;
        if (remGoods < 0 || remVat < 0)
            throw new InvalidOperationException("جمع برگشت‌های قبلی از مبلغ اولیه فاکتور بیشتر است.");

        var productIds = sale.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var prevQtyByLine = priorReturns.SelectMany(r => r.Lines)
            .GroupBy(l => l.OriginalLineId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        var remainingLineItems = new List<(TransactionLine Orig, Product Prod, decimal RemQty, SaleChequeReturnPlanner.Line Amounts)>();
        foreach (var origLine in sale.Lines)
        {
            var prevQty = prevQtyByLine.GetValueOrDefault(origLine.Id);
            var remQty = origLine.Quantity - prevQty;
            if (remQty < 0) throw new InvalidOperationException("مقدار برگشتی قبلی از مقدار سطر بیشتر است.");
            if (remQty == 0) continue;
            if (!products.TryGetValue(origLine.ProductId, out var product))
                throw new InvalidOperationException("کالای سطر فاکتور یافت نشد.");
            if (!product.IsService)
            {
                if (!origLine.UnitCostSnapshot.HasValue || origLine.UnitCostSnapshot.Value < 0)
                    throw new InvalidOperationException($"بهای ثبت‌شده کالای «{product.Name}» یافت نشد؛ ابطال خودکار مجاز نیست.");
                if (await db.InvStocks.AnyAsync(s => s.WarehouseId == sale.WarehouseId && s.ProductId == product.Id) ||
                    await db.InvLedger.AnyAsync(e => e.WarehouseId == sale.WarehouseId && e.ProductId == product.Id))
                    throw new InvalidOperationException($"کالای «{product.Name}» در دفتر انبارداری مجزا گردش دارد؛ ابطال خودکار مجاز نیست.");
            }
            var lineGoods = Math.Max(0m, (origLine.GrossAmount ?? OrderLineAmounts.Gross(origLine.Quantity, origLine.Price)) - origLine.DiscountAmount);
            var amounts = SaleChequeReturnPlanner.PlanLine(origLine.Quantity, prevQty, remQty, lineGoods, origLine.VatAmount);
            remainingLineItems.Add((origLine, product, remQty, amounts));
        }

        Transaction? returnTxn = null;
        if (remainingLineItems.Count > 0)
        {
            var retNo = await SaleChequeReturnService.NextReturnNumberAsync(db);
            returnTxn = new Transaction
            {
                CompanyId = sale.CompanyId,
                Number = retNo,
                Type = TransactionType.SaleReturn,
                Date = operationDate,
                WarehouseId = sale.WarehouseId,
                PartyId = sale.PartyId,
                Amount = remGoods + remVat,
                PaymentMethod = PaymentMethod.Credit,
                Description = $"برگشت کامل بابت ابطال فاکتور {sale.Number}"
            };
            foreach (var (orig, prod, remQty, amounts) in remainingLineItems)
            {
                returnTxn.Lines.Add(new TransactionLine
                {
                    CompanyId = sale.CompanyId,
                    ProductId = prod.Id,
                    Quantity = remQty,
                    Price = decimal.Round(amounts.GoodsBeforeVat / remQty, 2, MidpointRounding.ToEven),
                    GrossAmount = amounts.GoodsBeforeVat,
                    VatValue = amounts.Vat > 100m ? amounts.Vat : 0m,
                    VatAmount = amounts.Vat,
                    UnitCostSnapshot = prod.IsService ? orig.UnitCostSnapshot : orig.UnitCostSnapshot!.Value
                });
            }
            db.Transactions.Add(returnTxn);
            await db.SaveChangesAsync();

            foreach (var pid in remainingLineItems.Where(x => !x.Prod.IsService).Select(x => x.Prod.Id).Distinct())
                await SaleChequeReturnService.RebuildLegacyStockAsync(db, sale.WarehouseId, pid);
        }

        var title = $"ابطال فاکتور فروش {sale.Number}";
        SaleChequeChargeEvent? cancelEvent = null;
        if (reversedIncomeDelta != 0)
        {
            cancelEvent = new SaleChequeChargeEvent
            {
                CompanyId = charge.CompanyId,
                OperationId = operationId,
                SaleChequeChargeId = charge.Id,
                Kind = SaleChequeChargeEventKind.Cancellation,
                Date = operationDate,
                IncomeDelta = reversedIncomeDelta
            };
            db.SaleChequeChargeEvents.Add(cancelEvent);
        }

        TrsVoucher? refundTrs = null;
        if (sale.CashAmount > 0 && cashTrsAccount is not null)
        {
            var trsNo = (await db.TrsVouchers.IgnoreQueryFilters().Where(v => v.Kind == TreasuryKind.Payment)
                .Select(v => (int?)v.Number).MaxAsync() ?? 0) + 1;
            refundTrs = new TrsVoucher
            {
                CompanyId = sale.CompanyId,
                Number = trsNo,
                Kind = TreasuryKind.Payment,
                Date = operationDate,
                PartyId = sale.PartyId,
                Description = $"استرداد پیش‌دریافت نقدی — {title}",
                RefNumber = sale.Number,
                Status = TreasuryStatus.Confirmed,
                TotalAmount = sale.CashAmount,
                CashAmount = sale.CashAmount,
                CreatedBy = user,
                ConfirmedBy = user,
                CreatedAt = DateTime.Now,
                ConfirmedAt = DateTime.Now,
                Lines = new()
                {
                    new TrsVoucherLine
                    {
                        RowNo = 1,
                        TrsAccountId = cashTrsAccount.Id,
                        Amount = sale.CashAmount,
                        Method = cashTrsAccount.Kind is TreasuryAccountKind.Cash or TreasuryAccountKind.PettyCash ? PayMethod.Cash : PayMethod.Card,
                        RefNumber = sale.Number,
                        Description = title
                    }
                }
            };
            db.TrsVouchers.Add(refundTrs);
            sale.SettledAmount -= sale.CashAmount;
        }

        var chequeIndex = 0;
        foreach (var cheque in sale.Cheques.Where(c => c.Status != ChequeStatus.Cancelled))
        {
            var prev = cheque.Status;
            cheque.Status = ChequeStatus.Cancelled;
            var statusOp = chequeIndex++ == 0 ? operationId : Guid.NewGuid();
            db.SaleChequeStatusEvents.Add(new SaleChequeStatusEvent
            {
                CompanyId = cheque.CompanyId,
                OperationId = statusOp,
                ChequeId = cheque.Id,
                PreviousStatus = prev,
                NewStatus = ChequeStatus.Cancelled,
                CreatedAt = DateTime.Now,
                CreatedBy = user,
                Reason = cleanReason
            });
        }

        charge.Amount = 0;
        charge.CustomerCredit = 0;
        sale.Amount = SaleChequeVoucherAmounts.Create(charge.BaseAmount, 0, 0).CustomerDebit;

        var cancellation = new SaleChequeCancellation
        {
            CompanyId = sale.CompanyId,
            OperationId = operationId,
            SaleTransactionId = sale.Id,
            SaleChequeChargeId = charge.Id,
            ReturnTransaction = returnTxn,
            SaleChequeChargeEvent = cancelEvent,
            RefundTrsVoucher = refundTrs,
            Date = operationDate,
            ReversedGoodsAmount = remGoods,
            ReversedVatAmount = remVat,
            ReversedIncomeDelta = reversedIncomeDelta,
            RefundedCashAmount = sale.CashAmount,
            Reason = cleanReason,
            CreatedBy = user,
            CreatedAt = DateTime.Now
        };
        db.SaleChequeCancellations.Add(cancellation);
        await db.SaveChangesAsync();
        return cancellation;
    }
}
