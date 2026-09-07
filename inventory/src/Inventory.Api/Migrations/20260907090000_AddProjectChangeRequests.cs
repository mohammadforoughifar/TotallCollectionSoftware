using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectChangeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ManagerNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestedById = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ManagerActionById = table.Column<int>(type: "int", nullable: true),
                    ManagerActionAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectChangeRequests_ProjectEntryExits_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ProjectEntryExits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectChangeRequests_Users_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectChangeRequests_ProjectId",
                table: "ProjectChangeRequests",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectChangeRequests_RequestedById",
                table: "ProjectChangeRequests",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectChangeRequests_Status",
                table: "ProjectChangeRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectChangeRequests");
        }
    }
}
