# Repair-list status menu checks

Run commands from the `inventory` directory. Use a disposable test database and disable SMS or use a fake gateway for any end-to-end tests: the normal Ready policy may enqueue a real message when enabled.

## Source and handler contracts

```sh
python3 -B -m unittest discover -s tests/RepairStatusMenu -p 'test_*.py' -v
python3 -B tests/RepairStatusMenu/run_ui_contracts.py
```

The second command requires the .NET 8 SDK; it copies the current status and delivery handler methods, status service method, SMS event policy, DTOs and permission rules into a temporary console harness with fake services, JS and a fake transaction. It checks status permissions, in-flight and failed requests, cancellation, Ready SMS-result display, and the cash/credit/free-warranty delivery paths. It does **not** build Razor, execute an API, send SMS, or test real EF/database transactions. `--emit /path/to/harness.cs` writes the generated file for another C# compiler instead.

For real EF/SQLite coverage of the server status service (with a fake SMS gateway), on a machine able to restore the project's NuGet packages:

```sh
dotnet run --project tests/RepairSms/RepairSms.csproj
```

## Browser fixture

The optional Node test runs the production menu JavaScript and CSS in Chromium with a small simulated Blazor DOM. Install `playwright-core` outside the checkout and provide a compatible Chromium/Chrome executable:

```sh
npm install --prefix /tmp/repair-menu-test --no-save playwright-core@1.65.1
NODE_PATH=/tmp/repair-menu-test/node_modules CHROMIUM_PATH=/path/to/chromium \
  node --test tests/RepairStatusMenu/browser.test.cjs
```

Set `STATUS_SCREENSHOTS_DIR` to an existing directory to capture desktop and mobile fixture screenshots. The test skips if Playwright or `CHROMIUM_PATH` is not supplied. This verifies popover placement, scrolling, keyboard and outside-click behavior in a browser, **not** the actual Blazor renderer, API or financial workflow.

Before deployment, also build and run the real application on .NET 8, then verify desktop/mobile status changes and the delivery dialog (cancel, server error, normal payment and warranty) against an isolated database. Confirm the badge changes to delivered only after the real invoice/warranty transaction succeeds.
