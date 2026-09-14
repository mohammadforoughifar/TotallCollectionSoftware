# Document archive UI smoke test

Runs the compiled Blazor client against mock APIs in a temporary local HTTP server.
It does not connect to SQL Server, modify real documents, or test server authorization.

## Run

From the repository root, with .NET 8+ and Python installed:

```bash
dotnet build inventory/src/Inventory.Client/Inventory.Client.csproj -m:1 -nr:false -p:UseSharedCompilation=false
python3 -m pip install playwright
python3 -m playwright install --with-deps chromium
python3 tests/doc-archive/ui_test.py
```

Screenshots are saved to `~/.cache/doc-archive-ui`; override with `DOC_UI_ARTIFACTS`.

## Coverage

- `/doc-archive` and `/doc-archive/documents` list routes.
- Scoped CSS loaded, six-column table, customer metadata retained, left-aligned header actions.
- Tools menu, Escape dismissal, ZIP dialog and manager-only controls.
- Search, clear search and file-type filter reset issuing a fresh request.
- Collapsed form sections retain security switches and tags in the save payload.
- Title, description, folder selection, automatic-code mode and clean form reopening.
- Irreversible-deletion warning and typed-confirmation guard (no deletion executed).
- Desktop/mobile layout, mobile cards, no horizontal page overflow, accessible form footer.
- Read-only UI controls and no uncaught browser page errors.
