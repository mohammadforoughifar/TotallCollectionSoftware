using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Inventory.Api.Data;
using Inventory.Api.Services.Office.Outgoing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

// Real production extraction, composition and overlay; synthetic letters only.
// No DbContext, API startup, external services, actual signatures or production letters.
var root = new DirectoryInfo(Directory.GetCurrentDirectory());
while (root != null && !Directory.Exists(Path.Combine(root.FullName, "src", "Inventory.Api"))) root = root.Parent;
var apiRoot = Path.Combine(root!.FullName, "src", "Inventory.Api");
var output = Path.GetFullPath(Environment.GetEnvironmentVariable("ARIA_TEST_OUTPUT") ?? Path.Combine(AppContext.BaseDirectory, "artifacts"));
Directory.CreateDirectory(output);
var environment = new TestEnvironment { ContentRootPath = apiRoot, WebRootPath = Path.Combine(apiRoot, "wwwroot") };
OutgoingLetterPrintService Service(IConfiguration? config = null)
    => new(null!, environment, NullLogger<OutgoingLetterPrintService>.Instance, config);
var service = Service();
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
T Get<T>(object target, string property) => (T)target.GetType().GetProperty(property)!.GetValue(target)!;
bool TemplatePreferred(OutgoingLetterPrintService target, int company)
{
    var global = (bool)typeof(OutgoingLetterPrintService).GetProperty("PreferTemplateLetterhead", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    var ids = (int[])typeof(OutgoingLetterPrintService).GetField("_templateLetterheadCompanyIds", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    return global || ids.Contains((int)Call(target, "TemplateDesignId", company)!);
}
Check(TemplatePreferred(service, 1005), "Default Aria artwork preference is MRT, not the old registered PDF");
Check(TemplatePreferred(service, 1003), "Existing Homepa template preference is retained");
Check(!TemplatePreferred(service, 3) && !TemplatePreferred(service, 999), "Other companies' default registered-file preference is retained");
Check(!(bool)Call(service, "UseDesignLayout", 1005)!, "Aria's existing body/signature flow is not switched to another design engine");
var shippingConfig = new ConfigurationBuilder().AddJsonFile(Path.Combine(apiRoot, "appsettings.json"), optional: false).Build();
var shippingService = Service(shippingConfig);
Check(TemplatePreferred(shippingService, 1005), "Shipped appsettings enables the new Aria MRT artwork");
Check(!(bool)Call(shippingService, "UseDesignLayout", 1005)!, "Shipped config preserves Aria body/signature flow");
Check(Call(service, "ResolveLetterheadPath", "uploads/letterheads/aria-1005-letterhead.pdf") is string,
    "Old registered asset still exists but does not override Aria's preferred template");

var manifest = new List<object>();
var shortBody = "<p>با سلام و احترام،</p><p>ابتدای متن آزمایشی</p><p>این نامه فقط نمونهٔ کنترل کیفیت سربرگ آریا همیار ژنراتور است و ارسال نشده است.</p><p>جای تاریخ، شماره نامه و وضعیت پیوست با تصویر جدید بررسی می‌شود.</p><p>پایان متن آزمایشی</p>";
var longParagraph = "<p>این بند آزمایشی برای بررسی صفحه‌بندی، تکرار صحیح سربرگ و جلوگیری از برخورد متن با پابرگ شرکت است. همهٔ اطلاعات این نمونه ساختگی است.</p>";

object ReadFields(OutgoingLetterPrintService renderer, string template, bool isA5, bool fallback)
{
    if (!fallback) return Call(renderer, "ReadDeclaredMetaFields", template)!;
    var layout = typeof(OutgoingLetterPrintService).Assembly.GetType("Inventory.Api.Services.Office.Outgoing.AriaLetterheadLayout")!;
    return layout.GetMethod("MetaFields", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { isA5 })!;
}

void Render(OutgoingLetterPrintService renderer, int company, string size, string scenario,
    string number = "1405/30", bool attached = false, bool longBody = false, bool margin = true,
    bool signersAndCopies = false, bool fallback = false)
{
    var isA5 = size == "A5";
    var template = (string)Call(renderer, "FindCompanyTemplate", company, size)!;
    Check(Path.GetFileName(template) == $"Outgoing-{size}.mrt", $"{scenario}/{size}: matching-size MRT is selected");
    var background = (string)Call(renderer, "ExtractTemplateLetterhead", company, isA5)!;
    var embedded = Convert.FromBase64String(XDocument.Load(template).Descendants("Watermark").First().Element("ImageBytes")!.Value);
    Check(File.ReadAllBytes(background).SequenceEqual(embedded), $"{scenario}/{size}: production extraction retains exact high-resolution PNG bytes");
    Check((string)Call(renderer, "ExtractTemplateLetterhead", company, isA5)! == background,
        $"{scenario}/{size}: extracted-asset cache reuses the same template version");
    var letter = new OutgoingLetter
    {
        Id = 92001, CompanyId = company, SadereNumber = number, LetterNumber = "synthetic",
        DateSadere = new DateTime(2026, 11, 21), DateSabt = new DateTime(2026, 11, 20),
        Title = "آزمون سربرگ جدید آریا", ReceiverOrganization = "شرکت نمونه — صرفاً آزمایشی",
        Text = longBody ? "<p>ابتدای متن آزمایشی</p>" + string.Concat(Enumerable.Repeat(longParagraph, 32)) + "<p>پایان متن آزمایشی</p>" : shortBody
    };
    var signers = signersAndCopies ? Enumerable.Range(1, 3).Select(n => new OutgoingLetterSigner
    {
        Id = n, UserId = n, SematId = n, Order = n, IsSigned = false,
        User = new User { Id = n, FirstName = "امضاکننده آزمایشی", LastName = n.ToString(), Username = "synthetic-" + n }
    }).ToList() : new List<OutgoingLetterSigner>();
    var roles = Enumerable.Range(1, 3).ToDictionary(n => n, n => "سمت نمونه " + n);
    var copies = new List<OutgoingLetterCopyTo>
    {
        new() { Id = 1, RowNo = 1, Name = "بایگانی آزمایشی", Desc = "صرفاً نمونه" },
        new() { Id = 2, RowNo = 2, Name = "واحد کنترل کیفیت" }
    };
    var design = (bool)Call(renderer, "UseDesignLayout", company)!
        ? Call(renderer, "ReadDesignLayout", company, isA5) : null;
    Check(design == null, $"{scenario}/{size}: actual Aria production body path is exercised");
    var content = (byte[])Call(renderer, "BuildContentPdf", letter, signers, roles, roles,
        attached ? 2 : 0, isA5, signersAndCopies, copies, true, company, margin, design, false, true)!;
    // Same continuation-header calculation used by GeneratePdfCoreAsync with no full design layout.
    var homepa = typeof(OutgoingLetterPrintService).Assembly.GetType("Inventory.Api.Services.Office.Outgoing.HomepaLetterheadLayout")!;
    var contentTop = (float)homepa.GetMethod("ContentTop", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { isA5 })!;
    var band = Math.Max(contentTop, isA5 ? 150f : 160f);
    var pdf = (byte[])Call(renderer, "OverlayOnLetterhead", content, background, letter.Id, band)!;
    using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
    Check(longBody ? doc.PageCount > 1 : doc.PageCount == 1, $"{scenario}/{size}: correct single/multi-page output");
    var expectedPaper = isA5 ? PageSizes.A5 : PageSizes.A4;
    Check(doc.Pages.Cast<PdfSharp.Pdf.PdfPage>().All(p => Math.Abs(p.Width.Point - expectedPaper.Width) < 0.01
        && Math.Abs(p.Height.Point - expectedPaper.Height) < 0.01), $"{scenario}/{size}: exact production A4/A5 dimensions on every page");
    var name = $"{company}-{size}-{scenario}.pdf";
    File.WriteAllBytes(Path.Combine(output, name), pdf);
    var actualFields = ReadFields(renderer, template, isA5, fallback);
    var fields = new List<object>();
    foreach (var (property, value) in new[] { ("Date", "۱۴۰۵/۰۸/۳۰"), ("Number", ToFa(number)), ("Attachment", attached ? "دارد" : "ندارد") })
    {
        var field = Get<object>(actualFields, property);
        fields.Add(new { field = property, value, left = Get<float>(field, "Left"), top = Get<float>(field, "Top"),
            width = Get<float>(field, "Width"), height = Get<float>(field, "Height") });
    }
    manifest.Add(new { file = name, company, size, scenario, pages = doc.PageCount, signersAndCopies, longBody, fields });
}

foreach (var size in new[] { "A4", "A5" })
{
    Render(service, 1005, size, "no-attachment");
    Render(service, 1005, size, "attached-no-margin", attached: true, margin: false);
    Render(service, 1005, size, "long-number", number: "1405/123456789012345678901234567890", attached: true);
    Render(service, 1005, size, "multipage", longBody: true);
    Render(service, 1005, size, "copies-and-signers", attached: true, signersAndCopies: true);

    var isA5 = size == "A5";
    var blank = Document.Create(container => container.Page(page =>
    {
        page.Size(isA5 ? PageSizes.A5 : PageSizes.A4);
        page.PageColor(Colors.Transparent);
        page.Content().Height(1);
    })).GeneratePdf();
    var background = (string)Call(service, "ExtractTemplateLetterhead", 1005, isA5)!;
    var reference = (byte[])Call(service, "OverlayOnLetterhead", blank, background, 0, null)!;
    File.WriteAllBytes(Path.Combine(output, $"1005-{size}-art-only.pdf"), reference);
}

var mapping = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{ ["Reporting:OutgoingLetters:CompanyTemplateMap:501"] = "1005" }).Build();
var mapped = Service(mapping);
Check(TemplatePreferred(mapped, 501), "Company-template mapping also selects the new Aria artwork");
foreach (var size in new[] { "A4", "A5" }) Render(mapped, 501, size, "mapped-template", attached: true);

// Temporary customized templates are always removed: do not retain the inherited connection dictionary.
var customRoot = Path.Combine(output, "custom-templates");
Directory.CreateDirectory(Path.Combine(customRoot, "1005"));
try
{
    foreach (var size in new[] { "A4", "A5" })
        File.Copy(Path.Combine(apiRoot, "Reports/OutgoingLetters/Companies/1005", $"Outgoing-{size}.mrt"),
            Path.Combine(customRoot, "1005", $"Outgoing-{size}.mrt"), true);
    var customConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Reporting:Stimulsoft:TemplateRoot"] = customRoot }).Build();
    var custom = Service(customConfig);
    var customA5 = Path.Combine(customRoot, "1005", "Outgoing-A5.mrt");
    var stampBefore = (string)Call(custom, "PrintTemplateCacheStamp", 1005, "A5")!;
    var cachedArtBefore = (string)Call(custom, "ExtractTemplateLetterhead", 1005, true)!;
    var xml = XDocument.Load(customA5);
    var rectangle = xml.Descendants("Text1").First().Element("ClientRectangle")!;
    var parts = rectangle.Value.Split(',');
    parts[0] = (double.Parse(parts[0], CultureInfo.InvariantCulture) - 2d / 72).ToString("F6", CultureInfo.InvariantCulture);
    rectangle.Value = string.Join(',', parts);
    xml.Save(customA5); File.SetLastWriteTimeUtc(customA5, DateTime.UtcNow.AddSeconds(5));
    Check(stampBefore != (string)Call(custom, "PrintTemplateCacheStamp", 1005, "A5")!, "MRT edits invalidate the finished-PDF cache stamp");
    Check(cachedArtBefore != (string)Call(custom, "ExtractTemplateLetterhead", 1005, true)!, "MRT edits select a newly versioned extracted-asset path");
    Render(custom, 1005, "A5", "edited-mrt");
    foreach (var size in new[] { "A4", "A5" })
    {
        var path = Path.Combine(customRoot, "1005", $"Outgoing-{size}.mrt");
        File.Copy(Path.Combine(apiRoot, "Reports/OutgoingLetters/Companies/1005", $"Outgoing-{size}.mrt"), path, true);
        xml = XDocument.Load(path);
        foreach (var key in new[] { "Text1", "Text2", "Text3" }) xml.Descendants(key).First().Element("Text")!.Remove();
        xml.Save(path); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(10));
        var fallback = Service(customConfig);
        Check(Call(fallback, "ReadDeclaredMetaFields", path) == null, $"{size}: missing declared fields exercises calibrated fallback");
        Render(fallback, 1005, size, "fallback-geometry", fallback: true);
    }
}
finally
{
    if (Directory.Exists(customRoot)) Directory.Delete(customRoot, true);
}
File.WriteAllText(Path.Combine(output, "render-manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"ALL {checks} ARIA RENDER/RESOLVER CHECKS PASSED across {manifest.Count} letters plus 2 artwork-only references.");

static string ToFa(string value) => string.Concat(value.Select(c => c is >= '0' and <= '9' ? (char)('۰' + c - '0') : c));
sealed class TestEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "AriaLetterheadTests";
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
