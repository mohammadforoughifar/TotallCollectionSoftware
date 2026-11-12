# Shop-only invoice print design proposal

This is an isolated interactive **design preview**, not production functionality.
No application files, services, APIs, database schema, navigation, accounting,
letter templates or company settings are modified by this proposal. No settings
are saved, and no network calls are made except loading this static page's assets.
All customer, invoice, tax, contact and bank details are explicitly hypothetical.
The supplied Forough Aria logo is used for the preview; its near-black backdrop
was removed and the artwork downsampled without AI regeneration. Persian fonts
are copied from the repository's existing client assets.

## Review

Serve this directory, e.g. `python -m http.server 8082 --bind 0.0.0.0`.
The right-hand controls switch between classic economical and modern minimal
variants, A4/A5, document title and visibility of signatures/bank information/
notes/item remarks. Text inputs update with textContent, never injected HTML.
No settings save button is shown because this is not connected to persistence.
PDF output contains only the invoice, not the settings panel or preview UI.
The ordinary screen view fits narrow mobile viewports without changing paper size.

## Proposed production boundary (not yet implemented)

- New shop-only configuration route and company-owned storage for
  `ShopInvoicePrintSettings`, protected by appropriate shop settings permissions.
- Read company identity from its authoritative profile; do not duplicate company
  name or silently overwrite global profile/letter-print settings.
- Current `CompanyProfileDto` has Name but **no logo field**; `SaleInvoicePrint`
  currently renders a letter mark instead of an actual company logo. The company
  logo source must be identified/added with explicit approval before integration.
  The uploaded preview logo must not be hardcoded as every company's logo.
- Store shop template style, signature/bank/notes visibility, seller print-only
  card/IBAN/account holder and notes separately from global company print defaults.
  These details are for printed payment instructions, not new treasury accounts.
- At print time use invoice snapshots for number/date, buyer details, real line
  quantities/prices/discount/VAT and totals. Never recalculate actual tax with the
  hypothetical 10% used in this static example. No profit/cost should be printed.
- Independent shop renderer and settings service only; do not reuse or mutate
  administrative letter-print settings, office/invoicing templates or common CSS.
- Keep per-print A4/A5 choice. Support long invoices with repeated table headers
  and non-split rows/totals; broader long-content testing is still required.

## Validation performed

`node --check preview.js` passed. `check-preview.cjs` uses external puppeteer-core
and Chromium (`CHROME_BIN`, `NODE_PATH`, optional `PREVIEW_URL`). It verifies six
sample lines and total 378,840,000 rials, both layouts and paper sizes, text-only
notes, no browser errors and document overflow at 320/390/768/1024/1500px.
Four generated PDFs were inspected with pypdf: classic and modern A4 are each one
210x297mm page; A5 versions are each one 148x210mm page. Prototype print margins are
10mm A4 / 6mm A5. PDFs and screenshots are generated outside Git in
`/home/user/outputs`, never committed. The sample is short; no guarantee is made
for arbitrary long notes, bank values or hundreds of lines. Tests here are not a
.NET build, live Blazor integration, production profile retrieval or physical
printer verification.
