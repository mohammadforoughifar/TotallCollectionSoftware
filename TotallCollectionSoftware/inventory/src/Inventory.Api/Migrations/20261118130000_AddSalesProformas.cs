using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118130000_AddSalesProformas")]
public class AddSalesProformas : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        migrationBuilder.ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? SalesProformaSchemaV1.Sqlite : SalesProformaSchemaV1.SqlServer);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Sales proformas are retained.");
}
