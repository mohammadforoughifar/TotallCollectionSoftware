# Sale cheque charge — end-to-end probe & test guide

Run `dotnet run --project inventory/tests/SaleChequeCharge/SaleChequeCharge.csproj` from the parent of `inventory/`. This exercises the shared calculations, SQLite schema self-repair, SQL Server migration script generation, and end-to-end EF workflows. It requires .NET 8 and the project's NuGet packages.

## Implemented workflows (guarded behind `SaleChequeCharge:ExperimentalEnabled=true`)

The feature switch defaults to `false` until runtime verification in your .NET + SQLite/SQL Server environment is completed (`SaleChequeCharge__ExperimentalEnabled=true` in environment variables or `"SaleChequeCharge": { "ExperimentalEnabled": true }` in `appsettings.json`).

1. **Initial registration (`SaleChequeChargeRegistration` + `SaleChequeChargePosting`):**
   - Separate non-inventory financial row (`SaleChequeCharge`) inside the same sale invoice; no fake product, zero effect on inventory quantities or average cost, no VAT on cheque income.
   - Freezes legacy sales unit cost (`UnitCostSnapshot`) inside the invoice transaction (leaving cost unknown rather than guessed if the separate warehouse ledger has activity for that product).
   - Posts balanced confirmed `AccVoucher` separating Customer Debit, Goods Credit, Goods VAT Credit, and independent Cheque Income Credit (`FacRule.ChequeIncomeAccountId`), plus linked `TrsVoucher` when mixed cash is received.
   - Supports mixed payment components (cash, cheques, installments, and explicit credit due date).

2. **Due-date adjustment (`SaleChequeRescheduling`):**
   - Formula: `(remaining goods base including VAT) × ratePercent / periodDays × deltaDays`, with cumulative midpoint-to-even rounding per constant-base segment.
   - Reductions exceeding remaining cheque income record `CustomerCredit` without creating negative goods sales; cheque bounce/reset alone creates zero extra income.

3. **Atomic partial & full return + customer cheque decision (`SaleChequeReturnService`):**
   - Creates `TransactionType.SaleReturn` (`SR-xxxx`), restores physical stock at frozen `UnitCostSnapshot` without polluting purchase statistics, posts `VoucherSource.StoreSaleReturn` + `SaleChequeChargeEventKind.GoodsReturn`, and proportionally reverses cheque income based on returned goods **including VAT**.
   - When uncleared cheques exceed the post-return balance, requires explicit customer decision (`ReplaceCheque` vs `KeepChequeForGoods`) without automatically altering any cheque.

4. **Audited cheque replacement (`SaleChequeReplacementService`):**
   - Preserves the old cheque (`ChequeStatus.Cancelled` + `SaleChequeStatusEvent`) and links the replacement cheque via `SaleChequeReplacements`.

5. **Customer credit settlement (`SaleChequeCreditSettlementService`):**
   - Settles realized customer credit either by bank/cash payout (`RefundPayment`, posting `AccVoucher` + `TrsVoucher`) or by applying credit to another unpaid sale of the same customer (`ApplyToSale`) with zero double counting of cheque receipts.

6. **Bank-selected cheque & installment receipts and reversals (`SaleChequeReceiptPosting`, `SaleChequeReceiptReversal`, `SaleChequeInstallmentPosting`):**
   - Destination bank/treasury account is explicitly selected at collection; multi-company deployments enforce per-company ownership (`TrsAccount.CompanyId` + `SaleChequeTreasuryGuard`).
   - Reversals post separate reversing vouchers without deleting historical vouchers.

7. **Atomic edit & cancellation (`SaleChequeCancellationService` + `EditSaleChequeOrderAsync`):**
   - Cancels uncollected charged sales via full reversing `AccVoucher`, physical stock restoration, initial cash refund `TrsVoucher`, and cheque cancellation audit events.
   - Untouched initial charged sales can be edited atomically in a single `Serializable` transaction.

8. **Reports & statements (`OperationsReportsService`, `AccountingService`, `InventoryService`):**
   - Customer statement (`PartyStatementsReport`), customer list balances, sales report (`OperationsOrderReport` with `hasChequeIncome` filter and goods-only averages/trends), dedicated cheque income report (`ChequeIncomeReport`), dashboards, and P&L (`AccFinanceDashboard`) reconcile across all workflows.
