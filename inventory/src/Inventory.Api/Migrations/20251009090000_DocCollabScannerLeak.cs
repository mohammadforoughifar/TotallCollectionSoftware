using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    public partial class DocCollabScannerLeak : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.DocComments','U') IS NULL
CREATE TABLE dbo.DocComments(
  Id INT IDENTITY(1,1) PRIMARY KEY,
  DocumentId INT NOT NULL FOREIGN KEY REFERENCES dbo.Documents(Id) ON DELETE CASCADE,
  VersionId INT NULL,
  ParentId INT NULL FOREIGN KEY REFERENCES dbo.DocComments(Id),
  AuthorUserId INT NOT NULL,
  AuthorName NVARCHAR(150) NOT NULL,
  Body NVARCHAR(2000) NOT NULL,
  CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
  EditedAt DATETIME2 NULL,
  IsResolved BIT NOT NULL DEFAULT 0,
  ResolvedByUserId INT NULL,
  ResolvedAt DATETIME2 NULL,
  IsDeleted BIT NOT NULL DEFAULT 0
);
IF OBJECT_ID('dbo.DocCommentMentions','U') IS NULL
CREATE TABLE dbo.DocCommentMentions(
  Id INT IDENTITY(1,1) PRIMARY KEY,
  CommentId INT NOT NULL FOREIGN KEY REFERENCES dbo.DocComments(Id) ON DELETE CASCADE,
  MentionedUserId INT NOT NULL,
  MentionedUserName NVARCHAR(150) NOT NULL
);
IF OBJECT_ID('dbo.DocLeakTraces','U') IS NULL
CREATE TABLE dbo.DocLeakTraces(
  Id INT IDENTITY(1,1) PRIMARY KEY,
  DocumentId INT NOT NULL FOREIGN KEY REFERENCES dbo.Documents(Id) ON DELETE CASCADE,
  VersionId INT NULL,
  UserId INT NOT NULL,
  UserName NVARCHAR(150) NOT NULL,
  IpAddress NVARCHAR(60) NULL,
  VisibleWatermark NVARCHAR(300) NOT NULL,
  InvisiblePayload NVARCHAR(300) NOT NULL,
  FileHash NVARCHAR(64) NOT NULL DEFAULT '',
  CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
  [Action] NVARCHAR(30) NOT NULL DEFAULT 'Download'
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DocComments_DocumentId' AND object_id=OBJECT_ID('dbo.DocComments'))
  CREATE INDEX IX_DocComments_DocumentId ON dbo.DocComments(DocumentId, CreatedAt);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DocLeakTraces_DocumentId' AND object_id=OBJECT_ID('dbo.DocLeakTraces'))
  CREATE INDEX IX_DocLeakTraces_DocumentId ON dbo.DocLeakTraces(DocumentId, CreatedAt);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.DocCommentMentions','U') IS NOT NULL DROP TABLE dbo.DocCommentMentions;
IF OBJECT_ID('dbo.DocComments','U') IS NOT NULL DROP TABLE dbo.DocComments;
IF OBJECT_ID('dbo.DocLeakTraces','U') IS NOT NULL DROP TABLE dbo.DocLeakTraces;
");
        }
    }
}
