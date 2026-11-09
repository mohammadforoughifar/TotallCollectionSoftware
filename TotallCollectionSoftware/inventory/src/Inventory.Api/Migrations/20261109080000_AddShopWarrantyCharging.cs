using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261109080000_AddShopWarrantyCharging")]
public partial class AddShopWarrantyCharging : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!migrationBuilder.ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SQLite uses startup schema repair instead of SQL Server migrations.");
        migrationBuilder.Sql(ShopWarrantyChargingSchemaV1.SqlServer);
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Warranty financial history cannot be dropped safely.");
}
