using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LicenseSigner.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InterestedCompaniesJson",
                table: "Requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompaniesJson",
                table: "Drafts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InterestedCompaniesJson",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "CompaniesJson",
                table: "Drafts");
        }
    }
}
