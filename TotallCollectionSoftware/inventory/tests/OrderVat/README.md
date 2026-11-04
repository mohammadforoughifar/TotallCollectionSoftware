# Order VAT regression probe

Run `dotnet run --project inventory/tests/OrderVat/OrderVat.csproj` from the directory containing `inventory/`. The probe runs the real shared calculation, legacy SQLite schema repair twice, SQL Server migration generation, and SQLite service tests covering product defaults, purchase/sale snapshots, editing, backward compatibility, mixed payment, profit and printing. It needs .NET 8 and the repository's NuGet dependencies; it does not connect to a SQL Server.

Also run the Blazor build and browser checks: `/products` and warehouse product editor; purchase and sale rows with 0, 10, 100 and >100; editing after the product default changes; cash/cheque/installment/credit totals; printing; Tab/Enter and mobile/tablet layouts.
