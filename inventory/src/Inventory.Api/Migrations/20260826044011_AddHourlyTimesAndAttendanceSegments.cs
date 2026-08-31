using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// این مایگریشن ای‌دی‌ام‌پوتنت (idempotent) است: اگر اشیاء قبلاً — نیمه‌کاره یا دستی — ساخته شده باشند،
    /// دوباره ساخته نمی‌شوند و مایگریشن بدون خطا به پایان می‌رسد و در __EFMigrationsHistory ثبت می‌شود.
    /// (مخصوص SQL Server)
    /// </remarks>
    public partial class AddHourlyTimesAndAttendanceSegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- Hours: int -> float (فقط اگر هنوز int باشد)
IF EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID(N'[dbo].[LeaveRequests]')
      AND c.name = N'Hours' AND t.name IN ('int', 'smallint', 'tinyint')
)
    ALTER TABLE [dbo].[LeaveRequests] ALTER COLUMN [Hours] float NOT NULL;

-- ستون‌های ساعتی (فقط اگر نباشند)
IF COL_LENGTH(N'[dbo].[LeaveRequests]', N'EndTime') IS NULL
    ALTER TABLE [dbo].[LeaveRequests] ADD [EndTime] time NULL;

IF COL_LENGTH(N'[dbo].[LeaveRequests]', N'StartTime') IS NULL
    ALTER TABLE [dbo].[LeaveRequests] ADD [StartTime] time NULL;

IF COL_LENGTH(N'[dbo].[AttendanceRecords]', N'CoveredGapMinutes') IS NULL
    ALTER TABLE [dbo].[AttendanceRecords] ADD [CoveredGapMinutes] int NOT NULL CONSTRAINT [DF_AttendanceRecords_CoveredGapMinutes] DEFAULT 0;

-- جدول بازه‌های ورود/خروج (فقط اگر نباشد)
IF OBJECT_ID(N'[dbo].[AttendanceSegments]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AttendanceSegments] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [UserName] nvarchar(150) NOT NULL,
        [WorkDate] datetime2 NOT NULL,
        [Seq] int NOT NULL,
        [EnterAt] datetime2 NULL,
        [EnterIp] nvarchar(max) NULL,
        [ExitAt] datetime2 NULL,
        [ExitIp] nvarchar(max) NULL,
        [EnterStatus] nvarchar(20) NULL,
        [LateMinutes] int NOT NULL,
        [ExitCovered] bit NOT NULL,
        [LinkedLeaveRequestId] int NULL,
        [LinkedLeaveNumber] nvarchar(30) NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AttendanceSegments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AttendanceSegments_LeaveRequests_LinkedLeaveRequestId]
            FOREIGN KEY ([LinkedLeaveRequestId]) REFERENCES [dbo].[LeaveRequests] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_AttendanceSegments_LinkedLeaveRequestId'
      AND object_id = OBJECT_ID(N'[dbo].[AttendanceSegments]')
)
    CREATE INDEX [IX_AttendanceSegments_LinkedLeaveRequestId]
        ON [dbo].[AttendanceSegments] ([LinkedLeaveRequestId]);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_AttendanceSegments_UserId_WorkDate_Seq'
      AND object_id = OBJECT_ID(N'[dbo].[AttendanceSegments]')
)
    CREATE UNIQUE INDEX [IX_AttendanceSegments_UserId_WorkDate_Seq]
        ON [dbo].[AttendanceSegments] ([UserId], [WorkDate], [Seq]);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[AttendanceSegments]', N'U') IS NOT NULL
    DROP TABLE [dbo].[AttendanceSegments];

IF COL_LENGTH(N'[dbo].[LeaveRequests]', N'EndTime') IS NOT NULL
    ALTER TABLE [dbo].[LeaveRequests] DROP COLUMN [EndTime];

IF COL_LENGTH(N'[dbo].[LeaveRequests]', N'StartTime') IS NOT NULL
    ALTER TABLE [dbo].[LeaveRequests] DROP COLUMN [StartTime];

IF COL_LENGTH(N'[dbo].[AttendanceRecords]', N'CoveredGapMinutes') IS NOT NULL
    ALTER TABLE [dbo].[AttendanceRecords] DROP COLUMN [CoveredGapMinutes];

IF EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID(N'[dbo].[LeaveRequests]')
      AND c.name = N'Hours' AND t.name = 'float'
)
    ALTER TABLE [dbo].[LeaveRequests] ALTER COLUMN [Hours] int NOT NULL;
");
        }
    }
}
