using Inventory.Api.Controllers.DocArchive;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit;

namespace Inventory.DocArchiveEvolution.Tests;

public partial class EvolutionTests
{
    [Fact]
    public async Task Actual_HTTP_routes_return_Gone_not_SPA_HTML_and_never_mutate_history()
    {
        using var f = new Fixture(); await SeedHistoricalErp(f);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(f.Db);
        builder.Services.AddAuthentication("erp-test").AddScheme<AuthenticationSchemeOptions, ErpTestAuthentication>("erp-test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(DocEntityLinksController).Assembly);
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        app.MapFallback(() => "SPA fallback must not handle retired routes");
        app.Urls.Add("http://127.0.0.1:0"); await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var routes = new[] {
                (HttpMethod.Get, "/api/doc-archive/entity-links/Projects/77"),
                (HttpMethod.Post, "/api/doc-archive/entity-links"),
                (HttpMethod.Delete, "/api/doc-archive/entity-links/17"),
                (HttpMethod.Post, "/api/doc-archive/entity-links/quick-create"),
                (HttpMethod.Get, "/api/doc-archive/entity-lookups/Projects?q=legacy")
            };
            foreach (var (method, path) in routes)
            {
                using var request = new HttpRequestMessage(method, path);
                if (method == HttpMethod.Post) request.Content = JsonContent.Create(new { documentId = 1, module = "Projects", entityId = 88, entityTitle = "New", folderId = 1, title = "New document" });
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
                Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("DOC_ARCHIVE_ERP_DISABLED", json.GetProperty("code").GetString());
            }
            Assert.Single(f.Db.DocEntityLinks);
            Assert.Equal(2, f.Db.Documents.Count());
            Assert.Single(f.Db.DocumentLinks);
        }
        finally { await app.StopAsync(); }
    }

    private sealed class ErpTestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "Admin") }, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(user, Scheme.Name)));
        }
    }
}
