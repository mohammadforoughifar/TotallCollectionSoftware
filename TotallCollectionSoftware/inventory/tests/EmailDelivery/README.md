# Durable email delivery regression

Run from `inventory/` with .NET 8:

```sh
dotnet build tests/EmailDelivery/EmailDelivery.csproj -m:1 -p:UseSharedCompilation=false -p:RunAnalyzers=false -p:NuGetAudit=false
dotnet tests/EmailDelivery/bin/Debug/net8.0/EmailDelivery.dll
```

Current result: **90 checks passed**.

The project links the actual email service, dispatch store/coordinator/transport, schema bootstraps, email controller, and outgoing registration partial. Only the surrounding unrelated app context/entities, print generation, notification surface, and base-controller permission lookup are minimal synthetic infrastructure. Service/store account authorization runs against actual EF queries.

SQLite uses temporary synthetic files and the real additive DDL, executed twice. Independent contexts test database uniqueness for both same-request replay and letter guards. The real MailKit client connects **only to a loopback protocol fixture that never relays mail**. No production settings, accounts, application startup, database initialization, or recipient mailbox is used.

SQL Server provider commands/model are translated and intercepted with connection opening suppressed. They are **not** an executed SQL Server integration test. PDF content is synthetic; the PDF engine itself is not validated here.

Fault scenarios include auth/recipient/message rejection, DATA reply loss, cleanup exceptions after acceptance, required TLS, unavailable durable claim, pre-SMTP/final-save failures, attachment file/metadata failures, stale/pending guards, concurrent requests, sequential registration conflicts, notification/contact errors, access scoping and legacy rows.

Build and deployment limitations are documented in `../../EMAIL-DELIVERY-RELIABILITY-FA.md`. Full API and Blazor client builds now pass with command-line nullable analysis/analyzers disabled for the memory-limited sandbox. The diagnostic reference-only API output was removed before the executable build; both final assemblies have real method bodies and entrypoints, verified without running application startup. Normal CI analysis/publish and staging verification remain release prerequisites; no binaries are included in the source handoff.
