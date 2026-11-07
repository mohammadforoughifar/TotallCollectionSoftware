using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104180000_AddSaleChequeAccountSnapshots")]
public partial class AddSaleChequeAccountSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(SaleChequeAccountsSnapshotSchemaV1.SqlServer);
    // Account snapshots are part of the financial history and must not be discarded.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
