# Repair SMS offline checks

`dotnet run --project tests/RepairSms/RepairSms.csproj` uses SQLite in memory and a fake `IKavenegarSmsGateway`. It never connects to Kavenegar. It covers both repair status paths, duplicate prevention, disabled/missing/unknown events, abandoned claims, company and phone matching, and concurrent workers. Run it on a machine with .NET 8 and access to NuGet; this environment may lack both.

`python3 -m unittest discover -s tests/RepairSms -p 'test_*.py' -v` runs source/schema contracts and SQLite tests with Python. These are not a substitute for the .NET, EF, Razor, real SQL Server migration, or actual NuGet package tests.

`dotnet run --project tests/RepairSmsOffline/RepairSmsOffline.csproj` compiles the production queue and policy against small in-memory EF/hosting/gateway shims, so it needs no SMS service or external package. It exercises queue decisions but **does not** validate real EF translation, transactions, database concurrency, migrations or Razor.
