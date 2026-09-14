# Work-order regression tests

Run from the repository root with .NET 8 SDK:

```sh
dotnet test tests/work-orders/Inventory.WorkOrders.Tests.csproj -m:1
```

The tests use the real controller, EF model and SQLite in-memory database; notifications and file hosting are test doubles. They do not require a live server, production data, or SQL Server.

Coverage: finite Jalali recurrence, per-assignee calendar visibility, independent replies, no duplicate on close/edit/restart, soft deletion and audit, RBAC permission/inactive roles, parent protection, permission seeding, and idempotent schema upgrades.

See `inventory/docs/WORK-ORDERS-RECURRENCE.md` for behavior and deployment notes.

Verified in this workspace: **26 tests passed, 0 failed**; the full `inventory/Inventory.sln` build succeeded. Browser checks use the real Blazor client and mocked API responses; live SQL Server integration was not tested.

## Browser layout and interactions

```sh
dotnet build inventory/src/Inventory.Client/Inventory.Client.csproj -m:1
python3 -m pip install playwright
python3 -m playwright install --with-deps chromium
python3 tests/work-orders/ui_test.py
```

The script starts a temporary localhost static server, injects a test session, and mocks API responses. It checks desktop/mobile forms, left-aligned header actions, recipient selection, rich-text preservation, recurrence/checklist/tags payload, related parent/child links, and detail tabs. Screenshots go to `~/.cache/work-order-ui` (override with `WO_UI_ARTIFACTS`). No generated build output or screenshots are tracked in Git.
