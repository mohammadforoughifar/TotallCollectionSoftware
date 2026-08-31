using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddItRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItRequestAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    ExpertUserId = table.Column<int>(type: "int", nullable: false),
                    ExpertName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ManagerInstruction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ExpertReport = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ReportSubmitted = table.Column<bool>(type: "bit", nullable: false),
                    IncludeInFinal = table.Column<bool>(type: "bit", nullable: false),
                    RepliedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItRequestAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItRequestAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UploaderRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UploaderName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    UploaderUserId = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItRequestAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequesterName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    RequesterUserId = table.Column<int>(type: "int", nullable: false),
                    SystemInfoId = table.Column<int>(type: "int", nullable: true),
                    SystemLabel = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ManagerNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FinalResponse = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItRequests", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItRequestAssignments");

            migrationBuilder.DropTable(
                name: "ItRequestAttachments");

            migrationBuilder.DropTable(
                name: "ItRequests");
        }
    }
}
