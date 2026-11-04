using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104160000_AddChequeOriginalDueDate")]
public partial class AddChequeOriginalDueDate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(ChequeOriginalDueDateSchemaV1.SqlServer);
    // The snapshot can be financial history; do not silently discard it on rollback.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
