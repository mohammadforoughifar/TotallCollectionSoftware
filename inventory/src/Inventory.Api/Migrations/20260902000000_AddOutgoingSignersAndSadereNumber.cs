using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    public partial class AddOutgoingSignersAndSadereNumber : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SadereNumber",
                table: "OutgoingLetters",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateSadere",
                table: "OutgoingLetters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetters_SadereNumber",
                table: "OutgoingLetters",
                column: "SadereNumber");

            migrationBuilder.CreateTable(
                name: "OutgoingLetterSigners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SematId = table.Column<int>(type: "int", nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsSigned = table.Column<bool>(type: "bit", nullable: false),
                    DateSigned = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SignNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingLetterSigners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutgoingLetterSigners_LetterSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "LetterSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutgoingLetterSigners_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetterSigners_SourceId",
                table: "OutgoingLetterSigners",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetterSigners_SourceId_UserId",
                table: "OutgoingLetterSigners",
                columns: new[] { "SourceId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetterSigners_UserId",
                table: "OutgoingLetterSigners",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetterSigners_UserId_IsSigned",
                table: "OutgoingLetterSigners",
                columns: new[] { "UserId", "IsSigned" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OutgoingLetterSigners");

            migrationBuilder.DropIndex(name: "IX_OutgoingLetters_SadereNumber", table: "OutgoingLetters");

            migrationBuilder.DropColumn(name: "SadereNumber", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "DateSadere", table: "OutgoingLetters");
        }
    }
}
