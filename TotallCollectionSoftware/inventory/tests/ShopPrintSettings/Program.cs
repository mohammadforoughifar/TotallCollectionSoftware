using Inventory.Shared.Dtos;
static void Reject(ShopPrintSettingsDto dto) { try { ShopPrintBankRules.Validate(dto); } catch (ArgumentException) { return; } throw new Exception("Expected rejection"); }
var saved = ShopPrintBankRules.Validate(new() { Banks = new() { new() { Card = "۱۲۳۴ ۵۶۷۸ ۹۰۱۲ ۳۴۵۶", Iban = "123456789012345678901234" }, new() } });
if (saved.Banks.Count != 1 || saved.Banks[0].Card != "1234567890123456" || saved.Banks[0].Iban != "IR123456789012345678901234") throw new Exception("Normalization failed");
Reject(new() { Banks = new() { new() { Card = "123" } } });
Reject(new() { Banks = new() { new() { Card = new string('1', 17) } } });
Reject(new() { Banks = new() { new() { Iban = "DE123456789012345678901234" } } });
Reject(new() { Banks = new() { new() { Card = "1234567890123456" }, new() { Card = "۱۲۳۴۵۶۷۸۹۰۱۲۳۴۵۶" } } });
Reject(new() { Banks = Enumerable.Range(0, 31).Select(_ => new ShopPrintBankDto()).ToList() });
Console.WriteLine("PASS shop bank format, normalization, optional fields, duplicates and limits");
