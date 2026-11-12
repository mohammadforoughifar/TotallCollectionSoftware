# Shop dispatch note preview

Independent static dispatch/outgoing goods design, not production integration.
No monetary columns or payments. Header: dispatch/date/linked invoice. Recipient
and delivery/warehouse details are read-only, missing fields render ASCII `-`.
Items: row, description, quantity, unit, remarks. Signatures for warehouse issuer
and recipient; does not assert completed receipt/delivery. Company identity and
sample data are illustrative only; no persistence or API. Uses existing sibling
font/logo/base-style assets without modifying sales or purchase files.
Open /shop-dispatch-print/ on the prototypes server (8083). A4/A5 print supported.

Latest scope correction: unsupported invoice link, unit, contact/identity/address,
warehouse and transport fields were removed from markup and sample read model.
Only dispatch number/date, saved recipient name, item names/quantities/notes,
derived row count/quantity sum and saved document description remain. Blank
signature spaces are not prefilled recorded information. No database changes.
Title now says delivery note, not a stock decrement. Still static, not DB-bound.

Production integration: GoodsIssuesPrint now renders the approved simplified
layout from persisted GoodsIssueDto data, with exact quantities and HTML escaping.
Actual company name is used; company DTO has no logo so the existing letter-mark
fallback is retained (the static company illustration is not hardcoded). Print
from details reloads the saved document and refuses a new unsaved draft.
Paper is chosen in a cancellable A4/A5 combo dialog at print entry, not elsewhere
in orders/forms. Goods issue builder takes an optional paperSize, default A4 for
backward compatibility. No database or shared DTO changes were needed.
Source contracts and the real JS dialog are tested; .NET SDK is absent, so C#/Razor
compilation and full application runtime remain unverified.
