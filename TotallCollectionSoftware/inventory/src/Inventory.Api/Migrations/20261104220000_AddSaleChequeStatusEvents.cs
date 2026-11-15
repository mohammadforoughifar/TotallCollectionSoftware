using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104220000_AddSaleChequeStatusEvents")]
public partial class AddSaleChequeStatusEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(SaleChequeStatusEventSchemaV1.SqlServer);
    protected override void Down(MigrationBuilder migrationBuilder) { } // financial history is retained
}
