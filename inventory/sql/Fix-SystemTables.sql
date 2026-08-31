/* ============================================================
   اصلاح جدول‌های سیستم — InventoryDb (SQL Server)
   علت: جدول SystemUsers با شمای قدیمی ساخته شده و ستون‌های
        FirstName / LastName / StaffNumber را ندارد:
        «Invalid column name 'FirstName'»
   این اسکریپت هر ۴ جدول سیستم را با شمای صحیحِ تطبیق‌یافته با
   Entity های فعلی (SystemCompany, SystemDepartment, SystemUser, SystemInfo)
   از نو می‌سازد.
   ⚠ توجه: داده‌های داخل این ۴ جدول (شرکت‌ها/دپارتمان‌ها/کاربران سیستم/
   شناسه سیستم) پاک می‌شود — اگر داده‌ی مهم دارید قبل از اجرا بکاپ بگیرید.
   اجرا: در SSMS یا Azure Data Studio، روی دیتابیس InventoryDb
   ============================================================ */

USE InventoryDb;
GO

-- حذف جدول‌های قدیمی/ناسازگار (به ترتیب وابستگی FK)
IF OBJECT_ID('dbo.SystemInfos', 'U')    IS NOT NULL DROP TABLE dbo.SystemInfos;
IF OBJECT_ID('dbo.SystemUsers', 'U')    IS NOT NULL DROP TABLE dbo.SystemUsers;
IF OBJECT_ID('dbo.SystemDepartments', 'U') IS NOT NULL DROP TABLE dbo.SystemDepartments;
IF OBJECT_ID('dbo.SystemCompanies', 'U') IS NOT NULL DROP TABLE dbo.SystemCompanies;
GO

-- شرکت‌ها
CREATE TABLE dbo.SystemCompanies (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    Name      NVARCHAR(200) NOT NULL CONSTRAINT DF_SysCo_Name    DEFAULT N'',
    Code      NVARCHAR(100) NULL,
    Phone     NVARCHAR(50)  NULL,
    Address   NVARCHAR(250) NULL,
    IsActive  BIT           NOT NULL CONSTRAINT DF_SysCo_Active  DEFAULT 1,
    CreatedAt DATETIME2     NOT NULL CONSTRAINT DF_SysCo_Created DEFAULT GETDATE()
);
GO

-- دپارتمان‌ها
CREATE TABLE dbo.SystemDepartments (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    Name      NVARCHAR(150) NOT NULL CONSTRAINT DF_SysDept_Name    DEFAULT N'',
    CompanyId INT NULL CONSTRAINT FK_SystemDepartments_SystemCompanies
              REFERENCES dbo.SystemCompanies(Id),
    IsActive  BIT       NOT NULL CONSTRAINT DF_SysDept_Active  DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_SysDept_Created DEFAULT GETDATE()
);
GO

-- کاربران سیستم (با ستون‌های FirstName / LastName / StaffNumber)
CREATE TABLE dbo.SystemUsers (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    FirstName    NVARCHAR(100) NOT NULL CONSTRAINT DF_SysUser_First   DEFAULT N'',
    LastName     NVARCHAR(100) NOT NULL CONSTRAINT DF_SysUser_Last    DEFAULT N'',
    StaffNumber  NVARCHAR(50)  NOT NULL CONSTRAINT DF_SysUser_Staff   DEFAULT N'',
    DepartmentId INT NULL CONSTRAINT FK_SystemUsers_SystemDepartments
                 REFERENCES dbo.SystemDepartments(Id),
    CompanyId    INT NULL CONSTRAINT FK_SystemUsers_SystemCompanies
                 REFERENCES dbo.SystemCompanies(Id),
    Role         NVARCHAR(20) NULL,
    IsActive     BIT           NOT NULL CONSTRAINT DF_SysUser_Active  DEFAULT 1,
    CreatedAt    DATETIME2     NOT NULL CONSTRAINT DF_SysUser_Created DEFAULT GETDATE()
);
GO

-- شناسه‌های سیستم (SystemInfo)
CREATE TABLE dbo.SystemInfos (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    AgentId      NVARCHAR(MAX) NOT NULL CONSTRAINT DF_SysInfo_Agent DEFAULT N'',
    Motherboard  NVARCHAR(MAX) NULL,
    Cpu          NVARCHAR(MAX) NULL,
    Ram          NVARCHAR(MAX) NULL,
    HardDisk     NVARCHAR(MAX) NULL,
    Graphics     NVARCHAR(MAX) NULL,
    Monitor      NVARCHAR(MAX) NULL,
    IsApproved   BIT           NOT NULL CONSTRAINT DF_SysInfo_Approved DEFAULT 0,
    OsName       NVARCHAR(MAX) NULL,
    TotalRamGb   INT           NOT NULL CONSTRAINT DF_SysInfo_Ram      DEFAULT 0,
    CompanyId    INT NULL CONSTRAINT FK_SystemInfos_SystemCompanies
                 REFERENCES dbo.SystemCompanies(Id),
    DepartmentId INT NULL CONSTRAINT FK_SystemInfos_SystemDepartments
                 REFERENCES dbo.SystemDepartments(Id),
    UserId       INT NULL CONSTRAINT FK_SystemInfos_SystemUsers
                 REFERENCES dbo.SystemUsers(Id),
    ReceivedAt   DATETIME2 NOT NULL CONSTRAINT DF_SysInfo_Received DEFAULT GETDATE()
);
GO

PRINT N'✔ جدول‌های سیستم با موفقیت بازسازی شدند.';
GO
