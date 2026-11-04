using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104200000_AddSaleChequeReschedules")]
public partial class AddSaleChequeReschedules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(SaleChequeRescheduleSchemaV1.SqlServer);
    protected override void Down(MigrationBuilder migrationBuilder) { } // financial history is retained
}
