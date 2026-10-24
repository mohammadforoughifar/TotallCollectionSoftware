using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;
using OfficeOpenXml;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class ImportMoadianInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // این migration فقط اسکریپت SQL است، import داده‌ها از طریق
            // یک console app یا سرویس جداگانه انجام می‌شود.
            // voir فایل ImportMoadianInvoicesService.cs برای منطق import
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // هیچ revert specialist نیاز نیست — داده‌های import شده پاک نمی‌شوند
        }
    }
}
