# Shop purchase invoice visual prototype

Independent static purchase design; sales prototype files are unchanged. Open
index.html through the static server, not as an application integration. Company
identity fonts and base styles are referenced from the sibling sales prototype.
Supplier details and supplier invoice number are read-only sample read-model data.
Purchase prices, outgoing cheques, cash paid and remaining debt are shown from the
purchaser perspective. No shop collection card/IBAN settings are displayed.
A4/A5 selection and browser print work independently. No persistence, accounting
posting, database binding, or cheque-state verification is implemented.

Header alignment correction: equal left/right grid tracks center the title on
the printable page. Metadata rows align their numbers to the left content edge,
keeping labels nearby and supplier invoice caption non-wrapping. Print geometry
checks assert centered title and flush-left values in A4/A5.

Production header integration: the nested inventory Orders print builder now has
an isolated purchase-only header with symmetric tracks, centered company/title,
and left-aligned number/date rows. Sales header path is retained. Actual company
name and order number/date are used. Current Order DTO has no supplier invoice
number and CompanyProfileDto has no logo: supplier number prints `-`, and the
existing letter-mark approach is retained rather than embedding demo identities.
This is header integration only; the complete prototype (including settlement
layout) is NOT yet wired into production. .NET SDK is unavailable here; source
contract tests pass, but compilation/runtime verification remains outstanding.
