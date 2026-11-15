using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104120000_AddOrderVat")]
public partial class AddOrderVat : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(OrderVatSchemaV1.SqlServer);
    // Intentionally preserve financial snapshots if a migration is rolled back.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
