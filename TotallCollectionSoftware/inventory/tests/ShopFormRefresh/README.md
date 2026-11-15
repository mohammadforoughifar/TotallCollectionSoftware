# Shop form refresh regressions

With the .NET 8 SDK, run `dotnet run --project tests/ShopFormRefresh` from the
`inventory` directory. The executable exercises the production session-only
cheque schedule and purchase sale-price formatter. It covers changed goods,
repeated changes, interest and zero interest, mixed payments, exact rounding,
manual edits, invalid schedules, Persian/Arabic digits and malformed prices.

`python3 -m unittest discover -s tests/ShopFormRefresh -p 'test_*.py'` runs the
source integration contracts. These complement but do not replace a full Razor,
API and database integration test.
