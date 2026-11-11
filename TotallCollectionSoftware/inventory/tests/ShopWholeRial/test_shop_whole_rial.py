"""Executable SQLite-upgrade tests plus narrowly scoped architecture contracts.

The C# project in this folder covers actual service behavior with a .NET SDK.
"""
from pathlib import Path
import re
import sqlite3
import unittest

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "src"

def read(path):
    return (SRC / path).read_text(encoding="utf-8")

def csharp_verbatim(name, text):
    match = re.search(r'public const string ' + re.escape(name) + r'\s*=\s*@"(.*?)"\s*;', text, re.S)
    if not match:
        raise AssertionError(f"constant {name} not found")
    return match.group(1).replace('""', '"')

class WholeRialContract(unittest.TestCase):
    def test_sqlite_defaults_are_additive_idempotent_and_keep_user_decisions(self):
        schema = read("Inventory.Api/Data/ShopQuantitySchemaV1.cs")
        defaults = csharp_verbatim("SqliteDefaults", schema)
        db = sqlite3.connect(":memory:")
        self.addCleanup(db.close)
        db.execute("CREATE TABLE MeasureUnits (Id INTEGER PRIMARY KEY, CompanyId INTEGER, Name TEXT NOT NULL, IsActive INTEGER)")
        db.executemany("INSERT INTO MeasureUnits VALUES (?, ?, ?, 1)", [
            (1, 1, "عدد"), (2, 1, "کیلوگرم"), (3, 1, "واحد خاص"),
            (4, 2, "عدد"), (5, 2, "متر"), (6, 2, "بسته")])
        db.execute("ALTER TABLE MeasureUnits ADD COLUMN AllowsFractionalQuantity INTEGER NULL")
        db.execute("UPDATE MeasureUnits SET AllowsFractionalQuantity = 1 WHERE Id = 4")
        for _ in range(2):
            db.executescript(defaults)
        actual = db.execute("SELECT Id, AllowsFractionalQuantity FROM MeasureUnits ORDER BY Id").fetchall()
        self.assertEqual([(1, 0), (2, 1), (3, None), (4, 1), (5, 1), (6, 0)], actual)
        db.execute("UPDATE MeasureUnits SET AllowsFractionalQuantity = 0 WHERE Id = 2")
        db.executescript(defaults)
        self.assertEqual(0, db.execute("SELECT AllowsFractionalQuantity FROM MeasureUnits WHERE Id = 2").fetchone()[0])
        self.assertEqual(6, db.execute("SELECT COUNT(*) FROM MeasureUnits").fetchone()[0])
        self.assertIn("PRAGMA table_info('MeasureUnits')", schema)
        self.assertIn('if (!present.Contains("AllowsFractionalQuantity"))', schema)
        self.assertIn('COL_LENGTH', csharp_verbatim("SqlServer", schema))
        self.assertIn('WHERE [AllowsFractionalQuantity] IS NULL', csharp_verbatim("SqlServer", schema))
        self.assertNotIn('DROP TABLE', schema.upper())

    def test_new_company_units_use_parameterized_company_scoped_idempotent_seed(self):
        source = read("Inventory.Api/Data/Seeder.cs")
        match = re.search(r'ExecuteSqlInterpolatedAsync\(\$@"(.*?)"\);', source, re.S)
        self.assertIsNotNone(match)
        sql = re.sub(r'\{[^{}]+\}', '?', match.group(1))
        db = sqlite3.connect(":memory:")
        self.addCleanup(db.close)
        db.executescript("""
            CREATE TABLE MeasureUnits (Id INTEGER PRIMARY KEY, CompanyId INTEGER, Name TEXT,
                IsActive INTEGER, AllowsFractionalQuantity INTEGER, CreatedAt TEXT);
            CREATE UNIQUE INDEX IX_MeasureUnits_Company_Name ON MeasureUnits (CompanyId, Name);
            INSERT INTO MeasureUnits VALUES (1, 22, 'کیلوگرم', 1, 0, 'old');
        """)
        for _ in range(2):
            for name, fractional in [('عدد', 0), ('کیلوگرم', 1), ('گالن', None)]:
                db.execute(sql, (22, name, 1, fractional, 'now', 22, name))
        actual = db.execute('SELECT CompanyId, Name, AllowsFractionalQuantity FROM MeasureUnits ORDER BY Name').fetchall()
        self.assertCountEqual([(22, 'عدد', 0), (22, 'گالن', None), (22, 'کیلوگرم', 0)], actual)
        companies = read("Inventory.Api/Controllers/System/SystemCompaniesController.cs")
        self.assertIn('Seeder.EnsureUnitsForCompanyAsync(_db, c.Id)', companies)
        self.assertIn('Seeder.EnsureUnitsForCompanyAsync(_db, item.Id)', companies)

    def test_sqlite_purchase_basis_date_company_and_same_day_order(self):
        # Independent SQL exercise of the resolver's stated criteria; this does
        # not claim that EF's provider translation was executed.
        db = sqlite3.connect(":memory:")
        self.addCleanup(db.close)
        db.executescript("""
            CREATE TABLE transactions (id INTEGER, company INTEGER, date TEXT, type TEXT);
            CREATE TABLE lines (id INTEGER, transaction_id INTEGER, company INTEGER,
                                product_id INTEGER, quantity NUMERIC, gross NUMERIC, discount NUMERIC);
            INSERT INTO transactions VALUES
                (1,1,'2026-11-01','Purchase'), (9,1,'2026-11-06','Purchase'),
                (11,1,'2026-11-06','Purchase'), (12,2,'2026-11-06','Purchase'),
                (13,1,'2026-11-07','Purchase'), (14,1,'2026-11-06','Sale');
            INSERT INTO lines VALUES
                (1,1,1,7,1,70000000,0), (9,9,1,7,1,66000000,1000000),
                (11,11,1,7,1,75000000,0), (12,12,2,7,1,1,0),
                (13,13,1,7,1,90000000,0), (14,14,1,7,1,1,0);
        """)
        sql = """SELECT t.id,l.gross-l.discount FROM lines l
          JOIN transactions t ON l.transaction_id=t.id
          WHERE l.product_id=? AND t.company=? AND l.company=? AND t.type='Purchase'
            AND t.date<? AND (t.date<? OR t.id<?)
          ORDER BY t.date DESC,t.id DESC,l.id DESC LIMIT 1"""
        def basis(date, next_date, cutoff):
            return db.execute(sql, (7, 1, 1, next_date, date, cutoff)).fetchone()
        self.assertEqual((9, 65000000), basis('2026-11-06', '2026-11-07', 10))
        self.assertEqual((11, 75000000), basis('2026-11-06', '2026-11-07', 99))
        self.assertEqual((1, 70000000), basis('2026-11-05', '2026-11-06', 99))
        self.assertEqual((13, 90000000), basis('2026-11-07', '2026-11-08', 99))

    def test_editable_and_company_owned_units(self):
        dto = read("Inventory.Shared/Dtos/CatalogDtos.cs")
        unit = dto.split("public class MeasureUnit", 1)[1].split("public class", 1)[0]
        product = dto.split("public class Product\n", 1)[1]
        self.assertIn("bool? AllowsFractionalQuantity", unit)
        self.assertIn("bool? AllowsFractionalQuantity", product)
        units_ui = read("Inventory.Client/Pages/Catalog/Units.razor")
        self.assertIn("form.AllowsFractionalQuantity = false", units_ui)
        self.assertIn("form.AllowsFractionalQuantity = true", units_ui)
        service = read("Inventory.Api/Services/Catalog/InventoryService.cs")
        self.assertIn("p.CompanyId == entity.CompanyId && p.Unit == oldName", service)
        self.assertIn("u.CompanyId == unitOwner && u.Name == entity.Unit", service)
        self.assertIn("(product.CompanyId, (product.Unit ?? \"\").Trim())", service)
        self.assertIn("برای «{product.Name}» مشخص نیست", service)

    def test_every_shop_mutation_uses_the_same_line_math(self):
        service = read("Inventory.Api/Services/Catalog/InventoryService.cs")
        self.assertGreaterEqual(service.count("await ValidateShopLineRulesAsync(cmd, products"), 3)
        self.assertGreaterEqual(service.count("ShopInvoiceLineMath.Total(cmd.Lines"), 3)
        self.assertGreaterEqual(service.count("VatAmount = amounts.VatAmount"), 3)
        self.assertIn("old.ProductId == line.ProductId && old.Quantity == line.Quantity", service)
        self.assertGreaterEqual(service.count("originalLines.TryGetValue(l.Id, out var existingLine)"), 2)
        math = read("Inventory.Api/Services/Catalog/ShopInvoiceLineMath.cs")
        self.assertIn("original!.GrossAmount ?? OrderLineAmounts.Gross", math)
        self.assertIn("original.VatAmount", math)
        self.assertIn("ShopInvoiceAmounts.Gross(input.Quantity, input.Price)", math)
        shared = read("Inventory.Shared/ShopInvoiceAmounts.cs")
        self.assertIn("MidpointRounding.AwayFromZero", shared)
        self.assertIn("TryGross", shared)
        self.assertIn("مبلغ سطر یا ارزش افزوده خارج از محدوده", math)
        self.assertIn("جمع فاکتور خارج از محدوده", math)

    def test_form_never_silently_repairs_price_or_bad_quantity(self):
        form = read("Inventory.Client/Pages/Orders/OrderForm.razor")
        quantity = read("Inventory.Client/Pages/Orders/ShopQuantityInput.cs")
        price = read("Inventory.Client/Pages/Orders/PurchaseSalePriceInput.cs")
        self.assertIn("ShopQuantityInput.TryParse(QtyText", form)
        self.assertIn("ShopQuantityInput.Format(value?.ToString())", form)
        self.assertIn("if (!Parts(value, true", quantity)
        self.assertIn("return typed", quantity)
        self.assertIn("!ShopInvoiceAmounts.IsWholePrice(l.OriginalPrice.Value)", form)
        self.assertIn("PurchaseSalePriceInput.TryParse(priceText", form)
        self.assertIn("!dot", price)
        self.assertIn("TryParseLegacy", price)
        self.assertIn("OriginalDiscountAmount.Value", form)
        catalog = read("Inventory.Client/Pages/Catalog/Products.razor")
        self.assertIn("!ShopInvoiceAmounts.IsWholePrice(form.SalePrice)", catalog)
        service = read("Inventory.Api/Services/Catalog/InventoryService.cs")
        self.assertIn("var purchase = ReadWholePrice(row.Cell(5))", service)
        self.assertIn("var sale = ReadWholePrice(row.Cell(6))", service)
        self.assertIn('decimal.TryParse(raw.Replace(",", ""), NumberStyles.None', service)
        self.assertIn("_db.ChangeTracker.Clear()", service)
        opening = read("Inventory.Client/Pages/Shop/OpeningBalanceWizard.razor")
        self.assertIn("PurchaseSalePriceInput.TryParse(priceText, out var stockUnitPrice)", opening)
        self.assertIn("ShopQuantityRules.IsWhole(stockQuantity)", opening)
        # Party opening remains a monetary form; it does not ask for a product.
        party_section = opening.split("ثبت مانده افتتاحیه شخص", 1)[1].split("ثبت چک افتتاحیه", 1)[0]
        self.assertNotIn("stockProductId", party_section)
        self.assertIn('MoneyInput @bind-Value="partyBalance.Amount"', party_section)

    def test_guard_diagnostic_permission_and_shared_date_company_resolver(self):
        controller = read("Inventory.Api/Controllers/Sales/OrdersController.cs")
        endpoint = controller.split('[HttpGet("minimum-profit-basis")]', 1)[1].split('[HttpGet(', 1)[0]
        self.assertIn("CanSeeProfitAsync()", endpoint)
        self.assertIn("GetMinimumProfitBasisAsync", endpoint)
        guard = read("Inventory.Api/Services/Catalog/ShopMinimumProfitGuard.cs")
        pricing = read("Inventory.Api/Services/Catalog/ShopPricingService.cs")
        self.assertIn("t.CompanyId == company && l.CompanyId == company", guard)
        self.assertIn("t.Id < cutoffId", guard)
        self.assertIn("ShopMinimumProfit.MeetsPurchase", guard)
        self.assertIn("ShopMinimumProfitGuard.FindPurchasesAsync", pricing)
        self.assertIn("MidpointRounding.AwayFromZero", pricing)
        self.assertIn("unchangedIndexes?.Contains(x.index) != true", guard)
        ui = read("Inventory.Client/Pages/Orders/OrderForm.razor")
        self.assertIn("مبنای کنترل سود در تاریخ فاکتور", ui)
        self.assertIn("آخرین خرید کلی", ui)
        self.assertIn("basis.PurchaseQuantity.Value.Qty()", ui)
        self.assertIn("@l.Price.MoneyExact()", ui)
        orders = read("Inventory.Client/Pages/Orders/Orders.razor")
        self.assertIn("@line.RemainingQuantity.Qty()", orders)
        self.assertIn("l.Price % 1m != 0m", orders)
        self.assertIn("returnPreview.TotalCredit.MoneyExact()", orders)
        pdf = read("Inventory.Api/Services/Export/PdfWriter.cs")
        self.assertIn('n.ToString("#,##0.###", System.Globalization.CultureInfo.InvariantCulture) : Fa.Number(n)', pdf)
        self.assertIn("preserveFractionalDigits = false", pdf)
        exports = read("Inventory.Api/Services/Export/ExportService.cs")
        self.assertIn('new ExportColumn("قیمت خرید", ExportValueKind.Money) { PreserveFractionalDigits = true }', exports)
        self.assertIn('new ExportColumn("موجودی کل", ExportValueKind.Number) { Sum = true, PreserveFractionalDigits = true }', exports)
        self.assertIn("preserveFractionalDigits ? NumberFormat : MoneyFormat", read("Inventory.Api/Services/Export/ExcelWriter.cs"))
        self.assertIn("version != lastPurchaseRequestVersion", ui)

if __name__ == "__main__":
    unittest.main()
