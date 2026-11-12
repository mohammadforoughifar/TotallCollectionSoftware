# Shop dispatch note preview

Independent static dispatch/outgoing goods design, not production integration.
No monetary columns or payments. Header: dispatch/date/linked invoice. Recipient
and delivery/warehouse details are read-only, missing fields render ASCII `-`.
Items: row, description, quantity, unit, remarks. Signatures for warehouse issuer
and recipient; does not assert completed receipt/delivery. Company identity and
sample data are illustrative only; no persistence or API. Uses existing sibling
font/logo/base-style assets without modifying sales or purchase files.
Open /shop-dispatch-print/ on the prototypes server (8083). A4/A5 print supported.
