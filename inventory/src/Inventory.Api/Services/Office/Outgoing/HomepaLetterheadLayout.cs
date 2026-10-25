namespace Inventory.Api.Services.Office.Outgoing;

/// <summary>
/// Physical page coordinates (PDF points, measured from the top-left) of company 1003's values.
/// Source: Reports/OutgoingLetters/Companies/1003/Outgoing-{A4|A5}.mrt.
/// The templates use inches: page-left/top margin (0.39) plus ClientRectangle, multiplied by 72.
/// Date/attachment are calibrated a little further left: 4pt on A4 and 3pt on A5.
/// Number, vertical coordinates and field sizes retain the template's positions.
/// Keep each field independent; text height/wrapping must not move the following fields.
/// </summary>
internal static class HomepaLetterheadLayout
{
    internal readonly record struct Field(float Left, float Top, float Width, float Height);

    // Header field placement and body start spacing are deliberately independent.
    // These fixed header bounds keep values on company 1003's printed labels;
    // only the body content offset may change without moving those values.
    internal static float HeaderHeight(bool isA5) => isA5 ? 150f : 160f;
    // The art ends at about 232pt on A4 and scales to about 163pt on A5.
    // Keep roughly 32pt (11mm) clear below it in both formats.
    internal static float ReceiverTopPadding(bool isA5) => isA5 ? 28f : 80f;
    internal static float SubjectBodyGap(bool isA5) => isA5 ? 8f : 12f;
    internal static float FooterReserve(bool isA5) => isA5 ? 68f : 90f;
    internal static float HeaderValueFontSize(bool isA5) => isA5 ? 7.5f : 9.5f;

    internal static Field Number(bool isA5) => isA5
        ? new(7.2f, 41.04f, 59.04f, 12.96f)
        : new(14.4f, 60.48f, 82.08f, 14.4f);

    internal static Field Date(bool isA5) => isA5
        ? new(4.2f, 52.56f, 59.04f, 12.96f)
        : new(10.4f, 77.04f, 82.08f, 14.4f);

    internal static Field Attachment(bool isA5) => isA5
        ? new(4.2f, 64.08f, 59.04f, 12.96f)
        : new(10.4f, 93.6f, 82.08f, 14.4f);
}
