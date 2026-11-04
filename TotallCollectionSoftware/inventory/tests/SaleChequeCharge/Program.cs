using System.Data;
using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Api.Services.Invoicing;
using Inventory.Api.Services;
using Inventory.Api.Services.Reports;
using Inventory.Api.Services.Accounting;
using Microsoft.Extensions.Logging.Abstractions;
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
}
