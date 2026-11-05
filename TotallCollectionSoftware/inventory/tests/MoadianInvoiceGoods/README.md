# Moadian invoice goods resolution regression

Run from the canonical `inventory` directory:

```sh
dotnet run --project tests/MoadianInvoiceGoods -c Release -p:PublishSingleFile=false
```

This console regression project calls the real `ConnectService.SabtInvoiceAsync`
using an isolated in-memory SQLite database and an authenticated test company
context. No production database, credentials, tax-system submission, or network
service is used.

Covered cases:

- The reported `2720000005833` appears on multiple products, providers and a
  soft-deleted catalog row. Explicitly selected primary keys must be preserved.
- Repeated invoice lines and two different products sharing one tax code remain
  distinct; no dictionary keyed by tax code may collapse or reject them.
- Legacy code-only requests resolve only a unique active match in their provider;
  ambiguity, missing/foreign/deleted rows and invalid IDs give a clear 400 result.
- Explicit ID/code mismatch is rejected; a missing code is filled from a valid ID.
- The same resolution and provider ownership checks protect edits. Invalid edits
  leave persisted headers/lines untouched.
- Simulated line-save failures roll back newly created invoice headers and never
  return success for failed edits.

Scope: compiled production service and SQLite/EF behavior only. SQL Server
runtime, browser workflows and actual tax-system acceptance still need deployment
validation. No schema migration, catalog deletion or unique-code constraint is
introduced by this fix.
