using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
namespace Inventory.Api.Services.Core;

/// <summary>Decode a bounded raster and re-encode as PNG; never store SVG, URLs or arbitrary uploads.</summary>
public static class CompanyLogoImage
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > MaxBytes * 4 / 3 + 100) throw new ArgumentException("حجم لوگو حداکثر ۲ مگابایت است.");
        var comma = value.IndexOf(',');
        if (comma < 0 || value[..comma] is not ("data:image/png;base64" or "data:image/jpeg;base64"))
            throw new ArgumentException("لوگو باید تصویر PNG یا JPEG باشد.");
        try
        {
            var bytes = Convert.FromBase64String(value[(comma + 1)..]);
            if (bytes.Length > MaxBytes) throw new ArgumentException("حجم لوگو حداکثر ۲ مگابایت است.");
            var format = Image.DetectFormat(bytes);
            if (format.Name is not ("PNG" or "JPEG")) throw new ArgumentException("فقط PNG و JPEG مجازند.");
            var info = Image.Identify(bytes);
            if (info.Width > 2048 || info.Height > 2048 || info.Width < 1 || info.Height < 1)
                throw new ArgumentException("ابعاد لوگو نباید بیشتر از ۲۰۴۸ در ۲۰۴۸ باشد.");
            using var image = Image.Load(new DecoderOptions { MaxFrames = 1 }, bytes);
            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.XmpProfile = null;
            using var output = new MemoryStream();
            image.SaveAsPng(output);
            if (output.Length > MaxBytes) throw new ArgumentException("حجم تصویر پردازش‌شده بیش از ۲ مگابایت است.");
            return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
        }
        catch (Exception ex) when (ex is FormatException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        { throw new ArgumentException("فایل لوگو یک تصویر معتبر PNG یا JPEG نیست.", ex); }
    }
}
