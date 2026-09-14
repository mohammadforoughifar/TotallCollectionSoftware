# Main HR workspace regression tests

Run from the repository root with .NET 8:

```sh
dotnet build inventory/Inventory.sln -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test tests/hr-workspace/Inventory.HrWorkspace.Tests.csproj -m:1 -nr:false -p:UseSharedCompilation=false
python3 -m pip install playwright
python3 -m playwright install --with-deps chromium
python3 tests/hr-workspace/ui_test.py
```

The 19 xUnit cases cover:
- Preservation of 72 distinct destinations from the original navigation, unique catalog links and actual Razor routes.
- Exact read/create/manage permission combinations, including create-only employees, attendance and training management.
- Longest-match active links, canonical profile aliases, query/fragment normalization and exclusion of BonHr/other modules.
- Real `HrCoreService` on SQLite: organization-node filtering **before** total/count/paging, second page, search/status composition, five-argument backward compatibility, empty match, negative offset and no writes.

The browser suite serves the **compiled application**, including its real scoped CSS bundle, and mocks API responses and authentication. It tests desktop (1440×1000) and mobile-sized (390×844) viewports, accordion navigation, section links, employee list/profile/edit/save/back, legacy back, overview/list/wizard/dossier error and retry, empty-organization setup, restricted lookup preservation, independent role visibility, create-only save, left-aligned actions, mobile menu close and horizontal overflow. It also checks that leaving HR does not retain the HR topbar title. Screenshots are stored under `~/.cache/hr-ui/`.

No live SQL Server, real user session, production deployment or physical mobile device is used. Browser API mocks are not a substitute for backend authorization tests. The UI catalog never grants API permissions.

The menu regression now verifies that the original application background gradients, logo animation and dashboard-group color remain unchanged, while **only Main HR** has simplified non-glowing links, readable child labels, keyboard focus outlines and 44px mobile targets. The global light-sidebar stylesheet and mobile-close modifications were reverted at the user's request.
