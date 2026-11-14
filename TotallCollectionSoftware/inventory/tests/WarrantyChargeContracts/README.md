# Warranty charging tests

`dotnet run --project tests/WarrantyChargeContracts` compiles the production DTOs,
rounding, invoice-line calculations and print builders into a small executable.
It checks free/chargeable/unused/legacy contracts, whole-rial rounding, escaped
invoice descriptions, paper selection, free-delivery totals and repair-report
calculations (including legacy data). Its two small
model stubs isolate those calculations; it is not an EF or live UI test.

`dotnet run --project tests/ShopWarrantyTradeInExpenses` runs the separate
SQLite/EF integration scenarios, including finalization, idempotency, customer
statements, actual receipts, mixed repairs, rollback on insufficient stock,
future-dated stock and draft retry checks. A normal .NET 8 SDK and the
project's NuGet dependencies are required.

`python3 -m unittest discover -s tests/ShopWarrantyTradeInExpenses -p 'test_*.py'`
executes SQLite schema migration checks and source invariants. Those checks do
not replace the EF integration executable or a live UI/provider test.
