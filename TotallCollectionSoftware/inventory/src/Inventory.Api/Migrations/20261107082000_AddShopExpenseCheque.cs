using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Inventory.Api.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261107082000_AddShopExpenseCheque")]
public partial class AddShopExpenseCheque : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!migrationBuilder.ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SQLite uses startup schema repair instead of SQL Server migrations.");
        migrationBuilder.Sql(ShopExpenseChequeSchemaV1.SqlServer);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Expense cheque history cannot be dropped safely.");
}
