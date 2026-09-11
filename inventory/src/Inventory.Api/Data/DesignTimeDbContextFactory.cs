using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inventory.Api.Data;

/// <summary>
/// فکتوری دیزاین‌تایم برای ابزار dotnet-ef — فقط برای ساخت مایگریشن/اسکریپت.
/// به دیتابیس وصل نمی‌شود؛ مایگریشن‌های این پروژه SQL Server را هدف می‌گیرند
/// (حالت Sqlite توسعه با EnsureCreated + اسکیماهای خودترمیم بالا می‌آید).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=.;Database=InventoryDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False")
            .Options;
        return new AppDbContext(options);
    }
}
