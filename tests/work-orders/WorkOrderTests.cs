using System.Security.Claims;
using System.Text.Json;
using Inventory.Api.Controllers;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Inventory.Api.Services.ItAssets;
using Inventory.Shared;
using Inventory.Shared.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Inventory.WorkOrders.Tests;

public class WorkOrderTests
{
    private static JsonElement Json(IActionResult r) => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(r).Value);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Creation_materializes_every_date_for_every_recipient_and_close_does_not_duplicate(int recurrence)
    {
        using var f = new Fixture();
        var c = f.Controller(1, "Admin");
        var due = ScheduleTests.Fa(1450, 7, 27);
        var result = Json(await c.Create(new() { Title = "سرویس", DueAt = due, Recurrence = recurrence,
            AssigneeUserIds = new() { 2, 3, 2 }, ChecklistItems = new() { "بررسی", "تحویل" } }));
        var count = WorkOrderSchedule.Dates(due, recurrence).Count;
        Assert.Equal(count, result.GetProperty("occurrenceCount").GetInt32());
        var orders = await f.Db.WorkOrders.OrderBy(w => w.DueAt).ToListAsync();
        Assert.Equal(count, orders.Count);
        Assert.Equal(WorkOrderSchedule.Dates(due, recurrence), orders.Select(w => w.DueAt).ToArray());
        Assert.Equal(count * 2, await f.Db.WorkOrderAssignees.CountAsync());
        Assert.Equal(count * 2, await f.Db.WorkOrderChecklistItems.CountAsync());
        Assert.Single(orders.Select(w => w.RecurrenceSeriesId).Distinct());
        Assert.Equal(count, orders.Select(w => w.Number).Distinct().Count());
        foreach (var user in new[] { 2, 3 })
        {
            var calendar = Json(await f.Controller(user).Calendar(due.AddDays(-1), due.AddYears(1)));
            Assert.Equal(count, calendar.GetArrayLength());
        }
        Assert.Equal(2, f.Notify.Sends); // a single summary per assignee
        var id = orders[0].Id;
        Assert.IsType<OkResult>(await f.Controller(2).Reply(id, new() { Done = true }));
        Assert.Equal(1, await f.Db.WorkOrderAssignees.CountAsync(a => a.Done == true));
        Assert.IsType<OkResult>(await c.Close(id, new()));
        Assert.Equal(count, await f.Db.WorkOrders.CountAsync());
        Assert.Equal(count - 1, await f.Db.WorkOrders.CountAsync(w => w.Status == "Open"));
    }

    [Fact]
    public async Task Delete_requires_independent_permission_and_is_single_occurrence_soft_delete()
    {
        using var f = new Fixture();
        var c = f.Controller(1, "Admin");
        var r = Json(await c.Create(new() { Title = "تکرار", DueAt = ScheduleTests.Fa(1450, 7, 28), Recurrence = 1, AssigneeUserIds = new() { 2 } }));
        var id = r.GetProperty("id").GetInt32();
        await f.Grant(2, true, "View", "Create");
        Assert.False(Json(await f.Controller(2).MyAccess()).GetProperty("canDelete").GetBoolean());
        Assert.IsType<ForbidResult>(await f.Controller(2).Delete(id));
        await f.Grant(2, true, "Delete");
        Assert.True(Json(await f.Controller(2).MyAccess()).GetProperty("canDelete").GetBoolean());
        Assert.IsType<NoContentResult>(await f.Controller(2).Delete(id));
        Assert.Equal(2, await f.Db.WorkOrders.CountAsync());
        Assert.Equal(3, await f.Db.WorkOrders.IgnoreQueryFilters().CountAsync());
        var deleted = await f.Db.WorkOrders.IgnoreQueryFilters().SingleAsync(w => w.Id == id);
        Assert.NotNull(deleted.DeletedAt);
        Assert.Equal(2, deleted.DeletedByUserId);
        Assert.Contains(await f.Db.WorkOrderLogs.Where(l => l.OrderId == id).ToListAsync(), l => l.Action == "Deleted");
        Assert.IsType<NotFoundResult>(await f.Controller(2).Detail(id));
        Assert.IsType<NotFoundObjectResult>(await f.Controller(2).Reply(id, new()));
        Assert.IsType<NotFoundResult>(await f.Controller(2).Seen(id));
        Assert.IsType<ForbidResult>(await f.Controller(2).Logs(id));
        Assert.IsType<ForbidResult>(await f.Controller(2).Checklist(id));
        var cal = Json(await f.Controller(2).Calendar(ScheduleTests.Fa(1450, 7, 1), ScheduleTests.Fa(1450, 8, 1)));
        Assert.Equal(2, cal.GetArrayLength());
        await new WorkOrderSchedulingService(f.Db).UpgradeLegacyAsync(DateTime.Now);
        Assert.Equal(2, await f.Db.WorkOrders.CountAsync()); // never resurrect deleted occurrences
        var guard = new AttachmentGuard(f.Db, null!);
        Assert.Equal(AttachmentAccess.None, await guard.CheckAsync("WorkOrders", id, 1, true));
    }

    [Fact]
    public async Task Inactive_role_and_legacy_operator_cannot_delete()
    {
        using var f = new Fixture();
        var result = Json(await f.Controller(1, "Admin").Create(new() { Title = "تست", DueAt = ScheduleTests.Fa(1450, 1, 1), AssigneeUserIds = new() { 2 } }));
        var id = result.GetProperty("id").GetInt32();
        Assert.IsType<ForbidResult>(await f.Controller(2, "Operator").Delete(id));
        await f.Grant(2, false, "Delete", "View");
        Assert.IsType<ForbidResult>(await f.Controller(2, "Admin").Delete(id)); // token's legacy admin cannot bypass assigned RBAC
    }

    [Fact]
    public async Task Delete_permission_without_visibility_cannot_delete_an_unrelated_order()
    {
        using var f = new Fixture();
        var r = Json(await f.Controller(1, "Admin").Create(new() { Title = "تست", DueAt = ScheduleTests.Fa(1450, 1, 1), AssigneeUserIds = new() { 2 } }));
        await f.Grant(3, true, "Delete");
        Assert.IsType<ForbidResult>(await f.Controller(3).Delete(r.GetProperty("id").GetInt32()));
    }

    [Fact]
    public async Task Parent_with_children_cannot_be_deleted()
    {
        using var f = new Fixture();
        var c = f.Controller(1, "Admin");
        var r = Json(await c.Create(new() { Title = "والد", DueAt = ScheduleTests.Fa(1450, 3, 1), AssigneeUserIds = new() { 1 } }));
        var id = r.GetProperty("id").GetInt32();
        Json(await c.Create(new() { Title = "فرزند", DueAt = ScheduleTests.Fa(1450, 2, 1), AssigneeUserIds = new() { 1 }, ParentOrderId = id }));
        Assert.IsType<BadRequestObjectResult>(await c.Delete(id));
        Assert.Equal(2, await f.Db.WorkOrders.CountAsync());
    }

    [Fact]
    public async Task Editing_a_single_order_to_repeating_materializes_once_then_edits_only_that_occurrence()
    {
        using var f = new Fixture();
        var c = f.Controller(1, "Admin");
        var dto = new WorkOrdersController.CreateDto { Title = "تست", DueAt = ScheduleTests.Fa(1450, 7, 28), AssigneeUserIds = new() { 2 } };
        var id = Json(await c.Create(dto)).GetProperty("id").GetInt32();
        dto.Recurrence = 1;
        Assert.Equal(3, Json(await c.Update(id, dto)).GetProperty("occurrenceCount").GetInt32());
        dto.Title = "ویرایش همین نوبت";
        Json(await c.Update(id, dto));
        Assert.Equal(3, await f.Db.WorkOrders.CountAsync());
        Assert.Equal(1, await f.Db.WorkOrders.CountAsync(w => w.Title == dto.Title));
        dto.Recurrence = 2;
        Assert.IsType<BadRequestObjectResult>(await c.Update(id, dto));
    }

    [Fact]
    public async Task Invalid_recipient_does_not_leave_partial_order_or_series()
    {
        using var f = new Fixture();
        Assert.IsType<BadRequestObjectResult>(await f.Controller(1, "Admin").Create(new()
        { Title = "تست", DueAt = ScheduleTests.Fa(1450, 1, 1), Recurrence = 2, AssigneeUserIds = new() { 2, 999 } }));
        Assert.Empty(await f.Db.WorkOrders.ToListAsync());
    }

    [Fact]
    public async Task Legacy_open_leaf_upgrades_once_and_does_not_replay_past_dates()
    {
        using var f = new Fixture();
        var first = new WorkOrder { Title = "قدیمی", Number = "WO/1450/1", OwnerUserId = 1, DueAt = ScheduleTests.Fa(1450, 7, 27), Recurrence = 1 };
        f.Db.WorkOrders.Add(first); await f.Db.SaveChangesAsync();
        f.Db.WorkOrderAssignees.Add(new() { OrderId = first.Id, UserId = 2, Name = "گیرنده" }); await f.Db.SaveChangesAsync();
        var service = new WorkOrderSchedulingService(f.Db);
        var now = ScheduleTests.Fa(1450, 7, 28);
        await service.UpgradeLegacyAsync(now);
        await service.UpgradeLegacyAsync(now);
        Assert.Equal(3, await f.Db.WorkOrders.CountAsync()); // original + 29th + 30th
        Assert.Equal(3, await f.Db.WorkOrderAssignees.CountAsync());
    }

    [Fact]
    public async Task Legacy_chain_does_not_schedule_again_from_an_ancestor()
    {
        using var f = new Fixture();
        var old = new WorkOrder { DueAt = ScheduleTests.Fa(1450, 7, 27), Recurrence = 1, Status = "Closed" };
        f.Db.WorkOrders.Add(old); await f.Db.SaveChangesAsync();
        f.Db.WorkOrders.Add(new() { DueAt = ScheduleTests.Fa(1450, 7, 28), Recurrence = 1, RecurrenceParentId = old.Id });
        await f.Db.SaveChangesAsync();
        await new WorkOrderSchedulingService(f.Db).UpgradeLegacyAsync(ScheduleTests.Fa(1450, 7, 1));
        Assert.Equal(4, await f.Db.WorkOrders.CountAsync());
    }

    [Fact]
    public async Task Permission_seed_is_idempotent_and_does_not_grant_delete_to_operator()
    {
        using var f = new Fixture();
        await RbacSeeder.SeedAsync(f.Db);
        await RbacSeeder.SeedAsync(f.Db);
        var perm = Assert.Single(await f.Db.Permissions.Where(p => p.Module == "WorkOrders" && p.Action == "Delete").ToListAsync());
        var admin = await f.Db.Roles.SingleAsync(r => r.Name == "Admin");
        var op = await f.Db.Roles.SingleAsync(r => r.Name == "Operator");
        Assert.True(await f.Db.RolePermissions.AnyAsync(r => r.RoleId == admin.Id && r.PermissionId == perm.Id));
        Assert.False(await f.Db.RolePermissions.AnyAsync(r => r.RoleId == op.Id && r.PermissionId == perm.Id));
    }

    [Fact]
    public async Task Lists_return_parent_and_child_numbers_and_allow_assignee_deep_links()
    {
        using var f = new Fixture();
        var owner = f.Controller(1, "Admin");
        var parent = Json(await owner.Create(new() { Title = "اصلی", DueAt = ScheduleTests.Fa(1450, 3, 1), AssigneeUserIds = new() { 2 } }));
        var parentId = parent.GetProperty("id").GetInt32();
        var child = Json(await owner.Create(new() { Title = "مرتبط", DueAt = ScheduleTests.Fa(1450, 2, 1), AssigneeUserIds = new() { 2 }, ParentOrderId = parentId }));
        var childId = child.GetProperty("id").GetInt32();
        var mine = Json(await owner.Mine(new()));
        var parentRow = mine.EnumerateArray().Single(x => x.GetProperty("Id").GetInt32() == parentId);
        Assert.Equal(child.GetProperty("number").GetString(), parentRow.GetProperty("Children")[0].GetProperty("Number").GetString());
        var childRow = mine.EnumerateArray().Single(x => x.GetProperty("Id").GetInt32() == childId);
        Assert.Equal(parent.GetProperty("number").GetString(), childRow.GetProperty("ParentNumber").GetString());
        // An actual participant can follow a link without an additional global View permission.
        Assert.IsType<OkObjectResult>(await f.Controller(2).Detail(childId));
        Assert.IsType<ForbidResult>(await f.Controller(3).Detail(childId));
        Assert.IsType<NoContentResult>(await owner.Delete(childId));
        var updated = Json(await owner.Detail(parentId));
        Assert.Equal(0, updated.GetProperty("Children").GetArrayLength());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly TestEnvironment env = new();
        public AppDbContext Db { get; }
        public FakeNotify Notify { get; } = new();
        public Fixture()
        {
            connection.Open();
            Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            Db.Database.EnsureCreated();
            for (var i = 1; i <= 3; i++) Db.Users.Add(new User { Id = i, Username = "u" + i, FirstName = "کاربر", LastName = i.ToString() });
            Db.SaveChanges();
        }
        public WorkOrdersController Controller(int id, string role = "Employee") => new(Db, Notify, new FileStore(env))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            { new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Name, "u" + id), new Claim(ClaimTypes.Role, role) }, "test")) } }
        };
        public async Task Grant(int userId, bool active, params string[] actions)
        {
            var role = new Role { Name = Guid.NewGuid().ToString(), IsActive = active };
            Db.Roles.Add(role); await Db.SaveChangesAsync();
            Db.UserRoles.Add(new() { UserId = userId, RoleId = role.Id });
            foreach (var action in actions)
            {
                var perm = await Db.Permissions.FirstOrDefaultAsync(p => p.Module == "WorkOrders" && p.Action == action);
                if (perm == null) { perm = new Permission { Module = "WorkOrders", Action = action }; Db.Permissions.Add(perm); await Db.SaveChangesAsync(); }
                Db.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = perm.Id });
            }
            await Db.SaveChangesAsync();
        }
        public void Dispose() { Db.Dispose(); connection.Dispose(); if (Directory.Exists(env.ContentRootPath)) Directory.Delete(env.ContentRootPath, true); }
    }
    private sealed class FakeNotify : INotifyService
    {
        public int Sends;
        public Task SendAsync(int userId, string title, string? body, string fromName, string formName, string? link) { Sends++; return Task.CompletedTask; }
        public Task SendManyAsync(IEnumerable<int> ids, string t, string? b, string f, string n, string? l) => Task.CompletedTask;
        public Task SendToRoleAsync(string r, string t, string? b, string f, string n, string? l) => Task.CompletedTask;
        public Task BroadcastChangedAsync(string scope) => Task.CompletedTask;
    }
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "workorders-" + Guid.NewGuid());
        public string WebRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
