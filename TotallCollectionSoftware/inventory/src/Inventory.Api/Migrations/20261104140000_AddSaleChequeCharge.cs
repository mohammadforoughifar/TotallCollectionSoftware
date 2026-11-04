using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104140000_AddSaleChequeCharge")]
public partial class AddSaleChequeCharge : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(SaleChequeChargeSchemaV1.SqlServer);
    // Do not discard financial rows during a rollback.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
