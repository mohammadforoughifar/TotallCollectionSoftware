using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectFlowCartableAndAttachFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpertActionAt",
                table: "ProjectEntryExits",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpertActionById",
                table: "ProjectEntryExits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpertNote",
                table: "ProjectEntryExits",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FlowStatus",
                table: "ProjectEntryExits",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManagerActionAt",
                table: "ProjectEntryExits",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManagerActionById",
                table: "ProjectEntryExits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagerNote",
                table: "ProjectEntryExits",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // پروژه‌های موجود از قبل وارد چرخهٔ کارتابل بوده‌اند — همه را «نهایی» (۳) می‌کنیم تا کارتابل‌های جدید فقط موارد تازه را نشان دهند
            migrationBuilder.Sql("UPDATE [ProjectEntryExits] SET [FlowStatus] = 3 WHERE [FlowStatus] = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpertActionAt",
                table: "ProjectEntryExits");

            migrationBuilder.DropColumn(
                name: "ExpertActionById",
                table: "ProjectEntryExits");

            migrationBuilder.DropColumn(
                name: "ExpertNote",
                table: "ProjectEntryExits");

            migrationBuilder.DropColumn(
                name: "FlowStatus",
                table: "ProjectEntryExits");

            migrationBuilder.DropColumn(
                name: "ManagerActionAt",
                table: "ProjectEntryExits");

            migrationBuilder.DropColumn(
                name: "ManagerActionById",
                table: "ProjectEntryExits");

            migrationBuilder.DropColumn(
                name: "ManagerNote",
                table: "ProjectEntryExits");
        }
    }
}
