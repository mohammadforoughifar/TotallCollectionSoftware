using Inventory.Api.Data;
using Inventory.Api.Services.Invoicing;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Reports;

/// <summary>تجمیع و آماده‌سازی گزارش‌های عملیاتی بر مبنای اسناد خرید/فروش و تعمیرات.</summary>
public class OperationsReportsService : IOperationsReportsService
{
    private readonly AppDbContext _db;

    public OperationsReportsService(AppDbContext db) => _db = db;

    public async Task<OperationsOrderReportDto> GetOrderReportAsync(TransactionType type, DateTime? from, DateTime? to,
        int? partyId, int? warehouseId, bool? hasChequeIncome = null)
    {
        EnsureOrderType(type);
        (from, to) = NormalizeRange(from, to);

        var query = OrderQuery(type, from, to, partyId, warehouseId);
        if (hasChequeIncome.HasValue)
            query = query.Where(t => _db.SaleChequeCharges.Any(c => c.TransactionId == t.Id && c.Amount > 0) == hasChequeIncome.Value);
        var documents = await query
            .Include(t => t.Lines)
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .ToListAsync();

        var partyIds = documents.Where(t => t.PartyId.HasValue).Select(t => t.PartyId!.Value).Distinct().ToList();
        var partyNames = partyIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Parties.AsNoTracking().Where(p => partyIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name);

        var documentIds = documents.Select(t => t.Id).ToList();
        var charges = await _db.SaleChequeCharges.AsNoTracking()
            .Where(c => documentIds.Contains(c.TransactionId))
            .Select(c => new { c.TransactionId, c.Amount }).ToListAsync();
        var chargeById = charges.ToDictionary(c => c.TransactionId, c => c.Amount);
        if (documents.Any(t => chargeById.TryGetValue(t.Id, out var charge) && (charge < 0 || charge > t.Amount)))
            throw new InvalidOperationException("مبلغ تاخیر تادیه چک با مبلغ یکی از فاکتورها مطابقت ندارد.");
        var chequeIncomeTotal = charges.Sum(c => c.Amount);

        var returnRows = type == TransactionType.Sale
            ? await LoadSaleReturnsInPeriodAsync(from, to, partyId, warehouseId, hasChequeIncome)
            : new List<PeriodReturnRow>();
        var returnedGoodsTotal = returnRows.Sum(r => r.GoodsWithVat);
        var returnedQtyTotal = returnRows.Sum(r => r.Quantity);

        var result = new OperationsOrderReportDto
        {
            Type = type,
            From = from,
            To = to,
            DocumentCount = documents.Count,
            LineCount = documents.Sum(t => t.Lines.Count),
            TotalQuantity = documents.Sum(t => t.Lines.Sum(l => l.Quantity)) - returnedQtyTotal,
            TotalAmount = documents.Sum(t => t.Amount) - returnedGoodsTotal,
            GoodsAmount = documents.Sum(t => t.Amount) - chequeIncomeTotal - returnedGoodsTotal,
            ChequeIncomeAmount = chequeIncomeTotal
        };
        result.AverageDocumentAmount = result.DocumentCount == 0
            ? 0m
            : result.GoodsAmount / result.DocumentCount;

        var groupByDay = GroupByDay(from, to, documents.Select(t => t.Date).Concat(returnRows.Select(r => r.Date)));
        var returnByBucket = returnRows.GroupBy(r => BucketStart(r.Date, groupByDay))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.GoodsWithVat));
        var allBuckets = documents.Select(t => BucketStart(t.Date, groupByDay))
            .Concat(returnByBucket.Keys).Distinct().OrderBy(d => d).ToList();
        var docByBucket = documents.ToLookup(t => BucketStart(t.Date, groupByDay));
        result.Trend = allBuckets
            .Select(key =>
            {
                var g = docByBucket[key];
                return new OperationsOrderTrendPoint
                {
                    PeriodStart = key,
                    Label = PeriodLabel(key, groupByDay),
                    DocumentCount = g.Count(),
                    Amount = g.Sum(t => t.Amount - chargeById.GetValueOrDefault(t.Id)) - returnByBucket.GetValueOrDefault(key)
                };
            })
            .ToList();

        var returnByParty = returnRows.Where(r => r.PartyId.HasValue).GroupBy(r => r.PartyId!.Value)
            .ToDictionary(g => g.Key, g => (Qty: g.Sum(x => x.Quantity), Amt: g.Sum(x => x.GoodsWithVat)));
        result.TopParties = documents
            .Where(t => t.PartyId.HasValue)
            .GroupBy(t => t.PartyId!.Value)
            .Select(g => new OperationsPartyReportRow
            {
                PartyId = g.Key,
                PartyName = partyNames.TryGetValue(g.Key, out var name) ? name : "—",
                DocumentCount = g.Count(),
                TotalQuantity = g.Sum(t => t.Lines.Sum(l => l.Quantity)) - (returnByParty.TryGetValue(g.Key, out var rp) ? rp.Qty : 0m),
                TotalAmount = g.Sum(t => t.Amount - chargeById.GetValueOrDefault(t.Id)) - (returnByParty.TryGetValue(g.Key, out var rp2) ? rp2.Amt : 0m)
            })
            .OrderByDescending(p => p.TotalAmount)
            .ThenBy(p => p.PartyName)
            .Take(10)
            .ToList();

        return result;
    }

    public async Task<ChequeIncomeReportDto> GetChequeIncomeReportAsync(DateTime? from, DateTime? to, int? partyId)
    {
        (from, to) = NormalizeRange(from, to);
        var query = _db.SaleChequeChargeEvents.AsNoTracking()
            .Include(e => e.AccVoucher) // optional historical link, never required for new shop events
            .Include(e => e.SaleChequeCharge).ThenInclude(c => c.Transaction)
            .Where(e => e.SaleChequeCharge.Transaction.Type == TransactionType.Sale);
        if (from.HasValue) query = query.Where(e => e.Date >= from.Value);
        if (to.HasValue)
        {
            var end = EndExclusive(to.Value);
            query = query.Where(e => e.Date < end);
        }
        if (partyId is > 0) query = query.Where(e => e.SaleChequeCharge.Transaction.PartyId == partyId);
        var events = await query.OrderBy(e => e.Date).ThenBy(e => e.Id).ToListAsync();
        foreach (var charge in events.Select(e => e.SaleChequeCharge).DistinctBy(c => c.Id))
            await SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync(_db, charge.Transaction);
        var ids = events.Select(e => e.SaleChequeCharge.Transaction.PartyId).Where(id => id.HasValue)
            .Select(id => id!.Value).Distinct().ToList();
        var names = await _db.Parties.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
        var result = new ChequeIncomeReportDto { From = from, To = to };
        foreach (var item in events)
        {
            var sale = item.SaleChequeCharge.Transaction;
            result.Rows.Add(new ChequeIncomeReportRow
            {
                Date = item.Date, SaleId = sale.Id, SaleNumber = sale.Number,
                PartyId = sale.PartyId, PartyName = sale.PartyId.HasValue && names.TryGetValue(sale.PartyId.Value, out var name) ? name : "—",
                EventId = item.Id, VoucherNumber = item.AccVoucher?.Number ?? 0,
                Kind = item.Kind, Income = item.IncomeDelta
            });
            result.TotalIncome += item.IncomeDelta;
        }
        return result;
    }

    public async Task<OperationsDashboardDto> GetDashboardAsync(DateTime? from, DateTime? to,
        bool includeOrders, bool includeRepairs, bool includeReferrers)
    {
        (from, to) = NormalizeRange(from, to);
        var result = new OperationsDashboardDto { From = from, To = to };

        var transactions = new List<TransactionReportRow>();
        if (includeOrders)
        {
            var query = _db.Transactions.AsNoTracking()
                .Where(t => t.Type == TransactionType.Purchase || t.Type == TransactionType.Sale || t.Type == TransactionType.SaleReturn);
            query = ApplyDateRange(query, from, to);
            var rows = await query.Select(t => new { t.Id, t.Type, t.Date, t.Amount, t.ReferrerId }).ToListAsync();
            var chargeQuery = _db.SaleChequeCharges.AsNoTracking()
                .Where(c => c.Transaction.Type == TransactionType.Sale);
            if (from.HasValue) chargeQuery = chargeQuery.Where(c => c.Transaction.Date >= from.Value.Date);
            if (to.HasValue)
            {
                var end = EndExclusive(to.Value);
                chargeQuery = chargeQuery.Where(c => c.Transaction.Date < end);
            }
            var chequeIncomes = await chargeQuery.ToDictionaryAsync(c => c.TransactionId, c => c.Amount);
            transactions = rows.Select(t => new TransactionReportRow
            {
                Type = t.Type,
                Date = t.Date,
                Amount = t.Type == TransactionType.Sale
                    ? SaleChequeChargeMath.GoodsTotal(t.Amount, chequeIncomes.GetValueOrDefault(t.Id))
                    : t.Amount,
                ReferrerId = t.ReferrerId
            }).ToList();

            var purchases = transactions.Where(t => t.Type == TransactionType.Purchase).ToList();
            var sales = transactions.Where(t => t.Type == TransactionType.Sale).ToList();
            var saleReturns = transactions.Where(t => t.Type == TransactionType.SaleReturn).ToList();
            result.PurchaseCount = purchases.Count;
            result.PurchaseTotal = purchases.Sum(t => t.Amount);
            result.SaleCount = sales.Count;
            result.SaleTotal = sales.Sum(t => t.Amount) - saleReturns.Sum(t => t.Amount);

            if (includeReferrers)
                result.TopReferrers = await BuildReferrerRankingAsync(sales);
            result.BestReferrer = result.TopReferrers.FirstOrDefault();
        }

        var repairAdmissions = new List<RepairOrder>();
        var deliveredRepairs = new List<RepairOrder>();
        if (includeRepairs)
        {
            var admissionQuery = _db.RepairOrders.AsNoTracking().Include(r => r.Items).AsQueryable();
            if (from.HasValue)
            {
                var start = from.Value.Date;
                admissionQuery = admissionQuery.Where(r => r.ReceivedAt >= start);
            }
            if (to.HasValue)
            {
                var endExclusive = EndExclusive(to.Value);
                admissionQuery = admissionQuery.Where(r => r.ReceivedAt < endExclusive);
            }
            repairAdmissions = await admissionQuery.ToListAsync();

            var deliveryQuery = _db.RepairOrders.AsNoTracking().Include(r => r.Items)
                .Where(r => r.Status == RepairStatus.Delivered && r.DeliveredAt.HasValue);
            if (from.HasValue)
            {
                var start = from.Value.Date;
                deliveryQuery = deliveryQuery.Where(r => r.DeliveredAt.HasValue && r.DeliveredAt.Value >= start);
            }
            if (to.HasValue)
            {
                var endExclusive = EndExclusive(to.Value);
                deliveryQuery = deliveryQuery.Where(r => r.DeliveredAt.HasValue && r.DeliveredAt.Value < endExclusive);
            }
            deliveredRepairs = await deliveryQuery.ToListAsync();

            result.RepairCount = repairAdmissions.Count;
            result.ActiveRepairCount = repairAdmissions.Count(r => r.Status != RepairStatus.Delivered && r.Status != RepairStatus.Cancelled);
            result.CancelledRepairCount = repairAdmissions.Count(r => r.Status == RepairStatus.Cancelled);
            result.DeliveredRepairCount = deliveredRepairs.Count;

            var deliveredItems = deliveredRepairs.SelectMany(r => r.Items).ToList();
            result.RepairRevenue = deliveredItems.Sum(i => i.Price * i.Quantity);
            result.RepairPartsCost = deliveredItems.Where(i => i.ProductId is > 0).Sum(i => i.Cost * i.Quantity);
            result.RepairLaborCost = deliveredItems.Where(i => i.ProductId is null or 0).Sum(i => i.Cost * i.Quantity);
            result.RepairCost = result.RepairPartsCost + result.RepairLaborCost;
            result.RepairProfit = result.RepairRevenue - result.RepairCost;

            var turnaroundDays = deliveredRepairs
                .Where(r => r.DeliveredAt.HasValue && r.DeliveredAt.Value >= r.ReceivedAt)
                .Select(r => (decimal)(r.DeliveredAt!.Value - r.ReceivedAt).TotalHours / 24m)
                .ToList();
            result.AverageRepairTurnaroundDays = turnaroundDays.Count == 0
                ? null
                : decimal.Round(turnaroundDays.Average(), 1, MidpointRounding.AwayFromZero);

            // کارهای باز فعلی مستقل از فیلتر تاریخ گزارش می‌شوند تا پذیرش‌های قدیمیِ معوق پنهان نشوند.
            var openQuery = _db.RepairOrders.AsNoTracking()
                .Where(r => r.Status != RepairStatus.Delivered && r.Status != RepairStatus.Cancelled);
            var openStatusCounts = await openQuery.GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            result.OpenRepairCount = openStatusCounts.Sum(x => x.Count);
            result.OpenReceivedCount = openStatusCounts.Where(x => x.Status == RepairStatus.Received).Select(x => x.Count).FirstOrDefault();
            result.OpenInProgressCount = openStatusCounts.Where(x => x.Status == RepairStatus.InProgress).Select(x => x.Count).FirstOrDefault();
            result.OpenReadyCount = openStatusCounts.Where(x => x.Status == RepairStatus.Ready).Select(x => x.Count).FirstOrDefault();

            var today = DateTime.Today;
            var threeDaysAgo = today.AddDays(-3);
            var sevenDaysAgo = today.AddDays(-7);
            var tomorrow = today.AddDays(1);
            result.OpenAge0To3DaysCount = await openQuery.CountAsync(r => r.ReceivedAt >= threeDaysAgo && r.ReceivedAt < tomorrow);
            result.OpenAge4To7DaysCount = await openQuery.CountAsync(r => r.ReceivedAt >= sevenDaysAgo && r.ReceivedAt < threeDaysAgo);
            result.OpenAgeOver7DaysCount = await openQuery.CountAsync(r => r.ReceivedAt < sevenDaysAgo);
        }

        var dates = transactions.Select(t => t.Date)
            .Concat(deliveredRepairs.Where(r => r.DeliveredAt.HasValue).Select(r => r.DeliveredAt!.Value))
            .ToList();
        var groupByDay = GroupByDay(from, to, dates);
        var trend = new SortedDictionary<DateTime, OperationsDashboardTrendPoint>();

        OperationsDashboardTrendPoint PointFor(DateTime date)
        {
            var periodStart = BucketStart(date, groupByDay);
            if (!trend.TryGetValue(periodStart, out var point))
            {
                point = new OperationsDashboardTrendPoint
                {
                    PeriodStart = periodStart,
                    Label = PeriodLabel(periodStart, groupByDay)
                };
                trend.Add(periodStart, point);
            }
            return point;
        }

        foreach (var transaction in transactions)
        {
            var point = PointFor(transaction.Date);
            if (transaction.Type == TransactionType.Purchase) point.Purchase += transaction.Amount;
            else if (transaction.Type == TransactionType.Sale) point.Sale += transaction.Amount;
            else if (transaction.Type == TransactionType.SaleReturn) point.Sale -= transaction.Amount;
        }

        // درآمد و هزینهٔ تعمیر در روند بر مبنای تاریخ تحویل ثبت می‌شود؛ برآورد تعمیرات باز درآمد قطعی نیست.
        foreach (var repair in deliveredRepairs.Where(r => r.DeliveredAt.HasValue))
        {
            var revenue = repair.Items.Sum(i => i.Price * i.Quantity);
            var partsCost = repair.Items.Where(i => i.ProductId is > 0).Sum(i => i.Cost * i.Quantity);
            var laborCost = repair.Items.Where(i => i.ProductId is null or 0).Sum(i => i.Cost * i.Quantity);
            var cost = partsCost + laborCost;
            var point = PointFor(repair.DeliveredAt!.Value);
            point.RepairRevenue += revenue;
            point.RepairPartsCost += partsCost;
            point.RepairLaborCost += laborCost;
            point.RepairCost += cost;
            point.RepairProfit += revenue - cost;
        }

        result.Trend = trend.Values.ToList();
        return result;
    }

    private async Task<List<OperationsReferrerRankDto>> BuildReferrerRankingAsync(List<TransactionReportRow> sales)
    {
        var ranking = sales
            .Where(t => t.ReferrerId is > 0)
            .GroupBy(t => t.ReferrerId!.Value)
            .Select(g => new { ReferrerId = g.Key, SaleCount = g.Count(), SalesAmount = g.Sum(t => t.Amount) })
            .OrderByDescending(g => g.SalesAmount)
            .ThenByDescending(g => g.SaleCount)
            .ThenBy(g => g.ReferrerId)
            .Take(5)
            .ToList();

        var ids = ranking.Select(r => r.ReferrerId).ToList();
        if (ids.Count == 0) return new List<OperationsReferrerRankDto>();

        var names = await _db.Referrers.AsNoTracking().Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name);

        return ranking.Select(r => new OperationsReferrerRankDto
        {
            ReferrerId = r.ReferrerId,
            ReferrerName = names.TryGetValue(r.ReferrerId, out var name) ? name : "معرف حذف‌شده",
            SaleCount = r.SaleCount,
            SalesAmount = r.SalesAmount
        }).ToList();
    }

    private IQueryable<Transaction> OrderQuery(TransactionType type, DateTime? from, DateTime? to,
        int? partyId, int? warehouseId)
    {
        var query = _db.Transactions.AsNoTracking().Where(t => t.Type == type);
        query = ApplyDateRange(query, from, to);
        if (partyId is > 0) query = query.Where(t => t.PartyId == partyId.Value);
        if (warehouseId is > 0) query = query.Where(t => t.WarehouseId == warehouseId.Value);
        return query;
    }

    private static IQueryable<Transaction> ApplyDateRange(IQueryable<Transaction> query, DateTime? from, DateTime? to)
    {
        if (from.HasValue)
        {
            var start = from.Value.Date;
            query = query.Where(t => t.Date >= start);
        }
        if (to.HasValue)
        {
            var endExclusive = EndExclusive(to.Value);
            query = query.Where(t => t.Date < endExclusive);
        }
        return query;
    }

    private static DateTime EndExclusive(DateTime value)
        => value.Date == DateTime.MaxValue.Date ? DateTime.MaxValue : value.Date.AddDays(1);

    private static void EnsureOrderType(TransactionType type)
    {
        if (type != TransactionType.Purchase && type != TransactionType.Sale)
            throw new InvalidOperationException("نوع گزارش باید خرید یا فروش باشد.");
    }

    private static (DateTime? From, DateTime? To) NormalizeRange(DateTime? from, DateTime? to)
    {
        if (from.HasValue) from = from.Value.Date;
        if (to.HasValue) to = to.Value.Date;
        if (from.HasValue && to.HasValue && from.Value > to.Value) (from, to) = (to, from);
        return (from, to);
    }

    private static bool GroupByDay(DateTime? from, DateTime? to, IEnumerable<DateTime> dates)
    {
        var values = dates.Select(d => d.Date).ToList();
        var start = from?.Date ?? (values.Count > 0 ? values.Min() : DateTime.Today.AddDays(-30));
        var end = to?.Date ?? (values.Count > 0 ? values.Max() : DateTime.Today);
        return Math.Abs((end - start).TotalDays) <= 45;
    }

    private static DateTime BucketStart(DateTime date, bool byDay)
    {
        if (byDay) return date.Date;
        var (year, month, _) = PersianDate.FromGregorian(date);
        return PersianDate.ToGregorian(year, month, 1);
    }

    private static string PeriodLabel(DateTime date, bool byDay)
        => byDay ? PersianDate.ToShortFa(date) : PersianDate.MonthLabel(date);

    private sealed record PeriodReturnRow(DateTime Date, int? PartyId, decimal Quantity, decimal GoodsWithVat);

    private async Task<List<PeriodReturnRow>> LoadSaleReturnsInPeriodAsync(
        DateTime? from, DateTime? to, int? partyId, int? warehouseId, bool? hasChequeIncome)
    {
        var q = _db.SaleChequeReturns.AsNoTracking()
            .Include(r => r.SaleTransaction)
            .Include(r => r.Lines)
            .AsQueryable();
        if (from.HasValue) q = q.Where(r => r.Date >= from.Value.Date);
        if (to.HasValue)
        {
            var end = EndExclusive(to.Value);
            q = q.Where(r => r.Date < end);
        }
        if (partyId is > 0) q = q.Where(r => r.SaleTransaction.PartyId == partyId.Value);
        if (warehouseId is > 0) q = q.Where(r => r.SaleTransaction.WarehouseId == warehouseId.Value);
        if (hasChequeIncome.HasValue)
            q = q.Where(r => _db.SaleChequeCharges.Any(c => c.Id == r.SaleChequeChargeId && c.Amount > 0) == hasChequeIncome.Value);
        var list = await q.ToListAsync();
        var rows = list.Select(r => new PeriodReturnRow(
            r.Date, r.SaleTransaction.PartyId, r.Lines.Sum(l => l.Quantity), r.GoodsAmount + r.VatAmount)).ToList();

        var cancelQ = _db.SaleChequeCancellations.AsNoTracking()
            .Include(c => c.SaleTransaction)
            .Include(c => c.ReturnTransaction).ThenInclude(rt => rt!.Lines)
            .Where(c => c.ReversedGoodsAmount + c.ReversedVatAmount > 0);
        if (from.HasValue) cancelQ = cancelQ.Where(c => c.Date >= from.Value.Date);
        if (to.HasValue)
        {
            var end = EndExclusive(to.Value);
            cancelQ = cancelQ.Where(c => c.Date < end);
        }
        if (partyId is > 0) cancelQ = cancelQ.Where(c => c.SaleTransaction.PartyId == partyId.Value);
        if (warehouseId is > 0) cancelQ = cancelQ.Where(c => c.SaleTransaction.WarehouseId == warehouseId.Value);
        if (hasChequeIncome.HasValue)
            cancelQ = cancelQ.Where(c => _db.SaleChequeCharges.Any(ch => ch.Id == c.SaleChequeChargeId && ch.Amount > 0) == hasChequeIncome.Value);
        var cancels = await cancelQ.ToListAsync();
        rows.AddRange(cancels.Select(c => new PeriodReturnRow(
            c.Date,
            c.SaleTransaction.PartyId,
            c.ReturnTransaction?.Lines.Sum(l => l.Quantity) ?? 0m,
            c.ReversedGoodsAmount + c.ReversedVatAmount)));
        return rows;
    }

    private sealed class TransactionReportRow
    {
        public TransactionType Type { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public int? ReferrerId { get; set; }
    }
}
