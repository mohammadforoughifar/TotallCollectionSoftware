using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Finance;
using Inventory.Api.Services.Repairs;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Db = Inventory.Api.Data;

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}
static async Task Rejected(Func<Task> action, string message)
{
    try { await action(); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: " + message); return; }
    throw new Exception("FAIL (accepted): " + message);
}

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
await using (var setup = new AppDbContext(options))
{
    await setup.Database.EnsureCreatedAsync();
    setup.SystemCompanies.AddRange(new SystemCompany { Id = 1, Name = "شرکت اول" }, new SystemCompany { Id = 2, Name = "شرکت دوم" });
    await setup.SaveChangesAsync();
}
var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
http.HttpContext.Items["ActiveCompanyId"] = 1;
await using var db = new AppDbContext(options, http);
var today = DateTime.Today;
var customer = new Db.Party { Type = PartyType.Customer, Name = "مشتری" };
var warehouse = new Db.Warehouse { Name = "انبار" };
var product = new Db.Product { Name = "قطعه", Code = "PART-01", Unit = "عدد", PurchasePrice = 80 };
var category = new Db.ExpenseCategory { Name = "هزینه فروشگاه" };
var bank = new Db.TrsAccount { CompanyId = 1, Code = "BANK-01", Name = "بانک", Kind = TreasuryAccountKind.Bank, OpeningBalance = 1000 };
db.AddRange(customer, warehouse, product, category, bank);
await db.SaveChangesAsync();
db.Transactions.Add(new Db.Transaction
{
    Number = "PR-TEST", Type = TransactionType.Purchase, Date = today.AddDays(-5), WarehouseId = warehouse.Id,
    PartyId = null, Amount = 800, Lines = new() { new Db.TransactionLine { ProductId = product.Id, Quantity = 10, Price = 80, GrossAmount = 800 } }
});
db.Stocks.Add(new Db.Stock { WarehouseId = warehouse.Id, ProductId = product.Id, Quantity = 10, AvgCost = 80 });
await db.SaveChangesAsync();
var inventory = new InventoryService(db, NullLogger<InventoryService>.Instance);
var warranty = new ShopWarrantyService(db, inventory);
var draft = await warranty.SaveDraftAsync(new ShopWarrantyDto
{
    OperationId = Guid.NewGuid(), PartyId = customer.Id, WarehouseId = warehouse.Id, DeviceType = "دستگاه",
    ReceivedAt = today.AddDays(-1), Devices = new()
    {
        new ShopWarrantyDeviceDto { DeviceType = "دستگاه اول", DeviceModel = "مدل ۱", SerialNumber = "SN-1" },
        new ShopWarrantyDeviceDto { DeviceType = "دستگاه دوم", DeviceModel = "مدل ۲", SerialNumber = "SN-2" }
    }, Items = new()
    {
        new ShopWarrantyItemDto { Description = "قطعه پذیرفته", ProductId = product.Id, Quantity = 1, UnitCost = 80, Decision = WarrantyDecision.Accepted, Supplied = true },
        new ShopWarrantyItemDto { Description = "قطعه پذیرفته‌نشده", ProductId = product.Id, Quantity = 1, UnitCost = 80, CustomerUnitPrice = 120, Decision = WarrantyDecision.Rejected, Supplied = true }
    }
}, "test");
Check(draft.Devices.Count == 2 && draft.Devices[0].SerialNumber == "SN-1" && draft.Devices[1].SerialNumber == "SN-2",
      "one warranty acceptance retains multiple devices in order");
draft.Devices[0].DeviceModel = "مدل ویرایش‌شده";
draft = await warranty.SaveDraftAsync(draft, "test");
Check(draft.Devices.Count == 2 && draft.Devices[0].DeviceModel == "مدل ویرایش‌شده",
      "editing a multi-device draft keeps the device order and data");
var completed = await warranty.FinalizeAsync(draft.Id, "test");
await warranty.FinalizeAsync(draft.Id, "test");
var movement = await db.Transactions.Include(t => t.Lines).SingleAsync(t => t.Id == completed.StockTransactionId);
Check(movement.Type == TransactionType.WarrantyOut && movement.Amount == 0 && movement.SettledAmount == 0 &&
      movement.CashAmount == 0 && movement.Lines.Count == 1 && movement.Lines.All(x => x.Price == 0),
      "accepted part alone creates a zero-revenue stock-out");
var chargeSale = await db.Transactions.Include(t => t.Lines).SingleAsync(t => t.Id == completed.ChargeTransactionId);
Check(chargeSale.Amount == 120 && chargeSale.PaymentMethod == PaymentMethod.Credit && chargeSale.CashAmount == 0 &&
      chargeSale.Lines.Single().UnitCostSnapshot == 80 && chargeSale.Lines.Single().Price == 120 &&
      chargeSale.Lines.Single().Description == "قطعه پذیرفته‌نشده", "rejected part is priced independently on one credit sale");
Check((await db.Stocks.SingleAsync(s => s.ProductId == product.Id)).Quantity == 8 && completed.InternalCost == 160 &&
      await db.Transactions.CountAsync(t => t.Type == TransactionType.WarrantyOut) == 1 &&
      await db.Transactions.CountAsync(t => t.Type == TransactionType.Sale) == 1,
      "mixed warranty consumes each part once and retry creates no duplicate movement or charge");
Check(completed.CustomerAmount == 120 && completed.UnpaidAmount == 120 &&
      (await inventory.GetPartyStatementDetailAsync(customer.Id)).ClosingBalance == 120,
      "rejected warranty debit appears in the customer account");
await Rejected(() => warranty.ReceiveCreditAsync(draft.Id, new ShopOrderCreditReceiptCommand
    { OperationId = Guid.NewGuid(), TrsAccountId = bank.Id, Date = today, Amount = 0.5m }, "test"),
    "fractional-rial warranty receipt is rejected by the server");
var receipt = new ShopOrderCreditReceiptCommand { OperationId = Guid.NewGuid(), TrsAccountId = bank.Id, Date = today, Amount = 40 };
await warranty.ReceiveCreditAsync(draft.Id, receipt, "test");
await warranty.ReceiveCreditAsync(draft.Id, receipt, "test");
var creditState = await warranty.CreditStateAsync(draft.Id);
Check(creditState.Outstanding == 80 && creditState.Receipts.Count == 1 &&
      (await inventory.GetPartyStatementDetailAsync(customer.Id)).ClosingBalance == 80,
      "partial receipt reduces debt once despite retries");
var reversal = new ShopOrderCreditReversalCommand { OperationId = Guid.NewGuid(), Date = today };
await warranty.ReverseCreditAsync(draft.Id, creditState.Receipts.Single().Id, reversal, "test");
await warranty.ReverseCreditAsync(draft.Id, creditState.Receipts.Single().Id, reversal, "test");
Check((await warranty.CreditStateAsync(draft.Id)).Outstanding == 120 &&
      (await inventory.GetPartyStatementDetailAsync(customer.Id)).ClosingBalance == 120,
      "reversing a receipt restores debt once");
await warranty.ReceiveCreditAsync(draft.Id, new ShopOrderCreditReceiptCommand
    { OperationId = Guid.NewGuid(), TrsAccountId = bank.Id, Date = today, Amount = 120 }, "test");
Check((await warranty.CreditStateAsync(draft.Id)).Outstanding == 0 &&
      (await inventory.GetPartyStatementDetailAsync(customer.Id)).ClosingBalance == 0 &&
      await TreasuryBalanceCalculator.GetAmountAsync(db, bank.Id) == 1120,
      "real payment settles the debt and increases the selected bank balance");

var free = await warranty.SaveDraftAsync(new ShopWarrantyDto { OperationId = Guid.NewGuid(), PartyId = customer.Id,
    WarehouseId = warehouse.Id, DeviceType = "دیگر", ReceivedAt = today, Items = new() {
      new ShopWarrantyItemDto { Description = "قطعه استفاده‌نشده", ProductId = product.Id, Quantity = 1,
        UnitCost = 80, CustomerUnitPrice = 400, Decision = WarrantyDecision.Rejected, Supplied = false },
      new ShopWarrantyItemDto { Description = "کار انجام‌نشده", Quantity = 1, UnitCost = 50,
        CustomerUnitPrice = 200, Decision = WarrantyDecision.Rejected, Performed = false }
    } }, "test");
var freeDone = await warranty.FinalizeAsync(free.Id, "test");
await warranty.FinalizeAsync(free.Id, "test");
Check(freeDone.CustomerAmount == 0 && freeDone.InternalCost == 0 && freeDone.ChargeTransactionId is null &&
      freeDone.StockTransactionId is null && (await db.Stocks.SingleAsync(s => s.ProductId == product.Id)).Quantity == 8,
      "unused part and unperformed work create neither charge nor stock movement");
await Rejected(() => inventory.DeleteOrderAsync(chargeSale.Id), "linked warranty invoice cannot be deleted directly");

var trade = new ShopTradeInService(db);
var tradeDraft = await trade.SaveDraftAsync(new ShopTradeInDto { OperationId = Guid.NewGuid(), PartyId = customer.Id,
    DeviceType = "دستگاه قبلی", ReceivedAt = today, Value = 30 }, "test");
await trade.FinalizeAsync(tradeDraft.Id, today, "test");
await trade.FinalizeAsync(tradeDraft.Id, today, "test");
Check((await inventory.GetPartyStatementDetailAsync(customer.Id)).ClosingBalance == -30,
    "trade-in creates actual customer credit without a stock movement");
var sale = new Db.Transaction { Number = "SL-CREDIT", Type = TransactionType.Sale, Date = today,
    PartyId = customer.Id, WarehouseId = warehouse.Id, Amount = 100, PaymentMethod = PaymentMethod.Credit };
db.Transactions.Add(sale); await db.SaveChangesAsync();
var use = new ShopTradeInUseCommand { OperationId = Guid.NewGuid(), Kind = TradeInUseKind.ApplyToSale,
    TargetSaleId = sale.Id, Amount = 20, Date = today };
await trade.UseAsync(tradeDraft.Id, use, "test");
await trade.UseAsync(tradeDraft.Id, use, "test");
Check(sale.SettledAmount == 20 && (await inventory.GetPartyStatementDetailAsync(customer.Id)).ClosingBalance == 70,
    "trade-in allocation is traceable and does not duplicate credit on retry");
var refund = new ShopTradeInUseCommand { OperationId = Guid.NewGuid(), Kind = TradeInUseKind.Refund,
    TrsAccountId = bank.Id, Amount = 10, Date = today };
await trade.UseAsync(tradeDraft.Id, refund, "test");
await trade.UseAsync(tradeDraft.Id, refund, "test");
Check(await TreasuryBalanceCalculator.GetAmountAsync(db, bank.Id) == 1110 &&
      (await inventory.GetPartyStatementDetailAsync(customer.Id)).ClosingBalance == 80,
      "remaining trade-in credit is refunded from a real bank once");

var expenses = new ShopExpenseChequeService(db);
var issued = new ExpenseChequeCreateCommand { OperationId = Guid.NewGuid(), Kind = ExpenseChequeKind.Issued,
    Expense = new ExpenseDto { CategoryId = category.Id, Amount = 100, Date = today }, Recipient = "گیرنده",
    IssuingAccountId = bank.Id, Number = "ISS-1", DueDate = today.AddDays(30), HandedAt = today };
var expenseId = await expenses.CreateAsync(issued, ExpenseChequeKind.Issued, "test");
Check(await expenses.CreateAsync(issued, ExpenseChequeKind.Issued, "test") == expenseId &&
      await TreasuryBalanceCalculator.GetAmountAsync(db, bank.Id) == 1110,
      "issuance is idempotent and does not debit bank before clearance");
var issuedLink = await db.ShopExpenseCheques.SingleAsync(c => c.ExpenseId == expenseId);
var clear = new ExpenseChequeStatusCommand { OperationId = Guid.NewGuid(), Date = today, State = ExpenseChequeState.Cleared };
await expenses.ChangeStatusAsync(issuedLink.Id, clear, ExpenseChequeKind.Issued, "test");
await expenses.ChangeStatusAsync(issuedLink.Id, clear, ExpenseChequeKind.Issued, "test");
Check(await TreasuryBalanceCalculator.GetAmountAsync(db, bank.Id) == 1010 &&
      (await new ExpenseService(db).GetReportAsync(null, null, null)).TotalAmount == 100,
      "actual cheque clearance debits bank once without duplicating expense");

var chequeSale = new Db.Transaction { Number = "SL-CHEQUE", Type = TransactionType.Sale, Date = today, PartyId = customer.Id,
    WarehouseId = warehouse.Id, Amount = 70, PaymentMethod = PaymentMethod.Cheque,
    Cheques = new() { new Db.Cheque { Number = "RECEIVED-1", Amount = 70, DueDate = today.AddDays(20), Status = ChequeStatus.InHand } } };
db.Transactions.Add(chequeSale); await db.SaveChangesAsync();
var received = new ExpenseChequeCreateCommand { OperationId = Guid.NewGuid(), Kind = ExpenseChequeKind.Received,
    Expense = new ExpenseDto { CategoryId = category.Id, Amount = 70, Date = today }, Recipient = "گیرنده دوم",
    SourceChequeId = chequeSale.Cheques[0].Id, HandedAt = today };
var receivedExpenseId = await expenses.CreateAsync(received, ExpenseChequeKind.Received, "test");
Check(chequeSale.Cheques[0].Status == ChequeStatus.Endorsed && chequeSale.SettledAmount == 0 &&
      await TreasuryBalanceCalculator.GetAmountAsync(db, bank.Id) == 1010,
      "endorsing a received cheque reserves it without a fabricated bank receipt");
var receivedLink = await db.ShopExpenseCheques.SingleAsync(c => c.ExpenseId == receivedExpenseId);
var externalClear = new ExpenseChequeStatusCommand { OperationId = Guid.NewGuid(), Date = today, State = ExpenseChequeState.Cleared };
await expenses.ChangeStatusAsync(receivedLink.Id, externalClear, ExpenseChequeKind.Received, "test");
await expenses.ChangeStatusAsync(receivedLink.Id, externalClear, ExpenseChequeKind.Received, "test");
Check(chequeSale.SettledAmount == 70 && await TreasuryBalanceCalculator.GetAmountAsync(db, bank.Id) == 1010 &&
      (await inventory.GetPartyStatementDetailAsync(customer.Id)).ClosingBalance == 80,
      "recipient clearance settles the source invoice once without changing our bank");

// A one-rial labor charge must remain credit, and service work never creates a stock row.
var serviceDraft = await warranty.SaveDraftAsync(new ShopWarrantyDto { OperationId = Guid.NewGuid(), PartyId = customer.Id,
    WarehouseId = warehouse.Id, DeviceType = "خدمت", ReceivedAt = today, Items = new() {
      new ShopWarrantyItemDto { Description = "کار ردشده", Quantity = 1, UnitCost = 30, CustomerUnitPrice = 1,
        Decision = WarrantyDecision.Rejected, Performed = true }
    } }, "test");
var serviceDone = await warranty.FinalizeAsync(serviceDraft.Id, "test");
var serviceProductId = await db.Products.Where(p => p.Code == "S-REPAIR").Select(p => p.Id).SingleAsync();
Check(serviceDone.CustomerAmount == 1 && serviceDone.UnpaidAmount == 1 && serviceDone.InternalCost == 30 &&
      !await db.Stocks.AnyAsync(st => st.ProductId == serviceProductId) &&
      (await inventory.GetPartyStatementDetailAsync(customer.Id)).ClosingBalance == 81,
      "one-rial rejected labor is credit and does not consume inventory");

var repairs = new RepairService(db, inventory);
var mixed = await repairs.SaveRepairAsync(new RepairOrderDto { PartyId = customer.Id, DeviceType = "دستگاه ترکیبی",
    ReceivedAt = today, Items = new() {
      new RepairItemDto { Description = "پذیرفته", ProductId = product.Id, Quantity = 1, Cost = 80, Price = 900,
        WarrantyDecision = WarrantyDecision.Accepted, WarrantySupplied = true },
      new RepairItemDto { Description = "ردشده", ProductId = product.Id, Quantity = 1, Cost = 80, Price = 140,
        WarrantyDecision = WarrantyDecision.Rejected, WarrantySupplied = true },
      new RepairItemDto { Description = "قطعه استفاده‌نشده", ProductId = product.Id, Quantity = 1, Cost = 80, Price = 999,
        WarrantyDecision = WarrantyDecision.Rejected, WarrantySupplied = false },
      new RepairItemDto { Description = "کار عادی", Quantity = 1, Cost = 20, Price = 50 },
      new RepairItemDto { Description = "کار پذیرفته", Quantity = 1, Cost = 30, Price = 900,
        WarrantyDecision = WarrantyDecision.Accepted, WarrantyPerformed = true },
      new RepairItemDto { Description = "کار انجام‌نشده", Quantity = 1, Cost = 40, Price = 900,
        WarrantyDecision = WarrantyDecision.Rejected, WarrantyPerformed = false }
    } });
Check(mixed.TotalPrice == 190 && mixed.TotalCost == 210,
      "mixed repair excludes accepted, unused and unperformed charges but keeps their real costs");
await Rejected(() => repairs.SaveRepairAsync(new RepairOrderDto { PartyId = customer.Id, DeviceType = "دور زدن تحویل",
    Status = RepairStatus.Delivered }), "direct repair save cannot bypass delivery and billing");
var invoiceRequest = new RepairInvoiceRequest { WarehouseId = warehouse.Id, PaymentMethod = PaymentMethod.Credit,
    CashType = CashType.CardTransfer, TrsAccountId = bank.Id, CashAmount = 20, DueDate = today.AddDays(30) };
var mixedDone = await repairs.InvoiceAsync(mixed.Id, invoiceRequest, "test");
await repairs.InvoiceAsync(mixed.Id, invoiceRequest, "test");
var mixedSale = await db.Transactions.Include(t => t.Lines).SingleAsync(t => t.Id == mixedDone.InvoiceTransactionId);
Check(mixedDone.WarrantyProcessed && mixedDone.Status == RepairStatus.Delivered && mixedSale.Amount == 190 &&
      mixedSale.SettledAmount == 20 && mixedSale.Lines.Count == 2 &&
      (await db.Stocks.SingleAsync(st => st.ProductId == product.Id)).Quantity == 6 &&
      (await inventory.GetPartyStatementDetailAsync(customer.Id)).ClosingBalance == 251,
      "mixed delivery posts one invoice, real prepayment and one stock-out per supplied part");
await repairs.ReceiveWarrantyCreditAsync(mixed.Id, new ShopOrderCreditReceiptCommand {
    OperationId = Guid.NewGuid(), TrsAccountId = bank.Id, Date = today, Amount = 70 }, "test");
Check((await repairs.WarrantyCreditStateAsync(mixed.Id)).Outstanding == 100 &&
      (await inventory.GetPartyStatementDetailAsync(customer.Id)).ClosingBalance == 181,
      "mixed repair payment reduces its actual unpaid balance");
await Rejected(() => repairs.SetStatusAsync(mixed.Id, RepairStatus.Ready), "finalized mixed repair is immutable");
await Rejected(() => inventory.DeleteOrderAsync(mixedSale.Id), "mixed repair invoice cannot be deleted directly");

var explicitZeroCost = await repairs.SaveRepairAsync(new RepairOrderDto { PartyId = customer.Id, DeviceType = "هزینه صفر",
    ReceivedAt = today, Items = new() { new RepairItemDto { Description = "قطعه مشتری", ProductId = product.Id,
      Quantity = 1, Cost = 0, Price = 0, WarrantyDecision = WarrantyDecision.Accepted, WarrantySupplied = false } } });
Check(explicitZeroCost.Items.Single().Cost == 0, "explicit zero warranty cost is not silently replaced by purchase price");

// A failed mixed finalization must roll back the accepted stock-out as well as
// the rejected sale, including when the same physical product is repeated.
var beforeOuts = await db.Transactions.CountAsync(t => t.Type == TransactionType.WarrantyOut);
var beforeSales = await db.Transactions.CountAsync(t => t.Type == TransactionType.Sale);
var insufficient = await warranty.SaveDraftAsync(new ShopWarrantyDto { OperationId = Guid.NewGuid(),
    PartyId = customer.Id, WarehouseId = warehouse.Id, DeviceType = "تکرار قطعه", ReceivedAt = today, Items = new() {
      new ShopWarrantyItemDto { Description = "پذیرفته", ProductId = product.Id, Quantity = 4, UnitCost = 80,
        Decision = WarrantyDecision.Accepted, Supplied = true },
      new ShopWarrantyItemDto { Description = "ردشده", ProductId = product.Id, Quantity = 4, UnitCost = 80,
        CustomerUnitPrice = 140, Decision = WarrantyDecision.Rejected, Supplied = true }
    } }, "test");
await Rejected(() => warranty.FinalizeAsync(insufficient.Id, "test"), "insufficient combined stock rejects finalization");
Check((await db.Stocks.SingleAsync(st => st.ProductId == product.Id)).Quantity == 6 &&
      await db.Transactions.CountAsync(t => t.Type == TransactionType.WarrantyOut) == beforeOuts &&
      await db.Transactions.CountAsync(t => t.Type == TransactionType.Sale) == beforeSales &&
      !(await db.ShopWarranties.SingleAsync(w => w.Id == insufficient.Id)).FinalizedAt.HasValue,
      "failed finalization rolls back both stock and invoice atomically");

var duplicatePaid = await warranty.SaveDraftAsync(new ShopWarrantyDto { OperationId = Guid.NewGuid(),
    PartyId = customer.Id, WarehouseId = warehouse.Id, DeviceType = "دو ردیف ردشده", ReceivedAt = today, Items = new() {
      new ShopWarrantyItemDto { Description = "اول", ProductId = product.Id, Quantity = 4, UnitCost = 80,
        CustomerUnitPrice = 140, Decision = WarrantyDecision.Rejected, Supplied = true },
      new ShopWarrantyItemDto { Description = "دوم", ProductId = product.Id, Quantity = 4, UnitCost = 80,
        CustomerUnitPrice = 140, Decision = WarrantyDecision.Rejected, Supplied = true }
    } }, "test");
await Rejected(() => warranty.FinalizeAsync(duplicatePaid.Id, "test"), "repeated rejected product quantities are aggregated");
Check((await db.Stocks.SingleAsync(st => st.ProductId == product.Id)).Quantity == 6 &&
      await db.Transactions.CountAsync(t => t.Type == TransactionType.Sale) == beforeSales,
      "repeated rejected rows cannot leave a partial sale or stock change");

var futureProduct = new Db.Product { Name = "قطعه آینده", Code = "FUTURE", Unit = "عدد", PurchasePrice = 20 };
db.Products.Add(futureProduct); await db.SaveChangesAsync();
db.Transactions.Add(new Db.Transaction { Number = "PR-FUTURE", Type = TransactionType.Purchase,
    Date = today.AddDays(1), WarehouseId = warehouse.Id, Amount = 100,
    Lines = new() { new Db.TransactionLine { ProductId = futureProduct.Id, Quantity = 5, Price = 20, GrossAmount = 100 } } });
db.Stocks.Add(new Db.Stock { WarehouseId = warehouse.Id, ProductId = futureProduct.Id, Quantity = 5, AvgCost = 20 });
await db.SaveChangesAsync();
var future = await warranty.SaveDraftAsync(new ShopWarrantyDto { OperationId = Guid.NewGuid(), PartyId = customer.Id,
    WarehouseId = warehouse.Id, DeviceType = "قطعه آینده", ReceivedAt = today, Items = new() {
      new ShopWarrantyItemDto { Description = "قطعه", ProductId = futureProduct.Id, Quantity = 1, UnitCost = 20,
        CustomerUnitPrice = 30, Decision = WarrantyDecision.Rejected, Supplied = true } } }, "test");
await Rejected(() => warranty.FinalizeAsync(future.Id, "test"), "future receipts cannot supply a part delivered today");
Check((await db.Stocks.SingleAsync(st => st.ProductId == futureProduct.Id)).Quantity == 5 &&
      await db.Transactions.CountAsync(t => t.Type == TransactionType.Sale) == beforeSales,
      "future stock validation leaves no sale or stock movement");

var operation = new ShopWarrantyDto { OperationId = Guid.NewGuid(), PartyId = customer.Id, WarehouseId = warehouse.Id,
    DeviceType = "آزمون تکرار", ReceivedAt = today, Items = new() {
      new ShopWarrantyItemDto { Description = "کار رایگان", UnitCost = 1, Decision = WarrantyDecision.Accepted, Performed = true } } };
var savedOnce = await warranty.SaveDraftAsync(operation, "test");
var savedAgain = await warranty.SaveDraftAsync(operation, "test");
Check(savedOnce.Id == savedAgain.Id, "same draft operation safely returns the original record");
operation.Items[0].UnitCost = 2;
await Rejected(() => warranty.SaveDraftAsync(operation, "test"), "same operation id with different content is rejected");

http.HttpContext.Items["ActiveCompanyId"] = 2;
db.ChangeTracker.Clear();
Check(!await db.ShopTradeIns.AnyAsync() && !await db.ShopWarranties.AnyAsync() && !await db.ShopExpenseCheques.AnyAsync(),
    "new financial and warranty records are scoped to the active company");
await Rejected(() => expenses.ChangeStatusAsync(receivedLink.Id,
    new ExpenseChequeStatusCommand { OperationId = Guid.NewGuid(), Date = today, State = ExpenseChequeState.Returned },
    ExpenseChequeKind.Received, "test"), "another company cannot change cheque status");
Console.WriteLine("All warranty, trade-in and expense cheque integration checks passed.");
