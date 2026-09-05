using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOvertimeModesToWorkCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "OvertimeEnd",
                table: "WorkCalendarDays",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OvertimeMode",
                table: "WorkCalendarDays",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "OvertimeStart",
                table: "WorkCalendarDays",
                type: "time",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OvertimeEnd",
                table: "WorkCalendarDays");

            migrationBuilder.DropColumn(
                name: "OvertimeMode",
                table: "WorkCalendarDays");

            migrationBuilder.DropColumn(
                name: "OvertimeStart",
                table: "WorkCalendarDays");
        }
    }
}
