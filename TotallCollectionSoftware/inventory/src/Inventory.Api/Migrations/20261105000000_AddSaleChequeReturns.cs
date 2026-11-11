using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261105000000_AddSaleChequeReturns")]
public partial class AddSaleChequeReturns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(SaleChequeReturnSchemaV1.SqlServer);
    protected override void Down(MigrationBuilder migrationBuilder) { } // preserve financial history
}
