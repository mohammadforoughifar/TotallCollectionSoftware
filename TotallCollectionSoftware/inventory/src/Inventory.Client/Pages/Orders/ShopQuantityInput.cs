using System.Globalization;
using System.Text;
using Inventory.Shared;

namespace Inventory.Client.Pages.Orders;

/// <summary>Shop quantities support decimal comma or point without silently dropping bad separators.</summary>
internal static class ShopQuantityInput
{
    private static bool Digits(string value)
    {
        foreach (var c in value) if (c < '0' || c > '9') return false;
        return true;
    }

    private static bool Parts(string? value, bool formatting, out string typed, out string whole, out string fraction, out bool hasFraction)
    {
        typed = Fa.ToEn(value).Trim().Replace('\u066C', ',').Replace('\u066B', '.');
        whole = fraction = "";
        hasFraction = false;
        if (typed.Length == 0) return false;
        foreach (var c in typed) if ((c < '0' || c > '9') && c != '.' && c != ',') return false;

        var dot = typed.LastIndexOf('.');
        var comma = typed.LastIndexOf(',');
        char decimalSeparator = '\0', groupingSeparator = '\0';
        if (dot >= 0 && comma >= 0)
        {
            decimalSeparator = dot > comma ? '.' : ',';
            groupingSeparator = dot > comma ? ',' : '.';
        }
        else if (dot >= 0) decimalSeparator = '.';
        else if (comma >= 0)
        {
            // Comma alone is a decimal separator when followed by at most two
            // digits; full groups of three denote thousands, as before.
            if (typed.Length - comma - 1 <= 2) decimalSeparator = ',';
            else groupingSeparator = ',';
        }

        var integerText = typed;
        if (decimalSeparator != '\0')
        {
            var index = typed.LastIndexOf(decimalSeparator);
            integerText = typed[..index];
            fraction = typed[(index + 1)..];
            hasFraction = true;
            if ((!formatting && fraction.Length == 0) || !Digits(fraction)) return false;
            if (integerText.Contains(decimalSeparator))
            {
                if (decimalSeparator == ',' && dot < 0) groupingSeparator = ',';
                else return false;
            }
        }
        if (groupingSeparator == '\0')
        {
            if (!Digits(integerText)) return false;
            whole = integerText.Length == 0 && hasFraction ? "0" : integerText;
            return whole.Length > 0;
        }

        var groups = integerText.Split(groupingSeparator);
        if (groups.Length == 0 || groups[0].Length is < 1 or > 3 || !Digits(groups[0])) return false;
        for (var i = 1; i < groups.Length; i++)
        {
            // The final group can grow as the user types after an automatically
            // inserted separator; it is regrouped immediately on the next render.
            var extendedLast = formatting && !hasFraction && i == groups.Length - 1;
            if (!Digits(groups[i]) || (extendedLast ? groups[i].Length < 3 : groups[i].Length != 3)) return false;
        }
        whole = string.Concat(groups);
        return true;
    }

    public static string Format(string? value)
    {
        if (!Parts(value, true, out var typed, out var whole, out var fraction, out var hasFraction)) return typed;
        var first = 0;
        while (first < whole.Length - 1 && whole[first] == '0') first++;
        whole = whole[first..];
        var result = new StringBuilder(whole.Length + whole.Length / 3);
        for (var i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0) result.Append(',');
            result.Append(whole[i]);
        }
        if (hasFraction) result.Append('.').Append(fraction);
        return result.ToString();
    }

    public static bool TryParse(string? value, out decimal result)
    {
        result = 0m;
        if (!Parts(value, false, out _, out var whole, out var fraction, out var hasFraction)) return false;
        return decimal.TryParse(hasFraction ? whole + "." + fraction : whole,
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result);
    }
}
