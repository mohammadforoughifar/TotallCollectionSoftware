using System.Security.Claims;
using Inventory.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RadisHr.Api.Data;
using RadisHr.Shared.Models;
using CoreDb = Inventory.Api.Data.AppDbContext;
using HrDb = RadisHr.Api.Data.AppDbContext;

var passed = 0;
void Check(string name, bool ok)
{
    if (!ok) throw new InvalidOperationException("FAIL: " + name);
    passed++;
}

// No SQL Server process is needed: inspect the real provider's model and generated migrations.
var services = new ServiceCollection();
services.AddLogging();
services.AddRadisHrModule("SqlServer", "Server=localhost;Database=InventoryTests;Trusted_Connection=True;TrustServerCertificate=True");
services.AddControllers(options => options.Conventions.Add(new RadisHrControllerConvention()))
    .AddApplicationPart(typeof(RadisHr.Api.Controllers.EmployeesController).Assembly);
await using (var provider = services.BuildServiceProvider())
{
    var authorization = provider.GetRequiredService<IAuthorizationService>();
    var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
    var admin = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") }, "test"));
    var allowed = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("permission", "RadisHr.Access") }, "test"));
    var denied = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Operator") }, "test"));
    var legacyOnly = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "ceo") }, "test"));
    Check("anonymous denied", !(await authorization.AuthorizeAsync(anonymous, null, "RadisHrAccess")).Succeeded);
    Check("admin allowed", (await authorization.AuthorizeAsync(admin, null, "RadisHrAccess")).Succeeded);
    Check("explicit HR permission allowed", (await authorization.AuthorizeAsync(allowed, null, "RadisHrAccess")).Succeeded);
    Check("ordinary operator denied", !(await authorization.AuthorizeAsync(denied, null, "RadisHrAccess")).Succeeded);
    Check("old standalone role cannot bypass RBAC", !(await authorization.AuthorizeAsync(legacyOnly, null, "RadisHrAccess")).Succeeded);

    var actions = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
        .OfType<ControllerActionDescriptor>().Where(a => a.ControllerTypeInfo.Namespace == "RadisHr.Api.Controllers").ToList();
    Check("HR controllers discovered in Inventory assembly", actions.Count > 0);
    Check("every HR action is protected", actions.All(a => a.FilterDescriptors.Any(f => f.Filter is AuthorizeFilter)));
    var routes = actions.Select(a => a.AttributeRouteInfo!.Template!).ToHashSet();
    Check("native employee API", routes.Contains("api/hr/employees"));
    Check("legacy employee alias", routes.Contains("radis-hr/api/employees"));
    Check("no bare employee API collision", !routes.Contains("api/employees"));
    Check("no duplicate HR login", actions.All(a => a.ControllerName != "Auth"));

    await using var scope = provider.CreateAsyncScope();
    var sqlDb = scope.ServiceProvider.GetRequiredService<HrDb>();
    Check("original migration preserved", sqlDb.Database.GetMigrations().Contains("20260817180049_InitialCreate"));
    var script = sqlDb.GetService<IMigrator>().GenerateScript();
    Check("separate migration history", script.Contains("__EFMigrationsHistory_RadisHr"));
    Check("legacy HR table names retained", script.Contains("RadisHrUsers") && script.Contains("Employees"));
}

// Real SQLite storage shared with the core, including a second initialization (restart).
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var core = new CoreDb(new DbContextOptionsBuilder<CoreDb>().UseSqlite(connection).Options);
await core.Database.EnsureCreatedAsync();
var coreUser = new Inventory.Api.Data.User { Username = "core-user", PasswordHash = "test-only", Role = "Admin" };
core.Users.Add(coreUser);
await core.SaveChangesAsync();
var hrOptions = new DbContextOptionsBuilder<HrDb>().UseSqlite(connection).Options;
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Database:SeedDemoData"] = "false"
}).Build();

await using (var hr = new HrDb(hrOptions))
{
    var coreTables = core.Model.GetEntityTypes().Select(e => e.GetTableName()).OfType<string>().ToHashSet();
    var hrTables = hr.Model.GetEntityTypes().Select(e => e.GetTableName()).OfType<string>().ToHashSet();
    Check("no core/HR table collisions", !coreTables.Intersect(hrTables).Any());
    await RadisHrDatabaseInitializer.InitializeAsync(hr);
    await DbSeeder.SeedAsync(hr, configuration);
    Check("no unsolicited demo employees", !await hr.Employees.AnyAsync());
    Check("no standalone default logins", !await hr.Users.AnyAsync());
    Check("HR seeding does not overwrite core users", await core.Users.CountAsync() == 1);
    var employee = new Employee { Code = "TEST-HR", First = "تست", Last = "یکپارچگی", Salary = 123456789m };
    employee.Contracts.Add(new EmployeeContract { Name = "contract", Month = "1405/06", FileId = "file-test" });
    hr.Employees.Add(employee);
    hr.StoredFiles.Add(new StoredFile { Uid = "file-test", Name = "test.txt", ContentType = "text/plain", Size = 3, Content = new byte[] { 1, 2, 3 } });
    await hr.SaveChangesAsync();
}
await using (var hr = new HrDb(hrOptions))
{
    await RadisHrDatabaseInitializer.InitializeAsync(hr);
    await DbSeeder.SeedAsync(hr, configuration);
    var employee = await hr.Employees.Include(e => e.Contracts).SingleAsync();
    Check("restart preserves employees and salary", employee.Code == "TEST-HR" && employee.Salary == 123456789m);
    Check("restart preserves contract links", employee.Contracts.Single().FileId == "file-test");
    Check("binary attachments preserved", (await hr.StoredFiles.SingleAsync()).Content.SequenceEqual(new byte[] { 1, 2, 3 }));
    var rules = await hr.StatutoryRules.SingleAsync();
    Check("tax brackets are not doubled on reload", rules.TaxBrackets.Count == StatutoryRules.DefaultTaxBrackets().Count);
    Check("core data still present", await core.Users.CountAsync() == 1);
}
Console.WriteLine($"HR integration tests: {passed} passed.");
