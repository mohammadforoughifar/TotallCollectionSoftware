using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LicenseSigner.Data;

/// <summary>
/// فقط برای زمان طراحی (dotnet ef migrations): بدون این کلاس، `dotnet ef` نمی‌تواند
/// DbContext را بسازد. رشتهٔ اتصال از env خوانده می‌شود و هیچ رازی در کد نیست.
/// </summary>
public sealed class SupportDbContextFactory : IDesignTimeDbContextFactory<SupportDbContext>
{
    public SupportDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("LICENSESIGNER_DESIGN_DB")
                         ?? "Data Source=/var/tmp/signer-design.db";
        var builder = new DbContextOptionsBuilder<SupportDbContext>();
        if (connection.Contains("Server=", StringComparison.OrdinalIgnoreCase))
            builder.UseSqlServer(connection);
        else
            builder.UseSqlite(connection);
        return new SupportDbContext(builder.Options);
    }
}
