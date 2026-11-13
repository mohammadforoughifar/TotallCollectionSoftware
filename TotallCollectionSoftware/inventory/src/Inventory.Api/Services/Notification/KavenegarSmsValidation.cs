namespace Inventory.Api.Services;

/// <summary>Documented lookup restrictions, checked before a provider request.</summary>
public static class KavenegarSmsValidation
{
    // A lookup is always addressed to exactly one normalized mobile (local or international).
    public static bool Recipient(string? value) => !string.IsNullOrEmpty(value) &&
        value.Length is >= 7 and <= 17 && value.All(c => c is >= '0' and <= '9');

    // Template names in this integration are limited to unambiguous Latin letters/digits.
    public static bool TemplateName(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 100 && value.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9');

    // token is the numeric suffix of an RP- number; no whitespace or separator reaches it.
    public static bool NumericToken(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 100 &&
        value.All(c => c is >= '0' and <= '9');

    // token10 supports up to five spaces, but never newlines, other whitespace or underline.
    // Do not truncate, transliterate or silently remove characters from the company name.
    public static bool CompanyToken(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 100 &&
        value.Count(c => c == ' ') <= 5 && value.All(c => c != '_' && !char.IsControl(c) &&
            (!char.IsWhiteSpace(c) || c == ' '));
}
