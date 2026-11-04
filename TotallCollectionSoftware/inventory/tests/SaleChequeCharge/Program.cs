using System.Data;
using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Api.Services.Invoicing;
using Inventory.Api.Services;
using Inventory.Api.Services.Reports;
using Inventory.Api.Services.Accounting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

Check(SaleChequeInitialCalculation.Calculate(200, 5, 5) == 50, "original monthly calculator: 200 × 5% × 5 = 50");
Check(SaleChequeInitialCalculation.Calculate(100, 0.5m, 1) == 0, "original calculator keeps midpoint-to-even rounding");
Check(SaleChequeInitialCalculation.Calculate(200, 0, 1) == 0, "zero-rate invoice has zero cheque income");
Check(SaleChequeChargeMath.GoodsTotal(250, 50) == 200 && SaleChequeChargeMath.GoodsTotal(200, 0) == 200,
    "goods-only dashboards exclude cheque income and retain ordinary sales");
try { SaleChequeChargeMath.GoodsTotal(49, 50); throw new Exception("invalid income exceeded invoice amount"); }
catch (InvalidOperationException) { Console.WriteLine("PASS: inconsistent invoice amount rejected"); }
try { SaleChequeInitialCalculation.Calculate(200, 3, 0); throw new Exception("zero months accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: zero months rejected"); }
try { SaleChequeInitialCalculation.Calculate(200, -3, 1); throw new Exception("negative rate accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: negative rate rejected"); }

var oldDue = new DateTime(2026, 1, 10);
Check(SaleChequeChargeMath.DateAdjustment(200, 3, 30, oldDue, oldDue.AddDays(10)) == 2, "ten-day extension: 200 × 3% / 30 × 10 = 2");
Check(SaleChequeChargeMath.DateAdjustment(200, 3, 30, oldDue, oldDue.AddDays(-10)) == -2, "earlier due date reverses adjustment");
Check(SaleChequeChargeMath.DateAdjustment(200, 3, 30, oldDue, oldDue) == 0, "unchanged due date");
// Compare repeated one-day reschedules against one direct change, including a 0.5-rial tie.
decimal stepwise = 0;
for (var day = 0; day < 10; day++)
    stepwise += SaleChequeChargeMath.IncrementalDateAdjustment(100, 5, 30, oldDue, oldDue.AddDays(day), oldDue.AddDays(day + 1));
Check(stepwise == SaleChequeChargeMath.DateAdjustment(100, 5, 30, oldDue, oldDue.AddDays(10)), "reschedules do not accumulate rounding drift");
Check(SaleChequeChargeMath.IncrementalDateAdjustment(200, 3, 30, oldDue, oldDue.AddDays(10), oldDue) == -2, "rescheduling back reverses the same amount");
Check(SaleChequeChargeMath.ProportionalReturn(200, 50, 0, 100) == 25, "returning half the base reverses 25 of 50 income");
Check(SaleChequeChargeMath.ProportionalRemainingReturn(200, 50, 90 + 10) == 25,
    "partial return uses goods including VAT as its base");
Check(SaleChequeChargeMath.ProportionalRemainingReturn(100, 35, 100) == 35,
    "returning the remaining goods reverses all remaining income after an intervening reschedule");
var returnLine = SaleChequeReturnPlanner.PlanLine(2, 0, 1, 180, 20);
var unpaidReturn = SaleChequeReturnPlanner.Plan(200, 50, 0, 0, 250, new[] { returnLine });
Check(returnLine.GoodsBeforeVat == 90 && returnLine.Vat == 10 && unpaidReturn.Income == 25 &&
      unpaidReturn.BalanceAfter == 125 && unpaidReturn.UncollectedChequeExcess == 125,
    "partial return includes VAT and identifies excess uncollected cheque without changing it");
var collectedReturn = SaleChequeReturnPlanner.Plan(200, 50, 0, 250, 0, new[] { returnLine });
Check(collectedReturn.BalanceAfter == -125 && collectedReturn.UncollectedChequeExcess == 0,
    "collected cheque is counted once and customer credit is retained");
var pieces = Enumerable.Range(0, 3).Select(i => SaleChequeReturnPlanner.PlanLine(3, i, 1, 1, 0.05m)).ToList();
Check(pieces.Sum(x => x.GoodsBeforeVat) == 1 && pieces.Sum(x => x.Vat) == 0.05m,
    "repeated partial line returns exhaust original goods and VAT without rounding drift");
var secondPiece = SaleChequeReturnPlanner.Plan(2, 1, 0, 0, 0,
    new[] { new SaleChequeReturnPlanner.Line(1, 0) }, 3, 1, 1);
Check(secondPiece.Income == 1, "repeated returns use cumulative rounding within an unchanged-rate epoch");
try { SaleChequeReturnPlanner.PlanLine(2, 1, 2, 180, 20); throw new Exception("over-return accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: quantity over-return rejected"); }
Check(SaleChequeChargeMath.ProportionalRemainingReturn(6, 1, 3) == 0 &&
      SaleChequeChargeMath.ProportionalRemainingReturn(6, 3, 3) == 2,
    "half-rial returns use exact midpoint-to-even rounding");
Check(SaleChequeChargeMath.ProportionalRemainingReturn(10000000000000000m, 9999999999999999m, 5000000000000000m) == 5000000000000000m,
    "large supported amounts do not overflow before division");
try { SaleChequeChargeMath.ProportionalRemainingReturn(100, 35, 101); throw new Exception("remaining-base over-return accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: return beyond remaining base rejected"); }
var returned = SaleChequeChargeMath.ProportionalReturn(3, 2, 0, 1)
    + SaleChequeChargeMath.ProportionalReturn(3, 2, 1, 1)
    + SaleChequeChargeMath.ProportionalReturn(3, 2, 2, 1);
Check(returned == 2, "partial returns add to the original income despite rounding");
try { SaleChequeChargeMath.ProportionalReturn(200, 50, 100, 101); throw new Exception("over-return accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: return beyond original base rejected"); }
Check(SaleChequeChargeMath.Split(50, -60) == (0m, 10m), "reduction beyond income becomes customer credit");
var extended = SaleChequeCorrectionAmounts.ForDueDate(2);
Check(extended.PartyDebit == 2 && extended.IncomeCredit == 2 && extended.TotalDebit == extended.TotalCredit,
    "ten-day extension increases customer debt and cheque income by 2");
var advanced = SaleChequeCorrectionAmounts.ForDueDate(-60);
Check(advanced.PartyCredit == 60 && advanced.IncomeDebit == 60 && advanced.TotalDebit == advanced.TotalCredit,
    "reduction beyond recorded income produces balanced customer credit");
var returnedGoods = SaleChequeCorrectionAmounts.ForReturn(90, 10, 25);
Check(returnedGoods.GoodsDebit == 90 && returnedGoods.VatDebit == 10 && returnedGoods.IncomeDebit == 25 &&
      returnedGoods.PartyCredit == 125 && returnedGoods.TotalDebit == returnedGoods.TotalCredit,
    "return separates sale, goods VAT and cheque income without a second receipt");
try { SaleChequeCorrectionAmounts.ForReturn(1, 0, -1); throw new Exception("negative returned income accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: negative returned income rejected"); }
try { SaleChequeCorrectionAmounts.ForDueDate(0.001m); throw new Exception("unsupported correction precision accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: invalid correction precision rejected"); }
Check(SaleChequeChargeMath.Split(50, 2) == (52m, 0m), "additional income");
try { SaleChequeChargeMath.DateAdjustment(200, 3, 0, oldDue, oldDue); throw new Exception("zero period accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: zero period rejected"); }

var voucher = SaleChequeVoucherAmounts.Create(200, 0, 50);
Check(voucher.CustomerDebit == 250 && voucher.GoodsCredit == 200 && voucher.ChequeIncomeCredit == 50 && voucher.TotalCredit == 250,
    "200 goods + 50 cheque income produces a balanced voucher");
var taxedVoucher = SaleChequeVoucherAmounts.Create(180, 20, 50);
Check(taxedVoucher.CustomerDebit == 250 && taxedVoucher.GoodsCredit == 180 && taxedVoucher.VatCredit == 20 && taxedVoucher.ChequeIncomeCredit == 50,
    "VAT on goods is posted separately; cheque income is not taxed");
try { SaleChequeVoucherAmounts.Create(100, 0, -1); throw new Exception("negative income accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: negative income rejected"); }
try { SaleChequeVoucherAmounts.Create(100.001m, 0, 0); throw new Exception("unsupported precision accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: unsupported ledger precision rejected"); }

await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = "PRAGMA foreign_keys=ON; CREATE TABLE Transactions (Id INTEGER PRIMARY KEY); INSERT INTO Transactions VALUES (1);";
    await cmd.ExecuteNonQueryAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await SaleChequeChargeSchemaV1.EnsureAsync(db);
    await SaleChequeChargeSchemaV1.EnsureAsync(db);
    await SaleChequeCalculatorSchemaV1.EnsureAsync(db);
    await SaleChequeCalculatorSchemaV1.EnsureAsync(db);
    await SaleChequeAccountsSnapshotSchemaV1.EnsureAsync(db);
    await SaleChequeAccountsSnapshotSchemaV1.EnsureAsync(db);
    cmd.CommandText = "CREATE TABLE AccVouchers (Id INTEGER PRIMARY KEY);";
    await cmd.ExecuteNonQueryAsync();
    await SaleChequeChargeEventsSchemaV1.EnsureAsync(db);
    await SaleChequeChargeEventsSchemaV1.EnsureAsync(db);
    cmd.CommandText = "CREATE TABLE FacRules (Id INTEGER PRIMARY KEY); INSERT INTO FacRules VALUES (1);";
    await cmd.ExecuteNonQueryAsync();
    await SaleChequeIncomeAccountSchemaV1.EnsureAsync(db);
    await SaleChequeIncomeAccountSchemaV1.EnsureAsync(db);
    cmd.CommandText = "CREATE TABLE Cheques (Id INTEGER PRIMARY KEY, DueDate TEXT NOT NULL); INSERT INTO Cheques VALUES (1, '2026-07-01');";
    await cmd.ExecuteNonQueryAsync();
    await ChequeOriginalDueDateSchemaV1.EnsureAsync(db);
    await ChequeOriginalDueDateSchemaV1.EnsureAsync(db);
    cmd.CommandText = "SELECT OriginalDueDate FROM Cheques WHERE Id=1";
    Check(await cmd.ExecuteScalarAsync() is DBNull, "old cheque original due date remains unknown");
    cmd.CommandText = "SELECT ChequeIncomeAccountId FROM FacRules WHERE Id=1";
    Check(await cmd.ExecuteScalarAsync() is DBNull, "legacy rule preserves an unset cheque income account");
    cmd.CommandText = "INSERT INTO SaleChequeCharges (TransactionId, BaseAmount, RatePercent, PeriodDays, InitialAmount, Amount, CustomerCredit) VALUES (1, '200', '3', 30, '50', '50', '0')";
    await cmd.ExecuteNonQueryAsync();
    cmd.CommandText = "SELECT COUNT(*) FROM SaleChequeCharges WHERE TransactionId=1";
    Check(Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1, "one charge for invoice; repair is idempotent");
    cmd.CommandText = "SELECT CalculatorMonths FROM SaleChequeCharges WHERE TransactionId=1";
    Check(await cmd.ExecuteScalarAsync() is DBNull, "legacy charge does not acquire invented calculator input");
    cmd.CommandText = "DELETE FROM Transactions WHERE Id=1; SELECT COUNT(*) FROM SaleChequeCharges";
    Check(Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 0, "charge cascades with deleted invoice");
}
// Actual rule service round-trip and legacy-client compatibility (requires .NET + SQLite provider).
await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    var sales = new AccAccount { Code = "4001", Name = "فروش کالا", Type = AccountType.Income, IsPermanent = false, IsActive = true, IsPostable = true };
    var finance = new AccAccount { Code = "5001", Name = "درآمد تاخیر تادیه چک", Type = AccountType.Income, IsPermanent = false, IsActive = true, IsPostable = true };
    var bad = new AccAccount { Code = "1001", Name = "دارایی", Type = AccountType.Asset, IsPermanent = true, IsActive = true, IsPostable = true };
    db.AccAccounts.AddRange(sales, finance, bad);
    await db.SaveChangesAsync();
    var service = new InvoicingService(db, null!, null!, NullLogger<InvoicingService>.Instance);
    var rule = new Inventory.Shared.Dtos.FacRule { Kind = InvoiceKind.Sale, MainAccountId = sales.Id, ChequeIncomeAccountId = finance.Id };
    var saved = await service.SaveRuleAsync(rule);
    Check(saved.ChequeIncomeAccountId == finance.Id && saved.ChequeIncomeAccountName!.Contains(finance.Name), "independent account saved and loaded");
    rule.ChequeIncomeAccountId = null;
    saved = await service.SaveRuleAsync(rule);
    Check(saved.ChequeIncomeAccountId == finance.Id, "legacy client omission preserves setting");
    rule.ChequeIncomeAccountId = 0;
    saved = await service.SaveRuleAsync(rule);
    Check(saved.ChequeIncomeAccountId is null, "explicit zero clears setting");
    rule.ChequeIncomeAccountId = bad.Id;
    try { await service.SaveRuleAsync(rule); throw new Exception("asset account accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: asset account rejected"); }
    db.ChangeTracker.Clear();
    rule.ChequeIncomeAccountId = sales.Id;
    try { await service.SaveRuleAsync(rule); throw new Exception("sales account accepted as cheque income"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: sales account rejected as cheque income"); }
}
// Posting is only callable inside an existing serializable transaction and must roll back with it.
await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    var party = new Party { Name = "مشتری آزمایش" };
    var wh = new Warehouse { Name = "انبار آزمایش" };
    var prod = new Product { Name = "کالا", Code = "TEST-POST", Unit = "عدد" };
    var ar = new AccAccount { Code = "1101", Name = "مشتری", Type = AccountType.Asset, IsPostable = true };
    var sales = new AccAccount { Code = "4101", Name = "فروش", Type = AccountType.Income, IsPermanent = false, IsPostable = true };
    var income = new AccAccount { Code = "5101", Name = "درآمد تاخیر تادیه چک", Type = AccountType.Income, IsPermanent = false, IsPostable = true };
    var vatAccount = new AccAccount { Code = "2101", Name = "مالیات", Type = AccountType.Liability, IsPostable = true };
    var cashAccount = new AccAccount { Code = "1102", Name = "بانک", Type = AccountType.Asset, IsPostable = true };
    var selectedBankAccount = new AccAccount { Code = "1103", Name = "بانک انتخاب‌شده", Type = AccountType.Asset, IsPostable = true };
    var year = new AccFiscalYear { Title = "آزمایش", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31) };
    db.AddRange(party, wh, prod, ar, sales, income, vatAccount, cashAccount, selectedBankAccount, year);
    await db.SaveChangesAsync();
    var sale = new Inventory.Api.Data.Transaction
    {
        Number = "SL-TEST", Type = TransactionType.Sale, Date = new DateTime(2026, 6, 1, 13, 45, 0), PartyId = party.Id,
        WarehouseId = wh.Id, Amount = 250, PaymentMethod = PaymentMethod.Cheque,
        Lines = new() { new TransactionLine { ProductId = prod.Id, Quantity = 1, Price = 200, GrossAmount = 200 } },
        Cheques = new() { new Cheque { Number = "123", Amount = 250, DueDate = new DateTime(2026, 7, 1), OriginalDueDate = new DateTime(2026, 7, 1) } }
    };
    db.Transactions.Add(sale);
    await db.SaveChangesAsync();
    db.SaleChequeCharges.Add(new SaleChequeCharge { TransactionId = sale.Id, BaseAmount = 200, RatePercent = 3, PeriodDays = 30,
        CalculatorBaseAmount = 200, CalculatorRatePercent = 5, CalculatorMonths = 5, InitialAmount = 50, Amount = 50 });
    db.FacRules.Add(new Inventory.Api.Data.FacRule { Kind = InvoiceKind.Sale, IsActive = true, AutoVoucher = true,
        MainAccountId = sales.Id, PartyAccountId = ar.Id, ChequeIncomeAccountId = income.Id, VatAccountId = vatAccount.Id, CashAccountId = cashAccount.Id });
    await db.SaveChangesAsync();
    var older = new Inventory.Api.Data.Transaction
    {
        Number = "SL-OLD", Type = TransactionType.Sale, Date = new DateTime(2026, 5, 15), PartyId = party.Id,
        WarehouseId = wh.Id, Amount = 100, SettledAmount = 40
    };
    db.Transactions.Add(older);
    await db.SaveChangesAsync();
    var statementService = new InventoryService(db, NullLogger<InventoryService>.Instance);
    var statement = await statementService.GetPartyStatementDetailAsync(party.Id, new DateTime(2026, 6, 1));
    var statementSummary = await statementService.GetPartyStatementSummaryReportAsync(from: new DateTime(2026, 6, 1));
    Check(statement.OpeningBalance == 60 && statement.ClosingBalance == 310, "opening includes older settlement and agrees with 200 + 50 invoice");
    Check(statement.SaleGoodsTotal == 200 && statement.SaleChequeIncomeTotal == 50 && statement.Rows.Single(r => r.Title == "تاخیر تادیه چک").Debit == 50,
        "statement shows separate goods and cheque income without double counting");
    Check(statementSummary.Items.Single(x => x.PartyId == party.Id).RemainingBalance == statement.ClosingBalance,
        "statement detail and summary closing balances agree");
    Check((await statementService.GetPartiesAsync(PartyType.Customer)).Single(x => x.Id == party.Id).Balance == statement.ClosingBalance,
        "customer list balance matches the statement after the legacy partial settlement");
    var sameDay = await statementService.GetPartyStatementDetailAsync(party.Id, new DateTime(2026, 6, 1), new DateTime(2026, 6, 1));
    var sameDaySummary = await statementService.GetPartyStatementSummaryReportAsync(from: new DateTime(2026, 6, 1), to: new DateTime(2026, 6, 1));
    Check(sameDay.ClosingBalance == 310 && sameDaySummary.Items.Single(x => x.PartyId == party.Id).RemainingBalance == 310,
        "statement includes transactions throughout the selected last day");
    var reports = new OperationsReportsService(db);
    var operationsDashboard = await reports.GetDashboardAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 1), true, false, false);
    Check(operationsDashboard.SaleCount == 1 && operationsDashboard.SaleTotal == 200,
        "operations dashboard reports goods-only sales, excluding 50 cheque income");
    var withCharge = await reports.GetOrderReportAsync(TransactionType.Sale, null, null, party.Id, wh.Id, true);
    var withoutCharge = await reports.GetOrderReportAsync(TransactionType.Sale, null, null, party.Id, wh.Id, false);
    Check(withCharge.DocumentCount == 1 && withCharge.GoodsAmount == 200 && withCharge.ChequeIncomeAmount == 50 && withCharge.TotalAmount == 250,
        "sales report separates goods, cheque income and total");
    Check(withoutCharge.DocumentCount == 1 && withoutCharge.TotalAmount == 100 && withoutCharge.ChequeIncomeAmount == 0,
        "legacy invoice without cheque income appears in without filter");
    Check(withCharge.AverageDocumentAmount == 200 && withCharge.Trend.Sum(x => x.Amount) == 200 && withCharge.TopParties.Single().TotalAmount == 200,
        "product statistics exclude cheque income");
    var filteredOrders = await statementService.GetOrdersAsync(TransactionType.Sale, null, null, party.Id, wh.Id, 1, 20, true);
    Check(filteredOrders.TotalCount == 1 && filteredOrders.Items.Single().ChequeIncomeAmount == 50 && filteredOrders.Items.Single().GoodsAmount == 200,
        "paged sales use same cheque-income filter and amounts");
    try { await statementService.ClearChequeAsync(sale.Cheques[0].Id); throw new Exception("legacy cheque clearing accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: legacy cheque clearing is blocked for charge invoices"); }
    try { await statementService.DeleteOrderAsync(sale.Id); throw new Exception("legacy invoice deletion accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: legacy invoice deletion is blocked for charge invoices"); }
    var invoicing = new InvoicingService(db, null!, null!, NullLogger<InvoicingService>.Instance);
    try { await invoicing.PostStoreSaleVoucherAsync(sale.Id, "tester"); throw new Exception("legacy posting accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: legacy posting is blocked for charge invoices"); }
    try { await SaleChequeChargePosting.PostAsync(db, sale.Id, "tester"); throw new Exception("posting outside transaction accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: transaction required"); }
    await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
    {
        var posted = await SaleChequeChargePosting.PostAsync(db, sale.Id, "tester");
        Check(posted.TotalDebit == 250 && posted.TotalCredit == 250, "200 goods + 50 cheque income balances at 250");
        Check(posted.Lines.Single(l => l.AccountId == ar.Id).Debit == 250 && posted.Lines.Single(l => l.AccountId == sales.Id).Credit == 200 && posted.Lines.Single(l => l.AccountId == income.Id).Credit == 50, "distinct debit and income accounts");
        var chargeSnapshot = await db.SaleChequeCharges.SingleAsync(x => x.TransactionId == sale.Id);
        Check(chargeSnapshot.IncomeAccountId == income.Id && chargeSnapshot.PartyAccountId == ar.Id, "voucher account IDs are snapshotted");
        var initialEvent = await db.SaleChequeChargeEvents.SingleAsync(e => e.AccVoucherId == posted.Id);
        Check(initialEvent.Kind == SaleChequeChargeEventKind.Initial && initialEvent.IncomeDelta == 50 && initialEvent.SaleChequeChargeId == chargeSnapshot.Id,
            "initial cheque-income event links the financial row to its voucher");
        try { await new AccountingService(db).CancelVoucherAsync(posted.Id, "tester"); throw new Exception("linked voucher cancellation accepted"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS: linked voucher cannot be cancelled independently"); }
        db.AccVouchers.Add(new AccVoucher
        {
            FiscalYearId = year.Id, Number = posted.Number + 1, Date = sale.Date, Source = VoucherSource.Manual,
            Status = VoucherStatus.Confirmed, TotalDebit = 7, TotalCredit = 7,
            Lines = new() { new AccVoucherLine { RowNo = 1, AccountId = ar.Id, Debit = 7 }, new AccVoucherLine { RowNo = 2, AccountId = income.Id, Credit = 7 } }
        });
        var configuredRule = await db.FacRules.SingleAsync(x => x.Kind == InvoiceKind.Sale);
        configuredRule.ChequeIncomeAccountId = sales.Id; // historical sale must retain the original account
        await db.SaveChangesAsync();
        var dashboard = await new AccountingService(db).GetDashboardAsync();
        Check(dashboard.IncomeTotal == 257 && dashboard.ChequeIncomeTotal == 50 && dashboard.OtherIncomeTotal == 207,
            "profit/loss separates initial cheque income without counting unrelated account activity");
        var chequeReport = await reports.GetChequeIncomeReportAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 1), party.Id);
        Check(chequeReport.Rows.Count == 1 && chequeReport.TotalIncome == 50 && chequeReport.Rows[0].PartyId == party.Id &&
              chequeReport.Rows[0].SaleNumber == sale.Number && chequeReport.Rows[0].VoucherNumber == posted.Number,
            "dedicated report filters confirmed income by date and customer without unrelated income");
        var missingCustomer = await reports.GetChequeIncomeReportAsync(null, null, party.Id + 9999);
        Check(missingCustomer.Rows.Count == 0 && missingCustomer.TotalIncome == 0, "income report filters by customer");
        var correction = new AccVoucher
        {
            FiscalYearId = year.Id, Number = posted.Number + 2, Date = new DateTime(2026, 6, 3), Source = VoucherSource.Manual,
            Status = VoucherStatus.Confirmed, TotalDebit = 3, TotalCredit = 3,
            Lines = new() { new AccVoucherLine { RowNo = 1, AccountId = income.Id, Debit = 3 }, new AccVoucherLine { RowNo = 2, AccountId = ar.Id, Credit = 3 } }
        };
        db.SaleChequeChargeEvents.Add(new SaleChequeChargeEvent { OperationId = Guid.NewGuid(), SaleChequeChargeId = chargeSnapshot.Id,
            AccVoucher = correction, Kind = SaleChequeChargeEventKind.DueDateAdjustment, Date = correction.Date, IncomeDelta = -3 });
        await db.SaveChangesAsync();
        var withCorrection = await reports.GetChequeIncomeReportAsync(null, null, party.Id);
        var correctedDashboard = await new AccountingService(db).GetDashboardAsync();
        Check(withCorrection.Rows.Count == 2 && withCorrection.TotalIncome == 47 && withCorrection.Rows.Any(r => r.Income == -3) &&
              correctedDashboard.ChequeIncomeTotal == 47, "signed correction events reduce report and profit/loss without touching goods");
        var changedEvent = await db.SaleChequeChargeEvents.SingleAsync(e => e.AccVoucherId == posted.Id);
        changedEvent.IncomeDelta = 51;
        await db.SaveChangesAsync();
        try { await reports.GetChequeIncomeReportAsync(null, null, party.Id); throw new Exception("event and voucher mismatch accepted"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS: mismatched income event rejected"); }
        try { await new AccountingService(db).GetDashboardAsync(); throw new Exception("dashboard accepted mismatched income event"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS: dashboard rejects mismatched income event"); }
        changedEvent.IncomeDelta = 50;
        chargeSnapshot.IncomeAccountId = null;
        await db.SaveChangesAsync();
        try { await reports.GetChequeIncomeReportAsync(null, null, party.Id); throw new Exception("income report ignored missing account snapshot"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS: income report rejects missing account snapshot"); }
        try { await new AccountingService(db).GetDashboardAsync(); throw new Exception("dashboard ignored missing account snapshot"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS: dashboard rejects missing account snapshot"); }
        chargeSnapshot.IncomeAccountId = income.Id;
        await db.SaveChangesAsync();
        configuredRule.ChequeIncomeAccountId = income.Id;
        await db.SaveChangesAsync();
        try { await SaleChequeChargePosting.PostAsync(db, sale.Id, "tester"); throw new Exception("duplicate posting accepted"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS: duplicate posting rejected"); }
        // Initial event remains 50; an earlier test correction was only synthetic, so remove it
        // before exercising the transactional reschedule workflow.
        db.SaleChequeChargeEvents.RemoveRange(await db.SaleChequeChargeEvents.Where(e => e.AccVoucherId == correction.Id).ToListAsync());
        db.AccVouchers.Remove(correction);
        await db.SaveChangesAsync();
        var operation = Guid.NewGuid();
        var rescheduled = await SaleChequeRescheduling.ApplyAsync(db, sale.Cheques[0].Id, operation,
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 11), new DateTime(2026, 6, 4), "tester");
        Check(rescheduled.IncomeDelta == 2 && chargeSnapshot.Amount == 52 && sale.Amount == 252 &&
              sale.Cheques[0].DueDate == new DateTime(2026, 7, 11),
            "ten-day reschedule atomically updates due date, income and invoice total");
        var retry = await SaleChequeRescheduling.ApplyAsync(db, sale.Cheques[0].Id, operation,
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 11), new DateTime(2026, 6, 4), "tester");
        Check(retry.Id == rescheduled.Id && await db.SaleChequeReschedules.CountAsync() == 1,
            "reschedule retry is idempotent");
        var revisedReport = await reports.GetChequeIncomeReportAsync(null, null, party.Id);
        Check(revisedReport.TotalIncome == 52, "reschedule event is linked to the confirmed income voucher");
        var beforeAdjustment = await statementService.GetPartyStatementDetailAsync(party.Id, null, new DateTime(2026, 6, 3));
        var afterAdjustment = await statementService.GetPartyStatementDetailAsync(party.Id, new DateTime(2026, 6, 4), new DateTime(2026, 6, 4));
        var afterSummary = await statementService.GetPartyStatementSummaryReportAsync(from: new DateTime(2026, 6, 4), to: new DateTime(2026, 6, 4));
        Check(beforeAdjustment.ClosingBalance == 310 && afterAdjustment.OpeningBalance == 310 &&
              afterAdjustment.ClosingBalance == 312 && afterAdjustment.SaleChequeIncomeTotal == 2 &&
              afterSummary.Items.Single(x => x.PartyId == party.Id).RemainingBalance == 312,
            "historical customer statement dates the correction rather than rewriting the original invoice");
        var original = new DateTime(2026, 7, 1);
        var dueAfterFirst = original.AddDays(10);
        for (var day = 0; day < 10; day++)
            await SaleChequeRescheduling.ApplyAsync(db, sale.Cheques[0].Id, Guid.NewGuid(), dueAfterFirst.AddDays(day),
                dueAfterFirst.AddDays(day + 1), new DateTime(2026, 6, 4), "tester");
        Check(sale.Amount == 254, "ten one-day reschedules preserve cumulative whole-rial rounding");
        await SaleChequeRescheduling.ApplyAsync(db, sale.Cheques[0].Id, Guid.NewGuid(), dueAfterFirst.AddDays(10),
            dueAfterFirst, new DateTime(2026, 6, 4), "tester");
        Check(sale.Amount == 252, "returning the due date reverses the exact cumulative adjustment");
        var receiptBank = new TrsAccount { Code = "BANK-RECEIPT", Name = "بانک وصول", Kind = TreasuryAccountKind.Bank,
            IsActive = true, AccountId = selectedBankAccount.Id };
        db.TrsAccounts.Add(receiptBank);
        await db.SaveChangesAsync();
        var receiptId = Guid.NewGuid();
        var received = await SaleChequeReceiptPosting.ReceiveAsync(db, sale.Cheques[0].Id, receiptId, receiptBank.Id,
            new DateTime(2026, 6, 5), "tester");
        var receivedAgain = await SaleChequeReceiptPosting.ReceiveAsync(db, sale.Cheques[0].Id, receiptId, receiptBank.Id,
            new DateTime(2026, 6, 5), "tester");
        Check(received.Id == receivedAgain.Id && sale.SettledAmount == 250 && sale.Cheques[0].IsCleared,
            "receipt is idempotent and does not add cheque income");
        Check(await db.SaleChequeStatusEvents.CountAsync(e => e.ChequeId == sale.Cheques[0].Id && e.NewStatus == ChequeStatus.Cleared) == 1,
            "receipt writes one immutable status event even after retry");
        var beforeReceipt = await statementService.GetPartyStatementDetailAsync(party.Id, null, new DateTime(2026, 6, 4));
        var afterReceipt = await statementService.GetPartyStatementDetailAsync(party.Id, new DateTime(2026, 6, 5), new DateTime(2026, 6, 5));
        Check(beforeReceipt.ClosingBalance == 312 && afterReceipt.OpeningBalance == 312 && afterReceipt.ClosingBalance == 62,
            "statement posts cheque receipt at its actual date without double counting");
        Check((await statementService.GetPartiesAsync(PartyType.Customer)).Single(x => x.Id == party.Id).Balance == afterReceipt.ClosingBalance,
            "customer list reflects the dated adjustment and cheque receipt exactly once");
        var treasury = new Inventory.Api.Services.Treasury.TreasuryService(db, new AccountingService(db));
        var receiptAccount = await treasury.GetAccountAsync(receiptBank.Id);
        Check(receiptAccount is not null && receiptAccount.Balance == 250, "receipt updates selected treasury bank balance");
        try { await new AccountingService(db).CancelVoucherAsync(received.AccVoucherId, "tester"); throw new Exception("receipt voucher cancellation accepted"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS: receipt voucher cannot be cancelled independently"); }
        var reversalId = Guid.NewGuid();
        await SaleChequeReceiptReversal.ReverseAsync(db, sale.Cheques[0].Id, reversalId, new DateTime(2026, 6, 6), "tester");
        await SaleChequeReceiptReversal.ReverseAsync(db, sale.Cheques[0].Id, reversalId, new DateTime(2026, 6, 6), "tester");
        Check(!sale.Cheques[0].IsCleared && sale.SettledAmount == 0 && received.ReversalVoucherId.HasValue,
            "receipt reversal is idempotent and preserves the original voucher");
        var afterReversal = await statementService.GetPartyStatementDetailAsync(party.Id, new DateTime(2026, 6, 6), new DateTime(2026, 6, 6));
        Check(afterReversal.OpeningBalance == 62 && afterReversal.ClosingBalance == 312 &&
              (await treasury.GetAccountAsync(receiptBank.Id))!.Balance == 0,
            "dated receipt reversal restores customer debt and reverses treasury bank movement once");
        Check((await statementService.GetPartiesAsync(PartyType.Customer)).Single(x => x.Id == party.Id).Balance == afterReversal.ClosingBalance,
            "customer list restores the debt after receipt reversal");
        // A reduction larger than the recorded income creates customer credit, not negative invoice income.
        var earlier = original.AddDays(-300);
        await SaleChequeRescheduling.ApplyAsync(db, sale.Cheques[0].Id, Guid.NewGuid(), dueAfterFirst, earlier,
            new DateTime(2026, 6, 6), "tester");
        Check(chargeSnapshot.Amount == 0 && chargeSnapshot.CustomerCredit == 10 && sale.Amount == 200,
            "an excessive early-date reduction becomes customer credit");
        var creditStatement = await statementService.GetPartyStatementDetailAsync(party.Id);
        Check(creditStatement.ClosingBalance == 250 &&
              (await statementService.GetPartiesAsync(PartyType.Customer)).Single(x => x.Id == party.Id).Balance == creditStatement.ClosingBalance,
            "customer balance includes excessive income reduction without losing credit");
        await SaleChequeRescheduling.ApplyAsync(db, sale.Cheques[0].Id, Guid.NewGuid(), earlier, dueAfterFirst,
            new DateTime(2026, 6, 6), "tester");
        var receivedThird = await SaleChequeReceiptPosting.ReceiveAsync(db, sale.Cheques[0].Id, Guid.NewGuid(), receiptBank.Id,
            new DateTime(2026, 6, 7), "tester");
        Check(receivedThird.Id != received.Id && sale.SettledAmount == 250,
            "a new collection is permitted after explicit reversal without deleting history");
        await tx.RollbackAsync();
    }
    db.ChangeTracker.Clear();
    Check(await db.AccVouchers.CountAsync() == 0 && await db.Transactions.AnyAsync(x => x.Id == sale.Id), "rollback removes voucher but not previously saved invoice");
    await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
    {
        var cheque = await db.Cheques.SingleAsync(x => x.TransactionId == sale.Id);
        cheque.Amount = 249;
        await db.SaveChangesAsync();
        try { await SaleChequeChargePosting.PostAsync(db, sale.Id, "tester"); throw new Exception("one-rial mismatch accepted"); }
        catch (InvalidOperationException ex)
        {
            Check(ex.Message.Contains("جمع اجزای پرداخت"), "one-rial mismatch requires manual resolution");
        }
        await tx.RollbackAsync();
    }
    db.ChangeTracker.Clear();
    await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
    {
        var changedCharge = await db.SaleChequeCharges.SingleAsync(x => x.TransactionId == sale.Id);
        changedCharge.CalculatorRatePercent = 4;
        await db.SaveChangesAsync();
        try { await SaleChequeChargePosting.PostAsync(db, sale.Id, "tester"); throw new Exception("calculator mismatch accepted"); }
        catch (InvalidOperationException ex)
        {
            Check(ex.Message.Contains("نتیجه ماشین‌حساب"), "initial calculator mismatch rejected");
        }
        await tx.RollbackAsync();
    }
    db.ChangeTracker.Clear();
    var closedYear = await db.AccFiscalYears.SingleAsync(x => x.Id == year.Id);
    closedYear.IsClosed = true;
    await db.SaveChangesAsync();
    await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
    {
        try { await SaleChequeChargePosting.PostAsync(db, sale.Id, "tester"); throw new Exception("closed fiscal year accepted"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS: closed fiscal year rejected"); }
        await tx.RollbackAsync();
    }
    db.ChangeTracker.Clear();
    closedYear = await db.AccFiscalYears.SingleAsync(x => x.Id == year.Id);
    closedYear.IsClosed = false;
    await db.SaveChangesAsync();
    sale = await db.Transactions.Include(x => x.Lines).Include(x => x.Cheques).SingleAsync(x => x.Id == sale.Id);
    sale.Lines[0].Price = 180; sale.Lines[0].GrossAmount = 180; sale.Lines[0].VatAmount = 20;
    sale.Cheques[0].Amount = 200; sale.CashAmount = 50; sale.SettledAmount = 50; sale.PaymentMethod = PaymentMethod.Mixed;
    var treasuryBank = new TrsAccount { Code = "BANK-TEST", Name = "بانک انتخابی", IsActive = true, AccountId = selectedBankAccount.Id };
    db.TrsAccounts.Add(treasuryBank);
    await db.SaveChangesAsync();
    sale.TrsAccountId = treasuryBank.Id;
    await db.SaveChangesAsync();
    await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
    {
        var posted = await SaleChequeChargePosting.PostAsync(db, sale.Id, "tester");
        Check(posted.TotalDebit == 300 && posted.TotalCredit == 300, "mixed payment records separate customer settlement");
        Check(posted.Lines.Single(l => l.AccountId == vatAccount.Id).Credit == 20 && posted.Lines.Single(l => l.AccountId == selectedBankAccount.Id).Debit == 50 && !posted.Lines.Any(l => l.AccountId == cashAccount.Id),
            "VAT and selected treasury bank are posted to their own accounts");
        await tx.RollbackAsync();
    }
    db.ChangeTracker.Clear();
    Inventory.Api.Data.Transaction NewRegistrationSale(string number) => new()
    {
        Number = number, Type = TransactionType.Sale, Date = new DateTime(2026, 6, 2), PartyId = party.Id,
        WarehouseId = wh.Id, Amount = 200, PaymentMethod = PaymentMethod.Cheque,
        Lines = new() { new TransactionLine { ProductId = prod.Id, Quantity = 1, Price = 200, GrossAmount = 200 } },
        Cheques = new() { new Cheque { Number = number, Amount = 250, DueDate = new DateTime(2026, 7, 2), OriginalDueDate = new DateTime(2026, 7, 2) } }
    };
    var registrationInput = new SaleChequeChargeRegistrationInput(200, 5, 5, 3, 30, null);
    await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
    {
        var newSale = NewRegistrationSale("SL-REG");
        db.Transactions.Add(newSale);
        await db.SaveChangesAsync();
        var posted = await SaleChequeChargeRegistration.RegisterAsync(db, newSale.Id, registrationInput, "tester");
        var registeredCharge = await db.SaleChequeCharges.SingleAsync(x => x.TransactionId == newSale.Id);
        Check(newSale.Amount == 250 && registeredCharge.Amount == 50 && registeredCharge.BaseAmount == 200 &&
              registeredCharge.RatePercent == 3 && posted.TotalDebit == 250, "initial invoice, charge and voucher registered in one transaction");
        await tx.RollbackAsync();
    }
    db.ChangeTracker.Clear();
    Check(!await db.Transactions.AnyAsync(x => x.Number == "SL-REG"), "invoice and charge registration roll back together");
    await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
    {
        var mixed = NewRegistrationSale("SL-MIXED");
        mixed.PaymentMethod = PaymentMethod.Mixed;
        mixed.Cheques[0].Amount = 200;
        mixed.CashAmount = 50; mixed.SettledAmount = 50; mixed.CashType = CashType.Cash;
        mixed.TrsAccountId = treasuryBank.Id;
        db.Transactions.Add(mixed);
        await db.SaveChangesAsync();
        var posted = await SaleChequeChargeRegistration.RegisterAsync(db, mixed.Id, registrationInput, "tester");
        var cashReceipt = await db.TrsVouchers.Include(v => v.Lines).SingleAsync(v => v.VoucherId == posted.Id);
        Check(cashReceipt.TotalAmount == 50 && cashReceipt.Status == TreasuryStatus.Confirmed &&
              cashReceipt.Lines.Single().TrsAccountId == treasuryBank.Id && posted.TotalDebit == 300,
            "mixed cash and cheque writes exactly one treasury cash receipt linked to the initial voucher");
        var treasury = new Inventory.Api.Services.Treasury.TreasuryService(db, new AccountingService(db));
        try { await treasury.UnconfirmVoucherAsync(cashReceipt.Id, "tester"); throw new Exception("linked treasury voucher unconfirmed"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS: linked treasury receipt cannot be reversed independently"); }
        await tx.RollbackAsync();
    }
    db.ChangeTracker.Clear();
    Check(!await db.Transactions.AnyAsync(x => x.Number == "SL-MIXED"), "cash receipt and sale roll back together");
    // The ordinary sale path freezes actual legacy stock cost, not the selling price or an input value.
    var costProduct = new Product { Name = "کالای بهای آزمون", Code = "COST-SNAPSHOT", Unit = "عدد", PurchasePrice = 80 };
    db.Products.Add(costProduct);
    await db.SaveChangesAsync();
    db.Transactions.Add(new Inventory.Api.Data.Transaction
    {
        Number = "PR-COST", Type = TransactionType.Purchase, Date = new DateTime(2026, 5, 1), WarehouseId = wh.Id,
        PartyId = party.Id, Amount = 800,
        Lines = new() { new TransactionLine { ProductId = costProduct.Id, Quantity = 10, Price = 80, GrossAmount = 800 } }
    });
    db.Stocks.Add(new Stock { WarehouseId = wh.Id, ProductId = costProduct.Id, Quantity = 10, AvgCost = 80 });
    await db.SaveChangesAsync();
    var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["SaleChequeCharge:ExperimentalEnabled"] = "true" }).Build();
    var creationService = new InventoryService(db, NullLogger<InventoryService>.Instance, cfg);
    var created = await creationService.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = wh.Id, PartyId = party.Id, Date = new DateTime(2026, 6, 2),
        PaymentMethod = PaymentMethod.Cheque,
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = costProduct.Id, Quantity = 1, Price = 200, UnitCostSnapshot = 5 } },
        Cheques = new() { new Inventory.Shared.Dtos.ChequeDto { Number = "COST-CHEQUE", Amount = 250, DueDate = new DateTime(2026, 7, 2) } },
        ChequeCharge = new Inventory.Shared.Dtos.SaleChequeChargeInput { CalculatorBaseAmount = 200, CalculatorRatePercent = 5,
            CalculatorMonths = 5, AdjustmentRatePercent = 3, AdjustmentPeriodDays = 30 }
    }, "tester");
    var savedLine = await db.TransactionLines.SingleAsync(l => l.TransactionId == created.Id);
    Check(savedLine.UnitCostSnapshot == 80, "charged sale stores legacy cost instead of an input cost or sale price");

    // End-to-end return, decision, post-return reschedule, cheque replacement, credit settlement, installments, edit and void.
    var e2eParty = new Party { Type = PartyType.Customer, Name = "مشتری گردش کامل", IsActive = true };
    db.Parties.Add(e2eParty);
    await db.SaveChangesAsync();
    treasuryBank.OpeningBalance = 1000;
    await db.SaveChangesAsync();

    var fullSale = await creationService.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = wh.Id, PartyId = e2eParty.Id, Date = new DateTime(2026, 6, 3),
        PaymentMethod = PaymentMethod.Cheque,
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = costProduct.Id, Quantity = 2, Price = 90, VatValue = 200 } },
        // Price 90 * 2 = 180; VatValue 200 (>100 => fixed 20? Wait: use VatValue = 10% => 18, or Price=100, VatValue=0 => 200)
        Cheques = new() { new Inventory.Shared.Dtos.ChequeDto { Number = "CHQ-E2E-1", Amount = 250, DueDate = new DateTime(2026, 7, 3) } },
        ChequeCharge = new Inventory.Shared.Dtos.SaleChequeChargeInput
        {
            CalculatorBaseAmount = 200, CalculatorRatePercent = 5, CalculatorMonths = 5,
            AdjustmentRatePercent = 3, AdjustmentPeriodDays = 30
        }
    }, "tester");
    // Wait: 2 * 90 = 180 + fixed VAT 200 = 380! Keep exact 200+50=250 by using Price=90, VatValue=11.11? Even cleaner: Price=100, VatValue=0 => 200!
    // Let's cancel fullSale cleanly (exercising CancelSaleChequeOrderAsync!) and issue exact 200 (180 + 10% VAT on 200 -> Price=100, DiscountAmount=20, DiscountIsAmount=true, VatValue=111.11? No: Quantity=2, Price=90 => 180; VatValue=100.01? Wait: VatValue > 100 is fixed rial, so VatValue=120 is 120 rial, whereas 20% VAT is VatValue=20 => 180*20%=36, or Price=100, DiscountAmount=0, VatValue=0 => 200!).
    await creationService.CancelSaleChequeOrderAsync(fullSale.Id, new Inventory.Shared.Dtos.SaleChequeCancelCommand
    {
        OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 3), Reason = "تست ابطال کامل"
    }, "tester");
    var stmtAfterCancel = await creationService.GetPartyStatementDetailAsync(e2eParty.Id);
    Check(stmtAfterCancel.ClosingBalance == 0 && stmtAfterCancel.SaleGoodsTotal == 0 && stmtAfterCancel.SaleChequeIncomeTotal == 0,
        "atomic cancellation zeroes customer statement, goods total and cheque income");

    var sale250 = await creationService.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = wh.Id, PartyId = e2eParty.Id, Date = new DateTime(2026, 6, 4),
        PaymentMethod = PaymentMethod.Cheque,
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = costProduct.Id, Quantity = 2, Price = 100, VatValue = 0 } },
        Cheques = new() { new Inventory.Shared.Dtos.ChequeDto { Number = "CHQ-250", Amount = 250, DueDate = new DateTime(2026, 7, 4) } },
        ChequeCharge = new Inventory.Shared.Dtos.SaleChequeChargeInput
        {
            CalculatorBaseAmount = 200, CalculatorRatePercent = 5, CalculatorMonths = 5,
            AdjustmentRatePercent = 3, AdjustmentPeriodDays = 30
        }
    }, "tester");
    var origLineId = sale250.Lines.Single().Id;
    var retPreview = await creationService.PreviewSaleChequeReturnAsync(sale250.Id, new Inventory.Shared.Dtos.SaleChequeReturnPreviewRequest
    {
        Date = new DateTime(2026, 6, 5),
        Lines = new() { new Inventory.Shared.Dtos.SaleChequeReturnLineDto { OriginalLineId = origLineId, Quantity = 1 } }
    });
    Check(retPreview.GoodsBeforeVat == 100 && retPreview.Income == 25 && retPreview.BalanceAfter == 125 && retPreview.UncollectedChequeExcess == 125,
        "partial return preview computes proportional income and uncleared cheque excess");
    try
    {
        await creationService.ReturnSaleChequeOrderAsync(sale250.Id, new Inventory.Shared.Dtos.SaleChequeReturnCommand
        {
            OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 5),
            Lines = new() { new Inventory.Shared.Dtos.SaleChequeReturnLineDto { OriginalLineId = origLineId, Quantity = 1 } },
            Decision = SaleChequeReturnDecision.None, ConfirmedTotalCredit = 125
        }, "tester");
        throw new Exception("missing cheque decision accepted");
    }
    catch (InvalidOperationException) { Console.WriteLine("PASS: return with uncleared cheque excess requires explicit customer decision"); }

    await creationService.ReturnSaleChequeOrderAsync(sale250.Id, new Inventory.Shared.Dtos.SaleChequeReturnCommand
    {
        OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 5),
        Lines = new() { new Inventory.Shared.Dtos.SaleChequeReturnLineDto { OriginalLineId = origLineId, Quantity = 1 } },
        Decision = SaleChequeReturnDecision.ReplaceCheque, ConfirmedTotalCredit = 125
    }, "tester");
    var stockAfterReturn = await db.Stocks.SingleAsync(s => s.WarehouseId == wh.Id && s.ProductId == costProduct.Id);
    Check(stockAfterReturn.AvgCost == 80, "returned item restores stock at frozen cost rather than sale price");

    // Reschedule after partial return uses remaining base (100 * 3% / 30 * 10 = 1)
    var postReturnReschedule = await creationService.RescheduleSaleChequeAsync(sale250.Cheques[0].Id,
        new Inventory.Shared.Dtos.SaleChequeRescheduleCommand
        {
            OperationId = Guid.NewGuid(), ExpectedDueDate = new DateTime(2026, 7, 4),
            NewDueDate = new DateTime(2026, 7, 14), OperationDate = new DateTime(2026, 6, 6), ConfirmedIncomeDelta = 1
        }, "tester");
    Check(postReturnReschedule.BaseAmount == 100 && postReturnReschedule.IncomeDelta == 1,
        "reschedule after partial return uses remaining goods base");

    // Replace 250 cheque with 126 cheque matching remaining balance (100 goods + 26 income)
    await creationService.ReplaceSaleChequeAsync(sale250.Cheques[0].Id, new Inventory.Shared.Dtos.SaleChequeReplaceCommand
    {
        OperationId = Guid.NewGuid(), OperationDate = new DateTime(2026, 6, 6),
        Number = "CHQ-126", Amount = 126, DueDate = new DateTime(2026, 7, 14), BankName = "ملت"
    }, "tester");
    var refreshed250 = await creationService.GetOrderAsync(sale250.Id);
    var activeCheque = refreshed250!.Cheques.Single(c => c.Status == ChequeStatus.InHand);
    Check(refreshed250.Cheques.Any(c => c.Status == ChequeStatus.Cancelled) && activeCheque.Amount == 126,
        "cheque replacement preserves old cheque in history and activates replacement cheque");

    // Collect 126 cheque, then return remaining 1 unit -> creates 126 customer credit -> refund 26 + apply 100 to a new sale
    await creationService.ReceiveSaleChequeAsync(activeCheque.Id, new Inventory.Shared.Dtos.SaleChequeReceiptCommand
    {
        OperationId = Guid.NewGuid(), TrsAccountId = treasuryBank.Id, Date = new DateTime(2026, 6, 7)
    }, "tester");
    await creationService.ReturnSaleChequeOrderAsync(sale250.Id, new Inventory.Shared.Dtos.SaleChequeReturnCommand
    {
        OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 8),
        Lines = new() { new Inventory.Shared.Dtos.SaleChequeReturnLineDto { OriginalLineId = origLineId, Quantity = 1 } },
        Decision = SaleChequeReturnDecision.None, ConfirmedTotalCredit = 126
    }, "tester");
    var afterFullReturn = await creationService.GetOrderAsync(sale250.Id);
    Check(afterFullReturn!.AvailableCustomerCredit == 126, "return after cheque collection records customer credit without double counting");

    await creationService.RefundSaleChequeCreditAsync(sale250.Id, new Inventory.Shared.Dtos.SaleChequeCreditRefundCommand
    {
        OperationId = Guid.NewGuid(), TrsAccountId = treasuryBank.Id, Amount = 26, Date = new DateTime(2026, 6, 9)
    }, "tester");
    var secondSale = await creationService.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = wh.Id, PartyId = e2eParty.Id, Date = new DateTime(2026, 6, 9),
        PaymentMethod = PaymentMethod.Credit, DueDate = new DateTime(2026, 7, 9),
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = costProduct.Id, Quantity = 1, Price = 100, VatValue = 0 } }
    }, "tester");
    await creationService.ApplySaleChequeCreditAsync(sale250.Id, new Inventory.Shared.Dtos.SaleChequeCreditAllocationCommand
    {
        OperationId = Guid.NewGuid(), TargetSaleId = secondSale.Id, Amount = 100, Date = new DateTime(2026, 6, 10)
    }, "tester");
    var finalE2EStmt = await creationService.GetPartyStatementDetailAsync(e2eParty.Id);
    var finalE2EParty = (await creationService.GetPartiesAsync(PartyType.Customer)).Single(p => p.Id == e2eParty.Id);
    Check(finalE2EStmt.ClosingBalance == 0 && finalE2EParty.Balance == 0,
        "cash refund and credit allocation settle customer credit to zero with no double counting");

    // Charged sale with installment + cheque, installment receipt & reversal, and atomic edit
    var instSale = await creationService.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = wh.Id, PartyId = e2eParty.Id, Date = new DateTime(2026, 6, 11),
        PaymentMethod = PaymentMethod.Mixed,
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = costProduct.Id, Quantity = 2, Price = 100, VatValue = 0 } },
        Cheques = new() { new Inventory.Shared.Dtos.ChequeDto { Number = "CHQ-INST", Amount = 150, DueDate = new DateTime(2026, 7, 11) } },
        Installments = new() { new Inventory.Shared.Dtos.InstallmentDto { No = 1, Amount = 100, DueDate = new DateTime(2026, 7, 11) } },
        ChequeCharge = new Inventory.Shared.Dtos.SaleChequeChargeInput
        {
            CalculatorBaseAmount = 200, CalculatorRatePercent = 5, CalculatorMonths = 5,
            AdjustmentRatePercent = 3, AdjustmentPeriodDays = 30
        }
    }, "tester");
    var editedSale = await creationService.EditSaleChequeOrderAsync(instSale.Id, new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = wh.Id, PartyId = e2eParty.Id, Date = new DateTime(2026, 6, 11),
        PaymentMethod = PaymentMethod.Mixed,
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = costProduct.Id, Quantity = 2, Price = 100, VatValue = 0 } },
        Cheques = new() { new Inventory.Shared.Dtos.ChequeDto { Number = "CHQ-INST-2", Amount = 150, DueDate = new DateTime(2026, 7, 11) } },
        Installments = new() { new Inventory.Shared.Dtos.InstallmentDto { No = 1, Amount = 100, DueDate = new DateTime(2026, 7, 11) } },
        ChequeCharge = new Inventory.Shared.Dtos.SaleChequeChargeInput
        {
            CalculatorBaseAmount = 200, CalculatorRatePercent = 5, CalculatorMonths = 5,
            AdjustmentRatePercent = 3, AdjustmentPeriodDays = 30
        }
    }, "tester");
    var instId = editedSale.Installments.Single().Id;
    await creationService.ReceiveSaleChequeInstallmentAsync(instId, new Inventory.Shared.Dtos.SaleChequeInstallmentReceiptCommand
    {
        OperationId = Guid.NewGuid(), TrsAccountId = treasuryBank.Id, Date = new DateTime(2026, 6, 12)
    }, "tester");
    await creationService.ReverseSaleChequeInstallmentReceiptAsync(instId, new Inventory.Shared.Dtos.SaleChequeInstallmentReversalCommand
    {
        OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 13)
    }, "tester");
    Check((await creationService.GetOrderAsync(editedSale.Id))!.SettledAmount == 0,
        "charged installment receipt and reversal reconcile settled amount");

    var rule = await db.FacRules.SingleAsync(x => x.Kind == InvoiceKind.Sale);
    rule.ChequeIncomeAccountId = null;
    await db.SaveChangesAsync();
    await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
    {
        var failedSale = NewRegistrationSale("SL-FAILED");
        db.Transactions.Add(failedSale);
        await db.SaveChangesAsync();
        try { await SaleChequeChargeRegistration.RegisterAsync(db, failedSale.Id, registrationInput, "tester"); throw new Exception("registration without account accepted"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS: missing income account aborts the whole sale"); }
        Check(!db.ChangeTracker.Entries().Any(), "failed registration detaches rolled-back entities from a reused context");
    }
    await db.SaveChangesAsync(); // A reused context must not reinsert any part of the failed sale.
    Check(!await db.Transactions.AnyAsync(x => x.Number == "SL-FAILED") &&
          !await db.SaleChequeCharges.AnyAsync(c => c.Transaction.Number == "SL-FAILED"),
        "failure after charge save rolls back original invoice and leaves no tracked financial row");
}
await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer("Server=localhost;Database=not-used;Integrated Security=True;TrustServerCertificate=True").Options))
{
    const string migration = "20261104140000_AddSaleChequeCharge";
    Check(db.Database.GetMigrations().Contains(migration), "SQL Server migration discoverable");
    var script = db.GetService<IMigrator>().GenerateScript("20261104130000_AddOrderCashTracking", migration);
    Check(script.Contains("SaleChequeCharges") && script.Contains("TransactionId"), "SQL Server migration creates financial row");
    const string accountMigration = "20261104150000_AddSaleChequeIncomeAccount";
    Check(db.Database.GetMigrations().Contains(accountMigration), "account migration discoverable");
    var accountScript = db.GetService<IMigrator>().GenerateScript(migration, accountMigration);
    Check(accountScript.Contains("ChequeIncomeAccountId"), "SQL Server migration adds independent account setting");
    const string dueMigration = "20261104160000_AddChequeOriginalDueDate";
    Check(db.Database.GetMigrations().Contains(dueMigration), "original due date migration discoverable");
    var dueScript = db.GetService<IMigrator>().GenerateScript(accountMigration, dueMigration);
    Check(dueScript.Contains("OriginalDueDate"), "SQL Server migration adds original due date without backfilling");
    const string calcMigration = "20261104170000_AddSaleChequeCalculatorSnapshot";
    Check(db.Database.GetMigrations().Contains(calcMigration), "calculator snapshot migration discoverable");
    var calcScript = db.GetService<IMigrator>().GenerateScript(dueMigration, calcMigration);
    Check(calcScript.Contains("CalculatorBaseAmount") && calcScript.Contains("CalculatorMonths"), "SQL Server migration adds calculator snapshot");
    const string accountSnapshotMigration = "20261104180000_AddSaleChequeAccountSnapshots";
    Check(db.Database.GetMigrations().Contains(accountSnapshotMigration), "account snapshot migration discoverable");
    var accountSnapshotScript = db.GetService<IMigrator>().GenerateScript(calcMigration, accountSnapshotMigration);
    Check(accountSnapshotScript.Contains("IncomeAccountId") && accountSnapshotScript.Contains("PartyAccountId"), "SQL Server migration adds historical account IDs");
    const string eventsMigration = "20261104190000_AddSaleChequeChargeEvents";
    Check(db.Database.GetMigrations().Contains(eventsMigration), "cheque income events migration discoverable");
    var eventsScript = db.GetService<IMigrator>().GenerateScript(accountSnapshotMigration, eventsMigration);
    Check(eventsScript.Contains("SaleChequeChargeEvents") && eventsScript.Contains("OperationId"), "SQL Server migration adds financial event history");
    foreach (var (mig, tableName) in new[]
    {
        ("20261105000000_AddSaleChequeReturns", "SaleChequeReturns"),
        ("20261105010000_AddSaleChequeReplacements", "SaleChequeReplacements"),
        ("20261105020000_AddTreasuryAccountCompany", "IX_TrsAccounts_CompanyId"),
        ("20261105030000_AddSaleChequeCreditSettlements", "SaleChequeCreditSettlements"),
        ("20261105040000_AddSaleChequeInstallmentReceipts", "SaleChequeInstallmentReceipts"),
        ("20261105050000_AddSaleChequeCancellations", "SaleChequeCancellations")
    })
    {
        Check(db.Database.GetMigrations().Contains(mig), $"migration {mig} discoverable");
        var migScript = db.GetService<IMigrator>().GenerateScript("20261104230000_AddSaleChequeReceiptReversals", mig);
        Check(migScript.Contains(tableName), $"SQL Server script contains {tableName}");
    }
}
