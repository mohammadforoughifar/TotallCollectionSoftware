using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261127130000_EnsureLetterCreatedAt")]
public class EnsureLetterCreatedAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
            throw new NotSupportedException("SQLite uses startup schema repair instead of SQL Server migrations.");
        foreach (var batch in LetterCreatedAtSchemaV1.SqlServerBatches()) migrationBuilder.Sql(batch);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Dropping a letter's recorded creation time would destroy history.");
}
