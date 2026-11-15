using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104170000_AddSaleChequeCalculatorSnapshot")]
public partial class AddSaleChequeCalculatorSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(SaleChequeCalculatorSchemaV1.SqlServer);
    // Preserve the financial calculation history on rollback.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
