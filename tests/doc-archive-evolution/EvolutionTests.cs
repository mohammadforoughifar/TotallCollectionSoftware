using Inventory.Api.Data;
using Inventory.Api.Controllers.DocArchive;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using Xunit;

namespace Inventory.DocArchiveEvolution.Tests;
public partial class EvolutionTests
{
    private sealed class Fixture : IDisposable
    {
        public SqliteConnection Connection = new("Data Source=:memory:");
        public AppDbContext Db;
        public DocAccessService Access;
        public DocDownloadConfirmService Confirm = new(new MemoryCache(new MemoryCacheOptions()));
        public DocRenewalService Renewal;
        public Fixture()
        {
            Connection.Open();Db=new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);
            Db.Database.EnsureCreated();Access=new(Db);Renewal=new(Db,Access);
            Db.Users.AddRange(new User{Id=1,Username="owner",Role="Admin",IsActive=true},new User{Id=2,Username="reader",Role="Viewer",IsActive=true},new User{Id=3,Username="outsider",Role="Viewer",IsActive=true});
            Db.DocFolders.Add(new DocFolder{Id=1,Name="test",CreatedByUserId=1});Db.SaveChanges();
        }
        public ArchiveDocument Doc(int id,string title="سند",DateTime? expiry=null) => new(){Id=id,Code="DOC-"+id,Title=title,FolderId=1,CreatedByUserId=1,CreatedByName="owner",CreatedAt=new DateTime(2026,9,10),IsActive=true,ExpireDate=expiry};
        public void As(ControllerBase c,int user,string role="Viewer") => c.ControllerContext=new(){HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user.ToString()),new Claim(ClaimTypes.Name,"u"+user),new Claim(ClaimTypes.Role,role)},"test"))}};
        public DocSearchController Search(int user=2){var c=new DocSearchController(Db,Access,new FakeIndex(),new Extractor(),Confirm);As(c,user,user==1?"Admin":"Viewer");return c;}
        public DocEvolutionController Controller(int user=1){var c=new DocEvolutionController(Db,Access,Renewal);As(c,user,user==1?"Admin":"Viewer");return c;}
        public void Dispose(){Db.Dispose();Connection.Dispose();}
    }
    private sealed class FakeIndex : IDocIndexService
    {
        public Task QueueAttachmentIndexingAsync(int id)=>Task.CompletedTask;
        public Task<DocExtractionResult> IndexAttachmentAsync(int id,bool force=false)=>throw new NotImplementedException();
        public Task<int> IndexVersionAsync(int id,bool force=false)=>throw new NotImplementedException();
        public Task<int> IndexDocumentAsync(int id,bool force=false)=>throw new NotImplementedException();
        public Task<DocReindexResultDto> ReindexAllAsync()=>throw new NotImplementedException();
    }
    private sealed class Extractor : IDocTextExtractorService
    {
        public Task<DocExtractionResult> ExtractAsync(byte[] data,string name,string? type)=>throw new NotImplementedException();
        public string Normalize(string? s)=>s?.ToLowerInvariant()??"";
        public List<string> Tokenize(string? s)=>Normalize(s).Split(' ',StringSplitOptions.RemoveEmptyEntries).ToList();
        public string? MakeSnippet(string s,string q,int maxContext=100)=>s;
    }
    private static T Result<T>(IActionResult r)=>Assert.IsType<T>(Assert.IsType<OkObjectResult>(r).Value);
    [Fact]
    public async Task Pagination_applies_access_before_limit_and_reports_total()
    {
        using var f=new Fixture();f.Db.Documents.AddRange(Enumerable.Range(1,350).Select(i=>f.Doc(i)));
        f.Db.DocumentPermissions.Add(new(){DocumentId=1,UserId=2,Level=DocAccessLevel.Read});await f.Db.SaveChangesAsync();
        var page=Result<DocSearchPageDto>(await f.Search().SearchPage(new(){Page=1,PageSize=25}));
        Assert.Equal(1,page.Total);Assert.Equal(1,Assert.Single(page.Items).Id);
        var admin=Result<DocSearchPageDto>(await f.Search(1).SearchPage(new(){Page=2,PageSize=25}));
        Assert.Equal(350,admin.Total);Assert.Equal(25,admin.Items.Count);Assert.Equal(325,admin.Items[0].Id);
        var clamped=Result<DocSearchPageDto>(await f.Search(1).SearchPage(new(){Page=999,PageSize=25}));Assert.Equal(14,clamped.Page);
    }
    [Fact]
    public async Task Date_tag_attachment_author_and_expiry_filters_execute_in_sql()
    {
        using var f=new Fixture();var doc=f.Doc(1,expiry:new DateTime(2026,10,10));f.Db.Documents.AddRange(doc,f.Doc(2,expiry:new DateTime(2027,1,1)));
        f.Db.DocTags.Add(new(){Id=1,Name="tag"});f.Db.DocumentTags.Add(new(){DocumentId=1,TagId=1});await f.Db.SaveChangesAsync();
        var filter=new DocSearchFilterDto{CreatedFrom=new(2026,9,10),CreatedTo=new(2026,9,10),ExpiryFrom=new(2026,10,1),ExpiryTo=new(2026,10,31),TagIds=new(){1},HasAttachment=false,CreatedByUserId=1};
        Assert.Equal(1,Assert.Single(Result<DocSearchPageDto>(await f.Search(1).SearchPage(filter)).Items).Id);
        filter.CreatedTo=new(2026,9,9);Assert.IsType<BadRequestObjectResult>(await f.Search(1).SearchPage(filter));
        filter.CreatedTo=new(2026,9,10);filter.TagIds=new(){999};Assert.Empty(Result<DocSearchPageDto>(await f.Search(1).SearchPage(filter)).Items);
    }
    [Fact]
    public async Task Content_search_never_leaks_to_view_only_or_unconfirmed_users()
    {
        using var f=new Fixture();var doc=f.Doc(1);doc.RequireDownloadConfirm=true;f.Db.Documents.Add(doc);
        f.Db.DocumentPermissions.Add(new(){DocumentId=1,UserId=2,Level=DocAccessLevel.View});
        f.Db.DocExtractedTexts.Add(new(){DocumentId=1,AttachmentId=1,VersionId=1,FileName="a.txt",Status="Indexed",ExtractedText="secret content",NormalizedText="secret content"});await f.Db.SaveChangesAsync();
        var filter=new DocSearchFilterDto{ContentSearch="secret"};Assert.Empty(Result<DocSearchPageDto>(await f.Search().SearchPage(filter)).Items);
        var p=await f.Db.DocumentPermissions.SingleAsync();p.Level=DocAccessLevel.Read;await f.Db.SaveChangesAsync();
        Assert.Empty(Result<DocSearchPageDto>(await f.Search().SearchPage(filter)).Items);
        f.Confirm.Confirm(2,1);var page=Result<DocSearchPageDto>(await f.Search().SearchPage(filter));Assert.Contains("secret",Assert.Single(page.Items).ContentSnippet);
    }
    [Fact]
    public async Task Temporary_access_expires_revokes_and_preserves_permanent_grants()
    {
        using var f=new Fixture();f.Db.Documents.Add(f.Doc(1));await f.Db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await f.Controller(2).Grant(1,new(){UserId=3,ExpiresAtUtc=DateTime.UtcNow.AddHours(1)}));
        Assert.IsType<BadRequestObjectResult>(await f.Controller().Grant(1,new(){UserId=2,ExpiresAtUtc=DateTime.UtcNow.AddHours(-1)}));
        Assert.IsType<OkObjectResult>(await f.Controller().Grant(1,new(){UserId=2,ExpiresAtUtc=DateTime.UtcNow.AddHours(1),CanDownload=false}));
        Assert.True(await DocArchiveAccessProbe.AnyAsync(f.Db,2));Assert.Equal((DocAccessLevel.Read,false),await f.Access.DocumentAccessAsync(2,false,1));
        Assert.Single(Result<DocSearchPageDto>(await f.Search().SearchPage(new())).Items);
        Assert.Single(Result<List<DocTemporaryGrantDto>>(await f.Controller().Grants(1)));
        Assert.IsType<ForbidResult>(await f.Controller(2).Grants(1));
        var grant=await f.Db.DocTemporaryGrants.SingleAsync();grant.ExpiresAtUtc=DateTime.UtcNow.AddSeconds(-1);await f.Db.SaveChangesAsync();
        Assert.Equal((DocAccessLevel.None,false),await f.Access.DocumentAccessAsync(2,false,1));Assert.False(await DocArchiveAccessProbe.AnyAsync(f.Db,2));
        grant.ExpiresAtUtc=DateTime.UtcNow.AddHours(1);await f.Db.SaveChangesAsync();
        f.Db.DocumentPermissions.Add(new(){DocumentId=1,UserId=2,Level=DocAccessLevel.Write,CanDownload=true});await f.Db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await f.Controller().Revoke(1,grant.Id));Assert.Equal((DocAccessLevel.Write,true),await f.Access.DocumentAccessAsync(2,false,1));
        Assert.Equal((DocAccessLevel.None,false),await f.Access.DocumentAccessAsync(3,false,1));
    }
    [Fact]
    public async Task Renewal_is_opt_in_atomic_per_expiry_and_does_not_recreate_deleted_orders()
    {
        using var f=new Fixture();var now=new DateTime(2026,9,14,10,0,0);f.Db.Documents.Add(f.Doc(1,expiry:now.AddDays(10)));
        f.Db.DocumentPermissions.Add(new(){DocumentId=1,UserId=2,Level=DocAccessLevel.Read});await f.Db.SaveChangesAsync();
        Assert.Null(await f.Renewal.RunForDocumentAsync(1,now));
        Assert.IsType<OkObjectResult>(await f.Controller().SetRenewal(1,new(){Enabled=true,AssigneeUserId=2,LeadDays=30}));
        var id=await f.Renewal.RunForDocumentAsync(1,now);Assert.NotNull(id);Assert.Null(await f.Renewal.RunForDocumentAsync(1,now));
        var order=await f.Db.WorkOrders.SingleAsync();Assert.Equal("Document",order.SourceModule);Assert.Equal(2,(await f.Db.WorkOrderAssignees.SingleAsync()).UserId);
        order.DeletedAt=now;await f.Db.SaveChangesAsync();Assert.Null(await f.Renewal.RunForDocumentAsync(1,now));
        (await f.Db.Documents.SingleAsync()).ExpireDate=now.AddDays(20);await f.Db.SaveChangesAsync();Assert.NotNull(await f.Renewal.RunForDocumentAsync(1,now));
        Assert.Equal(2,await f.Db.WorkOrders.IgnoreQueryFilters().CountAsync());Assert.Equal(2,await f.Db.DocRenewalRuns.CountAsync());
    }
    [Fact]
    public async Task Renewal_rechecks_permissions_disabled_flags_and_assignee_access()
    {
        using var f=new Fixture();var now=DateTime.Now;f.Db.Documents.Add(f.Doc(1,expiry:now.AddDays(3)));await f.Db.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>(await f.Controller().SetRenewal(1,new(){Enabled=true,AssigneeUserId=2,LeadDays=30}));
        f.Db.DocumentPermissions.Add(new(){DocumentId=1,UserId=2,Level=DocAccessLevel.Read});await f.Db.SaveChangesAsync();await f.Controller().SetRenewal(1,new(){Enabled=true,AssigneeUserId=2,LeadDays=30});
        f.Db.DocumentPermissions.RemoveRange(f.Db.DocumentPermissions);await f.Db.SaveChangesAsync();Assert.Null(await f.Renewal.RunForDocumentAsync(1,now));
        Assert.False(await f.Renewal.CanAssignAsync(2,3));
        await f.Controller().SetRenewal(1,new(){Enabled=false,AssigneeUserId=2,LeadDays=30});Assert.Null(await f.Renewal.RunForDocumentAsync(1,now));Assert.Empty(await f.Db.WorkOrders.ToListAsync());
    }
    [Fact]
    public async Task Queue_claim_retry_lease_recovery_and_generation_fencing()
    {
        using var f=new Fixture();await DocIndexQueue.EnqueueAsync(f.Db,123);await DocIndexQueue.EnqueueAsync(f.Db,123);
        var now=DateTime.UtcNow;var claim=await DocIndexQueue.ClaimAsync(f.Db,now);Assert.NotNull(claim);Assert.Null(await DocIndexQueue.ClaimAsync(f.Db,now));
        await DocIndexQueue.EnqueueAsync(f.Db,123);await DocIndexQueue.CompleteAsync(f.Db,claim!,true,null);
        f.Db.ChangeTracker.Clear();Assert.Equal("Pending",(await f.Db.DocIndexJobs.SingleAsync()).Status);
        var retry=await DocIndexQueue.ClaimAsync(f.Db,now.AddHours(1));Assert.NotNull(retry);
        var recovered=await DocIndexQueue.ClaimAsync(f.Db,now.AddHours(2));Assert.NotNull(recovered);Assert.NotEqual(retry!.LeaseToken,recovered!.LeaseToken);
        await DocIndexQueue.CompleteAsync(f.Db,retry,true,null);f.Db.ChangeTracker.Clear();Assert.Equal("Working",(await f.Db.DocIndexJobs.SingleAsync()).Status);
        await DocIndexQueue.CompleteAsync(f.Db,recovered,true,null);f.Db.ChangeTracker.Clear();Assert.Equal("Done",(await f.Db.DocIndexJobs.SingleAsync()).Status);
    }
    [Fact]
    public async Task File_guards_honor_expiry_revocation_and_download_separately()
    {
        using var f=new Fixture();f.Db.Documents.Add(f.Doc(1));f.Db.DocumentVersions.Add(new(){Id=11,DocumentId=1,VersionNo=1});
        f.Db.DocTemporaryGrants.Add(new(){DocumentId=1,UserId=2,ExpiresAtUtc=DateTime.UtcNow.AddHours(1)});await f.Db.SaveChangesAsync();
        var guard=new Inventory.Api.Services.AttachmentGuard(f.Db,f.Access);
        Assert.Equal(Inventory.Api.Services.AttachmentAccess.PreviewOnly,await guard.CheckAsync("DocVersion",11,2,false));
        var grant=await f.Db.DocTemporaryGrants.SingleAsync();grant.CanDownload=true;await f.Db.SaveChangesAsync();
        Assert.Equal(Inventory.Api.Services.AttachmentAccess.Download,await guard.CheckAsync("DocVersion",11,2,false));
        grant.RevokedAtUtc=DateTime.UtcNow;await f.Db.SaveChangesAsync();
        Assert.Equal(Inventory.Api.Services.AttachmentAccess.None,await guard.CheckAsync("DocVersion",11,2,false));
        Assert.Equal(Inventory.Api.Services.AttachmentAccess.None,await guard.CheckAsync("DocVersion",11,3,false));
    }
    [Fact]
    public async Task Queue_failed_jobs_stop_after_five_attempts_and_manual_retry_resets()
    {
        using var f=new Fixture();await DocIndexQueue.EnqueueAsync(f.Db,101);var now=DateTime.UtcNow;
        for(var i=0;i<5;i++){var claim=await DocIndexQueue.ClaimAsync(f.Db,now.AddHours(i+1));Assert.NotNull(claim);await DocIndexQueue.CompleteAsync(f.Db,claim!,false,"failed");}
        f.Db.ChangeTracker.Clear();Assert.Equal("Failed",(await f.Db.DocIndexJobs.SingleAsync()).Status);Assert.Null(await DocIndexQueue.ClaimAsync(f.Db,now.AddDays(1)));
        await DocIndexQueue.EnqueueAsync(f.Db,101);var retry=await DocIndexQueue.ClaimAsync(f.Db,DateTime.UtcNow.AddSeconds(1));Assert.NotNull(retry);Assert.Equal(1,retry!.Attempts);
    }
    [Fact]
    public async Task Content_endpoint_checks_document_version_ownership_and_password()
    {
        using var f=new Fixture();var doc=f.Doc(1);doc.RequireDownloadConfirm=true;f.Db.Documents.AddRange(doc,f.Doc(2));
        f.Db.DocumentVersions.AddRange(new DocumentVersion{Id=11,DocumentId=1,VersionNo=1},new DocumentVersion{Id=12,DocumentId=1,VersionNo=2},new DocumentVersion{Id=21,DocumentId=2,VersionNo=1});
        f.Db.AppAttachments.AddRange(new AppAttachment{Id=101,Module="DocVersion",RefId=11,FileName="same.txt"},new AppAttachment{Id=102,Module="DocVersion",RefId=12,FileName="same.txt"},new AppAttachment{Id=201,Module="DocVersion",RefId=21,FileName="other.txt"});
        f.Db.DocumentPermissions.Add(new(){DocumentId=1,UserId=2,Level=DocAccessLevel.Read});
        f.Db.DocExtractedTexts.AddRange(new DocExtractedText{DocumentId=1,AttachmentId=101,VersionId=11,Status="Indexed",ExtractedText="old",NormalizedText="old"},new DocExtractedText{DocumentId=1,AttachmentId=102,VersionId=12,Status="Indexed",ExtractedText="new",NormalizedText="new"});await f.Db.SaveChangesAsync();
        var controller=new DocContentCompareController(f.Db,f.Access,f.Confirm,null!,new FakeIndex());f.As(controller,2);
        Assert.Equal(403,Assert.IsType<ObjectResult>(await controller.Compare(1,new(){LeftAttachmentId=101,RightAttachmentId=102})).StatusCode);
        Assert.Equal(403,Assert.IsType<ObjectResult>(await f.Search().GetExtractedTexts(1)).StatusCode);
        f.Confirm.Confirm(2,1);
        Assert.IsType<BadRequestObjectResult>(await controller.Compare(1,new(){LeftAttachmentId=101,RightAttachmentId=201}));
        var result=Result<DocContentCompareDto>(await controller.Compare(1,new(){LeftAttachmentId=101,RightAttachmentId=102}));Assert.Contains(result.Rows,r=>r.Kind=="added");
        f.As(controller,3);Assert.IsType<ForbidResult>(await controller.Compare(1,new(){LeftAttachmentId=101,RightAttachmentId=102}));
        f.As(controller,2);(await f.Db.Documents.SingleAsync(d=>d.Id==1)).IsDeleted=true;await f.Db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await controller.Compare(1,new(){LeftAttachmentId=101,RightAttachmentId=102}));
    }
    [Fact]
    public async Task Queue_reconciliation_recovers_an_attachment_committed_without_enqueue()
    {
        using var f=new Fixture();f.Db.Documents.Add(f.Doc(1));f.Db.DocumentVersions.Add(new(){Id=11,DocumentId=1,VersionNo=1});
        f.Db.AppAttachments.Add(new(){Id=101,Module="DocVersion",RefId=11,FileName="file.txt"});await f.Db.SaveChangesAsync();
        await DocIndexQueue.ReconcileAsync(f.Db);await DocIndexQueue.ReconcileAsync(f.Db);Assert.Single(await f.Db.DocIndexJobs.ToListAsync());
    }
    [Theory]
    [InlineData("/uploads/DocVersion/11/file.pdf",true)]
    [InlineData("/uploads/docversion/11/file.pdf",true)]
    [InlineData("/uploads/x/../DocVersion/11/file.pdf",true)]
    [InlineData("/uploads/%44ocVersion/11/file.pdf",true)]
    [InlineData("/uploads/users/avatar.jpg",false)]
    [InlineData("/api/attachments/download/11",false)]
    public void Raw_archive_paths_are_not_public(string path,bool blocked) => Assert.Equal(blocked,DocStaticProtection.IsProtectedPath(path));
    [Fact]
    public async Task Temporary_download_permission_never_allows_mutating_attachments()
    {
        using var f=new Fixture();f.Db.Documents.Add(f.Doc(1));f.Db.DocumentVersions.Add(new(){Id=11,DocumentId=1,VersionNo=1});
        f.Db.DocTemporaryGrants.Add(new(){DocumentId=1,UserId=2,ExpiresAtUtc=DateTime.UtcNow.AddDays(1),CanDownload=true});
        f.Db.AppAttachments.Add(new(){Id=101,Module="DocVersion",RefId=11,UploaderUserId=2,FileName="test.txt"});await f.Db.SaveChangesAsync();
        var c=new Inventory.Api.Controllers.AttachmentsController(f.Db,null!,new Inventory.Api.Services.AttachmentGuard(f.Db,f.Access),new FakeIndex(),f.Confirm,null!);f.As(c,2);
        Assert.Equal(403,Assert.IsType<ObjectResult>(await c.Upload("DocVersion",11,null!)).StatusCode);
        Assert.Equal(403,Assert.IsType<ObjectResult>(await c.Delete(101)).StatusCode);
        Assert.Single(await f.Db.AppAttachments.ToListAsync());
    }
    [Fact]
    public async Task Additive_schema_is_idempotent()
    {
        using var c=new SqliteConnection("Data Source=:memory:");c.Open();using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(c).Options);
        await DocEvolutionSchemaV1.EnsureAsync(db);await db.Database.ExecuteSqlRawAsync("INSERT INTO DocRenewalPolicies VALUES(42,1,2,30,1)");await DocEvolutionSchemaV1.EnsureAsync(db);
        Assert.Equal(42,(await db.DocRenewalPolicies.SingleAsync()).DocumentId);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO DocRenewalRuns(DocumentId,ExpiryDate,WorkOrderId,CreatedAtUtc) VALUES(42,'2026-01-01',1,'2026-01-01')");
        await Assert.ThrowsAsync<SqliteException>(()=>db.Database.ExecuteSqlRawAsync("INSERT INTO DocRenewalRuns(DocumentId,ExpiryDate,WorkOrderId,CreatedAtUtc) VALUES(42,'2026-01-01',2,'2026-01-01')"));
    }
    [Fact]
    public void Text_diff_detects_insertions_deletions_and_does_not_compare_only_names()
    {
        var diff=DocContentDiff.Text("الف\nب\nج","الف\nجدید\nب\nج");Assert.Single(diff.Rows,r=>r.Kind=="added");Assert.DoesNotContain(diff.Rows,r=>r.Kind=="removed");
        Assert.All(DocContentDiff.Text("same","same").Rows,r=>Assert.Equal("same",r.Kind));
        var changed=DocContentDiff.Text("مبلغ: 100","مبلغ: 200");Assert.Contains(changed.Rows,r=>r.Kind=="removed");Assert.Contains(changed.Rows,r=>r.Kind=="added");
        Assert.True(DocContentDiff.Text(new string('x',200001),"x").Truncated);
    }
    [Fact]
    public void Excel_diff_matches_sheet_and_cell_and_detects_formulas()
    {
        byte[] Book(string value,string formula){using var ms=new MemoryStream();using(var zip=new System.IO.Compression.ZipArchive(ms,System.IO.Compression.ZipArchiveMode.Create,true)){
            void Entry(string name,string text){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(text);}
            Entry("xl/workbook.xml","<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Sheet1' r:id='rId1'/></sheets></workbook>");
            Entry("xl/_rels/workbook.xml.rels","<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Target='worksheets/sheet1.xml'/></Relationships>");
            Entry("xl/worksheets/sheet1.xml",$"<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row r='1'><c r='A1'><f>{formula}</f><v>{value}</v></c></row></sheetData></worksheet>");}return ms.ToArray();}
        var result=DocContentDiff.Spreadsheet(Book("10","5+5"),Book("10","2*5"));Assert.Equal("Cells",result.Mode);var row=Assert.Single(result.Rows);Assert.Equal("Sheet1!A1",row.Field);Assert.Equal("changed",row.Kind);
    }
}
