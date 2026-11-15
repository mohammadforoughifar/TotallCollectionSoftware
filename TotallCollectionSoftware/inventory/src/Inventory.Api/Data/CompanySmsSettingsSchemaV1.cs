using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>One independent opt-in per company and module; absent means disabled.</summary>
public static class CompanySmsSettingsSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.SystemCompanies', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.CompanySmsSettings', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CompanySmsSettings] (
        [CompanyId] int NOT NULL,
        [ModuleKey] nvarchar(80) NOT NULL,
        [Enabled] bit NOT NULL CONSTRAINT [DF_CompanySmsSettings_Enabled] DEFAULT 0,
        [Revision] bigint NOT NULL CONSTRAINT [DF_CompanySmsSettings_Revision] DEFAULT 1,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [UpdatedBy] nvarchar(120) NULL,
        CONSTRAINT [PK_CompanySmsSettings] PRIMARY KEY ([CompanyId], [ModuleKey]),
        CONSTRAINT [FK_CompanySmsSettings_SystemCompanies_CompanyId] FOREIGN KEY ([CompanyId])
            REFERENCES [dbo].[SystemCompanies] ([Id]) ON DELETE CASCADE
    );
END;";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS [CompanySmsSettings] (
    [CompanyId] INTEGER NOT NULL,
    [ModuleKey] TEXT NOT NULL,
    [Enabled] INTEGER NOT NULL DEFAULT 0,
    [Revision] INTEGER NOT NULL DEFAULT 1,
    [UpdatedAtUtc] TEXT NOT NULL,
    [UpdatedBy] TEXT NULL,
    CONSTRAINT [PK_CompanySmsSettings] PRIMARY KEY ([CompanyId], [ModuleKey]),
    CONSTRAINT [FK_CompanySmsSettings_SystemCompanies_CompanyId] FOREIGN KEY ([CompanyId])
        REFERENCES [SystemCompanies] ([Id]) ON DELETE CASCADE
);";

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
