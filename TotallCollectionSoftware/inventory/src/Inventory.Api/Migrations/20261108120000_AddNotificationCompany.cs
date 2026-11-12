using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventory.Api.Migrations;

/// <summary>
/// ستون CompanyId برای AppNotifications — اعلان فقط در شرکت فعال انتخاب‌شده دیده شود.
/// (اعلان‌های عمومی با CompanyId = NULL در همهٔ شرکت‌ها می‌مانند.)
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261108120000_AddNotificationCompany")]
public partial class AddNotificationCompany : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!migrationBuilder.ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SQLite uses startup schema repair instead of SQL Server migrations.");
        migrationBuilder.Sql(NotificationCompanySchemaV1.SqlServer);
    }

    // برگشت بی‌اثر است: حذف ستون، نسبت‌دادن اعلان‌ها به شرکت را از بین می‌برد و
    // اعلان‌های شرکتی دوباره در همهٔ شرکت‌ها ظاهر می‌شوند.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Notification company attribution cannot be safely dropped.");
}
