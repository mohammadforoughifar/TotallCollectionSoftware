# Shop quantity, live-search, identifier and bank-form reliability

## Changes and scope
- Shop reorder/dashboard/repair and quantity/rial Kardex views now display fractional quantities with `Qty`, not the integer/money formatter. Existing stock calculation already uses decimal addition/subtraction (`20 - 12.8 = 7.2`); storage and unit rules were not changed. The shared rial Kardex keeps the previous rendering on its non-shop `/inv/kardex` route. Global `Num`, financial rounding and other modules are unchanged.
- Local credit searches now filter on input (including cut/paste/browser clear), not keyboard-only events. Category search resets pagination. Repair, expense, voucher and cheque searches debounce nonempty input, refresh immediately on clearing, reset to page one, cancel pending work on disposal and ignore stale server replies. Pages with explicit Apply/Search buttons retain their explicit filter workflow. Units and referrers already use input callbacks and request-version guards.
- Shop bank/cash forms validate optional supplied card numbers (16 digits) and Iranian IBANs (24 digits excluding optional IR), on both client and the dedicated `POST api/trs/accounts/shop` route. Existing general treasury save route and services are unchanged. Permission checks are retained. Persian/Arabic digits, normal spaces/hyphens and IR are normalized without converting identifiers to numbers or losing leading zeroes.
- Shop cheque saves also validate the optional 16-digit Sayad ID using a dedicated shop endpoint; the general treasury cheque-save endpoint is unchanged.
- Shop party forms validate postcodes (10 digits) and existing supported tax-ID lengths, with matching validation/normalization at the catalog API. Blank optional identifiers stay optional. No invented fixed length is applied to account numbers, branch codes, barcodes, or landline numbers. These checks validate length/characters, not bank checksum or identity ownership.
- Cash/bank management now reuses the existing invoice `QuickBankModal` for creation, including the bank picker and four account types. Editing retains accounting/company/sort/active controls. Created accounts switch to the matching type tab. Summary card borders/background/icon styling is isolated in `TrsAccounts.razor.css`.
- QuickBank initializes once per opening, not again when the parent rerenders while the name is blank. New opens reset previous draft fields.

## Tests

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/ShopInputReliability
# Requires .NET 8 SDK:
dotnet run --project tests/ShopInputReliability/ShopInputReliability.csproj
```

The seven Python checks are source contracts only. Program.cs exercises the production quantity formatter and identifier helpers, including the litre example, 0.001 precision, negatives, optional blanks, too-short/long values, Persian digits and leading zeroes. It is not an EF stock-ledger integration test.

.NET SDK and the deployed Blazor/customer database are unavailable in this workspace; the C# tests, Razor build, stock-ledger integration and interactive UI remain unverified here. Before release test a real 20-litre receipt / 12.8-litre sale, check 7.2 in stock and both shop Kardex reports, clear searches while older requests are pending, save short identifiers through the shop API, and compare bank/cash forms on desktop/mobile. Publish API and client together for the new shop save route and scoped CSS.
