using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Inventory.Api.Data;
using Inventory.Api.Services.Office;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

// Executes the production loaders, internal-letter renderer, minutes composer and PDF overlay.
// Reflection avoids changing API visibility for this test. No API startup, DB, real people or signatures.
var root = new DirectoryInfo(Directory.GetCurrentDirectory());
while (root != null && !Directory.Exists(Path.Combine(root.FullName, "src", "Inventory.Api"))) root = root.Parent;
var apiRoot = Environment.GetEnvironmentVariable("ARIA_OFFICE_API_ROOT") ?? Path.Combine(root!.FullName, "src", "Inventory.Api");
var output = Path.GetFullPath(Environment.GetEnvironmentVariable("ARIA_OFFICE_OUTPUT") ?? Path.Combine(AppContext.BaseDirectory, "artifacts"));
var mode = Environment.GetEnvironmentVariable("ARIA_OFFICE_MODE") ?? "all";
Directory.CreateDirectory(output);
var environment = new TestEnvironment { ContentRootPath = apiRoot, WebRootPath = Path.Combine(apiRoot, "wwwroot") };
var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(apiRoot, "appsettings.json"), optional: false).Build();
var minutesService = new MeetingMinutesPrintService(null!, environment, new HttpContextAccessor(), configuration,
    NullLogger<MeetingMinutesPrintService>.Instance);
var api = typeof(MeetingMinutesPrintService).Assembly;
var flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
Type Office(string name) => api.GetType("Inventory.Api.Services.Office." + name)!;
object? Invoke(Type type, object? target, string method, params object?[] values)
{
    var info = type.GetMethods(flags).Single(m => m.Name == method);
    var parameters = info.GetParameters();
    var args = parameters.Select((p, i) => i < values.Length ? values[i] : p.DefaultValue).ToArray();
    try { return info.Invoke(target, args); }
    catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
}
object? Static(string type, string method, params object?[] args) => Invoke(Office(type), null, method, args);
object? Minutes(string method, params object?[] args) => Invoke(typeof(MeetingMinutesPrintService), minutesService, method, args);
T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name, flags)!.GetValue(target)!;
var checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception("FAIL: " + message);
    checks++; Console.WriteLine("PASS: " + message);
}
string ToFa(string value) => string.Concat(value.Select(c => c is >= '0' and <= '9' ? (char)('۰' + c - '0') : c));
var fonts = Path.Combine(apiRoot, "Resources/fonts");
var manifest = new List<object>();
var first = "ابتدای متن آزمایشی";
var last = "پایان متن آزمایشی";
var paragraph = "این بند صرفاً آزمایشی است و برای کنترل صفحه‌بندی، تکرار سربرگ جدید آریا و حفظ تمام متن در صفحات بعد نوشته شده است. اطلاعات این نمونه ساختگی است.";
var syntheticDate = new DateTime(2026, 11, 21);
var metadataDate = "۱۴۰۵/۰۸/۳۰";

byte[] Embedded(string template) => Convert.FromBase64String(XDocument.Load(template).Descendants("Watermark").First().Element("ImageBytes")!.Value);
List<object> Fields(string template, string number, bool attached, bool fallback = false, bool include = true)
{
    var fields = new List<object>();
    if (!include) return fields;
    var doc = XDocument.Load(template);
    var page = doc.Descendants("Pages").First().Elements().First();
    var margins = page.Element("Margins")!.Value.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
    foreach (var (slot, value, tokens) in new[]
    {
        ("Date", metadataDate, new[] { "DateShams", "MeetingDate" }),
        ("Number", ToFa(number), new[] { "LetterNumber", "Number" }),
        ("Attachment", attached ? "دارد" : "ندارد", new[] { "Peyvast", "Attachment" })
    })
    {
        var box = fallback ? null : page.Element("Components")!.Elements().FirstOrDefault(e => tokens.Any(t =>
            e.Element("Text")?.Value.Trim() == "{DataSource1." + t + "}"));
        float left, top, width, height;
        if (box != null)
        {
            var parts = box.Element("ClientRectangle")!.Value.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            (left, top, width, height) = ((parts[0] + margins[0]) * 72, (parts[1] + margins[2]) * 72, parts[2] * 72, parts[3] * 72);
        }
        else if (template.Contains("Inner-", StringComparison.Ordinal))
        {
            var fallbackFields = Static("Outgoing.AriaLetterheadLayout", "MetaFields", template.Contains("A5"))!;
            var field = Get<object>(fallbackFields, slot);
            (left, top, width, height) = (Get<float>(field, "Left"), Get<float>(field, "Top"), Get<float>(field, "Width"), Get<float>(field, "Height"));
        }
        else continue; // Existing minutes missing-optional-attachment behavior, not invented data.
        fields.Add(new { field = slot, value, left, top, width, height });
    }
    return fields;
}
int Save(byte[] pdf, string file, bool? expectMultiple)
{
    using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
    Check(expectMultiple is null || (expectMultiple.Value ? document.PageCount > 1 : document.PageCount == 1), file + ": requested pagination");
    var size = file.Contains("-A5-") ? PageSizes.A5 : PageSizes.A4;
    Check(document.Pages.Cast<PdfSharp.Pdf.PdfPage>().All(p => Math.Abs(p.Width.Point - size.Width) < .01
        && Math.Abs(p.Height.Point - size.Height) < .01), file + ": requested paper size on every page");
    File.WriteAllBytes(Path.Combine(output, file), pdf);
    return document.PageCount;
}

void Inner(string size, string scenario, bool attached = false, bool withHamesh = false,
    bool longBody = false, int hameshCount = 3, string number = "1405/30", bool fallback = false,
    string? templateRoot = null, int company = 1005, bool verifyNew = true)
{
    var a5 = size == "A5";
    var template = (string)Static("InnerLetterheadTemplates", "ResolveCompanyTemplatePath", apiRoot, AppContext.BaseDirectory, company, a5, templateRoot)!;
    Check(Path.GetFileName(template) == $"Inner-{size}.mrt", $"Inner/{company}/{scenario}/{size}: company/size template selected");
    // Match the production default DPI=200. Aria must bypass lossy preparation without a config change.
    var art = (byte[])Static("InnerLetterheadTemplates", "TryLoadForCompany", apiRoot, AppContext.BaseDirectory, company, a5, 200, templateRoot)!;
    var again = (byte[])Static("InnerLetterheadTemplates", "TryLoadForCompany", apiRoot, AppContext.BaseDirectory, company, a5, 200, templateRoot)!;
    Check(ReferenceEquals(art, again), $"Inner/{company}/{scenario}/{size}: warm artwork cache");
    if (verifyNew) Check(art.SequenceEqual(Embedded(template)), $"Inner/{scenario}/{size}: exact native PNG even with configured 200 DPI");
    if (company is 1 or 2)
        art = (byte[]?)Static("InnerLetterheadTemplates", "TryLoadArtworkFromLetterheadFile", apiRoot, AppContext.BaseDirectory, a5, 200,
            "forough-letterhead.pdf") ?? art;
    var meta = fallback ? null : Static("InnerLetterheadTemplates", "TryLoadMetaLayout", apiRoot, AppContext.BaseDirectory, company, a5, templateRoot);
    if (!fallback) Check(meta != null, $"Inner/{company}/{scenario}/{size}: actual metadata/body layout read");
    var rows = Array.CreateInstance(Office("InnerLetterHameshRow"), hameshCount);
    var hameshMarkers = new List<string>();
    for (var n = 0; n < hameshCount; n++)
    {
        var receiver = $"گیرنده آزمایشی هامش {ToFa((n + 1).ToString())}";
        hameshMarkers.Add(receiver);
        rows.SetValue(Activator.CreateInstance(Office("InnerLetterHameshRow"), receiver,
            hameshCount > 3 ? paragraph : "این دستور ارجاع نمونه است.", "جهت بررسی"), n);
    }
    var body = $"<p>{first}</p>" + string.Concat(Enumerable.Repeat($"<p>{paragraph}</p>", longBody ? 32 : 1)) + $"<p>{last}</p>";
    var data = Activator.CreateInstance(Office("InnerLetterPrintData"), number, syntheticDate, "آزمون چاپ نامه داخلی آریا",
        body, "گیرنده آزمایشی داخلی", "CREATOR MUST NOT PRINT", rows, "REFERRAL DATE MUST NOT PRINT")!;
    var pdf = (byte[])Static("InnerLetterPdfRenderer", "Render", data, a5, withHamesh, art, attached, fonts, meta, company, art)!;
    var file = $"inner-{company}-{size}-{scenario}.pdf";
    var pages = Save(pdf, file, longBody || (withHamesh && hameshCount > 20));
    if (company == 1005)
        manifest.Add(new { file, family = "inner", size, scenario, pages, fields = Fields(template, number, attached, fallback),
            bodyTop = a5 ? 94f : 133f, footerReserve = a5 ? 62f : 97f, repeatFooter = true,
            expected = new[] { first, last, "گیرنده آزمایشی داخلی", "آزمون چاپ نامه داخلی آریا" }.Concat(withHamesh ? hameshMarkers : Array.Empty<string>()),
            forbidden = new[] { "CREATOR MUST NOT PRINT", "REFERRAL DATE MUST NOT PRINT", "محل امضای حاضرین" }.Concat(withHamesh ? Array.Empty<string>() : hameshMarkers),
            signatureImage = false, people = Array.Empty<string>(), absent = Array.Empty<string>() });
}

// A deliberately geometric blue test mark, not a person's autograph.
byte[] SyntheticSignature()
{
    using var bitmap = new SKBitmap(240, 80);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.White);
    using var paint = new SKPaint { Color = new SKColor(0, 70, 240), Style = SKPaintStyle.Stroke, StrokeWidth = 6, IsAntialias = true };
    canvas.DrawRect(12, 12, 216, 56, paint);
    canvas.DrawLine(18, 18, 222, 62, paint);
    canvas.DrawLine(18, 62, 222, 18, paint);
    using var image = SKImage.FromBitmap(bitmap);
    using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
    return encoded.ToArray();
}
var syntheticSignature = Convert.ToBase64String(SyntheticSignature());
void MinutesPdf(string size, string scenario, bool attached = false, bool longBody = false,
    int peopleCount = 3, bool signed = false, bool empty = false, int number = 93001,
    string? templateOverride = null, bool withMeta = true, int company = 1005, bool verifyNew = true)
{
    var a5 = size == "A5";
    var template = templateOverride ?? (string)Minutes("FindTemplate", company, a5)!;
    Check(Path.GetFileName(template) == $"Minutes-{size}.mrt", $"Minutes/{company}/{scenario}/{size}: company/size template selected");
    var artPath = (string)Minutes("ExtractTemplateLetterhead", template)!;
    Check(File.ReadAllBytes(artPath).SequenceEqual(Embedded(template)), $"Minutes/{company}/{scenario}/{size}: extraction preserves every artwork byte");
    Check(artPath == (string)Minutes("ExtractTemplateLetterhead", template)!, $"Minutes/{company}/{scenario}/{size}: versioned artwork cache");
    var design = Static("MeetingMinutesDesignLayout", "TryRead", template)!;
    Check(Get<bool>(design, "HasRequiredSlots"), $"Minutes/{company}/{scenario}/{size}: production full-template path");
    var profile = Static("MinutesLetterheadProfile", "For", company, a5)!;
    if (verifyNew) Check(Get<bool>(profile, "ExactMetaRectangles"), $"Minutes/{scenario}/{size}: independent rectangles enabled only for Aria");
    if (company is 1 or 2)
        artPath = (string?)Minutes("ResolveLetterheadPath", "uploads/letterheads/forough-letterhead.pdf") ?? artPath;
    var meeting = new MeetingMinutes { Id = number, CompanyId = company, MeetingDate = syntheticDate,
        DateRegistered = syntheticDate.AddDays(-2), Title = "آزمون چاپ صورتجلسه آریا", CreatedByName = "CREATOR MUST NOT PRINT" };
    var people = Enumerable.Range(1, empty ? 0 : peopleCount).Select(n => new MeetingMinutesParticipant
    {
        Id = n, MinutesId = number, UserId = n, Name = "حاضر آزمایشی " + ToFa(n.ToString()), Kind = MinutesParticipantKind.Attendee,
        SignatureData = signed && n == 1 ? "data:image/png;base64," + syntheticSignature : n == 3 ? "invalid-synthetic-image" : null,
        SignedAt = signed && n == 1 ? syntheticDate.AddDays(-1) : null
    }).ToList();
    var attendeeNames = people.Select(p => p.Name).ToArray();
    if (!empty) people.Add(new MeetingMinutesParticipant { Id = 900, MinutesId = number, Name = "غایب آزمایشی", Kind = MinutesParticipantKind.Absent });
    var items = Enumerable.Range(1, empty ? 0 : longBody ? 24 : 3).Select(n => new MeetingMinutesItem
    {
        Id = n, MinutesId = number, RowNo = n,
        Description = n == 1 ? first : n == (longBody ? 24 : 3) ? last : paragraph,
        ResponsibleName = "مسئول نمونه", FollowUpName = "پیگیر نمونه",
        ItemStatus = n % 3 == 0 ? MinutesItemStatus.Done : n % 3 == 1 ? MinutesItemStatus.InProgress : MinutesItemStatus.Rejected
    }).ToList();
    var content = (byte[])Minutes("BuildTemplateContentPdf", meeting, people, items, design, a5,
        "آریا همیار ژنراتور", true, attached ? 2 : 0, profile, withMeta, company is 1 or 2)!;
    var pdf = (byte[])Minutes("OverlayOnLetterhead", content, artPath, Get<float>(profile, "HeaderReserve"))!;
    var file = $"minutes-{company}-{size}-{scenario}.pdf";
    var pages = Save(pdf, file, longBody ? true : peopleCount > 3 ? null : false);
    if (company == 1005)
        manifest.Add(new { file, family = "minutes", size, scenario, pages, fields = Fields(template, number.ToString(), attached, include: withMeta),
            bodyTop = Get<float>(profile, "HeaderReserve"), footerReserve = Get<float>(profile, "FooterReserve"), repeatFooter = false,
            expected = (empty ? new[] { "بندی ثبت نشده است." } : new[] { first, last, "غایب آزمایشی", "مسئول نمونه", "پیگیر نمونه", "محل امضای حاضرین", "محل امضای دستی" })
                .Concat(new[] { "آزمون چاپ صورتجلسه آریا" }).Concat(attendeeNames)
                .Concat(signed ? new[] { "امضا شد:", "۱۴۰۵/۰۸/۲۹" } : Array.Empty<string>()),
            forbidden = new[] { "CREATOR MUST NOT PRINT", "در انتظار امضا" },
            signatureImage = signed, people = attendeeNames, absent = empty ? Array.Empty<string>() : new[] { "غایب آزمایشی" } });
}

if (mode is "all" or "aria")
{
    foreach (var size in new[] { "A4", "A5" })
    {
        Inner(size, "no-attachment");
        Inner(size, "attached-with-hamesh", attached: true, withHamesh: true);
        Inner(size, "long-number", number: "1405/123456789012345678901234567890", attached: true);
        Inner(size, "multipage", longBody: true);
        Inner(size, "multipage-hamesh", attached: true, withHamesh: true, hameshCount: 35);
        Inner(size, "fallback-fields", fallback: true);
        MinutesPdf(size, "no-attachment");
        MinutesPdf(size, "attached-signature", attached: true, signed: true);
        MinutesPdf(size, "max-number", number: int.MaxValue, attached: true);
        MinutesPdf(size, "multipage", attached: true, longBody: true, signed: true);
        MinutesPdf(size, "many-participants", attached: true, peopleCount: 13, signed: true);
        MinutesPdf(size, "empty-items-and-people", empty: true);
        MinutesPdf(size, "retry-without-meta", withMeta: false);

        var a5 = size == "A5";
        var art = (byte[])Static("InnerLetterheadTemplates", "TryLoadForCompany", apiRoot, AppContext.BaseDirectory, 1005, a5)!;
        var blank = Document.Create(d => d.Page(p => { p.Size(a5 ? PageSizes.A5 : PageSizes.A4); p.PageColor(Colors.Transparent); p.Content().Height(1); })).GeneratePdf();
        var innerReference = Document.Create(d => d.Page(p => { p.Size(a5 ? PageSizes.A5 : PageSizes.A4);
            p.Background().Image(art).UseOriginalImage().FitUnproportionally(); p.Content().Height(1); })).GeneratePdf();
        File.WriteAllBytes(Path.Combine(output, $"inner-1005-{size}-art-only.pdf"), innerReference);
        var path = (string)Minutes("ExtractTemplateLetterhead", (string)Minutes("FindTemplate", 1005, a5)!)!;
        var minutesReference = (byte[])Minutes("OverlayOnLetterhead", blank, path, 0f)!;
        File.WriteAllBytes(Path.Combine(output, $"minutes-1005-{size}-art-only.pdf"), minutesReference);
        var original = Embedded((string)Static("InnerLetterheadTemplates", "ResolveCompanyTemplatePath", apiRoot, AppContext.BaseDirectory, 1005, a5)!);
        foreach (var dpi in new[] { 0, 150, 300, 600 })
            Check(((byte[])Static("InnerLetterheadTemplates", "TryLoadForCompany", apiRoot, AppContext.BaseDirectory, 1005, a5, dpi)!).SequenceEqual(original),
                $"Inner/{size}: Aria native-quality policy remains lossless at configured DPI {dpi}");
    }

    // Customize only temporary templates; always delete inherited dictionaries afterwards.
    var temporary = Path.Combine(output, "temporary-templates");
    try
    {
        Directory.CreateDirectory(Path.Combine(temporary, "inner", "1005"));
        Directory.CreateDirectory(Path.Combine(temporary, "minutes", "1005"));
        foreach (var size in new[] { "A4", "A5" })
        {
            var inner = Path.Combine(temporary, "inner/1005", $"Inner-{size}.mrt");
            var source = Path.Combine(apiRoot, "Reports/InnerLetters/Companies/1005", $"Inner-{size}.mrt");
            File.Copy(source, inner, true);
            var xml = XDocument.Load(inner);
            xml.Descendants("DateValue").First().Element("Text")!.Remove();
            xml.Save(inner);
            Inner(size, "partial-date-fallback", attached: true, templateRoot: Path.Combine(temporary, "inner"));
        }
        var innerA5 = Path.Combine(temporary, "inner/1005/Inner-A5.mrt");
        File.Copy(Path.Combine(apiRoot, "Reports/InnerLetters/Companies/1005/Inner-A5.mrt"), innerA5, true);
        var oldArt = (byte[])Static("InnerLetterheadTemplates", "TryLoadForCompany", apiRoot, AppContext.BaseDirectory, 1005, true, 200, Path.Combine(temporary, "inner"))!;
        var oldLayout = Static("InnerLetterheadTemplates", "TryLoadMetaLayout", apiRoot, AppContext.BaseDirectory, 1005, true, Path.Combine(temporary, "inner"));
        MoveDate(innerA5, "DateValue");
        var newLayout = Static("InnerLetterheadTemplates", "TryLoadMetaLayout", apiRoot, AppContext.BaseDirectory, 1005, true, Path.Combine(temporary, "inner"));
        var newArt = (byte[])Static("InnerLetterheadTemplates", "TryLoadForCompany", apiRoot, AppContext.BaseDirectory, 1005, true, 200, Path.Combine(temporary, "inner"))!;
        Check(!ReferenceEquals(oldLayout, newLayout) && !ReferenceEquals(oldArt, newArt) && newArt.SequenceEqual(oldArt),
            "Inner: file-version edit invalidates both caches without altering native pixels");
        Inner("A5", "edited-mrt", attached: true, templateRoot: Path.Combine(temporary, "inner"));

        var minutesA5 = Path.Combine(temporary, "minutes/1005/Minutes-A5.mrt");
        File.Copy(Path.Combine(apiRoot, "Reports/MeetingMinutes/Companies/1005/Minutes-A5.mrt"), minutesA5, true);
        var oldPath = (string)Minutes("ExtractTemplateLetterhead", minutesA5)!;
        MoveDate(minutesA5, "MeetingDateValue");
        var newPath = (string)Minutes("ExtractTemplateLetterhead", minutesA5)!;
        Check(newPath != oldPath && File.ReadAllBytes(oldPath).SequenceEqual(File.ReadAllBytes(newPath)),
            "Minutes: edited MRT selects a new versioned asset, retaining exact native image");
        MinutesPdf("A5", "edited-mrt", attached: true, templateOverride: minutesA5);
        var partial = XDocument.Load(minutesA5);
        partial.Descendants("AttachmentValue").First().Element("Text")!.Remove(); partial.Save(minutesA5);
        MinutesPdf("A5", "missing-optional-attachment", templateOverride: minutesA5);
    }
    finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
}

if (mode is "all" or "regression")
{
    foreach (var company in new[] { 1003, 1 })
    foreach (var size in new[] { "A4", "A5" })
    {
        Inner(size, "regression", attached: true, withHamesh: true, company: company, verifyNew: false);
        MinutesPdf(size, "regression", attached: true, signed: true, company: company, verifyNew: false);
    }
}
File.WriteAllText(Path.Combine(output, "render-manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"ALL {checks} OFFICE RENDER/LOADER CHECKS PASSED; {manifest.Count} Aria documents. Mode={mode}. No database was accessed.");

void MoveDate(string file, string component)
{
    var doc = XDocument.Load(file);
    var rect = doc.Descendants(component).First().Element("ClientRectangle")!;
    var values = rect.Value.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
    values[0] -= 2d / 72; values[1] += 1.5d / 72;
    rect.Value = string.Join(',', values.Select(x => x.ToString("F6", CultureInfo.InvariantCulture)));
    doc.Save(file); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(10));
}
sealed class TestEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "AriaOfficePrintingTests";
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
