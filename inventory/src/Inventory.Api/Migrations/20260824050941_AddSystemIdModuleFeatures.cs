using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemIdModuleFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SmartStatus",
                table: "SystemDisks",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SmartUpdatedAt",
                table: "SystemDisks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SystemHandovers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    FromUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ToUserId = table.Column<int>(type: "int", nullable: true),
                    ToUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChecklistJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignatureDataUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemHandovers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemInfoUserHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StaffNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FromAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ToAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemInfoUserHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemRemoteCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemRemoteCommands", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemHandovers_SystemInfoId",
                table: "SystemHandovers",
                column: "SystemInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemInfoUserHistories_SystemInfoId",
                table: "SystemInfoUserHistories",
                column: "SystemInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemRemoteCommands_Status",
                table: "SystemRemoteCommands",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SystemRemoteCommands_SystemInfoId",
                table: "SystemRemoteCommands",
                column: "SystemInfoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemHandovers");

            migrationBuilder.DropTable(
                name: "SystemInfoUserHistories");

            migrationBuilder.DropTable(
                name: "SystemRemoteCommands");

            migrationBuilder.DropColumn(
                name: "SmartStatus",
                table: "SystemDisks");

            migrationBuilder.DropColumn(
                name: "SmartUpdatedAt",
                table: "SystemDisks");
        }
    }
}
