using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای «مدیریت برنامه‌نویسان» — نسخه ۱
//
//  چرا SchemaV1 و نه مایگریشن:
//    EnsureCreated (SQLite) و Migrate (SQL Server) جدول‌های تازه را به
//    دیتابیس‌های قدیمی اضافه نمی‌کنند. همان راه‌حلی که برای داشبورد شخصی
//    (DashboardSchemaV1) و کارتابل نامه وارده (IncomingLetterSchemaV1)
//    استفاده شده، اینجا هم به‌کار می‌رود تا ماژول روی دیتابیس‌های در حال
//    استفادهٔ تیم بدون اجرای دستی اسکریپت بالا بیاید.
//
//  جدول‌ها:
//    DevMembers   : اعضای تیم توسعه
//    DevModules   : ماژول‌های نرم‌افزار و مالک هرکدام
//    DevTasks     : آیتم‌های کاری (ستون‌های بورد)
//    DevTaskLogs  : تاریخچهٔ «کی، چه زمانی، چه کرد»
// ============================================================
public static class DevTeamSchemaV1
{
    private static int _done;
    private static int _running;

    /// <summary>آخرین خطای ساخت جدول (null یعنی موفق).</summary>
    public static string? LastError { get; private set; }

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    /// <summary>تضمین یک‌بار ساخت جدول‌ها در هر فرایند (با تلاش دوباره در صورت شکست قبلی).</summary>
    public static async Task EnsureOnceAsync(AppDbContext db)
    {
        if (Volatile.Read(ref _done) == 1 && LastError is null) return;
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            LastError = null;
            await EnsureAsync(db);
            if (LastError is null) Volatile.Write(ref _done, 1);
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DevMembers', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DevMembers](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [FullName] nvarchar(120) NOT NULL,
        [GithubHandle] nvarchar(60) NULL,
        [Email] nvarchar(160) NULL,
        [Phone] nvarchar(40) NULL,
        [Role] int NOT NULL DEFAULT(0),
        [IsActive] bit NOT NULL DEFAULT(1),
        [ColorHex] nvarchar(9) NOT NULL DEFAULT(N'#6c757d'),
        [Note] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME())
    );
    CREATE INDEX [IX_DevMembers_IsActive] ON [dbo].[DevMembers] ([IsActive]);
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DevModules', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DevModules](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Key] nvarchar(60) NOT NULL,
        [Title] nvarchar(120) NOT NULL,
        [OwnerId] int NULL,
        [Icon] nvarchar(60) NULL,
        [ColorHex] nvarchar(9) NOT NULL DEFAULT(N'#0d6efd'),
        [SortOrder] int NOT NULL DEFAULT(0),
        [IsActive] bit NOT NULL DEFAULT(1),
        [RepoPaths] nvarchar(1000) NULL,
        [ServiceLines] int NOT NULL DEFAULT(0),
        [PageCount] int NOT NULL DEFAULT(0),
        [Note] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        CONSTRAINT [FK_DevModules_DevMembers_Owner] FOREIGN KEY ([OwnerId])
            REFERENCES [dbo].[DevMembers] ([Id]) ON DELETE SET NULL
    );
    CREATE UNIQUE INDEX [UX_DevModules_Key] ON [dbo].[DevModules] ([Key]);
    CREATE INDEX [IX_DevModules_OwnerId] ON [dbo].[DevModules] ([OwnerId]);
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DevTasks', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DevTasks](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Number] nvarchar(30) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Description] nvarchar(2000) NULL,
        [ModuleId] int NOT NULL,
        [AssigneeId] int NULL,
        [Status] int NOT NULL DEFAULT(0),
        [Priority] int NOT NULL DEFAULT(1),
        [Size] int NOT NULL DEFAULT(1),
        [BranchName] nvarchar(160) NULL,
        [PullRequestUrl] nvarchar(300) NULL,
        [AgentAssisted] bit NOT NULL DEFAULT(0),
        [StartedAt] datetime2 NULL,
        [DueDate] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        [CreatedBy] nvarchar(120) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        [UpdatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        CONSTRAINT [FK_DevTasks_DevModules] FOREIGN KEY ([ModuleId])
            REFERENCES [dbo].[DevModules] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DevTasks_DevMembers_Assignee] FOREIGN KEY ([AssigneeId])
            REFERENCES [dbo].[DevMembers] ([Id]) ON DELETE SET NULL
    );
    CREATE INDEX [IX_DevTasks_Status] ON [dbo].[DevTasks] ([Status]);
    CREATE INDEX [IX_DevTasks_ModuleId] ON [dbo].[DevTasks] ([ModuleId]);
    CREATE INDEX [IX_DevTasks_AssigneeId] ON [dbo].[DevTasks] ([AssigneeId]);
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DevTaskLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DevTaskLogs](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [TaskId] int NOT NULL,
        [MemberId] int NULL,
        [Action] int NOT NULL DEFAULT(0),
        [Note] nvarchar(1000) NULL,
        [FromStatus] int NULL,
        [ToStatus] int NULL,
        [CommitSha] nvarchar(60) NULL,
        [At] datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        CONSTRAINT [FK_DevTaskLogs_DevTasks] FOREIGN KEY ([TaskId])
            REFERENCES [dbo].[DevTasks] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_DevTaskLogs_DevMembers] FOREIGN KEY ([MemberId])
            REFERENCES [dbo].[DevMembers] ([Id]) ON DELETE SET NULL
    );
    CREATE INDEX [IX_DevTaskLogs_TaskId] ON [dbo].[DevTaskLogs] ([TaskId]);
    CREATE INDEX [IX_DevTaskLogs_At] ON [dbo].[DevTaskLogs] ([At]);
    CREATE INDEX [IX_DevTaskLogs_MemberId] ON [dbo].[DevTaskLogs] ([MemberId]);
END");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DevMembers (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    FullName TEXT NOT NULL,
    GithubHandle TEXT NULL,
    Email TEXT NULL,
    Phone TEXT NULL,
    Role INTEGER NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 1,
    ColorHex TEXT NOT NULL DEFAULT '#6c757d',
    Note TEXT NULL,
    CreatedAt TEXT NOT NULL
);");
        await SafeAsync(db, @"CREATE INDEX IF NOT EXISTS IX_DevMembers_IsActive ON DevMembers (IsActive);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DevModules (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Key TEXT NOT NULL,
    Title TEXT NOT NULL,
    OwnerId INTEGER NULL,
    Icon TEXT NULL,
    ColorHex TEXT NOT NULL DEFAULT '#0d6efd',
    SortOrder INTEGER NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 1,
    RepoPaths TEXT NULL,
    ServiceLines INTEGER NOT NULL DEFAULT 0,
    PageCount INTEGER NOT NULL DEFAULT 0,
    Note TEXT NULL,
    CreatedAt TEXT NOT NULL,
    CONSTRAINT FK_DevModules_DevMembers_Owner FOREIGN KEY (OwnerId)
        REFERENCES DevMembers (Id) ON DELETE SET NULL
);");
        await SafeAsync(db, @"CREATE UNIQUE INDEX IF NOT EXISTS UX_DevModules_Key ON DevModules (Key);");
        await SafeAsync(db, @"CREATE INDEX IF NOT EXISTS IX_DevModules_OwnerId ON DevModules (OwnerId);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DevTasks (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Number TEXT NOT NULL,
    Title TEXT NOT NULL,
    Description TEXT NULL,
    ModuleId INTEGER NOT NULL,
    AssigneeId INTEGER NULL,
    Status INTEGER NOT NULL DEFAULT 0,
    Priority INTEGER NOT NULL DEFAULT 1,
    Size INTEGER NOT NULL DEFAULT 1,
    BranchName TEXT NULL,
    PullRequestUrl TEXT NULL,
    AgentAssisted INTEGER NOT NULL DEFAULT 0,
    StartedAt TEXT NULL,
    DueDate TEXT NULL,
    CompletedAt TEXT NULL,
    CreatedBy TEXT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    CONSTRAINT FK_DevTasks_DevModules FOREIGN KEY (ModuleId)
        REFERENCES DevModules (Id) ON DELETE NO ACTION,
    CONSTRAINT FK_DevTasks_DevMembers_Assignee FOREIGN KEY (AssigneeId)
        REFERENCES DevMembers (Id) ON DELETE SET NULL
);");
        await SafeAsync(db, @"CREATE INDEX IF NOT EXISTS IX_DevTasks_Status ON DevTasks (Status);");
        await SafeAsync(db, @"CREATE INDEX IF NOT EXISTS IX_DevTasks_ModuleId ON DevTasks (ModuleId);");
        await SafeAsync(db, @"CREATE INDEX IF NOT EXISTS IX_DevTasks_AssigneeId ON DevTasks (AssigneeId);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DevTaskLogs (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    TaskId INTEGER NOT NULL,
    MemberId INTEGER NULL,
    Action INTEGER NOT NULL DEFAULT 0,
    Note TEXT NULL,
    FromStatus INTEGER NULL,
    ToStatus INTEGER NULL,
    CommitSha TEXT NULL,
    At TEXT NOT NULL,
    CONSTRAINT FK_DevTaskLogs_DevTasks FOREIGN KEY (TaskId)
        REFERENCES DevTasks (Id) ON DELETE CASCADE,
    CONSTRAINT FK_DevTaskLogs_DevMembers FOREIGN KEY (MemberId)
        REFERENCES DevMembers (Id) ON DELETE SET NULL
);");
        await SafeAsync(db, @"CREATE INDEX IF NOT EXISTS IX_DevTaskLogs_TaskId ON DevTaskLogs (TaskId);");
        await SafeAsync(db, @"CREATE INDEX IF NOT EXISTS IX_DevTaskLogs_At ON DevTaskLogs (At);");
        await SafeAsync(db, @"CREATE INDEX IF NOT EXISTS IX_DevTaskLogs_MemberId ON DevTaskLogs (MemberId);");
    }

    /// <summary>اجرای امن — خطای «شیء تکراری/موجود» راه‌اندازی برنامه را متوقف نکند.</summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("already exists", StringComparison.OrdinalIgnoreCase)) return;

            LastError = m;
            Console.WriteLine();
            Console.WriteLine("============ خطای ساخت جداول مدیریت برنامه‌نویسان ============");
            Console.WriteLine($"[DB] DevTeamSchemaV1: {m}");
            Console.WriteLine("جدول‌های DevMembers / DevModules / DevTasks / DevTaskLogs ساخته نشدند؛");
            Console.WriteLine("ماژول «مدیریت برنامه‌نویسان» کار نخواهد کرد. این اسکریپت را با یک کاربر");
            Console.WriteLine("دارای مجوز DDL اجرا کنید:  inventory/sql/DevTeam.sql");
            Console.WriteLine("==============================================================");
            Console.WriteLine();
        }
    }
}
