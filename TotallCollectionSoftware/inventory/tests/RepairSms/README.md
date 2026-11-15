# Repair SMS offline checks

`dotnet run --project tests/RepairSms/RepairSms.csproj` uses SQLite in memory and a fake `IKavenegarSmsGateway`. It never connects to Kavenegar. It covers registration/Ready, cash and credit delivery, free and mixed warranty delivery, stage-failure/stock rollback, historical/idempotent delivery, invalid customer data, duplicate prevention, disabled/missing/unknown events, abandoned claims, company and phone matching, and concurrent claims. Run it on a machine with .NET 8 and access to NuGet; this environment may lack both.

`python3 -m unittest discover -s tests/RepairSms -p 'test_*.py' -v` runs source/schema contracts and SQLite tests with Python. These are not a substitute for the .NET, EF, Razor, real SQL Server migration, or actual NuGet package tests.

`dotnet run --project tests/RepairSmsOffline/RepairSmsOffline.csproj` compiles the production queue, policy, and product-kind guard against small in-memory EF/hosting/gateway shims, so it needs no SMS service or external package. It exercises queue decisions but **does not** validate real EF translation, transactions, database concurrency, migrations or Razor.

`python3 tests/RepairSms/run_invoice_contracts.py` copies the production `InvoiceAsync` into a temporary .NET 8 console harness. Its inventory, database, transaction and queue are fake; it checks delivery control flow, event order, and rollback paths, **not** real financial/stock accounting or EF. Pass `--emit /tmp/invoice-contract.cs` to use another C# runner.

The offline fixture deliberately defines the same `Party` and `Product` names in both Data and DTO namespaces, so an ambiguous production signature fails compilation.
