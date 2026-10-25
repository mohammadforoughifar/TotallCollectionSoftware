using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Inventory.Shared.Security;

/// <summary>
/// Small allow-list sanitizer for rich-text letter bodies. The editor stores HTML, so
/// render only formatting and document-structure markup and discard executable markup.
/// </summary>
public static class RichLetterHtml
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "br", "ol", "ul", "li", "strong", "b", "em", "i", "u", "s", "del",
        "sub", "sup", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "table", "thead",
        "tbody", "tfoot", "tr", "td", "th", "span", "a", "img", "hr"
    };

    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
        { "br", "hr", "img" };

    private static readonly Regex DangerousBlocks = new(
        @"<\s*(script|style|iframe|object|embed|svg|math|form|button|input|textarea|select|option|video|audio|source|link|meta)\b[^>]*>.*?<\s*/\s*\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex DangerousTags = new(
        @"</?\s*(script|style|iframe|object|embed|svg|math|form|button|input|textarea|select|option|video|audio|source|link|meta)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Comments = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Tags = new(@"<[^>]*>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Attributes = new(
        @"(?<name>[^\s=/>]+)(?:\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<bare>[^\s>]+)))?",
        RegexOptions.Compiled);

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var safe = DangerousBlocks.Replace(html, string.Empty);
        safe = DangerousTags.Replace(safe, string.Empty);
        safe = Comments.Replace(safe, string.Empty);
        return Tags.Replace(safe, SanitizeTag);
    }

    private static string SanitizeTag(Match match)
    {
        var token = match.Value;
        var header = Regex.Match(token, @"^<\s*(?<close>/?)\s*(?<tag>[a-zA-Z][a-zA-Z0-9]*)\b(?<attrs>.*?)\s*/?\s*>$", RegexOptions.Singleline);
        if (!header.Success) return string.Empty;

        var tag = header.Groups["tag"].Value.ToLowerInvariant();
        if (!AllowedTags.Contains(tag)) return string.Empty;
        if (header.Groups["close"].Value == "/") return VoidTags.Contains(tag) ? string.Empty : $"</{tag}>";

        var attrs = new List<string>();
        foreach (Match attribute in Attributes.Matches(header.Groups["attrs"].Value))
        {
            var name = attribute.Groups["name"].Value.ToLowerInvariant();
            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || name is "srcdoc" or "formaction" or "xlink:href")
                continue;

            var value = attribute.Groups["dq"].Success ? attribute.Groups["dq"].Value
                : attribute.Groups["sq"].Success ? attribute.Groups["sq"].Value
                : attribute.Groups["bare"].Value;

            if (name == "style")
            {
                var style = SanitizeStyle(value);
                if (style.Length > 0) attrs.Add($"style=\"{HtmlEncoder.Default.Encode(style)}\"");
            }
            else if (name == "href" && tag == "a" && SafeLink(value))
                attrs.Add($"href=\"{HtmlEncoder.Default.Encode(value)}\"");
            else if (name == "src" && tag == "img" && SafeImageSource(value))
                attrs.Add($"src=\"{HtmlEncoder.Default.Encode(value)}\"");
            else if (name == "alt" && tag == "img")
                attrs.Add($"alt=\"{HtmlEncoder.Default.Encode(value)}\"");
            else if (name == "dir" && (value is "rtl" or "ltr" or "auto"))
                attrs.Add($"dir=\"{value}\"");
            else if ((name is "colspan" or "rowspan") && (tag is "td" or "th") && int.TryParse(value, out var span) && span is >= 1 and <= 20)
                attrs.Add($"{name}=\"{span}\"");
            else if ((name is "width" or "height") && (tag is "td" or "th" or "table" or "img") && SafeDimension(value))
                attrs.Add($"{name}=\"{HtmlEncoder.Default.Encode(value)}\"");
        }

        var suffix = VoidTags.Contains(tag) ? " />" : ">";
        return "<" + tag + (attrs.Count == 0 ? string.Empty : " " + string.Join(" ", attrs)) + suffix;
    }

    private static string SanitizeStyle(string source)
    {
        var safe = new List<string>();
        foreach (var declaration in source.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = declaration.IndexOf(':');
            if (colon <= 0) continue;
            var name = declaration[..colon].Trim().ToLowerInvariant();
            var value = declaration[(colon + 1)..].Trim();
            if (value.Length == 0 || value.Contains("url", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("expression", StringComparison.OrdinalIgnoreCase) || value.Contains("var(", StringComparison.OrdinalIgnoreCase) ||
                value.Contains('<') || value.Contains('>')) continue;

            var valid = name switch
            {
                "font-weight" => Regex.IsMatch(value, @"^(normal|bold|[1-9]00)$", RegexOptions.IgnoreCase),
                "font-style" => value is "normal" or "italic" or "oblique",
                "text-decoration" => Regex.IsMatch(value, @"^(none|underline|line-through|overline)(\s+(underline|line-through|overline))*$", RegexOptions.IgnoreCase),
                "text-align" => value is "left" or "right" or "center" or "justify" or "start" or "end",
                "direction" => value is "ltr" or "rtl",
                "unicode-bidi" => value is "normal" or "embed" or "bidi-override" or "isolate",
                "font-size" or "width" or "height" or "max-width" or "min-width" or "margin" or "margin-top" or "margin-bottom" or "padding" or "padding-top" or "padding-bottom" => SafeDimension(value) || value == "auto",
                "font-family" => Regex.IsMatch(value, @"^[\w\s,'""-]{1,120}$"),
                "color" or "background-color" => SafeColor(value),
                "border" or "border-top" or "border-bottom" or "border-left" or "border-right" => SafeBorder(value),
                "border-collapse" => value is "collapse" or "separate",
                "vertical-align" => value is "top" or "middle" or "bottom" or "baseline",
                "white-space" => value is "normal" or "pre" or "pre-wrap" or "nowrap",
                "list-style-type" => value is "decimal" or "decimal-leading-zero" or "lower-alpha" or "upper-alpha" or "lower-roman" or "upper-roman" or "disc" or "circle" or "square",
                _ => false
            };
            if (valid) safe.Add($"{name}: {value}");
        }
        return string.Join("; ", safe);
    }

    private static bool SafeLink(string value)
    {
        var trimmed = WebUtility.HtmlDecode(value).Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsControl)) return false;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return uri.Scheme is "http" or "https" or "mailto" or "tel";
        return !trimmed.StartsWith("//", StringComparison.Ordinal) && !trimmed.StartsWith("\\\\", StringComparison.Ordinal);
    }

    private static bool SafeImageSource(string value)
    {
        var trimmed = WebUtility.HtmlDecode(value).Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsControl)) return false;
        if (trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return Regex.IsMatch(trimmed, @"^data:image/(png|jpeg|gif|webp);base64,[A-Za-z0-9+/=]+$", RegexOptions.IgnoreCase);
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return uri.Scheme is "http" or "https";
        return !trimmed.StartsWith("//", StringComparison.Ordinal) && !trimmed.StartsWith("\\\\", StringComparison.Ordinal);
    }

    private static bool SafeDimension(string value) =>
        Regex.IsMatch(value.Trim(), @"^\d{1,4}(?:\.\d{1,2})?(?:px|pt|em|rem|%)?$", RegexOptions.IgnoreCase);

    private static bool SafeColor(string value) =>
        Regex.IsMatch(value.Trim(), @"^(#[0-9a-f]{3,8}|[a-z]{1,24}|rgba?\(\s*[0-9.%\s,]+\))$", RegexOptions.IgnoreCase);

    private static bool SafeBorder(string value) =>
        Regex.IsMatch(value.Trim(), @"^(?:\d{1,3}(?:\.\d{1,2})?(?:px|pt)\s+)?(none|solid|dotted|dashed|double)(?:\s+(#[0-9a-f]{3,8}|[a-z]{1,24}))?$", RegexOptions.IgnoreCase);
}
