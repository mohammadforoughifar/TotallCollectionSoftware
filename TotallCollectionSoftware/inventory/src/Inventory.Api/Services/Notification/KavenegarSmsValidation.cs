using System.Text;

namespace Inventory.Api.Services;

/// <summary>Documented lookup restrictions, checked before a provider request.</summary>
public static class KavenegarSmsValidation
{
    // A lookup is always addressed to exactly one normalized mobile (local or international).
    public static bool Recipient(string? value) => !string.IsNullOrEmpty(value) &&
        value.Length is >= 7 and <= 17 && value.All(c => c is >= '0' and <= '9');

    // Accept one mobile only, with Persian/Arabic digits and ordinary formatting. Never strip
    // arbitrary letters or separators that could turn a list/landline into another recipient.
    public static string? NormalizeMobile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = new StringBuilder();
        foreach (var ch in value.Trim())
        {
            var n = char.GetNumericValue(ch);
            if (char.IsDigit(ch) && n is >= 0 and <= 9) { text.Append((char)('0' + (int)n)); continue; }
            if (ch == '+' && text.Length == 0) { text.Append(ch); continue; }
            if (char.IsWhiteSpace(ch) || ch is '-' or '(' or ')') continue;
            return null;
        }
        var raw = text.ToString();
        var plus = raw.StartsWith('+');
        var digits = plus ? raw[1..] : raw;
        if (digits.Length == 0 || digits.Any(c => c is < '0' or > '9')) return null;
        if (digits.StartsWith("0098", StringComparison.Ordinal)) digits = digits[4..];
        else if (digits.StartsWith("98", StringComparison.Ordinal) && (plus || digits.Length == 12)) digits = digits[2..];
        else if (plus)
            return digits.Length is >= 7 and <= 15 && digits[0] != '0' ? "00" + digits : null;
        else if (digits.StartsWith("00", StringComparison.Ordinal))
            return digits.Length is >= 9 and <= 17 && digits[2] != '0' ? digits : null;
        if (digits.Length == 10 && digits[0] == '9') digits = "0" + digits;
        return digits.Length == 11 && digits.StartsWith("09", StringComparison.Ordinal) ? digits : null;
    }

    // Template names in this integration are limited to unambiguous Latin letters/digits.
    public static bool TemplateName(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 100 && value.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9');

    // token is the numeric suffix of an RP- number; no whitespace or separator reaches it.
    public static bool NumericToken(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 100 &&
        value.All(c => c is >= '0' and <= '9');

    // token3 deadline is made of Persian letters and digits only, without separators or spaces.
    public static bool PlainToken(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 100 &&
        value.All(char.IsLetterOrDigit);

    // token10 supports up to five spaces, but never newlines, other whitespace or underline.
    // Do not truncate, transliterate or silently remove characters from the company name.
    public static bool CompanyToken(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 100 &&
        value.Count(c => c == ' ') <= 5 && value.All(c => c != '_' && !char.IsControl(c) &&
            (!char.IsWhiteSpace(c) || c == ' '));

    // Check all original characters, even those beyond a work-order title's shortening
    // point. A malformed surrogate cannot safely be sent through URI encoding either.
    public static bool Token20Characters(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return false;
                i++;
                continue;
            }
            if (char.IsLowSurrogate(c) || c == '_' || char.IsControl(c) ||
                (char.IsWhiteSpace(c) && c != ' ')) return false;
        }
        return true;
    }

    // token20 permits at most eight spaces. Repair delivery uses the actual customer's name;
    // invalid values are reported rather than truncated, invented or silently substituted.
    public static bool Token20(string? value) => Token20Characters(value) && value!.Length <= 100 &&
        value.Count(c => c == ' ') <= 8;
}
