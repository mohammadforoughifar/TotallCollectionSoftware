using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services.Moadian;
using Inventory.Shared.Dtos.Moadian;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Inventory.Shared.Dtos.Moadian.VmInvoiceItems;

internal static class InvoiceNumberRegression
{
    public static async Task RunAsync()
    {
        // Independent database: test real preview/create/delete paths without tax submission.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.SystemCompanies.Add(new SystemCompany { Id = 1, Name = "Numbering test", IsActive = true });
        db.CPInfos.AddRange(new CpInfo { Id = 1, CompanyId = 1 }, new CpInfo { Id = 3, CompanyId = 1 },
            new CpInfo { Id = 4, CompanyId = 1 });
        db.FiscalYears.Add(new FiscalYear { Id = 1, CpInfo = 1, Title = "Test year" });
        db.GoodsOrServices.Add(new GoodsOrService { Id = 1, CpInfo = 1, Name = "Test goods", UniqueIdentifier = "2720000005833" });
        await db.SaveChangesAsync();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1"),
                new Claim(ClaimTypes.Name, "numbering-test"), new Claim(ClaimTypes.Role, "Admin") }, "test"))
        };
        http.Request.Headers["X-Company-Id"] = "1";
        var service = new ConnectService(db, new HttpContextAccessor { HttpContext = http });
        await ExpectNext(1, 1, "empty provider starts at 1");
        var first = await Create(1);
        var second = await Create(2);
        var third = await Create(3);
        db.Invoices.Add(new Invoice { CpInfo = 3, InvoiceNumber = 900 });
        db.Invoices.Add(new Invoice { CpInfo = 4, InvoiceNumber = 80, IsDelete = true });
        await db.SaveChangesAsync();
        await ExpectNext(1, 4, "other providers' larger numbers are ignored");

        await Delete(third.Id);
        await ExpectNext(1, 3, "deleting 3 from 1/2/3 previews 3, not 4 or 2");
        var replacement = await Create(3);
        Check(await db.Invoices.CountAsync(i => i.CpInfo == 1 && i.InvoiceNumber == 3 && i.IsDelete) == 1
            && await db.Invoices.CountAsync(i => i.CpInfo == 1 && i.InvoiceNumber == 3 && !i.IsDelete) == 1,
            "number reuse preserves deleted history without duplicating an active number");
        await Delete(replacement.Id);
        await Delete(second.Id);
        await ExpectNext(1, 2, "multiple deleted trailing numbers use active maximum plus one");
        var newSecond = await Create(2);
        await Delete(first.Id);
        await ExpectNext(1, 3, "deleting a lower number does not fill an internal gap");
        await ExpectNext(3, 901, "other provider retains its independent numbering");
        await ExpectNext(4, 1, "provider with only deleted invoices starts again at 1");
        await Delete(newSecond.Id);
        await ExpectNext(1, 1, "all invoices deleted resets the next number to 1");
        var restarted = await Create(1);
        var persisted = await db.Invoices.SingleAsync(i => i.Id == restarted.Id);
        persisted.StatusSentToSystem = "1";
        persisted.ShowButtons = false;
        await db.SaveChangesAsync();
        var denied = await service.DeleteInvoiceByIdAsync(restarted.Id);
        Check(!denied.Success && denied.Status == 400, "existing prohibition on deleting sent invoices is preserved");
        await ExpectNext(1, 2, "sent invoice keeps its number and remains part of active maximum");
        Check((await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == restarted.Id)).InvoiceNumber == 1,
            "existing invoice numbers are never renumbered");

        async Task ExpectNext(int cpInfo, int expected, string description)
        {
            var response = await service.NextInvoiceNumberAsync(cpInfo);
            Check(response.Success && response.Data is int actual && actual == expected, description);
        }
        async Task<InvoiceVM> Create(int expected)
        {
            var form = new InvoiceVM
            {
                IsNew = true, CPInfoId = 1, TypePattern = "نوع دوم", PaymentType = "نقدی",
                StatusSentToSystem = "0", FiscalYearId = "1", DateOrder = new DateOnly(2026, 11, 5),
                InvoiceItems = new() { new VmInvoiceItems { GoodsOrServicesId = 1, UniqueIdentifier = "2720000005833",
                    Count = 1, PriceUnit = 100, Discount = 0, ValueAddedPercentage = 10 } }
            };
            var result = await service.SabtInvoiceAsync(form);
            Check(result.Success && form.NumberInvoice == expected, "actual create assigns number " + expected);
            return form;
        }
        async Task Delete(int id)
        {
            var result = await service.DeleteInvoiceByIdAsync(id);
            Check(result.Success, "draft invoice can be soft-deleted");
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("Invoice numbering: " + description);
        Console.WriteLine("PASS: invoice numbering: " + description);
    }
}
