# Expense cheque dates probe

Bug: in **هزینه‌ها** (expenses), creating a new expense with payment method
**چک صادره** (issued cheque) accepted a **سررسید** (due date) earlier than the
**تاریخ تحویل چک** (hand-over date). Expected: the system warns the user and
rejects the invalid due date.

Rule under test (single source of truth:
`src/Inventory.Shared/ExpenseChequeDates.cs`, used by both the API service and
the Blazor form):

1. **due < hand-over** → invalid, message
   `تاریخ سررسید چک صادره نمی‌تواند پیش از تاریخ تحویل چک باشد.`
2. **due == hand-over** → valid (a cheque handed over on its due date is
   normal);
3. **due > hand-over** → valid;
4. only the date part matters — the time of day never changes the result;
5. invalid data cannot be *saved* even if it bypasses the form (API guard in
   `ShopExpenseChequeService.CreateAsync`), so a warning is not just cosmetic;
6. the reverse path is explicit and recoverable: picking `چک صادره` pulls a
   stale due date back to the hand-over date, while typing/selecting an earlier
   date is refused with an inline red message and `is-invalid` frame.

## Entry points

```bash
# static contracts on the razor/css/service sources — no .NET SDK needed
python3 -m unittest discover -s tests/ExpenseChequeDueDate -v

# real unit probe of the shared rule — needs .NET 8
dotnet run --project tests/ExpenseChequeDueDate/ExpenseChequeDueDate.csproj
```

The .NET probe links `src/Inventory.Shared/ExpenseChequeDates.cs` directly (no
copy), so the rule cannot drift from the probe.

## What the user sees

- Calendar: days before the hand-over date are dimmed, struck through and not
  clickable; hovering shows `پیش از تاریخ تحویل چک`.
- Typed Persian date earlier than hand-over: the field turns red
  (`is-invalid`) and the sentence above appears under it.
- Save (`F1`) is blocked with the same sentence as a toast, and the API rejects
  the request with the same text (HTTP 400 → toast) even if the form is
  bypassed.

Manual browser check still worth doing: new expense → `چک صادره` → hand-over =
today → try to pick tomorrow's due date and yesterday's due date; then set the
hand-over date *after* the due date and confirm the warning appears on the due
field and saving is refused.
