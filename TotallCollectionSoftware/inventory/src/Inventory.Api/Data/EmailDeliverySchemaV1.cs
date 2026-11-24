using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>Additive, idempotent send ledger. Existing email rows are not assigned invented delivery outcomes.</summary>
public static class EmailDeliverySchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync(SqliteSql);
        }
        else if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync(SqlServerSql);
        }
    }

    public const string SqliteSql = """
        CREATE TABLE IF NOT EXISTS Oto_EmailSendAttempts (
          Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          RequestId TEXT NOT NULL, RequestHash TEXT NOT NULL, GuardKey TEXT NULL, PreviousRequestId TEXT NULL,
          RequestedByUserId INTEGER NOT NULL, EmailAccountId INTEGER NOT NULL,
          LetterSourceId INTEGER NULL, SentId INTEGER NULL,
          AccountAddress TEXT NOT NULL, Recipients TEXT NOT NULL, Subject TEXT NOT NULL,
          Status INTEGER NOT NULL, Stage TEXT NOT NULL, Code TEXT NOT NULL, Message TEXT NOT NULL,
          TechnicalType TEXT NULL, SmtpStatusCode INTEGER NULL, MessageId TEXT NOT NULL,
          StartedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL,
          AcceptedAtUtc TEXT NULL, CompletedAtUtc TEXT NULL, EventsJson TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS UX_EmailSendAttempt_Request ON Oto_EmailSendAttempts (RequestId);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_EmailSendAttempt_Guard ON Oto_EmailSendAttempts (GuardKey) WHERE GuardKey IS NOT NULL;
        CREATE INDEX IF NOT EXISTS IX_EmailSendAttempt_Account_Date ON Oto_EmailSendAttempts (EmailAccountId, StartedAtUtc);
        CREATE INDEX IF NOT EXISTS IX_EmailSendAttempt_Letter_Date ON Oto_EmailSendAttempts (LetterSourceId, StartedAtUtc);
        CREATE INDEX IF NOT EXISTS IX_EmailSendAttempt_Sent ON Oto_EmailSendAttempts (SentId);
        """;

    public const string SqlServerSql = """
        IF OBJECT_ID(N'dbo.Oto_EmailSendAttempts', N'U') IS NULL
        BEGIN
          CREATE TABLE dbo.Oto_EmailSendAttempts (
            Id bigint NOT NULL IDENTITY(1,1) CONSTRAINT PK_Oto_EmailSendAttempts PRIMARY KEY,
            RequestId uniqueidentifier NOT NULL, RequestHash nvarchar(64) NOT NULL,
            GuardKey nvarchar(64) NULL, PreviousRequestId uniqueidentifier NULL,
            RequestedByUserId int NOT NULL, EmailAccountId int NOT NULL,
            LetterSourceId int NULL, SentId int NULL,
            AccountAddress nvarchar(250) NOT NULL, Recipients nvarchar(1000) NOT NULL, Subject nvarchar(500) NOT NULL,
            Status int NOT NULL, Stage nvarchar(40) NOT NULL, Code nvarchar(64) NOT NULL, Message nvarchar(1000) NOT NULL,
            TechnicalType nvarchar(200) NULL, SmtpStatusCode int NULL, MessageId nvarchar(300) NOT NULL,
            StartedAtUtc datetime2 NOT NULL, UpdatedAtUtc datetime2 NOT NULL,
            AcceptedAtUtc datetime2 NULL, CompletedAtUtc datetime2 NULL, EventsJson nvarchar(max) NOT NULL
          );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Oto_EmailSendAttempts') AND name = N'UX_EmailSendAttempt_Request')
          CREATE UNIQUE INDEX UX_EmailSendAttempt_Request ON dbo.Oto_EmailSendAttempts (RequestId);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Oto_EmailSendAttempts') AND name = N'UX_EmailSendAttempt_Guard')
          CREATE UNIQUE INDEX UX_EmailSendAttempt_Guard ON dbo.Oto_EmailSendAttempts (GuardKey) WHERE GuardKey IS NOT NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Oto_EmailSendAttempts') AND name = N'IX_EmailSendAttempt_Account_Date')
          CREATE INDEX IX_EmailSendAttempt_Account_Date ON dbo.Oto_EmailSendAttempts (EmailAccountId, StartedAtUtc);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Oto_EmailSendAttempts') AND name = N'IX_EmailSendAttempt_Letter_Date')
          CREATE INDEX IX_EmailSendAttempt_Letter_Date ON dbo.Oto_EmailSendAttempts (LetterSourceId, StartedAtUtc);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Oto_EmailSendAttempts') AND name = N'IX_EmailSendAttempt_Sent')
          CREATE INDEX IX_EmailSendAttempt_Sent ON dbo.Oto_EmailSendAttempts (SentId);
        """;
}
