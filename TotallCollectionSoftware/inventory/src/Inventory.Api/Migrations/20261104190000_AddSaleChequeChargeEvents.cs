using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104190000_AddSaleChequeChargeEvents")]
public partial class AddSaleChequeChargeEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(SaleChequeChargeEventsSchemaV1.SqlServer);
    protected override void Down(MigrationBuilder migrationBuilder) { } // preserve financial history
}
