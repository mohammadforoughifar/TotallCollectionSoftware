# Invoice discount / VAT field probe

Fields covered: line **discount** and **VAT** in the shop invoice form
(`OrderForm.razor`) and line **discount %**, **discount**, **VAT %** plus the
**invoice discount** in the FAC invoice form (`FacInvoiceForm.razor`).

Behaviour under test (see `inventory/docs/SHOP-INVOICE-DISCOUNT-VAT-FIELDS-FA.md`):

1. a zero value renders as `0`;
2. focusing a zero field empties it so the user can type immediately;
3. leaving the field without typing puts `0` back;
4. typed text is cleaned while typing (Persian digits, one decimal separator,
   thousands grouping for rial amounts).

Two entry points:

```bash
# static contracts on the two .razor files — no .NET SDK needed
python3 -m unittest discover -s tests/InvoiceRateFields -v

# real unit probe of the shared helper — needs .NET 8
dotnet run --project tests/InvoiceRateFields/InvoiceRateFields.csproj
```

The .NET probe links `src/Inventory.Client/Shared/InvoiceRateInput.cs` directly
(no copy), so the helper cannot drift from the probe.

Browser checks still worth doing by hand: focus/typing/blur in the shop sale
invoice and the purchase invoice (wizard steps), a copied invoice (F7) and an
edited saved invoice — the seeded text must show real values, never a leftover
`0` in front of a typed number.
