# Purchase and sale wizard checks

Run the source-contract checks with `python3 inventory/tests/PurchaseWizard/test_purchase_wizard_contract.py` from the parent solution directory. These checks are not a substitute for a .NET build or interactive tests.

Before release, build `inventory/Inventory.sln` and test in a browser with a configured database:

- Purchase: validate missing warehouse, supplier, date, item and quantity precision before advancing for both purchase and sale. Return from payment and verify all invoice data survives; edit it and check the totals update.
- Purchase: test cash, cheque, installments and credit both independently and together; toggle cash off and on, including after entering an amount. Verify remaining balance, due dates, accounts, validation, save, save-and-print and editing existing invoices.
- Test F1/F2 and Esc, quick supplier/bank dialogs, and multiple rapid clicks on save.
- Verify item entry and payment on desktop, tablet and narrow mobile, including search dropdowns and date pickers.
- Sale: verify the two-step flow for new and edited invoices, cash / cheque / installments / credit, save and print. Check customer/referrer selection, last-purchase lookup, profit/commission permissions and values, and responsive item rows.

### Keyboard tests

Run `node --test inventory/tests/PurchaseWizard/keyboard.test.cjs` from the inventory parent. These tests use a simulated DOM; the actual Blazor app still requires browser verification. In both purchase and sale check initial focus on the warehouse, Enter through warehouse, party, date, description and each item field, selection in an open search menu, and automatic opening of a new product selector. Check first focus and Enter in each payment component, cheque/installment rows, the calculator, and quick party/bank forms. Shift+Enter must retain newlines in the party note. Verify F1/F2 cannot activate an obscured invoice, Esc closes an open search menu first, calendar controls remain usable, and Alt shortcuts only operate on available controls. The purchase-only installment label must remain unchanged in sales.
