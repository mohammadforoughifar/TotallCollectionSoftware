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
using Microsoft.AspNetCore.Http;
using Inventory.Api.Infrastructure;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

// This method must accept the EF treasury entity, not the similarly named shared DTO.
Func<Inventory.Api.Data.TrsVoucher, Inventory.Api.Data.Transaction, bool> legacyVoucherMatcher = ShopOrderCashLedger.MatchesLegacyVoucher;
Check(legacyVoucherMatcher.Method.GetParameters()[0].ParameterType == typeof(Inventory.Api.Data.TrsVoucher),
    "legacy cash reconciliation uses the database treasury voucher");

var saleOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Shop.Sale.Manage" };
var purchaseOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Shop.Purchase.Manage" };
Check(ShopPermissionAuthorizer.OrderType(saleOnly, TransactionType.Sale, true) &&
      !ShopPermissionAuthorizer.OrderType(saleOnly, TransactionType.Purchase, true) &&
      ShopPermissionAuthorizer.OrderType(purchaseOnly, TransactionType.Purchase, true) &&
      !ShopPermissionAuthorizer.OrderType(purchaseOnly, TransactionType.Sale, true),
    "sale and purchase write permissions are independent");
Check(!ShopSections.AliasAllows("Orders.ViewProfit", "Shop.Sale.Manage") &&
      !ShopSections.AliasAllows("Orders.Create", "Shop.OrderReports.View") &&
      ShopSections.AliasAllows("ReportPages.Kardex", "Shop.Kardex.View"),
    "profit is explicit and read-only reports never grant writes");

Check(Fa.MoneyExact(200.49m) == "۲۰۰.۴۹" && Fa.MoneyExact(250m) == "۲۵۰",
    "money formatting retains recorded fractional rials without adding zero decimals");
Check(Fa.FormatTyping("۲۰۰٫۴۹") == "200.49" && Fa.ParseDecimal(Fa.FormatTyping("۲۰۰٫۴۹")) == 200.49m,
    "Persian decimal input remains a decimal in the calculator and payment fields");
Check(SaleChequeInitialCalculation.Calculate(200, 5, 5) == 50, "original monthly calculator: 200 × 5% × 5 = 50");
Check(SaleChequeInitialCalculation.Calculate(100, 0.5m, 1) == 0, "original calculator keeps midpoint-to-even rounding");
Check(SaleChequeInitialCalculation.Calculate(200, 0, 1) == 0, "zero-rate invoice has zero cheque income");
// The client and server share this exact plan; payment rows and the income snapshot
// must use the same basis, rate and month count, including on a repeated calculation.
var calculatedPlan = InvoiceInstallmentCalculation.Create(200m, 5m, 5);
var calculatedTerms = calculatedPlan.CreateSaleChequeCharge(200m, 5m, 30);
Check(calculatedPlan.Interest == 50m && calculatedPlan.Total == 250m &&
      calculatedPlan.RegularAmount == 50m && calculatedPlan.LastAmount == 50m &&
      calculatedTerms is not null && calculatedTerms.CalculatorBaseAmount == 200m &&
      calculatedTerms.CalculatorRatePercent == 5m && calculatedTerms.CalculatorMonths == 5 &&
      calculatedTerms.AdjustmentRatePercent == 5m && calculatedTerms.AdjustmentPeriodDays == 30,
    "one calculator creates 250 of cheque rows and a 50 income snapshot");
var repeatedPlan = InvoiceInstallmentCalculation.Create(calculatedPlan.BaseAmount, calculatedPlan.MonthlyRatePercent, calculatedPlan.Months);
Check(repeatedPlan == calculatedPlan, "recalculation does not charge interest on prior interest");
var mixedPlan = InvoiceInstallmentCalculation.Create(200m - 50m - 20m, 5m, 2);
Check(mixedPlan.Interest == 13m && mixedPlan.RegularAmount == 71m && mixedPlan.LastAmount == 72m &&
      mixedPlan.Total + 50m + 20m == 213m,
    "mixed cash and installments are excluded from the cheque principal");
var noInterestPlan = InvoiceInstallmentCalculation.Create(5.01m, 0m, 3);
Check(noInterestPlan.CreateSaleChequeCharge(5.01m, 0m, 30) is null && noInterestPlan.RegularAmount == 1m &&
      noInterestPlan.LastAmount == 3.01m && noInterestPlan.RegularAmount * 2 + noInterestPlan.LastAmount == 5.01m,
    "zero income creates no charge and the final row retains fractional money");
Check(InvoiceInstallmentCalculation.Create(0.51m, 0m, 1).LastAmount == 0.51m,
    "a single cheque retains its exact fractional amount above the payment tolerance");
try { InvoiceInstallmentCalculation.Create(0.50m, 0m, 1); throw new Exception("unclassifiable cheque component accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: a cheque below the payment classifier threshold is rejected"); }
try { InvoiceInstallmentCalculation.Create(1m, 0m, 3); throw new Exception("zero-value cheque rows accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: insufficient amount cannot create zero cheque rows"); }
try { InvoiceInstallmentCalculation.Create(200m, 0.0000001m, 1); throw new Exception("rate precision accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: invalid rate precision rejected before row replacement"); }
try { calculatedPlan.CreateSaleChequeCharge(199m, 5m, 30); throw new Exception("basis exceeds goods accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: calculator basis cannot exceed goods with VAT"); }
try { calculatedPlan.CreateSaleChequeCharge(200m, 5m, 0); throw new Exception("zero adjustment period accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: invalid adjustment terms rejected"); }
try { calculatedPlan.CreateSaleChequeCharge(9999999999999999.99m, 5m, 30); throw new Exception("total invoice overflow accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS: full goods plus income must fit the ledger before rows are replaced"); }

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
// Store-only posting and all subsequent workflows run without an ERP rule, account, year or voucher.
await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    var party = new Party { Name = "مشتری آزمایش", Type = PartyType.Customer, IsActive = true };
    var wh = new Warehouse { Name = "انبار آزمایش", IsActive = true };
    var prod = new Product { Name = "کالا", Code = "TEST-POST", Unit = "عدد" };
    var treasuryBank = new TrsAccount { Code = "BANK-TEST", Name = "بانک مستقل", BankName = "آزمایش",
        Kind = TreasuryAccountKind.Bank, IsActive = true, OpeningBalance = 1000 };
    db.AddRange(party, wh, prod, treasuryBank);
    await db.SaveChangesAsync();
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
        var charge = await db.SaleChequeCharges.SingleAsync(x => x.TransactionId == newSale.Id);
        Check(newSale.Amount == 250 && charge.Amount == 50 && posted.IncomeDelta == 50 && posted.AccVoucherId is null,
            "sale and initial income are written atomically without an ERP voucher");
        await tx.RollbackAsync();
    }
    db.ChangeTracker.Clear();
    Check(!await db.Transactions.AnyAsync(x => x.Number == "SL-REG"), "sale and income roll back together");
    await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
    {
        var mixed = NewRegistrationSale("SL-MIXED");
        mixed.PaymentMethod = PaymentMethod.Mixed;
        mixed.Cheques[0].Amount = 200;
        mixed.CashAmount = 50; mixed.SettledAmount = 50; mixed.CashType = CashType.CardTransfer;
        mixed.TrsAccountId = treasuryBank.Id;
        db.Transactions.Add(mixed);
        await db.SaveChangesAsync();
        var posted = await SaleChequeChargeRegistration.RegisterAsync(db, mixed.Id, registrationInput, "tester");
        var charge = await db.SaleChequeCharges.SingleAsync(c => c.TransactionId == mixed.Id);
        var cashReceipt = await db.TrsVouchers.Include(v => v.Lines).SingleAsync(v => v.Id == charge.InitialCashTrsVoucherId);
        Check(cashReceipt.TotalAmount == 50 && cashReceipt.Status == TreasuryStatus.Confirmed &&
              cashReceipt.Lines.Single().TrsAccountId == treasuryBank.Id && posted.AccVoucherId is null,
            "mixed payment writes one protected store treasury receipt, with no ERP mapping");
        var treasury = new Inventory.Api.Services.Treasury.TreasuryService(db, new AccountingService(db));
        Check((await treasury.GetAccountAsync(treasuryBank.Id))!.Balance == 1050, "initial cash changes bank balance once");
        try { await treasury.UnconfirmVoucherAsync(cashReceipt.Id, "tester"); throw new Exception("linked treasury receipt unconfirmed"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS: linked treasury receipt cannot be changed independently"); }
        await tx.RollbackAsync();
    }
    db.ChangeTracker.Clear();
    await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
    {
        var failed = NewRegistrationSale("SL-FAILED");
        failed.Cheques[0].OriginalDueDate = null; // error occurs after charge save, inside posting
        db.Transactions.Add(failed);
        await db.SaveChangesAsync();
        try { await SaleChequeChargeRegistration.RegisterAsync(db, failed.Id, registrationInput, "tester"); throw new Exception("invalid posting accepted"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS: invalid initial cheque aborts the sale"); }
        Check(!db.ChangeTracker.Entries().Any(), "failed registration detaches rolled-back entities");
    }
    await db.SaveChangesAsync();
    Check(!await db.Transactions.AnyAsync(x => x.Number == "SL-FAILED") &&
          !await db.SaleChequeCharges.AnyAsync(c => c.Transaction.Number == "SL-FAILED"),
        "failed posting leaves no invoice, income or tracker residue");
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
    var creationService = new InventoryService(db, NullLogger<InventoryService>.Instance);
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

    var cashParty = new Party { Type = PartyType.Customer, Name = "مشتری نقدی", IsActive = true };
    db.Parties.Add(cashParty); await db.SaveChangesAsync();
    var cashSale = await creationService.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = wh.Id, PartyId = cashParty.Id, Date = new DateTime(2026, 6, 2),
        PaymentMethod = PaymentMethod.Mixed, CashType = CashType.CardTransfer, CashAmount = 50,
        CashTrackingNumber = "ref-50", TrsAccountId = treasuryBank.Id,
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = costProduct.Id, Quantity = 1, Price = 200 } },
        Cheques = new() { new Inventory.Shared.Dtos.ChequeDto { Number = "CASH-MIX", Amount = 200, DueDate = new DateTime(2026, 7, 2) } },
        ChequeCharge = new Inventory.Shared.Dtos.SaleChequeChargeInput { CalculatorBaseAmount = 200, CalculatorRatePercent = 5,
            CalculatorMonths = 5, AdjustmentRatePercent = 3, AdjustmentPeriodDays = 30 }
    }, "tester");
    await creationService.CancelSaleChequeOrderAsync(cashSale.Id, new Inventory.Shared.Dtos.SaleChequeCancelCommand
    {
        OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 3), Reason = "تست استرداد"
    }, "tester");
    var afterCashCancel = await creationService.GetPartyStatementDetailAsync(cashParty.Id, new DateTime(2026, 6, 4));
    var summaryAfterCashCancel = await creationService.GetPartyStatementSummaryReportAsync(from: new DateTime(2026, 6, 4));
    var cashBank = new Inventory.Api.Services.Treasury.TreasuryService(db, new AccountingService(db));
    var cashFlow = await cashBank.GetFlowAsync(treasuryBank.Id, null, null);
    Check(afterCashCancel.OpeningBalance == 0 && afterCashCancel.ClosingBalance == 0 &&
          summaryAfterCashCancel.Items.Single(x => x.PartyId == cashParty.Id).ClosingBalance == 0 &&
          (await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance == cashFlow.ClosingBalance && cashFlow.ClosingBalance == 1000,
        "cancellation preserves the initial cash and refund in opening statements and bank flow");

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
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = costProduct.Id, Quantity = 2, Price = 100, VatValue = 0 } },
        Cheques = new() { new Inventory.Shared.Dtos.ChequeDto { Number = "CHQ-E2E-1", Amount = 250, DueDate = new DateTime(2026, 7, 3) } },
        ChequeCharge = new Inventory.Shared.Dtos.SaleChequeChargeInput
        {
            CalculatorBaseAmount = 200, CalculatorRatePercent = 5, CalculatorMonths = 5,
            AdjustmentRatePercent = 3, AdjustmentPeriodDays = 30
        }
    }, "tester");
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

    var incomeReport = await new OperationsReportsService(db).GetChequeIncomeReportAsync(
        new DateTime(2026, 6, 4), new DateTime(2026, 6, 6), e2eParty.Id);
    Check(incomeReport.TotalIncome == 26 && incomeReport.Rows.Count == 3 &&
          incomeReport.Rows.All(r => r.EventId > 0 && r.VoucherNumber == 0),
        "dated operational income report reconciles without an ERP voucher");

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
    var receiptCommand = new Inventory.Shared.Dtos.SaleChequeReceiptCommand
    {
        OperationId = Guid.NewGuid(), TrsAccountId = treasuryBank.Id, Date = new DateTime(2026, 6, 7)
    };
    await creationService.ReceiveSaleChequeAsync(activeCheque.Id, receiptCommand, "tester");
    await creationService.ReceiveSaleChequeAsync(activeCheque.Id, receiptCommand, "tester");
    Check((await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance == 1126, "replayed cheque receipt is not counted twice");
    var reverseCommand = new Inventory.Shared.Dtos.SaleChequeReceiptReversalCommand
    {
        OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 7)
    };
    await creationService.ReverseSaleChequeReceiptAsync(activeCheque.Id, reverseCommand, "tester");
    await creationService.ReverseSaleChequeReceiptAsync(activeCheque.Id, reverseCommand, "tester");
    Check((await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance == 1000, "receipt reversal is idempotent");
    await creationService.ReceiveSaleChequeAsync(activeCheque.Id, new Inventory.Shared.Dtos.SaleChequeReceiptCommand
    {
        OperationId = Guid.NewGuid(), TrsAccountId = treasuryBank.Id, Date = new DateTime(2026, 6, 7)
    }, "tester");
    var chequeFlow = await cashBank.GetFlowAsync(treasuryBank.Id, null, null);
    Check(chequeFlow.ClosingBalance == 1126 && (await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance == chequeFlow.ClosingBalance,
        "recollection and detailed account flow agree");
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

    // Ordinary store cash and issued cheques use the same operational treasury,
    // without silently rebuilding older manual vouchers.
    var plainParty = new Party { Type = PartyType.Customer, Name = "مشتری فاکتور عادی", IsActive = true };
    var supplier = new Party { Type = PartyType.Supplier, Name = "تامین‌کننده عادی", IsActive = true };
    var serviceProduct = new Product { Name = "خدمت آزمون نقدی", Code = "SHOP-CASH-SERVICE", Unit = "عدد", IsService = true, IsActive = true };
    var purchaseProduct = new Product { Name = "کالای آزمون چک خرید", Code = "SHOP-PURCHASE", Unit = "عدد", IsActive = true };
    db.AddRange(plainParty, supplier, serviceProduct, purchaseProduct);
    await db.SaveChangesAsync();
    var beforeOrdinary = (await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance;
    var ordinaryCash = await creationService.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = wh.Id, PartyId = plainParty.Id, Date = new DateTime(2026, 6, 10),
        PaymentMethod = PaymentMethod.Cash, CashType = CashType.CardTransfer, CashTrackingNumber = "cash-ref",
        TrsAccountId = treasuryBank.Id,
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = serviceProduct.Id, Quantity = 1, Price = 100, VatValue = 0 } }
    }, "tester");
    Check(ordinaryCash.ShopCashLedgerEnabled && (await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance == beforeOrdinary + 100,
        "ordinary cash sale enters bank balance once");
    var ordinaryCashStmt = await creationService.GetPartyStatementDetailAsync(plainParty.Id);
    Check(ordinaryCashStmt.ClosingBalance == 0, "ordinary cash sale and its dated receipt balance the party statement");
    try { await creationService.DeleteOrderAsync(ordinaryCash.Id, "tester"); throw new Exception("financial history deleted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: direct delete preserves ordinary cash history"); }

    var ordinaryPurchase = await creationService.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Purchase, WarehouseId = wh.Id, PartyId = supplier.Id, Date = new DateTime(2026, 6, 11),
        PaymentMethod = PaymentMethod.Cheque,
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = purchaseProduct.Id, Quantity = 1, Price = 80, VatValue = 0 } },
        Cheques = new() { new Inventory.Shared.Dtos.ChequeDto { Number = "SHOP-PURCHASE-CHQ", Amount = 80,
            TrsAccountId = treasuryBank.Id, DueDate = new DateTime(2026, 7, 11) } }
    }, "tester");
    var paidPurchase = new Inventory.Shared.Dtos.SaleChequeReceiptCommand
    { OperationId = Guid.NewGuid(), TrsAccountId = treasuryBank.Id, Date = new DateTime(2026, 6, 12) };
    await creationService.ReceiveSaleChequeAsync(ordinaryPurchase.Cheques.Single().Id, paidPurchase, "tester");
    await creationService.ReceiveSaleChequeAsync(ordinaryPurchase.Cheques.Single().Id, paidPurchase, "tester");
    Check((await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance == beforeOrdinary + 20,
        "issued purchase cheque is an idempotent bank outflow");
    var purchaseBefore = await creationService.GetPartyStatementDetailAsync(supplier.Id, to: new DateTime(2026, 6, 11));
    var purchasePaid = await creationService.GetPartyStatementDetailAsync(supplier.Id, to: new DateTime(2026, 6, 12));
    Check(purchaseBefore.ClosingBalance == -80 && purchasePaid.ClosingBalance == 0,
        "purchase cheque appears on the payment date, not the invoice date");
    await creationService.ReverseSaleChequeReceiptAsync(ordinaryPurchase.Cheques.Single().Id,
        new Inventory.Shared.Dtos.SaleChequeReceiptReversalCommand { OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 13) }, "tester");
    var purchaseReversed = await creationService.GetPartyStatementDetailAsync(supplier.Id, to: new DateTime(2026, 6, 13));
    var ordinaryFlow = await cashBank.GetFlowAsync(treasuryBank.Id, null, null);
    Check(purchaseReversed.ClosingBalance == -80 && ordinaryFlow.ClosingBalance == beforeOrdinary + 100 &&
          (await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance == ordinaryFlow.ClosingBalance,
        "purchase reversal restores the bank and statement balance on its own date");

    // A mixed ordinary invoice can collect an installment and cheque in either
    // order; both have dated, reversible bank and party-statement movements.
    var ordinaryMixed = await creationService.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = wh.Id, PartyId = plainParty.Id, Date = new DateTime(2026, 6, 14),
        PaymentMethod = PaymentMethod.Mixed,
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = serviceProduct.Id, Quantity = 1, Price = 200, VatValue = 0 } },
        Cheques = new() { new Inventory.Shared.Dtos.ChequeDto { Number = "ORDINARY-MIX-CHQ", Amount = 80, DueDate = new DateTime(2026, 7, 14) } },
        Installments = new() { new Inventory.Shared.Dtos.InstallmentDto { No = 1, Amount = 120, DueDate = new DateTime(2026, 7, 15) } }
    }, "tester");
    var instOp = new Inventory.Shared.Dtos.SaleChequeInstallmentReceiptCommand
    { OperationId = Guid.NewGuid(), TrsAccountId = treasuryBank.Id, Date = new DateTime(2026, 6, 20) };
    await creationService.ReceiveSaleChequeInstallmentAsync(ordinaryMixed.Installments.Single().Id, instOp, "tester");
    await creationService.ReceiveSaleChequeInstallmentAsync(ordinaryMixed.Installments.Single().Id, instOp, "tester");
    var afterInst = await creationService.GetPartyStatementDetailAsync(plainParty.Id, to: new DateTime(2026, 6, 20));
    Check(afterInst.ClosingBalance == 80 && (await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance == beforeOrdinary + 220,
        "ordinary installment is idempotent and dated in bank and party statement");
    await creationService.ReceiveSaleChequeAsync(ordinaryMixed.Cheques.Single().Id,
        new Inventory.Shared.Dtos.SaleChequeReceiptCommand { OperationId = Guid.NewGuid(), TrsAccountId = treasuryBank.Id, Date = new DateTime(2026, 6, 21) }, "tester");
    Check((await creationService.GetPartyStatementDetailAsync(plainParty.Id, to: new DateTime(2026, 6, 21))).ClosingBalance == 0,
        "ordinary mixed invoice can collect cheque after installment");
    await creationService.ReverseSaleChequeInstallmentReceiptAsync(ordinaryMixed.Installments.Single().Id,
        new Inventory.Shared.Dtos.SaleChequeInstallmentReversalCommand { OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 22) }, "tester");
    Check((await creationService.GetPartyStatementDetailAsync(plainParty.Id, to: new DateTime(2026, 6, 22))).ClosingBalance == 120,
        "ordinary installment reversal restores receivable on the reversal date");

    var ordinaryPurchaseInst = await creationService.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Purchase, WarehouseId = wh.Id, PartyId = supplier.Id, Date = new DateTime(2026, 6, 14),
        PaymentMethod = PaymentMethod.Installment,
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = purchaseProduct.Id, Quantity = 1, Price = 40, VatValue = 0 } },
        Installments = new() { new Inventory.Shared.Dtos.InstallmentDto { No = 1, Amount = 40, DueDate = new DateTime(2026, 7, 14) } }
    }, "tester");
    var purchaseInstBankBefore = (await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance;
    var emptyBank = new TrsAccount { Code = "EMPTY-TEST", Name = "حساب بدون مانده", Kind = TreasuryAccountKind.Bank, IsActive = true };
    db.TrsAccounts.Add(emptyBank);
    await db.SaveChangesAsync();
    try
    {
        await creationService.ReceiveSaleChequeInstallmentAsync(ordinaryPurchaseInst.Installments.Single().Id,
            new Inventory.Shared.Dtos.SaleChequeInstallmentReceiptCommand { OperationId = Guid.NewGuid(), TrsAccountId = emptyBank.Id, Date = new DateTime(2026, 6, 20) }, "tester");
        throw new Exception("purchase installment paid without funds");
    }
    catch (InvalidOperationException) { Console.WriteLine("PASS: purchase installment with insufficient funds rolls back"); }
    Check(!(await creationService.GetOrderAsync(ordinaryPurchaseInst.Id))!.Installments.Single().IsPaid &&
          (await cashBank.GetAccountAsync(emptyBank.Id))!.Balance == 0,
        "failed purchase installment leaves status and bank unchanged");
    await creationService.ReceiveSaleChequeInstallmentAsync(ordinaryPurchaseInst.Installments.Single().Id,
        new Inventory.Shared.Dtos.SaleChequeInstallmentReceiptCommand { OperationId = Guid.NewGuid(), TrsAccountId = treasuryBank.Id, Date = new DateTime(2026, 6, 20) }, "tester");
    Check((await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance == purchaseInstBankBefore - 40,
        "ordinary purchase installment is a bank outflow");
    await creationService.ReverseSaleChequeInstallmentReceiptAsync(ordinaryPurchaseInst.Installments.Single().Id,
        new Inventory.Shared.Dtos.SaleChequeInstallmentReversalCommand { OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 21) }, "tester");
    Check((await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance == purchaseInstBankBefore &&
          (await cashBank.GetFlowAsync(treasuryBank.Id, null, null)).ClosingBalance == purchaseInstBankBefore,
        "ordinary purchase installment reversal restores bank and flow balance");

    // Ordinary nasiya is independent of ERP, partial, dated and reversible.
    var creditCustomer = new Party { Type = PartyType.Customer, Name = "مشتری نسیه", IsActive = true };
    var creditSupplier = new Party { Type = PartyType.Supplier, Name = "تامین‌کننده نسیه", IsActive = true };
    db.AddRange(creditCustomer, creditSupplier);
    await db.SaveChangesAsync();
    var mixedCreditSale = await creationService.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = wh.Id, PartyId = creditCustomer.Id, Date = new DateTime(2026, 6, 14),
        PaymentMethod = PaymentMethod.Mixed, DueDate = new DateTime(2026, 8, 1),
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = serviceProduct.Id, Quantity = 1, Price = 300, VatValue = 0 } },
        Cheques = new() { new Inventory.Shared.Dtos.ChequeDto { Number = "CREDIT-MIX-CHQ", Amount = 100, DueDate = new DateTime(2026, 7, 14) } },
        Installments = new() { new Inventory.Shared.Dtos.InstallmentDto { No = 1, Amount = 50, DueDate = new DateTime(2026, 7, 15) } }
    }, "tester");
    var creditState = await creationService.GetOrderCreditStateAsync(mixedCreditSale.Id);
    Check(creditState.Outstanding == 300 && creditState.ReservedForCheques == 100 &&
          creditState.ReservedForInstallments == 50 && creditState.AvailableCredit == 150,
        "mixed invoice credit excludes open cheques and installments");
    var creditBankBefore = (await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance;
    var creditCommand = new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
    { OperationId = Guid.NewGuid(), TrsAccountId = treasuryBank.Id, Amount = 70, Date = new DateTime(2026, 6, 20) };
    await creationService.ReceiveOrderCreditAsync(mixedCreditSale.Id, creditCommand, "tester");
    await creationService.ReceiveOrderCreditAsync(mixedCreditSale.Id, creditCommand, "tester");
    creditState = await creationService.GetOrderCreditStateAsync(mixedCreditSale.Id);
    Check(creditState.AvailableCredit == 80 && creditState.Receipts.Count == 1 &&
          (await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance == creditBankBefore + 70,
        "partial sale credit receipt is idempotent and changes the bank once");
    try
    {
        await creationService.ReceiveOrderCreditAsync(mixedCreditSale.Id, new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
        { OperationId = Guid.NewGuid(), TrsAccountId = treasuryBank.Id, Amount = 81, Date = new DateTime(2026, 6, 21) }, "tester");
        throw new Exception("credit receipt consumed reserved obligations");
    }
    catch (InvalidOperationException) { Console.WriteLine("PASS: credit receipt cannot consume open cheque or installment"); }
    try
    {
        await creationService.ReceiveOrderCreditAsync(mixedCreditSale.Id, new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
        { OperationId = creditCommand.OperationId, TrsAccountId = treasuryBank.Id, Amount = 71, Date = creditCommand.Date }, "tester");
        throw new Exception("credit operation reused with different amount");
    }
    catch (InvalidOperationException) { Console.WriteLine("PASS: altered credit replay rejected"); }
    var creditBefore = await creationService.GetPartyStatementDetailAsync(creditCustomer.Id, to: new DateTime(2026, 6, 19));
    var creditAfter = await creationService.GetPartyStatementDetailAsync(creditCustomer.Id, to: new DateTime(2026, 6, 20));
    var creditSummary = await creationService.GetPartyStatementSummaryReportAsync(to: new DateTime(2026, 6, 20));
    Check(creditBefore.ClosingBalance == 300 && creditAfter.ClosingBalance == 230 &&
          creditSummary.Items.Single(x => x.PartyId == creditCustomer.Id).ClosingBalance == creditAfter.ClosingBalance,
        "credit receipt is dated correctly and party summary equals detail");
    var creditReceiptId = creditState.Receipts.Single().Id;
    var reversalCommand = new Inventory.Shared.Dtos.ShopOrderCreditReversalCommand
    { OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 25) };
    await creationService.ReverseOrderCreditAsync(mixedCreditSale.Id, creditReceiptId, reversalCommand, "tester");
    await creationService.ReverseOrderCreditAsync(mixedCreditSale.Id, creditReceiptId, reversalCommand, "tester");
    var creditReopened = await creationService.GetOrderCreditStateAsync(mixedCreditSale.Id);
    var creditOpeningAfter = await creationService.GetPartyStatementDetailAsync(creditCustomer.Id, from: new DateTime(2026, 6, 24));
    Check(creditReopened.AvailableCredit == 150 && creditReopened.Receipts.Single().ReversalDate == reversalCommand.Date &&
          creditOpeningAfter.OpeningBalance == 230 && creditOpeningAfter.ClosingBalance == 300 &&
          (await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance == creditBankBefore,
        "reversal preserves history, bank and opening/closing statement dates");
    try { await creationService.DeleteOrderAsync(mixedCreditSale.Id, "tester"); throw new Exception("credit history deleted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: even reversed credit history prevents direct invoice deletion"); }

    var creditPurchase = await creationService.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Purchase, WarehouseId = wh.Id, PartyId = creditSupplier.Id, Date = new DateTime(2026, 6, 14),
        PaymentMethod = PaymentMethod.Credit, DueDate = new DateTime(2026, 7, 14),
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = purchaseProduct.Id, Quantity = 1, Price = 80, VatValue = 0 } }
    }, "tester");
    try
    {
        await creationService.ReceiveOrderCreditAsync(creditPurchase.Id, new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
        { OperationId = Guid.NewGuid(), TrsAccountId = emptyBank.Id, Amount = 30, Date = new DateTime(2026, 6, 20) }, "tester");
        throw new Exception("purchase credit paid without funds");
    }
    catch (InvalidOperationException) { Console.WriteLine("PASS: purchase credit with insufficient funds rolls back"); }
    Check((await creationService.GetOrderCreditStateAsync(creditPurchase.Id)).AvailableCredit == 80,
        "failed purchase payment does not change payable");
    var purchaseCreditOp = new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
    { OperationId = Guid.NewGuid(), TrsAccountId = treasuryBank.Id, Amount = 30, Date = new DateTime(2026, 6, 20) };
    await creationService.ReceiveOrderCreditAsync(creditPurchase.Id, purchaseCreditOp, "tester");
    var paidCreditPurchase = await creationService.GetOrderCreditStateAsync(creditPurchase.Id);
    Check(paidCreditPurchase.AvailableCredit == 50 && (await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance == creditBankBefore - 30 &&
          (await creationService.GetPartyStatementDetailAsync(creditSupplier.Id, to: purchaseCreditOp.Date)).ClosingBalance == -50,
        "purchase credit payment is an outflow and reduces supplier payable on its date");
    await creationService.ReverseOrderCreditAsync(creditPurchase.Id, paidCreditPurchase.Receipts.Single().Id,
        new Inventory.Shared.Dtos.ShopOrderCreditReversalCommand { OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 21) }, "tester");
    Check((await cashBank.GetAccountAsync(treasuryBank.Id))!.Balance == creditBankBefore &&
          (await cashBank.GetFlowAsync(treasuryBank.Id, null, null)).ClosingBalance == creditBankBefore &&
          (await creationService.GetPartyStatementDetailAsync(creditSupplier.Id, to: new DateTime(2026, 6, 21))).ClosingBalance == -80,
        "purchase credit reversal restores account, flow and payable");

    // Sale reversal is a real outflow: money already spent cannot be silently removed from the account.
    await creationService.ReceiveOrderCreditAsync(mixedCreditSale.Id, new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
    { OperationId = Guid.NewGuid(), TrsAccountId = emptyBank.Id, Amount = 50, Date = new DateTime(2026, 6, 26) }, "tester");
    var saleCreditOnEmptyBank = (await creationService.GetOrderCreditStateAsync(mixedCreditSale.Id)).Receipts.Single(r => !r.ReversalDate.HasValue);
    await creationService.ReceiveOrderCreditAsync(creditPurchase.Id, new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
    { OperationId = Guid.NewGuid(), TrsAccountId = emptyBank.Id, Amount = 40, Date = new DateTime(2026, 6, 27) }, "tester");
    var purchaseCreditOnEmptyBank = (await creationService.GetOrderCreditStateAsync(creditPurchase.Id)).Receipts.Single(r => !r.ReversalDate.HasValue);
    try
    {
        await creationService.ReverseOrderCreditAsync(mixedCreditSale.Id, saleCreditOnEmptyBank.Id,
            new Inventory.Shared.Dtos.ShopOrderCreditReversalCommand { OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 28) }, "tester");
        throw new Exception("sale credit reversed with insufficient bank funds");
    }
    catch (InvalidOperationException) { Console.WriteLine("PASS: sale credit reversal with insufficient funds rolls back"); }
    Check((await cashBank.GetAccountAsync(emptyBank.Id))!.Balance == 10 &&
          (await creationService.GetOrderCreditStateAsync(mixedCreditSale.Id)).AvailableCredit == 100,
        "failed sale reversal leaves the active receipt and bank unchanged");
    await creationService.ReverseOrderCreditAsync(creditPurchase.Id, purchaseCreditOnEmptyBank.Id,
        new Inventory.Shared.Dtos.ShopOrderCreditReversalCommand { OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 29) }, "tester");
    await creationService.ReverseOrderCreditAsync(mixedCreditSale.Id, saleCreditOnEmptyBank.Id,
        new Inventory.Shared.Dtos.ShopOrderCreditReversalCommand { OperationId = Guid.NewGuid(), Date = new DateTime(2026, 6, 30) }, "tester");
    Check((await cashBank.GetAccountAsync(emptyBank.Id))!.Balance == 0,
        "reversing both real movements restores the original account balance");

    // Current balance alone cannot justify a debit before funding or before a later daily low point.
    var datedBank = new TrsAccount { Code = "DATED-TEST", Name = "حساب آزمون تاریخ", Kind = TreasuryAccountKind.Bank,
        OpeningBalance = 0, IsActive = true };
    var datedSales = new Inventory.Api.Data.Transaction
    { Number = "DATED-SALES", Type = TransactionType.Sale, WarehouseId = wh.Id, PartyId = creditCustomer.Id,
      Date = new DateTime(2026, 6, 25), Amount = 200, SettledAmount = 0, PaymentMethod = PaymentMethod.Credit };
    var datedPurchase = new Inventory.Api.Data.Transaction
    { Number = "DATED-PURCHASE", Type = TransactionType.Purchase, WarehouseId = wh.Id, PartyId = creditSupplier.Id,
      Date = new DateTime(2026, 6, 25), Amount = 80, SettledAmount = 0, PaymentMethod = PaymentMethod.Credit };
    var backdatedPurchase = new Inventory.Api.Data.Transaction
    { Number = "BACKDATED-PURCHASE", Type = TransactionType.Purchase, WarehouseId = wh.Id, PartyId = creditSupplier.Id,
      Date = new DateTime(2026, 6, 25), Amount = 40, SettledAmount = 0, PaymentMethod = PaymentMethod.Credit };
    db.AddRange(datedBank, datedSales, datedPurchase, backdatedPurchase); await db.SaveChangesAsync();
    foreach (var day in new[] { 10, 30 })
        await creationService.ReceiveOrderCreditAsync(datedSales.Id, new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
        { OperationId = Guid.NewGuid(), TrsAccountId = datedBank.Id, Amount = 100, Date = new DateTime(2026, 7, day) }, "tester");
    try
    {
        await creationService.ReceiveOrderCreditAsync(backdatedPurchase.Id, new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
        { OperationId = Guid.NewGuid(), TrsAccountId = datedBank.Id, Amount = 40, Date = new DateTime(2026, 7, 5) }, "tester");
        throw new Exception("backdated purchase spent a future receipt");
    }
    catch (InvalidOperationException) { Console.WriteLine("PASS: purchase cannot spend money before it arrived"); }
    await creationService.ReceiveOrderCreditAsync(datedPurchase.Id, new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
    { OperationId = Guid.NewGuid(), TrsAccountId = datedBank.Id, Amount = 80, Date = new DateTime(2026, 7, 20) }, "tester");
    try
    {
        await creationService.ReceiveOrderCreditAsync(backdatedPurchase.Id, new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
        { OperationId = Guid.NewGuid(), TrsAccountId = datedBank.Id, Amount = 30, Date = new DateTime(2026, 7, 15) }, "tester");
        throw new Exception("backdated purchase made a later daily balance negative");
    }
    catch (InvalidOperationException) { Console.WriteLine("PASS: backdated purchase protects later daily low point"); }
    await creationService.ReceiveOrderCreditAsync(backdatedPurchase.Id, new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
    { OperationId = Guid.NewGuid(), TrsAccountId = datedBank.Id, Amount = 20, Date = new DateTime(2026, 7, 15) }, "tester");
    Check((await Inventory.Api.Services.Treasury.TreasuryBalanceCalculator.GetAmountAsync(db, datedBank.Id)) == 100,
        "backdated purchase that leaves every daily balance nonnegative is allowed");

    var chargedCreditCustomer = new Party { Type = PartyType.Customer, Name = "مشتری نسیه چک سوددار", IsActive = true };
    db.Parties.Add(chargedCreditCustomer); await db.SaveChangesAsync();
    var chargedCreditSale = await creationService.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = wh.Id, PartyId = chargedCreditCustomer.Id, Date = new DateTime(2026, 6, 14),
        PaymentMethod = PaymentMethod.Mixed, DueDate = new DateTime(2026, 8, 1),
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = serviceProduct.Id, Quantity = 1, Price = 200, VatValue = 0 } },
        Cheques = new() { new Inventory.Shared.Dtos.ChequeDto { Number = "CHARGED-CREDIT-CHQ", Amount = 100, DueDate = new DateTime(2026, 7, 14) } },
        Installments = new() { new Inventory.Shared.Dtos.InstallmentDto { No = 1, Amount = 50, DueDate = new DateTime(2026, 7, 15) } },
        ChequeCharge = new Inventory.Shared.Dtos.SaleChequeChargeInput { CalculatorBaseAmount = 200, CalculatorRatePercent = 5,
            CalculatorMonths = 5, AdjustmentRatePercent = 3, AdjustmentPeriodDays = 30 }
    }, "tester");
    Check((await creationService.GetOrderCreditStateAsync(chargedCreditSale.Id)).AvailableCredit == 100,
        "charged sale has only its unreserved credit remainder available");
    await creationService.ReceiveOrderCreditAsync(chargedCreditSale.Id, new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
    { OperationId = Guid.NewGuid(), TrsAccountId = treasuryBank.Id, Amount = 60, Date = new DateTime(2026, 6, 20) }, "tester");
    var chargedStatement = await creationService.GetPartyStatementDetailAsync(chargedCreditCustomer.Id, to: new DateTime(2026, 6, 20));
    var chargedSummary = await creationService.GetPartyStatementSummaryReportAsync(to: new DateTime(2026, 6, 20));
    var chargedParty = (await creationService.GetPartiesAsync(PartyType.Customer)).Single(p => p.Id == chargedCreditCustomer.Id);
    Check(chargedStatement.ClosingBalance == 190 && chargedSummary.Items.Single(x => x.PartyId == chargedCreditCustomer.Id).ClosingBalance == 190 &&
          chargedParty.Balance == 190, "charged sale credit appears once in summary, detail and party balance");

    // A status-only historical payment has no reliable bank/date; do not synthesize it.
    var oldCredit = new Inventory.Api.Data.Transaction
    { Number = "OLD-CREDIT", Type = TransactionType.Sale, WarehouseId = wh.Id, PartyId = creditCustomer.Id,
      Date = new DateTime(2026, 6, 14), Amount = 100, SettledAmount = 25, PaymentMethod = PaymentMethod.Credit };
    db.Transactions.Add(oldCredit); await db.SaveChangesAsync();
    try { await creationService.GetOrderCreditStateAsync(oldCredit.Id); throw new Exception("untracked historical credit accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: status-only historical payment is not guessed"); }
    Check(!await db.ShopOrderCreditReceipts.AnyAsync(r => r.TransactionId == oldCredit.Id),
        "review of old credit creates no invented receipt");
    var oldPaidFlag = new Inventory.Api.Data.Transaction
    { Number = "OLD-PAID-FLAG", Type = TransactionType.Sale, WarehouseId = wh.Id, PartyId = creditCustomer.Id,
      Date = new DateTime(2026, 6, 14), Amount = 100, SettledAmount = 0, PaymentMethod = PaymentMethod.Mixed,
      Installments = new() { new InstallmentLine { No = 1, Amount = 30, DueDate = new DateTime(2026, 7, 1), IsPaid = true } } };
    db.Transactions.Add(oldPaidFlag); await db.SaveChangesAsync();
    try { await creationService.GetOrderCreditStateAsync(oldPaidFlag.Id); throw new Exception("untracked paid flag was treated as free credit"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: status-only paid installment cannot be collected again as credit"); }
    await SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync(db, oldPaidFlag, allowUnpostedLegacy: true);
    Check(!await db.SaleChequeInstallmentReceipts.AnyAsync(r => r.TransactionId == oldPaidFlag.Id),
        "legacy report verification does not invent a dated installment receipt");

    Check(await db.AccVouchers.CountAsync() == 0 && await db.AccAccounts.CountAsync() == 0 &&
          await db.AccFiscalYears.CountAsync() == 0 && await db.FacRules.CountAsync() == 0,
        "complete store workflows do not create or require ERP records");
}
// End-to-end example generated by the same shared calculator as the UI.  A separate
// service product and party keep stock and other accounting probes unaffected.
await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    var customer = new Party { Name = "مشتری ماشین‌حساب", Type = PartyType.Customer, IsActive = true };
    var warehouse = new Warehouse { Name = "انبار ماشین‌حساب", IsActive = true };
    var product = new Product { Name = "خدمت آزمایشی", Code = "CALC-SERVICE", IsService = true };
    var bank = new TrsAccount { Code = "CALC-BANK", Name = "بانک وصول", BankName = "آزمایش",
        Kind = TreasuryAccountKind.Bank, IsActive = true, OpeningBalance = 0m };
    db.AddRange(customer, warehouse, product, bank);
    await db.SaveChangesAsync();
    var service = new InventoryService(db, NullLogger<InventoryService>.Instance);
    var firstDue = new DateTime(2026, 7, 1);
    var chequeRows = Enumerable.Range(0, calculatedPlan.Months).Select(i => new Inventory.Shared.Dtos.ChequeDto
    {
        Number = $"CALC-{i + 1}", DueDate = firstDue.AddDays(i * 30),
        Amount = i == calculatedPlan.Months - 1 ? calculatedPlan.LastAmount : calculatedPlan.RegularAmount
    }).ToList();
    var sale = await service.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = warehouse.Id, PartyId = customer.Id,
        Date = new DateTime(2026, 6, 2), PaymentMethod = PaymentMethod.Cheque,
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = product.Id, Quantity = 1m, Price = 200m, VatValue = 0m } },
        Cheques = chequeRows, ChequeCharge = calculatedTerms
    }, "tester");
    Check(sale.GoodsAmount == 200m && sale.ChequeIncomeAmount == 50m && sale.TotalAmount == 250m &&
          sale.Cheques.Sum(c => c.Amount) == 250m && sale.HasChequeCharge,
        "calculator output persists as 200 goods, 50 income and 250 in cheques");
    var before = await service.GetPartyStatementDetailAsync(customer.Id, to: new DateTime(2026, 6, 2));
    var incomeRow = before.Rows.Single(r => r.Title == "تاخیر تادیه چک");
    Check(before.ClosingBalance == 250m && before.SaleGoodsTotal == 200m && before.SaleChequeIncomeTotal == 50m &&
          before.Rows.Single(r => r.Title == "فاکتور فروش").Debit == 200m && incomeRow.Debit == 50m &&
          incomeRow.Description.Contains(sale.Number) && incomeRow.Description.Contains("۲۰۰") &&
          incomeRow.Description.Contains("۵٪") && incomeRow.Description.Contains("۵ ماه") &&
          !before.Rows.Any(r => r.Title == "وصول چک"),
        "handed-over cheques remain due; statement separately explains invoice and saved calculator terms");
    var salesReport = await new OperationsReportsService(db).GetOrderReportAsync(TransactionType.Sale,
        new DateTime(2026, 6, 2), new DateTime(2026, 6, 2), customer.Id, null);
    var incomeReport = await new OperationsReportsService(db).GetChequeIncomeReportAsync(
        new DateTime(2026, 6, 2), new DateTime(2026, 6, 2), customer.Id);
    Check(salesReport.TotalAmount == 250m && salesReport.GoodsAmount == 200m && salesReport.ChequeIncomeAmount == 50m &&
          incomeReport.TotalIncome == 50m && incomeReport.Rows.Count == 1 && incomeReport.Rows[0].VoucherNumber == 0,
        "sales report and store income event reconcile without an ERP voucher");
    for (var i = 0; i < sale.Cheques.Count; i++)
    {
        var command = new Inventory.Shared.Dtos.SaleChequeReceiptCommand
        { OperationId = Guid.NewGuid(), TrsAccountId = bank.Id, Date = firstDue.AddDays(i * 30) };
        await service.ReceiveSaleChequeAsync(sale.Cheques[i].Id, command, "tester");
        if (i == 0) await service.ReceiveSaleChequeAsync(sale.Cheques[i].Id, command, "tester");
    }
    var after = await service.GetPartyStatementDetailAsync(customer.Id);
    Check(after.ClosingBalance == 0m && after.Rows.Where(r => r.Title == "وصول چک").Sum(r => r.Credit) == 250m &&
          await db.SaleChequeReceipts.CountAsync(r => r.TransactionId == sale.Id) == calculatedPlan.Months &&
          await db.SaleChequeChargeEvents.CountAsync(e => e.SaleChequeCharge.TransactionId == sale.Id) == 1,
        "collecting the generated 250 once clears the customer balance without duplicating income");
    Check(!await db.AccAccounts.AnyAsync() && !await db.AccFiscalYears.AnyAsync() &&
          !await db.AccVouchers.AnyAsync() && !await db.FacRules.AnyAsync(),
        "the example works without ERP accounts, fiscal years, vouchers or rules");

    // An older saved charge may lack the original calculator terms. Keep its amount
    // and invoice reference visible, but never invent those terms in a statement.
    var legacyTerms = await db.SaleChequeCharges.SingleAsync(c => c.TransactionId == sale.Id);
    legacyTerms.CalculatorBaseAmount = null;
    legacyTerms.CalculatorRatePercent = null;
    legacyTerms.CalculatorMonths = null;
    await db.SaveChangesAsync();
    var legacyStatement = await service.GetPartyStatementDetailAsync(customer.Id);
    var legacyIncome = legacyStatement.Rows.Single(r => r.Title == "تاخیر تادیه چک");
    Check(legacyIncome.Debit == 50m && legacyIncome.Description.Contains(sale.Number) &&
          !legacyIncome.Description.Contains("مبنا") && legacyStatement.ClosingBalance == 0m,
        "old missing calculator terms are not fabricated");

    var zeroCustomer = new Party { Name = "مشتری بدون سود", Type = PartyType.Customer, IsActive = true };
    var mixedCustomer = new Party { Name = "مشتری ترکیبی ماشین‌حساب", Type = PartyType.Customer, IsActive = true };
    db.AddRange(zeroCustomer, mixedCustomer);
    await db.SaveChangesAsync();
    var zeroPlan = InvoiceInstallmentCalculation.Create(200m, 0m, 2);
    var zeroSale = await service.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = warehouse.Id, PartyId = zeroCustomer.Id,
        Date = new DateTime(2026, 6, 2), PaymentMethod = PaymentMethod.Cheque,
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = product.Id, Quantity = 1m, Price = 200m } },
        Cheques = new() {
            new Inventory.Shared.Dtos.ChequeDto { Number = "ZERO-1", Amount = zeroPlan.RegularAmount, DueDate = firstDue },
            new Inventory.Shared.Dtos.ChequeDto { Number = "ZERO-2", Amount = zeroPlan.LastAmount, DueDate = firstDue.AddDays(30) }
        },
        ChequeCharge = zeroPlan.CreateSaleChequeCharge(200m, 0m, 30)
    }, "tester");
    Check(zeroSale.TotalAmount == 200m && !zeroSale.HasChequeCharge &&
          !await db.SaleChequeCharges.AnyAsync(c => c.TransactionId == zeroSale.Id) &&
          (await service.GetPartyStatementDetailAsync(zeroCustomer.Id)).ClosingBalance == 200m,
        "zero-rate cheque sale creates no income event and remains receivable");

    var mixedSale = await service.CreateOrderAsync(new Inventory.Shared.Dtos.OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = warehouse.Id, PartyId = mixedCustomer.Id,
        Date = new DateTime(2026, 6, 2), PaymentMethod = PaymentMethod.Mixed, CashType = CashType.CardReader,
        CashAmount = 50m, TrsAccountId = bank.Id,
        Lines = new() { new Inventory.Shared.Dtos.OrderLineInput { ProductId = product.Id, Quantity = 1m, Price = 200m } },
        Installments = new() { new Inventory.Shared.Dtos.InstallmentDto { No = 1, Amount = 20m, DueDate = firstDue } },
        Cheques = new() {
            new Inventory.Shared.Dtos.ChequeDto { Number = "MIX-1", Amount = mixedPlan.RegularAmount, DueDate = firstDue },
            new Inventory.Shared.Dtos.ChequeDto { Number = "MIX-2", Amount = mixedPlan.LastAmount, DueDate = firstDue.AddDays(30) }
        },
        ChequeCharge = mixedPlan.CreateSaleChequeCharge(200m, 5m, 30)
    }, "tester");
    var mixedStatement = await service.GetPartyStatementDetailAsync(mixedCustomer.Id);
    Check(mixedSale.TotalAmount == 213m && mixedSale.ChequeIncomeAmount == 13m &&
          mixedSale.Cheques.Sum(c => c.Amount) == 143m && mixedStatement.ClosingBalance == 163m &&
          mixedStatement.SaleGoodsTotal == 200m && mixedStatement.SaleChequeIncomeTotal == 13m,
        "mixed cash and installment payment uses only 130 as the cheque principal");
}

// Explicit account/company scope, including legacy public accounts and no-company access.
await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
    await using (var seed = new AppDbContext(options))
    {
        await seed.Database.EnsureCreatedAsync();
        seed.SystemCompanies.AddRange(new SystemCompany { Id = 1, Name = "الف" }, new SystemCompany { Id = 2, Name = "ب" });
        seed.TrsAccounts.AddRange(new TrsAccount { Id = 1, CompanyId = 1, Code = "A", Name = "الف", IsActive = true },
            new TrsAccount { Id = 2, CompanyId = 2, Code = "B", Name = "ب", IsActive = true },
            new TrsAccount { Id = 3, Code = "C", Name = "قدیمی عمومی", IsActive = true });
        seed.Parties.AddRange(new Party { Id = 1, CompanyId = 1, Name = "مشتری الف", Type = PartyType.Customer },
            new Party { Id = 2, CompanyId = 2, Name = "مشتری ب", Type = PartyType.Customer });
        seed.Warehouses.AddRange(new Warehouse { Id = 1, CompanyId = 1, Name = "انبار الف" },
            new Warehouse { Id = 2, CompanyId = 2, Name = "انبار ب" });
        seed.Transactions.AddRange(
            new Inventory.Api.Data.Transaction { Id = 1, CompanyId = 1, Number = "COMPANY-A", Type = TransactionType.Sale,
                PartyId = 1, WarehouseId = 1, Date = new DateTime(2026, 6, 1), Amount = 100, PaymentMethod = PaymentMethod.Credit },
            new Inventory.Api.Data.Transaction { Id = 2, CompanyId = 2, Number = "COMPANY-B", Type = TransactionType.Sale,
                PartyId = 2, WarehouseId = 2, Date = new DateTime(2026, 6, 1), Amount = 100, PaymentMethod = PaymentMethod.Credit });
        var wrongBankOrder = new Inventory.Api.Data.Transaction
        { Id = 3, CompanyId = 1, Number = "COMPANY-A-WRONG-BANK", Type = TransactionType.Sale,
          PartyId = 1, WarehouseId = 1, Date = new DateTime(2026, 6, 1), Amount = 100, SettledAmount = 10,
          PaymentMethod = PaymentMethod.Mixed, Installments = new()
          { new InstallmentLine { CompanyId = 1, No = 1, Amount = 10, DueDate = new DateTime(2026, 7, 1), IsPaid = true } } };
        seed.Transactions.Add(wrongBankOrder);
        await seed.SaveChangesAsync();
        seed.SaleChequeInstallmentReceipts.Add(new SaleChequeInstallmentReceipt
        { CompanyId = 1, OperationId = Guid.NewGuid(), InstallmentId = wrongBankOrder.Installments.Single().Id,
          TransactionId = 3, TrsAccountId = 2, Amount = 10, Date = new DateTime(2026, 6, 2) });
        await seed.SaveChangesAsync();
    }
    var http = new DefaultHttpContext();
    http.Items["ActiveCompanyId"] = 1;
    var accessor = new HttpContextAccessor { HttpContext = http };
    await using var scoped = new AppDbContext(options, accessor);
    var usable = await SaleChequeTreasuryGuard.ListCompanyAccountsAsync(scoped);
    var treasury = new Inventory.Api.Services.Treasury.TreasuryService(scoped, new AccountingService(scoped));
    var visible = await treasury.GetAccountsAsync();
    Check(usable.Select(a => a.Id).SequenceEqual(new[] { 1 }) && visible.All(a => a.Id != 2 && a.Id != 3),
        "multi-company payment choices are strict and ownerless accounts stay hidden");
    try { await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(scoped, new TrsAccount { CompanyId = 2 }, 1); throw new Exception("foreign company account accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: foreign account rejected"); }
    Check(await treasury.GetAccountAsync(2) is null, "other company bank cannot be read by id");
    var scopedInventory = new InventoryService(scoped, NullLogger<InventoryService>.Instance);
    try
    {
        await scopedInventory.ReceiveOrderCreditAsync(1, new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
        { OperationId = Guid.NewGuid(), TrsAccountId = 2, Amount = 20, Date = new DateTime(2026, 6, 2) }, "tester");
        throw new Exception("foreign company credit account accepted");
    }
    catch (InvalidOperationException) { Console.WriteLine("PASS: foreign company cannot receive credit on its account"); }
    try { await scopedInventory.GetOrderCreditStateAsync(2); throw new Exception("foreign credit invoice exposed"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: foreign company credit invoice is not visible"); }
    try { await scopedInventory.GetOrderCreditStateAsync(3); throw new Exception("foreign bank on paid installment accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: a paid installment with another company's bank is not trusted"); }
    var scopedCreditOp = new Inventory.Shared.Dtos.ShopOrderCreditReceiptCommand
    { OperationId = Guid.NewGuid(), TrsAccountId = 1, Amount = 20, Date = new DateTime(2026, 6, 2) };
    await scopedInventory.ReceiveOrderCreditAsync(1, scopedCreditOp, "tester");
    Check((await scopedInventory.GetOrderCreditStateAsync(1)).AvailableCredit == 80,
        "company-scoped credit receipt updates only its own invoice");
    http.Items["ActiveCompanyId"] = 0;
    Check((await SaleChequeTreasuryGuard.ListCompanyAccountsAsync(scoped)).Count == 0 && (await treasury.GetAccountsAsync()).Count == 0,
        "user without a company cannot see bank accounts");
    scoped.Parties.Add(new Party { Name = "بدون شرکت", Type = PartyType.Customer });
    try { await scoped.SaveChangesAsync(); throw new Exception("write without company accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: user without company cannot create shop records"); }
}

// Exercise the actual provider-specific startup upgrade against a historical SQLite schema.
await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.ExecuteSqlRawAsync(@"
        PRAGMA foreign_keys=ON;
        CREATE TABLE AccVouchers(Id INTEGER PRIMARY KEY);
        CREATE TABLE SaleChequeCharges(Id INTEGER PRIMARY KEY);
        CREATE TABLE TrsVouchers(Id INTEGER PRIMARY KEY, VoucherId INTEGER, Kind INTEGER);
        INSERT INTO AccVouchers VALUES (10), (11);
        INSERT INTO SaleChequeCharges VALUES (1), (2);
        INSERT INTO TrsVouchers VALUES (40,10,0), (41,11,0), (42,11,0);
        CREATE TABLE SaleChequeChargeEvents(
            Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, OperationId TEXT NOT NULL UNIQUE,
            SaleChequeChargeId INTEGER NOT NULL REFERENCES SaleChequeCharges(Id) ON DELETE RESTRICT,
            AccVoucherId INTEGER NOT NULL REFERENCES AccVouchers(Id) ON DELETE RESTRICT,
            Kind INTEGER NOT NULL, Date TEXT NOT NULL, IncomeDelta TEXT NOT NULL);
        CREATE UNIQUE INDEX IX_SaleChequeChargeEvents_AccVoucherId ON SaleChequeChargeEvents(AccVoucherId);
        CREATE TABLE Audit(EventId INTEGER);
        CREATE TRIGGER audit_event AFTER INSERT ON SaleChequeChargeEvents BEGIN INSERT INTO Audit VALUES(new.Id); END;
        INSERT INTO SaleChequeChargeEvents VALUES (5,'old',1,10,1,'2026-06-01','50');
        INSERT INTO SaleChequeChargeEvents VALUES (100,'deleted',2,11,1,'2026-06-01','7');
        DELETE FROM SaleChequeChargeEvents WHERE Id=100;
        INSERT INTO SaleChequeChargeEvents VALUES (6,'ambiguous',2,11,1,'2026-06-01','7');");
    await SaleChequeShopLedgerSchemaV1.EnsureAsync(db);
    await SaleChequeShopLedgerSchemaV1.EnsureAsync(db);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT InitialCashTrsVoucherId FROM SaleChequeCharges WHERE Id=1";
    Check(Convert.ToInt32(await command.ExecuteScalarAsync()) == 40, "unambiguous historical cash link is retained");
    command.CommandText = "SELECT InitialCashTrsVoucherId FROM SaleChequeCharges WHERE Id=2";
    Check(await command.ExecuteScalarAsync() is DBNull, "ambiguous historical cash link is not guessed");
    await db.Database.ExecuteSqlRawAsync("INSERT INTO SaleChequeChargeEvents(OperationId,SaleChequeChargeId,AccVoucherId,Kind,Date,IncomeDelta) VALUES ('new',1,NULL,2,'2026-06-02','1')");
    command.CommandText = "SELECT MAX(Id) FROM SaleChequeChargeEvents";
    Check(Convert.ToInt32(await command.ExecuteScalarAsync()) > 100, "upgrade preserves AUTOINCREMENT high-water mark");
    command.CommandText = "SELECT COUNT(*) FROM Audit WHERE EventId > 100";
    Check(Convert.ToInt32(await command.ExecuteScalarAsync()) == 1, "upgrade preserves triggers");
    command.CommandText = "SELECT AccVoucherId FROM SaleChequeChargeEvents WHERE Id=5";
    Check(Convert.ToInt32(await command.ExecuteScalarAsync()) == 10, "historical voucher id is retained");
    try { await db.Database.ExecuteSqlRawAsync("DELETE FROM AccVouchers WHERE Id=10"); throw new Exception("historic FK lost"); }
    catch (SqliteException) { Console.WriteLine("PASS: historical voucher FK remains restrictive"); }
    command.CommandText = "PRAGMA foreign_keys";
    Check(Convert.ToInt32(await command.ExecuteScalarAsync()) == 1, "SQLite foreign keys restored after upgrade");
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
        ("20261105050000_AddSaleChequeCancellations", "SaleChequeCancellations"),
        ("20261105060000_DecoupleShopChequeLedger", "InitialCashTrsVoucherId"),
        ("20261105090000_AddShopCreditReceipts", "ShopOrderCreditReceipts")
    })
    {
        Check(db.Database.GetMigrations().Contains(mig), $"migration {mig} discoverable");
        var migScript = db.GetService<IMigrator>().GenerateScript("20261104230000_AddSaleChequeReceiptReversals", mig);
        Check(migScript.Contains(tableName), $"SQL Server script contains {tableName}");
    }
}
