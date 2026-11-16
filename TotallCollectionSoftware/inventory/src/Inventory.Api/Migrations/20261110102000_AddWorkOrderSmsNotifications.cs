using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261110102000_AddWorkOrderSmsNotifications")]
public partial class AddWorkOrderSmsNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!migrationBuilder.ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SQLite uses startup schema repair instead of SQL Server migrations.");
        migrationBuilder.Sql(WorkOrderSmsSchemaV1.SqlServer);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Work-order SMS and request history cannot be dropped safely.");
}
