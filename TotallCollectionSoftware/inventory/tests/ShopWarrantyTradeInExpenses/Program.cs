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
    ReceivedAt = today.AddDays(-1), Items = new()
    {
        new ShopWarrantyItemDto { Description = "قطعه پذیرفته", ProductId = product.Id, Quantity = 1, UnitCost = 80, Decision = WarrantyDecision.Accepted, Supplied = true },
        new ShopWarrantyItemDto { Description = "قطعه پذیرفته‌نشده", ProductId = product.Id, Quantity = 1, UnitCost = 80, Decision = WarrantyDecision.Rejected, Supplied = true }
    }
}, "test");
var completed = await warranty.FinalizeAsync(draft.Id, "test");
await warranty.FinalizeAsync(draft.Id, "test");
var movement = await db.Transactions.Include(t => t.Lines).SingleAsync(t => t.Id == completed.StockTransactionId);
Check(movement.Type == TransactionType.WarrantyOut && movement.Amount == 0 && movement.SettledAmount == 0 &&
      movement.CashAmount == 0 && movement.Lines.Count == 2 && movement.Lines.All(x => x.Price == 0),
      "accepted and rejected warranty lines create only zero-revenue stock-out");
Check((await db.Stocks.SingleAsync(s => s.ProductId == product.Id)).Quantity == 8 && completed.InternalCost == 160 &&
      await db.Transactions.CountAsync(t => t.Type == TransactionType.WarrantyOut) == 1,
      "warranty consumes actual inventory once and keeps internal cost");

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
Check(await TreasuryBalanceCalculator.GetAmountAsync(db, bank.Id) == 990 &&
      (await inventory.GetPartyStatementDetailAsync(customer.Id)).ClosingBalance == 80,
      "remaining trade-in credit is refunded from a real bank once");

var expenses = new ShopExpenseChequeService(db);
var issued = new ExpenseChequeCreateCommand { OperationId = Guid.NewGuid(), Kind = ExpenseChequeKind.Issued,
    Expense = new ExpenseDto { CategoryId = category.Id, Amount = 100, Date = today }, Recipient = "گیرنده",
    IssuingAccountId = bank.Id, Number = "ISS-1", DueDate = today.AddDays(30), HandedAt = today };
var expenseId = await expenses.CreateAsync(issued, ExpenseChequeKind.Issued, "test");
Check(await expenses.CreateAsync(issued, ExpenseChequeKind.Issued, "test") == expenseId &&
      await TreasuryBalanceCalculator.GetAmountAsync(db, bank.Id) == 990,
      "issuance is idempotent and does not debit bank before clearance");
var issuedLink = await db.ShopExpenseCheques.SingleAsync(c => c.ExpenseId == expenseId);
var clear = new ExpenseChequeStatusCommand { OperationId = Guid.NewGuid(), Date = today, State = ExpenseChequeState.Cleared };
await expenses.ChangeStatusAsync(issuedLink.Id, clear, ExpenseChequeKind.Issued, "test");
await expenses.ChangeStatusAsync(issuedLink.Id, clear, ExpenseChequeKind.Issued, "test");
Check(await TreasuryBalanceCalculator.GetAmountAsync(db, bank.Id) == 890 &&
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
      await TreasuryBalanceCalculator.GetAmountAsync(db, bank.Id) == 890,
      "endorsing a received cheque reserves it without a fabricated bank receipt");
var receivedLink = await db.ShopExpenseCheques.SingleAsync(c => c.ExpenseId == receivedExpenseId);
var externalClear = new ExpenseChequeStatusCommand { OperationId = Guid.NewGuid(), Date = today, State = ExpenseChequeState.Cleared };
await expenses.ChangeStatusAsync(receivedLink.Id, externalClear, ExpenseChequeKind.Received, "test");
await expenses.ChangeStatusAsync(receivedLink.Id, externalClear, ExpenseChequeKind.Received, "test");
Check(chequeSale.SettledAmount == 70 && await TreasuryBalanceCalculator.GetAmountAsync(db, bank.Id) == 890 &&
      (await inventory.GetPartyStatementDetailAsync(customer.Id)).ClosingBalance == 80,
      "recipient clearance settles the source invoice once without changing our bank");

http.HttpContext.Items["ActiveCompanyId"] = 2;
db.ChangeTracker.Clear();
Check(!await db.ShopTradeIns.AnyAsync() && !await db.ShopWarranties.AnyAsync() && !await db.ShopExpenseCheques.AnyAsync(),
    "new financial and warranty records are scoped to the active company");
await Rejected(() => expenses.ChangeStatusAsync(receivedLink.Id,
    new ExpenseChequeStatusCommand { OperationId = Guid.NewGuid(), Date = today, State = ExpenseChequeState.Returned },
    ExpenseChequeKind.Received, "test"), "another company cannot change cheque status");
Console.WriteLine("All warranty, trade-in and expense cheque integration checks passed.");
