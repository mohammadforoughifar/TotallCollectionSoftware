# Bank list probe — purchase invoice

Bug: in **ثبت فاکتور خرید** the bank/account list did not open under its own
field; it appeared in the middle of the page, and only once the invoice had
**more than three item rows**. Expected: the form opens the list at its
designated place and its behaviour does not depend on the item count.

Rule under test (see `inventory/docs/INVOICE-BANK-LIST-POSITION-FA.md`):

1. the menu is positioned from the **live** anchor rectangle on every render,
   never from coordinates cached at focus time;
2. it is clamped to the **visible modal box** (not to the whole window), so a
   tall form cannot push it onto the middle of the page;
3. it stays **hidden** until placement succeeds (hidden field, inactive tab,
   field scrolled behind the modal header);
4. it follows **layout changes** — scroll, resize, a new item row — because the
   modal and its body are observed and the open menu is re-placed;
5. RTL: the menu's right edge lines up with the field's right edge.

## Entry points

```bash
# source contracts on the Razor/CSS/JS files — no .NET SDK needed
python3 -m unittest discover -s tests/InvoiceBankMenu -v

# simulated-DOM placement test that loads the production js/order-entry.js
node --test tests/InvoiceBankMenu/menu-placement.test.cjs
```

The DOM test stubs only `getBoundingClientRect`, `ResizeObserver`,
`requestAnimationFrame` and `getComputedStyle`; the placement math is the real
one from `wwwroot/js/order-entry.js` (`window.appMenu.place`).

## Manual browser check

1. New purchase invoice → add **two** items → focus «بانک/صندوق» in the cash
   section: the list opens right under the field.
2. Add **ten** items, scroll the form so the bank field sits low: the list still
   opens under the field (above it when there is no room below), never in the
   middle of the page.
3. Keep the list open and scroll the form body: it must follow the field or
   disappear — never float on stale coordinates.
4. Do the same for a cheque row («بانک (حساب ما)») and for the picker inside
   «تعریف بانک» (دکمهٔ +) — both use the same menu.
