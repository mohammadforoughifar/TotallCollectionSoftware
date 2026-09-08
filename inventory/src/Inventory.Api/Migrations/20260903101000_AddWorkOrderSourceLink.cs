using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderSourceLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceModule",
                table: "WorkOrders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceId",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_SourceModule_SourceId",
                table: "WorkOrders",
                columns: new[] { "SourceModule", "SourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_SourceModule_SourceId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SourceModule",
                table: "WorkOrders");
        }
    }
}
