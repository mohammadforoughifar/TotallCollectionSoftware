using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMessengers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaleChatId",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EitaaChatId",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mobile",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaleBotToken",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EitaaToken",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessengerSenderNumber",
                table: "AppSettings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaleChatId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EitaaChatId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Mobile",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BaleBotToken",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "EitaaToken",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "MessengerSenderNumber",
                table: "AppSettings");
        }
    }
}
