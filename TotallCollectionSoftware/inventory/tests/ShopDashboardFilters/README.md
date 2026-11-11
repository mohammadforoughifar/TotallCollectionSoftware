# Shop credit KPI filters and financial dashboard alerts

KPI cards are native buttons with keyboard support and pressed state. Clicking
clears stale searches/direction filters, switches the tab, and applies the matching
status. Successful collections filter cleared cheques and paid installments in
their respective tabs. Reset restores all records. In-hand excludes endorsed,
cancelled and cleared cheques, including legacy cleared-status/flag mismatches.

Investigation: the screenshot's cards are in Shared/AdminHome.razor, not the trend
page. Their data is persisted by normal invoice payment components: Transactions,
Cheques and Installments. There is no evidence that they are unbacked placeholders,
so they were retained. Production DB contents are not accessible here; the screenshot
alone cannot establish whether any due records should have been present.

Corrections: full calendar-day cutoff, actual credit remainder rather than a legacy
payment-method gate, sales-only debtor installments, cancellation/terminal cheque
exclusion, no classification of orphaned cheques as incoming sales. Pending incoming
and outgoing counts distinguish future-due cheques from no open records. Empty states
are compact and link to credit management. Load failures are reported, not swallowed.
Credit list filtering and summary predicates were aligned for cancelled sales and
legacy cleared cheques. Treasury cheques remain separate: their IDs and settlement
endpoints must not be mixed with shop invoice cheques. No other module is modified.

Validation: run `python -B -m unittest discover -s TotallCollectionSoftware/inventory/tests/ShopDashboardFilters`.
Seven source/policy tests plus 19 warranty/trade-in/expense and five repair-form source
regressions pass. No .NET SDK is available: no Razor build, EF query/database integration
or live Blazor/browser behavior is claimed. Date/status examples are isolated policy
fixtures, not execution of the C# implementation.
