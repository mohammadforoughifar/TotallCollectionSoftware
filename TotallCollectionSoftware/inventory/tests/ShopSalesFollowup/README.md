# Shop sales follow-up

- Sale-row last-purchase control displays a short action label followed by F8. F7 no longer opens purchase history in an invoice; its existing invoice-list/details copy behavior remains.
- Both the latest-purchase number and historical minimum-profit-basis purchase number open the actual Purchase OrderForm as an additional modal. The normal GetAsync API enforces read/company permissions; saving uses existing purchase-write permissions. No navigation or assignment to the parent sale lines/payment takes place. Closing keeps the sale draft; saving refreshes the purchase summary/basis. Responses after closing the summary are ignored.
- Optional rescheduling controls are not rendered in manual-cheque interest-only mode. The automatic schedule calculator still has its existing controls. Stored adjustment parameters and financial calculations were not changed or silently discarded.
- Repair aging percentage now wraps the complete .Num() invocation inside its Razor expression instead of rendering the method name literally.

Validation: four source regression tests; 14 Node keyboard tests; real Chromium keyboard/layout fixtures passed (9 tests). Existing Python regression suites also passed. The browser tests use static DOM fixtures with production JS/CSS; they do not execute Blazor or invoice API mutations. .NET SDK is unavailable here, so recursive/nested OrderForm rendering, actual role access, saving a purchase and restoring a real sale draft still require a Razor build and interactive deployment test.
