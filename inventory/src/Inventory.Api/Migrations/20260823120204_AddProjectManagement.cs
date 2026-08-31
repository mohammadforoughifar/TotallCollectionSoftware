using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KarFarmas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ModirAmelPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Telephone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Fax = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ShomareSabt = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KarFarmas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TypeFactors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TypeFactors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectEntryExits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnProjectId = table.Column<int>(type: "int", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProjectName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    GhabzExit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FactorNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    KarshenasiAvalie = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ProjectReceiver = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    KarFarmaId = table.Column<int>(type: "int", nullable: false),
                    FactorTypeId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ExitDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EntryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FileDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TemporaryExitDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProjectRegistrationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CustomerRequiredDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsFolder = table.Column<bool>(type: "bit", nullable: true),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    TotalSpentTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectEntryExits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectEntryExits_KarFarmas_KarFarmaId",
                        column: x => x.KarFarmaId,
                        principalTable: "KarFarmas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectEntryExits_TypeFactors_FactorTypeId",
                        column: x => x.FactorTypeId,
                        principalTable: "TypeFactors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectEntryExits_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectAttaches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OriginalFileNameEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    DateSabt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectAttaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectAttaches_ProjectEntryExits_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ProjectEntryExits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectAttaches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportWorks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    WorkDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    BreakfastTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    LunchTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    SpentTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportWorks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportWorks_ProjectEntryExits_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ProjectEntryExits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReportWorks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KarFarmas_Name",
                table: "KarFarmas",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAttaches_ProjectId",
                table: "ProjectAttaches",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAttaches_UserId",
                table: "ProjectAttaches",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEntryExits_FactorTypeId",
                table: "ProjectEntryExits",
                column: "FactorTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEntryExits_KarFarmaId",
                table: "ProjectEntryExits",
                column: "KarFarmaId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEntryExits_SerialNumber",
                table: "ProjectEntryExits",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEntryExits_UserId",
                table: "ProjectEntryExits",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportWorks_ProjectId",
                table: "ReportWorks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportWorks_UserId",
                table: "ReportWorks",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectAttaches");

            migrationBuilder.DropTable(
                name: "ReportWorks");

            migrationBuilder.DropTable(
                name: "ProjectEntryExits");

            migrationBuilder.DropTable(
                name: "KarFarmas");

            migrationBuilder.DropTable(
                name: "TypeFactors");
        }
    }
}
