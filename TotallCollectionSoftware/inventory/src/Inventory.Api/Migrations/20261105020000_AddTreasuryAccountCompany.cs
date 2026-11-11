using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261105020000_AddTreasuryAccountCompany")]
public partial class AddTreasuryAccountCompany : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(TreasuryAccountCompanySchemaV1.SqlServer);
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
