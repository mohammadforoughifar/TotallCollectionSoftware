using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Inventory.Chat.Tests;

public sealed class TestChatEnvironment : IWebHostEnvironment, IDisposable
{
    public string ApplicationName { get; set; } = "Inventory.Api";
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "inventory-chat-test-" + Guid.NewGuid().ToString("N"));
    public string WebRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public TestChatEnvironment() { WebRootPath = Path.Combine(ContentRootPath, "wwwroot"); }
    public void Dispose() { if (Directory.Exists(ContentRootPath)) Directory.Delete(ContentRootPath, true); }
}
