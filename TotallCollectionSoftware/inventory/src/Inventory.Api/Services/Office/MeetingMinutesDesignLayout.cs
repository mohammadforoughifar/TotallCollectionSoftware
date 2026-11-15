using System.Globalization;
using System.Xml.Linq;

namespace Inventory.Api.Services.Office;

/// <summary>
/// هندسهٔ چاپ «صورتجلسه» را از خودِ فایل طرح شرکت (MRT) می‌خواند:
/// <c>Reports/MeetingMinutes/Companies/{CompanyId}/Minutes-{A4|A5}.mrt</c>
///
/// چرا؟ همان کاری که برای نامهٔ داخلی و نامهٔ صادره انجام شد: جای عنوان، شماره، تاریخ جلسه،
/// حاضرین، غایبین و بندها در خودِ قالب Stimulsoft تعیین می‌شود، نه با اعداد ثابتِ داخل کد.
/// هر تغییری که طراح در Stimulsoft بدهد (جابه‌جایی، فونت، رنگ، برچسب فارسی) همان در چاپ
/// دیده می‌شود.
///
/// نکته‌های خواندن قالب:
/// • واحد قالب <c>ReportUnit</c> است (پیش‌فرض این پروژه Inches) و به point تبدیل می‌شود.
/// • مستطیل هر جزء نسبت به حاشیهٔ صفحه است: نقطهٔ مطلق = (حاشیه + مقدار) × ضریب.
/// • جزءهای <c>Enabled=False</c> نادیده گرفته می‌شوند.
/// • هر برچسب (متن ثابت مثل «:حاضرین») به کادری وصل می‌شود که ردیفش با آن یکی است؛ پس
///   برچسب‌ها هم از قالب می‌آیند و در کد هارد‌کد نمی‌شوند.
/// • شماره/تاریخ/پیوست در کادرهای سربرگِ چاپ‌شده می‌نشینند (بالای صفحه)، پس «بدنه» از عنوان
///   شروع می‌شود و این سه کادر فقط برای لایهٔ متای سربرگ خوانده می‌شوند.
/// • این خواننده هرگز استثنا نمی‌دهد؛ قالب ناقص/خراب → <c>null</c> و چاپ به مسیر قبلی برمی‌گردد.
/// </summary>
internal sealed class MeetingMinutesDesignLayout
{
    internal enum Slot
    {
        Caption,
        Title,
        Number,
        MeetingDate,
        Attachment,
        Attendees,
        Absentees,
        Items
    }

    /// <summary>یک کادر روی صفحه با مختصات فیزیکی point از گوشهٔ بالا-چپ صفحه.</summary>
    internal sealed record Box(
        Slot Slot,
        float Left,
        float Top,
        float Width,
        float Height,
        string Text,
        float FontSize,
        bool Bold,
        string Color,
        bool AlignCenter,
        bool AlignRight,
        bool AlignMiddle,
        bool Wrap,
        // پس‌زمینهٔ کادر؛ «null» یعنی شفاف (روی تصویر سربرگ، بدون پوشاندن چیزی).
        // شرکت ۱ سربرگش نقطه‌چین دارد و در قالب <Brush>White</Brush> اعلام می‌کند تا چاپ
        // یک نوار سفید پشت مقدار بکشد و نقطه‌ها متن را ناخوانا نکنند.
        string? Fill = null);

    internal float PageWidth { get; private init; }
    internal float PageHeight { get; private init; }
    internal IReadOnlyList<Box> Boxes { get; private init; } = Array.Empty<Box>();

    internal Box? Title => First(Slot.Title);
    internal Box? Number => First(Slot.Number);
    internal Box? MeetingDate => First(Slot.MeetingDate);

    /// <summary>کادر «پیوست: دارد/ندارد» روی برچسب چاپ‌شدهٔ سربرگ (ممکن است در قالب نباشد).</summary>
    internal Box? Attachment => First(Slot.Attachment);
    internal Box? Attendees => First(Slot.Attendees);
    internal Box? Absentees => First(Slot.Absentees);
    internal Box? Items => First(Slot.Items);

    /// <summary>
    /// کادر برچسبِ هر بخش (مثل «:حاضرین»). متن برچسب از خودِ قالب خوانده می‌شود تا تغییر آن
    /// در Stimulsoft بی‌درنگ در چاپ دیده شود.
    /// </summary>
    internal Box? Caption(Slot slot)
    {
        var value = First(slot);
        if (value == null) return null;
        var caption = Boxes
            .Where(box => box.Slot == Slot.Caption && !string.IsNullOrWhiteSpace(box.Text))
            .OrderBy(box => Math.Abs(box.Top - value.Top))
            .FirstOrDefault();
        if (caption == null) return null;
        // برچسب باید روی همان ردیف باشد (بالای کادر مقدار)، نه یک بخش دیگر.
        return Math.Abs(caption.Top - value.Top) <= Math.Max(6f, value.Height) ? caption : null;
    }

    /// <summary>آیا قالب هر پنج جزء الزامی صورتجلسه را اعلام کرده است؟</summary>
    internal bool HasRequiredSlots
        => Title != null && Number != null && MeetingDate != null
           && Attendees != null && Absentees != null;

    /// <summary>بالای اولین بخش فهرست‌شده در قالب (شروع محتوای جاری: بندها و امضاها).</summary>
    internal float ContentStart
    {
        get
        {
            var boxes = new[] { Title, Attendees, Absentees, Items }
                .Where(box => box != null)
                .Select(box => box!)
                .ToList();
            return boxes.Count == 0 ? 0f : boxes.Min(box => box.Top);
        }
    }

    /// <summary>پایین‌ترین مقدارِ اعلام‌شده در قالب (برای اطمینان از جا شدن محتوا).</summary>
    internal float ContentEnd => Boxes.Count == 0 ? PageHeight : Boxes.Max(box => box.Top + box.Height);

    private Box? First(Slot slot) => Boxes.FirstOrDefault(box => box.Slot == slot);

    /// <summary>خواندن طرح؛ در هر خطا/نبود اطلاعات <c>null</c> (بدون استثنا).</summary>
    internal static MeetingMinutesDesignLayout? TryRead(string? templatePath)
    {
        if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath)) return null;

        try
        {
            using var stream = File.OpenRead(templatePath);
            var root = XDocument.Load(stream, LoadOptions.None).Root;
            if (root == null) return null;

            var factor = UnitToPoints(Value(root, "ReportUnit"));
            var page = root.Descendants().FirstOrDefault(element => element.Name.LocalName == "Pages")
                ?.Elements().FirstOrDefault();
            if (page == null) return null;

            var (marginLeft, marginTop) = ParseMargins(Value(page, "Margins"));
            var pageWidth = ParseNumber(Value(page, "PageWidth")) * factor;
            var pageHeight = ParseNumber(Value(page, "PageHeight")) * factor;
            if (pageWidth <= 0 || pageHeight <= 0) return null;

            var boxes = new List<Box>();
            var components = page.Elements().FirstOrDefault(element => element.Name.LocalName == "Components")
                ?.Elements() ?? Enumerable.Empty<XElement>();

            foreach (var component in components)
            {
                if (string.Equals(Value(component, "Enabled"), "False", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!TryParseRectangle(Value(component, "ClientRectangle"), out var x, out var y,
                        out var width, out var height))
                    continue;

                var text = (Value(component, "Text") ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                var slot = Classify(text);
                if (slot == null) continue;

                var options = Value(component, "TextOptions") ?? string.Empty;
                var rightToLeft = options.Contains("RightToLeft=True", StringComparison.OrdinalIgnoreCase);
                var horizontal = (Value(component, "HorAlignment") ?? string.Empty).Trim();

                // در استیمول‌سافت با RightToLeft تراز افقی آینه می‌شود.
                if (rightToLeft)
                {
                    if (horizontal.Equals("Left", StringComparison.OrdinalIgnoreCase)) horizontal = "Right";
                    else if (horizontal.Equals("Right", StringComparison.OrdinalIgnoreCase)) horizontal = "Left";
                }

                var (fontSize, bold) = ParseFont(Value(component, "Font"));
                boxes.Add(new Box(
                    slot.Value,
                    (marginLeft + x) * factor,
                    (marginTop + y) * factor,
                    Math.Max(width * factor, 4f),
                    Math.Max(height * factor, 4f),
                    slot == Slot.Caption ? text : StripExpression(text),
                    fontSize <= 0f ? 10f : fontSize,
                    bold,
                    ParseColor(Value(component, "TextBrush")),
                    horizontal.Equals("Center", StringComparison.OrdinalIgnoreCase),
                    horizontal.Equals("Right", StringComparison.OrdinalIgnoreCase),
                    (Value(component, "VertAlignment") ?? string.Empty)
                        .Equals("Center", StringComparison.OrdinalIgnoreCase),
                    options.Contains("WordWrap=True", StringComparison.OrdinalIgnoreCase),
                    ParseFill(Value(component, "Brush"))));
            }

            if (boxes.Count == 0) return null;

            return new MeetingMinutesDesignLayout
            {
                PageWidth = pageWidth,
                PageHeight = pageHeight,
                Boxes = boxes.OrderBy(box => box.Top).ThenBy(box => box.Left).ToList()
            };
        }
        catch
        {
            // قالب خراب یا ناقص نباید چاپ صورتجلسه را متوقف کند.
            return null;
        }
    }

    /// <summary>شناسایی نقش هر کادر از روی متن/عبارت داخل قالب.</summary>
    private static Slot? Classify(string text)
    {
        if (text.Contains('{'))
        {
            // پیوست پیش از بقیه: نام ستون در قالب‌های قدیمی «Peyvast» است.
            if (Contains(text, "Attachment") || Contains(text, "Peyvast")) return Slot.Attachment;
            if (Contains(text, "MeetingDate") || Contains(text, "DateMeeting")) return Slot.MeetingDate;
            if (Contains(text, "Attendees") || Contains(text, "Hazerin")) return Slot.Attendees;
            if (Contains(text, "Absentees") || Contains(text, "Ghayebin")) return Slot.Absentees;
            if (Contains(text, "Items")) return Slot.Items;
            if (Contains(text, "Number") || Contains(text, "MinutesNo")) return Slot.Number;
            if (Contains(text, "Title")) return Slot.Title;
            // عبارت ناشناخته: نه چاپ می‌شود نه خطا می‌دهد.
            return null;
        }

        return Slot.Caption;
    }

    private static string StripExpression(string text)
    {
        var start = text.IndexOf('{');
        var end = text.IndexOf('}', start + 1);
        return start >= 0 && end > start ? text[(start + 1)..end] : text;
    }

    private static bool Contains(string text, string token)
        => text.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static string? Value(XElement parent, string name)
        => parent.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value;

    private static float ParseNumber(string? value)
        => float.TryParse((value ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0f;

    /// <summary>واحد گزارش استیمول‌سافت → point (پیش‌فرض قالب‌های این پروژه Inches است).</summary>
    private static float UnitToPoints(string? unit) => (unit ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "centimeters" or "centimeter" => 72f / 2.54f,
        "millimeters" or "millimeter" => 72f / 25.4f,
        "hundredthsofinch" => 0.72f,
        "tenthsofmillimeter" => 72f / 254f,
        _ => 72f,
    };

    /// <summary>ترتیب مقادیر <c>Margins</c>: چپ، راست، بالا، پایین.</summary>
    private static (float Left, float Top) ParseMargins(string? value)
    {
        var parts = (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => ParseNumber(part))
            .ToArray();
        return parts.Length switch
        {
            0 => (0f, 0f),
            1 => (parts[0], parts[0]),
            2 => (parts[0], parts[1]),
            _ => (parts[0], parts[2]),
        };
    }

    private static bool TryParseRectangle(string? value, out float x, out float y, out float width, out float height)
    {
        x = y = width = height = 0f;
        var parts = (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4) return false;

        var numbers = new float[4];
        for (var index = 0; index < 4; index++)
        {
            if (!float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[index]))
                return false;
        }

        x = numbers[0];
        y = numbers[1];
        width = numbers[2];
        height = numbers[3];
        return true;
    }

    /// <summary>«Vazirmatn,12,Bold» → اندازهٔ ۱۲ و پرچم توپُر.</summary>
    private static (float Size, bool Bold) ParseFont(string? value)
    {
        var parts = (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var size = 0f;
        var bold = false;
        foreach (var part in parts)
        {
            if (part.Equals("Bold", StringComparison.OrdinalIgnoreCase))
            {
                bold = true;
                continue;
            }

            if (size <= 0f && float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                && parsed > 0f)
                size = parsed;
        }

        return (size, bold);
    }

    /// <summary>
    /// پس‌زمینهٔ کادر از قالب: <c>Transparent</c> (یا خالی) یعنی «هیچ»، و رنگ اعلام‌شده یعنی
    /// چاپ یک مستطیل همرنگ پشت مقدار. سربرگ شرکت ۱ نقطه‌چین دارد، پس قالبش
    /// <c>&lt;Brush&gt;White&lt;/Brush&gt;</c> می‌گذارد تا مقدار خوانا بماند؛ قالب ۱۰۰۳ شفاف است
    /// و بنابراین همان چاپ قبلی (متن سفید روی نوار آبی) دست‌نخورده می‌ماند.
    /// </summary>
    private static string? ParseFill(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return null;
        if (text.Equals("Transparent", StringComparison.OrdinalIgnoreCase)) return null;
        return ParseColor(text);
    }

    /// <summary>رنگ متنِ اعلام‌شده در قالب؛ ناشناخته‌ها با مشکی (خوانا روی کاغذ سفید) چاپ می‌شوند.</summary>
    private static string ParseColor(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return "#000000";
        if (text.StartsWith("#", StringComparison.Ordinal)) return text;

        if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
        {
            // قالب استیمول‌سافت: [A:R:G:B] با اعداد اعشاری.
            var parts = text.Trim('[', ']').Split(':');
            if (parts.Length == 4)
            {
                var channels = new int[4];
                for (var index = 0; index < 4; index++)
                    int.TryParse(ParseNumber(parts[index]).ToString(CultureInfo.InvariantCulture).Split('.')[0],
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out channels[index]);
                return $"#{(byte)channels[1]:X2}{(byte)channels[2]:X2}{(byte)channels[3]:X2}";
            }
        }

        return text.ToLowerInvariant() switch
        {
            "white" => "#FFFFFF",
            "black" => "#000000",
            _ => "#000000",
        };
    }
}
