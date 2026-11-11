using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261105010000_AddSaleChequeReplacements")]
public partial class AddSaleChequeReplacements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(SaleChequeReplacementSchemaV1.SqlServer);
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
