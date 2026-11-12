using System.Text.RegularExpressions;
namespace Inventory.Shared.Dtos;

public class ShopPrintBankDto
{
    public string Card { get; set; } = "";
    public string Iban { get; set; } = "";
}
public class ShopPrintSettingsDto
{
    public List<ShopPrintBankDto> Banks { get; set; } = new();
}
/// <summary>Shop-only digit/length validation; does not verify banking checksum or ownership.</summary>
public static class ShopPrintBankRules
{
    public static string Normalize(string? value)
    {
        var chars = (value ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').Select(c =>
            c >= '۰' && c <= '۹' ? (char)('0' + c - '۰') : c >= '٠' && c <= '٩' ? (char)('0' + c - '٠') : char.ToUpperInvariant(c));
        return new string(chars.ToArray());
    }
    public static ShopPrintSettingsDto Validate(ShopPrintSettingsDto value)
    {
        if (value.Banks is null || value.Banks.Count > 30) throw new ArgumentException("حداکثر ۳۰ ردیف اطلاعات بانکی مجاز است.");
        var result = new ShopPrintSettingsDto();
        var cards = new HashSet<string>(); var ibans = new HashSet<string>();
        foreach (var bank in value.Banks)
        {
            if (bank is null) throw new ArgumentException("ردیف بانکی معتبر نیست.");
            var card = Normalize(bank.Card); var iban = Normalize(bank.Iban);
            if (iban.Length > 0 && !iban.StartsWith("IR", StringComparison.Ordinal)) iban = "IR" + iban;
            if (card.Length > 0 && !Regex.IsMatch(card, @"\A[0-9]{16}\z")) throw new ArgumentException("شماره کارت باید دقیقاً ۱۶ رقم باشد.");
            if (iban.Length > 0 && !Regex.IsMatch(iban, @"\AIR[0-9]{24}\z")) throw new ArgumentException("شبا باید IR و دقیقاً ۲۴ رقم باشد.");
            if (card.Length > 0 && !cards.Add(card) || iban.Length > 0 && !ibans.Add(iban)) throw new ArgumentException("شماره کارت یا شبا تکراری است.");
            if (card.Length > 0 || iban.Length > 0) result.Banks.Add(new() { Card = card, Iban = iban });
        }
        return result;
    }
}
