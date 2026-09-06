using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMoadianFiscalSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FiscalPrinterSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PrinterName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PortName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PaperWidthMm = table.Column<int>(type: "int", nullable: false),
                    Copies = table.Column<int>(type: "int", nullable: false),
                    HeaderLines = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FooterLines = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CutPaper = table.Column<bool>(type: "bit", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalPrinterSettings", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "MoadianCpcList",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    EnTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoadianCpcList", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "MoadianFiscalPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoadianFiscalPeriods", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "MoadianSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaxId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EconomicCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SellerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SellerAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SellerPostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SellerPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TaxCardToken = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BaseUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PrivateKeyPem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublicKeyPem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AutoSend = table.Column<bool>(type: "bit", nullable: false),
                    SendIntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    DefaultVatRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    LoggingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoadianSettings", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "MoadianInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FiscalPeriodId = table.Column<int>(type: "int", nullable: false),
                    FacInvoiceId = table.Column<int>(type: "int", nullable: true),
                    FacInvoiceRef = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Settlement = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    TaxId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SellerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EconomicCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BuyerTaxId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BuyerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BuyerAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BuyerPostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BuyerPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TotalGross = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalTaxable = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalNet = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TrackingId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SendAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoadianInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MoadianInvoices_MoadianFiscalPeriods_FiscalPeriodId",
                        column: x => x.FiscalPeriodId,
                        principalTable: "MoadianFiscalPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "MoadianInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    RowNo = table.Column<int>(type: "int", nullable: false),
                    SstId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SstTitle = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoadianInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MoadianInvoiceLines_MoadianInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "MoadianInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateTable(
                name: "MoadianLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoadianLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MoadianLogs_MoadianInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "MoadianInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(
                name: "IX_MoadianCpcList_Code",
                table: "MoadianCpcList",
                column: "Code",
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_MoadianFiscalPeriods_Year_Month",
                table: "MoadianFiscalPeriods",
                columns: new[] { "Year", "Month" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_MoadianInvoiceLines_InvoiceId",
                table: "MoadianInvoiceLines",
                column: "InvoiceId");
            migrationBuilder.CreateIndex(
                name: "IX_MoadianInvoices_FacInvoiceId",
                table: "MoadianInvoices",
                column: "FacInvoiceId");
            migrationBuilder.CreateIndex(
                name: "IX_MoadianInvoices_FiscalPeriodId_Number",
                table: "MoadianInvoices",
                columns: new[] { "FiscalPeriodId", "Number" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_MoadianInvoices_ReferenceId",
                table: "MoadianInvoices",
                column: "ReferenceId");
            migrationBuilder.CreateIndex(
                name: "IX_MoadianInvoices_Status",
                table: "MoadianInvoices",
                column: "Status");
            migrationBuilder.CreateIndex(
                name: "IX_MoadianLogs_InvoiceId",
                table: "MoadianLogs",
                column: "InvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiscalPrinterSettings");
            migrationBuilder.DropTable(
                name: "MoadianCpcList");
            migrationBuilder.DropTable(
                name: "MoadianInvoiceLines");
            migrationBuilder.DropTable(
                name: "MoadianLogs");
            migrationBuilder.DropTable(
                name: "MoadianSettings");
            migrationBuilder.DropTable(
                name: "MoadianInvoices");
            migrationBuilder.DropTable(
                name: "MoadianFiscalPeriods");
        }
    }
}
