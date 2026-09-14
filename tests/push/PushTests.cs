using Inventory.Api.Controllers;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Inventory.Push.Tests;

public sealed class PushTests
{
    private static string Enc(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static (string Pub, string Private) Keys()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256); var p = ec.ExportParameters(true);
        return (Enc(new byte[] { 4 }.Concat(p.Q.X!).Concat(p.Q.Y!).ToArray()), Enc(p.D!));
    }
    private sealed class Fixture : IDisposable
    {
        public SqliteConnection Connection = new("Data Source=:memory:");
        public AppDbContext Db;
        public PushService Service;
        public string Pub = Keys().Pub;
        public string Auth = Enc(RandomNumberGenerator.GetBytes(16));
        public Fixture(bool configured = true)
        {
            Connection.Open(); Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options); Db.Database.EnsureCreated();
            Db.Users.AddRange(new User { Id=1, Username="one", IsActive=true }, new User { Id=2, Username="two", IsActive=true }); Db.SaveChanges();
            var keys = Keys(); var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["PushNotifications:VapidPublicKey"]= configured ? keys.Pub : "", ["PushNotifications:VapidPrivateKey"]= configured ? keys.Private : "", ["PushNotifications:VapidSubject"]="mailto:push@example.test" }).Build();
            Service = new(Db, new PushSettings(cfg));
        }
        public async Task Subscribe(int user=1, string endpoint="https://fcm.googleapis.com/wp/device") => await Service.SaveSubscriptionAsync(user,endpoint,Pub,Auth,new string('u',400));
        public NotificationsController Controller(int user=1)
        {
            var c = new NotificationsController(Db,null!,Service); c.ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier,user.ToString()) },"test")) } }; return c;
        }
        public void Dispose() { Db.Dispose(); Connection.Dispose(); }
    }
    private sealed class Transport(int status=201, bool fail=false) : IPushTransport
    {
        public int Calls; public string? Payload;
        public Task<int> SendAsync(Inventory.Api.Data.PushSubscription sub,string payload,CancellationToken ct)
        { Calls++; Payload=payload; if(fail) throw new HttpRequestException(); return Task.FromResult(status); }
    }
    [Fact] public async Task Registration_refreshes_keys_and_changes_account_without_duplicates()
    {
        using var f=new Fixture(); await f.Subscribe(); var id=(await f.Db.PushSubscriptions.SingleAsync()).Id;
        var next=Keys().Pub; await f.Service.SaveSubscriptionAsync(2,"https://fcm.googleapis.com/wp/device",next,f.Auth,"new");
        var row=await f.Db.PushSubscriptions.SingleAsync(); Assert.Equal(id,row.Id); Assert.Equal(2,row.UserId); Assert.Equal(next,row.P256DH);
        await f.Service.RemoveSubscriptionAsync(1,row.Endpoint); Assert.Single(f.Db.PushSubscriptions);
        await f.Service.RemoveSubscriptionAsync(2,row.Endpoint); Assert.Empty(f.Db.PushSubscriptions);
    }
    [Theory]
    [InlineData("http://fcm.googleapis.com/wp/x")]
    [InlineData("https://127.0.0.1/wp/x")]
    [InlineData("https://fcm.googleapis.com.evil.test/wp/x")]
    [InlineData("https://fcm.googleapis.com:8443/wp/x")]
    [InlineData("https://user@fcm.googleapis.com/wp/x")]
    [InlineData("https://fcm.googleapis.com/redirect")]
    public async Task Rejects_non_provider_and_private_endpoints(string endpoint)
    { using var f=new Fixture(); await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.SaveSubscriptionAsync(1,endpoint,f.Pub,f.Auth,null)); Assert.Empty(f.Db.PushSubscriptions); }
    [Fact] public async Task Rejects_bad_encryption_keys_and_missing_vapid()
    {
        using var f=new Fixture(); await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.SaveSubscriptionAsync(1,"https://fcm.googleapis.com/wp/x","bad","bad",null));
        using var disabled=new Fixture(false); Assert.False(disabled.Service.IsConfigured); Assert.Empty(disabled.Service.VapidPublicKey);
        Assert.Equal(503,Assert.IsType<ObjectResult>(await disabled.Controller().TestPush(new())).StatusCode);
    }
    [Fact] public async Task Notification_and_queue_rollback_together_and_target_only_registered_users()
    {
        using var f=new Fixture(); await f.Subscribe();
        await using(var tx=await f.Db.Database.BeginTransactionAsync())
        {
            f.Db.AppNotifications.Add(new() {UserId=1,Title="notification"});
            Assert.Equal(1,await PushQueue.StageAsync(f.Db,new[]{1,1,2},"title","body","/workorders"));
            await f.Db.SaveChangesAsync(); await tx.RollbackAsync();
        }
        f.Db.ChangeTracker.Clear(); Assert.Empty(f.Db.AppNotifications); Assert.Empty(f.Db.PushDeliveries);
    }
    [Fact] public async Task Claims_once_and_records_provider_acceptance_not_device_delivery()
    {
        using var f=new Fixture(); await f.Subscribe(); await f.Service.SendToUserAsync(1,"عنوان","بدنه","/workorders/1");
        var job=await PushQueue.ClaimAsync(f.Db,DateTime.UtcNow.AddSeconds(1)); Assert.NotNull(job); Assert.Null(await PushQueue.ClaimAsync(f.Db,DateTime.UtcNow.AddSeconds(1)));
        var transport=new Transport(); await PushQueue.ProcessAsync(f.Db,transport,job!); f.Db.ChangeTracker.Clear();
        Assert.Equal("Accepted",(await f.Db.PushDeliveries.SingleAsync()).Status); Assert.Equal(1,transport.Calls);
        var payload=JsonSerializer.Deserialize<JsonElement>(transport.Payload!); Assert.Equal(1,payload.GetProperty("userId").GetInt32()); Assert.EndsWith("Z",payload.GetProperty("expiresAtUtc").GetString()); Assert.StartsWith("inv-",payload.GetProperty("tag").GetString());
    }
    [Theory] [InlineData(404)] [InlineData(410)]
    public async Task Expired_subscription_is_removed(int code)
    {
        using var f=new Fixture(); await f.Subscribe(); await f.Service.SendToUserAsync(1,"t",null,null);
        var job=(await PushQueue.ClaimAsync(f.Db,DateTime.UtcNow.AddSeconds(1)))!; await PushQueue.ProcessAsync(f.Db,new Transport(code),job);
        Assert.Empty(f.Db.PushSubscriptions); f.Db.ChangeTracker.Clear(); Assert.Equal("Expired",(await f.Db.PushDeliveries.SingleAsync()).Status);
    }
    [Fact] public async Task Retries_transient_failures_with_backoff_then_stops()
    {
        using var f=new Fixture(); await f.Subscribe(); await f.Service.SendToUserAsync(1,"t",null,null);
        for(var i=1;i<=6;i++)
        {
            var job=(await PushQueue.ClaimAsync(f.Db,DateTime.UtcNow.AddHours(i)))!; Assert.NotNull(job);
            await PushQueue.ProcessAsync(f.Db,new Transport(503),job); f.Db.ChangeTracker.Clear();
            var row=await f.Db.PushDeliveries.SingleAsync(); Assert.Equal(i,row.Attempts); Assert.Equal(i==6?"Failed":"Pending",row.Status);
            if(i<6) Assert.True(row.NextAttemptAtUtc>DateTime.UtcNow);
        }
        Assert.Null(await PushQueue.ClaimAsync(f.Db,DateTime.UtcNow.AddDays(1)));
    }
    [Fact] public async Task Lease_recovery_fences_old_completion_and_keeps_stable_tag()
    {
        using var f=new Fixture(); await f.Subscribe(); await f.Service.SendToUserAsync(1,"t",null,null);
        var old=(await PushQueue.ClaimAsync(f.Db,DateTime.UtcNow.AddSeconds(1)))!;
        var newer=(await PushQueue.ClaimAsync(f.Db,DateTime.UtcNow.AddMinutes(3)))!;
        Assert.NotEqual(old.LeaseToken,newer.LeaseToken); Assert.Equal(old.Tag,newer.Tag);
        await PushQueue.ProcessAsync(f.Db,new Transport(),old); f.Db.ChangeTracker.Clear(); Assert.Equal("Working",(await f.Db.PushDeliveries.SingleAsync()).Status);
        await PushQueue.ProcessAsync(f.Db,new Transport(),newer); f.Db.ChangeTracker.Clear(); Assert.Equal("Accepted",(await f.Db.PushDeliveries.SingleAsync()).Status);
    }
    [Fact] public async Task Revoked_or_reassigned_device_does_not_receive_old_users_queue()
    {
        using var f=new Fixture(); await f.Subscribe(); await f.Service.SendToUserAsync(1,"secret",null,null);
        await f.Subscribe(2); var job=(await PushQueue.ClaimAsync(f.Db,DateTime.UtcNow.AddSeconds(1)))!; var transport=new Transport();
        await PushQueue.ProcessAsync(f.Db,transport,job); Assert.Equal(0,transport.Calls); f.Db.ChangeTracker.Clear(); Assert.Equal("Cancelled",(await f.Db.PushDeliveries.SingleAsync()).Status);
    }
    [Fact] public async Task Expired_jobs_are_not_sent()
    {
        using var f=new Fixture(); await f.Subscribe(); await f.Service.SendToUserAsync(1,"t",null,null);
        await f.Db.PushDeliveries.ExecuteUpdateAsync(s=>s.SetProperty(j=>j.ExpiresAtUtc,DateTime.UtcNow.AddMinutes(-1)));
        var job=(await PushQueue.ClaimAsync(f.Db,DateTime.UtcNow.AddSeconds(1)))!; var transport=new Transport();
        await PushQueue.ProcessAsync(f.Db,transport,job); Assert.Equal(0,transport.Calls);
    }
    [Fact] public async Task Test_endpoint_is_owned_rate_limited_and_reports_queued()
    {
        using var f=new Fixture(); await f.Subscribe(); var input=new NotificationsController.PushUnsubscribeInput { Endpoint="https://fcm.googleapis.com/wp/device" };
        Assert.IsType<BadRequestObjectResult>(await f.Controller(2).TestPush(input));
        Assert.IsType<AcceptedResult>(await f.Controller().TestPush(input));
        Assert.Equal(429,Assert.IsType<ObjectResult>(await f.Controller().TestPush(input)).StatusCode);
        var result=Assert.IsType<OkObjectResult>(await f.Controller(2).PushStatus(input));
        Assert.False(JsonSerializer.SerializeToElement(result.Value).GetProperty("registered").GetBoolean());
    }
    [Fact] public async Task Schema_upgrade_is_idempotent_and_deduplicates_only_subscriptions()
    {
        using var f=new Fixture(); await f.Subscribe();
        await f.Db.Database.ExecuteSqlRawAsync("DROP INDEX IX_PushSubscriptions_Endpoint");
        f.Db.PushSubscriptions.Add(new() {UserId=2,Endpoint="https://fcm.googleapis.com/wp/device",P256DH=f.Pub,Auth=f.Auth,LastSeenAt=DateTime.Now.AddMinutes(1)}); await f.Db.SaveChangesAsync();
        await PushDeliverySchema.EnsureAsync(f.Db); await PushDeliverySchema.EnsureAsync(f.Db);
        Assert.Equal(2,(await f.Db.PushSubscriptions.AsNoTracking().SingleAsync()).UserId); Assert.Equal(2,await f.Db.Users.CountAsync());
    }
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Urgency, Ttl, Authorization;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Urgency=request.Headers.GetValues("Urgency").Single(); Ttl=request.Headers.GetValues("TTL").Single();
            Authorization=request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Created));
        }
    }
    private sealed class TestHttpFactory(CaptureHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler,disposeHandler:false); }
    [Fact] public async Task Real_transport_encrypts_and_sets_auth_urgency_and_bounded_ttl_without_network()
    {
        var keys=Keys(); var cfg=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["PushNotifications:VapidPublicKey"]=keys.Pub,["PushNotifications:VapidPrivateKey"]=keys.Private,["PushNotifications:VapidSubject"]="mailto:push@example.test" }).Build();
        using var handler=new CaptureHandler(); var transport=new WebPushTransport(new PushSettings(cfg),new TestHttpFactory(handler));
        var sub=new Inventory.Api.Data.PushSubscription { Endpoint="https://fcm.googleapis.com/wp/test",P256DH=Keys().Pub,Auth=Enc(RandomNumberGenerator.GetBytes(16)) };
        var result=await transport.SendAsync(sub,JsonSerializer.Serialize(new {title="test",expiresAtUtc=DateTime.UtcNow.AddHours(48)}),default);
        Assert.Equal(201,result); Assert.Equal("high",handler.Urgency); Assert.InRange(int.Parse(handler.Ttl!),172790,172800); Assert.NotNull(handler.Authorization);
    }

}
