using System.Reflection;
using System.Text;
using Inventory.Api.Controllers;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Office.Outgoing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using PdfSharp.Pdf.IO;
using User = Inventory.Api.Data.User;

// Integration probe for the real upload/store/read/PDF path; it creates no HTTP host or external DB.
var tempRoot = Path.Combine(Path.GetTempPath(), "user-signature-alpha-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
    await using var db = new AppDbContext(options);
    await db.Database.EnsureCreatedAsync();
    var user = new User
    {
        Id = 74001, Username = "signature-alpha-test", PasswordHash = "test", Role = "Admin",
        FirstName = "آزمایش", LastName = "شفافیت", IsActive = true
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();

    var environment = new TestEnvironment
    {
        ContentRootPath = tempRoot,
        WebRootPath = Path.Combine(tempRoot, "wwwroot")
    };
    var store = new FileStore(environment);
    var photos = new UserPhotoService(store, NullLogger<UserPhotoService>.Instance);
    var controller = new AuthController(null!, db, store, photos);

    // RGBA fixture with both a pre-transparent corner and a white opaque background around dark ink.
    using var sourceImage = new Image<Rgba32>(12, 8);
    for (var y = 0; y < sourceImage.Height; y++)
    for (var x = 0; x < sourceImage.Width; x++)
        sourceImage[x, y] = new Rgba32(255, 255, 255, 255);
    sourceImage[0, 0] = new Rgba32(0, 0, 0, 0);
    for (var x = 3; x <= 8; x++) sourceImage[x, 4] = new Rgba32(15, 25, 45, 255);
    sourceImage[4, 3] = new Rgba32(40, 50, 65, 160); // antialiased ink edge

    await using var input = new MemoryStream();
    await sourceImage.SaveAsync(input, new PngEncoder { ColorType = PngColorType.RgbWithAlpha });
    var inputBytes = input.ToArray();
    using var formStream = new MemoryStream(inputBytes, writable: false);
    var formFile = new FormFile(formStream, 0, inputBytes.Length, "file", "signature.png")
    {
        Headers = new HeaderDictionary()
    };

    var response = await controller.UploadUserSignature(user.Id, formFile);
    Check(response is OkObjectResult, "PNG signature upload returns success");

    var savedUser = await db.Users.SingleAsync(x => x.Id == user.Id);
    Check(savedUser.SignaturePath?.Contains($"UserSignatures/{user.Id}/", StringComparison.OrdinalIgnoreCase) == true,
        "signature is stored under the user-ID folder");
    Check(Path.GetFileName(savedUser.SignaturePath).Contains(user.Id.ToString(), StringComparison.Ordinal),
        "signature filename is based on the user ID");
    var initialSignaturePath = savedUser.SignaturePath;
    using (var replacementStream = new MemoryStream(inputBytes, writable: false))
    {
        var replacementFile = new FormFile(replacementStream, 0, inputBytes.Length, "file", "signature.png")
        {
            Headers = new HeaderDictionary()
        };
        var replacement = await controller.UploadUserSignature(user.Id, replacementFile);
        Check(replacement is OkObjectResult, "a replacement signature upload succeeds");
    }
    savedUser = await db.Users.SingleAsync(x => x.Id == user.Id);
    Check(savedUser.SignaturePath == initialSignaturePath, "re-upload replaces the signature at the same user-ID filename");
    var signatureDirectory = Path.Combine(store.RootPath, "UserSignatures", user.Id.ToString());
    Check(Directory.GetFiles(signatureDirectory).Length == 1, "re-upload leaves only one signature file for the user");
    var storedBytes = store.ReadBytes(savedUser.SignaturePath)
        ?? throw new Exception("FAIL: stored signature bytes are missing");
    Check(storedBytes.Length > 25, "stored signature bytes are readable");
    Check(storedBytes[0] == 0x89 && storedBytes[1] == 0x50 && storedBytes[2] == 0x4E && storedBytes[3] == 0x47,
        "stored file has PNG magic bytes");
    Check(storedBytes[25] == 6, "stored PNG IHDR color type is RGBA (6), not RGB/indexed");

    using var storedImage = Image.Load<Rgba32>(storedBytes);
    Check(storedImage[0, 0].A == 0, "transparent source corner remains alpha zero after upload/save");
    Check(storedImage[1, 1].A == 0, "white background is removed to alpha zero");
    Check(storedImage[5, 4].A >= 240, "signature ink stays opaque after background removal");
    Check(storedImage[4, 3].A is > 0 and < 200, "antialiased signature alpha is preserved");

    // Exercise the production outgoing-letter PDF content builder using the saved signature.
    var printService = new OutgoingLetterPrintService(db, environment, NullLogger<OutgoingLetterPrintService>.Instance);
    var signer = new OutgoingLetterSigner { UserId = user.Id, Order = 1, IsSigned = true, User = savedUser };
    var build = typeof(OutgoingLetterPrintService).GetMethod("BuildContentPdf", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var pdf = (byte[])build.Invoke(printService, new object?[]
    {
        new OutgoingLetter
        {
            Id = 1, LetterNumber = "1405/1", SadereNumber = "1405/1", DateSabt = DateTime.Now,
            Title = "آزمون امضای شفاف", ReceiverOrganization = "گیرنده", Text = "بررسی PDF"
        },
        new List<OutgoingLetterSigner> { signer },
        new Dictionary<int, string>(), new Dictionary<int, string>(),
        false, false, true, new List<OutgoingLetterCopyTo>(), false, null, true
    })!;
    using var pdfDocument = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
    Check(pdfDocument.PageCount == 1, "the production PDF renderer creates a page with the signature");
    var pdfText = Encoding.Latin1.GetString(pdf);
    Check(!pdfText.Contains("/Subtype /Image", StringComparison.Ordinal),
        "signature is emitted as ink-only vector paths, not a raster XObject that can lose its alpha mask");

    // Run the same PDFsharp page-overlay stage used for company letterheads.
    var backgroundPath = Path.Combine(tempRoot, "blank-letterhead.pdf");
    using (var background = new PdfSharp.Pdf.PdfDocument())
    {
        background.AddPage();
        background.Save(backgroundPath);
    }
    var overlay = typeof(OutgoingLetterPrintService).GetMethod("OverlayOnLetterhead", BindingFlags.Static | BindingFlags.NonPublic)!;
    var finalPdf = (byte[])overlay.Invoke(null, new object[] { pdf, backgroundPath })!;
    using var finalDocument = PdfReader.Open(new MemoryStream(finalPdf), PdfDocumentOpenMode.Import);
    Check(finalDocument.PageCount == 1, "PDFsharp letterhead overlay retains the printed page");
    Check(!Encoding.Latin1.GetString(finalPdf).Contains("/Subtype /Image", StringComparison.Ordinal),
        "letterhead overlay does not reintroduce a raster signature/alpha mask");

    Console.WriteLine("User signature RGBA upload and PDF transparency probes passed.");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

sealed class TestEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "UserSignatureTransparencyTests";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
