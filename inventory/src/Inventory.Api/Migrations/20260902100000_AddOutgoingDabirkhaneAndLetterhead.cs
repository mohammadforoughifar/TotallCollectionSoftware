using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <summary>
    /// دبیرخانه نامه صادره + سربرگ شرکت:
    /// • OutgoingLetters: CompanyId (شرکت صادرکننده/سربرگ)، DabirkhaneSabt، DabirkhaneUserId،
    ///   DateDabirkhane، DestRegNumber (شماره ثبت مقصد)، SendMethod (روش ارسال)، DabirkhaneNote
    /// • SystemCompanies: LetterheadFileName — نام فایل PDF سربرگ در مسیر روت API
    /// </summary>
    public partial class AddOutgoingDabirkhaneAndLetterhead : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "OutgoingLetters",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DabirkhaneSabt",
                table: "OutgoingLetters",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DabirkhaneUserId",
                table: "OutgoingLetters",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateDabirkhane",
                table: "OutgoingLetters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestRegNumber",
                table: "OutgoingLetters",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SendMethod",
                table: "OutgoingLetters",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DabirkhaneNote",
                table: "OutgoingLetters",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LetterheadFileName",
                table: "SystemCompanies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CompanyId", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "DabirkhaneSabt", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "DabirkhaneUserId", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "DateDabirkhane", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "DestRegNumber", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "SendMethod", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "DabirkhaneNote", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "LetterheadFileName", table: "SystemCompanies");
        }
    }
}
