"""Run equivalent SQLite DDL and guard source contract; EF execution is in Program.cs."""
import re
import sqlite3
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2] / 'src'
def read(path): return (ROOT / path).read_text(encoding='utf8')

class CalculatorSchemaTests(unittest.TestCase):
    def test_columns_are_additive_and_repairable_after_partial_migration(self):
        src = read('Inventory.Api/Data/SaleChequeCalculatorSchemaV1.cs')
        columns = re.findall(r'\("(Calculator\w+)", "(TEXT|INTEGER)"\)', src)
        self.assertEqual(columns, [('CalculatorBaseAmount', 'TEXT'), ('CalculatorRatePercent', 'TEXT'), ('CalculatorMonths', 'INTEGER')])
        db = sqlite3.connect(':memory:')
        db.execute('CREATE TABLE SaleChequeCharges (Id INTEGER PRIMARY KEY, Amount TEXT NOT NULL, CalculatorBaseAmount TEXT)')
        db.execute("INSERT INTO SaleChequeCharges (Id, Amount) VALUES (1, '50')")
        for _ in range(2):
            present = {row[1].lower() for row in db.execute('PRAGMA table_info(SaleChequeCharges)')}
            for name, kind in columns:
                if name.lower() not in present:
                    db.execute(f'ALTER TABLE "SaleChequeCharges" ADD COLUMN "{name}" {kind} NULL')
        self.assertEqual(db.execute('SELECT Amount, CalculatorBaseAmount, CalculatorRatePercent, CalculatorMonths FROM SaleChequeCharges').fetchone(), ('50', None, None, None))
        db.close()
        self.assertEqual(read('Inventory.Api/Data/DbInitializer.cs').count('SaleChequeCalculatorSchemaV1.EnsureAsync(db)'), 2)

    def test_posting_checks_server_calculation_with_independent_rate(self):
        s = read('Inventory.Api/Services/Invoicing/SaleChequeChargePosting.cs')
        for part in ('SaleChequeInitialCalculation.Calculate(charge.CalculatorBaseAmount.Value',
                     'charge.CalculatorRatePercent.Value, charge.CalculatorMonths.Value',
                     'calculatedInitial != charge.InitialAmount', 'charge.CalculatorBaseAmount.Value > charge.BaseAmount',
                     'charge.RatePercent < 0'):
            self.assertIn(part, s)
        self.assertIn('SaleChequeCalculatorSchemaV1.SqlServer', read('Inventory.Api/Migrations/20261104170000_AddSaleChequeCalculatorSnapshot.cs'))

if __name__ == '__main__': unittest.main()
