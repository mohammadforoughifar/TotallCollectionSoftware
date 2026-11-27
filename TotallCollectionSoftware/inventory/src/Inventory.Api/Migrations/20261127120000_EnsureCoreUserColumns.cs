using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261127120000_EnsureCoreUserColumns")]
public class EnsureCoreUserColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
            throw new NotSupportedException("SQLite uses the startup schema repair, not SQL Server migrations.");
        migrationBuilder.Sql(CoreUserColumnsSchemaV1.SqlServer);
    }

    // Both fields can already contain data from an older installation. Do not drop them on rollback.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Existing user organization assignments and compatibility flags must be retained.");
}
