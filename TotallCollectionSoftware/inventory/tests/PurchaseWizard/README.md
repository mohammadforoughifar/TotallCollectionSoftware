# Purchase, sale and shop workflow checks

From the `inventory` directory, run the source contracts and the simulated-DOM keyboard checks:

```sh
python3 -m unittest discover -s tests/PurchaseWizard -p 'test_*.py'
node --test tests/PurchaseWizard/keyboard.test.cjs
```

The contracts do not replace a .NET build or interactive tests. The VAT test project also exercises invoice copying, the profit-permission rules and controller responses using SQLite:

```sh
dotnet build Inventory.sln
dotnet run --project tests/OrderVat/OrderVat.csproj
dotnet run --project tests/SaleChequeCharge/SaleChequeCharge.csproj
```

## Optional Chromium fixtures

The layout fixture reads the real Razor column weights and CSS. The keyboard fixture runs the production shortcut and legacy handlers together with actual browser focus, nested dialogs, dropdowns, and error/toast positioning. These are static fixtures, **not** a running Blazor application. Install `puppeteer-core` outside the repository and provide a local Chrome/Chromium, for example:

```sh
npm install --prefix /tmp/invoice-layout-test --no-save --no-package-lock puppeteer-core@24
CHROME_BIN=/path/to/chromium NODE_PATH=/tmp/invoice-layout-test/node_modules \
  node --test tests/PurchaseWizard/layout-browser.test.cjs tests/PurchaseWizard/keyboard-browser.test.cjs
```

On Windows, set `NODE_PATH` to the temporary installation's `node_modules` and `CHROME_BIN` to the Chrome executable.

## Manual checks against a configured database

- In a role with `Orders.ViewProfit`, check sales with neither, either, and both profit and commission columns. Remove the profit permission and reopen the form/refresh the list; a general shop View/Manage permission alone must not show profit. Confirm totals, tax, commission, and row alignment still work.
- Copy a purchase and a sale using the button, F7 on a focused row/card, and F7 in the detail dialog. Review the new date, party, warehouse, items, quantities, discounts and VAT. Old invoice/line IDs, payments, cheques, installments and cheque-income history must not carry over. Check inactive sources, unavailable items, and old quantities or prices with excessive precision; the new copy must require corrections rather than silently rounding them.
- F1 saves (or advances from the invoice's first step), F2 adds an item row where available, F3 goes to the previous step, F4 prints or saves and prints, and Esc cancels/closes. F7 copies only the focused invoice or the invoice in the active detail. Check disabled buttons, key repeats and modifiers, hidden tabs, multiple open forms and child dialogs. Opening a search menu or calendar makes the first Esc close that control. The visit-order form's F1 saves a draft; the treasury form's F2 adds an invoice allocation. No shortcut selects a payment method.
- Test cash, cheque, installments and credit independently and in combination; toggle cash off/on, verify totals, due dates, accounts, validation, save, save-and-print and editing. Exercise the existing «تاخیر تادیه چک» lifecycle and ensure copying a charged invoice does not create any old cheque or charge history.
- On desktop, tablet and narrow mobile, check the table/card layout, keyboard focus ring, fixed validation error and temporary messages at the top right. Show a long validation error and multiple toasts simultaneously, resize/scroll, dismiss each, and check that they remain separate and usable.
- Check initial focus and Enter navigation in invoice and quick party/bank forms, Shift+Enter in multiline notes, and the product search menu near each viewport edge. Focusing invoice combos and dates should open them. A new cheque bank should open only the bank-name form. Browser-native calendar month/year pickers can require a user gesture.
