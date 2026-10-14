namespace Inventory.Client.Extensions;
// A render-harness-only number formatter; the app uses its existing PersianExtensions.Num.
public static class Numbers
{
    public static string Num(this int value) => string.Concat(value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        .Select(c => c >= '0' && c <= '9' ? (char)('۰' + c - '0') : c));
}
