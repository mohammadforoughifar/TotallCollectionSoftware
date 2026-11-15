using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// Makes the old ERP links optional, retains them for audit, and changes the active
/// receipt key to the operational reversal date.  Existing rows are copied unchanged;
/// no ERP voucher/account is deleted or modified.  Called after the older schema repairs.
/// </summary>
public static class SaleChequeShopLedgerSchemaV1
{
    public const string SqlServer = @"DECLARE @dropFk nvarchar(max), @restoreFk nvarchar(max);
IF OBJECT_ID(N'dbo.SaleChequeChargeEvents', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SaleChequeChargeEvents', N'AccVoucherId') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeChargeEvents') AND name=N'AccVoucherId' AND is_nullable=0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeChargeEvents') AND name=N'IX_SaleChequeChargeEvents_AccVoucherId')
            DROP INDEX [IX_SaleChequeChargeEvents_AccVoucherId] ON [dbo].[SaleChequeChargeEvents];
        IF EXISTS (SELECT 1 FROM sys.foreign_key_columns c WHERE c.parent_object_id=OBJECT_ID(N'dbo.SaleChequeChargeEvents')
            AND c.parent_column_id=COLUMNPROPERTY(c.parent_object_id, N'AccVoucherId', 'ColumnId')
            AND (SELECT COUNT(*) FROM sys.foreign_key_columns cc WHERE cc.constraint_object_id=c.constraint_object_id) <> 1)
            THROW 51000, 'Cannot safely upgrade a composite voucher foreign key.', 1;
        SET @dropFk=N''; SET @restoreFk=N'';
        SELECT @dropFk=@dropFk + N'ALTER TABLE [dbo].[SaleChequeChargeEvents] DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';',
            @restoreFk=@restoreFk + N'ALTER TABLE [dbo].[SaleChequeChargeEvents] WITH ' +
                CASE WHEN fk.is_not_trusted=1 THEN N'NOCHECK' ELSE N'CHECK' END +
                N' ADD CONSTRAINT ' + QUOTENAME(fk.name) + N' FOREIGN KEY ([AccVoucherId]) REFERENCES ' +
                QUOTENAME(OBJECT_SCHEMA_NAME(fk.referenced_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.referenced_object_id)) +
                N' (' + QUOTENAME(rc.name) + N') ON DELETE ' + REPLACE(fk.delete_referential_action_desc, N'_', N' ') +
                N' ON UPDATE ' + REPLACE(fk.update_referential_action_desc, N'_', N' ') +
                CASE WHEN fk.is_not_for_replication=1 THEN N' NOT FOR REPLICATION' ELSE N'' END + N';' +
                CASE WHEN fk.is_disabled=1 THEN N'ALTER TABLE [dbo].[SaleChequeChargeEvents] NOCHECK CONSTRAINT ' + QUOTENAME(fk.name) + N';' ELSE N'' END
        FROM sys.foreign_keys fk JOIN sys.foreign_key_columns c ON c.constraint_object_id=fk.object_id
        JOIN sys.columns rc ON rc.object_id=c.referenced_object_id AND rc.column_id=c.referenced_column_id
        WHERE fk.parent_object_id=OBJECT_ID(N'dbo.SaleChequeChargeEvents') AND c.parent_column_id=COLUMNPROPERTY(c.parent_object_id, N'AccVoucherId', 'ColumnId');
        IF LEN(@dropFk)>0 EXEC sp_executesql @dropFk;
        ALTER TABLE [dbo].[SaleChequeChargeEvents] ALTER COLUMN [AccVoucherId] int NULL;
        IF LEN(@restoreFk)>0 EXEC sp_executesql @restoreFk;
    END
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeChargeEvents') AND name=N'IX_SaleChequeChargeEvents_AccVoucherId' AND has_filter=0)
        DROP INDEX [IX_SaleChequeChargeEvents_AccVoucherId] ON [dbo].[SaleChequeChargeEvents];
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeChargeEvents') AND name=N'IX_SaleChequeChargeEvents_AccVoucherId')
        CREATE UNIQUE INDEX [IX_SaleChequeChargeEvents_AccVoucherId] ON [dbo].[SaleChequeChargeEvents] ([AccVoucherId]) WHERE [AccVoucherId] IS NOT NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeReceipts', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SaleChequeReceipts', N'AccVoucherId') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReceipts') AND name=N'AccVoucherId' AND is_nullable=0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReceipts') AND name=N'IX_SaleChequeReceipts_AccVoucherId')
            DROP INDEX [IX_SaleChequeReceipts_AccVoucherId] ON [dbo].[SaleChequeReceipts];
        IF EXISTS (SELECT 1 FROM sys.foreign_key_columns c WHERE c.parent_object_id=OBJECT_ID(N'dbo.SaleChequeReceipts')
            AND c.parent_column_id=COLUMNPROPERTY(c.parent_object_id, N'AccVoucherId', 'ColumnId')
            AND (SELECT COUNT(*) FROM sys.foreign_key_columns cc WHERE cc.constraint_object_id=c.constraint_object_id) <> 1)
            THROW 51000, 'Cannot safely upgrade a composite voucher foreign key.', 1;
        SET @dropFk=N''; SET @restoreFk=N'';
        SELECT @dropFk=@dropFk + N'ALTER TABLE [dbo].[SaleChequeReceipts] DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';',
            @restoreFk=@restoreFk + N'ALTER TABLE [dbo].[SaleChequeReceipts] WITH ' +
                CASE WHEN fk.is_not_trusted=1 THEN N'NOCHECK' ELSE N'CHECK' END +
                N' ADD CONSTRAINT ' + QUOTENAME(fk.name) + N' FOREIGN KEY ([AccVoucherId]) REFERENCES ' +
                QUOTENAME(OBJECT_SCHEMA_NAME(fk.referenced_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.referenced_object_id)) +
                N' (' + QUOTENAME(rc.name) + N') ON DELETE ' + REPLACE(fk.delete_referential_action_desc, N'_', N' ') +
                N' ON UPDATE ' + REPLACE(fk.update_referential_action_desc, N'_', N' ') +
                CASE WHEN fk.is_not_for_replication=1 THEN N' NOT FOR REPLICATION' ELSE N'' END + N';' +
                CASE WHEN fk.is_disabled=1 THEN N'ALTER TABLE [dbo].[SaleChequeReceipts] NOCHECK CONSTRAINT ' + QUOTENAME(fk.name) + N';' ELSE N'' END
        FROM sys.foreign_keys fk JOIN sys.foreign_key_columns c ON c.constraint_object_id=fk.object_id
        JOIN sys.columns rc ON rc.object_id=c.referenced_object_id AND rc.column_id=c.referenced_column_id
        WHERE fk.parent_object_id=OBJECT_ID(N'dbo.SaleChequeReceipts') AND c.parent_column_id=COLUMNPROPERTY(c.parent_object_id, N'AccVoucherId', 'ColumnId');
        IF LEN(@dropFk)>0 EXEC sp_executesql @dropFk;
        ALTER TABLE [dbo].[SaleChequeReceipts] ALTER COLUMN [AccVoucherId] int NULL;
        IF LEN(@restoreFk)>0 EXEC sp_executesql @restoreFk;
    END
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReceipts') AND name=N'IX_SaleChequeReceipts_AccVoucherId' AND has_filter=0)
        DROP INDEX [IX_SaleChequeReceipts_AccVoucherId] ON [dbo].[SaleChequeReceipts];
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReceipts') AND name=N'IX_SaleChequeReceipts_AccVoucherId')
        CREATE UNIQUE INDEX [IX_SaleChequeReceipts_AccVoucherId] ON [dbo].[SaleChequeReceipts] ([AccVoucherId]) WHERE [AccVoucherId] IS NOT NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeReturns', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SaleChequeReturns', N'AccVoucherId') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReturns') AND name=N'AccVoucherId' AND is_nullable=0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReturns') AND name=N'IX_SaleChequeReturns_AccVoucherId')
            DROP INDEX [IX_SaleChequeReturns_AccVoucherId] ON [dbo].[SaleChequeReturns];
        IF EXISTS (SELECT 1 FROM sys.foreign_key_columns c WHERE c.parent_object_id=OBJECT_ID(N'dbo.SaleChequeReturns')
            AND c.parent_column_id=COLUMNPROPERTY(c.parent_object_id, N'AccVoucherId', 'ColumnId')
            AND (SELECT COUNT(*) FROM sys.foreign_key_columns cc WHERE cc.constraint_object_id=c.constraint_object_id) <> 1)
            THROW 51000, 'Cannot safely upgrade a composite voucher foreign key.', 1;
        SET @dropFk=N''; SET @restoreFk=N'';
        SELECT @dropFk=@dropFk + N'ALTER TABLE [dbo].[SaleChequeReturns] DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';',
            @restoreFk=@restoreFk + N'ALTER TABLE [dbo].[SaleChequeReturns] WITH ' +
                CASE WHEN fk.is_not_trusted=1 THEN N'NOCHECK' ELSE N'CHECK' END +
                N' ADD CONSTRAINT ' + QUOTENAME(fk.name) + N' FOREIGN KEY ([AccVoucherId]) REFERENCES ' +
                QUOTENAME(OBJECT_SCHEMA_NAME(fk.referenced_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.referenced_object_id)) +
                N' (' + QUOTENAME(rc.name) + N') ON DELETE ' + REPLACE(fk.delete_referential_action_desc, N'_', N' ') +
                N' ON UPDATE ' + REPLACE(fk.update_referential_action_desc, N'_', N' ') +
                CASE WHEN fk.is_not_for_replication=1 THEN N' NOT FOR REPLICATION' ELSE N'' END + N';' +
                CASE WHEN fk.is_disabled=1 THEN N'ALTER TABLE [dbo].[SaleChequeReturns] NOCHECK CONSTRAINT ' + QUOTENAME(fk.name) + N';' ELSE N'' END
        FROM sys.foreign_keys fk JOIN sys.foreign_key_columns c ON c.constraint_object_id=fk.object_id
        JOIN sys.columns rc ON rc.object_id=c.referenced_object_id AND rc.column_id=c.referenced_column_id
        WHERE fk.parent_object_id=OBJECT_ID(N'dbo.SaleChequeReturns') AND c.parent_column_id=COLUMNPROPERTY(c.parent_object_id, N'AccVoucherId', 'ColumnId');
        IF LEN(@dropFk)>0 EXEC sp_executesql @dropFk;
        ALTER TABLE [dbo].[SaleChequeReturns] ALTER COLUMN [AccVoucherId] int NULL;
        IF LEN(@restoreFk)>0 EXEC sp_executesql @restoreFk;
    END
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReturns') AND name=N'IX_SaleChequeReturns_AccVoucherId' AND has_filter=0)
        DROP INDEX [IX_SaleChequeReturns_AccVoucherId] ON [dbo].[SaleChequeReturns];
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReturns') AND name=N'IX_SaleChequeReturns_AccVoucherId')
        CREATE UNIQUE INDEX [IX_SaleChequeReturns_AccVoucherId] ON [dbo].[SaleChequeReturns] ([AccVoucherId]) WHERE [AccVoucherId] IS NOT NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeCreditSettlements', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SaleChequeCreditSettlements', N'AccVoucherId') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeCreditSettlements') AND name=N'AccVoucherId' AND is_nullable=0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeCreditSettlements') AND name=N'IX_SaleChequeCreditSettlements_AccVoucherId')
            DROP INDEX [IX_SaleChequeCreditSettlements_AccVoucherId] ON [dbo].[SaleChequeCreditSettlements];
        IF EXISTS (SELECT 1 FROM sys.foreign_key_columns c WHERE c.parent_object_id=OBJECT_ID(N'dbo.SaleChequeCreditSettlements')
            AND c.parent_column_id=COLUMNPROPERTY(c.parent_object_id, N'AccVoucherId', 'ColumnId')
            AND (SELECT COUNT(*) FROM sys.foreign_key_columns cc WHERE cc.constraint_object_id=c.constraint_object_id) <> 1)
            THROW 51000, 'Cannot safely upgrade a composite voucher foreign key.', 1;
        SET @dropFk=N''; SET @restoreFk=N'';
        SELECT @dropFk=@dropFk + N'ALTER TABLE [dbo].[SaleChequeCreditSettlements] DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';',
            @restoreFk=@restoreFk + N'ALTER TABLE [dbo].[SaleChequeCreditSettlements] WITH ' +
                CASE WHEN fk.is_not_trusted=1 THEN N'NOCHECK' ELSE N'CHECK' END +
                N' ADD CONSTRAINT ' + QUOTENAME(fk.name) + N' FOREIGN KEY ([AccVoucherId]) REFERENCES ' +
                QUOTENAME(OBJECT_SCHEMA_NAME(fk.referenced_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.referenced_object_id)) +
                N' (' + QUOTENAME(rc.name) + N') ON DELETE ' + REPLACE(fk.delete_referential_action_desc, N'_', N' ') +
                N' ON UPDATE ' + REPLACE(fk.update_referential_action_desc, N'_', N' ') +
                CASE WHEN fk.is_not_for_replication=1 THEN N' NOT FOR REPLICATION' ELSE N'' END + N';' +
                CASE WHEN fk.is_disabled=1 THEN N'ALTER TABLE [dbo].[SaleChequeCreditSettlements] NOCHECK CONSTRAINT ' + QUOTENAME(fk.name) + N';' ELSE N'' END
        FROM sys.foreign_keys fk JOIN sys.foreign_key_columns c ON c.constraint_object_id=fk.object_id
        JOIN sys.columns rc ON rc.object_id=c.referenced_object_id AND rc.column_id=c.referenced_column_id
        WHERE fk.parent_object_id=OBJECT_ID(N'dbo.SaleChequeCreditSettlements') AND c.parent_column_id=COLUMNPROPERTY(c.parent_object_id, N'AccVoucherId', 'ColumnId');
        IF LEN(@dropFk)>0 EXEC sp_executesql @dropFk;
        ALTER TABLE [dbo].[SaleChequeCreditSettlements] ALTER COLUMN [AccVoucherId] int NULL;
        IF LEN(@restoreFk)>0 EXEC sp_executesql @restoreFk;
    END
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeCreditSettlements') AND name=N'IX_SaleChequeCreditSettlements_AccVoucherId' AND has_filter=0)
        DROP INDEX [IX_SaleChequeCreditSettlements_AccVoucherId] ON [dbo].[SaleChequeCreditSettlements];
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeCreditSettlements') AND name=N'IX_SaleChequeCreditSettlements_AccVoucherId')
        CREATE UNIQUE INDEX [IX_SaleChequeCreditSettlements_AccVoucherId] ON [dbo].[SaleChequeCreditSettlements] ([AccVoucherId]) WHERE [AccVoucherId] IS NOT NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SaleChequeInstallmentReceipts', N'AccVoucherId') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts') AND name=N'AccVoucherId' AND is_nullable=0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts') AND name=N'IX_SaleChequeInstReceipts_AccVoucherId')
            DROP INDEX [IX_SaleChequeInstReceipts_AccVoucherId] ON [dbo].[SaleChequeInstallmentReceipts];
        IF EXISTS (SELECT 1 FROM sys.foreign_key_columns c WHERE c.parent_object_id=OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts')
            AND c.parent_column_id=COLUMNPROPERTY(c.parent_object_id, N'AccVoucherId', 'ColumnId')
            AND (SELECT COUNT(*) FROM sys.foreign_key_columns cc WHERE cc.constraint_object_id=c.constraint_object_id) <> 1)
            THROW 51000, 'Cannot safely upgrade a composite voucher foreign key.', 1;
        SET @dropFk=N''; SET @restoreFk=N'';
        SELECT @dropFk=@dropFk + N'ALTER TABLE [dbo].[SaleChequeInstallmentReceipts] DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';',
            @restoreFk=@restoreFk + N'ALTER TABLE [dbo].[SaleChequeInstallmentReceipts] WITH ' +
                CASE WHEN fk.is_not_trusted=1 THEN N'NOCHECK' ELSE N'CHECK' END +
                N' ADD CONSTRAINT ' + QUOTENAME(fk.name) + N' FOREIGN KEY ([AccVoucherId]) REFERENCES ' +
                QUOTENAME(OBJECT_SCHEMA_NAME(fk.referenced_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.referenced_object_id)) +
                N' (' + QUOTENAME(rc.name) + N') ON DELETE ' + REPLACE(fk.delete_referential_action_desc, N'_', N' ') +
                N' ON UPDATE ' + REPLACE(fk.update_referential_action_desc, N'_', N' ') +
                CASE WHEN fk.is_not_for_replication=1 THEN N' NOT FOR REPLICATION' ELSE N'' END + N';' +
                CASE WHEN fk.is_disabled=1 THEN N'ALTER TABLE [dbo].[SaleChequeInstallmentReceipts] NOCHECK CONSTRAINT ' + QUOTENAME(fk.name) + N';' ELSE N'' END
        FROM sys.foreign_keys fk JOIN sys.foreign_key_columns c ON c.constraint_object_id=fk.object_id
        JOIN sys.columns rc ON rc.object_id=c.referenced_object_id AND rc.column_id=c.referenced_column_id
        WHERE fk.parent_object_id=OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts') AND c.parent_column_id=COLUMNPROPERTY(c.parent_object_id, N'AccVoucherId', 'ColumnId');
        IF LEN(@dropFk)>0 EXEC sp_executesql @dropFk;
        ALTER TABLE [dbo].[SaleChequeInstallmentReceipts] ALTER COLUMN [AccVoucherId] int NULL;
        IF LEN(@restoreFk)>0 EXEC sp_executesql @restoreFk;
    END
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts') AND name=N'IX_SaleChequeInstReceipts_AccVoucherId' AND has_filter=0)
        DROP INDEX [IX_SaleChequeInstReceipts_AccVoucherId] ON [dbo].[SaleChequeInstallmentReceipts];
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts') AND name=N'IX_SaleChequeInstReceipts_AccVoucherId')
        CREATE UNIQUE INDEX [IX_SaleChequeInstReceipts_AccVoucherId] ON [dbo].[SaleChequeInstallmentReceipts] ([AccVoucherId]) WHERE [AccVoucherId] IS NOT NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeCancellations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SaleChequeCancellations', N'AccVoucherId') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeCancellations') AND name=N'AccVoucherId' AND is_nullable=0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeCancellations') AND name=N'IX_SaleChequeCancellations_AccVoucherId')
            DROP INDEX [IX_SaleChequeCancellations_AccVoucherId] ON [dbo].[SaleChequeCancellations];
        IF EXISTS (SELECT 1 FROM sys.foreign_key_columns c WHERE c.parent_object_id=OBJECT_ID(N'dbo.SaleChequeCancellations')
            AND c.parent_column_id=COLUMNPROPERTY(c.parent_object_id, N'AccVoucherId', 'ColumnId')
            AND (SELECT COUNT(*) FROM sys.foreign_key_columns cc WHERE cc.constraint_object_id=c.constraint_object_id) <> 1)
            THROW 51000, 'Cannot safely upgrade a composite voucher foreign key.', 1;
        SET @dropFk=N''; SET @restoreFk=N'';
        SELECT @dropFk=@dropFk + N'ALTER TABLE [dbo].[SaleChequeCancellations] DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';',
            @restoreFk=@restoreFk + N'ALTER TABLE [dbo].[SaleChequeCancellations] WITH ' +
                CASE WHEN fk.is_not_trusted=1 THEN N'NOCHECK' ELSE N'CHECK' END +
                N' ADD CONSTRAINT ' + QUOTENAME(fk.name) + N' FOREIGN KEY ([AccVoucherId]) REFERENCES ' +
                QUOTENAME(OBJECT_SCHEMA_NAME(fk.referenced_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.referenced_object_id)) +
                N' (' + QUOTENAME(rc.name) + N') ON DELETE ' + REPLACE(fk.delete_referential_action_desc, N'_', N' ') +
                N' ON UPDATE ' + REPLACE(fk.update_referential_action_desc, N'_', N' ') +
                CASE WHEN fk.is_not_for_replication=1 THEN N' NOT FOR REPLICATION' ELSE N'' END + N';' +
                CASE WHEN fk.is_disabled=1 THEN N'ALTER TABLE [dbo].[SaleChequeCancellations] NOCHECK CONSTRAINT ' + QUOTENAME(fk.name) + N';' ELSE N'' END
        FROM sys.foreign_keys fk JOIN sys.foreign_key_columns c ON c.constraint_object_id=fk.object_id
        JOIN sys.columns rc ON rc.object_id=c.referenced_object_id AND rc.column_id=c.referenced_column_id
        WHERE fk.parent_object_id=OBJECT_ID(N'dbo.SaleChequeCancellations') AND c.parent_column_id=COLUMNPROPERTY(c.parent_object_id, N'AccVoucherId', 'ColumnId');
        IF LEN(@dropFk)>0 EXEC sp_executesql @dropFk;
        ALTER TABLE [dbo].[SaleChequeCancellations] ALTER COLUMN [AccVoucherId] int NULL;
        IF LEN(@restoreFk)>0 EXEC sp_executesql @restoreFk;
    END
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeCancellations') AND name=N'IX_SaleChequeCancellations_AccVoucherId' AND has_filter=0)
        DROP INDEX [IX_SaleChequeCancellations_AccVoucherId] ON [dbo].[SaleChequeCancellations];
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeCancellations') AND name=N'IX_SaleChequeCancellations_AccVoucherId')
        CREATE UNIQUE INDEX [IX_SaleChequeCancellations_AccVoucherId] ON [dbo].[SaleChequeCancellations] ([AccVoucherId]) WHERE [AccVoucherId] IS NOT NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeReceipts', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SaleChequeReceipts', N'ReversalDate') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReceipts') AND name=N'IX_SaleChequeReceipts_ChequeId' AND (has_filter=0 OR filter_definition NOT LIKE N'%ReversalDate%'))
        DROP INDEX [IX_SaleChequeReceipts_ChequeId] ON [dbo].[SaleChequeReceipts];
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReceipts') AND name=N'IX_SaleChequeReceipts_ChequeId')
        EXEC(N'CREATE UNIQUE INDEX [IX_SaleChequeReceipts_ChequeId] ON [dbo].[SaleChequeReceipts] ([ChequeId]) WHERE [ReversalDate] IS NULL;');
END
IF OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SaleChequeInstallmentReceipts', N'ReversalDate') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts') AND name=N'IX_SaleChequeInstReceipts_ActiveInstallment' AND (has_filter=0 OR filter_definition NOT LIKE N'%ReversalDate%'))
        DROP INDEX [IX_SaleChequeInstReceipts_ActiveInstallment] ON [dbo].[SaleChequeInstallmentReceipts];
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts') AND name=N'IX_SaleChequeInstReceipts_ActiveInstallment')
        EXEC(N'CREATE UNIQUE INDEX [IX_SaleChequeInstReceipts_ActiveInstallment] ON [dbo].[SaleChequeInstallmentReceipts] ([InstallmentId]) WHERE [ReversalDate] IS NULL;');
END
-- Historical ERP account snapshots remain NULL for store-only operations.
IF OBJECT_ID(N'dbo.SaleChequeReceipts', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReceipts') AND name=N'BankAccountId' AND is_nullable=0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_key_columns WHERE parent_object_id=OBJECT_ID(N'dbo.SaleChequeReceipts') AND parent_column_id=COLUMNPROPERTY(parent_object_id, N'BankAccountId', 'ColumnId'))
        THROW 51000, 'Cannot safely upgrade an unexpected account foreign key.', 1;
    ALTER TABLE [dbo].[SaleChequeReceipts] ALTER COLUMN [BankAccountId] int NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeReceipts', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReceipts') AND name=N'PartyAccountId' AND is_nullable=0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_key_columns WHERE parent_object_id=OBJECT_ID(N'dbo.SaleChequeReceipts') AND parent_column_id=COLUMNPROPERTY(parent_object_id, N'PartyAccountId', 'ColumnId'))
        THROW 51000, 'Cannot safely upgrade an unexpected account foreign key.', 1;
    ALTER TABLE [dbo].[SaleChequeReceipts] ALTER COLUMN [PartyAccountId] int NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts') AND name=N'BankAccountId' AND is_nullable=0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_key_columns WHERE parent_object_id=OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts') AND parent_column_id=COLUMNPROPERTY(parent_object_id, N'BankAccountId', 'ColumnId'))
        THROW 51000, 'Cannot safely upgrade an unexpected account foreign key.', 1;
    ALTER TABLE [dbo].[SaleChequeInstallmentReceipts] ALTER COLUMN [BankAccountId] int NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts') AND name=N'PartyAccountId' AND is_nullable=0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_key_columns WHERE parent_object_id=OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts') AND parent_column_id=COLUMNPROPERTY(parent_object_id, N'PartyAccountId', 'ColumnId'))
        THROW 51000, 'Cannot safely upgrade an unexpected account foreign key.', 1;
    ALTER TABLE [dbo].[SaleChequeInstallmentReceipts] ALTER COLUMN [PartyAccountId] int NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeReturns', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReturns') AND name=N'SalesAccountId' AND is_nullable=0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_key_columns WHERE parent_object_id=OBJECT_ID(N'dbo.SaleChequeReturns') AND parent_column_id=COLUMNPROPERTY(parent_object_id, N'SalesAccountId', 'ColumnId'))
        THROW 51000, 'Cannot safely upgrade an unexpected account foreign key.', 1;
    ALTER TABLE [dbo].[SaleChequeReturns] ALTER COLUMN [SalesAccountId] int NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeReturns', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReturns') AND name=N'IncomeAccountId' AND is_nullable=0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_key_columns WHERE parent_object_id=OBJECT_ID(N'dbo.SaleChequeReturns') AND parent_column_id=COLUMNPROPERTY(parent_object_id, N'IncomeAccountId', 'ColumnId'))
        THROW 51000, 'Cannot safely upgrade an unexpected account foreign key.', 1;
    ALTER TABLE [dbo].[SaleChequeReturns] ALTER COLUMN [IncomeAccountId] int NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeReturns', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeReturns') AND name=N'PartyAccountId' AND is_nullable=0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_key_columns WHERE parent_object_id=OBJECT_ID(N'dbo.SaleChequeReturns') AND parent_column_id=COLUMNPROPERTY(parent_object_id, N'PartyAccountId', 'ColumnId'))
        THROW 51000, 'Cannot safely upgrade an unexpected account foreign key.', 1;
    ALTER TABLE [dbo].[SaleChequeReturns] ALTER COLUMN [PartyAccountId] int NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeCancellations', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeCancellations') AND name=N'SalesAccountId' AND is_nullable=0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_key_columns WHERE parent_object_id=OBJECT_ID(N'dbo.SaleChequeCancellations') AND parent_column_id=COLUMNPROPERTY(parent_object_id, N'SalesAccountId', 'ColumnId'))
        THROW 51000, 'Cannot safely upgrade an unexpected account foreign key.', 1;
    ALTER TABLE [dbo].[SaleChequeCancellations] ALTER COLUMN [SalesAccountId] int NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeCancellations', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeCancellations') AND name=N'IncomeAccountId' AND is_nullable=0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_key_columns WHERE parent_object_id=OBJECT_ID(N'dbo.SaleChequeCancellations') AND parent_column_id=COLUMNPROPERTY(parent_object_id, N'IncomeAccountId', 'ColumnId'))
        THROW 51000, 'Cannot safely upgrade an unexpected account foreign key.', 1;
    ALTER TABLE [dbo].[SaleChequeCancellations] ALTER COLUMN [IncomeAccountId] int NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeCancellations', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeCancellations') AND name=N'PartyAccountId' AND is_nullable=0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_key_columns WHERE parent_object_id=OBJECT_ID(N'dbo.SaleChequeCancellations') AND parent_column_id=COLUMNPROPERTY(parent_object_id, N'PartyAccountId', 'ColumnId'))
        THROW 51000, 'Cannot safely upgrade an unexpected account foreign key.', 1;
    ALTER TABLE [dbo].[SaleChequeCancellations] ALTER COLUMN [PartyAccountId] int NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeCreditSettlements', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SaleChequeCreditSettlements') AND name=N'PartyAccountId' AND is_nullable=0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_key_columns WHERE parent_object_id=OBJECT_ID(N'dbo.SaleChequeCreditSettlements') AND parent_column_id=COLUMNPROPERTY(parent_object_id, N'PartyAccountId', 'ColumnId'))
        THROW 51000, 'Cannot safely upgrade an unexpected account foreign key.', 1;
    ALTER TABLE [dbo].[SaleChequeCreditSettlements] ALTER COLUMN [PartyAccountId] int NULL;
END
IF OBJECT_ID(N'dbo.SaleChequeCharges', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TrsVouchers', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.SaleChequeCharges', N'InitialCashTrsVoucherId') IS NULL
        ALTER TABLE [dbo].[SaleChequeCharges] ADD [InitialCashTrsVoucherId] int NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_key_columns WHERE parent_object_id=OBJECT_ID(N'dbo.SaleChequeCharges') AND parent_column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.SaleChequeCharges'), N'InitialCashTrsVoucherId', 'ColumnId'))
        EXEC(N'ALTER TABLE [dbo].[SaleChequeCharges] ADD CONSTRAINT [FK_SaleChequeCharges_InitialCashTrsVoucher] FOREIGN KEY ([InitialCashTrsVoucherId]) REFERENCES [dbo].[TrsVouchers] ([Id]) ON DELETE NO ACTION;');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SaleChequeCharges') AND name=N'IX_SaleChequeCharges_InitialCashTrsVoucherId')
        EXEC(N'CREATE UNIQUE INDEX [IX_SaleChequeCharges_InitialCashTrsVoucherId] ON [dbo].[SaleChequeCharges] ([InitialCashTrsVoucherId]) WHERE [InitialCashTrsVoucherId] IS NOT NULL;');
    IF OBJECT_ID(N'dbo.SaleChequeChargeEvents', N'U') IS NOT NULL
        EXEC(N'UPDATE c SET InitialCashTrsVoucherId = (SELECT MIN(v.Id) FROM dbo.SaleChequeChargeEvents e JOIN dbo.TrsVouchers v ON v.VoucherId=e.AccVoucherId WHERE e.SaleChequeChargeId=c.Id AND e.Kind=1 AND v.Kind=0)
              FROM dbo.SaleChequeCharges c WHERE c.InitialCashTrsVoucherId IS NULL AND (SELECT COUNT(*) FROM dbo.SaleChequeChargeEvents e JOIN dbo.TrsVouchers v ON v.VoucherId=e.AccVoucherId WHERE e.SaleChequeChargeId=c.Id AND e.Kind=1 AND v.Kind=0)=1;');
END";

    private static readonly (string Table, string VoucherIndex, string[] AccountSnapshots)[] Tables =
    {
        ("SaleChequeChargeEvents", "IX_SaleChequeChargeEvents_AccVoucherId", Array.Empty<string>()),
        ("SaleChequeReceipts", "IX_SaleChequeReceipts_AccVoucherId", new[] { "BankAccountId", "PartyAccountId" }),
        ("SaleChequeReturns", "IX_SaleChequeReturns_AccVoucherId", new[] { "SalesAccountId", "IncomeAccountId", "PartyAccountId" }),
        ("SaleChequeCreditSettlements", "IX_SaleChequeCreditSettlements_AccVoucherId", new[] { "PartyAccountId" }),
        ("SaleChequeInstallmentReceipts", "IX_SaleChequeInstReceipts_AccVoucherId", new[] { "BankAccountId", "PartyAccountId" }),
        ("SaleChequeCancellations", "IX_SaleChequeCancellations_AccVoucherId", new[] { "SalesAccountId", "IncomeAccountId", "PartyAccountId" })
    };

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (!db.Database.IsSqlite())
        {
            if (db.Database.CurrentTransaction is not null)
            {
                await db.Database.ExecuteSqlRawAsync(SqlServer);
                return;
            }
            await using var sqlTransaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync(SqlServer);
            await sqlTransaction.CommitAsync();
            return;
        }

        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("ارتقای SQLite باید خارج از تراکنش دیگری اجرا شود.");
        var connection = db.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync();
        var foreignKeysOn = Convert.ToInt64(await ScalarAsync(connection, "PRAGMA foreign_keys")) != 0;
        try
        {
            // SQLite cannot relax NOT NULL in place.  Rebuild the affected tables in one
            // transaction with FK enforcement temporarily off, preserving all other
            // columns, indexes, triggers, primary keys and historical voucher IDs.
            if (foreignKeysOn) await ExecuteAsync(connection, "PRAGMA foreign_keys=OFF");
            await ExecuteAsync(connection, "BEGIN IMMEDIATE");
            try
            {
                foreach (var (table, voucherIndex, accountSnapshots) in Tables)
                    await RelaxHistoricalColumnsAsync(connection, table, voucherIndex, accountSnapshots);

                await EnsureInitialCashLinkAsync(connection);
                await ReplaceActiveIndexAsync(connection, "SaleChequeReceipts", "IX_SaleChequeReceipts_ChequeId", "ChequeId");
                await ReplaceActiveIndexAsync(connection, "SaleChequeInstallmentReceipts", "IX_SaleChequeInstReceipts_ActiveInstallment", "InstallmentId");

                // Check rebuilt tables and their children before committing.  A broken
                // reference aborts the whole upgrade rather than losing history.
                foreach (var table in Tables.Select(t => t.Table).Concat(new[] { "SaleChequeCharges", "SaleChequeReschedules", "SaleChequeReturnLines" }))
                {
                    if (Convert.ToInt64(await ScalarAsync(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'")) == 0)
                        continue;
                    await using var check = connection.CreateCommand();
                    check.CommandText = $"PRAGMA foreign_key_check({Quote(table)})";
                    await using var reader = await check.ExecuteReaderAsync();
                    if (await reader.ReadAsync()) throw new InvalidOperationException($"ارتقای گردش فروشگاهی: ارجاع نامعتبر در جدول {table}؛ هیچ تغییری ثبت نشد.");
                }
                await ExecuteAsync(connection, "COMMIT");
            }
            catch
            {
                await ExecuteAsync(connection, "ROLLBACK");
                throw;
            }
        }
        finally
        {
            if (foreignKeysOn) await ExecuteAsync(connection, "PRAGMA foreign_keys=ON");
            if (close) await connection.CloseAsync();
        }
    }

    private static async Task RelaxHistoricalColumnsAsync(DbConnection connection, string table, string voucherIndex, string[] accountSnapshots)
    {
        var createSql = await ScalarAsync(connection,
            $"SELECT sql FROM sqlite_master WHERE type='table' AND name='{table}'") as string;
        if (string.IsNullOrWhiteSpace(createSql)) return;
        var historicalColumns = accountSnapshots.Append("AccVoucherId").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredColumns = new List<string>();
        await using (var info = connection.CreateCommand())
        {
            info.CommandText = $"PRAGMA table_info({Quote(table)})";
            await using var reader = await info.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                if (historicalColumns.Contains(reader.GetString(1)) && reader.GetInt32(3) != 0)
                    requiredColumns.Add(reader.GetString(1));
        }

        if (requiredColumns.Count > 0)
        {
            // Rebuilding an AUTOINCREMENT table must not reuse IDs that were issued
            // and subsequently deleted; copying the surviving rows alone loses that high-water mark.
            var sequence = createSql.Contains("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase)
                ? await ScalarAsync(connection, $"SELECT seq FROM sqlite_sequence WHERE name='{table}'") : null;
            var objects = new List<string>();
            await using (var saved = connection.CreateCommand())
            {
                saved.CommandText = $"SELECT sql FROM sqlite_master WHERE tbl_name='{table}' AND type IN ('index','trigger') AND sql IS NOT NULL ORDER BY type, name";
                await using var reader = await saved.ExecuteReaderAsync();
                while (await reader.ReadAsync()) objects.Add(reader.GetString(0));
            }
            var relaxed = createSql;
            foreach (var column in requiredColumns)
            {
                var name = Regex.Escape(column);
                var columnPattern = $@"(?i)((?:""{name}""|\[{name}\]|`{name}`|\b{name}\b)\s+INTEGER\s+)NOT\s+NULL";
                var replaced = Regex.Replace(relaxed, columnPattern, "$1NULL", RegexOptions.CultureInvariant);
                if (replaced == relaxed)
                    throw new InvalidOperationException($"ساختار ستون تاریخی {column} در {table} قابل شناسایی نیست؛ ارتقا متوقف شد.");
                relaxed = replaced;
            }
            var temporary = "__shop_upgrade_" + table;
            var prefix = @"(?is)^\s*CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:""[^""]+""|\[[^\]]+\]|`[^`]+`|\w+)";
            relaxed = Regex.Replace(relaxed, prefix, "CREATE TABLE " + Quote(temporary), RegexOptions.CultureInvariant);
            if (!relaxed.StartsWith("CREATE TABLE " + Quote(temporary), StringComparison.Ordinal))
                throw new InvalidOperationException($"ساختار جدول {table} قابل ارتقا نیست.");
            await ExecuteAsync(connection, relaxed);
            await ExecuteAsync(connection, $"INSERT INTO {Quote(temporary)} SELECT * FROM {Quote(table)}");
            await ExecuteAsync(connection, $"DROP TABLE {Quote(table)}");
            await ExecuteAsync(connection, $"ALTER TABLE {Quote(temporary)} RENAME TO {Quote(table)}");
            if (sequence is not null && sequence is not DBNull)
            {
                var highWaterMark = Convert.ToInt64(sequence);
                await ExecuteAsync(connection, $"UPDATE sqlite_sequence SET seq=MAX(seq, {highWaterMark}) WHERE name='{table}'");
                await ExecuteAsync(connection, $"INSERT INTO sqlite_sequence(name, seq) SELECT '{table}', {highWaterMark} WHERE NOT EXISTS (SELECT 1 FROM sqlite_sequence WHERE name='{table}')");
            }
            foreach (var sql in objects)
                await ExecuteAsync(connection, sql);
        }
        // The old unique indexes allow only one NULL on SQL Server.  Explicit
        // filtered indexes also make the intended invariant identical on both DBs.
        await ExecuteAsync(connection, $"DROP INDEX IF EXISTS {Quote(voucherIndex)}");
        await ExecuteAsync(connection, $"CREATE UNIQUE INDEX {Quote(voucherIndex)} ON {Quote(table)} ([AccVoucherId]) WHERE [AccVoucherId] IS NOT NULL");
    }

    private static async Task EnsureInitialCashLinkAsync(DbConnection connection)
    {
        if (Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SaleChequeCharges'")) == 0) return;
        var hasColumn = false;
        await using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info('SaleChequeCharges')";
            await using var reader = await info.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                hasColumn |= string.Equals(reader.GetString(1), "InitialCashTrsVoucherId", StringComparison.OrdinalIgnoreCase);
        }
        if (!hasColumn)
            await ExecuteAsync(connection, "ALTER TABLE SaleChequeCharges ADD COLUMN InitialCashTrsVoucherId INTEGER NULL REFERENCES TrsVouchers(Id) ON DELETE RESTRICT");
        await ExecuteAsync(connection, "CREATE UNIQUE INDEX IF NOT EXISTS IX_SaleChequeCharges_InitialCashTrsVoucherId ON SaleChequeCharges(InitialCashTrsVoucherId) WHERE InitialCashTrsVoucherId IS NOT NULL");
        if (Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('SaleChequeChargeEvents','TrsVouchers')")) == 2)
            await ExecuteAsync(connection, @"UPDATE SaleChequeCharges AS c
SET InitialCashTrsVoucherId=(SELECT MIN(v.Id) FROM SaleChequeChargeEvents e JOIN TrsVouchers v ON v.VoucherId=e.AccVoucherId WHERE e.SaleChequeChargeId=c.Id AND e.Kind=1 AND v.Kind=0)
WHERE c.InitialCashTrsVoucherId IS NULL AND (SELECT COUNT(*) FROM SaleChequeChargeEvents e JOIN TrsVouchers v ON v.VoucherId=e.AccVoucherId WHERE e.SaleChequeChargeId=c.Id AND e.Kind=1 AND v.Kind=0)=1");
    }

    private static async Task ReplaceActiveIndexAsync(DbConnection connection, string table, string index, string key)
    {
        if (Convert.ToInt64(await ScalarAsync(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'")) == 0) return;
        await ExecuteAsync(connection, $"DROP INDEX IF EXISTS {Quote(index)}");
        await ExecuteAsync(connection, $"CREATE UNIQUE INDEX {Quote(index)} ON {Quote(table)} ({Quote(key)}) WHERE [ReversalDate] IS NULL");
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
