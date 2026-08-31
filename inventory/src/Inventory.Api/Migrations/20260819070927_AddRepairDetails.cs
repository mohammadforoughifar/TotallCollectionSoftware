using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRepairDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Cost",
                table: "OfficeMachineRepairs",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "GoneDate",
                table: "OfficeMachineRepairs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PerformedWork",
                table: "OfficeMachineRepairs",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnDate",
                table: "OfficeMachineRepairs",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cost",
                table: "OfficeMachineRepairs");

            migrationBuilder.DropColumn(
                name: "GoneDate",
                table: "OfficeMachineRepairs");

            migrationBuilder.DropColumn(
                name: "PerformedWork",
                table: "OfficeMachineRepairs");

            migrationBuilder.DropColumn(
                name: "ReturnDate",
                table: "OfficeMachineRepairs");
        }
    }
}
