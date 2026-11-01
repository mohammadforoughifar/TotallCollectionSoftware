using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Inventory.Api.Services.Office;

/// <summary>
/// یک مقدار سربرگ (شماره / تاریخ / پیوست) با کادر مطلق صفحه و ظاهر نهایی‌اش.
/// مختصات از گوشهٔ بالا-چپ کاغذ و به point است؛ همان چیزی که طراح در Stimulsoft می‌بیند.
/// </summary>
/// <param name="Fill">
/// پس‌زمینهٔ کادر. «null» یعنی شفاف (روی سربرگ بدون پوشاندن چیزی). قالب‌های نقطه‌چین‌دار
/// (سربرگ ۱۰۰۵ و ۱) این را <c>White</c> اعلام می‌کنند تا نقطه‌ها از پشت رقم‌ها رد نشوند.
/// </param>
internal readonly record struct HeaderMetaCell(
    float Left,
    float Top,
    float Width,
    float Height,
    string Value,
    string Color,
    float FontSize,
    string? Fill = null,
    bool Bold = true);

/// <summary>
/// چاپ شماره/تاریخ/پیوست روی برچسب‌های چاپ‌شدهٔ سربرگ — یک پیاده‌سازی برای نامهٔ داخلی،
/// نامهٔ صادره و صورتجلسه، با دو شکلِ سربرگ:
///
/// <list type="bullet">
///   <item><b>ستونی</b> (۱۰۰۳ و ۳ و ۱): سه مقدار زیر هم؛ مقدارها راست‌چین در یک ستون‌اند و
///   ۴pt پیش از برچسبِ چاپ‌شده تمام می‌شوند — همان رفتار قبلی، بدون هیچ تغییری.</item>
///   <item><b>ردیفی</b> (۱۰۰۵): سه مقدار روی یک ردیف افقی و هر کدام روی نقطه‌چین خودش؛
///   هر کادر با مختصات مطلق خودش چاپ می‌شود.</item>
/// </list>
///
/// چرا Layering؟ با <c>Row</c>، در چیدمان راست‌به‌چپ QuestPDF ترتیب فرزندان را آینه می‌کند و
/// مقدارها از جای خود می‌پریدند (همان ایرادی که یک بار گزارش شد). <c>PaddingLeft</c> + <c>Width</c>
/// فیزیکی است و به جهت چیدمان کاری ندارد.
/// </summary>
internal static class HeaderMetaPainter
{
    /// <summary>دو کادر با اختلاف عمودی کمتر از این مقدار، یک «ردیف افقی» شمرده می‌شوند.</summary>
    private const float RowTolerance = 3f;

    /// <summary>کمینهٔ عرض ستون مقدار؛ در A5 باریک‌تر از این، مقدار ناخوانا می‌شود.</summary>
    private const float MinimumColumnWidth = 44f;

    private const float TextLineHeight = 1.15f;

    private readonly record struct Row(float Top, float Height, IReadOnlyList<HeaderMetaCell> Cells);

    internal static void Draw(IContainer host, IReadOnlyList<HeaderMetaCell> cells, float columnFloor = 0f)
    {
        if (cells.Count == 0) return;

        // ستون «قدیمی» برای سربرگ‌های ستونی: مقدارها راست‌چین در یک ستون که لبهٔ راستش ۴pt
        // پیش از برچسب چاپ‌شده است (همان قرارداد تأییدشدهٔ ۱۰۰۳).
        var columnRight = cells.Min(cell => cell.Left + cell.Width) - 4f;
        var columnLeft = Math.Max(cells.Min(cell => cell.Left), columnFloor);
        if (columnRight - columnLeft < MinimumColumnWidth)
            columnLeft = Math.Max(0f, columnRight - MinimumColumnWidth);
        var columnWidth = Math.Max(20f, columnRight - columnLeft);

        host.AlignTop().Column(column =>
        {
            var cursor = 0f;
            foreach (var row in GroupIntoRows(cells))
            {
                var gap = row.Top - cursor;
                if (gap > 0.5f) column.Item().Height(gap);

                var height = Math.Max(10f, row.Height);
                if (row.Cells.Count == 1)
                {
                    var cell = row.Cells[0];
                    var width = Math.Max(12f, columnWidth);
                    column.Item().Height(height)
                        .PaddingLeft(Math.Max(0f, columnLeft))
                        .AlignLeft()
                        .Width(width)
                        .Element(box => Paint(box, cell, width));
                }
                else
                {
                    column.Item().Height(height).Layers(layers =>
                    {
                        // QuestPDF: هر «Layers» باید «دقیقاً یک» لایهٔ اصلی داشته باشد، وگرنه
                        // رندر با «The Layers component needs to have exactly one primary
                        // layer. It has none.» متوقف می‌شود — همان خطای واقعی چاپ نامهٔ داخلی
                        // شرکت ۱۰۰۵ که سه کادر (پیوست/شماره/تاریخ) در یک ردیف دارد.
                        //
                        // لایهٔ اصلی نباید خالی باشد: لایهٔ اصلیِ خالی، اندازهٔ ردیف را Empty
                        // می‌کند و QuestPDF آن ردیف را (و مقدارهایش را) رسم نمی‌کند.
                        // پس کادر اولِ ردیف نقش لایهٔ اصلی را می‌گیرد؛ بقیه لایه‌های رویی‌اند
                        // و با PaddingLeft مطلق سر جای خودشان می‌نشینند.
                        var primaryTaken = false;
                        foreach (var cell in row.Cells)
                        {
                            var width = Math.Max(12f, cell.Width);
                            var layer = primaryTaken ? layers.Layer() : layers.PrimaryLayer();
                            primaryTaken = true;
                            layer.PaddingLeft(Math.Max(0f, cell.Left))
                                .AlignLeft()
                                .Width(width)
                                .Height(height)
                                .Element(box => Paint(box, cell, width));
                        }
                    });
                }

                cursor = row.Top + height;
            }
        });
    }

    /// <summary>کادرهایی که Top یکسانی دارند (با تلورانس) در یک ردیف افقی جمع می‌شوند.</summary>
    private static List<Row> GroupIntoRows(IReadOnlyList<HeaderMetaCell> cells)
    {
        var rows = new List<Row>();
        foreach (var cell in cells.OrderBy(cell => cell.Top).ThenBy(cell => cell.Left))
        {
            if (rows.Count > 0 && Math.Abs(cell.Top - rows[^1].Top) <= RowTolerance)
            {
                var last = rows[^1];
                rows[^1] = last with
                {
                    Height = Math.Max(last.Height, cell.Height),
                    Cells = last.Cells.Append(cell).ToList()
                };
                continue;
            }

            rows.Add(new Row(cell.Top, cell.Height, new List<HeaderMetaCell> { cell }));
        }

        return rows;
    }

    /// <summary>
    /// مقدار، راست‌چین و وسط‌چین عمودی در کادر خودش. «پلاک سفید» فقط وقتی کشیده می‌شود که
    /// قالب پس‌زمینه اعلام کرده باشد؛ سربرگ‌های تیره (۱۰۰۳) شفاف می‌مانند.
    /// </summary>
    private static void Paint(IContainer cell, in HeaderMetaCell value, float width)
    {
        var box = cell;
        if (!string.IsNullOrWhiteSpace(value.Fill)) box = box.Background(value.Fill!);

        var text = string.IsNullOrWhiteSpace(value.Value) ? "—" : value.Value;
        box.AlignMiddle()
            .AlignRight()
            .Text(LetterPrintTemplate.IsolateMixedText(text))
            .FontFamily(value.Bold ? "Vazirmatn-Bold" : "Vazirmatn")
            .FontSize(FitFontSize(text, width, value.FontSize))
            .FontColor(string.IsNullOrWhiteSpace(value.Color) ? "#000000" : value.Color)
            .LineHeight(TextLineHeight)
            .AlignRight()
            .DirectionFromRightToLeft();
    }

    /// <summary>بزرگ‌ترین اندازه‌ای که مقدار در یک خط جا می‌شود (کف ۷pt، مثل نامهٔ صادره).</summary>
    internal static float FitFontSize(string text, float maxWidth, float preferred, float minimum = 7f)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f) return preferred > 0f ? preferred : 10f;
        if (preferred <= 0f) return 10f;
        var estimated = text.Length * preferred * 0.58f;
        return estimated <= maxWidth ? preferred : Math.Max(minimum, preferred * maxWidth / estimated);
    }
}
