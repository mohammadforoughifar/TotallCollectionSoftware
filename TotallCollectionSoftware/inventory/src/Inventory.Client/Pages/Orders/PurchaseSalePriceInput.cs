using System.Globalization;
using System.Text;
using Inventory.Shared;

namespace Inventory.Client.Pages.Orders;

/// <summary>Shop price input: preserve malformed text for correction; never strip a decimal or sign.</summary>
internal static class PurchaseSalePriceInput
{
    private static string NormalizeCharacters(string? value) => Fa.ToEn(value).Trim()
        .Replace('\u066C', ',').Replace('\u066B', '.');

    private static bool DigitsOnly(string value)
    {
        foreach (var c in value)
            if (c < '0' || c > '9') return false;
        return true;
    }

    private static bool TryParts(string? value, out string typed, out string whole, out string fraction, out bool hasDot, bool formatting = false)
    {
        typed = NormalizeCharacters(value);
        whole = fraction = "";
        hasDot = false;
        if (typed.Length == 0) return false;
        var dot = typed.IndexOf('.');
        if (dot != typed.LastIndexOf('.')) return false;
        hasDot = dot >= 0;
        var grouped = hasDot ? typed[..dot] : typed;
        fraction = hasDot ? typed[(dot + 1)..] : "";
        if (!DigitsOnly(fraction)) return false;
        var groups = grouped.Split(',');
        if (groups[0].Length == 0 || !DigitsOnly(groups[0])) return false;
        if (groups.Length > 1 && groups[0].Length > 3) return false;
        for (var i = 1; i < groups.Length; i++)
        {
            var extendedLastGroup = formatting && i == groups.Length - 1;
            if (!DigitsOnly(groups[i]) || (extendedLastGroup ? groups[i].Length < 3 : groups[i].Length != 3)) return false;
        }
        whole = string.Concat(groups);
        return true;
    }

    public static string Format(string? value)
    {
        if (!TryParts(value, out var typed, out var whole, out var fraction, out var dot, formatting: true)) return typed;
        var first = 0;
        while (first < whole.Length - 1 && whole[first] == '0') first++;
        whole = whole[first..];
        var grouped = new StringBuilder(whole.Length + whole.Length / 3);
        for (var i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0) grouped.Append(',');
            grouped.Append(whole[i]);
        }
        if (dot) grouped.Append('.').Append(fraction);
        return grouped.ToString();
    }

    /// <summary>New prices contain only whole rials; decimal separators remain visible but invalid.</summary>
    public static bool TryParse(string? value, out decimal result)
    {
        result = 0m;
        return TryParts(value, out _, out var whole, out _, out var dot) && !dot &&
            decimal.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out result) &&
            ShopInvoiceAmounts.IsWholePrice(result);
    }

    /// <summary>Used only to preserve an unchanged pre-existing price, never to register a new one.</summary>
    public static bool TryParseLegacy(string? value, out decimal result)
    {
        result = 0m;
        if (!TryParts(value, out _, out var whole, out var fraction, out var dot) ||
            (dot && (fraction.Length == 0 || fraction.Length > 2))) return false;
        var raw = dot ? whole + "." + fraction : whole;
        return decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result) &&
            result >= 0m && result <= 9999999999999999.99m;
    }
}
