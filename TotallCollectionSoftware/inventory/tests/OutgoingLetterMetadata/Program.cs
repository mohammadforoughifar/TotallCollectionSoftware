using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Inventory.Api.Data;
using Inventory.Api.Services.Office.Outgoing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;

// Real production renderer + real letterhead artwork, synthetic letters only.
// No DbContext is constructed; API startup/GeneratePdfAsync/SMTP are never invoked.
var root = new DirectoryInfo(Directory.GetCurrentDirectory());
while (root != null && !Directory.Exists(Path.Combine(root.FullName, "src", "Inventory.Api"))) root = root.Parent;
var apiRoot = Path.Combine(root!.FullName, "src", "Inventory.Api");
var output = Path.GetFullPath(Environment.GetEnvironmentVariable("METADATA_TEST_OUTPUT") ?? Path.Combine(AppContext.BaseDirectory, "artifacts"));
Directory.CreateDirectory(output);
var env = new TestEnvironment { ContentRootPath = apiRoot, WebRootPath = Path.Combine(apiRoot, "wwwroot") };
var service = new OutgoingLetterPrintService(null!, env, NullLogger<OutgoingLetterPrintService>.Instance);
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    checks++; Console.WriteLine("PASS: " + message);
}
object? Call(OutgoingLetterPrintService target, string method, params object?[] arguments)
{
    try { return typeof(OutgoingLetterPrintService).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, arguments); }
    catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
}
var manifest = new List<object>();
var pdfBackground = Path.Combine(apiRoot, "wwwroot/uploads/letterheads/forough-letterhead.pdf");
var shortText = "<p>با سلام و احترام،</p><p>این نامه فقط برای آزمون جایگذاری تاریخ، شماره و پیوست روی سربرگ تهیه شده است و مکاتبهٔ واقعی نیست.</p><p>با احترام</p>";

void Render(OutgoingLetterPrintService renderer, int company, string size, string scenario,
    string number = "1405/30", bool attached = false, bool longBody = false, bool margin = true, string? background = null)
{
    var isA5 = size == "A5";
    var template = (string)Call(renderer, "FindCompanyTemplate", company, size)!;
    var design = Call(renderer, "ReadDesignLayout", company, isA5);
    Check(design != null, $"{company}/{size}/{scenario}: real MRT full layout is used");
    var letter = new OutgoingLetter
    {
        Id = 91001, CompanyId = company, SadereNumber = number, LetterNumber = "synthetic",
        DateSadere = new DateTime(2026, 11, 21), DateSabt = new DateTime(2026, 11, 20),
        Title = "آزمون جایگذاری اطلاعات سربرگ", ReceiverOrganization = "گیرندهٔ آزمایشی — بدون ارسال",
        Text = longBody ? string.Concat(Enumerable.Repeat(shortText, 20)) : shortText
    };
    var content = (byte[])Call(renderer, "BuildContentPdf", letter,
        new List<OutgoingLetterSigner>(), new Dictionary<int, string>(), new Dictionary<int, string>(),
        attached ? 2 : 0, isA5, false, null, true, company, margin, design, false, true)!;
    var pdf = (byte[])Call(renderer, "OverlayOnLetterhead", content, background ?? pdfBackground, letter.Id, isA5 ? 100f : 140f)!;
    using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
    Check(longBody ? doc.PageCount > 1 : doc.PageCount == 1, $"{company}/{size}/{scenario}: correct single/multi-page output");
    Check(doc.Pages.Cast<PdfSharp.Pdf.PdfPage>().All(p => Math.Abs(p.Width.Point - (isA5 ? 419.53 : 595.28)) < 1), $"{company}/{size}/{scenario}: paper width retained on every page");
    var name = $"{company}-{size}-{scenario}.pdf";
    File.WriteAllBytes(Path.Combine(output, name), pdf);
    var xml = XDocument.Load(template);
    var page = xml.Descendants("Pages").First().Elements().First();
    var margins = page.Element("Margins")!.Value.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
    var fields = new List<object>();
    foreach (var (column, value) in new[] { ("DateShams", "۱۴۰۵/۰۸/۳۰"), ("LetterNumber", ToFa(number)), ("Peyvast", attached ? "دارد" : "ندارد") })
    {
        var node = page.Element("Components")!.Elements().Single(e => e.Element("Text")?.Value == "{DataSource1." + column + "}");
        var r = node.Element("ClientRectangle")!.Value.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        fields.Add(new { column, value, left = (margins[0]+r[0])*72, top = (margins[2]+r[1])*72, width=r[2]*72, height=r[3]*72 });
    }
    manifest.Add(new { file = name, company, size, scenario, pages=doc.PageCount, fields });
}

foreach (var company in new[] { 1, 2 })
foreach (var size in new[] { "A4", "A5" })
{
    Render(service, company, size, "no-attachment");
    Render(service, company, size, "attached-no-margin", attached: true, margin: false);
    Render(service, company, size, "long-number", number: "1405/123456789012345678901234567890", attached: true);
    Render(service, company, size, "multipage", longBody: true);
}

var mapped = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
{ ["Reporting:OutgoingLetters:CompanyTemplateMap:501"] = "1" }).Build();
var mappedService = new OutgoingLetterPrintService(null!, env, NullLogger<OutgoingLetterPrintService>.Instance, mapped);
Render(mappedService, 501, "A5", "mapped-template", attached: true);

// A customized MRT must actually move the rendered value; old hard-coded geometry must not win.
var customRoot = Path.Combine(output, "custom-templates");
Directory.CreateDirectory(Path.Combine(customRoot, "1"));
var customTemplate = Path.Combine(customRoot, "1", "Outgoing-A5.mrt");
File.Copy(Path.Combine(apiRoot, "Reports/OutgoingLetters/Companies/1/Outgoing-A5.mrt"), customTemplate, true);
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
{ ["Reporting:Stimulsoft:TemplateRoot"] = customRoot }).Build();
var customService = new OutgoingLetterPrintService(null!, env, NullLogger<OutgoingLetterPrintService>.Instance, config);
var stampBefore = Call(customService, "PrintTemplateCacheStamp", 1, "A5") as string;
var x = XDocument.Load(customTemplate);
var dateRect = x.Descendants("Text1").First().Element("ClientRectangle")!;
var parts = dateRect.Value.Split(',');
parts[0] = (double.Parse(parts[0], CultureInfo.InvariantCulture) - 2d/72d).ToString("F6", CultureInfo.InvariantCulture);
dateRect.Value = string.Join(',', parts);
x.Save(customTemplate);
File.SetLastWriteTimeUtc(customTemplate, DateTime.UtcNow.AddSeconds(5));
var stampAfter = Call(customService, "PrintTemplateCacheStamp", 1, "A5") as string;
Check(stampBefore != stampAfter, "MRT edits change the print-cache stamp without changing the letter");
Render(customService, 1, "A5", "edited-mrt");

// Also use the actual embedded artwork, not only the registered PDF.
var art = XDocument.Load(Path.Combine(apiRoot, "Reports/OutgoingLetters/Companies/1/Outgoing-A5.mrt"));
var png = Path.Combine(output, "embedded-art.png");
File.WriteAllBytes(png, Convert.FromBase64String(art.Descendants("Watermark").First().Element("ImageBytes")!.Value));
Render(service, 1, "A5", "embedded-art", background: png);
File.Delete(customTemplate); // Do not retain the legacy encrypted connection dictionary in artifacts.
File.WriteAllText(Path.Combine(output, "render-manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"ALL {checks} OUTGOING METADATA RENDER CHECKS PASSED. PDFs: {manifest.Count}. Synthetic only; no database or mail.");

static string ToFa(string value) => string.Concat(value.Select(c => c is >= '0' and <= '9' ? (char)('۰' + c - '0') : c));
sealed class TestEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "OutgoingMetadataTests";
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
