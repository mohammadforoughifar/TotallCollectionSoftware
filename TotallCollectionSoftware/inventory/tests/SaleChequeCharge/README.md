# Sale cheque charge — store-only checks

The operational store workflows do not require an ERP rule, account, fiscal year, voucher, permission or experimental flag. A real store treasury account remains required when money is actually received or paid. Historical ERP links are retained for audit and are not rewritten or deleted.

## Tests

From the `inventory` directory:

```bash
python3 -m unittest discover -s tests/SaleChequeCharge -p 'test_*.py'
dotnet run --project tests/SaleChequeCharge/SaleChequeCharge.csproj
```

The Python tests execute historical and cash-ledger SQLite DDL, small arithmetic mirrors and a statement-for-statement analogue of the transactional rebuild, including history, foreign keys, uniqueness, triggers, AUTOINCREMENT high-water marks, reversal/re-receipt, ambiguous cash links, rollback and repeatability. The other Python contracts inspect the source. These tests **do not** compile or execute the C# service.

The .NET 8 probe is designed to exercise the actual SQLite/EF upgrade helper, ordinary cash, cheque, installment and credit-receipt flows and store workflows with no ERP records: registration and rollback, mixed cash and cancellation, statement openings, return decisions, adjustment, cheque replacement, receipt/reversal/re-receipt and operation replay, refund/credit allocation, installments, company scope and profit permissions. It also generates SQL Server migration scripts; it does **not** connect to a running SQL Server. The .NET SDK and project NuGet dependencies are required.

Before a production upgrade, back up the database and run the .NET probe and application tests against a copy of the existing database. Test the SQL Server migration on a copy of a representative SQL Server database as well. The schema repair aborts on broken foreign keys or unsafe upgrades instead of deleting history. Historical records without an initial event remain readable only when unchanged; unsafe writes fail rather than inventing history. In a multi-company installation, an account selected for a cheque workflow must belong explicitly to the invoice's company.

## Unified cheque calculator

In a new sale, choose cheque and use the existing monthly cheque-installment calculator. Its single calculation creates both the cheque rows and the separate, VAT-free income/debt article. The default adjustment terms are the same monthly rate and 30 days; different terms can be entered in the calculator's optional section. The suggested cheque principal excludes cash, other installments and any previous cheque income. Rebuilding cheque rows explicitly replaces the previous rows. Removing the income article does not silently change the cheques, and an overpayment after removal must be corrected by the user.

Cheque calculator rows and the invoice's ordinary installment booklet use the same 10,000-rial rounding distribution. Whole units are spread across rows; only the last row keeps any sub-unit remainder, so the generated rows add up exactly to the calculated total, including interest where applicable. If the total is too small to create the requested number of positive rounded rows, generation is rejected before the existing rows are replaced. Existing and manually edited payments are not automatically rebalanced.

The .NET probe includes the example `2,000,000 goods + 500,000 cheque income = 2,500,000`, checks the customer statement before and after actual collection of the generated cheques, and compares the separate sales and income reports with no ERP records. It also covers zero interest, mixed payments, recalculation, rounding, fractional amounts and invalid inputs. These checks need the SDK and SQLite provider; the Python checks are arithmetic mirrors and source contracts only. The standalone `tests/InstallmentRounding` probe runs the shared distribution without EF or a database.

Manual UI checks: create that sale; verify the article below the cheques, goods/income totals in invoice detail and print, and the separate customer statement row with the invoice number and calculator terms. Before collection the customer owes 2,500,000; after collecting all cheques into an actual store bank, the balance is zero. Also split 1,000,000 into three rows (330,000, 330,000, 340,000) and 1,000,001.23 into three rows (330,000, 340,000, 330,001.23); check the sums, preview and last-row remainder in both calculator modes and the ordinary booklet. Test a total too small for the requested count and verify previously entered rows remain unchanged. Reopen the calculator and verify the original principal is not increased by previous interest. Check 0% creates no income article, removal leaves cheque amounts unchanged, and invoice edits with existing history fail without deleting it. Repeat with a partial cash payment and a credit remainder. Historical invoices without a recorded charge are not automatically assigned income; old charge snapshots are shown only when actually saved.

## Ordinary orders and legacy cash

New ordinary purchase/sale cash posts to an operational cash-entry ledger in the invoice transaction. New shop cheque receipts and reversals record the selected bank and date; a purchase cheque is a bank outflow and its reversal an inflow. Account flows and party summary/detail statements include those dated events. Existing ordinary orders are not automatically backfilled: they may already have manual treasury vouchers.

On an old ordinary invoice, the **تطبیق نقد** action requires explicit user confirmation. It can either link one existing confirmed, unallocated manual treasury voucher with matching company, party, date, amount, method and account, or record a missing cash movement after the user confirms there is no prior one. A linked manual voucher is protected and counted once. A zero-cash invoice can be marked as reviewed only if the recorded data is unambiguous. Old pure-cash invoices whose cash amount is missing, old status-only cheque receipts, and conflicting multi-company records cannot be safely inferred and require manual review. Matching and account/party data should be reviewed on a backed-up copy first.

To avoid losing historical invoice amounts from period statements, direct deletion of an ordinary invoice with a cash history, and changes to its amount, party or invoice date, are currently rejected. Those cases need a future dated invoice-correction/cancellation ledger. New ordinary sale-installment receipts and purchase-installment payments also require a selected account and date; their reversals preserve the original movement. Status-only legacy paid installments and credits have no trustworthy account history, so they cannot be automatically reversed or inferred, and may prevent later receipts until reviewed. The old status-only installment and ordinary-credit settlement endpoints remain blocked. The order-list **نسیه و سوابق** action now records partial sale receipts and purchase payments with a selected store account, real date, operation ID and reversible history. Open cheques and installments are reserved separately; they cannot be settled twice as free credit. The bank and party reports include the original and reversal on their own dates. Status-only historical payments still need an audited review; the new workflow does not manufacture or automatically migrate their missing dates or accounts.

## Dated account balance checks

Shop purchase payments, sale-receipt reversals and customer-credit refunds check the account balance at the selected day and every later day with recorded movements; a future receipt cannot fund an earlier payment. Cash purchases are checked after posting within the same serializable transaction. The check groups entries by calendar day because the store records dates, not the order of movements within each day, and treats the account opening balance as preceding its recorded history. An old cleared treasury cheque without a recorded clearing date blocks a dated debit rather than guessing from its due date. The C# probe includes examples of payments before funding and a later daily low point, but still needs to be run with .NET and against representative production data.

## Multi-company treasury history

General treasury vouchers and cheques now have explicit company ownership, and their queries and child rows are scoped. Numbering remains globally unique by `(Kind, Number)`. A transactional backfill assigns an owner only when the single active company or an unambiguous parent link agrees with the accounts and party; unknown or conflicting records are not reassigned. It runs again when the first company is created or activated. In multi-company databases, ownerless vouchers, transfers, cheques and accounts require an administrator's audited repair. Balance calculation fails closed on conflicting ownership rather than silently omitting movements. No general-purpose repair UI for those ambiguous treasury records is provided.

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
