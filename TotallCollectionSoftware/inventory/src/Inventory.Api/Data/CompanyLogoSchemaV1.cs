using Microsoft.EntityFrameworkCore;
namespace Inventory.Api.Data;
public static class CompanyLogoSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.SystemCompanies', N'U') IS NOT NULL AND COL_LENGTH('dbo.SystemCompanies','LogoDataUrl') IS NULL
 ALTER TABLE [dbo].[SystemCompanies] ADD [LogoDataUrl] nvarchar(max) NULL;";
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (!db.Database.IsSqlite()) { await db.Database.ExecuteSqlRawAsync(SqlServer); return; }
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('SystemCompanies');";
        var exists = false; var hasLogo = false;
        using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) { exists = true; if (reader.GetString(1) == "LogoDataUrl") hasLogo = true; }
        if (exists && !hasLogo) await db.Database.ExecuteSqlRawAsync("ALTER TABLE SystemCompanies ADD COLUMN LogoDataUrl TEXT NULL;");
    }
}
