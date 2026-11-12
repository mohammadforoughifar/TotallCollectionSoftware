# Approved sales design: production integration correction

The earlier integration retained the legacy sales builder (dark blue table,
boxed metadata, full-width total). This follow-up routes actual shop sales from
Orders list, save-and-print and repair-linked sales to `ShopSaleInvoicePrint`.
Purchases retain their existing builder, as do non-shop invoice/office modules.

The new builder embeds a frozen copy of the approved preview's HTML structure and
stylesheet: right logo, truly centered company/title, left compact metadata,
read-only buyer section, seven-column detailed-price table, narrow single-column
summary, compact settlement headers, optional real cheque/installment schedules,
signatures, seller contact/banks and a brand separator above invoice-derived notes.
No app UI controls or preview sample records are embedded in printed HTML.

Data integrity:
- Lines/quantity, gross, discount, net and VAT use persisted DTO data. Never apply
  a hardcoded 10% tax. Final amount comes from Order.TotalAmount, not a fresh sum.
- Cheque-charge amounts, returns and cancellation remain visible, and exact money
  formatting is retained. No profit/commission data is printed.
- Initial settlement components use CashAmount, cheque/installment records and the
  residual issuance amount, NOT SettledAmount (which includes later collections).
  This is not a recalculated current account statement. Individual real cheque
  statuses are shown; no default claim of collection and no invented Sayad ID.
- The Order read model now includes linked-party phone/address/TaxId from the
  existing company-filtered party query. No manual customer inputs or new schema.
- Logos come from company profile and seller banks from saved shop settings.
- All free text is HTML encoded; token replacement is single-pass to prevent
  customer text resembling template placeholders from being substituted again.

Validation:
- Python source-contract tests verify ALL shop sales entry points use the new
  builder; keeping the new class unused is explicitly tested against.
- `render_fixture.py` extracts the REAL production HTML template/CSS into browser
  fixtures using representative data, including the screenshot's large amounts.
  `check-layout.cjs` checks seven columns, no monetary overflow, centered title,
  one-column totals, brand position, A4/A5 and preservation of 48-row content.
  Example two-line A4/A5 PDFs were one page; 48-row A4 was three pages. These are
  template/CSS tests, NOT executed C# builder output or a database-backed app.
- `dotnet run --project tests/ShopSalesLayout` is provided to test actual C# output,
  exact values, encoding, return/cancel states and payment schedules. It was NOT
  run here: .NET SDK is unavailable. C#/Razor build and full app verification remain
  outstanding and must not be presented as completed.
