# Whole-rial shop regressions

* `python3 -m unittest discover -s tests/ShopWholeRial -p 'test*.py' -v` exercises SQLite schema upgrades and source-wiring contracts without an SDK.
* `dotnet run --project tests/ShopWholeRial` runs the actual decimal and input helpers, SQL Server migration generation, Excel/PDF formatting, and SQLite/EF purchase, sale, history, unit and minimum-profit integration cases. SQL Server execution and live browser/API tests still require their own environments.

## Manual acceptance on a backed-up test database

1. Apply the migration/start the upgraded API. Verify standard units have editable classifications, custom units need a selection, and a newly created company's defaults are isolated.
2. A new fractional-rial unit price must produce an error in purchase, sale and catalog entry/import. Existing fractional prices and unchanged historical invoices must remain visible and unchanged; using an old price in a new invoice requires correction.
3. A quantity of 1.5 is rejected for a whole-only unit in both purchase and sale; it is accepted for a fractional unit. For 1.5 × 1,001 rials, the line gross is 1,502 rials (also check detail, payment and print). VAT keeps its existing calculation.
4. Enable and save 20% minimum markup, create an eligible purchase of 65,000,000 rials, then check that a sale of 76,000,000 fails and 78,000,000 succeeds with no discount or commission. An authorized user can press F7 on the sale row to inspect the policy, company and dated purchase basis. Verify disabled-policy and no-purchase cases separately.
5. Check historical and fractional quantities/prices in stock/product views and their affected exports; confirm unrelated PDF/Excel formatting and previous cheque/payment workflows remain unchanged.

The exact basis for a previously reported sale cannot be determined from the source code alone; inspect its company, date, eligible purchases, policy, discounts, referrer and deployed version.
