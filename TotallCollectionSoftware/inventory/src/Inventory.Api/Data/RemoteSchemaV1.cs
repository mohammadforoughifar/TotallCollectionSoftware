using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>اسکیمای idempotent ماژول ریموت دسکتاپ — مثل BackupSchemaV1 بدون migration اجباری.</summary>
public static class RemoteSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite()) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);
    }

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS RemoteSettings (
    Id INTEGER PRIMARY KEY,
    Enabled INTEGER NOT NULL DEFAULT 1,
    ConsentTimeoutSeconds INTEGER NOT NULL DEFAULT 60,
    SessionIdleTimeoutMinutes INTEGER NOT NULL DEFAULT 10,
    FrameIntervalMs INTEGER NOT NULL DEFAULT 250,
    JpegQuality INTEGER NOT NULL DEFAULT 55,
    MaxWidth INTEGER NOT NULL DEFAULT 1280,
    UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
INSERT OR IGNORE INTO RemoteSettings (Id, Enabled, UpdatedAt) VALUES (1, 1, CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS RemoteDevices (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL DEFAULT '',
    CompanyCode TEXT NOT NULL DEFAULT '',
    DeviceKeyHash TEXT NOT NULL DEFAULT '',
    DeviceKeyHint TEXT NOT NULL DEFAULT '',
    Os TEXT NOT NULL DEFAULT '',
    AgentVersion TEXT NOT NULL DEFAULT '',
    IsOnline INTEGER NOT NULL DEFAULT 0,
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    LastSeenAt TEXT NULL,
    LastIp TEXT NULL,
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS RemoteSessions (
    Id TEXT PRIMARY KEY,
    DeviceId TEXT NOT NULL DEFAULT '',
    TechnicianUserId INTEGER NOT NULL DEFAULT 0,
    TechnicianName TEXT NOT NULL DEFAULT '',
    Status TEXT NOT NULL DEFAULT 'Pending',
    AllowControl INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ExpiresAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    StartedAt TEXT NULL,
    EndedAt TEXT NULL,
    LastActivityAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    EndReason TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_RemoteSessions_DeviceId ON RemoteSessions(DeviceId);
CREATE INDEX IF NOT EXISTS IX_RemoteSessions_Status ON RemoteSessions(Status);
CREATE TABLE IF NOT EXISTS RemoteAuditLogs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId TEXT NULL,
    DeviceId TEXT NOT NULL DEFAULT '',
    UserId INTEGER NULL,
    Action TEXT NOT NULL DEFAULT '',
    Detail TEXT NULL,
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS IX_RemoteAuditLogs_SessionId ON RemoteAuditLogs(SessionId);
");
    }

    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.RemoteSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RemoteSettings (
        Id INT NOT NULL CONSTRAINT PK_RemoteSettings PRIMARY KEY,
        Enabled BIT NOT NULL CONSTRAINT DF_RemoteSettings_Enabled DEFAULT(1),
        ConsentTimeoutSeconds INT NOT NULL CONSTRAINT DF_RemoteSettings_Consent DEFAULT(60),
        SessionIdleTimeoutMinutes INT NOT NULL CONSTRAINT DF_RemoteSettings_Idle DEFAULT(10),
        FrameIntervalMs INT NOT NULL CONSTRAINT DF_RemoteSettings_Frame DEFAULT(250),
        JpegQuality INT NOT NULL CONSTRAINT DF_RemoteSettings_Quality DEFAULT(55),
        MaxWidth INT NOT NULL CONSTRAINT DF_RemoteSettings_MaxWidth DEFAULT(1280),
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_RemoteSettings_Updated DEFAULT(GETDATE())
    );
    INSERT INTO dbo.RemoteSettings (Id, Enabled) VALUES (1, 1);
END
IF OBJECT_ID(N'dbo.RemoteDevices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RemoteDevices (
        Id NVARCHAR(32) NOT NULL CONSTRAINT PK_RemoteDevices PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL CONSTRAINT DF_RemoteDevices_Name DEFAULT(''),
        CompanyCode NVARCHAR(50) NOT NULL CONSTRAINT DF_RemoteDevices_Company DEFAULT(''),
        DeviceKeyHash NVARCHAR(64) NOT NULL CONSTRAINT DF_RemoteDevices_Hash DEFAULT(''),
        DeviceKeyHint NVARCHAR(20) NOT NULL CONSTRAINT DF_RemoteDevices_Hint DEFAULT(''),
        Os NVARCHAR(100) NOT NULL CONSTRAINT DF_RemoteDevices_Os DEFAULT(''),
        AgentVersion NVARCHAR(50) NOT NULL CONSTRAINT DF_RemoteDevices_Version DEFAULT(''),
        IsOnline BIT NOT NULL CONSTRAINT DF_RemoteDevices_Online DEFAULT(0),
        IsEnabled BIT NOT NULL CONSTRAINT DF_RemoteDevices_Enabled DEFAULT(1),
        LastSeenAt DATETIME2 NULL,
        LastIp NVARCHAR(64) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_RemoteDevices_Created DEFAULT(GETDATE()),
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_RemoteDevices_Updated DEFAULT(GETDATE())
    );
    CREATE INDEX IX_RemoteDevices_CompanyCode ON dbo.RemoteDevices(CompanyCode);
END
IF OBJECT_ID(N'dbo.RemoteSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RemoteSessions (
        Id NVARCHAR(32) NOT NULL CONSTRAINT PK_RemoteSessions PRIMARY KEY,
        DeviceId NVARCHAR(32) NOT NULL CONSTRAINT DF_RemoteSessions_Device DEFAULT(''),
        TechnicianUserId INT NOT NULL CONSTRAINT DF_RemoteSessions_User DEFAULT(0),
        TechnicianName NVARCHAR(200) NOT NULL CONSTRAINT DF_RemoteSessions_Tech DEFAULT(''),
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_RemoteSessions_Status DEFAULT('Pending'),
        AllowControl BIT NOT NULL CONSTRAINT DF_RemoteSessions_Control DEFAULT(0),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_RemoteSessions_Created DEFAULT(GETDATE()),
        ExpiresAt DATETIME2 NOT NULL CONSTRAINT DF_RemoteSessions_Expires DEFAULT(GETDATE()),
        StartedAt DATETIME2 NULL,
        EndedAt DATETIME2 NULL,
        LastActivityAt DATETIME2 NOT NULL CONSTRAINT DF_RemoteSessions_Activity DEFAULT(GETDATE()),
        EndReason NVARCHAR(500) NULL
    );
    CREATE INDEX IX_RemoteSessions_DeviceId ON dbo.RemoteSessions(DeviceId);
    CREATE INDEX IX_RemoteSessions_Status ON dbo.RemoteSessions(Status);
END
IF OBJECT_ID(N'dbo.RemoteAuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RemoteAuditLogs (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RemoteAuditLogs PRIMARY KEY,
        SessionId NVARCHAR(32) NULL,
        DeviceId NVARCHAR(32) NOT NULL CONSTRAINT DF_RemoteAuditLogs_Device DEFAULT(''),
        UserId INT NULL,
        Action NVARCHAR(50) NOT NULL CONSTRAINT DF_RemoteAuditLogs_Action DEFAULT(''),
        Detail NVARCHAR(1000) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_RemoteAuditLogs_Created DEFAULT(GETDATE())
    );
    CREATE INDEX IX_RemoteAuditLogs_SessionId ON dbo.RemoteAuditLogs(SessionId);
END
");
    }
}
