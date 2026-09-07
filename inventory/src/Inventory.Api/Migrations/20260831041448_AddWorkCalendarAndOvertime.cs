using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkCalendarAndOvertime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsUnauthorized",
                table: "AttendanceSegments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OvertimeMinutes",
                table: "AttendanceSegments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OvertimeMinutes",
                table: "AttendanceRecords",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnauthorizedMinutes",
                table: "AttendanceRecords",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "WorkCalendarDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsWorkday = table.Column<bool>(type: "bit", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    EndTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    GraceMinutes = table.Column<int>(type: "int", nullable: false),
                    OvertimeHours = table.Column<double>(type: "float", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCalendarDays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkCalendarDays_Date",
                table: "WorkCalendarDays",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkCalendarDays");

            migrationBuilder.DropColumn(
                name: "IsUnauthorized",
                table: "AttendanceSegments");

            migrationBuilder.DropColumn(
                name: "OvertimeMinutes",
                table: "AttendanceSegments");

            migrationBuilder.DropColumn(
                name: "OvertimeMinutes",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "UnauthorizedMinutes",
                table: "AttendanceRecords");
        }
    }
}
