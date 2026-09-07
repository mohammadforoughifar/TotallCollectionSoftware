using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Inventory.Api.Services.DocArchive;

public record DocExtractionResult(
    bool Success,
    string SourceType,
    string ExtractedText,
    string NormalizedText,
    int CharacterCount,
    string? ErrorMessage);

public interface IDocTextExtractorService
{
    Task<DocExtractionResult> ExtractAsync(byte[] fileBytes, string fileName, string? contentType);
    string Normalize(string? text);
    List<string> Tokenize(string? text);
    string? MakeSnippet(string content, string query, int maxContext = 100);
}

public class DocTextExtractorService : IDocTextExtractorService
{
    private readonly ILogger<DocTextExtractorService> _logger;

    public DocTextExtractorService(ILogger<DocTextExtractorService> logger)
    {
        _logger = logger;
    }

    public async Task<DocExtractionResult> ExtractAsync(byte[] fileBytes, string fileName, string? contentType)
    {
        if (fileBytes == null || fileBytes.Length == 0)
            return new DocExtractionResult(false, "Unknown", "", "", 0, "فایل خالی است.");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var ct = (contentType ?? "").ToLowerInvariant();

        try
        {
            // 1) PDF Documents (Digital + Scanned OCR)
            if (ext == ".pdf" || ct == "application/pdf")
            {
                return await ExtractPdfAsync(fileBytes);
            }

            // 2) Word Documents (.docx)
            if (ext == ".docx" || ct.Contains("wordprocessingml"))
            {
                return ExtractDocx(fileBytes);
            }

            // 3) Excel Spreadsheets (.xlsx)
            if (ext == ".xlsx" || ct.Contains("spreadsheetml"))
            {
                return ExtractXlsx(fileBytes);
            }

            // 4) Plain Text / Structured Data Files
            if (ext is ".txt" or ".csv" or ".json" or ".xml" or ".html" or ".md" or ".sql" or ".log"
                || ct.StartsWith("text/") || ct.Contains("json") || ct.Contains("xml"))
            {
                return ExtractPlainText(fileBytes);
            }

            // 5) Images (PNG, JPG, JPEG, BMP, WEBP, TIFF) -> Advanced Dual-Language OCR (Persian + English)
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".tiff" or ".tif"
                || ct.StartsWith("image/"))
            {
                return await ExtractImageOcrAsync(fileBytes, ext);
            }

            return new DocExtractionResult(false, "Unsupported", "", "", 0, $"فرمت فایل ({ext}) برای استخراج متن پشتیبانی نمی‌شود.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در استخراج متن از فایل {FileName}", fileName);
            return new DocExtractionResult(false, "Error", "", "", 0, ex.Message);
        }
    }

    // =========================================================================
    // PDF Extraction & Scanned PDF OCR Fallback
    // =========================================================================

    private async Task<DocExtractionResult> ExtractPdfAsync(byte[] bytes)
    {
        var tempPdf = Path.Combine(Path.GetTempPath(), $"doc_extract_{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(tempPdf, bytes);

            // 1. Try pdftotext first (fast & accurate for digital PDFs)
            var text = await RunProcessAsync("pdftotext", $"-layout \"{tempPdf}\" -", 15);
            var cleanText = text?.Trim() ?? "";

            // Check if PDF has real embedded text
            if (CountMeaningfulChars(cleanText) >= 30)
            {
                var norm = Normalize(cleanText);
                return new DocExtractionResult(true, "Pdf", cleanText, norm, cleanText.Length, null);
            }

            // 2. If text is empty or very short, it's likely a scanned PDF -> Run OCR on pages
            _logger.LogInformation("PDF فاقد لایه متنی کافی است، اجرای OCR بر روی صفحات اسکن‌شده...");
            var ocrText = await OcrPdfPagesAsync(tempPdf);
            if (!string.IsNullOrWhiteSpace(ocrText) && CountMeaningfulChars(ocrText) > 5)
            {
                var norm = Normalize(ocrText);
                return new DocExtractionResult(true, "PdfOcr", ocrText, norm, ocrText.Length, null);
            }

            var fallbackNorm = Normalize(cleanText);
            return new DocExtractionResult(true, "Pdf", cleanText, fallbackNorm, cleanText.Length, null);
        }
        finally
        {
            try { if (File.Exists(tempPdf)) File.Delete(tempPdf); } catch { }
        }
    }

    private async Task<string> OcrPdfPagesAsync(string pdfPath)
    {
        var prefix = Path.Combine(Path.GetTempPath(), $"pdfpage_{Guid.NewGuid():N}");
        try
        {
            // Render first 6 pages to PNG (200 DPI for high OCR accuracy)
            await RunProcessAsync("pdftoppm", $"-png -r 200 -f 1 -l 6 \"{pdfPath}\" \"{prefix}\"", 25);

            var dir = Path.GetDirectoryName(prefix)!;
            var pattern = Path.GetFileName(prefix) + "*.png";
            var pageFiles = Directory.GetFiles(dir, pattern).OrderBy(f => f).ToList();

            var sb = new StringBuilder();
            foreach (var pageFile in pageFiles)
            {
                try
                {
                    var ocr = await PerformOptimizedOcrAsync(pageFile);
                    if (!string.IsNullOrWhiteSpace(ocr))
                    {
                        sb.AppendLine(ocr);
                        sb.AppendLine();
                    }
                }
                finally
                {
                    try { File.Delete(pageFile); } catch { }
                }
            }

            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "خطا در اجرای OCR روی صفحات PDF");
            return "";
        }
    }

    // =========================================================================
    // Word Document (.docx) Extraction (Body + Headers + Footers)
    // =========================================================================

    private DocExtractionResult ExtractDocx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var sb = new StringBuilder();

        // Extract from body, headers, and footers
        var docXmlEntries = zip.Entries.Where(e =>
            e.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase)).ToList();

        if (docXmlEntries.Count == 0)
            return new DocExtractionResult(false, "Word", "", "", 0, "فایل‌های متنی در سند Word یافت نشد.");

        foreach (var entry in docXmlEntries)
        {
            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8);
            var xmlContent = reader.ReadToEnd();

            var paragraphMatches = Regex.Matches(xmlContent, @"<w:p\b[^>]*>(.*?)</w:p>", RegexOptions.Singleline);
            if (paragraphMatches.Count > 0)
            {
                foreach (Match p in paragraphMatches)
                {
                    var textMatches = Regex.Matches(p.Value, @"<w:t\b[^>]*>(.*?)</w:t>", RegexOptions.Singleline);
                    if (textMatches.Count > 0)
                    {
                        foreach (Match t in textMatches)
                        {
                            sb.Append(System.Net.WebUtility.HtmlDecode(t.Groups[1].Value));
                        }
                        sb.AppendLine();
                    }
                }
            }
            else
            {
                var textMatches = Regex.Matches(xmlContent, @"<w:t\b[^>]*>(.*?)</w:t>", RegexOptions.Singleline);
                foreach (Match t in textMatches)
                {
                    sb.Append(System.Net.WebUtility.HtmlDecode(t.Groups[1].Value)).Append(' ');
                }
            }
        }

        var text = sb.ToString().Trim();
        var norm = Normalize(text);
        return new DocExtractionResult(true, "Word", text, norm, text.Length, null);
    }

    // =========================================================================
    // Excel Spreadsheet (.xlsx) Extraction
    // =========================================================================

    private DocExtractionResult ExtractXlsx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var sb = new StringBuilder();

        // 1. Read shared strings
        var sharedStrings = zip.GetEntry("xl/sharedStrings.xml");
        if (sharedStrings != null)
        {
            using var sStream = sharedStrings.Open();
            using var sReader = new StreamReader(sStream, Encoding.UTF8);
            var sXml = sReader.ReadToEnd();
            var matches = Regex.Matches(sXml, @"<t\b[^>]*>(.*?)</t>", RegexOptions.Singleline);
            foreach (Match m in matches)
            {
                sb.Append(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).Append(" | ");
            }
            sb.AppendLine();
        }

        // 2. Read sheet cells (inline strings / values)
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet") && e.FullName.EndsWith(".xml")))
        {
            using var sheetStream = entry.Open();
            using var sheetReader = new StreamReader(sheetStream, Encoding.UTF8);
            var sheetXml = sheetReader.ReadToEnd();
            var inlineMatches = Regex.Matches(sheetXml, @"<v>(.*?)</v>", RegexOptions.Singleline);
            foreach (Match m in inlineMatches)
            {
                var val = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(val)) sb.Append(val).Append(' ');
            }
        }

        var text = sb.ToString().Trim();
        var norm = Normalize(text);
        return new DocExtractionResult(true, "Excel", text, norm, text.Length, null);
    }

    // =========================================================================
    // Plain Text Extraction
    // =========================================================================

    private DocExtractionResult ExtractPlainText(byte[] bytes)
    {
        string text;
        try
        {
            text = Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            text = Encoding.GetEncoding("windows-1256").GetString(bytes);
        }

        var norm = Normalize(text);
        return new DocExtractionResult(true, "Text", text, norm, text.Length, null);
    }

    // =========================================================================
    // Enhanced Image OCR Extraction (Persian + English with Preprocessing)
    // =========================================================================

    private async Task<DocExtractionResult> ExtractImageOcrAsync(byte[] bytes, string ext)
    {
        var tempImg = Path.Combine(Path.GetTempPath(), $"ocr_raw_{Guid.NewGuid():N}{ext}");
        try
        {
            await File.WriteAllBytesAsync(tempImg, bytes);
            var cleanText = await PerformOptimizedOcrAsync(tempImg);

            var norm = Normalize(cleanText);
            return new DocExtractionResult(true, "ImageOcr", cleanText, norm, cleanText.Length, null);
        }
        finally
        {
            try { if (File.Exists(tempImg)) File.Delete(tempImg); } catch { }
        }
    }

    /// <summary>
    /// اجرای OCR بهینه با پشتیبانی همزمان از زبان‌های فارسی و انگلیسی و استفاده از Tesseract
    /// </summary>
    private async Task<string> PerformOptimizedOcrAsync(string imagePath)
    {
        try
        {
            // ۱. اجرای مستقیم Tesseract با زبان‌های فارسی و انگلیسی
            var text = await RunProcessAsync("tesseract", $"\"{imagePath}\" stdout -l fas+eng --psm 3", 25);
            var clean = text?.Trim() ?? "";

            // ۲. اگر خروجی کم بود، حالت بلوک یکنواخت (--psm 6) را امتحان کن
            if (CountMeaningfulChars(clean) < 10)
            {
                var textPsm6 = await RunProcessAsync("tesseract", $"\"{imagePath}\" stdout -l fas+eng --psm 6", 20);
                if (!string.IsNullOrWhiteSpace(textPsm6) && textPsm6.Length > clean.Length)
                {
                    clean = textPsm6.Trim();
                }
            }

            // ۳. در صورتی که زبان فارسی نصب نباشد، بازگشت به زبان انگلیسی
            if (string.IsNullOrWhiteSpace(clean))
            {
                var textEng = await RunProcessAsync("tesseract", $"\"{imagePath}\" stdout -l eng --psm 3", 15);
                if (!string.IsNullOrWhiteSpace(textEng)) clean = textEng.Trim();
            }

            return clean;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "خطا در پردازش OCR فایل تصویر {ImagePath}", imagePath);
            return "";
        }
    }

    // =========================================================================
    // Persian & Arabic Normalization & Tokenization
    // =========================================================================

    public string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            // حذف اعراب و نشانه‌های صوتی عربی (حرکات: فتحه، کسره، ضمه، تنوین‌ها، تشدید، سکون)
            if (ch >= '\u064B' && ch <= '\u065F') continue;
            if (ch == '\u0670') continue; // الف کوتاه خنجری

            switch (ch)
            {
                case 'ي':
                case 'ى':
                case 'ئ':
                    sb.Append('ی');
                    break;
                case 'ك':
                case 'ڪ':
                    sb.Append('ک');
                    break;
                case 'ة':
                    sb.Append('ه');
                    break;
                case 'ؤ':
                    sb.Append('و');
                    break;
                case 'أ':
                case 'إ':
                case 'آ':
                case 'ٱ':
                    sb.Append('ا');
                    break;
                // یکدست‌سازی ارقام فارسی و عربی به ارقام انگلیسی
                case '۰':
                case '٠':
                    sb.Append('0');
                    break;
                case '۱':
                case '١':
                    sb.Append('1');
                    break;
                case '۲':
                case '٢':
                    sb.Append('2');
                    break;
                case '۳':
                case '٣':
                    sb.Append('3');
                    break;
                case '۴':
                case '٤':
                    sb.Append('4');
                    break;
                case '۵':
                case '٥':
                    sb.Append('5');
                    break;
                case '۶':
                case '٦':
                    sb.Append('6');
                    break;
                case '۷':
                case '٧':
                    sb.Append('7');
                    break;
                case '۸':
                case '٨':
                    sb.Append('8');
                    break;
                case '۹':
                case '٩':
                    sb.Append('9');
                    break;
                // تبدیل نیم‌فاصله و نویسه‌های کنترلی به فاصله
                case '\u200c': // Zero-width non-joiner
                case '\u200d': // Zero-width joiner
                case '\ufeff': // Byte order mark
                case '\u00a0': // Non-breaking space
                case '\u200e': // Left-to-right mark
                case '\u200f': // Right-to-left mark
                case '\r':
                case '\n':
                case '\t':
                    sb.Append(' ');
                    break;
                default:
                    sb.Append(char.ToLowerInvariant(ch));
                    break;
            }
        }

        // یکدست‌سازی فاصله‌های متوالی
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    public List<string> Tokenize(string? text)
    {
        var norm = Normalize(text);
        if (string.IsNullOrWhiteSpace(norm)) return new List<string>();

        return norm.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1)
            .Distinct()
            .ToList();
    }

    public string? MakeSnippet(string content, string query, int maxContext = 100)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(query))
            return null;

        var normContent = Normalize(content);
        var normQuery = Normalize(query);

        var idx = normContent.IndexOf(normQuery, StringComparison.OrdinalIgnoreCase);
        var matchLen = normQuery.Length;

        if (idx < 0)
        {
            // تلاش برای انطباق بر روی کلمات جداگانه عبارت جستجو
            var tokens = Tokenize(query);
            foreach (var token in tokens)
            {
                var tokenIdx = normContent.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                if (tokenIdx >= 0)
                {
                    idx = tokenIdx;
                    matchLen = token.Length;
                    break;
                }
            }
        }

        if (idx < 0)
        {
            return content.Length > 180 ? content[..180] + "…" : content;
        }

        var start = Math.Max(0, idx - maxContext);
        var length = Math.Min(content.Length - start, matchLen + (maxContext * 2));

        var prefix = start > 0 ? "… " : "";
        var suffix = (start + length) < content.Length ? " …" : "";

        var sub = content.Substring(start, length).Replace("\r", " ").Replace("\n", " ");
        return prefix + Regex.Replace(sub, @"\s+", " ").Trim() + suffix;
    }

    private static int CountMeaningfulChars(string text)
    {
        return text.Count(c => char.IsLetterOrDigit(c));
    }

    private static async Task<string> RunProcessAsync(string command, string args, int timeoutSeconds)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            p.Start();
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            var completed = await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), timeoutTask);

            if (completed == timeoutTask)
            {
                try { p.Kill(true); } catch { }
                return "";
            }

            await p.WaitForExitAsync();
            return await stdoutTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DocTextExtractor] Process {command} error: {ex.Message}");
            return "";
        }
    }
}
