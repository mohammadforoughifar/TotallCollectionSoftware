using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <summary>
    /// تنظیمات ساختار شماره نامه (اندیکاتور) + کد واحد سازمانی
    /// (کد واحد در یکی از اجزای قابل انتخابِ ساختار شماره استفاده می‌شود)
    /// </summary>
    public partial class AddLetterNumberSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "SystemDepartments",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LetterNumberSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    PartsOrder = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Separator = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Suffix = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Text1 = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Text2 = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    YearDigits = table.Column<int>(type: "int", nullable: false),
                    SerialDigits = table.Column<int>(type: "int", nullable: false),
                    StartNumber = table.Column<int>(type: "int", nullable: false),
                    Step = table.Column<int>(type: "int", nullable: false),
                    ResetPolicy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UsePersianDigits = table.Column<bool>(type: "bit", nullable: false),
                    DefaultDeptCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DefaultCompanyCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterNumberSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LetterNumberSettings_SourceType",
                table: "LetterNumberSettings",
                column: "SourceType",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LetterNumberSettings");
            migrationBuilder.DropColumn(name: "Code", table: "SystemDepartments");
        }
    }
}
