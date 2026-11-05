# Sale cheque charge — store-only checks

The operational store workflows do not require an ERP rule, account, fiscal year, voucher, permission or experimental flag. A real store treasury account remains required when money is actually received or paid. Historical ERP links are retained for audit and are not rewritten or deleted.

## Tests

From the `inventory` directory:

```bash
python3 -m unittest discover -s tests/SaleChequeCharge -p 'test_*.py'
dotnet run --project tests/SaleChequeCharge/SaleChequeCharge.csproj
```

The Python tests execute historical SQLite DDL and a statement-for-statement analogue of the transactional rebuild, including history, foreign keys, uniqueness, triggers, AUTOINCREMENT high-water marks, reversal/re-receipt, ambiguous cash links, rollback and repeatability. The other Python contracts inspect the source. These tests **do not** compile or execute the C# service.

The .NET 8 probe is designed to exercise the actual SQLite/EF upgrade helper and store workflows with no ERP records: registration and rollback, mixed cash and cancellation, statement openings, return decisions, adjustment, cheque replacement, receipt/reversal/re-receipt and operation replay, refund/credit allocation, installments, company scope and profit permissions. It also generates SQL Server migration scripts; it does **not** connect to a running SQL Server. The .NET SDK and project NuGet dependencies are required.

Before a production upgrade, back up the database and run the .NET probe and application tests against a copy of the existing database. Test the SQL Server migration on a copy of a representative SQL Server database as well. The schema repair aborts on broken foreign keys or unsafe upgrades instead of deleting history. Historical records without an initial event remain readable only when unchanged; unsafe writes fail rather than inventing history. In a multi-company installation, an account selected for a cheque workflow must belong explicitly to the invoice's company.

## Existing ordinary-order limitation

Ordinary purchase/sale orders without a cheque-charge ledger historically save their cash component and selected account on the order but do not automatically create a treasury movement. This change does not infer or backfill those historical movements: they may already have matching manual treasury vouchers. Ordinary-order editing/deletion also needs an auditable reversal design before automatic posting can be added safely. Consequently, the bank balance covered here includes store cheque-charge cash, receipts/reversals, refunds and existing treasury activity, but **not** an automatically derived cash movement for an ordinary order. Reconcile those orders explicitly; do not assume that saving one has changed the bank balance.

## Existing multi-company treasury limitation

The legacy general treasury voucher and cheque tables do not have their own company ownership. Account lists and the cheque-charge operational history are scoped and checked, but the old general treasury voucher/cheque listings and dashboard are not fully isolated by company. Do not treat this change as complete multi-tenant isolation for those legacy screens; they need a separate ownership and reconciliation migration before use across mutually untrusted companies.

## Legacy ownerless bank accounts

In databases with more than one company, ownerless legacy store records are no longer automatically assigned to the first company; they remain unchanged and need an administrator's documented ownership review. A legacy account whose `CompanyId` is null is hidden from company-scoped bank lists and cannot be selected for a cheque workflow. It is **not** silently assigned, and no transaction is deleted. A user with store bank-management permission can create a new account for the active company.

To restore an old account, a database administrator must first back up the database and explicitly audit its opening balance, linked vouchers, transfers, cheques and all store receipt/reversal histories to determine whether the entire account really belongs to one company. After retaining a record of that review, the administrator may assign that company to the existing null `TrsAccounts.CompanyId` using an explicitly authorized database repair. If any movement belongs to another company, or ownership is uncertain, do not assign it to one company: reconcile the mixed history separately on a copy and verify balances before applying a reviewed repair. The normal account editor deliberately refuses to change ownership of an account with protected cheque history.

## Operational invariants

- Cheque income is separate from merchandise, inventory cost and VAT. The initial calculation is rechecked on the server; a difference in payment is not silently assigned to a cheque.
- Due-date changes use the remaining goods base including VAT and a saved rate/period. Bounce alone adds no income; reductions beyond income create customer credit.
- Returns restore stock using a reliable frozen cost, reverse the appropriate income, and require a recorded decision when uncleared cheques exceed the remaining balance.
- Receipts, reversals, installments, customer-credit settlements and cancellation are atomic and use unique operation IDs. Income events and treasury movements feed statements and reports without ERP postings.
- Existing linked financial history is protected from independent voucher edits. The balance calculator and account-flow report include store receipts and reversals once; summary and detailed statements include the original cash and any subsequent refund even across a date boundary.
- `Orders.ViewProfit` remains separate from shop read/manage rights; invoice lookups do not expose sale cost without it.
