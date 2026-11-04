# Sale cheque charge — phase 1 probe

Run `dotnet run --project inventory/tests/SaleChequeCharge/SaleChequeCharge.csproj` from the parent of `inventory/`. This covers the shared calculation, SQLite schema repair and SQL Server migration generation. It requires .NET 8 and the project's NuGet packages.

This phase introduces the isolated data structure, calculation, independent-account setting, an unconnected voucher amount planner, and a transaction-bound posting service. It does **not** enable invoice posting, cheque changes, returns, or a complete financial workflow. Statement and sales reports contain preliminary read-only separation and filtering for existing charge rows, but adjustments and settlement history are not implemented. Do not deploy it as a completed financial feature.

The posting service is intentionally not connected to the invoice API. Its .NET probe checks account separation, mixed payment, duplicate prevention, closed fiscal years, and rollback. Run the project before connecting it to production.

The EF probe also covers statement opening balances, 200 + 50 separation, sales-report filtering, goods-only statistics (including the operations dashboard), and mapping of the selected treasury bank. Sales totals and trends in the general/admin dashboards exclude the cheque-income row, while payment breakdowns continue to show actual payment components. Syntax and structural tests are not a substitute for running it with .NET and both supported database providers.

New cheques retain the first known due date. Legacy cheque dates stay unknown rather than being guessed, and legacy edits preserve known dates only when cheque IDs match. The correction amount planner is also isolated: it does not write vouchers, change a cheque, refund goods, or settle an account.

A transaction-bound initial registration service now computes the initial calculator result on the server and rolls back the caller transaction on failure. It is still intentionally unconnected to production invoice creation; cheque changes, settlements, returns and end-to-end runtime tests must be completed before activation.

The financial event table preserves the voucher link and account snapshot. The dedicated, permission-checked income report and profit/loss dashboard read confirmed linked voucher lines, excluding unrelated activity on the same account. They reject missing account snapshots and inconsistent event amounts. At present only initial posting writes events; rescheduling, returns, settlements, and historical customer-statement timing remain unimplemented.

User decisions for the unfinished workflow: customer credit can be applied to a later purchase or paid out; the destination bank is selected at cheque collection; returned goods' proportional base includes goods VAT; an uncleared cheque can be replaced or retained to cover further goods, as explicitly chosen by the customer. Holding a cheque is not the same as cash receipt; collection or a replacement must not be counted twice. Recording a decision, allocating it to later sales, and posting corresponding accounting entries are still required before this can be enabled.

The isolated return calculator also handles a return after an intervening reschedule using the remaining goods base and remaining income, with exact midpoint-to-even whole-rial rounding. It is not yet wired to a return operation. Run the .NET probe to verify it; the Python tests do not execute C#.
