# Shop-only invoice print proposal — fixed layout revision

This is an isolated **interactive prototype**, not a production settings page.
No company data, APIs, database schema, letter templates, application settings or
financial records are changed. There is no persistence. Serve this directory with
`python -m http.server 8082 --bind 0.0.0.0`. Bank values start empty; never interpret
checksum-valid test values as real bank accounts or proof of account ownership.

## Current agreed layout

- Fixed supplied company logo at the right, document title/company name centered,
  invoice number/date at the left. No layout/logo/section visibility switches.
- Only customer example data and repeatable seller card/IBAN rows can be edited.
  A4/A5 remains a per-print choice in the toolbar, not a layout setting.
- Multiple card/IBAN rows; optional blank fields, live accessible field errors,
  digit normalization, exact card length plus Luhn checksum, Iranian IBAN format
  plus ISO mod-97 checksum. IR is accepted explicitly or added to 24 digits.
  Malformed, all-zero and repeated card values are rejected; duplicate normalized
  cards/IBANs within the current list are rejected. No silent truncation.
- Invalid entered rows disable the print action and are not rendered as payment
  instructions. Remove/add does not reset other data. No treasury accounts are made.
- Notes are **always present**, generated from the sample invoice number/date,
  customer, line/quantity count, gross, discount, tax and final total/payment method.
  They do not invent receipt, delivery or warranty acceptance. All values are sample;
  the 10% tax is hypothetical and must not become a production tax calculation.
- A centered Forough Aria brand label interrupts the bottom line of the footer frame.
  The company name/logo and the software brand footer are distinct concepts.

The supplied logo's near-black backdrop was removed without AI regeneration.
The fonts are from existing repository client assets.

## Required future production boundary — not implemented

Use a company-owned, shop-only settings store with shop authorization. Read invoice
buyer data from the same authorized invoice/customer rather than a global customer
default. Read company name/logo from authoritative company identity; do not hardcode
the uploaded logo for every company. Current CompanyProfileDto contains Name but
no logo field; the real logo source needs identification/explicit integration.
Use real invoice snapshots/totals and actual payment records for generated notes.
Validate bank data again server-side before saving; this prototype validates only
in the browser. Keep administrative letters, accounting and other modules independent.
No unapproved production integration is included in this proposal.

## Validation

- `node --test bank-validation.test.cjs`: three unit groups for normalization,
  length, checksum, invalid input and blank fields.
- `check-preview.cjs`: uses external puppeteer-core / CHROME_BIN / NODE_PATH,
  optional PREVIEW_URL. Tests invalid print blocking, duplicates, multiple valid
  cards/removal, text-only customer insertion, fixed logo/notes and responsive
  widths 320/390/768/1024/1500px. Outputs are outside Git in /home/user/outputs.
- Generated six-line example PDFs were inspected using pypdf: A4 and A5 are each
  one page. A4 margins 10mm, A5 6mm. Arbitrary many accounts/long notes may naturally
  require additional pages; these are not forced onto one page by shrinking text.
- Not a .NET build, live Blazor/API test, bank verification or physical printer test.
