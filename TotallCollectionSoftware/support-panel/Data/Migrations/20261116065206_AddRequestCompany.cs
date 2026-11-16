using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LicenseSigner.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompanyId",
                table: "Requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyName",
                table: "Requests",
                type: "TEXT",
                nullable: true);

            // پرکردن درخواست‌های قدیمی از InterestedCompaniesJson (اولین شرکت اعلام‌شده).
            // قاعدهٔ محصول: هر لایسنس برای یک شرکت ⇒ اولین شرکت، شرکتِ همین درخواست است.
            migrationBuilder.Sql(
                "UPDATE Requests " +
                "SET CompanyId = (SELECT json_extract(value, '$.companyId') FROM json_each(Requests.InterestedCompaniesJson) LIMIT 1), " +
                "    CompanyName = (SELECT json_extract(value, '$.name') FROM json_each(Requests.InterestedCompaniesJson) LIMIT 1) " +
                "WHERE CompanyId IS NULL AND InterestedCompaniesJson IS NOT NULL " +
                "  AND InterestedCompaniesJson LIKE '[%' AND InterestedCompaniesJson <> '[]';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "CompanyName",
                table: "Requests");
        }
    }
}
