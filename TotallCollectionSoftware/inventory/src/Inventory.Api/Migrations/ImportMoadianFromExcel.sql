/* ============================================================================
   اسکریپت Import فاکتورهای قدیمی سامانه مودیان به دیتابیس جدید (FacInvoice)
   ============================================================================

   مراحل اجرا:
   ۱. فایل اکسل را در یک جدول موقت (Staging) baraye SQL Server بارگذاری کنید
   ۲. این اسکریپت را در SSMS / Azure Data Studio اجرا کنید
   ۳. داده‌ها از Staging به FacInvoice / FacInvoiceLine / Party منتقل می‌شوند

   پیش‌نیاز:
   -Excel باید این ستونها را داشته باشد (با این نام‌های فارسی):
     نوع صورتحساب | وضعیت صورتحساب | وضعیت ارسال | تاریخ ثبت صورتحساب |
     شناسه مالیاتی | مبلغ کالا | تخفیف کالا | مالیات کالا | مبلغ نقد | مبلغ نسیه | مبلغ صورتحساب (ریال)

   ============================================================================ */

-- ============================================================================
-- مرحلہ ۱: ایجاد جدول موقت (Staging) برای داده‌های اکسل
-- ============================================================================
IF OBJECT_ID(N'dbo.Stg_MoadianInvoices', N'U') IS NOT NULL
    DROP TABLE dbo.Stg_MoadianInvoices;

CREATE TABLE dbo.Stg_MoadianInvoices (
    StgId INT IDENTITY(1,1) PRIMARY KEY,
    InvoiceType NVARCHAR(100) NULL,           -- نوع صورتحساب
    InvoiceStatus NVARCHAR(100) NULL,         -- وضعیت صورتحساب
    SendStatus NVARCHAR(100) NULL,            -- وضعیت ارسال
    InvoiceDate NVARCHAR(50) NULL,            -- تاریخ ثبت صورتحbass (متن اولیه)
    TaxId NVARCHAR(50) NULL,                  -- شناسه مالیاتی
    GoodsAmount DECIMAL(18,2) NULL DEFAULT 0, -- مبلغ کالا
    GoodsDiscount DECIMAL(18,2) NULL DEFAULT 0, -- تخفیف کالا
    GoodsVat DECIMAL(18,2) NULL DEFAULT 0,    -- مالیات کالا
    CashAmount DECIMAL(18,2) NULL DEFAULT 0,    -- مبلغ نقد
    CreditAmount DECIMAL(18,2) NULL DEFAULT 0,  -- مبلغ نسیه
    TotalAmount DECIMAL(18,2) NULL DEFAULT 0    -- مبلغ صورتحساب (ریال)
);

-- ============================================================================
-- مرحلہ ۲: بارگذاری فایل اکسل در جدول موقت (مثلاً از طریق SSMS Import Wizard)
-- ============================================================================
-- نکته: این بخش را به صورت دستی در SSMS انجام دهید:
-- Right-click on Database → Tasks → Import Data → انتخاب فایل اکسل → مقصد: Stg_MoadianInvoices

-- مثالی از دستور BULK INSERT (اگر فایل CSV است):
-- BULK INSERT dbo.Stg_MoadianInvoices
-- FROM 'C:\path\to\your\invoices.csv'
-- WITH (FIELDTERMINATOR = ',', ROWTERMINATOR = '0x0a', FIRSTROW = 2);

-- ============================================================================
-- مرحلہ ۳: ایجاد/获取 مشتریان (Party) بر اساس شناسه مالیاتی
-- ============================================================================
-- ابتدا مشتریانی که قبلاً وجود ندارند را ایجاد می‌کنیم
INSERT INTO dbo.Parties (Name, TaxId, Type, CompanyId, IsActive)
SELECT DISTINCT
    'مشتری ' + ISNULL(TaxId, 'UNKNOWN') AS Name,
    ISNULL(TaxId, 'UNKNOWN') AS TaxId,
    0 AS Type, -- 0 = Customer
    1 AS CompanyId, -- ⚠️ این مقدار را با CompanyId واقعی خود جایگزین کنید
    1 AS IsActive
FROM dbo.Stg_MoadianInvoices
WHERE TaxId IS NOT NULL AND TaxId <> ''
  AND NOT EXISTS (
      SELECT 1 FROM dbo.Parties p WHERE p.TaxId = ISNULL(Stg_MoadianInvoices.TaxId, 'UNKNOWN')
  );

-- ============================================================================
-- مرحلہ ۴: ایجاد فاکتورها (FacInvoice)
-- ============================================================================
INSERT INTO dbo.FacInvoices (
    Number, RefNumber, Kind, Date, PartyId, WarehouseId, Settlement,
    Description, Status, TotalGross, TotalLineDiscount, InvoiceDiscount,
    TotalTaxable, TotalVat, ShippingCost, TotalNet, SettledAmount,
    RemainingAmount, CreatedBy, CreatedAt
)
SELECT
    -- Number: عدد استخراجی از تاریخ و شناسه مالیاتی
    CAST(FORMAT(pDate, 'yyyyMMdd') + RIGHT('000000' + TaxId, 6) AS INT) AS Number,
    -- RefNumber: شناسه unico
    TaxId + '_' + FORMAT(pDate, 'yyyyMMdd') AS RefNumber,
    -- Kind: نوع فاکتور (0=Sale, 1=Purchase, 2=PurchaseReturn, 3=SaleReturn)
    CASE 
        WHEN LOWER(InvoiceType) LIKE N'%خرید%' THEN 1
        WHEN LOWER(InvoiceType) LIKE N'%برگشت از فروش%' THEN 3
        WHEN LOWER(InvoiceType) LIKE N'%برگشت از خرید%' THEN 2
        ELSE 0 -- Sale
    END AS Kind,
    -- Date: تاریخ Convert شده
    CASE 
        WHEN TRY_CONVERT(DATE, InvoiceDate, 103) IS NOT NULL THEN TRY_CONVERT(DATE, InvoiceDate, 103)
        WHEN TRY_CONVERT(DATE, InvoiceDate) IS NOT NULL THEN TRY_CONVERT(DATE, InvoiceDate)
        ELSE GETDATE()
    END AS pDate,
    -- PartyId: mendapatkan ID مشتری
    (SELECT TOP 1 p.Id FROM dbo.Parties p WHERE p.TaxId = ISNULL(s.TaxId, 'UNKNOWN')) AS PartyId,
    -- WarehouseId: ⚠️ این مقدار را با انبار واقعی خود جایگزین کنید
    1 AS WarehouseId,
    -- Settlement: نقدی یا نسیه
    CASE 
        WHEN CashAmount > CreditAmount THEN 0 -- Cash
        ELSE 1 -- Credit
    END AS Settlement,
    -- Description
    N'Imported from Moudian — SendStatus: ' + ISNULL(SendStatus, '') AS Description,
    -- Status: پیش‌نویس/قطعی/ابطال
    CASE 
        WHEN LOWER(InvoiceStatus) LIKE N'%قطعی%' OR LOWER(InvoiceStatus) LIKE '%confirmed%' THEN 1
        WHEN LOWER(InvoiceStatus) LIKE N'%ابطال%' OR LOWER(InvoiceStatus) LIKE '%cancel%' THEN 2
        ELSE 0 -- Draft
    END AS Status,
    -- مبالغ
    GoodsAmount AS TotalGross,
    GoodsDiscount AS TotalLineDiscount,
    0 AS InvoiceDiscount,
    GoodsAmount - GoodsDiscount AS TotalTaxable,
    GoodsVat AS TotalVat,
    0 AS ShippingCost,
    TotalAmount AS TotalNet,
    CashAmount + CreditAmount AS SettledAmount,
    TotalAmount - (CashAmount + CreditAmount) AS RemainingAmount,
    'System' AS CreatedBy,
    GETDATE() AS CreatedAt
FROM dbo.Stg_MoadianInvoices s
CROSS APPLY (
    SELECT 
        CASE 
            WHEN TRY_CONVERT(DATE, s.InvoiceDate, 103) IS NOT NULL THEN TRY_CONVERT(DATE, s.InvoiceDate, 103)
            WHEN TRY_CONVERT(DATE, s.InvoiceDate) IS NOT NULL THEN TRY_CONVERT(DATE, s.InvoiceDate)
            ELSE GETDATE()
        END AS pDate,
        ISNULL(s.TaxId, 'UNKNOWN') AS TaxId
) t
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.FacInvoices f 
    WHERE f.RefNumber = TaxId + '_' + FORMAT(t.pDate, 'yyyyMMdd')
);

-- ============================================================================
-- مرحلہ ۵: ایجاد خطوط فاکتور (FacInvoiceLine)
-- ============================================================================
-- یک خط برای هر فاکتور با کل مبلغ
INSERT INTO dbo.FacInvoiceLines (
    InvoiceId, RowNo, ProductId, ProductCode, ProductName, Unit, TaxCode,
    Quantity, UnitPrice, DiscountPercent, Discount, VatRate, VatAmount,
    Taxable, Total, Description, CurrentStock
)
SELECT
    f.Id AS InvoiceId,
    1 AS RowNo,
    0 AS ProductId, -- ⚠️ محصول خودکار ایجاد نشده — باید در UI تکمیل شود
    'IMPORTED' AS ProductCode,
    N'کالای واردشده از مودیان' AS ProductName,
    '' AS Unit,
    '' AS TaxCode,
    1 AS Quantity,
    f.TotalGross AS UnitPrice,
    CASE WHEN f.TotalGross > 0 THEN (f.TotalLineDiscount / f.TotalGross) * 100 ELSE 0 END AS DiscountPercent,
    f.TotalLineDiscount AS Discount,
    CASE WHEN f.TotalTaxable > 0 THEN (f.TotalVat / f.TotalTaxable) * 100 ELSE 9 END AS VatRate,
    f.TotalVat AS VatAmount,
    f.TotalTaxable AS Taxable,
    f.TotalNet AS Total,
    N'Imported from Moudian' AS Description,
    0 AS CurrentStock
FROM dbo.FacInvoices f
INNER JOIN dbo.Stg_MoadianInvoices s ON f.RefNumber = ISNULL(s.TaxId, 'UNKNOWN') + '_' + FORMAT(f.Date, 'yyyyMMdd')
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.FacInvoiceLines fl WHERE fl.InvoiceId = f.Id
);

-- ============================================================================
-- مرحلہ ۶: گزارش نتیجه import
-- ============================================================================
SELECT 
    'Staging Rows' AS Stage, COUNT(*) AS Count FROM dbo.Stg_MoadianInvoices
UNION ALL
SELECT 'Parties Created', COUNT(*) FROM dbo.Parties WHERE CreatedAt >= DATEADD(hour, -1, GETDATE())
UNION ALL
SELECT 'Invoices Imported', COUNT(*) FROM dbo.FacInvoices WHERE CreatedAt >= DATEADD(hour, -1, GETDATE())
UNION ALL
SELECT 'Invoice Lines Created', COUNT(*) FROM dbo.FacInvoiceLines WHERE Description = N'Imported from Moudian';

-- ============================================================================
-- مرحلہ ۷: (اختیاری) پاکسازی جدول موقت پس از import موفق
-- ============================================================================
-- DROP TABLE dbo.Stg_MoadianInvoices;

/* ============================================================================
   ⚠️ نکات مهم قبل از اجرا:
   
   ۱. CompanyId در مرحلہ ۳ را با CompanyId واقعی خود جایگزین کنید (اکنون 1 است)
   ۲. WarehouseId در مرحلہ ۴ را با انبار واقعی خود جایگزین کنید (اکنون 1 است)
   ۳. فایل اکسل باید اول در جدول Stg_MoadianInvoices بارگذاری شود
   ۴. تاریخ‌ها باید با فرمت DD/MM/YYYY یا YYYY/MM/DD باشند
   ۵. مبالغ باید عددی باشند (بدون جداکننده هزارگان یا با کاما)
   
   ============================================================================ */
