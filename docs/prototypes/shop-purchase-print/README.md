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
