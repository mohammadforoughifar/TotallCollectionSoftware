"""Exercise historical SQLite DDL and the rebuild algorithm; Program.cs runs the actual EF helper.

The Python rebuild mirrors the SQL issued by the C# helper; it is not a substitute for
.NET provider execution.  The old schemas and the cash-link UPDATE are extracted from
production source so changes to them are exercised rather than copied by hand.
"""
import re
import sqlite3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src/Inventory.Api/Data'
HELPER = (ROOT / 'SaleChequeShopLedgerSchemaV1.cs').read_text(encoding='utf8')
TABLES = [
    ('SaleChequeChargeEvents', 'IX_SaleChequeChargeEvents_AccVoucherId', ()),
    ('SaleChequeReceipts', 'IX_SaleChequeReceipts_AccVoucherId', ('BankAccountId', 'PartyAccountId')),
    ('SaleChequeReturns', 'IX_SaleChequeReturns_AccVoucherId', ('SalesAccountId', 'IncomeAccountId', 'PartyAccountId')),
    ('SaleChequeCreditSettlements', 'IX_SaleChequeCreditSettlements_AccVoucherId', ('PartyAccountId',)),
    ('SaleChequeInstallmentReceipts', 'IX_SaleChequeInstReceipts_AccVoucherId', ('BankAccountId', 'PartyAccountId')),
    ('SaleChequeCancellations', 'IX_SaleChequeCancellations_AccVoucherId', ('SalesAccountId', 'IncomeAccountId', 'PartyAccountId')),
]


def ddl(name):
    source = (ROOT / (name + '.cs')).read_text(encoding='utf8')
    match = re.search(r'public const string Sqlite = @"(.*?)";', source, re.S)
    assert match, name
    return match.group(1).replace('""', '"')


def quote(value):
    return '"' + value.replace('"', '""') + '"'


def upgrade(db):
    """Equivalent statements/ordering to the provider's transactional rebuild."""
    db.commit()
    fk_on = db.execute('PRAGMA foreign_keys').fetchone()[0]
    try:
        if fk_on:
            db.execute('PRAGMA foreign_keys=OFF')
        db.execute('BEGIN IMMEDIATE')
        try:
            for table, voucher_index, snapshots in TABLES:
                row = db.execute("SELECT sql FROM sqlite_master WHERE type='table' AND name=?", (table,)).fetchone()
                if not row:
                    continue
                create = row[0]
                historical = {'accvoucherid', *(name.lower() for name in snapshots)}
                required = [c[1] for c in db.execute(f'PRAGMA table_info({quote(table)})')
                            if c[1].lower() in historical and c[3]]
                if required:
                    sequence = db.execute('SELECT seq FROM sqlite_sequence WHERE name=?', (table,)).fetchone()
                    objects = [r[0] for r in db.execute("SELECT sql FROM sqlite_master WHERE tbl_name=? AND type IN ('index','trigger') AND sql IS NOT NULL ORDER BY type,name", (table,))]
                    relaxed = create
                    for column in required:
                        name = re.escape(column)
                        pattern = rf'((?:"{name}"|\[{name}\]|`{name}`|\b{name}\b)\s+INTEGER\s+)NOT\s+NULL'
                        relaxed, count = re.subn(pattern, r'\1NULL', relaxed, flags=re.I)
                        if not count:
                            raise ValueError('unrecognized historical column')
                    temporary = '__shop_upgrade_' + table
                    prefix = r'^\s*CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:"[^"]+"|\[[^\]]+\]|`[^`]+`|\w+)'
                    relaxed, count = re.subn(prefix, 'CREATE TABLE ' + quote(temporary), relaxed, flags=re.I | re.S)
                    if not count:
                        raise ValueError('unrecognized table')
                    db.execute(relaxed)
                    db.execute(f'INSERT INTO {quote(temporary)} SELECT * FROM {quote(table)}')
                    db.execute(f'DROP TABLE {quote(table)}')
                    db.execute(f'ALTER TABLE {quote(temporary)} RENAME TO {quote(table)}')
                    if sequence is not None:
                        db.execute('UPDATE sqlite_sequence SET seq=MAX(seq, ?) WHERE name=?', (sequence[0], table))
                        db.execute('INSERT INTO sqlite_sequence(name,seq) SELECT ?,? WHERE NOT EXISTS (SELECT 1 FROM sqlite_sequence WHERE name=?)', (table, sequence[0], table))
                    for sql in objects:
                        db.execute(sql)
                db.execute(f'DROP INDEX IF EXISTS {quote(voucher_index)}')
                db.execute(f'CREATE UNIQUE INDEX {quote(voucher_index)} ON {quote(table)} ([AccVoucherId]) WHERE [AccVoucherId] IS NOT NULL')
            if db.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name='SaleChequeCharges'").fetchone():
                present = {c[1] for c in db.execute('PRAGMA table_info(SaleChequeCharges)')}
                if 'InitialCashTrsVoucherId' not in present:
                    db.execute('ALTER TABLE SaleChequeCharges ADD COLUMN InitialCashTrsVoucherId INTEGER NULL REFERENCES TrsVouchers(Id) ON DELETE RESTRICT')
                db.execute('CREATE UNIQUE INDEX IF NOT EXISTS IX_SaleChequeCharges_InitialCashTrsVoucherId ON SaleChequeCharges(InitialCashTrsVoucherId) WHERE InitialCashTrsVoucherId IS NOT NULL')
                count = db.execute("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('SaleChequeChargeEvents','TrsVouchers')").fetchone()[0]
                if count == 2:
                    match = re.search(r'await ExecuteAsync\(connection, @"(UPDATE SaleChequeCharges AS c.*?)"\);', HELPER, re.S)
                    assert match
                    db.execute(match.group(1).replace('""', '"'))
            for table, index, key in [('SaleChequeReceipts', 'IX_SaleChequeReceipts_ChequeId', 'ChequeId'),
                                      ('SaleChequeInstallmentReceipts', 'IX_SaleChequeInstReceipts_ActiveInstallment', 'InstallmentId')]:
                if db.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name=?", (table,)).fetchone():
                    db.execute(f'DROP INDEX IF EXISTS {quote(index)}')
                    db.execute(f'CREATE UNIQUE INDEX {quote(index)} ON {quote(table)} ({quote(key)}) WHERE [ReversalDate] IS NULL')
            for table in [t[0] for t in TABLES] + ['SaleChequeCharges', 'SaleChequeReschedules', 'SaleChequeReturnLines']:
                if db.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name=?", (table,)).fetchone():
                    if db.execute(f'PRAGMA foreign_key_check({quote(table)})').fetchone():
                        raise ValueError('bad foreign key in ' + table)
            db.execute('COMMIT')
        except BaseException:
            db.execute('ROLLBACK')
            raise
    finally:
        if fk_on:
            db.execute('PRAGMA foreign_keys=ON')


def historical_db():
    db = sqlite3.connect(':memory:')
    db.executescript('''PRAGMA foreign_keys=ON;
        CREATE TABLE Transactions(Id INTEGER PRIMARY KEY, CompanyId INTEGER);
        CREATE TABLE TransactionLines(Id INTEGER PRIMARY KEY);
        CREATE TABLE SaleChequeCharges(Id INTEGER PRIMARY KEY, TransactionId INTEGER, CompanyId INTEGER);
        CREATE TABLE Cheques(Id INTEGER PRIMARY KEY, CompanyId INTEGER);
        CREATE TABLE Installments(Id INTEGER PRIMARY KEY);
        CREATE TABLE TrsAccounts(Id INTEGER PRIMARY KEY);
        CREATE TABLE TrsVouchers(Id INTEGER PRIMARY KEY, VoucherId INTEGER, Kind INTEGER);
        CREATE TABLE AccVouchers(Id INTEGER PRIMARY KEY);
        INSERT INTO Transactions VALUES(1,7),(2,7),(3,8);
        INSERT INTO TransactionLines VALUES(10),(20);
        INSERT INTO SaleChequeCharges VALUES(1,1,NULL),(2,2,8);
        INSERT INTO Cheques VALUES(1,7),(2,7);
        INSERT INTO Installments VALUES(1),(2);
        INSERT INTO TrsAccounts VALUES(1);
        INSERT INTO AccVouchers VALUES(10),(11),(12),(13),(14),(15),(16),(17),(18),(19);
        INSERT INTO TrsVouchers VALUES(40,10,0),(41,11,0),(42,11,0);''')
    for schema in ('SaleChequeChargeEventsSchemaV1', 'SaleChequeReceiptSchemaV1', 'SaleChequeReturnSchemaV1',
                   'SaleChequeCreditSettlementSchemaV1', 'SaleChequeInstallmentReceiptSchemaV1', 'SaleChequeCancellationSchemaV1'):
        db.executescript(ddl(schema))
    db.executescript('''ALTER TABLE SaleChequeReceipts ADD COLUMN ReversalOperationId TEXT;
        ALTER TABLE SaleChequeReceipts ADD COLUMN ReversalVoucherId INTEGER REFERENCES AccVouchers(Id) ON DELETE RESTRICT;
        ALTER TABLE SaleChequeReceipts ADD COLUMN ReversalDate TEXT;
        ALTER TABLE SaleChequeReceipts ADD COLUMN PreviousAccountKnown INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE SaleChequeReceipts ADD COLUMN PreviousTrsAccountId INTEGER;
        DROP INDEX IX_SaleChequeReceipts_ChequeId;
        CREATE UNIQUE INDEX IX_SaleChequeReceipts_ChequeId ON SaleChequeReceipts(ChequeId) WHERE ReversalVoucherId IS NULL;
        DROP INDEX IX_SaleChequeInstReceipts_ActiveInstallment;
        CREATE UNIQUE INDEX IX_SaleChequeInstReceipts_ActiveInstallment ON SaleChequeInstallmentReceipts(InstallmentId) WHERE ReversalVoucherId IS NULL;
        CREATE TABLE Audit(EventId INTEGER);
        CREATE TRIGGER AuditEvent AFTER INSERT ON SaleChequeChargeEvents BEGIN INSERT INTO Audit VALUES(new.Id); END;
        INSERT INTO SaleChequeChargeEvents(Id,OperationId,SaleChequeChargeId,AccVoucherId,Kind,Date,IncomeDelta)
            VALUES(5,'event-old',1,10,1,'2026-06-01','50'),(100,'deleted',2,11,1,'2026-06-01','7');
        DELETE FROM SaleChequeChargeEvents WHERE Id=100;
        INSERT INTO SaleChequeChargeEvents(Id,OperationId,SaleChequeChargeId,AccVoucherId,Kind,Date,IncomeDelta)
            VALUES(6,'ambiguous',2,11,1,'2026-06-01','7');
        INSERT INTO SaleChequeReceipts(OperationId,ChequeId,TransactionId,TrsAccountId,BankAccountId,PartyAccountId,AccVoucherId,Date,Amount)
            VALUES('receipt-old',1,1,1,101,102,12,'2026-06-02','250');
        INSERT INTO SaleChequeInstallmentReceipts(OperationId,InstallmentId,TransactionId,TrsAccountId,BankAccountId,PartyAccountId,AccVoucherId,Date,Amount)
            VALUES('inst-old',1,1,1,101,102,13,'2026-06-03','20');
        INSERT INTO SaleChequeReturns(OperationId,SaleTransactionId,ReturnTransactionId,SaleChequeChargeId,AccVoucherId,SalesAccountId,IncomeAccountId,PartyAccountId,Date,GoodsAmount,VatAmount,IncomeAmount,EpochBase,EpochIncome,PreviouslyReturnedBaseInEpoch,BalanceAfter,UncollectedChequeExcess,Decision,CreatedAt)
            VALUES('return-old',1,2,1,14,101,102,103,'2026-06-04','100','0','25','200','50','0','125','0',0,'2026-06-04');
        INSERT INTO SaleChequeReturnLines(SaleChequeReturnId,OriginalLineId,ReturnLineId,Quantity,GoodsAmount,VatAmount) VALUES(1,10,20,'1','100','0');
        INSERT INTO SaleChequeCreditSettlements(OperationId,Kind,SaleTransactionId,SaleChequeChargeId,PartyAccountId,AccVoucherId,Date,Amount,CreatedAt)
            VALUES('settlement-old',1,1,1,103,15,'2026-06-05','3','2026-06-05');
        INSERT INTO SaleChequeCancellations(OperationId,SaleTransactionId,SaleChequeChargeId,AccVoucherId,SalesAccountId,IncomeAccountId,PartyAccountId,Date,ReversedGoodsAmount,ReversedVatAmount,ReversedIncomeDelta,RefundedCashAmount,CreatedAt)
            VALUES('cancel-old',1,1,16,101,102,103,'2026-06-06','0','0','-25','0','2026-06-06');''')
    return db


class StoreLedgerUpgrade(unittest.TestCase):
    def test_history_fks_indexes_triggers_sequence_cash_link_and_idempotence(self):
        db = historical_db()
        before = {table: db.execute(f'SELECT * FROM {table}').fetchall() for table, _, _ in TABLES}
        upgrade(db)
        after = {table: db.execute(f'SELECT * FROM {table}').fetchall() for table, _, _ in TABLES}
        self.assertEqual(before, after)  # no voucher/account or event history rewritten
        self.assertEqual(db.execute('PRAGMA foreign_keys').fetchone()[0], 1)
        self.assertEqual(db.execute('PRAGMA foreign_key_check').fetchall(), [])
        self.assertEqual(db.execute('SELECT Id,InitialCashTrsVoucherId FROM SaleChequeCharges ORDER BY Id').fetchall(), [(1, 40), (2, None)])
        for table, index, snapshots in TABLES:
            cols = {r[1]: r[3] for r in db.execute(f'PRAGMA table_info({table})')}
            self.assertEqual(cols['AccVoucherId'], 0, table)
            self.assertTrue(all(cols[name] == 0 for name in snapshots), table)
            self.assertIn('IS NOT NULL', db.execute('SELECT sql FROM sqlite_master WHERE name=?', (index,)).fetchone()[0])
        upgrade(db)
        self.assertEqual(after, {table: db.execute(f'SELECT * FROM {table}').fetchall() for table, _, _ in TABLES})
        db.execute("INSERT INTO SaleChequeChargeEvents(OperationId,SaleChequeChargeId,AccVoucherId,Kind,Date,IncomeDelta) VALUES('new-1',1,NULL,2,'2026-06-02','1')")
        db.execute("INSERT INTO SaleChequeChargeEvents(OperationId,SaleChequeChargeId,AccVoucherId,Kind,Date,IncomeDelta) VALUES('new-2',1,NULL,2,'2026-06-03','1')")
        self.assertGreater(db.execute('SELECT MAX(Id) FROM SaleChequeChargeEvents').fetchone()[0], 100)
        self.assertEqual(db.execute('SELECT COUNT(*) FROM Audit WHERE EventId>100').fetchone()[0], 2)
        with self.assertRaises(sqlite3.IntegrityError):
            db.execute('DELETE FROM AccVouchers WHERE Id=10')
        with self.assertRaises(sqlite3.IntegrityError):
            db.execute('DELETE FROM TrsVouchers WHERE Id=40')
        # A reversal without ERP voucher permits exactly one later active receipt.
        db.execute("UPDATE SaleChequeReceipts SET ReversalDate='2026-06-07',ReversalOperationId='undo' WHERE OperationId='receipt-old'")
        db.execute("INSERT INTO SaleChequeReceipts(OperationId,ChequeId,TransactionId,TrsAccountId,Date,Amount) VALUES('receipt-new',1,1,1,'2026-06-08','250')")
        with self.assertRaises(sqlite3.IntegrityError):
            db.execute("INSERT INTO SaleChequeReceipts(OperationId,ChequeId,TransactionId,TrsAccountId,Date,Amount) VALUES('receipt-duplicate',1,1,1,'2026-06-09','250')")
        db.execute("UPDATE SaleChequeInstallmentReceipts SET ReversalDate='2026-06-07',ReversalOperationId='inst-undo' WHERE OperationId='inst-old'")
        db.execute("INSERT INTO SaleChequeInstallmentReceipts(OperationId,InstallmentId,TransactionId,TrsAccountId,Date,Amount) VALUES('inst-new',1,1,1,'2026-06-08','20')")
        with self.assertRaises(sqlite3.IntegrityError):
            db.execute("INSERT INTO SaleChequeInstallmentReceipts(OperationId,InstallmentId,TransactionId,TrsAccountId,Date,Amount) VALUES('inst-duplicate',1,1,1,'2026-06-09','20')")
        db.close()

    def test_bad_historical_reference_rolls_back_entire_upgrade(self):
        db = historical_db()
        db.commit(); db.execute('PRAGMA foreign_keys=OFF')
        db.execute("INSERT INTO SaleChequeReturnLines(SaleChequeReturnId,OriginalLineId,ReturnLineId,Quantity,GoodsAmount,VatAmount) VALUES(999,10,10,'1','1','0')")
        db.commit(); db.execute('PRAGMA foreign_keys=ON')
        before = db.execute('SELECT * FROM SaleChequeChargeEvents ORDER BY Id').fetchall()
        with self.assertRaisesRegex(ValueError, 'bad foreign key'):
            upgrade(db)
        self.assertEqual(before, db.execute('SELECT * FROM SaleChequeChargeEvents ORDER BY Id').fetchall())
        cols = {r[1]: r[3] for r in db.execute('PRAGMA table_info(SaleChequeChargeEvents)')}
        self.assertEqual(cols['AccVoucherId'], 1)
        self.assertNotIn('InitialCashTrsVoucherId', [r[1] for r in db.execute('PRAGMA table_info(SaleChequeCharges)')])
        self.assertEqual(db.execute('PRAGMA foreign_keys').fetchone()[0], 1)
        db.close()

    def test_company_backfill_uses_parent_and_never_overwrites_existing_ownership(self):
        db = historical_db(); upgrade(db)
        source = (ROOT / 'SaleChequeCompanyOwnershipSchemaV1.cs').read_text(encoding='utf8')
        links = re.findall(r'\("(SaleCheque\w+)", "(\w+)", "(\w+)"\)', source)
        self.assertEqual(len(links), 11)
        for child, key, parent in links:
            if db.execute("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN (?,?)", (child, parent)).fetchone()[0] == 2:
                db.execute(f'''UPDATE [{child}] SET [CompanyId]=(SELECT [CompanyId] FROM [{parent}] WHERE [Id]=[{child}].[{key}])
                    WHERE [CompanyId] IS NULL AND EXISTS (SELECT 1 FROM [{parent}] WHERE [Id]=[{child}].[{key}] AND [CompanyId] IS NOT NULL)''')
        self.assertEqual(db.execute('SELECT Id,CompanyId FROM SaleChequeCharges ORDER BY Id').fetchall(), [(1, 7), (2, 8)])
        self.assertEqual(db.execute('SELECT CompanyId FROM SaleChequeChargeEvents WHERE Id=5').fetchone()[0], 7)
        self.assertEqual(db.execute('SELECT CompanyId FROM SaleChequeChargeEvents WHERE Id=6').fetchone()[0], 8)
        self.assertEqual(db.execute('SELECT CompanyId FROM SaleChequeReturnLines').fetchone()[0], 7)
        db.close()

    def test_sql_server_script_preserves_and_restores_keys_and_fails_safe(self):
        for table, index, _ in TABLES:
            self.assertIn(f'ALTER TABLE [dbo].[{table}] ALTER COLUMN [AccVoucherId] int NULL', HELPER)
            self.assertIn(f'CREATE UNIQUE INDEX [{index}]', HELPER)
        for token in ('sys.foreign_key_columns', 'Cannot safely upgrade a composite voucher foreign key',
                      'fk.is_not_trusted', 'fk.is_disabled', 'fk.delete_referential_action_desc',
                      'fk.update_referential_action_desc', 'fk.is_not_for_replication',
                      'BEGIN IMMEDIATE', 'PRAGMA foreign_key_check', 'ROLLBACK', 'sqlite_sequence',
                      'BeginTransactionAsync()', '[ReversalDate] IS NULL'):
            self.assertIn(token, HELPER)


if __name__ == '__main__':
    unittest.main()
