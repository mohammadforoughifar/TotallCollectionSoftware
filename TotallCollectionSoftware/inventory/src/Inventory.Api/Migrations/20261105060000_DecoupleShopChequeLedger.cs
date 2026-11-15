using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261105060000_DecoupleShopChequeLedger")]
public partial class DecoupleShopChequeLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!migrationBuilder.ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SQLite uses the transactional startup schema repair, not SQL Server migrations.");
        migrationBuilder.Sql(SaleChequeShopLedgerSchemaV1.SqlServer);
    }
    // Restoring NOT NULL would lose or invent ERP references for independent shop records.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Downgrading this financial history would require inventing ERP vouchers.");
}
