using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

/// <summary>
/// پرداخت ترکیبی سفارش ویزیتور (نقد/چک/اقساط/نسیه) و تصمیم مالی روی پیوست‌ها.
/// فقط ستون‌های افزودنی؛ هیچ سند، مبلغ یا وضعیتی بازنویسی نمی‌شود.
/// SQLite با خودتعمیر زمان راه‌اندازی (VisPaymentPlanSchemaV1) پوشش داده می‌شود.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261125120000_AddVisPaymentPlan")]
public class AddVisPaymentPlan : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) != true)
            throw new NotSupportedException("SQLite uses startup schema repair instead of SQL Server migrations.");
        migrationBuilder.Sql(VisPaymentPlanSchemaV1.SqlServer);
        // ستون‌های دسته/شرکت/تصمیم مالی روی جدول مشترک پیوست‌ها — همان چیزی که
        // کارتابل مالی برای عکس چک لازم دارد.
        migrationBuilder.Sql(VisAttachmentSchemaV1.SqlServer);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Payment plan parts are part of the order history and cannot be dropped safely.");
}
