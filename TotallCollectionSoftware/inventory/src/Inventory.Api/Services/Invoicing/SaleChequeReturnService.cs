using System.Data;
using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

public sealed record SaleChequeReturnLineInput(int OriginalLineId, decimal Quantity);

public sealed record SaleChequeReturnItemComputation(
    TransactionLine OriginalLine,
    Product Product,
    decimal PreviouslyReturnedQuantity,
    decimal Quantity,
    SaleChequeReturnPlanner.Line Amounts);

public sealed record SaleChequeReturnComputation(
    Transaction Sale,
    SaleChequeCharge Charge,
    SaleChequeReturnPlanner.Preview Plan,
    decimal RemainingBaseBefore,
    int? EpochRescheduleId,
    decimal EpochBase,
    decimal EpochIncome,
    decimal PreviouslyReturnedBaseInEpoch,
    IReadOnlyList<SaleChequeReturnItemComputation> Items);

/// <summary>
/// پیش‌نمایش و ثبت اتمیک برگشت کالای فاکتور سوددار در تراکنش Serializable،
/// بدون حذف فاکتور یا تغییر خودکار چک دریافتی.
/// </summary>
public static class SaleChequeReturnService
{
    public static Task<SaleChequeReturnComputation> PreviewAsync(
        AppDbContext db, int saleId, DateTime operationDate, IReadOnlyCollection<SaleChequeReturnLineInput> lines)
        => ComputeAsync(db, saleId, operationDate, lines, tracking: false);

    public static async Task<SaleChequeReturn> ApplyAsync(
        AppDbContext db,
        int saleId,
        Guid operationId,
        DateTime operationDate,
        IReadOnlyCollection<SaleChequeReturnLineInput> lines,
        SaleChequeReturnDecision decision,
        decimal? confirmedTotalCredit,
        string? user)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("ثبت برگشت فروش باید داخل تراکنش فاکتور انجام شود.");
        try
        {
            if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("تراکنش برگشت فروش باید سطح Serializable داشته باشد.");
            return await ApplyInTransactionAsync(db, saleId, operationId, operationDate, lines, decision, confirmedTotalCredit, user);
        }
        catch
        {
            try { await transaction.RollbackAsync(); }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    private static async Task<SaleChequeReturn> ApplyInTransactionAsync(
        AppDbContext db,
        int saleId,
        Guid operationId,
        DateTime operationDate,
        IReadOnlyCollection<SaleChequeReturnLineInput> lines,
        SaleChequeReturnDecision decision,
        decimal? confirmedTotalCredit,
        string? user)
    {
        if (operationId == Guid.Empty || operationDate == default)
            throw new InvalidOperationException("شناسه عملیات و تاریخ برگشت الزامی است.");
        if (user?.Length > 120)
            throw new InvalidOperationException("نام کاربر بیش از حد طولانی است.");
        operationDate = operationDate.Date;

        var requested = NormalizeLines(lines);
        var prior = await db.SaleChequeReturns.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.OperationId == operationId);
        if (prior is not null)
        {
            var priorPairs = prior.Lines.OrderBy(l => l.OriginalLineId)
                .Select(l => (l.OriginalLineId, l.Quantity)).ToList();
            var reqPairs = requested.OrderBy(l => l.OriginalLineId)
                .Select(l => (l.OriginalLineId, l.Quantity)).ToList();
            if (prior.SaleTransactionId != saleId || prior.Date.Date != operationDate ||
                prior.Decision != decision || !priorPairs.SequenceEqual(reqPairs) ||
                (confirmedTotalCredit.HasValue && prior.GoodsAmount + prior.VatAmount + prior.IncomeAmount != confirmedTotalCredit.Value))
                throw new InvalidOperationException("شناسه عملیات قبلاً برای برگشت دیگری استفاده شده است.");
            return prior;
        }

        var computed = await ComputeAsync(db, saleId, operationDate, requested, tracking: true);
        var plan = computed.Plan;
        if (plan.UncollectedChequeExcess > 0)
        {
            if (decision is not (SaleChequeReturnDecision.ReplaceCheque or SaleChequeReturnDecision.KeepChequeForGoods))
                throw new InvalidOperationException("مبلغ چک وصول‌نشده از مانده فاکتور بیشتر است؛ تصمیم مشتری برای تعویض چک یا نگهداری آن در قبال کالای دیگر را ثبت کنید.");
        }
        else if (decision != SaleChequeReturnDecision.None)
        {
            throw new InvalidOperationException("مازاد چک وصول‌نشده وجود ندارد؛ پیش‌نمایش برگشت را دوباره بررسی کنید.");
        }

        if (confirmedTotalCredit.HasValue && confirmedTotalCredit.Value != plan.TotalCredit)
            throw new InvalidOperationException("مبلغ برگشت نسبت به پیش‌نمایش تغییر کرده است؛ دوباره بررسی و تأیید کنید.");

        var sale = computed.Sale;
        var charge = computed.Charge;
        var returnNumber = await NextReturnNumberAsync(db);
        var returnTxn = new Transaction
        {
            CompanyId = sale.CompanyId,
            Number = returnNumber,
            Type = TransactionType.SaleReturn,
            Date = operationDate,
            WarehouseId = sale.WarehouseId,
            PartyId = sale.PartyId,
            Amount = plan.GoodsBeforeVat + plan.Vat,
            PaymentMethod = PaymentMethod.Credit,
            Description = $"برگشت کالا از فاکتور {sale.Number}"
        };

        var linePairs = new List<(SaleChequeReturnItemComputation Item, TransactionLine ReturnLine)>();
        foreach (var item in computed.Items)
        {
            var unitGoods = decimal.Round(item.Amounts.GoodsBeforeVat / item.Quantity, 2, MidpointRounding.ToEven);
            var retLine = new TransactionLine
            {
                CompanyId = sale.CompanyId,
                ProductId = item.Product.Id,
                Quantity = item.Quantity,
                Price = unitGoods,
                GrossAmount = item.Amounts.GoodsBeforeVat,
                DiscountPercent = 0,
                DiscountAmount = 0,
                DiscountIsAmount = false,
                VatValue = item.Amounts.Vat > 0 ? 100.01m : 0m,
                VatAmount = item.Amounts.Vat,
                UnitCostSnapshot = item.Product.IsService ? item.OriginalLine.UnitCostSnapshot : item.OriginalLine.UnitCostSnapshot!.Value
            };
            if (retLine.VatAmount > 0) retLine.VatValue = retLine.VatAmount > 100m ? retLine.VatAmount : 0m;
            returnTxn.Lines.Add(retLine);
            linePairs.Add((item, retLine));
        }

        db.Transactions.Add(returnTxn);
        await db.SaveChangesAsync();

        var affectedProducts = computed.Items.Where(x => !x.Product.IsService).Select(x => x.Product.Id).Distinct().ToList();
        foreach (var productId in affectedProducts)
            await RebuildLegacyStockAsync(db, sale.WarehouseId, productId);

        SaleChequeChargeEvent? incomeEvent = null;
        if (plan.Income > 0)
        {
            incomeEvent = new SaleChequeChargeEvent
            {
                CompanyId = charge.CompanyId,
                OperationId = operationId,
                SaleChequeChargeId = charge.Id,
                Kind = SaleChequeChargeEventKind.GoodsReturn,
                Date = operationDate,
                IncomeDelta = -plan.Income
            };
            db.SaleChequeChargeEvents.Add(incomeEvent);
        }

        charge.Amount -= plan.Income;
        sale.Amount = SaleChequeVoucherAmounts.Create(charge.BaseAmount, 0, charge.Amount).CustomerDebit;

        var record = new SaleChequeReturn
        {
            CompanyId = sale.CompanyId,
            OperationId = operationId,
            SaleTransactionId = sale.Id,
            ReturnTransaction = returnTxn,
            SaleChequeChargeId = charge.Id,
            SaleChequeChargeEvent = incomeEvent,
            Date = operationDate,
            GoodsAmount = plan.GoodsBeforeVat,
            VatAmount = plan.Vat,
            IncomeAmount = plan.Income,
            EpochRescheduleId = computed.EpochRescheduleId,
            EpochBase = computed.EpochBase,
            EpochIncome = computed.EpochIncome,
            PreviouslyReturnedBaseInEpoch = computed.PreviouslyReturnedBaseInEpoch,
            BalanceAfter = plan.BalanceAfter,
            UncollectedChequeExcess = plan.UncollectedChequeExcess,
            Decision = decision,
            CreatedBy = user,
            CreatedAt = DateTime.Now
        };
        foreach (var (item, retLine) in linePairs)
        {
            record.Lines.Add(new SaleChequeReturnLine
            {
                CompanyId = sale.CompanyId,
                OriginalLineId = item.OriginalLine.Id,
                ReturnLine = retLine,
                Quantity = item.Quantity,
                GoodsAmount = item.Amounts.GoodsBeforeVat,
                VatAmount = item.Amounts.Vat
            });
        }

        db.SaleChequeReturns.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    private static List<SaleChequeReturnLineInput> NormalizeLines(IReadOnlyCollection<SaleChequeReturnLineInput> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var items = lines.Where(l => l.Quantity != 0).ToList();
        if (items.Count == 0)
            throw new InvalidOperationException("حداقل یک سطر با مقدار برگشتی مثبت انتخاب کنید.");
        if (items.Any(l => l.OriginalLineId <= 0 || l.Quantity <= 0 || decimal.Round(l.Quantity, 2) != l.Quantity))
            throw new InvalidOperationException("ردیف یا مقدار برگشتی معتبر نیست.");
        if (items.Select(l => l.OriginalLineId).Distinct().Count() != items.Count)
            throw new InvalidOperationException("هر سطر فاکتور فقط یک‌بار باید در برگشت ارسال شود.");
        return items;
    }

    private static async Task<SaleChequeReturnComputation> ComputeAsync(
        AppDbContext db,
        int saleId,
        DateTime operationDate,
        IReadOnlyCollection<SaleChequeReturnLineInput> rawLines,
        bool tracking)
    {
        if (operationDate == default)
            throw new InvalidOperationException("تاریخ برگشت معتبر نیست.");
        operationDate = operationDate.Date;
        var requested = NormalizeLines(rawLines);

        var saleQuery = tracking ? db.Transactions.AsQueryable() : db.Transactions.AsNoTracking();
        var sale = await saleQuery.Include(t => t.Lines).Include(t => t.Cheques).Include(t => t.Installments)
            .FirstOrDefaultAsync(t => t.Id == saleId)
            ?? throw new InvalidOperationException("فاکتور فروش یافت نشد.");
        if (sale.Type != TransactionType.Sale || sale.PartyId is not > 0 || operationDate < sale.Date.Date)
            throw new InvalidOperationException("فاکتور فروش یا تاریخ برگشت معتبر نیست.");

        var chargeQuery = tracking ? db.SaleChequeCharges.AsQueryable() : db.SaleChequeCharges.AsNoTracking();
        var charge = await chargeQuery.FirstOrDefaultAsync(c => c.TransactionId == sale.Id)
            ?? throw new InvalidOperationException("ردیف تاخیر تادیه چک یافت نشد.");
        if (charge.CompanyId != sale.CompanyId || charge.Amount < 0 || charge.CustomerCredit < 0 || (charge.Amount > 0 && charge.CustomerCredit > 0) ||
            sale.Amount != charge.BaseAmount + charge.Amount)
            throw new InvalidOperationException("اطلاعات مالی فاکتور ناسازگار است؛ برگشت متوقف شد.");

        var origGoods = sale.Lines.Sum(LineGoodsBeforeVat);
        var origVat = sale.Lines.Sum(l => l.VatAmount);
        if (origGoods + origVat != charge.BaseAmount)
            throw new InvalidOperationException("مبنای کالا و مالیات فاکتور با ردیف تاخیر تادیه چک مطابقت ندارد.");

        if (await db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == sale.Id))
            throw new InvalidOperationException("فاکتور ابطال شده است و امکان ثبت برگشت مجدد ندارد.");

        var events = await db.SaleChequeChargeEvents.AsNoTracking()
            .Where(e => e.SaleChequeChargeId == charge.Id).ToListAsync();
        var reschedules = await db.SaleChequeReschedules.AsNoTracking()
            .Where(r => r.SaleChequeChargeId == charge.Id).OrderBy(r => r.Id).ToListAsync();
        var priorReturns = await db.SaleChequeReturns.AsNoTracking().Include(r => r.Lines)
            .Where(r => r.SaleTransactionId == sale.Id).OrderBy(r => r.Id).ToListAsync();
        var receipts = await db.SaleChequeReceipts.AsNoTracking()
            .Where(r => r.TransactionId == sale.Id).ToListAsync();
        var installmentReceipts = await db.SaleChequeInstallmentReceipts.AsNoTracking()
            .Where(r => r.TransactionId == sale.Id).ToListAsync();
        var creditSettlements = await db.SaleChequeCreditSettlements.AsNoTracking()
            .Where(s => s.SaleTransactionId == sale.Id).ToListAsync();

        if (events.Count(e => e.Kind == SaleChequeChargeEventKind.Initial) != 1 ||
            events.Any(e => e.Date.Date > operationDate) ||
            events.Sum(e => e.IncomeDelta) != charge.Amount - charge.CustomerCredit ||
            reschedules.Any(r => r.Date.Date > operationDate) ||
            priorReturns.Any(r => r.Date.Date > operationDate) ||
            creditSettlements.Any(s => s.Date.Date > operationDate) ||
            installmentReceipts.Any(r => r.Date.Date > operationDate || (r.ReversalDate.HasValue && r.ReversalDate.Value.Date > operationDate)) ||
            receipts.Any(r => r.Date.Date > operationDate || (r.ReversalDate.HasValue && r.ReversalDate.Value.Date > operationDate)))
            throw new InvalidOperationException("تاریخچه مالی فاکتور ناقص است یا تاریخ برگشت پیش از عملیات قبلی است.");

        await SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync(db, sale);
        var netSettled = Math.Max(0m, sale.SettledAmount - creditSettlements.Sum(s => s.Amount));

        var totalReturnedBase = priorReturns.Sum(r => r.GoodsAmount + r.VatAmount);
        var remainingBase = charge.BaseAmount - totalReturnedBase;
        if (remainingBase <= 0)
            throw new InvalidOperationException("کالای قابل برگشتی در این فاکتور باقی نمانده است.");

        var productIds = sale.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var prevQtyByLine = priorReturns.SelectMany(r => r.Lines)
            .GroupBy(l => l.OriginalLineId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        var items = new List<SaleChequeReturnItemComputation>(requested.Count);
        foreach (var req in requested)
        {
            var origLine = sale.Lines.FirstOrDefault(l => l.Id == req.OriginalLineId)
                ?? throw new InvalidOperationException("سطر انتخاب‌شده متعلق به این فاکتور نیست.");
            if (!products.TryGetValue(origLine.ProductId, out var product))
                throw new InvalidOperationException("کالای سطر فاکتور یافت نشد.");
            if (!product.IsService)
            {
                if (!origLine.UnitCostSnapshot.HasValue || origLine.UnitCostSnapshot.Value < 0)
                    throw new InvalidOperationException($"بهای تمام‌شده ثبت‌شده برای «{product.Name}» یافت نشد؛ برگشت خودکار مجاز نیست.");
                if (await db.InvStocks.AnyAsync(s => s.WarehouseId == sale.WarehouseId && s.ProductId == product.Id) ||
                    await db.InvLedger.AnyAsync(e => e.WarehouseId == sale.WarehouseId && e.ProductId == product.Id))
                    throw new InvalidOperationException($"کالای «{product.Name}» در دفتر انبارداری مجزا گردش دارد؛ برگشت خودکار بدون ارزیابی واحد مجاز نیست.");
            }
            var prevQty = prevQtyByLine.GetValueOrDefault(origLine.Id);
            SaleChequeReturnPlanner.Line linePlan;
            try
            {
                linePlan = SaleChequeReturnPlanner.PlanLine(
                    origLine.Quantity, prevQty, req.Quantity, LineGoodsBeforeVat(origLine), origLine.VatAmount);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }
            items.Add(new SaleChequeReturnItemComputation(origLine, product, prevQty, req.Quantity, linePlan));
        }

        int? epochRescheduleId = reschedules.LastOrDefault()?.Id;
        var epochReturns = priorReturns.Where(r => r.EpochRescheduleId == epochRescheduleId).ToList();
        var previouslyReturnedInEpoch = epochReturns.Sum(r => r.GoodsAmount + r.VatAmount);
        decimal epochBase, epochIncome;
        if (epochReturns.Count > 0)
        {
            epochBase = epochReturns[0].EpochBase;
            epochIncome = epochReturns[0].EpochIncome;
            if (epochReturns.Any(r => r.EpochBase != epochBase || r.EpochIncome != epochIncome))
                throw new InvalidOperationException("تاریخچه دوره برگشت ناسازگار است؛ بررسی دستی لازم است.");
        }
        else
        {
            epochBase = remainingBase;
            epochIncome = charge.Amount;
        }

        var uncollectedCheques = sale.Cheques
            .Where(c => !c.IsCleared && c.Status is ChequeStatus.InHand or ChequeStatus.Bounced or ChequeStatus.InCollection)
            .Sum(c => c.Amount);

        SaleChequeReturnPlanner.Preview plan;
        try
        {
            plan = SaleChequeReturnPlanner.Plan(
                remainingBase,
                charge.Amount,
                charge.CustomerCredit,
                netSettled,
                uncollectedCheques,
                items.Select(x => x.Amounts),
                epochBase,
                epochIncome,
                previouslyReturnedInEpoch);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }

        return new SaleChequeReturnComputation(
            sale, charge, plan, remainingBase, epochRescheduleId, epochBase, epochIncome, previouslyReturnedInEpoch,
            items);
    }

    private static decimal LineGoodsBeforeVat(TransactionLine l)
        => Math.Max(0m, (l.GrossAmount ?? OrderLineAmounts.Gross(l.Quantity, l.Price)) - l.DiscountAmount);

    public static async Task<string> NextReturnNumberAsync(AppDbContext db)
    {
        var n = await db.Transactions.CountAsync(t => t.Type == TransactionType.SaleReturn) + 1;
        while (true)
        {
            var candidate = $"SR-{n:0000}";
            if (!await db.Transactions.AnyAsync(t => t.Number == candidate))
                return candidate;
            n++;
        }
    }

    public static async Task RebuildLegacyStockAsync(AppDbContext db, int warehouseId, int productId)
    {
        var txns = await db.Transactions.Include(t => t.Lines)
            .Where(t => t.WarehouseId == warehouseId && t.Lines.Any(l => l.ProductId == productId))
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .ToListAsync();

        decimal qty = 0, cost = 0;
        var product = await db.Products.FindAsync(productId);
        var defaultCost = product?.PurchasePrice ?? 0;

        foreach (var t in txns)
        {
            foreach (var l in t.Lines.Where(l => l.ProductId == productId))
            {
                switch (t.Type)
                {
                    case TransactionType.Purchase:
                        var newQty = qty + l.Quantity;
                        if (newQty != 0) cost = (qty * cost + l.Quantity * l.Price) / newQty;
                        qty = newQty;
                        break;
                    case TransactionType.Sale:
                        qty -= l.Quantity;
                        break;
                    case TransactionType.SaleReturn:
                        if (!l.UnitCostSnapshot.HasValue || l.UnitCostSnapshot.Value < 0)
                            throw new InvalidOperationException("بهای ثبت‌شده کالای برگشتی معتبر نیست.");
                        var returnedQty = qty + l.Quantity;
                        if (returnedQty != 0) cost = (qty * cost + l.Quantity * l.UnitCostSnapshot.Value) / returnedQty;
                        qty = returnedQty;
                        break;
                    default:
                        qty = l.Quantity;
                        if (cost == 0 && qty > 0) cost = l.Price > 0 ? l.Price : defaultCost;
                        break;
                }
            }
        }

        var invStock = await db.InvStocks.AsNoTracking()
            .FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.ProductId == productId);
        if (invStock is not null)
        {
            qty += invStock.Quantity;
            if (invStock.AvgCost > 0) cost = invStock.AvgCost;
        }

        var stock = await db.Stocks.FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.ProductId == productId);
        if (qty == 0 && stock is null) return;
        if (stock is null)
        {
            stock = new Stock { CompanyId = product?.CompanyId, WarehouseId = warehouseId, ProductId = productId };
            db.Stocks.Add(stock);
        }
        stock.Quantity = qty;
        stock.AvgCost = qty > 0 ? cost : defaultCost;
    }
}
