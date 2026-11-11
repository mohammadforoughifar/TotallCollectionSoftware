using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261105030000_AddSaleChequeCreditSettlements")]
public partial class AddSaleChequeCreditSettlements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(SaleChequeCreditSettlementSchemaV1.SqlServer);
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
