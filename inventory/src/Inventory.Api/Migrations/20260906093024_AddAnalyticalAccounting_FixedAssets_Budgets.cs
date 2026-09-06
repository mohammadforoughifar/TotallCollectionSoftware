using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyticalAccounting_FixedAssets_Budgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DimensionValueId",
                table: "AccVoucherLines",
                type: "int",
                nullable: true);
            migrationBuilder.CreateTable(
                name: "AccDimensions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccDimensions", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "FixedAssetCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    DefaultUsefulLifeMonths = table.Column<int>(type: "int", nullable: false),
                    DefaultMethod = table.Column<int>(type: "int", nullable: false),
                    DefaultResidualPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DepreciationAccAccountId = table.Column<int>(type: "int", nullable: true),
                    ExpenseAccAccountId = table.Column<int>(type: "int", nullable: true),
                    AccumulatedAccAccountId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssetCategories", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "FixedAssetDepreciationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    RunDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: true),
                    IsPosted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssetDepreciationRuns", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "AccDimensionValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DimensionId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    CodeTree = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AccDimensionId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccDimensionValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccDimensionValues_AccDimensionValues_ParentId",
                        column: x => x.ParentId,
                        principalTable: "AccDimensionValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccDimensionValues_AccDimensions_AccDimensionId",
                        column: x => x.AccDimensionId,
                        principalTable: "AccDimensions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AccDimensionValues_AccDimensions_DimensionId",
                        column: x => x.DimensionId,
                        principalTable: "AccDimensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FiscalYearId = table.Column<int>(type: "int", nullable: false),
                    DimensionValueId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsMaster = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Budgets_AccDimensionValues_DimensionValueId",
                        column: x => x.DimensionValueId,
                        principalTable: "AccDimensionValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Budgets_AccFiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalTable: "AccFiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "FixedAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    EnName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Vendor = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    PurchaseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SalvageValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UsefulLifeMonths = table.Column<int>(type: "int", nullable: false),
                    DepreciationMethod = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DimensionValueId = table.Column<int>(type: "int", nullable: true),
                    AssetAccAccountId = table.Column<int>(type: "int", nullable: true),
                    ExpenseAccAccountId = table.Column<int>(type: "int", nullable: true),
                    AccumulatedAccAccountId = table.Column<int>(type: "int", nullable: true),
                    AccumulatedDepreciation = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixedAssets_AccDimensionValues_DimensionValueId",
                        column: x => x.DimensionValueId,
                        principalTable: "AccDimensionValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FixedAssets_FixedAssetCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "FixedAssetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "BudgetItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BudgetId = table.Column<int>(type: "int", nullable: false),
                    AccAccountId = table.Column<int>(type: "int", nullable: false),
                    PlannedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CommittedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ActualAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetItems_AccAccounts_AccAccountId",
                        column: x => x.AccAccountId,
                        principalTable: "AccAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetItems_Budgets_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "Budgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "FixedAssetDepreciationLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<int>(type: "int", nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AccumulatedAfter = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BookValueAfter = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssetDepreciationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixedAssetDepreciationLines_FixedAssetDepreciationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "FixedAssetDepreciationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FixedAssetDepreciationLines_FixedAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "FixedAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "BudgetTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BudgetId = table.Column<int>(type: "int", nullable: false),
                    BudgetItemId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: true),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    SourceTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetTransactions_BudgetItems_BudgetItemId",
                        column: x => x.BudgetItemId,
                        principalTable: "BudgetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransactions_Budgets_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "Budgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(
                name: "IX_AccVoucherLines_DimensionValueId",
                table: "AccVoucherLines",
                column: "DimensionValueId");
            migrationBuilder.CreateIndex(
                name: "IX_AccDimensions_Code",
                table: "AccDimensions",
                column: "Code",
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_AccDimensionValues_AccDimensionId",
                table: "AccDimensionValues",
                column: "AccDimensionId");
            migrationBuilder.CreateIndex(
                name: "IX_AccDimensionValues_CodeTree",
                table: "AccDimensionValues",
                column: "CodeTree");
            migrationBuilder.CreateIndex(
                name: "IX_AccDimensionValues_DimensionId_Code",
                table: "AccDimensionValues",
                columns: new[] { "DimensionId", "Code" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_AccDimensionValues_ParentId",
                table: "AccDimensionValues",
                column: "ParentId");
            migrationBuilder.CreateIndex(
                name: "IX_BudgetItems_AccAccountId",
                table: "BudgetItems",
                column: "AccAccountId");
            migrationBuilder.CreateIndex(
                name: "IX_BudgetItems_BudgetId_AccAccountId",
                table: "BudgetItems",
                columns: new[] { "BudgetId", "AccAccountId" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_Budgets_DimensionValueId",
                table: "Budgets",
                column: "DimensionValueId");
            migrationBuilder.CreateIndex(
                name: "IX_Budgets_FiscalYearId_DimensionValueId_Name",
                table: "Budgets",
                columns: new[] { "FiscalYearId", "DimensionValueId", "Name" },
                unique: true,
                filter: "[DimensionValueId] IS NOT NULL");
            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransactions_BudgetId",
                table: "BudgetTransactions",
                column: "BudgetId");
            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransactions_BudgetItemId",
                table: "BudgetTransactions",
                column: "BudgetItemId");
            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransactions_VoucherId",
                table: "BudgetTransactions",
                column: "VoucherId");
            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetCategories_Code",
                table: "FixedAssetCategories",
                column: "Code",
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDepreciationLines_AssetId",
                table: "FixedAssetDepreciationLines",
                column: "AssetId");
            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDepreciationLines_RunId_AssetId",
                table: "FixedAssetDepreciationLines",
                columns: new[] { "RunId", "AssetId" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDepreciationRuns_Year_Month",
                table: "FixedAssetDepreciationRuns",
                columns: new[] { "Year", "Month" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_CategoryId",
                table: "FixedAssets",
                column: "CategoryId");
            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_Code",
                table: "FixedAssets",
                column: "Code",
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_DimensionValueId",
                table: "FixedAssets",
                column: "DimensionValueId");
            migrationBuilder.AddForeignKey(
                name: "FK_AccVoucherLines_AccDimensionValues_DimensionValueId",
                table: "AccVoucherLines",
                column: "DimensionValueId",
                principalTable: "AccDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccVoucherLines_AccDimensionValues_DimensionValueId",
                table: "AccVoucherLines");
            migrationBuilder.DropTable(
                name: "BudgetTransactions");
            migrationBuilder.DropTable(
                name: "FixedAssetDepreciationLines");
            migrationBuilder.DropTable(
                name: "BudgetItems");
            migrationBuilder.DropTable(
                name: "FixedAssetDepreciationRuns");
            migrationBuilder.DropTable(
                name: "FixedAssets");
            migrationBuilder.DropTable(
                name: "Budgets");
            migrationBuilder.DropTable(
                name: "FixedAssetCategories");
            migrationBuilder.DropTable(
                name: "AccDimensionValues");
            migrationBuilder.DropTable(
                name: "AccDimensions");
            migrationBuilder.DropIndex(
                name: "IX_AccVoucherLines_DimensionValueId",
                table: "AccVoucherLines");
            migrationBuilder.DropColumn(
                name: "DimensionValueId",
                table: "AccVoucherLines");
        }
    }
}
