# Archive evolution tests

Run from repository root (.NET 8+):

```bash
dotnet test tests/doc-archive-evolution/Inventory.DocArchiveEvolution.Tests.csproj -m:1 -nr:false -p:UseSharedCompilation=false
```

The suite uses disposable SQLite in-memory databases, not the production database. It checks server-side pagination/filters and permission boundaries, temporary grants and file mutation restrictions, confidentiality guards, per-expiry renewal idempotency, queue retry/lease/generation behavior, additive schema upgrades, static URL blocking, and content diff algorithms.

Browser checks use the **real compiled Blazor client with mock APIs**:

```bash
dotnet build inventory/src/Inventory.Client/Inventory.Client.csproj -m:1 -nr:false -p:UseSharedCompilation=false
python3 -m pip install playwright
python3 -m playwright install --with-deps chromium
python3 tests/doc-archive/ui_test.py
python3 tests/doc-archive-evolution/ui_test.py
```

Screenshots are under `~/.cache/archive-evolution-ui`. SQL Server execution, multi-node deployment, performance/load and production OCR quality are not covered. See [deployment and behavior notes](../../inventory/docs/DOC-ARCHIVE-EVOLUTION.md).

## ERP retirement regression

`ErpDisabledTests.cs` and `ErpRouteTests.cs` cover the five retired integration actions, real local HTTP route matching (test authentication, not the production Program pipeline), 410 responses, preservation of historical rows, rejection of retired search filters, empty compatibility metadata, intact document-to-document links and a ten-column ZIP manifest without ERP data. SQLite is disposable; there is no production DB connection.

The browser fixtures deliberately include historical ERP metadata and assert that cards/badges/filters are absent and no ERP lookup/link requests are sent. The seven other module pages are checked by compilation/source inspection, not individual interactive browser scenarios.

Latest local run after ERP retirement: **34 archive tests + 26 WorkOrders tests passed**, full solution build succeeded, all three browser suites passed. See [ERP retirement notes](../../inventory/docs/DOC-ARCHIVE-ERP-DISABLED.md).

## Attachment download regression

```bash
python3 tests/doc-archive-evolution/download_ui_test.py
```

Uses compiled Blazor with mock APIs and simulated popup blocking (`window.open` returns null). Verifies actual saved bytes and a Persian Content-Disposition filename on desktop/mobile, preview downloads, busy/double-click prevention, Bearer headers without query tokens, 401/403/404/network/HTML-fallback errors, password confirmation/wrong password/repeated server-side expiry and view-only controls. Files/screenshots are temporary artifacts under `~/.cache/archive-download-ui`.

After the download fix, this suite and the three existing UI suites passed; archive **34/34** and WorkOrders **26/26** tests passed; full solution build: **0 errors, 24 existing warnings**. Run the browser suites sequentially on constrained machines: a parallel run alongside .NET builds timed out during the 30-second initial Blazor startup; sequential reruns passed without page errors. These tests do not establish the cause on an unobserved production browser or validate live file storage.
