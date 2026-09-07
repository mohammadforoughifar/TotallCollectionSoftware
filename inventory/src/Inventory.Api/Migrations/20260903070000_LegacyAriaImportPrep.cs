using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <summary>
    /// آماده‌سازی برای مهاجرت داده‌های سیستم قدیمی (ویندوز فرم — دیتابیس aria):
    ///  ۱) ستون <c>OperatorId</c> روی گزارش کار — اپراتوری که عملاً کار را انجام داده (متفاوت از ثبت‌کننده)؛ FK اختیاری به Users
    ///  ۲) <c>KarshenasiAvalie</c> از ۵۰ به ۱۰۰ کاراکتر (داده‌های قدیمی تا ۸۱ کاراکتر داشتند)
    ///  ۳) <c>TotalSpentTime</c> از <c>time</c> به <c>bigint</c> (تیک) — نوع time بیش از ۲۴ ساعت را نمی‌پذیرد
    ///     و پروژه‌های واقعی صدها ساعت گزارش کار دارند
    /// </summary>
    public partial class LegacyAriaImportPrep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OperatorId",
                table: "ReportWorks",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "KarshenasiAvalie",
                table: "ProjectEntryExits",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            // time → bigint (تیک): تبدیل صریح چون SQL Server تبدیل ضمنی time→bigint ندارد
            migrationBuilder.Sql(@"
ALTER TABLE [ProjectEntryExits] ADD [TotalSpentTime_Ticks] bigint NOT NULL CONSTRAINT [DF_ProjectEntryExits_TotalSpentTime_Ticks] DEFAULT(0);
EXEC('UPDATE [ProjectEntryExits] SET [TotalSpentTime_Ticks] = CAST(DATEDIFF(millisecond, CAST(''00:00:00'' AS time), [TotalSpentTime]) AS bigint) * 10000;');
ALTER TABLE [ProjectEntryExits] DROP COLUMN [TotalSpentTime];
EXEC sp_rename 'ProjectEntryExits.TotalSpentTime_Ticks', 'TotalSpentTime', 'COLUMN';
ALTER TABLE [ProjectEntryExits] DROP CONSTRAINT [DF_ProjectEntryExits_TotalSpentTime_Ticks];
");

            migrationBuilder.CreateIndex(
                name: "IX_ReportWorks_OperatorId",
                table: "ReportWorks",
                column: "OperatorId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReportWorks_Users_OperatorId",
                table: "ReportWorks",
                column: "OperatorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReportWorks_Users_OperatorId",
                table: "ReportWorks");

            migrationBuilder.DropIndex(
                name: "IX_ReportWorks_OperatorId",
                table: "ReportWorks");

            migrationBuilder.DropColumn(
                name: "OperatorId",
                table: "ReportWorks");

            migrationBuilder.AlterColumn<string>(
                name: "KarshenasiAvalie",
                table: "ProjectEntryExits",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            // bigint → time (مقادیر بیش از ۲۴ ساعت به ناچار بریده می‌شوند)
            migrationBuilder.Sql(@"
ALTER TABLE [ProjectEntryExits] ADD [TotalSpentTime_Time] time NOT NULL CONSTRAINT [DF_ProjectEntryExits_TotalSpentTime_Time] DEFAULT('00:00:00');
EXEC('UPDATE [ProjectEntryExits] SET [TotalSpentTime_Time] = DATEADD(second, ([TotalSpentTime] / 10000000) % 86400, CAST(''00:00:00'' AS time));');
ALTER TABLE [ProjectEntryExits] DROP COLUMN [TotalSpentTime];
EXEC sp_rename 'ProjectEntryExits.TotalSpentTime_Time', 'TotalSpentTime', 'COLUMN';
ALTER TABLE [ProjectEntryExits] DROP CONSTRAINT [DF_ProjectEntryExits_TotalSpentTime_Time];
");
        }
    }
}
