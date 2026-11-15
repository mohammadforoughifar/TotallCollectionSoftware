using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104130000_AddOrderCashTracking")]
public partial class AddOrderCashTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(OrderTrackingSchemaV1.SqlServer);
    // Financial references are retained on rollback.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
