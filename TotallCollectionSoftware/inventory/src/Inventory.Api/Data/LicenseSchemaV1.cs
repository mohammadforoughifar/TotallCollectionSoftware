using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ساخت افزایشی جداول لایسنس.
///
/// <para>
/// چرا موازی با مایگریشن EF؟ چون این پروژه دو مسیر دارد:
/// <list type="bullet">
/// <item>SQL Server ⇒ <c>db.Database.Migrate()</c> برای جداول اصلی پروژه؛ جداول
/// لایسنس از همین فایل با دستورهای افزایشی ساخته می‌شوند (مایگریشن جداگانه‌ای برایشان
/// وجود ندارد و لازم هم نیست).</item>
/// <item>SQLite (محیط توسعه) ⇒ <c>EnsureCreated()</c> که دیتابیس موجود را ارتقا
/// <b>نمی‌دهد</b>؛ پس ستون/جدول‌های تازه باید دستی اضافه شوند. الگوی موجود پروژه
/// همین کلاس‌های <c>XxxSchemaV1.EnsureAsync</c> است.</item>
/// </list>
/// </para>
///
/// <para>
/// ⚠️ همهٔ دستورها <b>افزایشی</b> هستند: فقط <c>CREATE TABLE IF NOT EXISTS</c>،
/// <c>ALTER TABLE ... ADD</c> برای ستون تازه، و <c>CREATE INDEX</c> در صورت نبود.
/// هیچ Drop/Rebuild در این فایل نیست و نباید اضافه شود.
/// </para>
/// </summary>
public static class LicenseSchemaV1
{
    public const string SqlServer = @"
-- ---------- هویت نصب ----------
IF OBJECT_ID(N'dbo.LicenseInstallations', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LicenseInstallations] (
        [Id]               int            NOT NULL IDENTITY(1,1),
        [InstallationId]   nvarchar(64)   NOT NULL,
        [DeploymentMode]   nvarchar(20)   NOT NULL,
        [TenantId]         int            NULL,
        [CustomerName]     nvarchar(200)  NULL,
        [MachineFingerprint] nvarchar(600) NULL,
        [CreatedAt]        datetime2      NOT NULL,
        [UpdatedAt]        datetime2      NOT NULL,
        CONSTRAINT [PK_LicenseInstallations] PRIMARY KEY ([Id])
    );
END;

-- ---------- ستون‌های مهلت سازگاری (افزایشی) ----------
IF OBJECT_ID(N'dbo.LicenseInstallations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.LicenseInstallations', 'GraceUntil') IS NULL
    ALTER TABLE [dbo].[LicenseInstallations] ADD [GraceUntil] datetime2 NULL;
IF OBJECT_ID(N'dbo.LicenseInstallations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.LicenseInstallations', 'IsLegacyInstall') IS NULL
    ALTER TABLE [dbo].[LicenseInstallations] ADD [IsLegacyInstall] bit NULL;
IF OBJECT_ID(N'dbo.LicenseInstallations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.LicenseInstallations', 'GraceReason') IS NULL
    ALTER TABLE [dbo].[LicenseInstallations] ADD [GraceReason] nvarchar(600) NULL;

-- ---------- فایل‌های لایسنس اعمال‌شده ----------
IF OBJECT_ID(N'dbo.LicenseGrants', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LicenseGrants] (
        [Id]              int            NOT NULL IDENTITY(1,1),
        [TenantId]        int            NULL,
        [Serial]          nvarchar(64)   NOT NULL,
        [KeyId]           nvarchar(64)   NOT NULL,
        [Algorithm]       nvarchar(20)   NOT NULL,
        [Payload]         nvarchar(max)  NOT NULL,
        [Signature]       nvarchar(max)  NOT NULL,
        [FileName]        nvarchar(260)  NULL,
        [Status]          nvarchar(20)   NOT NULL,
        [IsAddOn]         bit            NOT NULL,
        [CustomerName]    nvarchar(200)  NULL,
        [ValidFrom]       datetime2      NULL,
        [ValidUntil]      datetime2      NULL,
        [AppliedAt]       datetime2      NOT NULL,
        [AppliedByUserId] int            NULL,
        [ValidationNote]  nvarchar(1200) NULL,
        CONSTRAINT [PK_LicenseGrants] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_LicenseGrants_Serial] ON [dbo].[LicenseGrants] ([Serial]);
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LicenseGrants_Serial' AND object_id = OBJECT_ID(N'dbo.LicenseGrants'))
    CREATE UNIQUE INDEX [IX_LicenseGrants_Serial] ON [dbo].[LicenseGrants] ([Serial]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LicenseGrants_Status' AND object_id = OBJECT_ID(N'dbo.LicenseGrants'))
    CREATE INDEX [IX_LicenseGrants_Status] ON [dbo].[LicenseGrants] ([Status], [AppliedAt]);

-- ---------- آیتم‌های سند (فقط SaaS) ----------
IF OBJECT_ID(N'dbo.LicenseItemRows', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LicenseItemRows] (
        [Id]             int           NOT NULL IDENTITY(1,1),
        [GrantId]        int           NOT NULL,
        [ItemId]         nvarchar(64)  NOT NULL,
        [ScopeCsv]       nvarchar(600) NOT NULL,
        [TermType]       nvarchar(20)  NOT NULL,
        [ValidFrom]      datetime2     NULL,
        [ValidUntil]     datetime2     NULL,
        [UpdatesUntil]   datetime2     NULL,
        [UserMode]       nvarchar(20)  NOT NULL,
        [UserMax]        int           NOT NULL,
        [UserExempt]     int           NOT NULL,
        [QuotasJson]     nvarchar(max) NULL,
        [CarryOverUsage] bit           NOT NULL,
        CONSTRAINT [PK_LicenseItemRows] PRIMARY KEY ([Id])
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LicenseItemRows_GrantId' AND object_id = OBJECT_ID(N'dbo.LicenseItemRows'))
    CREATE INDEX [IX_LicenseItemRows_GrantId] ON [dbo].[LicenseItemRows] ([GrantId]);

-- ---------- شمارندهٔ مصرف ----------
IF OBJECT_ID(N'dbo.LicenseUsages', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LicenseUsages] (
        [Id]        int          NOT NULL IDENTITY(1,1),
        [Module]    nvarchar(32) NOT NULL,
        [Metric]    nvarchar(64) NOT NULL,
        [PeriodKey] nvarchar(16) NOT NULL,
        [Limit]     bigint       NOT NULL,
        [Used]      bigint       NOT NULL,
        [Hmac]      nvarchar(88) NULL,
        [UpdatedAt] datetime2    NOT NULL,
        CONSTRAINT [PK_LicenseUsages] PRIMARY KEY ([Id])
    );
    -- کلید یکتا: یک ردیف مصرف برای هر (ماژول، متریک، دوره)
    CREATE UNIQUE INDEX [IX_LicenseUsages_Key] ON [dbo].[LicenseUsages] ([Module], [Metric], [PeriodKey]);
END;

-- ---------- دفتر الحاقی مصرف (مصرف/بازگشت) ----------
IF OBJECT_ID(N'dbo.LicenseUsageLedger', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LicenseUsageLedger] (
        [Id]         bigint        NOT NULL IDENTITY(1,1),
        [Module]     nvarchar(32)  NOT NULL,
        [Metric]     nvarchar(64)  NOT NULL,
        [PeriodKey]  nvarchar(16)  NOT NULL,
        [Delta]      int           NOT NULL,
        [EntityType] nvarchar(60)  NULL,
        [EntityId]   nvarchar(64)  NULL,
        [Reason]     nvarchar(20)  NOT NULL,
        [Hmac]       nvarchar(88)  NULL,
        [At]         datetime2     NOT NULL,
        [UserId]     int           NULL,
        CONSTRAINT [PK_LicenseUsageLedger] PRIMARY KEY ([Id])
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LicenseUsageLedger_Key' AND object_id = OBJECT_ID(N'dbo.LicenseUsageLedger'))
    CREATE INDEX [IX_LicenseUsageLedger_Key] ON [dbo].[LicenseUsageLedger] ([Module], [Metric], [PeriodKey]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LicenseUsageLedger_Entity' AND object_id = OBJECT_ID(N'dbo.LicenseUsageLedger'))
    CREATE INDEX [IX_LicenseUsageLedger_Entity] ON [dbo].[LicenseUsageLedger] ([EntityType], [EntityId]);

-- ---------- نشست کاربر (کاربر هم‌زمان) ----------
IF OBJECT_ID(N'dbo.LicenseSessions', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LicenseSessions] (
        [Id]          int          NOT NULL IDENTITY(1,1),
        [SessionId]   nvarchar(64) NOT NULL,
        [UserId]      int          NOT NULL,
        [StartedAt]   datetime2    NOT NULL,
        [LastSeen]    datetime2    NOT NULL,
        [MachineName] nvarchar(100) NULL,
        [Ip]          nvarchar(64) NULL,
        [Status]      nvarchar(20) NOT NULL,
        [EndedAt]     datetime2    NULL,
        [EndReason]   nvarchar(60) NULL,
        CONSTRAINT [PK_LicenseSessions] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_LicenseSessions_SessionId] ON [dbo].[LicenseSessions] ([SessionId]);
    CREATE INDEX [IX_LicenseSessions_Status] ON [dbo].[LicenseSessions] ([Status], [LastSeen]);
END;

-- ---------- لاگ رخداد ----------
IF OBJECT_ID(N'dbo.LicenseEvents', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LicenseEvents] (
        [Id]       int           NOT NULL IDENTITY(1,1),
        [At]       datetime2     NOT NULL,
        [Kind]     nvarchar(60)  NOT NULL,
        [Severity] nvarchar(20)  NOT NULL,
        [Message]  nvarchar(600) NOT NULL,
        [DataJson] nvarchar(max) NULL,
        [UserId]   int           NULL,
        [Serial]   nvarchar(64)  NULL,
        CONSTRAINT [PK_LicenseEvents] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_LicenseEvents_At] ON [dbo].[LicenseEvents] ([At]);
END;

-- ---------- ستون معافیت صندلی روی جدول کاربران (افزایشی) ----------
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Users', 'LicenseSeatExempt') IS NULL
    ALTER TABLE [dbo].[Users] ADD [LicenseSeatExempt] bit NOT NULL CONSTRAINT [DF_Users_LicenseSeatExempt] DEFAULT(0);
";

    /// <summary>اجرای افزایشی روی دیتابیس جاری (SQL Server یا SQLite).</summary>
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (!db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync(SqlServer);
            return;
        }

        // ---------- SQLite ----------
        var connection = db.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync();
        try
        {
            await Exec(connection, @"
CREATE TABLE IF NOT EXISTS ""LicenseInstallations"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_LicenseInstallations"" PRIMARY KEY AUTOINCREMENT,
    ""InstallationId"" TEXT NOT NULL,
    ""DeploymentMode"" TEXT NOT NULL,
    ""TenantId"" INTEGER NULL,
    ""CustomerName"" TEXT NULL,
    ""MachineFingerprint"" TEXT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL);");

            await Exec(connection, @"
CREATE TABLE IF NOT EXISTS ""LicenseGrants"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_LicenseGrants"" PRIMARY KEY AUTOINCREMENT,
    ""TenantId"" INTEGER NULL,
    ""Serial"" TEXT NOT NULL,
    ""KeyId"" TEXT NOT NULL,
    ""Algorithm"" TEXT NOT NULL,
    ""Payload"" TEXT NOT NULL,
    ""Signature"" TEXT NOT NULL,
    ""FileName"" TEXT NULL,
    ""Status"" TEXT NOT NULL,
    ""IsAddOn"" INTEGER NOT NULL,
    ""CustomerName"" TEXT NULL,
    ""ValidFrom"" TEXT NULL,
    ""ValidUntil"" TEXT NULL,
    ""AppliedAt"" TEXT NOT NULL,
    ""AppliedByUserId"" INTEGER NULL,
    ""ValidationNote"" TEXT NULL);");
                        // ---------- ستون‌های مهلت سازگاری (افزایشی؛ جدول ممکن است از قبل بدون این‌ها باشد) ----------
            await AddColumnIfMissingAsync(connection, "LicenseInstallations", "GraceUntil", "TEXT NULL");
            await AddColumnIfMissingAsync(connection, "LicenseInstallations", "IsLegacyInstall", "INTEGER NULL");
            await AddColumnIfMissingAsync(connection, "LicenseInstallations", "GraceReason", "TEXT NULL");

await Exec(connection, @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LicenseGrants_Serial"" ON ""LicenseGrants"" (""Serial"");");
            await Exec(connection, @"CREATE INDEX IF NOT EXISTS ""IX_LicenseGrants_Status"" ON ""LicenseGrants"" (""Status"", ""AppliedAt"");");

            await Exec(connection, @"
CREATE TABLE IF NOT EXISTS ""LicenseItemRows"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_LicenseItemRows"" PRIMARY KEY AUTOINCREMENT,
    ""GrantId"" INTEGER NOT NULL,
    ""ItemId"" TEXT NOT NULL,
    ""ScopeCsv"" TEXT NOT NULL,
    ""TermType"" TEXT NOT NULL,
    ""ValidFrom"" TEXT NULL,
    ""ValidUntil"" TEXT NULL,
    ""UpdatesUntil"" TEXT NULL,
    ""UserMode"" TEXT NOT NULL,
    ""UserMax"" INTEGER NOT NULL,
    ""UserExempt"" INTEGER NOT NULL,
    ""QuotasJson"" TEXT NULL,
    ""CarryOverUsage"" INTEGER NOT NULL);");
            await Exec(connection, @"CREATE INDEX IF NOT EXISTS ""IX_LicenseItemRows_GrantId"" ON ""LicenseItemRows"" (""GrantId"");");

            await Exec(connection, @"
CREATE TABLE IF NOT EXISTS ""LicenseUsages"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_LicenseUsages"" PRIMARY KEY AUTOINCREMENT,
    ""Module"" TEXT NOT NULL,
    ""Metric"" TEXT NOT NULL,
    ""PeriodKey"" TEXT NOT NULL,
    ""Limit"" INTEGER NOT NULL,
    ""Used"" INTEGER NOT NULL,
    ""Hmac"" TEXT NULL,
    ""UpdatedAt"" TEXT NOT NULL);");
            await Exec(connection, @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LicenseUsages_Key"" ON ""LicenseUsages"" (""Module"", ""Metric"", ""PeriodKey"");");

            await Exec(connection, @"
CREATE TABLE IF NOT EXISTS ""LicenseUsageLedger"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_LicenseUsageLedger"" PRIMARY KEY AUTOINCREMENT,
    ""Module"" TEXT NOT NULL,
    ""Metric"" TEXT NOT NULL,
    ""PeriodKey"" TEXT NOT NULL,
    ""Delta"" INTEGER NOT NULL,
    ""EntityType"" TEXT NULL,
    ""EntityId"" TEXT NULL,
    ""Reason"" TEXT NOT NULL,
    ""Hmac"" TEXT NULL,
    ""At"" TEXT NOT NULL,
    ""UserId"" INTEGER NULL);");
            await Exec(connection, @"CREATE INDEX IF NOT EXISTS ""IX_LicenseUsageLedger_Key"" ON ""LicenseUsageLedger"" (""Module"", ""Metric"", ""PeriodKey"");");
            await Exec(connection, @"CREATE INDEX IF NOT EXISTS ""IX_LicenseUsageLedger_Entity"" ON ""LicenseUsageLedger"" (""EntityType"", ""EntityId"");");

            await Exec(connection, @"
CREATE TABLE IF NOT EXISTS ""LicenseSessions"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_LicenseSessions"" PRIMARY KEY AUTOINCREMENT,
    ""SessionId"" TEXT NOT NULL,
    ""UserId"" INTEGER NOT NULL,
    ""StartedAt"" TEXT NOT NULL,
    ""LastSeen"" TEXT NOT NULL,
    ""MachineName"" TEXT NULL,
    ""Ip"" TEXT NULL,
    ""Status"" TEXT NOT NULL,
    ""EndedAt"" TEXT NULL,
    ""EndReason"" TEXT NULL);");
            await Exec(connection, @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LicenseSessions_SessionId"" ON ""LicenseSessions"" (""SessionId"");");
            await Exec(connection, @"CREATE INDEX IF NOT EXISTS ""IX_LicenseSessions_Status"" ON ""LicenseSessions"" (""Status"", ""LastSeen"");");

            await Exec(connection, @"
CREATE TABLE IF NOT EXISTS ""LicenseEvents"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_LicenseEvents"" PRIMARY KEY AUTOINCREMENT,
    ""At"" TEXT NOT NULL,
    ""Kind"" TEXT NOT NULL,
    ""Severity"" TEXT NOT NULL,
    ""Message"" TEXT NOT NULL,
    ""DataJson"" TEXT NULL,
    ""UserId"" INTEGER NULL,
    ""Serial"" TEXT NULL);");
            await Exec(connection, @"CREATE INDEX IF NOT EXISTS ""IX_LicenseEvents_At"" ON ""LicenseEvents"" (""At"");");

            // ستون معافیت صندلی — فقط اگر جدول Users از قبل هست و ستون را ندارد
            await using var exists = connection.CreateCommand();
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Users'";
            if (Convert.ToInt32(await exists.ExecuteScalarAsync()) > 0)
            {
                var present = false;
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA table_info('Users')";
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        if (string.Equals(reader.GetString(1), "LicenseSeatExempt", StringComparison.OrdinalIgnoreCase)) present = true;
                }
                if (!present) await Exec(connection, @"ALTER TABLE ""Users"" ADD COLUMN ""LicenseSeatExempt"" INTEGER NOT NULL DEFAULT 0;");
            }
        }
        finally { if (close) await connection.CloseAsync(); }
    }

    /// <summary>افزودن ستون در صورت نبود (فقط SQLite؛ SQL Server بلوک SQL خودش را دارد).</summary>
    private static async Task AddColumnIfMissingAsync(System.Data.Common.DbConnection connection,
        string table, string column, string sqlType)
    {
        var present = false;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info('{table}')";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) present = true;
        }

        if (!present)
            // نام ستون/جدول فقط از مقادیر ثابت همین فایل می‌آید (نه ورودی کاربر)؛ نوع هم ثابت است.
            await Exec(connection, $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {sqlType};");
    }

    private static async Task Exec(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
