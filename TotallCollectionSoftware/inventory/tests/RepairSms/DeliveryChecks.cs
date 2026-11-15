using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.ItAssets;
using Inventory.Api.Services.Repairs;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Db = Inventory.Api.Data;

// Real EF/SQLite with a fake gateway. No request can reach the SMS provider.
internal static class DeliveryChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    public static async Task RunAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var setup = new AppDbContext(dbOptions))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.SystemCompanies.AddRange(new SystemCompany { Id = 1, Name = "فروشگاه اول" },
                new SystemCompany { Id = 2, Name = "فروشگاه دوم" });
            setup.CompanySmsSettings.AddRange(
                new CompanySmsSetting { CompanyId = 1, ModuleKey = SmsModuleCatalog.ShopRepairs, Enabled = true },
                new CompanySmsSetting { CompanyId = 2, ModuleKey = SmsModuleCatalog.ShopRepairs, Enabled = true });
            await setup.SaveChangesAsync();
        }
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.Items["ActiveCompanyId"] = 1;
        await using var db = new AppDbContext(dbOptions, http);
        var http2 = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http2.HttpContext.Items["ActiveCompanyId"] = 2;
        await using var db2 = new AppDbContext(dbOptions, http2);
        await using var workerDb = new AppDbContext(dbOptions);
        var gateway = new FakeSmsGateway();
        var smsOptions = Options.Create(new KavenegarSmsOptions());
        var repairs = new RepairService(db, new InventoryService(db, NullLogger<InventoryService>.Instance), gateway, smsOptions);
        var repairs2 = new RepairService(db2, new InventoryService(db2, NullLogger<InventoryService>.Instance), gateway, smsOptions);
        var worker = new RepairSmsDispatcher(workerDb, gateway, smsOptions);

        var customer = new Db.Party { Name = "علی رضایی", Type = PartyType.Customer, Mobile = "۰۹۱۲۳۴۵۶۷۸۹" };
        var invalidName = new Db.Party { Name = new string('ا', 101), Type = PartyType.Customer, Mobile = "09120000003" };
        var noMobile = new Db.Party { Name = "مشتری بدون موبایل", Type = PartyType.Customer, Phone = "02112345678" };
        var warehouse = new Db.Warehouse { Name = "انبار اول" };
        var service = new Db.Product { Name = "خدمت", Code = "SMS-SVC-1", Unit = "مورد", IsService = true };
        var part = new Db.Product { Name = "قطعه", Code = "SMS-PART-1", Unit = "عدد", PurchasePrice = 80 };
        db.AddRange(customer, invalidName, noMobile, warehouse, service, part);
        await db.SaveChangesAsync();
        db.Transactions.Add(new Db.Transaction { Number = "PR-SMS-1", Type = TransactionType.Purchase,
            Date = DateTime.Today.AddDays(-1), WarehouseId = warehouse.Id, Amount = 240,
            Lines = new() { new Db.TransactionLine { ProductId = part.Id, Quantity = 3, Price = 80, GrossAmount = 240 } } });
        db.Stocks.Add(new Db.Stock { WarehouseId = warehouse.Id, ProductId = part.Id, Quantity = 3, AvgCost = 80 });
        await db.SaveChangesAsync();
        var customer2 = new Db.Party { Name = "مریم احمدی", Type = PartyType.Customer, Mobile = "09120000002" };
        var warehouse2 = new Db.Warehouse { Name = "انبار دوم" };
        var service2 = new Db.Product { Name = "خدمت دوم", Code = "SMS-SVC-2", Unit = "مورد", IsService = true };
        db2.AddRange(customer2, warehouse2, service2);
        await db2.SaveChangesAsync();

        RepairItemDto Labor(int productId, decimal price = 150) => new()
            { Description = "کار", ProductId = productId, Quantity = 1, Cost = 20, Price = price };
        async Task<RepairOrderDto> Draft(RepairService svc, int partyId, params RepairItemDto[] items)
        {
            // Keep unrelated registration messages out of the queue during these delivery tests.
            gateway.IsEnabled = false;
            try
            {
                return await svc.SaveRepairAsync(new RepairOrderDto
                    { PartyId = partyId, DeviceType = "دستگاه", ReceivedAt = DateTime.Today, Items = items.ToList() });
            }
            finally { gateway.IsEnabled = true; }
        }
        RepairInvoiceRequest Cash(int wh) => new() { WarehouseId = wh, PaymentMethod = PaymentMethod.Cash, CashType = CashType.Cash };
        RepairSmsNotificationDto Delivery(RepairOrderDto dto) => dto.SmsNotifications.Single(n => n.EventType == RepairSmsEventType.Delivery);

        var cash = await Draft(repairs, customer.Id, Labor(service.Id));
        var before = gateway.Sent.Count;
        var cashDone = await repairs.InvoiceAsync(cash.Id, Cash(warehouse.Id));
        Check(cashDone.Status == RepairStatus.Delivered && cashDone.InvoiceTransactionId is > 0 &&
            Delivery(cashDone).Status == RepairSmsStatus.Pending && gateway.Sent.Count == before,
            "cash delivery commits an invoice and queues its SMS before any provider call");
        var cashRetry = await repairs.InvoiceAsync(cash.Id, Cash(warehouse.Id));
        Check(cashRetry.InvoiceTransactionId == cashDone.InvoiceTransactionId &&
            await db.RepairSmsNotifications.CountAsync(n => n.RepairOrderId == cash.Id && n.EventType == RepairSmsEventType.Delivery) == 1 &&
            await db.Transactions.CountAsync(t => t.Type == TransactionType.Sale) == 1,
            "repeating cash delivery creates neither a second sale nor another event");
        await worker.ProcessOneAsync();
        var sent = gateway.Sent[^1];
        Check(gateway.Sent.Count == before + 1 && sent.Recipient == "09123456789" && sent.CustomerName == "علی رضایی" &&
            sent.CompanyName == "فروشگاه اول" && sent.Token == cash.Number[3..] && sent.Template == "RepairDelivered" &&
            !await worker.ProcessOneAsync(), "cash delivery addresses the exact customer, company and number once");

        var credit = await Draft(repairs, customer.Id, Labor(service.Id, 200));
        var creditRequest = new RepairInvoiceRequest { WarehouseId = warehouse.Id, PaymentMethod = PaymentMethod.Credit,
            DueDate = DateTime.Today.AddDays(30), CashAmount = 50, CashType = CashType.Cash };
        var creditDone = await repairs.InvoiceAsync(credit.Id, creditRequest);
        var creditSale = await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == creditDone.InvoiceTransactionId);
        Check(creditSale.PaymentMethod == PaymentMethod.Mixed && creditSale.CashAmount == 50 &&
            creditSale.DueDate?.Date == creditRequest.DueDate?.Date && Delivery(creditDone).Status == RepairSmsStatus.Pending,
            "credit delivery preserves prepayment and due date and stages the delivery event");
        await repairs.InvoiceAsync(credit.Id, creditRequest);
        await worker.ProcessOneAsync();
        Check(await db.RepairSmsNotifications.CountAsync(n => n.RepairOrderId == credit.Id && n.EventType == RepairSmsEventType.Delivery) == 1 &&
            gateway.Sent[^1].Token == credit.Number[3..] && !await worker.ProcessOneAsync(),
            "repeated credit delivery does not duplicate the event or send");

        var free = await Draft(repairs2, customer2.Id, new RepairItemDto { Description = "کار پذیرفته",
            ProductId = service2.Id, Quantity = 1, Cost = 10, Price = 0,
            WarrantyDecision = WarrantyDecision.Accepted, WarrantyPerformed = true });
        var freeDone = await repairs2.InvoiceAsync(free.Id, Cash(warehouse2.Id));
        Check(freeDone.Status == RepairStatus.Delivered && freeDone.WarrantyProcessed && freeDone.InvoiceTransactionId is null &&
            Delivery(freeDone).Status == RepairSmsStatus.Pending,
            "free warranty delivery queues SMS without creating a sales invoice");
        await repairs2.InvoiceAsync(free.Id, Cash(warehouse2.Id));
        await worker.ProcessOneAsync();
        Check(gateway.Sent[^1].CompanyName == "فروشگاه دوم" && gateway.Sent[^1].CustomerName == "مریم احمدی" &&
            gateway.Sent[^1].Recipient == "09120000002" && gateway.Sent[^1].Token == free.Number[3..] &&
            await db2.RepairSmsNotifications.CountAsync(n => n.RepairOrderId == free.Id && n.EventType == RepairSmsEventType.Delivery) == 1,
            "free warranty retry is idempotent and the worker preserves the second tenant");

        var invalid = await Draft(repairs, invalidName.Id, Labor(service.Id));
        var invalidDone = await repairs.InvoiceAsync(invalid.Id, Cash(warehouse.Id));
        Check(invalidDone.Status == RepairStatus.Delivered && invalidDone.InvoiceTransactionId is > 0 &&
            Delivery(invalidDone).Status == RepairSmsStatus.InvalidCustomerName && !await worker.ProcessOneAsync(),
            "an incompatible customer name warns but does not prevent invoicing or delivery");
        var missing = await Draft(repairs, noMobile.Id, Labor(service.Id));
        var missingDone = await repairs.InvoiceAsync(missing.Id, Cash(warehouse.Id));
        Check(missingDone.Status == RepairStatus.Delivered && Delivery(missingDone).Status == RepairSmsStatus.InvalidMobile &&
            !await worker.ProcessOneAsync(), "missing mobile neither blocks delivery nor falls back to a landline");

        var cancelled = await Draft(repairs, customer.Id, Labor(service.Id));
        await repairs.SetStatusAsync(cancelled.Id, RepairStatus.Cancelled);
        var rejected = false;
        try { await repairs.InvoiceAsync(cancelled.Id, Cash(warehouse.Id)); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && !await db.RepairSmsNotifications.AnyAsync(n => n.RepairOrderId == cancelled.Id && n.EventType == RepairSmsEventType.Delivery),
            "a cancelled or failed delivery produces no delivery notification");

        RepairItemDto Part(WarrantyDecision decision) => new() { Description = decision.ToString(), ProductId = part.Id,
            Quantity = 1, Cost = 80, Price = decision == WarrantyDecision.Rejected ? 140 : 0,
            WarrantyDecision = decision, WarrantySupplied = true };
        var mixed = await Draft(repairs, customer.Id, Part(WarrantyDecision.Accepted), Part(WarrantyDecision.Rejected));
        var mixedRequest = new RepairInvoiceRequest { WarehouseId = warehouse.Id, PaymentMethod = PaymentMethod.Credit,
            DueDate = DateTime.Today.AddDays(30) };
        var mixedDone = await repairs.InvoiceAsync(mixed.Id, mixedRequest);
        Check(mixedDone.Status == RepairStatus.Delivered && mixedDone.WarrantyProcessed &&
            mixedDone.InvoiceTransactionId is > 0 && mixedDone.WarrantyStockTransactionId is > 0 &&
            (await db.Stocks.AsNoTracking().SingleAsync(s => s.ProductId == part.Id)).Quantity == 1 &&
            Delivery(mixedDone).Status == RepairSmsStatus.Pending,
            "mixed warranty consumes stock, creates its sale and stages exactly one delivery event");
        await repairs.InvoiceAsync(mixed.Id, mixedRequest);
        await worker.ProcessOneAsync();
        Check(await db.RepairSmsNotifications.CountAsync(n => n.RepairOrderId == mixed.Id && n.EventType == RepairSmsEventType.Delivery) == 1 &&
            gateway.Sent[^1].Token == mixed.Number[3..] && !await worker.ProcessOneAsync(),
            "mixed warranty retry does not duplicate the delivery message");

        // One part remains: accepted stock-out can start, but there is no second part
        // for the rejected sale. Neither the stock-out nor an SMS may survive failure.
        var insufficient = await Draft(repairs, customer.Id, Part(WarrantyDecision.Accepted), Part(WarrantyDecision.Rejected));
        var outBefore = await db.Transactions.CountAsync(t => t.Type == TransactionType.WarrantyOut);
        var saleBeforeInsufficient = await db.Transactions.CountAsync(t => t.Type == TransactionType.Sale);
        var insufficientRejected = false;
        try { await repairs.InvoiceAsync(insufficient.Id, mixedRequest); }
        catch (InvalidOperationException) { insufficientRejected = true; }
        var insufficientAfter = await repairs.GetRepairAsync(insufficient.Id);
        Check(insufficientRejected && insufficientAfter!.Status == RepairStatus.Received &&
            (await db.Stocks.AsNoTracking().SingleAsync(s => s.ProductId == part.Id)).Quantity == 1 &&
            await db.Transactions.CountAsync(t => t.Type == TransactionType.WarrantyOut) == outBefore &&
            await db.Transactions.CountAsync(t => t.Type == TransactionType.Sale) == saleBeforeInsufficient &&
            !await db.RepairSmsNotifications.AnyAsync(n => n.RepairOrderId == insufficient.Id && n.EventType == RepairSmsEventType.Delivery),
            "failed mixed delivery rolls back stock, sale and SMS event");

        var staleReady = await Draft(repairs, customer.Id, Labor(service.Id));
        await repairs.SetStatusAsync(staleReady.Id, RepairStatus.Ready);
        var staleDone = await repairs.InvoiceAsync(staleReady.Id, Cash(warehouse.Id));
        before = gateway.Sent.Count;
        await worker.ProcessOneAsync(); // Earlier Ready job is no longer valid.
        await worker.ProcessOneAsync(); // Delivery job is still eligible.
        var staleRead = (await repairs.GetRepairAsync(staleReady.Id))!;
        Check(staleRead.SmsNotifications.Single(n => n.EventType == RepairSmsEventType.Ready).Status == RepairSmsStatus.Cancelled &&
            Delivery(staleRead).Status == RepairSmsStatus.Accepted && gateway.Sent.Count == before + 1 &&
            gateway.Sent[^1].Template == "RepairDelivered", "delivery cancels a stale Ready message but sends its own event");

        // Throw after a sale is saved but before the event can be inserted: the owning
        // transaction must roll back both the financial transaction and status change.
        var failStage = await Draft(repairs, customer.Id, Labor(service.Id));
        var salesBefore = await db.Transactions.CountAsync(t => t.Type == TransactionType.Sale);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailDeliverySms BEFORE INSERT ON RepairSmsNotifications WHEN NEW.EventType = 3 BEGIN SELECT RAISE(ABORT, 'test stage failure'); END;");
        var stageRejected = false;
        try { await repairs.InvoiceAsync(failStage.Id, Cash(warehouse.Id)); }
        catch (DbUpdateException) { stageRejected = true; }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER FailDeliverySms;"); }
        var afterFailure = await repairs.GetRepairAsync(failStage.Id);
        Check(stageRejected && afterFailure!.Status == RepairStatus.Received && afterFailure.InvoiceTransactionId is null &&
            await db.Transactions.CountAsync(t => t.Type == TransactionType.Sale) == salesBefore &&
            !await db.RepairSmsNotifications.AnyAsync(n => n.RepairOrderId == failStage.Id && n.EventType == RepairSmsEventType.Delivery),
            "failure staging the outbox rolls back the sale and delivery together");

        // Simulate an earlier delivery which predates this integration: idempotent reads
        // must not generate a new message merely because the integration is installed.
        var historical = await Draft(repairs, customer.Id, Labor(service.Id));
        var raw = await db.RepairOrders.SingleAsync(r => r.Id == historical.Id);
        raw.Status = RepairStatus.Delivered;
        raw.DeliveredAt = DateTime.Now;
        raw.InvoiceTransactionId = cashDone.InvoiceTransactionId;
        await db.SaveChangesAsync();
        await repairs.InvoiceAsync(historical.Id, Cash(warehouse.Id));
        Check(!await db.RepairSmsNotifications.AnyAsync(n => n.RepairOrderId == historical.Id && n.EventType == RepairSmsEventType.Delivery),
            "an already finalized historical repair is not backfilled");
    }
}
