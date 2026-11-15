using Inventory.Api.Services.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
void Reject(string value) { try { CompanyLogoImage.Normalize(value); } catch (ArgumentException) { return; } throw new Exception("Expected invalid logo rejection"); }
if (CompanyLogoImage.Normalize(null) != null) throw new Exception("Clearing logo failed");
using var image = new Image<Rgba32>(2, 2);
using var png = new MemoryStream(); image.SaveAsPng(png);
var value = CompanyLogoImage.Normalize("data:image/png;base64," + Convert.ToBase64String(png.ToArray()));
if (value?.StartsWith("data:image/png;base64,") != true) throw new Exception("PNG round trip failed");
using var jpeg = new MemoryStream(); image.SaveAsJpeg(jpeg);
if (CompanyLogoImage.Normalize("data:image/jpeg;base64," + Convert.ToBase64String(jpeg.ToArray()))?.StartsWith("data:image/png;base64,") != true) throw new Exception("JPEG normalization failed");
Reject("https://example.org/logo.png");Reject("data:image/svg+xml;base64,PHN2Zz4=");Reject("data:image/png;base64,invalid");Reject("data:image/png;base64," + Convert.ToBase64String(new byte[]{1,2,3}));
Reject("data:image/png;base64,"+new string('A',3*1024*1024));
using var large = new Image<Rgba32>(2049, 1);using var data = new MemoryStream();large.SaveAsPng(data);Reject("data:image/png;base64,"+Convert.ToBase64String(data.ToArray()));
Console.WriteLine("PASS PNG/JPEG, clear, malformed/unsupported/oversized logos and dimensions");
