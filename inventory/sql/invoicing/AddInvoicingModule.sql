-- ماژول فاکتور — اسکریپت ایمن و قابل اجرای مکرر (Idempotent)

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NULL
BEGIN
    CREATE TABLE [FacInvoices] (
        [Id] int NOT NULL IDENTITY,
        [Number] int NOT NULL,
        [RefNumber] nvarchar(60) NULL,
        [Kind] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [DueDate] datetime2 NULL,
        [PartyId] int NULL,
        [WarehouseId] int NOT NULL,
        [Settlement] int NOT NULL,
        [Description] nvarchar(600) NULL,
        [Status] int NOT NULL,
        [TotalGross] decimal(18,2) NOT NULL,
        [TotalLineDiscount] decimal(18,2) NOT NULL,
        [InvoiceDiscount] decimal(18,2) NOT NULL,
        [TotalTaxable] decimal(18,2) NOT NULL,
        [TotalVat] decimal(18,2) NOT NULL,
        [ShippingCost] decimal(18,2) NOT NULL,
        [TotalNet] decimal(18,2) NOT NULL,
        [InvDocId] int NULL,
        [VoucherId] int NULL,
        [CreatedBy] nvarchar(120) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ConfirmedBy] nvarchar(120) NULL,
        [ConfirmedAt] datetime2 NULL,
        CONSTRAINT [PK_FacInvoices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FacInvoices_Parties_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [Parties] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FacInvoices_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END

IF OBJECT_ID(N'[FacRules]', N'U') IS NULL
BEGIN
    CREATE TABLE [FacRules] (
        [Id] int NOT NULL IDENTITY,
        [Kind] int NOT NULL,
        [DocTypeId] int NULL,
        [PartyAccountId] int NULL,
        [MainAccountId] int NULL,
        [VatAccountId] int NULL,
        [CashAccountId] int NULL,
        [ShippingAccountId] int NULL,
        [AutoInvDoc] bit NOT NULL,
        [AutoVoucher] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(500) NULL,
        CONSTRAINT [PK_FacRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FacRules_InvDocTypes_DocTypeId] FOREIGN KEY ([DocTypeId]) REFERENCES [InvDocTypes] ([Id]) ON DELETE SET NULL
    );
END

IF OBJECT_ID(N'[FacInvoiceLines]', N'U') IS NULL
BEGIN
    CREATE TABLE [FacInvoiceLines] (
        [Id] int NOT NULL IDENTITY,
        [InvoiceId] int NOT NULL,
        [RowNo] int NOT NULL,
        [ProductId] int NOT NULL,
        [TaxCode] nvarchar(40) NULL,
        [Quantity] decimal(18,2) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [DiscountPercent] decimal(18,2) NOT NULL,
        [Discount] decimal(18,2) NOT NULL,
        [VatRate] decimal(18,2) NOT NULL,
        [VatAmount] decimal(18,2) NOT NULL,
        [Taxable] decimal(18,2) NOT NULL,
        [Total] decimal(18,2) NOT NULL,
        [Description] nvarchar(400) NULL,
        CONSTRAINT [PK_FacInvoiceLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FacInvoiceLines_FacInvoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [FacInvoices] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_FacInvoiceLines_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END

IF OBJECT_ID(N'[FacInvoiceLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoiceLines_InvoiceId' AND [object_id] = OBJECT_ID(N'[FacInvoiceLines]'))
BEGIN
    CREATE INDEX [IX_FacInvoiceLines_InvoiceId] ON [FacInvoiceLines] ([InvoiceId]);
END

IF OBJECT_ID(N'[FacInvoiceLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoiceLines_ProductId' AND [object_id] = OBJECT_ID(N'[FacInvoiceLines]'))
BEGIN
    CREATE INDEX [IX_FacInvoiceLines_ProductId] ON [FacInvoiceLines] ([ProductId]);
END

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoices_Date' AND [object_id] = OBJECT_ID(N'[FacInvoices]'))
BEGIN
    CREATE INDEX [IX_FacInvoices_Date] ON [FacInvoices] ([Date]);
END

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoices_InvDocId' AND [object_id] = OBJECT_ID(N'[FacInvoices]'))
BEGIN
    CREATE INDEX [IX_FacInvoices_InvDocId] ON [FacInvoices] ([InvDocId]);
END

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoices_Kind_Number' AND [object_id] = OBJECT_ID(N'[FacInvoices]'))
BEGIN
    CREATE UNIQUE INDEX [IX_FacInvoices_Kind_Number] ON [FacInvoices] ([Kind], [Number]);
END

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoices_PartyId' AND [object_id] = OBJECT_ID(N'[FacInvoices]'))
BEGIN
    CREATE INDEX [IX_FacInvoices_PartyId] ON [FacInvoices] ([PartyId]);
END

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoices_Status' AND [object_id] = OBJECT_ID(N'[FacInvoices]'))
BEGIN
    CREATE INDEX [IX_FacInvoices_Status] ON [FacInvoices] ([Status]);
END

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoices_VoucherId' AND [object_id] = OBJECT_ID(N'[FacInvoices]'))
BEGIN
    CREATE INDEX [IX_FacInvoices_VoucherId] ON [FacInvoices] ([VoucherId]);
END

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoices_WarehouseId' AND [object_id] = OBJECT_ID(N'[FacInvoices]'))
BEGIN
    CREATE INDEX [IX_FacInvoices_WarehouseId] ON [FacInvoices] ([WarehouseId]);
END

IF OBJECT_ID(N'[FacRules]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacRules_DocTypeId' AND [object_id] = OBJECT_ID(N'[FacRules]'))
BEGIN
    CREATE INDEX [IX_FacRules_DocTypeId] ON [FacRules] ([DocTypeId]);
END

IF OBJECT_ID(N'[FacRules]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacRules_Kind' AND [object_id] = OBJECT_ID(N'[FacRules]'))
BEGIN
    CREATE UNIQUE INDEX [IX_FacRules_Kind] ON [FacRules] ([Kind]);
END
