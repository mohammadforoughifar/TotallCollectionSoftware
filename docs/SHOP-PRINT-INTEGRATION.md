# Shop print integration status

Canonical source: `TotallCollectionSoftware/inventory/src`.

All three approved layouts are now routed from the real shop print buttons:

| Document | Actual caller | Complete builder |
| --- | --- | --- |
| Sale | Orders list, OrderForm save-and-print, repair-linked sale | ShopSaleInvoicePrint |
| Purchase | Orders list and OrderForm save-and-print | ShopPurchaseInvoicePrint (shared approved invoice renderer in purchase mode) |
| Goods issue | GoodsIssues list/details, reload saved record | GoodsIssuesPrint |

No shop order entry point falls back to the old SaleInvoicePrint layout. The old
builder remains for compatibility/test references, not as a purchase fallback.
ERP invoice and office print paths are unchanged.

Complete layout includes logo/header, metadata, registered counterparty, detail
table, single-column totals, real payment schedules, correct signatures and
invoice-derived notes. Purchase mode labels outgoing cheques as payments, not
collections, and omits seller collection bank accounts. Supplier invoice number
has no persisted Order field; its approved header slot displays `-`, never a demo
number. All other available values come from the saved DTO/company/settings.

Goods-issue rendering now embeds the actual approved minimal HTML structure and
uses the same embedded design styles. It contains only saved recipient name,
number/date, descriptions/quantities/remarks and notes, plus blank signature boxes;
no unsupported logistics, invoice link, unit, address or money fields.

Paper-size selection stays exclusively in the existing print-time A4/A5 combo
dialog. Company identity/logo comes from company definition. Bank settings remain
independent shop settings and are displayed on sales only.

Validation: source/routing contracts and browser tests of the real embedded
production templates/styles (A4/A5, centered header, left metadata, nonwrapping
supplier label, monetary overflow, dispatch column restrictions) passed.
Representative fixtures are NOT executed C# builder output or a live API/database
round trip. Executable .NET cases now cover all three builders but remain unrun
here because no .NET SDK is installed. Compilation and deployment verification
remain required. Prototype HTML pages themselves still use illustrative data.
