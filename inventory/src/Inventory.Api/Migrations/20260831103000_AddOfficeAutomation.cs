using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficeAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.CreateTable(
                name: "Amalgars",
                columns: table => new
                {
                    AmalgarId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TaeedEmza = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Amalgars", x => x.AmalgarId);
                });

            migrationBuilder.CreateTable(
                name: "LetterBayeganis",
                columns: table => new
                {
                    BayeganiId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ErjaId = table.Column<int>(type: "int", nullable: true),
                    ParentId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SematId = table.Column<int>(type: "int", nullable: true),
                    TypeBayegani = table.Column<int>(type: "int", nullable: false),
                    IsFolder = table.Column<bool>(type: "bit", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterBayeganis", x => x.BayeganiId);
                });

            migrationBuilder.CreateTable(
                name: "LetterSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PishnevisLetters",
                columns: table => new
                {
                    PishnevisId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SematId = table.Column<int>(type: "int", nullable: true),
                    IsNeshan = table.Column<bool>(type: "bit", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PishnevisLetters", x => x.PishnevisId);
                    table.ForeignKey(
                        name: "FK_PishnevisLetters_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Erjas",
                columns: table => new
                {
                    ErjaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    SenderUserId = table.Column<int>(type: "int", nullable: false),
                    ReciverUserId = table.Column<int>(type: "int", nullable: false),
                    SenderSematId = table.Column<int>(type: "int", nullable: true),
                    ReciverSematId = table.Column<int>(type: "int", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TypeTaeed = table.Column<int>(type: "int", nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    IsBayegani = table.Column<bool>(type: "bit", nullable: true),
                    MohlatPasokh = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MatnErja = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AmalgarId = table.Column<int>(type: "int", nullable: false),
                    IsNeshan = table.Column<bool>(type: "bit", nullable: false),
                    ShowForAll = table.Column<bool>(type: "bit", nullable: false),
                    ShowMassage = table.Column<bool>(type: "bit", nullable: false),
                    DateRead = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateEmza = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateAnswer = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsReadAnswer = table.Column<bool>(type: "bit", nullable: false),
                    ShowMassageAnswer = table.Column<bool>(type: "bit", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    ParentErjaId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Erjas", x => x.ErjaId);
                    table.ForeignKey(
                        name: "FK_Erjas_Amalgars_AmalgarId",
                        column: x => x.AmalgarId,
                        principalTable: "Amalgars",
                        principalColumn: "AmalgarId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Erjas_LetterSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "LetterSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Erjas_Users_ReciverUserId",
                        column: x => x.ReciverUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Erjas_Users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InnerLetters",
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
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InnerLetters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InnerLetters_LetterSources_Id",
                        column: x => x.Id,
                        principalTable: "LetterSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InnerLetters_Users_CreatorUserId",
                        column: x => x.CreatorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RelatedLetters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Related = table.Column<int>(type: "int", nullable: false),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    RelateLetterId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SematId = table.Column<int>(type: "int", nullable: true),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelatedLetters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelatedLetters_LetterSources_LetterId",
                        column: x => x.LetterId,
                        principalTable: "LetterSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RelatedLetters_LetterSources_RelateLetterId",
                        column: x => x.RelateLetterId,
                        principalTable: "LetterSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Erjas_AmalgarId",
                table: "Erjas",
                column: "AmalgarId");

            migrationBuilder.CreateIndex(
                name: "IX_Erjas_ReciverUserId",
                table: "Erjas",
                column: "ReciverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Erjas_ReciverUserId_IsRead",
                table: "Erjas",
                columns: new[] { "ReciverUserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_Erjas_SenderUserId",
                table: "Erjas",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Erjas_SourceId",
                table: "Erjas",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_InnerLetters_CreatorUserId",
                table: "InnerLetters",
                column: "CreatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InnerLetters_DateSabt",
                table: "InnerLetters",
                column: "DateSabt");

            migrationBuilder.CreateIndex(
                name: "IX_InnerLetters_Number",
                table: "InnerLetters",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_LetterBayeganis_UserId",
                table: "LetterBayeganis",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PishnevisLetters_UserId",
                table: "PishnevisLetters",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedLetters_LetterId",
                table: "RelatedLetters",
                column: "LetterId");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedLetters_RelateLetterId",
                table: "RelatedLetters",
                column: "RelateLetterId");



            migrationBuilder.CreateTable(
                name: "LetterGroups",
                columns: table => new
                {
                    GroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NameGroup = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Condition = table.Column<bool>(type: "bit", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    CreatorSematId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterGroups", x => x.GroupId);
                    table.ForeignKey(
                        name: "FK_LetterGroups_Users_CreatorUserId",
                        column: x => x.CreatorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LetterGroupMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SematId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LetterGroupMembers_LetterGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "LetterGroups",
                        principalColumn: "GroupId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LetterGroupMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LetterGroupMembers_GroupId_UserId",
                table: "LetterGroupMembers",
                columns: new[] { "GroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LetterGroupMembers_UserId",
                table: "LetterGroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LetterGroups_CreatorUserId",
                table: "LetterGroups",
                column: "CreatorUserId");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.DropTable(
                name: "Erjas");

            migrationBuilder.DropTable(
                name: "InnerLetters");

            migrationBuilder.DropTable(
                name: "LetterBayeganis");

            migrationBuilder.DropTable(
                name: "PishnevisLetters");

            migrationBuilder.DropTable(
                name: "RelatedLetters");

            migrationBuilder.DropTable(
                name: "Amalgars");

            migrationBuilder.DropTable(
                name: "LetterSources");



            migrationBuilder.DropTable(
                name: "LetterGroupMembers");

            migrationBuilder.DropTable(
                name: "LetterGroups");

        }
    }
}
