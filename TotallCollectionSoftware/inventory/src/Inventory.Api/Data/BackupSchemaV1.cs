using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>جدول‌های بک‌آپ خودکار — idempotent برای SQL Server و SQLite.</summary>
public static class BackupSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    private static Task EnsureSqlServerAsync(AppDbContext db)
    {
        Run(db, @"
IF OBJECT_ID(N'dbo.BackupSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BackupSettings(
        Id int NOT NULL CONSTRAINT PK_BackupSettings PRIMARY KEY,
        Enabled bit NOT NULL CONSTRAINT DF_BackupSettings_Enabled DEFAULT(0),
        CentralServerUrl nvarchar(300) NULL,
        CompanyCode nvarchar(50) NULL,
        CompanyName nvarchar(200) NULL,
        ApiKeyProtected nvarchar(2000) NULL,
        TriggerOnLastSession bit NOT NULL CONSTRAINT DF_BackupSettings_LastSession DEFAULT(1),
        TriggerDaily bit NOT NULL CONSTRAINT DF_BackupSettings_Daily DEFAULT(1),
        DailyTime nvarchar(5) NOT NULL CONSTRAINT DF_BackupSettings_DailyTime DEFAULT('23:00'),
        LastSessionGraceSeconds int NOT NULL CONSTRAINT DF_BackupSettings_Grace DEFAULT(90),
        CooldownMinutes int NOT NULL CONSTRAINT DF_BackupSettings_Cooldown DEFAULT(15),
        LocalBackupPath nvarchar(500) NULL,
        LocalKeepCount int NOT NULL CONSTRAINT DF_BackupSettings_Keep DEFAULT(5),
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_BackupSettings_UpdatedAt DEFAULT(GETDATE()),
        LastDailyRunDate datetime2 NULL,
        LastAutoTriggerAt datetime2 NULL
    );
    INSERT INTO dbo.BackupSettings (Id) VALUES (1);
END", "BackupSettings");

        Run(db, @"
IF OBJECT_ID(N'dbo.BackupBrowserSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BackupBrowserSessions(
        Id nvarchar(32) NOT NULL CONSTRAINT PK_BackupBrowserSessions PRIMARY KEY,
        UserId int NOT NULL,
        UserName nvarchar(150) NOT NULL,
        StartedAt datetime2 NOT NULL,
        LastHeartbeatAt datetime2 NOT NULL,
        ClosedAt datetime2 NULL,
        CloseReason nvarchar(30) NULL,
        UserAgent nvarchar(300) NULL
    );
    CREATE INDEX IX_BackupBrowserSessions_Active ON dbo.BackupBrowserSessions (ClosedAt, LastHeartbeatAt);
END", "BackupBrowserSessions");

        Run(db, @"
IF OBJECT_ID(N'dbo.BackupJobs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BackupJobs(
        Id nvarchar(32) NOT NULL CONSTRAINT PK_BackupJobs PRIMARY KEY,
        ExternalId nvarchar(32) NOT NULL,
        Reason nvarchar(20) NOT NULL,
        Status nvarchar(20) NOT NULL,
        DatabaseName nvarchar(128) NOT NULL,
        FileName nvarchar(260) NULL,
        FilePath nvarchar(1000) NULL,
        SizeBytes bigint NOT NULL CONSTRAINT DF_BackupJobs_Size DEFAULT(0),
        Sha256 nvarchar(64) NULL,
        SqlVersion nvarchar(100) NULL,
        SqlEdition nvarchar(100) NULL,
        Compression nvarchar(20) NULL,
        CreatedAt datetime2 NOT NULL,
        StartedAt datetime2 NULL,
        CompletedAt datetime2 NULL,
        SentAt datetime2 NULL,
        Attempts int NOT NULL CONSTRAINT DF_BackupJobs_Attempts DEFAULT(0),
        NextAttemptAt datetime2 NULL,
        LastError nvarchar(2000) NULL
    );
    CREATE UNIQUE INDEX IX_BackupJobs_ExternalId ON dbo.BackupJobs (ExternalId);
    CREATE INDEX IX_BackupJobs_Status ON dbo.BackupJobs (Status, NextAttemptAt);
END", "BackupJobs");

        Run(db, @"
IF OBJECT_ID(N'dbo.CentralBackupCompanies', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CentralBackupCompanies(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CentralBackupCompanies PRIMARY KEY,
        Code nvarchar(50) NOT NULL,
        Name nvarchar(200) NOT NULL,
        ApiKeyHash nvarchar(100) NOT NULL,
        ApiKeyHint nvarchar(20) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_CentralBackupCompanies_Active DEFAULT(1),
        RetentionDays int NOT NULL CONSTRAINT DF_CentralBackupCompanies_Retention DEFAULT(30),
        MaxStorageMb bigint NOT NULL CONSTRAINT DF_CentralBackupCompanies_MaxStorage DEFAULT(10240),
        Note nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_CentralBackupCompanies_Created DEFAULT(GETDATE()),
        KeyRotatedAt datetime2 NULL,
        LastSeenAt datetime2 NULL
    );
    CREATE UNIQUE INDEX IX_CentralBackupCompanies_Code ON dbo.CentralBackupCompanies (Code);
END", "CentralBackupCompanies");

        Run(db, @"
IF OBJECT_ID(N'dbo.CentralBackupReceipts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CentralBackupReceipts(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CentralBackupReceipts PRIMARY KEY,
        CompanyId int NOT NULL,
        ExternalId nvarchar(32) NOT NULL,
        DatabaseName nvarchar(128) NOT NULL,
        FileName nvarchar(260) NOT NULL,
        StoredPath nvarchar(1000) NOT NULL,
        SizeBytes bigint NOT NULL,
        Sha256 nvarchar(64) NOT NULL,
        Reason nvarchar(20) NOT NULL,
        SqlVersion nvarchar(100) NULL,
        SqlEdition nvarchar(100) NULL,
        ReceivedAt datetime2 NOT NULL,
        Status nvarchar(20) NOT NULL
    );
    CREATE UNIQUE INDEX IX_CentralBackupReceipts_CompanyExternal ON dbo.CentralBackupReceipts (CompanyId, ExternalId);
    CREATE INDEX IX_CentralBackupReceipts_Received ON dbo.CentralBackupReceipts (CompanyId, ReceivedAt);
END", "CentralBackupReceipts");
        return Task.CompletedTask;
    }

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        async Task Exec(string sql)
        {
            try { using var c = conn.CreateCommand(); c.CommandText = sql; await c.ExecuteNonQueryAsync(); }
            catch (Exception ex) { Console.WriteLine($"[DB] BackupSchemaV1 SQLite: {ex.Message}"); }
        }
        await Exec(@"
CREATE TABLE IF NOT EXISTS BackupSettings (
    Id INTEGER NOT NULL PRIMARY KEY,
    Enabled INTEGER NOT NULL DEFAULT 0,
    CentralServerUrl TEXT NULL,
    CompanyCode TEXT NULL,
    CompanyName TEXT NULL,
    ApiKeyProtected TEXT NULL,
    TriggerOnLastSession INTEGER NOT NULL DEFAULT 1,
    TriggerDaily INTEGER NOT NULL DEFAULT 1,
    DailyTime TEXT NOT NULL DEFAULT '23:00',
    LastSessionGraceSeconds INTEGER NOT NULL DEFAULT 90,
    CooldownMinutes INTEGER NOT NULL DEFAULT 15,
    LocalBackupPath TEXT NULL,
    LocalKeepCount INTEGER NOT NULL DEFAULT 5,
    UpdatedAt TEXT NOT NULL,
    LastDailyRunDate TEXT NULL,
    LastAutoTriggerAt TEXT NULL
);
INSERT OR IGNORE INTO BackupSettings (Id, UpdatedAt) VALUES (1, CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS BackupBrowserSessions (
    Id TEXT NOT NULL PRIMARY KEY,
    UserId INTEGER NOT NULL,
    UserName TEXT NOT NULL,
    StartedAt TEXT NOT NULL,
    LastHeartbeatAt TEXT NOT NULL,
    ClosedAt TEXT NULL,
    CloseReason TEXT NULL,
    UserAgent TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_BackupBrowserSessions_Active ON BackupBrowserSessions (ClosedAt, LastHeartbeatAt);
CREATE TABLE IF NOT EXISTS BackupJobs (
    Id TEXT NOT NULL PRIMARY KEY,
    ExternalId TEXT NOT NULL,
    Reason TEXT NOT NULL,
    Status TEXT NOT NULL,
    DatabaseName TEXT NOT NULL,
    FileName TEXT NULL,
    FilePath TEXT NULL,
    SizeBytes INTEGER NOT NULL DEFAULT 0,
    Sha256 TEXT NULL,
    SqlVersion TEXT NULL,
    SqlEdition TEXT NULL,
    Compression TEXT NULL,
    CreatedAt TEXT NOT NULL,
    StartedAt TEXT NULL,
    CompletedAt TEXT NULL,
    SentAt TEXT NULL,
    Attempts INTEGER NOT NULL DEFAULT 0,
    NextAttemptAt TEXT NULL,
    LastError TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_BackupJobs_ExternalId ON BackupJobs (ExternalId);
CREATE INDEX IF NOT EXISTS IX_BackupJobs_Status ON BackupJobs (Status, NextAttemptAt);
CREATE TABLE IF NOT EXISTS CentralBackupCompanies (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL,
    Name TEXT NOT NULL,
    ApiKeyHash TEXT NOT NULL,
    ApiKeyHint TEXT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    RetentionDays INTEGER NOT NULL DEFAULT 30,
    MaxStorageMb INTEGER NOT NULL DEFAULT 10240,
    Note TEXT NULL,
    CreatedAt TEXT NOT NULL,
    KeyRotatedAt TEXT NULL,
    LastSeenAt TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_CentralBackupCompanies_Code ON CentralBackupCompanies (Code);
CREATE TABLE IF NOT EXISTS CentralBackupReceipts (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NOT NULL,
    ExternalId TEXT NOT NULL,
    DatabaseName TEXT NOT NULL,
    FileName TEXT NOT NULL,
    StoredPath TEXT NOT NULL,
    SizeBytes INTEGER NOT NULL,
    Sha256 TEXT NOT NULL,
    Reason TEXT NOT NULL,
    SqlVersion TEXT NULL,
    SqlEdition TEXT NULL,
    ReceivedAt TEXT NOT NULL,
    Status TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_CentralBackupReceipts_CompanyExternal ON CentralBackupReceipts (CompanyId, ExternalId);
CREATE INDEX IF NOT EXISTS IX_CentralBackupReceipts_Received ON CentralBackupReceipts (CompanyId, ReceivedAt);");
    }

    private static void Run(AppDbContext db, string sql, string label)
    {
        try { db.Database.ExecuteSqlRaw(sql); }
        catch (Exception ex) { Console.WriteLine($"[DB] BackupSchemaV1: {label} → {ex.Message}"); }
    }
}
