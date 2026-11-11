using Inventory.Shared;
using Inventory.Shared.Dtos;
using Inventory.Client.Extensions;

static void Check(bool pass, string message) { if (!pass) throw new Exception(message); }

// Production decimal/formatting helpers: the stock ledger itself is not exercised here.
Check((20m - 12.8m) == 7.2m, "Decimal stock subtraction");
Check((20m - 12.8m).Qty() == "۷٫۲", "Do not render a stock balance as integer 7");
Check((7.2m + 12.8m).Qty() == "۲۰", "Whole balances have no artificial decimals");
Check(0.001m.Qty() == "۰٫۰۰۱", "Preserve stock precision");
Check((-7.2m).Qty() == "-۷٫۲", "Negative stock formatting");
Check(ShopQuantityRules.DefaultAllowsFraction("لیتر") == true, "Litre fractional unit");
Check(ShopQuantityRules.DefaultAllowsFraction("عدد") == false, "Whole units unchanged");

foreach (var length in new[] { 1, 9, 15, 17 })
    Check(ShopIdentifierRules.ValidateBank(new TrsAccount { CardNumber = new string('1', length) }) is not null, "Invalid card length");
foreach (var length in new[] { 1, 23, 25 })
    Check(ShopIdentifierRules.ValidateBank(new TrsAccount { Iban = new string('1', length) }) is not null, "Invalid IBAN length");
foreach (var bad in new[] { "123456789", "12345678901", "123456789x" })
    Check(ShopIdentifierRules.ValidateParty(new Party { PostalCode = bad }) is not null, "Invalid postcode");
var account = new TrsAccount { CardNumber = "۶۰۳۷-۹۹۱۲-۳۴۵۶-۷۸۹۰", Iban = "ir۱۲۳۴۵۶۷۸۹۰۱۲۳۴۵۶۷۸۹۰۱۲۳۴" };
Check(ShopIdentifierRules.ValidateBank(account) is null, "Persian identifiers and IR prefix");
Check(account.CardNumber == "6037991234567890", "Normalized card");
Check(account.Iban == "123456789012345678901234", "Normalized IBAN");
Check(ShopIdentifierRules.ValidateBank(new TrsAccount { CardNumber = "123456789012345X" }) is not null, "No letters in card");
Check(ShopIdentifierRules.ValidateBank(new TrsAccount { Iban = "IR" }) is not null, "Prefix alone is not IBAN");
Check(ShopIdentifierRules.ValidateBank(new TrsAccount()) is null, "Optional banking fields");
var party = new Party { PostalCode = "۰۱۲۳۴۵۶۷۸۹" };
Check(ShopIdentifierRules.ValidateParty(party) is null && party.PostalCode == "0123456789", "Postal leading zero and Persian digits");
Check(ShopIdentifierRules.ValidateParty(new Party()) is null, "Optional postcode");
Check(ShopIdentifierRules.ValidateCheque(new TrsCheque { SayadId = "۱۲۳" }) is not null, "Short Sayad");
Check(ShopIdentifierRules.ValidateCheque(new TrsCheque { SayadId = "۱۲۳۴۵۶۷۸۹۰۱۲۳۴۵۶" }) is null, "Persian Sayad");
Check(ShopIdentifierRules.ValidateCheque(new TrsCheque()) is null, "Optional Sayad");
Console.WriteLine("Shop identifier and quantity checks passed.");
