# Shop invoice payment mobile layout and bank-entry follow-up

## Behavior
- QuickBank uses the bank picker (and its existing add-bank manager) as the single bank-name input. Account title remains separate. Removing/clearing the selected bank clears its model value; opening a new form resets the selection.
- Card and IBAN inputs show live inline errors using the same strict ShopIdentifierRules used by the shop save API. Raw input is not truncated to a potentially valid prefix. Counts apply to normalized digits: card 16, Iranian IBAN 24 excluding optional IR; Persian/Arabic digits and formatting separators remain supported. Optional empty fields remain optional. This does not check checksum/ownership.
- Invoice payment totals are explicitly bold. The invoice footer (not shared Modal or global CSS) has equal-sized grid buttons; small screens use two rows/two columns with a shorter accessible back label.
- The always-visible paper-size combo is removed from the invoice footer. Save-and-print/F4 opens a small paper-choice dialog (company default, A4, A5). Cancel/Escape performs no save. Confirm/F1 invokes the existing SaveInternal(true), preserving invoice/payment validation and save-before-preview order. Normal Save/F1 remains unchanged. The child dialog carries the invoice marker so shortcuts stay in the top modal.

## Validation
```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/ShopInvoiceMobile
CHROME_BIN=/path/to/chromium NODE_PATH=/path/to/node_modules node --test tests/ShopInvoiceMobile/layout.test.cjs
```
The five Python tests check source wiring. Twelve Chromium layout tests use the production Bootstrap, app CSS and OrderForm CSS with static purchase/sale payment fixtures at 320/360/390/600/768/1280px. They check equal button sizes, visibility inside the viewport, no horizontal page overflow and computed amount weights. They do **not** render Blazor or execute invoice persistence and therefore are not a substitute for a Razor build or interactive API/Blazor test. The C# identifier tests in ShopInputReliability still require .NET 8 SDK, unavailable in this workspace.

Before release: build client/API, open real purchase/sale payment steps on a phone, enter 15/16/17-digit cards and 23/24/25-digit IBANs, clear/select/add a bank, cancel F4 without saving, confirm each paper choice, and verify invalid payments still cannot save and the resulting preview has the selected paper size.
