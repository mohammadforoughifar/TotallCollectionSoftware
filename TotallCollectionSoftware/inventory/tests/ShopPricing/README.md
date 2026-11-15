# Pricing regression probes

* `python3 -m unittest discover -s tests/ShopPricing -p 'test*.py' -v` runs executable SQLite schema/date/ownership tests and source contracts without .NET.
* `dotnet run --project tests/ShopPricing/MinimumProfit/MinimumProfit.csproj` exercises the production decimal markup/commission helper (10,000 randomized cases).
* `dotnet run --project tests/ShopPricing/ShopPricing.csproj` exercises EF Core/SQLite orders, dated prices, purchase history, policy, permissions, companies, transaction rollback, and SQL Server migration script generation. A running SQL Server is not needed to generate the script. A real SQL Server and browser/API end-to-end test remain separately necessary.

The Arena environment used for this change has no .NET SDK. The EF probe has been written but has **not** been compiled or run here.
