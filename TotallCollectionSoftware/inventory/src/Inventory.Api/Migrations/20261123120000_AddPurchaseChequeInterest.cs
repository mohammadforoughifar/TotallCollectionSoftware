using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Inventory.Api.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261123120000_AddPurchaseChequeInterest")]
public class AddPurchaseChequeInterest : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        migrationBuilder.ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? PurchaseChequeInterestSchemaV1.Sqlite : PurchaseChequeInterestSchemaV1.SqlServer);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Purchase cheque interest is part of the purchase history and is retained.");
}
