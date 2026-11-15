# Rounded invoice payment distribution

Run from `inventory` with the .NET 8 SDK:

```bash
dotnet run --project tests/InstallmentRounding/InstallmentRounding.csproj
```

This standalone probe compiles the actual shared rounding helper without EF or database dependencies. It checks examples, exact sums, one-to-60 rows, fractional and whole-unit remainders, exhaustive small-value feasibility boundaries, maximum supported totals, invalid inputs and 21,000 deterministic randomized schedules (including near-maximum values). It does not execute Razor, the API, SQL Server, or the real financial workflows; use the `SaleChequeCharge` probe and application tests for those.
