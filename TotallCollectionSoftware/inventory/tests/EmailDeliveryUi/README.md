# Email / secretariat delivery UI regression

Run from `inventory/` with .NET 8:

```sh
dotnet build tests/EmailDeliveryUi/EmailDeliveryUi.csproj -m:1 -p:UseSharedCompilation=false -p:RunAnalyzers=false -p:NuGetAudit=false
dotnet tests/EmailDeliveryUi/bin/Debug/net8.0/EmailDeliveryUi.dll
```

Current result: **19 HTTP/recovery + 40 real Razor checks passed**.

This project reuses the existing avatar harness dependencies and links the actual production email page and guides. Shared components and client services are compiled from production sources, not copied markup. Calls and data are synthetic. UI checks cover separate registration/email outcomes, UUID and immutable-payload retention, read-only history/status, safe replay, explicit new attempts, risk confirmation, a separate genuinely blank new-message action, truncated/unreadable uploads, personal scope and browser-session recovery.

## Browser checks

Start the synthetic host (not the application API):

```sh
dotnet tests/EmailDeliveryUi/bin/Debug/net8.0/EmailDeliveryUi.dll --serve --urls http://0.0.0.0:5144
```

Install Playwright 1.56.1 in a separate test-tools directory, then install its Chromium and OS dependencies. With that directory on `NODE_PATH`:

```sh
EMAIL_TEST_URL=http://127.0.0.1:5144 TEST_OUTPUT=/path/to/test-artifacts node tests/EmailDeliveryUi/browser.cjs
```

Current result: **32 browser checks passed**, desktop 1365×960 and mobile 390×844. Test routes are `/probe/email`, `/probe/personal-email` and `/probe/dabirkhane`, intentionally separate from the linked components' real route attributes.

The synthetic service never opens SMTP or the application database. It provides a controllable lost-response/failed/accepted/warning model; real protocol and SQLite behavior are tested separately by `tests/EmailDelivery`. Always stop the host afterwards. Screenshots contain synthetic addresses and documents only.

The full deployed WASM/browser-to-production-API path is not covered by this harness. Full API and Blazor client compilation separately passed with low-memory compiler settings; this is not production runtime validation. See `../../EMAIL-DELIVERY-RELIABILITY-FA.md` for exact verification boundaries.
