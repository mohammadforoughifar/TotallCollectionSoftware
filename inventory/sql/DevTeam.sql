-- =====================================================================
--  ماژول «مدیریت برنامه‌نویسان» (DevTeam) — اسکریپت آمادهٔ SQL Server
--
--  برنامه این جدول‌ها را هنگام راه‌اندازی خودش می‌سازد (DevTeamSchemaV1).
--  این فایل فقط برای وقتی لازم است که حساب کاربری سرویس مجوز DDL نداشته
--  باشد؛ در آن حالت DBA این اسکریپت را یک‌بار در SSMS اجرا می‌کند.
--
--  اجرای این اسکریپت ایمن و تکرارپذیر (idempotent) است.
-- =====================================================================
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.DevMembers', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DevMembers](
        [Id]           int            NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [FullName]     nvarchar(120)  NOT NULL,
        [GithubHandle] nvarchar(60)   NULL,
        [Email]        nvarchar(160)  NULL,
        [Phone]        nvarchar(40)   NULL,
        [Role]         int            NOT NULL DEFAULT(0),
        [IsActive]     bit            NOT NULL DEFAULT(1),
        [ColorHex]     nvarchar(9)    NOT NULL DEFAULT(N'#6c757d'),
        [Note]         nvarchar(500)  NULL,
        [CreatedAt]    datetime2      NOT NULL DEFAULT(SYSDATETIME())
    );
    CREATE INDEX [IX_DevMembers_IsActive] ON [dbo].[DevMembers] ([IsActive]);
    PRINT N'✔ جدول DevMembers ساخته شد.';
END
ELSE PRINT N'– جدول DevMembers از قبل وجود دارد.';
GO

IF OBJECT_ID(N'dbo.DevModules', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DevModules](
        [Id]           int             NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Key]          nvarchar(60)    NOT NULL,
        [Title]        nvarchar(120)   NOT NULL,
        [OwnerId]      int             NULL,
        [Icon]         nvarchar(60)    NULL,
        [ColorHex]     nvarchar(9)     NOT NULL DEFAULT(N'#0d6efd'),
        [SortOrder]    int             NOT NULL DEFAULT(0),
        [IsActive]     bit             NOT NULL DEFAULT(1),
        [RepoPaths]    nvarchar(1000)  NULL,
        [ServiceLines] int             NOT NULL DEFAULT(0),
        [PageCount]    int             NOT NULL DEFAULT(0),
        [Note]         nvarchar(500)   NULL,
        [CreatedAt]    datetime2       NOT NULL DEFAULT(SYSDATETIME()),
        CONSTRAINT [FK_DevModules_DevMembers_Owner] FOREIGN KEY ([OwnerId])
            REFERENCES [dbo].[DevMembers] ([Id]) ON DELETE SET NULL
    );
    CREATE UNIQUE INDEX [UX_DevModules_Key] ON [dbo].[DevModules] ([Key]);
    CREATE INDEX [IX_DevModules_OwnerId] ON [dbo].[DevModules] ([OwnerId]);
    PRINT N'✔ جدول DevModules ساخته شد.';
END
ELSE PRINT N'– جدول DevModules از قبل وجود دارد.';
GO

IF OBJECT_ID(N'dbo.DevTasks', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DevTasks](
        [Id]             int             NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Number]         nvarchar(30)    NOT NULL,
        [Title]          nvarchar(200)   NOT NULL,
        [Description]    nvarchar(2000)  NULL,
        [ModuleId]       int             NOT NULL,
        [AssigneeId]     int             NULL,
        [Status]         int             NOT NULL DEFAULT(0),
        [Priority]       int             NOT NULL DEFAULT(1),
        [Size]           int             NOT NULL DEFAULT(1),
        [BranchName]     nvarchar(160)   NULL,
        [PullRequestUrl] nvarchar(300)   NULL,
        [AgentAssisted]  bit             NOT NULL DEFAULT(0),
        [StartedAt]      datetime2       NULL,
        [DueDate]        datetime2       NULL,
        [CompletedAt]    datetime2       NULL,
        [CreatedBy]      nvarchar(120)   NULL,
        [CreatedAt]      datetime2       NOT NULL DEFAULT(SYSDATETIME()),
        [UpdatedAt]      datetime2       NOT NULL DEFAULT(SYSDATETIME()),
        -- عمداً NO ACTION: ماژول دارای آیتم کاری نباید آبشاری حذف شود.
        CONSTRAINT [FK_DevTasks_DevModules] FOREIGN KEY ([ModuleId])
            REFERENCES [dbo].[DevModules] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DevTasks_DevMembers_Assignee] FOREIGN KEY ([AssigneeId])
            REFERENCES [dbo].[DevMembers] ([Id]) ON DELETE SET NULL
    );
    CREATE INDEX [IX_DevTasks_Status]     ON [dbo].[DevTasks] ([Status]);
    CREATE INDEX [IX_DevTasks_ModuleId]   ON [dbo].[DevTasks] ([ModuleId]);
    CREATE INDEX [IX_DevTasks_AssigneeId] ON [dbo].[DevTasks] ([AssigneeId]);
    PRINT N'✔ جدول DevTasks ساخته شد.';
END
ELSE PRINT N'– جدول DevTasks از قبل وجود دارد.';
GO

IF OBJECT_ID(N'dbo.DevTaskLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DevTaskLogs](
        [Id]         int             NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [TaskId]     int             NOT NULL,
        [MemberId]   int             NULL,
        [Action]     int             NOT NULL DEFAULT(0),
        [Note]       nvarchar(1000)  NULL,
        [FromStatus] int             NULL,
        [ToStatus]   int             NULL,
        [CommitSha]  nvarchar(60)    NULL,
        [At]         datetime2       NOT NULL DEFAULT(SYSDATETIME()),
        CONSTRAINT [FK_DevTaskLogs_DevTasks] FOREIGN KEY ([TaskId])
            REFERENCES [dbo].[DevTasks] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_DevTaskLogs_DevMembers] FOREIGN KEY ([MemberId])
            REFERENCES [dbo].[DevMembers] ([Id]) ON DELETE SET NULL
    );
    CREATE INDEX [IX_DevTaskLogs_TaskId]   ON [dbo].[DevTaskLogs] ([TaskId]);
    CREATE INDEX [IX_DevTaskLogs_At]       ON [dbo].[DevTaskLogs] ([At]);
    CREATE INDEX [IX_DevTaskLogs_MemberId] ON [dbo].[DevTaskLogs] ([MemberId]);
    PRINT N'✔ جدول DevTaskLogs ساخته شد.';
END
ELSE PRINT N'– جدول DevTaskLogs از قبل وجود دارد.';
GO

PRINT N'— اسکریپت ماژول مدیریت برنامه‌نویسان پایان یافت.';
GO
