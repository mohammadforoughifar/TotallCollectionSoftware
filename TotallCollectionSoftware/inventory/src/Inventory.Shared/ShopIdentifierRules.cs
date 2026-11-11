using Inventory.Shared.Dtos;

namespace Inventory.Shared;

/// <summary>Optional Iranian shop identifiers: empty is allowed; supplied values must be complete.</summary>
public static class ShopIdentifierRules
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Fa.ToEn(value).Trim().Replace(" ", "").Replace("-", "");
    }

    public static string? NormalizeIban(string? value)
    {
        var normalized = Normalize(value)?.ToUpperInvariant();
        return normalized?.StartsWith("IR", StringComparison.Ordinal) == true ? normalized[2..] : normalized;
    }

    public static bool IsDigits(string value) => value.All(c => c is >= '0' and <= '9');
    public static string? ValidateBank(TrsAccount account)
    {
        account.CardNumber = Normalize(account.CardNumber);
        account.Iban = NormalizeIban(account.Iban);
        if (account.CardNumber is not null && (account.CardNumber.Length != 16 || !IsDigits(account.CardNumber)))
            return "شماره کارت باید دقیقاً ۱۶ رقم باشد.";
        if (account.Iban is not null && (account.Iban.Length != 24 || !IsDigits(account.Iban)))
            return "شماره شبا باید دقیقاً ۲۴ رقم بدون IR (یا ۲۶ کاراکتر با IR) باشد.";
        return null;
    }

    public static string? ValidateCheque(TrsCheque cheque)
    {
        cheque.SayadId = Normalize(cheque.SayadId);
        return cheque.SayadId is not null && (cheque.SayadId.Length != 16 || !IsDigits(cheque.SayadId))
            ? "شناسه صیاد باید دقیقاً ۱۶ رقم باشد." : null;
    }

    public static string? ValidateParty(Party party)
    {
        party.PostalCode = Normalize(party.PostalCode);
        party.TaxId = Normalize(party.TaxId);
        if (party.PostalCode is not null && (party.PostalCode.Length != 10 || !IsDigits(party.PostalCode)))
            return "کد پستی باید دقیقاً ۱۰ رقم باشد.";
        if (party.TaxId is not null && (party.TaxId.Length is not (10 or 11 or 12 or 14) || !IsDigits(party.TaxId)))
            return "شناسه ملی/مالیاتی باید ۱۰، ۱۱، ۱۲ یا ۱۴ رقم باشد.";
        return null;
    }
}
