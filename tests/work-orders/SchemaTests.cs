using Inventory.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventory.WorkOrders.Tests;

public class SchemaTests
{
    [Fact]
    public async Task Existing_sqlite_schema_upgrades_idempotently_without_losing_rows()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE WorkOrders (Id INTEGER PRIMARY KEY, Title TEXT); INSERT INTO WorkOrders VALUES (1, 'legacy');");
        await WorkOrderSchemaV2.EnsureAsync(db);
        await WorkOrderSchemaV2.EnsureAsync(db);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('WorkOrders') WHERE name IN ('RecurrenceSeriesId','RecurrenceScheduledAt','DeletedAt','DeletedByUserId')";
        Assert.Equal(4L, await cmd.ExecuteScalarAsync());
        cmd.CommandText = "SELECT Title FROM WorkOrders WHERE Id=1";
        Assert.Equal("legacy", await cmd.ExecuteScalarAsync());
        await db.Database.ExecuteSqlRawAsync("INSERT INTO WorkOrders (Id, RecurrenceSeriesId, RecurrenceScheduledAt) VALUES (2, 'series', '2050-01-01');");
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("INSERT INTO WorkOrders (Id, RecurrenceSeriesId, RecurrenceScheduledAt) VALUES (3, 'series', '2050-01-01');"));
    }
}
