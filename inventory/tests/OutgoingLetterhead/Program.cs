using System.Reflection;
using System.Globalization;
using System.Xml.Linq;
using Inventory.Api.Data;
using Inventory.Api.Services.Office.Outgoing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;

// Test the real renderer/helpers without constructing a DbContext, executing Program, or connecting to SQL.
var apiRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Inventory.Api"));
var output = Path.Combine(AppContext.BaseDirectory, "letterhead-probe");
Directory.CreateDirectory(output);
var env = new TestEnvironment { ContentRootPath = apiRoot, WebRootPath = Path.Combine(apiRoot, "wwwroot") };
var service = new OutgoingLetterPrintService(null!, env, NullLogger<OutgoingLetterPrintService>.Instance);
var type = typeof(OutgoingLetterPrintService);
var find = type.GetMethod("FindCompanyTemplate", BindingFlags.Instance | BindingFlags.NonPublic)!;
var read = type.GetMethod("ReadTemplateWatermark", BindingFlags.Static | BindingFlags.NonPublic)!;
var build = type.GetMethod("BuildContentPdf", BindingFlags.Instance | BindingFlags.NonPublic)!;
var overlay = type.GetMethod("OverlayOnLetterhead", BindingFlags.Static | BindingFlags.NonPublic)!;

foreach (var company in new[] { 1, 2, 3, 1003, 1005 })
foreach (var size in new[] { "A4", "A5" })
foreach (var hasAttachment in company == 1003 ? new[] { false, true } : new[] { false })
{
    var template = (string?)find.Invoke(service, new object[] { company, size });
    Check(template is not null && File.Exists(template), $"template {company}/{size} found");
    var image = (byte[]?)read.Invoke(null, new object[] { template! });
    Check(image is { Length: > 0 }, "embedded watermark decoded");
    var background = Path.Combine(output, $"{company}-{size}.jpg");
    File.WriteAllBytes(background, image!);
    var letter = new OutgoingLetter
    {
        Id = 1, CompanyId = company, LetterNumber = "1405/12",
        SadereNumber = company == 1003 && hasAttachment ? "1405/1003/12345678" : "1405/12",
        DateSabt = new DateTime(2026, 10, 22), Title = "آزمایش سربرگ شرکت",
        ReceiverOrganization = "گیرنده آزمایشی", Text = "این یک آزمایش مستقل چاپ است.\nبا تشکر"
    };
    var content = (byte[])build.Invoke(service, new object?[]
    {
        letter, new List<OutgoingLetterSigner>(), new Dictionary<int, string>(),
        hasAttachment, size == "A5", false, null, true, company
    })!;
    var pdf = (byte[])overlay.Invoke(null, new object[] { content, background })!;
    using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
    Check(document.PageCount > 0, "overlay yields a PDF page");
    Check(Math.Abs(document.Pages[0].Width.Point - (size == "A5" ? 419.53 : 595.28)) < 1, "paper size preserved");
    File.WriteAllBytes(Path.Combine(output, $"{company}-{size}-{(hasAttachment ? "attached-long-number" : "no-attachment")}.pdf"), pdf);
}
Check(find.Invoke(service, new object[] { 999999, "A4" }) is null, "never uses a different company's template");

// Validate production field coordinates against the corresponding real MRT, in points including margins.
var layoutType = type.Assembly.GetType("Inventory.Api.Services.Office.Outgoing.HomepaLetterheadLayout")!;
foreach (var size in new[] { "A4", "A5" })
{
    var template = (string)find.Invoke(service, new object[] { 1003, size })!;
    var xml = XDocument.Load(template);
    var page = xml.Descendants("Page1").Single();
    var margin = page.Element("Margins")!.Value.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
    foreach (var (field, column) in new[] { ("Number", "LetterNumber"), ("Date", "DateShams"), ("Attachment", "Peyvast") })
    {
        var rect = page.Descendants().Single(e => e.Element("Text")?.Value == "{DataSource1." + column + "}")
            .Element("ClientRectangle")!.Value.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        var expected = new[] { (margin[0] + rect[0]) * 72, (margin[2] + rect[1]) * 72, rect[2] * 72, rect[3] * 72 };
        var actual = layoutType.GetMethod(field, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { size == "A5" })!;
        var names = new[] { "Left", "Top", "Width", "Height" };
        for (var i = 0; i < names.Length; i++)
            Check(Math.Abs(Convert.ToDouble(actual.GetType().GetProperty(names[i])!.GetValue(actual)) - expected[i]) < 0.001,
                $"1003/{size}/{field}/{names[i]} matches the MRT page coordinates");
    }
}

// The configured root must also work in the active QuestPDF service, for Windows separators and absolute paths.
foreach (var configuredRoot in new[]
{
    "Reports\\OutgoingLetters\\Companies", Path.Combine(apiRoot, "Reports", "OutgoingLetters", "Companies")
})
{
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Reporting:Stimulsoft:TemplateRoot"] = configuredRoot
    }).Build();
    var configuredService = new OutgoingLetterPrintService(null!, env, NullLogger<OutgoingLetterPrintService>.Instance, config);
    Check(find.Invoke(configuredService, new object[] { 1003, "A4" }) is string path && File.Exists(path), "configured root resolved");
}
Console.WriteLine($"PDF probes written to {output}. Visually check logos/header/footer are visible; no database was accessed.");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

sealed class TestEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "OutgoingLetterheadTests";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
