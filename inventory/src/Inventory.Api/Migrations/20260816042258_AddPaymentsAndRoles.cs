using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentsAndRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CashType",
                table: "Transactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "Transactions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SettledAmount",
                table: "Transactions",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Cheques",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccountInfo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OwnerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsCleared = table.Column<bool>(type: "bit", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cheques", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cheques_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Installments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    No = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Installments_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cheques_DueDate",
                table: "Cheques",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Cheques_IsCleared",
                table: "Cheques",
                column: "IsCleared");

            migrationBuilder.CreateIndex(
                name: "IX_Cheques_TransactionId",
                table: "Cheques",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Installments_DueDate",
                table: "Installments",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Installments_TransactionId",
                table: "Installments",
                column: "TransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cheques");

            migrationBuilder.DropTable(
                name: "Installments");

            migrationBuilder.DropColumn(
                name: "CashType",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SettledAmount",
                table: "Transactions");
        }
    }
}
