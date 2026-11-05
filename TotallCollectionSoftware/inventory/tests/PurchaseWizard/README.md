# Purchase and sale wizard checks

Run the source-contract checks with `python3 inventory/tests/PurchaseWizard/test_purchase_wizard_contract.py` from the parent solution directory. These checks are not a substitute for a .NET build or interactive tests.

Before release, build `inventory/Inventory.sln` and test in a browser with a configured database:

- Purchase: validate missing warehouse, supplier, date, item and quantity precision before advancing for both purchase and sale. Return from payment and verify all invoice data survives; edit it and check the totals update.
- Purchase: test cash, cheque, installments and credit both independently and together; toggle cash off and on, including after entering an amount. Verify remaining balance, due dates, accounts, validation, save, save-and-print and editing existing invoices.
- Test F1, Alt+U, Alt+P and Esc, quick supplier/bank dialogs, and multiple rapid clicks on save. In invoice mode, F2 and F3 must not activate an invoice or an obscured form.
- Verify item entry and payment on desktop, tablet and narrow mobile, including search dropdowns and date pickers.
- Sale: verify the two-step flow for new and edited invoices, cash / cheque / installments / credit, save and print. Check customer/referrer selection, last-purchase lookup, profit/commission permissions and values, and responsive item rows.

### Keyboard tests

Run `node --test inventory/tests/PurchaseWizard/keyboard.test.cjs` from the inventory parent. These tests use a simulated DOM; the actual Blazor app still requires browser verification. In both purchase and sale check initial focus on the warehouse, Enter through warehouse, party, date, description and each item field, selection in an open search menu, and automatic opening of a new product selector. Check first focus and Enter in each payment component, cheque/installment rows, the calculator, and quick party/bank forms. Shift+Enter must retain newlines in the party note. Verify F1 cannot activate an obscured invoice, F2/F3 do not activate the invoice, Esc closes an open search menu first, calendar controls remain usable, and Alt+U/Alt+P only operate on available footer controls. The purchase-only installment label must remain unchanged in sales.

### Responsive rows, menus and bank entry

In a real browser, check that the item cards fit without horizontal scrolling and that fields and computed amounts line up on desktop, tablet and narrow mobile. In sales, repeat with neither, either, and both of the profit and commission columns visible, and with both action buttons on a stock item. Open a product menu near every edge of the modal, then resize and scroll: its options must stay inside the visible form and remain clickable. Verify that focusing invoice comboboxes and dates opens them, that typing/filtering and outside clicks work, and that the plus next to a focused cheque-bank field works in one click. For purchase, confirm the name-only form creates and selects a minimal treasury bank account; for sale it stores a reusable bank name in the current browser and sets the cheque's name without creating a treasury account. Check save failure and cancellation. Verify payment choices and other in-form actions do not respond to former F-key or Alt-key shortcuts; only the labelled footer shortcuts operate, and they never operate on an invoice behind a child dialog. Browser-native calendar month/year pickers can require a user gesture.

An optional Chromium layout regression test exercises a static table fixture built from the Razor column definitions and project CSS. It checks purchase and all sale column combinations at desktop, tablet and mobile widths, including the two sale actions and both rate units. It needs Node.js, `puppeteer-core` and an existing Chrome/Chromium; it is not a substitute for a Blazor build or the interactive checks above. For example, on Linux from the parent solution directory:

```sh
npm install --prefix /tmp/invoice-layout-test --no-save --no-package-lock puppeteer-core@24
CHROME_BIN=/path/to/chromium NODE_PATH=/tmp/invoice-layout-test/node_modules \
  node --test inventory/tests/PurchaseWizard/layout-browser.test.cjs
```

On Windows, install `puppeteer-core@24` into a temporary directory and set `NODE_PATH` to its `node_modules` and `CHROME_BIN` to the Chrome executable before running the same test.
