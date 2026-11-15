# Shop print settings

The actual shop navigation now exposes `/shop/print-settings`, guarded by
`Shop.PrintSettings.View` / `Shop.PrintSettings.Manage`. The shared shop-section
catalog feeds route guarding, permission seeding and role labels. Administrators
can access the page; other roles need the new view/manage permission explicitly.

Only seller bank rows are editable. Company identity/contact are read-only from
company definition; customer data is not edited here. No paper-size default is
stored: the print-entry A4/A5 chooser remains the sole paper control.

Independent `ShopPrintSettings` storage uses active-company ownership and a
company-unique index. The API refuses missing/zero company context, gates GET/PUT,
validates lengths/digits/duplicates on the server, normalizes Persian/Arabic digits
and IR, and limits rows to 30. It is not bank ownership/checksum verification.
Both SQL Server migration and SQLite/SQL Server startup repair are provided.
This does not modify company-profile, office settings or their print builders.

Shop sales print callers load saved bank settings at print time and pass them to
the sales builder; only those calls use the new banks. Non-shop callers with the
old optional signature remain unchanged. Bank entries are HTML escaped. Purchase
and delivery-note printing do not print seller collection cards from this setting.
The existing full invoice previews have NOT thereby been fully integrated.

Validation:
- `python -m unittest discover -s .../tests/ShopPrintSettings -p 'test*.py'`:
  source contracts plus actual SQLite DDL idempotence, company uniqueness and JSON
  storage/retrieval checks (not EF/API runtime).
- `dotnet run --project .../tests/ShopPrintSettings`: executable bank-rule cases,
  provided but NOT run here because .NET SDK is unavailable.
- `check-paper-choice.cjs`: actual browser tests for print-time selection and the
  bank-field typing/paste normalization/length behavior.

C#/Razor compilation, authenticated API/EF round trips, and SQL Server migration
execution still require validation in a .NET-enabled environment.
