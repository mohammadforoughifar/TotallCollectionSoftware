namespace Inventory.Api.Services.Office.Outgoing;

/// <summary>
/// Physical page coordinates (PDF points, measured from the top-left) of company 1003's values.
/// Source: Reports/OutgoingLetters/Companies/1003/Outgoing-{A4|A5}.mrt.
/// The templates use inches: page-left/top margin (0.39) plus ClientRectangle, multiplied by 72.
/// Keep each field independent; text height/wrapping must not move the following fields.
/// </summary>
internal static class HomepaLetterheadLayout
{
    internal readonly record struct Field(float Left, float Top, float Width, float Height);

    internal static Field Number(bool isA5) => isA5
        ? new(7.2f, 41.04f, 59.04f, 12.96f)
        : new(14.4f, 60.48f, 82.08f, 14.4f);

    internal static Field Date(bool isA5) => isA5
        ? new(7.2f, 52.56f, 59.04f, 12.96f)
        : new(14.4f, 77.04f, 82.08f, 14.4f);

    internal static Field Attachment(bool isA5) => isA5
        ? new(7.2f, 64.08f, 59.04f, 12.96f)
        : new(14.4f, 93.6f, 82.08f, 14.4f);
}
