# Inventory + HR regression checks

From the repository root (no external Python/npm dependencies):

```bash
python3 tests/hr/test_structure.py
node tests/hr/interop_test.cjs
```

With **.NET 8 SDK**, from `inventory`:

```bash
dotnet restore Inventory.sln
dotnet build Inventory.sln -c Debug --no-restore
dotnet build Inventory.sln -c Release --no-restore
dotnet run --project tests/Inventory.RegressionTests -c Release --no-build
dotnet run --project tests/RadisHr.ParityTests -c Release --no-build
dotnet run --project tests/Inventory.HrIntegrationTests -c Release --no-build
dotnet publish src/Inventory.Client -c Release --no-restore -o publish/client
```

- `Inventory.RegressionTests` exercises the real Shared DTO and client adapters: both legacy paging JSON shapes, Persian formatting in invariant mode, menu routes, shared sign-in/out, updated tokens, denied access, file UIDs and authenticated uploads/downloads. It links the client service sources so no browser/second SPA is required.
- `RadisHr.ParityTests` uses the unchanged original JSON oracle (23,088 comparisons).
- `Inventory.HrIntegrationTests` uses MVC/authorization services and real EF Core providers. SQL Server migration scripts are generated **without connecting to SQL Server**. A fresh in-memory SQLite database contains both contexts, then HR is initialized again to verify data retention. It never touches the application's configured database.
- `test_structure.py` checks the project graph, single paging definition, native page routes, scoped CSS and single-client deployment.
- `interop_test.cjs` checks same-origin browser API addressing, including cloud previews and explicit local development.

The GitHub Actions workflow runs the .NET checks on Windows and Linux in a checkout named `TotallCollectionSoftware-main (2)` to reproduce paths with spaces/parentheses.

## Manual browser smoke test

1. Back up any real database/files; preferably use a disposable test database.
2. Publish with `inventory/deploy-single.ps1` or `.sh`, then start `Inventory.Api`.
3. Sign in once. Expand **منابع انسانی** and open `/hr/employees`, `/hr/payroll`, `/hr/hse` and `/hr/notices`.
4. The Inventory header/sidebar must remain visible; switching to `/projects` must not reload a different SPA or change the shell's styles.
5. Reload a deep link and try an old `/radis-hr/employees` bookmark; both must show the native page.
6. Upload a notice attachment, then download it. The request must go to `/api/hr/files/...` with the same Bearer token and no token in the URL.
7. Give a non-admin account `RadisHr.Access`, sign out/in, and confirm access. Remove it and sign out/in again: both UI and API must deny access. An unauthenticated API request must return 401, not the SPA HTML.
8. Sign out and in as another user; HR must reflect the new user immediately. Check the original attendance/leave pages and project lists still work.
