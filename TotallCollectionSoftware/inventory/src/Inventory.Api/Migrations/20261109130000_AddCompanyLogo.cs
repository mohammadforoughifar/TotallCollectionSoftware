using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Inventory.Api.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261109130000_AddCompanyLogo")]
public class AddCompanyLogo : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.AddColumn<string>(name: "LogoDataUrl", table: "SystemCompanies", type: "TEXT", nullable: true);
        else migrationBuilder.Sql(CompanyLogoSchemaV1.SqlServer);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Company logos are retained.");
}
