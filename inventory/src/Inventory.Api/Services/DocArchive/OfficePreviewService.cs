using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace Inventory.Api.Services.DocArchive;

/// <summary>
/// تبدیل Word/Excel به HTML برای پیش‌نمایش داخل برنامه (بدون نیاز به Office روی کلاینت).
/// </summary>
public static class OfficePreviewHtml
{
    public static bool CanHandle(string fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var ct = (contentType ?? "").ToLowerInvariant();
        return ext is ".docx" or ".xlsx" or ".csv"
            || ct.Contains("wordprocessingml")
            || ct.Contains("spreadsheetml")
            || ct.Contains("csv");
    }

    public static bool TryBuild(byte[] bytes, string fileName, string? contentType, out string html, out string? error)
    {
        html = "";
        error = null;
        if (bytes is not { Length: > 0 })
        {
            error = "فایل خالی است.";
            return false;
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var ct = (contentType ?? "").ToLowerInvariant();
        var isZip = bytes.Length > 3 && bytes[0] == 0x50 && bytes[1] == 0x4B;

        try
        {
            if (ext == ".csv" || ct.Contains("csv"))
            {
                html = Wrap(fileName, "Excel", CsvToHtml(bytes));
                return true;
            }

            if (ext == ".docx" || ct.Contains("wordprocessingml") || (ext == ".doc" && isZip))
            {
                html = Wrap(fileName, "Word", DocxToHtml(bytes));
                return true;
            }

            if (ext == ".xlsx" || ct.Contains("spreadsheetml") || (ext == ".xls" && isZip))
            {
                html = Wrap(fileName, "Excel", XlsxToHtml(bytes));
                return true;
            }

            error = ext is ".doc" or ".xls"
                ? "فرمت قدیمی آفیس (.doc / .xls) پیش‌نمایش جدولی ندارد."
                : "این نوع فایل به HTML تبدیل نمی‌شود.";
            return false;
        }
        catch (Exception ex)
        {
            error = "تبدیل پیش‌نمایش ناموفق بود: " + ex.Message;
            return false;
        }
    }

    private static string Wrap(string fileName, string kind, string body) =>
        "<!DOCTYPE html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\"/>" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>" +
        "<title>" + Enc(fileName) + "</title><style>" + Css + "</style></head><body>" +
        "<div class=\"bar\"><span class=\"kind\">" + Enc(kind) + "</span><span>" + Enc(fileName) + "</span></div>" +
        "<div class=\"doc\">" + body + "</div></body></html>";

    private const string Css =
        "html,body{margin:0;padding:0;background:#f8fafc;color:#0f172a;" +
        "font-family:Tahoma,'Segoe UI',sans-serif;font-size:14px;line-height:1.9}" +
        ".bar{position:sticky;top:0;background:#1e3a5f;color:#fff;padding:.45rem .9rem;" +
        "display:flex;gap:.6rem;align-items:center;font-size:12px;z-index:2}" +
        ".kind{background:#38bdf8;color:#0f172a;border-radius:999px;padding:.05rem .5rem;font-weight:700}" +
        ".doc{padding:1rem 1.2rem 2rem;max-width:1100px;margin:0 auto;background:#fff;min-height:100vh}" +
        "p{margin:.35rem 0} table{border-collapse:collapse;width:100%;margin:.6rem 0 1rem;font-size:12.5px}" +
        "td,th{border:1px solid #cbd5e1;padding:.25rem .5rem;vertical-align:top}" +
        "th{background:#1e3a5f;color:#fff} .ph{color:#64748b;font-size:12px}" +
        "h3{margin:1.1rem 0 .4rem;font-size:15px;color:#1e3a5f}" +
        ".xl-wrap{overflow:auto;border:1px solid #e2e8f0;border-radius:8px}";

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string DocxToHtml(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml")
            ?? zip.Entries.FirstOrDefault(e => e.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase));
        if (entry == null) throw new InvalidOperationException("ساختار فایل Word نامعتبر است.");

        using var s = entry.Open();
        var xdoc = XDocument.Load(s);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var body = xdoc.Root?.Element(w + "body")
            ?? xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "body");
        if (body == null) throw new InvalidOperationException("بدنه سند Word یافت نشد.");

        var sb = new StringBuilder();
        Walk(body, w, sb);
        return sb.Length == 0 ? "<p class=\"ph\">متن قابل نمایش در این سند یافت نشد.</p>" : sb.ToString();
    }

    private static void Walk(XElement el, XNamespace w, StringBuilder sb)
    {
        foreach (var child in el.Elements())
        {
            var n = child.Name.LocalName;
            if (n == "p") sb.Append(Para(child, w));
            else if (n == "tbl") sb.Append(Tbl(child, w));
            else if (n is "sdt" or "sdtContent" or "tc" or "tr") Walk(child, w, sb);
        }
    }

    private static string Para(XElement p, XNamespace w)
    {
        var inner = new StringBuilder();
        foreach (var r in p.Elements())
        {
            var n = r.Name.LocalName;
            if (n == "r") inner.Append(Run(r, w));
            else if (n == "hyperlink")
            {
                foreach (var rr in r.Elements().Where(e => e.Name.LocalName == "r"))
                    inner.Append(Run(rr, w));
            }
        }
        if (inner.Length == 0)
        {
            foreach (var t in p.Elements().Where(e => e.Name.LocalName == "r")
                         .SelectMany(e => e.Elements().Where(x => x.Name.LocalName == "t")))
                inner.Append(Enc(t.Value));
        }

        var pPr = p.Elements().FirstOrDefault(e => e.Name.LocalName == "pPr");
        var jc = Attr(pPr?.Descendants().FirstOrDefault(e => e.Name.LocalName == "jc"), "val");
        var align = jc switch { "center" => "center", "left" => "left", "right" => "right", "both" => "justify", _ => null };
        var st = align != null ? $" style=\"text-align:{align}\"" : "";
        return inner.Length == 0 ? "<p><br/></p>" : $"<p{st}>{inner}</p>";
    }

    private static string Run(XElement r, XNamespace w)
    {
        var rPr = r.Elements().FirstOrDefault(e => e.Name.LocalName == "rPr");
        var b = On(rPr, "b");
        var i = On(rPr, "i");
        var u = On(rPr, "u");
        var sb = new StringBuilder();
        foreach (var child in r.Elements())
        {
            var n = child.Name.LocalName;
            if (n == "t") sb.Append(Enc(child.Value));
            else if (n == "tab") sb.Append("&emsp;");
            else if (n is "br" or "cr") sb.Append("<br/>");
            else if (n is "drawing" or "pict" or "object") sb.Append("<span class=\"ph\"> [تصویر] </span>");
        }
        var s = sb.ToString();
        if (s.Length == 0) return "";
        if (b) s = "<strong>" + s + "</strong>";
        if (i) s = "<em>" + s + "</em>";
        if (u) s = "<u>" + s + "</u>";
        return s;
    }

    private static bool On(XElement? rPr, string local)
    {
        var el = rPr?.Elements().FirstOrDefault(e => e.Name.LocalName == local);
        if (el == null) return false;
        var v = Attr(el, "val");
        return v is null or "1" or "true" or "on";
    }

    private static string? Attr(XElement? el, string local) =>
        el?.Attributes().FirstOrDefault(a => a.Name.LocalName == local)?.Value;

    private static string Tbl(XElement tbl, XNamespace w)
    {
        var sb = new StringBuilder();
        sb.Append("<table>");
        foreach (var tr in tbl.Elements().Where(e => e.Name.LocalName == "tr"))
        {
            sb.Append("<tr>");
            foreach (var tc in tr.Elements().Where(e => e.Name.LocalName == "tc"))
            {
                sb.Append("<td>");
                foreach (var p in tc.Elements().Where(e => e.Name.LocalName == "p"))
                    sb.Append(Para(p, w));
                foreach (var inner in tc.Elements().Where(e => e.Name.LocalName is "sdt" or "sdtContent"))
                    Walk(inner, w, sb);
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    private static string XlsxToHtml(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var sb = new StringBuilder();
        var any = false;
        foreach (var ws in wb.Worksheets)
        {
            var range = ws.RangeUsed();
            sb.Append("<h3>").Append(Enc(ws.Name)).Append("</h3>");
            if (range == null)
            {
                sb.Append("<p class=\"ph\">این برگه خالی است.</p>");
                continue;
            }
            any = true;
            var r1 = range.FirstRow().RowNumber();
            var r2 = Math.Min(range.LastRow().RowNumber(), r1 + 249);
            var c1 = range.FirstColumn().ColumnNumber();
            var c2 = Math.Min(range.LastColumn().ColumnNumber(), c1 + 49);
            sb.Append("<div class=\"xl-wrap\"><table>");
            for (var r = r1; r <= r2; r++)
            {
                sb.Append("<tr>");
                for (var c = c1; c <= c2; c++)
                {
                    var val = ws.Cell(r, c).GetFormattedString() ?? "";
                    var tag = r == r1 ? "th" : "td";
                    sb.Append('<').Append(tag).Append('>').Append(Enc(val)).Append("</").Append(tag).Append('>');
                }
                sb.Append("</tr>");
            }
            sb.Append("</table></div>");
            if (range.LastRow().RowNumber() > r2 || range.LastColumn().ColumnNumber() > c2)
                sb.Append("<p class=\"ph\">نمایش محدود به ۲۵۰ ردیف و ۵۰ ستون اول است.</p>");
        }
        return any ? sb.ToString() : "<p class=\"ph\">برگه‌ای برای نمایش یافت نشد.</p>";
    }

    private static string CsvToHtml(byte[] bytes)
    {
        string text;
        try { text = Encoding.UTF8.GetString(bytes); }
        catch { text = Encoding.GetEncoding(1256).GetString(bytes); }
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n').Take(251).ToList();
        if (lines.Count == 0) return "<p class=\"ph\">فایل CSV خالی است.</p>";

        char sep = ',';
        var first = lines[0];
        if (first.Count(c => c == '\t') > first.Count(c => c == ',')) sep = '\t';
        else if (first.Count(c => c == ';') > first.Count(c => c == ',')) sep = ';';

        var sb = new StringBuilder();
        sb.Append("<div class=\"xl-wrap\"><table>");
        for (var i = 0; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]) && i > 0) continue;
            var cells = SplitCsv(lines[i], sep);
            sb.Append("<tr>");
            foreach (var cell in cells.Take(50))
            {
                var tag = i == 0 ? "th" : "td";
                sb.Append('<').Append(tag).Append('>').Append(Enc(cell)).Append("</").Append(tag).Append('>');
            }
            sb.Append("</tr>");
        }
        sb.Append("</table></div>");
        return sb.ToString();
    }

    private static List<string> SplitCsv(string line, char sep)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        var q = false;
        foreach (var ch in line)
        {
            if (ch == '"') { q = !q; continue; }
            if (ch == sep && !q) { list.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        list.Add(sb.ToString());
        return list;
    }
}
