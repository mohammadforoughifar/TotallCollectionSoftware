using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftGroupsAndAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShiftGroupId",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShiftGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    GraceMinutes = table.Column<int>(type: "int", nullable: false),
                    IncludeFriday = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ShiftGroupId = table.Column<int>(type: "int", nullable: true),
                    EnterAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExitAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EnterIp = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExitIp = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EnterStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LateMinutes = table.Column<int>(type: "int", nullable: false),
                    EarlyLeaveMinutes = table.Column<int>(type: "int", nullable: false),
                    WorkMinutes = table.Column<int>(type: "int", nullable: false),
                    DeficitMinutes = table.Column<int>(type: "int", nullable: false),
                    HasApprovedLeave = table.Column<bool>(type: "bit", nullable: false),
                    FinalStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_ShiftGroups_ShiftGroupId",
                        column: x => x.ShiftGroupId,
                        principalTable: "ShiftGroups",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_ShiftGroupId",
                table: "Users",
                column: "ShiftGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_ShiftGroupId",
                table: "AttendanceRecords",
                column: "ShiftGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_ShiftGroups_ShiftGroupId",
                table: "Users",
                column: "ShiftGroupId",
                principalTable: "ShiftGroups",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_ShiftGroups_ShiftGroupId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "AttendanceRecords");

            migrationBuilder.DropTable(
                name: "ShiftGroups");

            migrationBuilder.DropIndex(
                name: "IX_Users_ShiftGroupId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ShiftGroupId",
                table: "Users");
        }
    }
}
