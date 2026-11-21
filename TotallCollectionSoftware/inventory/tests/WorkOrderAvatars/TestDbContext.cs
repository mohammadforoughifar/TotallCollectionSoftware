using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// This small context tests the real entity and schema helper against SQLite. It is
// deliberately not a replacement for integration tests of the full production context.
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<WorkOrderLog> WorkOrderLogs => Set<WorkOrderLog>();
}

// Unused navigation target in WorkOrder.cs; only WorkOrderLog is mapped in this test.
public sealed class WorkProject { }
