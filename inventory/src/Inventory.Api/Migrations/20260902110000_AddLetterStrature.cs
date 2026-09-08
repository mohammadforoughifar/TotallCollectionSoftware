using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLetterStrature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LetterStratures",
                columns: table => new
                {
                    StratureId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TypeForm = table.Column<int>(type: "int", nullable: false),
                    TypeStrature = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterStratures", x => x.StratureId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LetterStratures_TypeForm",
                table: "LetterStratures",
                column: "TypeForm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LetterStratures");
        }
    }
}
