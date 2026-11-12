# Shop-only invoice print proposal — fixed layout revision

This is an isolated **interactive prototype**, not a production settings page.
No company data, APIs, database schema, letter templates, application settings or
financial records are changed. There is no persistence. Serve this directory with
`python -m http.server 8082 --bind 0.0.0.0`. Bank values start empty; never interpret
checksum-valid test values as real bank accounts or proof of account ownership.

## Current agreed layout

- Fixed supplied company logo at the right, document title/company name centered
  (without the invented sample business tagline),
  invoice number/date at the left. No layout/logo/section visibility switches.
- Only repeatable seller card/IBAN rows can be edited. Customer details are read-only,
  rendered from the sample invoice-linked customer object; no customer form inputs
  or customer print settings exist. The static sample is not a real DB connection.
  A4/A5 remains a per-print choice in the toolbar, not a layout setting.
- Multiple card/IBAN rows; optional blank fields, live accessible field errors,
  digit normalization, exact card/IBAN digit lengths and format only; no bank checksum. IR is accepted explicitly or added to 24 digits.
  Malformed or incomplete values are rejected; duplicate normalized
  cards/IBANs within the current list are rejected. No silent truncation.
- Invalid entered rows disable the print action. All nonblank entries render live
  on screen, with incomplete/duplicate values explicitly marked as preview only. Remove/add does not reset other data. No treasury accounts are made.
- Notes are **always present**, generated from the sample invoice number/date,
  customer, line/quantity count, gross, discount, tax and final total/payment method.
  They do not invent receipt, delivery or warranty acceptance. All values are sample;
  the 10% tax is hypothetical and must not become a production tax calculation.
- A centered Forough Aria brand label interrupts the separator immediately above
  the invoice notes, below seller payment information; there is no separate footer frame.
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
  length, digit normalization, invalid input and blank fields.
- `check-preview.cjs`: uses external puppeteer-core / CHROME_BIN / NODE_PATH,
  optional PREVIEW_URL. Tests invalid print blocking, duplicates, multiple valid
  cards/removal, read-only customer identity and missing-field fallback, fixed logo/notes and responsive
  widths 320/390/768/1024/1500px. Outputs are outside Git in /home/user/outputs.
- Generated six-line example PDFs were inspected using pypdf: A4 and A5 are each
  one page. A4 margins 10mm, A5 6mm. Arbitrary many accounts/long notes may naturally
  require additional pages; these are not forced onto one page by shrinking text.
- Not a .NET build, live Blazor/API test, bank verification or physical printer test.

## Add-row interaction regression

The desktop add-and-type path worked during investigation; the user's exact
failed state was not reproduced. Adding a row now explicitly scrolls it into view
and focuses its card input (including mobile), with a numbered heading and fresh
asset query versions to avoid stale cached scripts. The browser regression now
uses actual button clicks and keyboard typing in both card and IBAN inputs at all
five viewport widths, rather than relying only on programmatically assigned values.

Empty, whitespace-only, null or missing customer fields (including a missing customer
object) render a literal ASCII `-`, never invented contact data. Browser regression
checks all four fields for empty and missing-customer cases.

## Exact entry lengths

Card inputs have 16-digit min/max constraints. IBAN uses a fixed IR prefix and
a 24-digit min/max input. Normalized paste supports a full IR-prefixed IBAN and
Persian/Arabic digits, but rejects an overlong paste as a whole rather than silently
truncating it. Typing cannot exceed the cap. Partial nonblank values remain editable
but fail validation and block printing; optional unused fields may remain empty.
Validation checks exact length/format, not banking checksum. Browser regression checks extra keystrokes,
short numbers, valid full-IBAN paste and overlong paste for both fields.

Rejected extra digits/pastes do not mark an unchanged valid value red. Validation
is recalculated from the actual retained value, so duplicate/short-number
errors still apply. Regression checks aria-invalid and empty error messages after
over-limit attempts on valid cards and IBANs.

Seller address and telephone are displayed inside the lower seller information
panel alongside manually entered payment details, no longer under the header.
Buyer contact/address remain in the separate buyer section. Contact values here
are still explicit prototype samples, not a live company-profile connection.

## Latest requested behavior

Exactly 16 card digits or 24 IBAN digits clear length errors immediately, even if
a banking checksum would not match. Banking checksums are intentionally no longer
validated in this prototype; this does not verify a real account. Nonblank numbers
are visible in seller information during typing. Incomplete or duplicate values
are labelled as preview-only, and the print action stays blocked until resolved.
Customer information remains read-only sample invoice data.

Latest visual simplification: no supplemental-info or seller-info heading and no
numbered payment-method caption. Seller card labels and numbers are adjacent, not
justified to opposite edges. The row heading is non-wrapping with a reserved width.
Supplemental facts are limited to payment method/quantity; generated notes are two
short, larger-type lines instead of repeating the complete totals breakdown.

Visual review revision: invoice-only sample/proposal labels, artificial business
taglines, sample version and financial disclaimer removed at the user's request.
The last explanatory sentence is removed; one concise invoice summary remains.
Unknown addresses render `-` instead of invented placeholder prose. A short notice
OUTSIDE the printable invoice still states that live data is not connected. This
remains a static prototype and does not become a real transaction by removing labels.

Header metadata contains only invoice number/date, with no version label. Plain
Enter in a card field moves focus to the IBAN in the same row, without printing,
submitting or adding another row. Browser tests verify this in five viewport sizes.

## Table design comparison (review only)

Three buttons outside the invoice/settings compare proposed table structures;
this does not introduce a persisted layout setting or alter the other fixed sections.

- Balanced (default/recommended): row, description, quantity, unit price, discount,
  amount after discount. Three equal monetary columns; notes under product name.
- Detailed: same plus gross row amount. Four equal monetary columns, more explicit
  multiplication/discount audit trail, at the expense of description width.
- Minimal: row, description, quantity, unit price, amount after discount. Row discount
  moves to a short note under the description. More room for long descriptions.

All table amounts after discount are before tax; VAT remains explicitly in the
unchanged invoice totals. Values are rendered from the same sample lines, not
recomputed differently between styles. Subtle horizontal and vertical rules separate rows and columns; proportional
column widths and tabular numeric glyphs improve symmetry. Existing manual bank entries survive switching table proposals.

Browser regression checks six sample rows, equal money-column widths, no monetary
cell overflow and unchanged final total for all three structures in A4 and A5.
The six generated example PDFs were each confirmed one-page with pypdf. Screen
captures are outside Git in /home/user/outputs/shop-table-{balanced,detailed,minimal}.png.
These checks still exercise only the isolated static prototype, not the application.

Per review, all three proposals include thin vertical column separators, in
screen and print, checked for both header and data cells in A4/A5.

The lower summary now fills the invoice width: a compact payment/quantity strip,
two equal columns of four summary figures, and a full-width highlighted final
payable row. This replaces the tall blank right-hand panel in all three table
proposals. Browser geometry assertions cover equal widths/alignment in A4/A5.

Latest revision supersedes paired totals: payment/quantity are directly ABOVE
the item table. A final sum row aligns quantities and additive monetary figures
with their respective columns; unit prices are deliberately not summed. Repeated
gross/discount/net summary rows below are removed. VAT and final payable remain
below the table in equal-width cells. Minimal layout still shows row discounts
in descriptions and the total discount is derivable from those invoice lines.

Review correction: the item sum footer was rejected and removed. Monetary
summary restored to the preceding full-width two-column layout, including gross,
discount, net, VAT and a full-width final payable. Payment method and quantity
remain above the item table; no other table proposal or bank behavior changed.

Latest review: invoice date/number values sit next to fixed-width labels with
small gaps instead of space-between. Monetary summary is restored to the original
single narrow column on the left (280px A4 / 210px A5); all five rows stack
vertically. Payment/quantity stay above the item table. Geometry regression
checks cover label gaps and stacked summary rows across all proposals/papers.

The three comparison proposals have now been rejected and removed, including
the selector and dynamic layout code. The original six columns are restored:
row, description, quantity, unit price, gross amount before deductions, remarks.
Column separators remain. Single-column totals and payment/quantity above the
table are unchanged. Earlier proposal descriptions are historical, not active.
