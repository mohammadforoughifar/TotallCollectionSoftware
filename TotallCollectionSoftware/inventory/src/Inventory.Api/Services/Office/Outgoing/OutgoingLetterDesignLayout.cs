using System.Globalization;
using System.Xml.Linq;

namespace Inventory.Api.Services.Office.Outgoing;

/// <summary>
/// هندسهٔ چاپ را از خودِ فایل طرح شرکت (MRT) می‌خواند:
/// <c>Reports/OutgoingLetters/Companies/{CompanyId}/Outgoing-{A4|A5}.mrt</c>
///
/// چرا لازم است؟
/// تا پیش از این، فقط «تصویر سربرگ» و سه مقدار شماره/تاریخ/پیوست از طرح می‌آمد و جای
/// گیرنده، عنوان، متن، امضاها و رونوشت با اعداد ثابتِ داخل کد تعیین می‌شد؛ نتیجه این بود که
/// هر تغییری در طرح ۱۰۰۳ (جابه‌جایی فیلدها، اضافه‌کردن کپشن «گیرنده نامه:/عنوان نامه:»،
/// تغییر ستون‌های امضا) در چاپ دیده نمی‌شد. اکنون همان مستطیل/فونت/رنگ/ترازی که طراح در
/// قالب ذخیره کرده، مبنای چاپ است.
///
/// نکته‌های مهم خواندن قالب:
/// • واحد قالب <c>ReportUnit</c> است (پیش‌فرض این پروژه Inches) و به point تبدیل می‌شود.
/// • مستطیل هر جزء نسبت به حاشیهٔ صفحه است: نقطهٔ مطلق = (حاشیه + مقدار) × ضریب.
/// • جزءهای <c>Enabled=False</c> (لایه‌های قدیمی و خالی) نادیده گرفته می‌شوند.
/// • جزءهایی که <c>TextOptions</c> آن‌ها <c>RightToLeft=True</c> دارد، ترازشان در استیمول‌سافت
///   آینه می‌شود (Left ↔ Right)؛ همان چیزی که پیش‌نمایش HTML این پوشه هم نشان می‌دهد
///   (گیرنده/عنوان/متن راست‌چین و کپشن‌ها چپ‌چین).
/// • این خواننده هرگز استثنا نمی‌دهد؛ اگر قالب ناقص/خراب باشد <c>null</c> برمی‌گردد و چاپ
///   به مسیر قبلی (چیدمان کالیبره‌شدهٔ کد) برمی‌گردد.
/// </summary>
internal sealed class OutgoingLetterDesignLayout
{
    internal enum Slot
    {
        Caption,
        Number,
        Date,
        Attachment,
        Receiver,
        Title,
        Body,
        CopyTo,
        SignerName,
        SignerRole,
        SignatureImage
    }

    /// <summary>یک کادر روی صفحه با مختصات فیزیکی point از گوشهٔ بالا-چپ صفحه.</summary>
    internal sealed record Box(
        Slot Slot,
        int Index,
        float Left,
        float Top,
        float Width,
        float Height,
        string Text,
        string FontFamily,
        float FontSize,
        bool Bold,
        string Color,
        bool AlignRight,
        bool AlignCenter,
        bool AlignMiddle,
        bool IsolateLtr);

    private const string DataSource = "DataSource1";

    internal float PageWidth { get; private init; }
    internal float PageHeight { get; private init; }
    internal IReadOnlyList<Box> Boxes { get; private init; } = Array.Empty<Box>();

    internal Box? Body => First(Slot.Body);
    internal Box? Receiver => First(Slot.Receiver);
    internal Box? Title => First(Slot.Title);
    internal Box? CopyTo => First(Slot.CopyTo);

    /// <summary>جای تصویر امضاها (به ترتیب از چپ به راست).</summary>
    internal IReadOnlyList<Box> Signatures => All(Slot.SignatureImage);

    /// <summary>همهٔ اجزای بالای صفحه: کپشن‌ها و مقادیر (شماره/تاریخ/پیوست/گیرنده/عنوان).</summary>
    internal IReadOnlyList<Box> HeaderBoxes => Boxes
        .Where(box => box.Slot is Slot.Caption or Slot.Number or Slot.Date or Slot.Attachment
            or Slot.Receiver or Slot.Title)
        .ToList();

    internal IReadOnlyList<Box> SignerNames => All(Slot.SignerName);
    internal IReadOnlyList<Box> SignerRoles => All(Slot.SignerRole);

    /// <summary>آیا قالب به‌اندازهٔ کافی گویا هست که چیدمان از آن خوانده شود؟</summary>
    internal bool HasFullLayout => Body != null && Receiver != null && Title != null;

    /// <summary>بالای ناحیهٔ متن (پایین همهٔ اجزای سربرگ).</summary>
    internal float BodyTop => Body?.Top ?? 0f;

    /// <summary>
    /// پایین‌ترین نقطه‌ای که متن می‌تواند برود: بالای بلوک رونوشت یا تصویر امضاها.
    /// اگر قالب این بلوک‌ها را اعلام نکند، ۹۰pt برای پابرگ در نظر گرفته می‌شود.
    /// </summary>
    internal float ContentBottom
    {
        get
        {
            // پایین خودِ کادر متن در طرح هم مرز است؛ هرکدام بالاتر بود ملاک است.
            var limit = Body is { } body ? body.Top + body.Height : PageHeight - 90f;
            var tops = new List<float>();
            if (CopyTo is { } copyTo) tops.Add(copyTo.Top);
            foreach (var signature in Signatures) tops.Add(signature.Top);
            foreach (var role in SignerRoles) tops.Add(role.Top);
            if (tops.Count > 0) limit = Math.Min(limit, tops.Min());
            return limit > BodyTop ? limit : Math.Max(BodyTop, PageHeight - 90f);
        }
    }

    private Box? First(Slot slot) => Boxes.FirstOrDefault(box => box.Slot == slot);

    private IReadOnlyList<Box> All(Slot slot) => Boxes.Where(box => box.Slot == slot).ToList();

    /// <summary>خواندن طرح؛ در هر خطا/نبود اطلاعات <c>null</c> (بدون استثنا).</summary>
    internal static OutgoingLetterDesignLayout? TryRead(string? templatePath)
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
            var pageWidth = Number(Value(page, "PageWidth")) * factor;
            var pageHeight = Number(Value(page, "PageHeight")) * factor;
            if (pageWidth <= 0 || pageHeight <= 0) return null;

            var boxes = new List<Box>();
            var components = page.Elements().FirstOrDefault(element => element.Name.LocalName == "Components")
                ?.Elements() ?? Enumerable.Empty<XElement>();

            foreach (var component in components)
            {
                // لایه‌های قدیمی/خاموش قالب (مثل کادر متن قبلی) نباید چاپ شوند.
                if (string.Equals(Value(component, "Enabled"), "False", StringComparison.OrdinalIgnoreCase)) continue;
                if (!TryParseRectangle(Value(component, "ClientRectangle"), out var x, out var y, out var width, out var height))
                    continue;

                var name = Value(component, "Name") ?? string.Empty;
                var tag = component.Name.LocalName;
                var left = (marginLeft + x) * factor;
                var top = (marginTop + y) * factor;
                var boxWidth = Math.Max(width * factor, 4f);
                var boxHeight = Math.Max(height * factor, 4f);

                if (IsImage(component, tag))
                {
                    var imageIndex = TrailingIndex(name);
                    boxes.Add(new Box(Slot.SignatureImage, imageIndex, left, top, boxWidth, boxHeight,
                        string.Empty, "Vazirmatn", 8f, false, "#000000", false, true, true, false));
                    continue;
                }

                var text = Value(component, "Text") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text)) continue;

                var (slot, index, cleanText) = Classify(text);
                if (slot == Slot.Caption && string.IsNullOrWhiteSpace(cleanText)) continue;

                var rightToLeft = (Value(component, "TextOptions") ?? string.Empty)
                    .Contains("RightToLeft=True", StringComparison.OrdinalIgnoreCase);
                var horizontal = (Value(component, "HorAlignment") ?? string.Empty).Trim();
                var vertical = (Value(component, "VertAlignment") ?? string.Empty).Trim();

                // در استیمول‌سافت با RightToLeft، تراز افقی آینه می‌شود.
                if (rightToLeft)
                {
                    if (horizontal.Equals("Left", StringComparison.OrdinalIgnoreCase)) horizontal = "Right";
                    else if (horizontal.Equals("Right", StringComparison.OrdinalIgnoreCase)) horizontal = "Left";
                }

                var (fontSize, bold) = ParseFont(Value(component, "Font"));
                var fontFamily = ParseFontFamily(Value(component, "Font"));

                boxes.Add(new Box(
                    slot,
                    index,
                    left,
                    top,
                    boxWidth,
                    boxHeight,
                    cleanText,
                    fontFamily,
                    fontSize <= 0f ? 10f : fontSize,
                    bold,
                    ParseColor(Value(component, "TextBrush")),
                    horizontal.Equals("Right", StringComparison.OrdinalIgnoreCase),
                    horizontal.Equals("Center", StringComparison.OrdinalIgnoreCase),
                    vertical.Equals("Center", StringComparison.OrdinalIgnoreCase),
                    slot is Slot.Number or Slot.Date));
            }

            if (boxes.Count == 0) return null;

            return new OutgoingLetterDesignLayout
            {
                PageWidth = pageWidth,
                PageHeight = pageHeight,
                // تصویر امضاها به ترتیب افقی (چپ به راست) استفاده می‌شوند.
                Boxes = boxes
                    .OrderBy(box => box.Top)
                    .ThenBy(box => box.Left)
                    .Select(NormalizeSignatureIndex)
                    .ToList()
            };
        }
        catch
        {
            // قالب خراب یا ناقص نباید چاپ را متوقف کند؛ فراخوان به مسیر پیش‌فرض برمی‌گردد.
            return null;
        }
    }

    private static Box NormalizeSignatureIndex(Box box, int position)
        => box.Slot == Slot.SignatureImage ? box with { Index = box.Index > 0 ? box.Index : position + 1 } : box;

    private static bool IsImage(XElement component, string tag)
        => tag.StartsWith("Image", StringComparison.OrdinalIgnoreCase)
           || component.Elements().Any(element => element.Name.LocalName == "ImageData");

    /// <summary>شناسایی نقش هر جزء از روی متن/عبارت داخل قالب.</summary>
    private static (Slot Slot, int Index, string Text) Classify(string text)
    {
        var value = text.Trim();

        if (value.Contains('{'))
        {
            if (Contains(value, "LetterNumber")) return (Slot.Number, 0, value);
            if (Contains(value, "DateShams")) return (Slot.Date, 0, value);
            if (Contains(value, "Peyvast")) return (Slot.Attachment, 0, value);
            if (Contains(value, "GirandeAsli")) return (Slot.Receiver, 0, value);
            if (Contains(value, "TitleLetter")) return (Slot.Title, 0, value);
            if (Contains(value, "TextLetter")) return (Slot.Body, 0, value);
            if (Contains(value, DataSource + ".Name")) return (Slot.CopyTo, 0, value);
            if (Contains(value, DataSource + ".Desc") && value.Contains(DataSource + ".Name", StringComparison.OrdinalIgnoreCase))
                return (Slot.CopyTo, 0, value);

            var signer = SignerIndex(value);
            if (signer > 0)
                return value.Contains("SematTitle", StringComparison.OrdinalIgnoreCase)
                    ? (Slot.SignerRole, signer, value)
                    : (Slot.SignerName, signer, value);

            // عبارت ناشناخته: نه چاپ می‌شود نه خطا می‌دهد.
            return (Slot.Caption, 0, string.Empty);
        }

        return (Slot.Caption, 0, value);
    }

    private static bool Contains(string text, string token)
        => text.Contains(token, StringComparison.OrdinalIgnoreCase);

    /// <summary>«{Name2}» یا «{SematTitle3}» → ۲ و ۳.</summary>
    private static int SignerIndex(string text)
    {
        foreach (var token in new[] { "SematTitle", "Name" })
        {
            var index = text.IndexOf('{' + token, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            index += token.Length + 1;
            var digits = new string(text.Skip(index).TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }

        return 0;
    }

    private static int TrailingIndex(string name)
    {
        var digits = new string(name.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static string? Value(XElement parent, string name)
        => parent.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value;

    private static float Number(string? value)
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
            .Select(part => Number(part))
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

            if (size <= 0f && float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0f)
                size = parsed;
        }

        return (size, bold);
    }

    private static string ParseFontFamily(string? value)
    {
        var first = (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(first) ? "Vazirmatn" : first;
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
                    int.TryParse(Number(parts[index]).ToString(CultureInfo.InvariantCulture).Split('.')[0],
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
