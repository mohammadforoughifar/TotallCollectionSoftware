using System.Xml.Linq;

namespace Inventory.Api.Services.Office;

public sealed class InnerLetterPrintException(string message, Exception? inner = null) : Exception(message, inner)
{
}

/// <summary>Read only embedded art, never execute the MRT or its database dictionary.</summary>
internal static class InnerLetterheadTemplates
{
    internal static string HomepaPath(string contentRoot, bool a5) => Path.Combine(contentRoot,
        "Reports", "InnerLetters", "Companies", "1003", $"Inner-{(a5 ? "A5" : "A4")}.mrt");

    /// <summary>
    /// مسیر قالب اختصاصی هر شرکت: Reports/InnerLetters/Companies/{companyId}/Inner-{A4|A5}.mrt
    /// (پیش‌تر شناسهٔ ۱۰۰۳ در کد ثابت بود و هر شرکتی که شناسه‌اش ۱۰۰۳ بود —
    /// مثلاً نصب‌شده روی پایگاه‌دادهٔ نمونه — سربرگ شرکت دیگری را می‌گرفت.)
    /// </summary>
    internal static string CompanyTemplatePath(string contentRoot, int companyId, bool a5) => Path.Combine(
        contentRoot, "Reports", "InnerLetters", "Companies",
        companyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        $"Inner-{(a5 ? "A5" : "A4")}.mrt");

    /// <summary>
    /// خواندن تصویر سربرگ جاسازی‌شده در قالب همان شرکت. اگر قالب نباشد یا سربرگ
    /// جاسازی‌شده نداشته باشد <c>null</c> برمی‌گردد (نه خطا) تا مسیرهای دیگر امتحان شوند.
    /// </summary>
    internal static byte[]? TryLoadForCompany(string contentRoot, string baseDirectory, int companyId, bool a5)
    {
        if (companyId <= 0) return null;
        var paths = new[] { CompanyTemplatePath(contentRoot, companyId, a5), CompanyTemplatePath(baseDirectory, companyId, a5) }
            .Distinct().ToArray();
        var path = paths.FirstOrDefault(File.Exists);
        if (path == null) return null;
        using var stream = File.OpenRead(path);
        var document = XDocument.Load(stream);
        var encoded = document.Descendants().Where(e => e.Name.LocalName == "Watermark")
            .SelectMany(e => e.Elements()).FirstOrDefault(e => e.Name.LocalName == "ImageBytes")?.Value;
        if (string.IsNullOrWhiteSpace(encoded)) return null;
        var bytes = Convert.FromBase64String(encoded);
        return bytes.Length == 0 ? null : bytes;
    }

    // Company 1's black-on-white value slots differ from Homepa's white-on-colour layout.
    // Read only coordinates from its MRT; never execute expressions or database sources.
    internal sealed record ValueLayout(
        Outgoing.HomepaLetterheadLayout.Field Number,
        Outgoing.HomepaLetterheadLayout.Field Date,
        Outgoing.HomepaLetterheadLayout.Field Attachment);

    internal static ValueLayout? LoadValueLayout(string contentRoot, string baseDirectory, int companyId, bool a5)
    {
        if (companyId != 1) return null;
        var path = new[] { CompanyTemplatePath(contentRoot, companyId, a5), CompanyTemplatePath(baseDirectory, companyId, a5) }
            .FirstOrDefault(File.Exists);
        if (path is null) return null;
        var document = XDocument.Load(path);
        Outgoing.HomepaLetterheadLayout.Field Field(string name)
        {
            var component = document.Descendants().First(e => e.Elements().Any(x => x.Name.LocalName == "Name" && x.Value == name));
            var values = component.Elements().First(e => e.Name.LocalName == "ClientRectangle").Value.Split(',')
                .Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture) * 72f).ToArray();
            if (values.Length != 4 || values.Any(v => !float.IsFinite(v) || v < 0) || values[2] <= 0 || values[3] <= 0)
                throw new InvalidDataException("Invalid company 1 MRT header rectangle: " + name);
            return new(values[0], values[1], values[2], values[3]);
        }
        return new(Field("Text2"), Field("Text1"), Field("Text3"));
    }

    internal static byte[] LoadHomepa(string contentRoot, string baseDirectory, bool a5)
    {
        var paths = new[] { HomepaPath(contentRoot, a5), HomepaPath(baseDirectory, a5) }.Distinct().ToArray();
        var path = paths.FirstOrDefault(File.Exists)
            ?? throw new InnerLetterPrintException($"قالب سربرگ نامه داخلی شرکت ۱۰۰۳ پیدا نشد: {string.Join(" | ", paths)}");
        try
        {
            using var stream = File.OpenRead(path);
            var document = XDocument.Load(stream);
            var encoded = document.Descendants().Where(e => e.Name.LocalName == "Watermark")
                .SelectMany(e => e.Elements()).FirstOrDefault(e => e.Name.LocalName == "ImageBytes")?.Value;
            if (string.IsNullOrWhiteSpace(encoded))
                throw new InvalidDataException("Watermark.ImageBytes خالی است.");
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length == 0) throw new InvalidDataException("تصویر سربرگ خالی است.");
            return bytes;
        }
        catch (Exception ex) when (ex is not InnerLetterPrintException)
        {
            throw new InnerLetterPrintException($"خواندن سربرگ نامه داخلی شرکت ۱۰۰۳ ناموفق بود: {path}", ex);
        }
    }
}
