# Regression tests for build / expression-tree compatibility

Run from the repository root with .NET SDK 8.0.4xx (tested: 8.0.425):

```powershell
dotnet restore tests/compatibility/Inventory.Compatibility.Tests.csproj --configfile inventory/NuGet.config
dotnet test tests/compatibility/Inventory.Compatibility.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
```

The test project imports the production `inventory/Directory.Build.props`, including its C# 12 language policy, and directly links the production Popover helper. IP tests invoke the actual static target-building methods by reflection, not copies of their implementations.

23 cases cover:

- Invariant fixed-menu and calendar CSS under en-US, fa-IR, de-DE and ar-SA.
- Both IPv4 target builders: network-byte order, high unsigned addresses, /29 and /30 networks, /24 fallback, and existing /31 and /32 behavior.
- Array Contains binding to Enumerable rather than a span overload, interpretation of expression trees, and EF Core SQLite evaluation/translation.

These tests never perform network scans, connect to the deployed application, or use a production database. The database test uses an independent in-memory SQLite connection. Browser rendering and full application initialization are outside this suite.

See `../../FIXES-2026-09-09.md` for the cause, validation results and deployment instructions.
