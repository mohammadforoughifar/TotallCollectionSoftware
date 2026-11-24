namespace Inventory.Api.Services.Office.Outgoing;

/// <summary>
/// Company 1005 (Aria Hamyar Generator), calibrated against the supplied 300-DPI A4/A5 artwork.
/// The metadata comes from the MRT first; these rectangles are a fallback for an older/incomplete template.
/// Field.Top is relative to page.Header (24pt below the A4 page top, 16pt for A5).
/// The new artwork's dotted row is at 109.92pt (A4) / 77.65pt (A5), not the scanned artwork's 97.65/69.05.
/// Each white value rectangle ends before its printed colon; body/signature spacing is unchanged.
/// </summary>
internal static class AriaLetterheadLayout
{
    internal readonly record struct Field(float Left, float Top, float Width, float Height);

    internal static string ValueColor => "#000000";

    // Header height controls only where flowing content begins; field rectangles are independent.
    internal static float HeaderHeight(bool isA5) => isA5 ? 70f : 95f;

    // Preserve the receiver/body start, clear of the new artwork and its metadata row.
    internal static float ContentTop(bool isA5) => isA5 ? 116.64f : 165.6f;
    internal static float BodyTopPadding(bool isA5) => isA5 ? 30.64f : 46.6f;

    // The footer artwork starts around 762pt on A4; keep flowing text/signatures above it.
    internal static float FooterReserve(bool isA5) => isA5 ? 68f : 97f;
    internal static float FieldFontSize(bool isA5) => isA5 ? 6.4f : 9f;

    // Dotted regions from left to right: attachment, number, date.
    // A4 absolute top = 102.4, height = 15; A5 absolute top = 72.35, height = 10.6.
    // A4 right edges = 62 / 157 / 251; A5 = 43.8 / 111 / 177 (before each printed colon).
    internal static Field Date(bool isA5) => isA5
        ? new(141.5f, 56.35f, 35.5f, 10.6f)
        : new(201f, 78.4f, 50f, 15f);

    internal static Field Number(bool isA5) => isA5
        ? new(77f, 56.35f, 34f, 10.6f)
        : new(109f, 78.4f, 48f, 15f);

    internal static Field Attachment(bool isA5) => isA5
        ? new(13.5f, 56.35f, 30.3f, 10.6f)
        : new(19f, 78.4f, 43f, 15f);

    /// <summary>Fallback in physical page coordinates, the same convention as LetterheadMetaFields.TryRead.</summary>
    internal static LetterheadMetaFields MetaFields(bool isA5)
    {
        var origin = isA5 ? 16f : 24f;
        LetterheadMetaFields.Field Convert(Field field, bool ltr) => new(
            field.Left, field.Top + origin, field.Width, field.Height,
            ValueColor, FieldFontSize(isA5), true, false, ltr, "#FFFFFF");
        return new(Convert(Number(isA5), true), Convert(Date(isA5), true), Convert(Attachment(isA5), false));
    }
}
