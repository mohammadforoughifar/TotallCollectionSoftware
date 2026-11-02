# Purchase wizard checks

Run the source-contract checks with `python3 inventory/tests/PurchaseWizard/test_purchase_wizard_contract.py` from the parent solution directory. These checks are not a substitute for a .NET build or interactive tests.

Before release, build `inventory/Inventory.sln` and test in a browser with a configured database:

- Purchase: validate missing warehouse, supplier, date, item and quantity precision before advancing. Return from payment and verify all invoice data survives; edit it and check the totals update.
- Purchase: test cash, cheque, installments and credit both independently and together; toggle cash off and on, including after entering an amount. Verify remaining balance, due dates, accounts, validation, save, save-and-print and editing existing invoices.
- Test F1/F2 and Esc, quick supplier/bank dialogs, and multiple rapid clicks on save.
- Verify item entry and payment on desktop, tablet and narrow mobile, including search dropdowns and date pickers.
- Sale: verify the original single-stage form, payment, save and print remain unchanged.
