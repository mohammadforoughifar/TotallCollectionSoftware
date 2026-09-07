using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOutgoingLetters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutgoingPishnevisLetters",
                columns: table => new
                {
                    PishnevisId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceiverOrganization = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ReceiverName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ReceiverTitle = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SematId = table.Column<int>(type: "int", nullable: true),
                    IsNeshan = table.Column<bool>(type: "bit", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingPishnevisLetters", x => x.PishnevisId);
                    table.ForeignKey(
                        name: "FK_OutgoingPishnevisLetters_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutgoingLetters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LetterNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Number = table.Column<int>(type: "int", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    CreatorSematId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateSabt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Mahramanegi = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Foriat = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReceiverOrganization = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReceiverName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ReceiverTitle = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ReceiverAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CopyTo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExternalRefNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingLetters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutgoingLetters_LetterSources_Id",
                        column: x => x.Id,
                        principalTable: "LetterSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutgoingLetters_Users_CreatorUserId",
                        column: x => x.CreatorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetters_CreatorUserId",
                table: "OutgoingLetters",
                column: "CreatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetters_DateSabt",
                table: "OutgoingLetters",
                column: "DateSabt");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetters_Number",
                table: "OutgoingLetters",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetters_ReceiverOrganization",
                table: "OutgoingLetters",
                column: "ReceiverOrganization");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingPishnevisLetters_UserId",
                table: "OutgoingPishnevisLetters",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutgoingLetters");

            migrationBuilder.DropTable(
                name: "OutgoingPishnevisLetters");
        }
    }
}
