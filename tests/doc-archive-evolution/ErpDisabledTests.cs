using ClosedXML.Excel;
using Inventory.Api.Controllers.DocArchive;
using Inventory.Api.Services;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.IO.Compression;
using System.Text.Json;
using Xunit;

namespace Inventory.DocArchiveEvolution.Tests;

public partial class EvolutionTests
{
    private static async Task SeedHistoricalErp(Fixture f)
    {
        f.Db.Documents.AddRange(f.Doc(1), f.Doc(2));
        f.Db.DocEntityLinks.Add(new() { Id = 17, DocumentId = 1, Module = "Projects", EntityId = 77,
            EntityCode = "LEGACY-ERP-CODE", EntityTitle = "LEGACY-ERP-TITLE", Note = "Keep historical link", CreatedByUserId = 1 });
        f.Db.DocumentLinks.Add(new() { DocumentId = 1, LinkedDocumentId = 2, CreatedByUserId = 1 });
        await f.Db.SaveChangesAsync();
    }

    private static void AssertRetired(IActionResult result)
    {
        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(410, response.StatusCode);
        Assert.Equal("DOC_ARCHIVE_ERP_DISABLED", JsonSerializer.SerializeToElement(response.Value).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Operator")]
    [InlineData("Viewer")]
    public async Task All_five_ERP_actions_are_retired_and_preserve_rows(string role)
    {
        using var f = new Fixture(); await SeedHistoricalErp(f);
        var before = JsonSerializer.Serialize(await f.Db.DocEntityLinks.AsNoTracking().SingleAsync());
        f.Db.ChangeTracker.Clear();
        var controller = new DocEntityLinksController(f.Db); f.As(controller, 1, role);
        AssertRetired(await controller.GetLinkedDocuments("Projects", 77));
        AssertRetired(await controller.AddLink(new() { DocumentId = 2, Module = "Projects", EntityId = 88, EntityTitle = "New link" }));
        AssertRetired(await controller.RemoveLink(17));
        AssertRetired(await controller.QuickCreateLinked(new() { FolderId = 1, Title = "Must not be created", Module = "Projects", EntityId = 88 }));
        AssertRetired(await controller.SearchEntities("Projects", "LEGACY"));
        Assert.Empty(f.Db.ChangeTracker.Entries());
        Assert.Equal(before, JsonSerializer.Serialize(await f.Db.DocEntityLinks.AsNoTracking().SingleAsync()));
        Assert.Equal(2, await f.Db.Documents.CountAsync());
        Assert.Single(await f.Db.DocumentLinks.ToListAsync());
        Assert.Empty(await f.Db.DocumentLogs.ToListAsync());
    }

    [Fact]
    public async Task Retired_actions_do_not_even_need_a_database_connection()
    {
        using var f = new Fixture(); var controller = new DocEntityLinksController(f.Db);
        // Any query after this point would fail; tombstones must never consult ERP tables.
        await f.Db.DisposeAsync();
        AssertRetired(await controller.GetLinkedDocuments("UnknownModule", -1));
        AssertRetired(await controller.AddLink(new()));
        AssertRetired(await controller.RemoveLink(-1));
        AssertRetired(await controller.QuickCreateLinked(new()));
        AssertRetired(await controller.SearchEntities("UnknownModule"));
    }

    [Theory]
    [InlineData(false, "Projects", null)]
    [InlineData(true, "Projects", null)]
    [InlineData(false, null, 77)]
    [InlineData(true, null, 77)]
    [InlineData(false, "Projects", 77)]
    [InlineData(true, "Projects", 77)]
    public async Task Both_search_routes_explicitly_reject_retired_filters(bool paged, string? module, int? entityId)
    {
        using var f = new Fixture(); await SeedHistoricalErp(f);
        var filter = new DocSearchFilterDto { LinkedModule = module, LinkedEntityId = entityId };
        AssertRetired(paged ? await f.Search(1).SearchPage(filter) : await f.Search(1).AdvancedSearch(filter));
        Assert.Single(await f.Db.DocEntityLinks.ToListAsync());
    }

    [Fact]
    public async Task General_document_apis_hide_ERP_metadata_but_preserve_document_links()
    {
        using var f = new Fixture(); await SeedHistoricalErp(f);
        var controller = new DocumentsController(f.Db, f.Access, null!, null!, f.Confirm, null!);
        f.As(controller, 1, "Admin");
        var list = Result<List<DocumentListDto>>(await controller.List());
        var search = Result<List<DocumentListDto>>(await f.Search(1).AdvancedSearch(new()));
        var page = Result<DocSearchPageDto>(await f.Search(1).SearchPage(new()));
        foreach (var d in list.Concat(search).Concat(page.Items))
        {
            Assert.Equal(0, d.EntityLinkCount); Assert.Empty(d.LinkedModules);
        }
        var detail = Result<DocumentDto>(await controller.Get(1));
        Assert.Empty(detail.EntityLinks);
        Assert.Equal(2, Assert.Single(detail.Links).LinkedDocumentId);
        Assert.Single(await f.Db.DocEntityLinks.ToListAsync());
    }

    [Fact]
    public async Task ZIP_manifest_has_ten_columns_without_ERP_and_keeps_history()
    {
        using var f = new Fixture(); await SeedHistoricalErp(f);
        var env = new ErpTestEnvironment();
        try
        {
            var service = new DocFolderZipService(f.Db, f.Access, new FileStore(env));
            var result = await service.ExportZipAsync(1, true, true, true, 1, true);
            using var zip = new ZipArchive(new MemoryStream(result.ZipBytes));
            var entry = Assert.Single(zip.Entries, e => e.Name.EndsWith(".xlsx"));
            using var stream = new MemoryStream(); using (var input = entry.Open()) await input.CopyToAsync(stream); stream.Position = 0;
            using var workbook = new XLWorkbook(stream); var sheet = workbook.Worksheet(1);
            Assert.Equal(10, sheet.LastColumnUsed()!.ColumnNumber());
            Assert.Equal("تعداد پیوست", sheet.Cell(4, 10).GetString());
            Assert.Equal(0, sheet.Cell(5, 10).GetValue<int>());
            foreach (var cell in sheet.CellsUsed())
            {
                Assert.DoesNotContain("ERP", cell.GetString()); Assert.DoesNotContain("LEGACY-", cell.GetString());
            }
            foreach (var text in zip.Entries.Where(e => e.Name.EndsWith(".txt")))
            {
                using var reader = new StreamReader(text.Open()); Assert.DoesNotContain("ERP", await reader.ReadToEndAsync());
            }
            Assert.Single(await f.Db.DocEntityLinks.ToListAsync());
        }
        finally { if (Directory.Exists(env.ContentRootPath)) Directory.Delete(env.ContentRootPath, true); }
    }

    private sealed class ErpTestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "archive-erp-" + Guid.NewGuid());
        public string WebRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
