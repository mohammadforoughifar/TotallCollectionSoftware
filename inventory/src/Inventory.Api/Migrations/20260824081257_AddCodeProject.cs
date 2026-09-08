using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodeProject",
                table: "ReportWorks",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CodeProject",
                table: "ProjectEntryExits",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEntryExits_CodeProject",
                table: "ProjectEntryExits",
                column: "CodeProject");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectEntryExits_CodeProject",
                table: "ProjectEntryExits");

            migrationBuilder.DropColumn(
                name: "CodeProject",
                table: "ReportWorks");

            migrationBuilder.DropColumn(
                name: "CodeProject",
                table: "ProjectEntryExits");
        }
    }
}
