using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261105050000_AddSaleChequeCancellations")]
public partial class AddSaleChequeCancellations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(SaleChequeCancellationSchemaV1.SqlServer);
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
