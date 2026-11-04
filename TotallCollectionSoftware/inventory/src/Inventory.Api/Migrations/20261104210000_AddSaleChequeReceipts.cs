using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104210000_AddSaleChequeReceipts")]
public partial class AddSaleChequeReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(SaleChequeReceiptSchemaV1.SqlServer);
    protected override void Down(MigrationBuilder migrationBuilder) { } // retain financial history
}
