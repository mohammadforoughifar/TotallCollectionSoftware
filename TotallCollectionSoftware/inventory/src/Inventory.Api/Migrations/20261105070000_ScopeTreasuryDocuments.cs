using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261105070000_ScopeTreasuryDocuments")]
public partial class ScopeTreasuryDocuments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!migrationBuilder.ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SQLite uses startup schema repair instead of SQL Server migrations.");
        migrationBuilder.Sql(TreasuryCompanyScopeSchemaV1.SqlServerColumns);
    }

    // Removing ownership would expose documents across companies.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Company ownership of financial history cannot be removed safely.");
}
