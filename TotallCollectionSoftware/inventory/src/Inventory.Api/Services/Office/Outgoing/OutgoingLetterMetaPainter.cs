using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Inventory.Api.Services.Office.Outgoing;

/// <summary>
/// Paints the three Forough/Aria outgoing-letter values in the physical rectangles declared
/// by the MRT. Width is a width, never the right edge; fixed independent boxes prevent a
/// long number from moving the following rows onto their printed captions.
/// </summary>
internal static class OutgoingLetterMetaPainter
{
    internal static void Draw(IContainer host, LetterheadMetaFields fields,
        string number, string date, string attachment)
    {
        var cells = new[] { (fields.Date, date), (fields.Number, number), (fields.Attachment, attachment) };
        var bottom = cells.Max(cell => cell.Item1.Top + cell.Item1.Height);
        var right = cells.Max(cell => cell.Item1.Left + cell.Item1.Width);
        // Both canvas dimensions must cover every field, not only the primary date layer.
        // Otherwise moving just the date left in MRT can silently hide the other two layers.
        host.ContentFromLeftToRight().AlignLeft().AlignTop().Width(right).Height(bottom).Layers(layers =>
        {
            var first = true;
            foreach (var (field, value) in cells)
            {
                var layer = first ? layers.PrimaryLayer() : layers.Layer();
                first = false;
                var box = layer.PaddingLeft(Math.Max(0f, field.Left)).PaddingTop(Math.Max(0f, field.Top))
                    .Width(field.Width).Height(field.Height);
                // Cover only the value rectangle. The printed caption is outside it.
                if (!string.IsNullOrWhiteSpace(field.Fill)) box = box.Background(field.Fill!);
                box = box.AlignMiddle();
                box = field.Center ? box.AlignCenter() : box.AlignRight();
                var display = string.IsNullOrWhiteSpace(value) ? "—" : value;
                // Keep slash-separated dates/numbers on one line. No minimum font size that
                // could force a long value to wrap into the next fixed row.
                var preferred = Math.Min(field.FontSize, field.Height / 1.35f);
                var fontSize = Math.Min(preferred, field.Width / Math.Max(1f, display.Length * 0.65f));
                var text = box.ScaleToFit().Text(display)
                    .FontFamily(field.Bold ? "Vazirmatn-Bold" : "Vazirmatn")
                    .FontSize(fontSize).FontColor(field.Color).LineHeight(1f);
                if (field.IsolateLtr) text.DirectionFromLeftToRight();
                else text.DirectionFromRightToLeft();
            }
        });
    }
}
