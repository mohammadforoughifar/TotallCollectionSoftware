using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // هرگز PK_Users را دوباره نساز — حتی اگر جدول Users با اسکیمای دیگر موجود باشد.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.tables WHERE [name] = N'Users')
   OR EXISTS (SELECT 1 FROM sys.key_constraints WHERE [name] = N'PK_Users')
   OR EXISTS (SELECT 1 FROM sys.objects WHERE [name] = N'PK_Users')
BEGIN
    IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
       AND NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [name] = N'IX_Users_Username' AND [object_id] = OBJECT_ID(N'dbo.Users')
       )
    BEGIN
        CREATE UNIQUE INDEX [IX_Users_Username] ON [dbo].[Users] ([Username]);
    END
END
ELSE
BEGIN
    CREATE TABLE [dbo].[Users] (
        [Id] int NOT NULL IDENTITY,
        [Username] nvarchar(100) NOT NULL,
        [PasswordHash] nvarchar(200) NOT NULL,
        [Role] nvarchar(20) NOT NULL,
        [ReferrerId] int NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_Users_Username] ON [dbo].[Users] ([Username]);
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
    DROP TABLE [dbo].[Users];
");
        }
    }
}
