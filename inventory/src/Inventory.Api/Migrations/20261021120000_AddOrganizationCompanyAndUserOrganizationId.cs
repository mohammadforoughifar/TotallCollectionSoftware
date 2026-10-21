using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationCompanyAndUserOrganizationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // اضافه کردن CompanyId به جدول Organizations — واحدها به صورت جداگانه برای هر شرکت تعریف می‌شوند
            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Organizations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "SystemUsers",
                type: "int",
                nullable: true);

            // Foreign Key: SystemUser.OrganizationId → Organizations.OrganizationId
            migrationBuilder.CreateIndex(
                name: "IX_SystemUsers_OrganizationId",
                table: "SystemUsers",
                column: "OrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_SystemUsers_Organizations_OrganizationId",
                table: "SystemUsers",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "OrganizationId",
                onDelete: ReferentialAction.Restrict);

            // Foreign Key: Organization.CompanyId → SystemCompanies.Id (اختیاری — اگر شرکت وجود نداشته باشد null می‌ماند)
            migrationBuilder.CreateIndex(
                name: "IX_Organizations_CompanyId",
                table: "Organizations",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Organizations_SystemCompanies_CompanyId",
                table: "Organizations",
                column: "CompanyId",
                principalTable: "SystemCompanies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SystemUsers_Organizations_OrganizationId",
                table: "SystemUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Organizations_SystemCompanies_CompanyId",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_SystemUsers_OrganizationId",
                table: "SystemUsers");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_CompanyId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "SystemUsers");
        }
    }
}
