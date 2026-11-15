using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261105100000_AddShopReferrerCommissionRates")]
public partial class AddShopReferrerCommissionRates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!migrationBuilder.ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SQLite uses startup schema repair instead of SQL Server migrations.");
        migrationBuilder.Sql(ShopReferrerCommissionSchemaV1.SqlServer);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Historical commission rates cannot be dropped safely.");
}
