using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Inventory.Api.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261109120000_AddShopPrintSettings")]
public class AddShopPrintSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        migrationBuilder.ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? ShopPrintSettingsSchemaV1.Sqlite : ShopPrintSettingsSchemaV1.SqlServer);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Print settings are retained to avoid deleting saved bank data.");
}
