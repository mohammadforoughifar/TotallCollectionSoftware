using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Inventory.Api.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261118120000_AddShopVisitorDiscounts")]
public class AddShopVisitorDiscounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        migrationBuilder.ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? ShopVisitorDiscountSchemaV1.Sqlite : ShopVisitorDiscountSchemaV1.SqlServer);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Visitor discount settings are retained.");
}
