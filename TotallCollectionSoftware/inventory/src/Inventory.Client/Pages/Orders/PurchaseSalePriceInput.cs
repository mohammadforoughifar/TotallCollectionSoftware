using System.Globalization;
using System.Text;
using Inventory.Shared;

namespace Inventory.Client.Pages.Orders;

/// <summary>Optional sale price typed on a purchase line; formatting never changes its value.</summary>
internal static class PurchaseSalePriceInput
{
    private static string Normalize(string? value) => Fa.ToEn(value).Trim()
        .Replace(",", "").Replace("\u066C", "").Replace('\u066B', '.');

    public static string Format(string? value)
    {
        var typed = Fa.ToEn(value).Trim().Replace('\u066B', '.');
        if (typed.Length == 0) return "";
        var raw = Normalize(typed);
        // Even a separator without digits is not an optional blank price.
        if (raw.Length == 0) return typed;
        var dot = raw.IndexOf('.');
        if (dot != raw.LastIndexOf('.')) return typed;
        var whole = dot < 0 ? raw : raw[..dot];
        var fraction = dot < 0 ? "" : raw[(dot + 1)..];
        // Keep invalid input visible so validation can reject it instead of silently
        // turning a negative, a letter, a second dot, or an incomplete value into zero.
        if (whole.Length == 0 || !DigitsOnly(whole) || !DigitsOnly(fraction)) return typed;

        var first = 0;
        while (first < whole.Length - 1 && whole[first] == '0') first++;
        whole = whole[first..];
        var grouped = new StringBuilder(whole.Length + whole.Length / 3);
        for (var i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0) grouped.Append(',');
            grouped.Append(whole[i]);
        }
        if (dot >= 0) grouped.Append('.').Append(fraction);
        return grouped.ToString();
    }

    private static bool DigitsOnly(string value)
    {
        foreach (var c in value)
            if (c < '0' || c > '9') return false;
        return true;
    }

    public static bool TryParse(string? value, out decimal result)
    {
        var raw = Normalize(value);
        return decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result)
            && result >= 0 && result <= 9999999999999999.99m && decimal.Round(result, 2) == result;
    }
}
