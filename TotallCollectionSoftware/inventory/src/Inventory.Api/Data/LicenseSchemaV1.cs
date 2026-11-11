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

-- ---------- مبنای مصرف (استثنا کردن رکوردهای پیش از فعال‌سازی) ----------
IF OBJECT_ID(N'dbo.LicenseUsageBaselines', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LicenseUsageBaselines] (
        [Id]            bigint        NOT NULL IDENTITY(1,1),
        [Module]        nvarchar(32)  NOT NULL,
        [Metric]        nvarchar(64)  NOT NULL,
        [BaselineCount] bigint        NOT NULL,
        [CapturedAt]    datetime2     NOT NULL,
        [Reason]        nvarchar(20)  NOT NULL,
        [Serial]        nvarchar(60)  NULL,
        CONSTRAINT [PK_LicenseUsageBaselines] PRIMARY KEY ([Id])
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LicenseUsageBaselines_Key' AND object_id = OBJECT_ID(N'dbo.LicenseUsageBaselines'))
    CREATE UNIQUE INDEX [IX_LicenseUsageBaselines_Key] ON [dbo].[LicenseUsageBaselines] ([Module], [Metric]);

-- ---------- رزرو سهمیه (عملیات شبکه‌ای: ارسال به مودیان) ----------
-- وضعیت صریح لازم است؛ بدون آن، رزروی که برنامه وسط آن خاموش شده «در هوا» می‌ماند.
IF OBJECT_ID(N'dbo.LicenseReservations', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LicenseReservations] (
        [Id]         bigint        NOT NULL IDENTITY(1,1),
        [Module]     nvarchar(32)  NOT NULL,
        [Metric]     nvarchar(64)  NOT NULL,
        [PeriodKey]  nvarchar(16)  NOT NULL,
        [Amount]     int           NOT NULL,
        [EntityType] nvarchar(64)  NULL,
        [EntityId]   nvarchar(64)  NULL,
        [State]      nvarchar(20)  NOT NULL,
        [ReservedAt] datetime2     NOT NULL,
        [SettledAt]  datetime2     NULL,
        [SettledBy]  nvarchar(100) NULL,
        [Note]       nvarchar(600) NULL,
        [Hmac]       nvarchar(88)  NULL,
        CONSTRAINT [PK_LicenseReservations] PRIMARY KEY ([Id])
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LicenseReservations_State' AND object_id = OBJECT_ID(N'dbo.LicenseReservations'))
    CREATE INDEX [IX_LicenseReservations_State] ON [dbo].[LicenseReservations] ([State], [ReservedAt]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LicenseReservations_Key' AND object_id = OBJECT_ID(N'dbo.LicenseReservations'))
    CREATE INDEX [IX_LicenseReservations_Key] ON [dbo].[LicenseReservations] ([Module], [Metric], [PeriodKey]);

-- ---------- مبنای «بخشودگی گذشته» (فقط دورهٔ جاری) ----------
-- چرا جدول تازه و نه ستون در LicenseUsageBaselines؟ چون آن جدول ایندکس یکتای
-- (Module, Metric) دارد و برای نگه‌داشتن چند دوره باید Drop + ساخت دوباره می‌شد —
-- که خلاف قرارداد «فقط افزایشی» این فایل است. کلید سه‌گانه، هر دوره را جدا نگه می‌دارد.
IF OBJECT_ID(N'dbo.LicenseGrandfatherBaselines', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LicenseGrandfatherBaselines] (
        [Id]            bigint       NOT NULL IDENTITY(1,1),
        [Module]        nvarchar(32) NOT NULL,
        [Metric]        nvarchar(64) NOT NULL,
        [PeriodKey]     nvarchar(16) NOT NULL,
        [BaselineCount] bigint       NOT NULL,
        [CapturedAt]    datetime2    NOT NULL,
        [Serial]        nvarchar(60) NULL,
        CONSTRAINT [PK_LicenseGrandfatherBaselines] PRIMARY KEY ([Id])
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LicenseGrandfatherBaselines_Key' AND object_id = OBJECT_ID(N'dbo.LicenseGrandfatherBaselines'))
    CREATE UNIQUE INDEX [IX_LicenseGrandfatherBaselines_Key] ON [dbo].[LicenseGrandfatherBaselines] ([Module], [Metric], [PeriodKey]);

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
            await RetireCarryOverUsageSqlServerAsync(db);
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
    ""QuotasJson"" TEXT NULL);");
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
CREATE TABLE IF NOT EXISTS ""LicenseUsageBaselines"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_LicenseUsageBaselines"" PRIMARY KEY AUTOINCREMENT,
    ""Module"" TEXT NOT NULL,
    ""Metric"" TEXT NOT NULL,
    ""BaselineCount"" INTEGER NOT NULL,
    ""CapturedAt"" TEXT NOT NULL,
    ""Reason"" TEXT NOT NULL,
    ""Serial"" TEXT NULL);");
            await Exec(connection, @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LicenseUsageBaselines_Key"" ON ""LicenseUsageBaselines"" (""Module"", ""Metric"");");

            await Exec(connection, @"
CREATE TABLE IF NOT EXISTS ""LicenseReservations"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_LicenseReservations"" PRIMARY KEY AUTOINCREMENT,
    ""Module"" TEXT NOT NULL,
    ""Metric"" TEXT NOT NULL,
    ""PeriodKey"" TEXT NOT NULL,
    ""Amount"" INTEGER NOT NULL,
    ""EntityType"" TEXT NULL,
    ""EntityId"" TEXT NULL,
    ""State"" TEXT NOT NULL,
    ""ReservedAt"" TEXT NOT NULL,
    ""SettledAt"" TEXT NULL,
    ""SettledBy"" TEXT NULL,
    ""Note"" TEXT NULL,
    ""Hmac"" TEXT NULL);");
            await Exec(connection, @"CREATE INDEX IF NOT EXISTS ""IX_LicenseReservations_State"" ON ""LicenseReservations"" (""State"", ""ReservedAt"");");
            await Exec(connection, @"CREATE INDEX IF NOT EXISTS ""IX_LicenseReservations_Key"" ON ""LicenseReservations"" (""Module"", ""Metric"", ""PeriodKey"");");

            await Exec(connection, @"
CREATE TABLE IF NOT EXISTS ""LicenseGrandfatherBaselines"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_LicenseGrandfatherBaselines"" PRIMARY KEY AUTOINCREMENT,
    ""Module"" TEXT NOT NULL,
    ""Metric"" TEXT NOT NULL,
    ""PeriodKey"" TEXT NOT NULL,
    ""BaselineCount"" INTEGER NOT NULL,
    ""CapturedAt"" TEXT NOT NULL,
    ""Serial"" TEXT NULL);");
            await Exec(connection, @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LicenseGrandfatherBaselines_Key"" ON ""LicenseGrandfatherBaselines"" (""Module"", ""Metric"", ""PeriodKey"");");

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
            await RetireCarryOverUsageSqliteAsync(connection);
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

    // ==================== بازنشستگی ستون CarryOverUsage ====================

    /// <summary>
    /// ستون <c>CarryOverUsage</c> در نصب‌های قدیمی وجود دارد ولی دیگر در مدل نیست.
    ///
    /// <para>
    /// <b>چرا این متد لازم است؟</b> ستون قدیمی <c>bit NOT NULL</c> و <b>بدون مقدار پیش‌فرض</b>
    /// ساخته شده بود. حالا که موجودیت دیگر این خاصیت را ندارد، EF آن را در
    /// <c>INSERT</c> نمی‌آورد و SQL Server با «Cannot insert the value NULL» می‌شکند.
    /// این متد یک <b>مقدار پیش‌فرض</b> روی همان ستون می‌گذارد تا نصب‌های موجود سالم بمانند.
    /// </para>
    ///
    /// <para>
    /// عمداً ستون را <b>حذف نمی‌کنیم</b>: قاعدهٔ پروژه «اسکیمای افزایشی» است و ستون
    /// بی‌مصرف هیچ آسیبی نمی‌زند. حذف آن یک تصمیم جدا (و بازگشت‌ناپذیر) است.
    /// </para>
    ///
    /// <para>کاملاً idempotent است: اگر ستون نباشد یا پیش‌فرض داشته باشد، کاری نمی‌کند.</para>
    /// </summary>
    private static async Task RetireCarryOverUsageSqlServerAsync(AppDbContext db)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.LicenseItemRows', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.LicenseItemRows', N'CarryOverUsage') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1 FROM sys.default_constraints dc
          JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
         WHERE dc.parent_object_id = OBJECT_ID(N'dbo.LicenseItemRows')
           AND c.name = N'CarryOverUsage')
BEGIN
    ALTER TABLE [dbo].[LicenseItemRows]
        ADD CONSTRAINT [DF_LicenseItemRows_CarryOverUsage] DEFAULT (0) FOR [CarryOverUsage];
END;");
        }
        catch (Exception)
        {
            // اگر این مرحله شکست خورد، برنامه باید همچنان بالا بیاید؛ بدترین حالت
            // «ثبت آیتم در نصب قدیمی» است، نه از کار افتادن کل سامانه.
        }
    }

    /// <summary>
    /// نسخهٔ SQLite: SQLite امکان افزودن مقدار پیش‌فرض به ستون موجود را نمی‌دهد، پس
    /// جدول را با ساختار تازه بازسازی می‌کنیم و ردیف‌ها را با نام ستون‌های صریح کپی
    /// می‌کنیم (بدون آن ستون). اگر ستون نباشد، هیچ کاری نمی‌کنیم.
    /// </summary>
    private static async Task RetireCarryOverUsageSqliteAsync(System.Data.Common.DbConnection connection)
    {
        try
        {
            var hasColumn = false;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('LicenseItemRows')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    if (string.Equals(reader.GetString(1), "CarryOverUsage", StringComparison.OrdinalIgnoreCase))
                        hasColumn = true;
            }

            if (!hasColumn) return;

            await Exec(connection, @"
BEGIN TRANSACTION;
CREATE TABLE ""LicenseItemRows_new"" (
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
    ""QuotasJson"" TEXT NULL);
INSERT INTO ""LicenseItemRows_new""
    (""Id"", ""GrantId"", ""ItemId"", ""ScopeCsv"", ""TermType"", ""ValidFrom"", ""ValidUntil"",
     ""UpdatesUntil"", ""UserMode"", ""UserMax"", ""UserExempt"", ""QuotasJson"")
SELECT ""Id"", ""GrantId"", ""ItemId"", ""ScopeCsv"", ""TermType"", ""ValidFrom"", ""ValidUntil"",
       ""UpdatesUntil"", ""UserMode"", ""UserMax"", ""UserExempt"", ""QuotasJson""
  FROM ""LicenseItemRows"";
DROP TABLE ""LicenseItemRows"";
ALTER TABLE ""LicenseItemRows_new"" RENAME TO ""LicenseItemRows"";
CREATE INDEX IF NOT EXISTS ""IX_LicenseItemRows_GrantId"" ON ""LicenseItemRows"" (""GrantId"");
COMMIT;");
        }
        catch (Exception)
        {
            // مثل نسخهٔ SQL Server: بالا آمدن برنامه مهم‌تر از این پاک‌سازی است.
        }
    }

    private static async Task Exec(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
