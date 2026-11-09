using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Repairs;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using DbParty = Inventory.Api.Data.Party;
class Test {
 static int count; static void C(bool c,string n){if(!c) throw new Exception(n);count++; Console.WriteLine("PASS "+n);}
 static async Task Main() {
   var db=new AppDbContext(); var g=new Fake(); var dispatcher=new RepairSmsDispatcher(db,g);
   var p=new DbParty {Id=7,CompanyId=1,Mobile="۰۹۱۲۳۴۵۶۷۸۹"}; db.Parties.Add(p);
   var r=new RepairOrder {Id=1,CompanyId=1,PartyId=7,Number="RP-1",Status=RepairStatus.Received}; db.RepairOrders.Add(r);
   await RepairSmsQueue.StageAsync(db,g,r,p,RepairSmsEventType.Registration);
   db.RepairSmsNotifications.Single().Id=1;
   await RepairSmsQueue.StageAsync(db,g,r,p,RepairSmsEventType.Registration);
   C(db.RepairSmsNotifications.Count()==1,"duplicate stage prevented");
   C(db.RepairSmsNotifications.Single().Status==RepairSmsStatus.Pending,"pending when enabled");
   C(g.Calls==0,"stage has no provider call");
   await dispatcher.ProcessOneAsync();
   C(g.Calls==1 && g.Recipient=="09123456789" && g.Message!.Contains("RP-1"),"correct recipient and number");
   C(db.RepairSmsNotifications.Single().Status==RepairSmsStatus.Accepted,"accepted status persisted");
   C(!await dispatcher.ProcessOneAsync() && g.Calls==1,"no duplicate after accepted");
   r.Status=RepairStatus.Ready; await RepairSmsQueue.StageAsync(db,g,r,p,RepairSmsEventType.Ready);
   db.RepairSmsNotifications.Last().Id=2; g.Result=new(KavenegarSmsSendState.Unknown);
   await dispatcher.ProcessOneAsync();
   C(db.RepairSmsNotifications.Last().Status==RepairSmsStatus.Unknown && g.Calls==2,"unknown persisted");
   C(!await dispatcher.ProcessOneAsync() && g.Calls==2,"unknown not retried");
   var r2=new RepairOrder {Id=2,CompanyId=1,PartyId=7,Number="RP-2",Status=RepairStatus.Received}; db.RepairOrders.Add(r2);
   g.IsEnabled=false; await RepairSmsQueue.StageAsync(db,g,r2,p,RepairSmsEventType.Registration);
   db.RepairSmsNotifications.Last().Id=3; g.IsEnabled=true;
   C(db.RepairSmsNotifications.Last().Status==RepairSmsStatus.Disabled && !await dispatcher.ProcessOneAsync(),"disabled event not sent later");
   var r3=new RepairOrder {Id=3,CompanyId=1,PartyId=7,Number="RP-3",Status=RepairStatus.Received}; db.RepairOrders.Add(r3);
   await RepairSmsQueue.StageAsync(db,g,r3,p,RepairSmsEventType.Registration); db.RepairSmsNotifications.Last().Id=4;
   p.Mobile="09120000000"; var calls=g.Calls; await dispatcher.ProcessOneAsync();
   C(db.RepairSmsNotifications.Last().Status==RepairSmsStatus.Cancelled && g.Calls==calls,"changed mobile cancels");
   var r4=new RepairOrder {Id=4,CompanyId=1,PartyId=7,Number="RP-4",Status=RepairStatus.Received}; db.RepairOrders.Add(r4);
   await RepairSmsQueue.StageAsync(db,g,r4,p,RepairSmsEventType.Registration); var lost=db.RepairSmsNotifications.Last();lost.Id=5;
   lost.Status=RepairSmsStatus.Sending;lost.AttemptStartedAtUtc=DateTime.UtcNow.AddMinutes(-11);
   await dispatcher.MarkAbandonedUnknownAsync(DateTime.UtcNow);
   C(lost.Status==RepairSmsStatus.Unknown && !await dispatcher.ProcessOneAsync(),"abandoned send not retried");
   var r5=new RepairOrder {Id=5,CompanyId=1,PartyId=7,Number="RP-5",Status=RepairStatus.Received}; db.RepairOrders.Add(r5);
   await RepairSmsQueue.StageAsync(db,g,r5,p,RepairSmsEventType.Registration); var bad=db.RepairSmsNotifications.Last(); bad.Id=6;bad.CompanyId=2;
   await dispatcher.ProcessOneAsync(); C(bad.Status==RepairSmsStatus.Cancelled && g.Calls==calls,"cross-company mismatch never sent");
   var r6=new RepairOrder {Id=6,CompanyId=1,PartyId=7,Number="RP-6",Status=RepairStatus.Ready}; db.RepairOrders.Add(r6);
   await RepairSmsQueue.StageAsync(db,g,r6,p,RepairSmsEventType.Ready); var stale=db.RepairSmsNotifications.Last();stale.Id=7;
   r6.Status=RepairStatus.InProgress; await dispatcher.ProcessOneAsync();
   C(stale.Status==RepairSmsStatus.Cancelled && g.Calls==calls,"ready event cancelled when no longer ready");
   var r7=new RepairOrder {Id=7,CompanyId=1,PartyId=7,Number="RP-7",Status=RepairStatus.Received}; db.RepairOrders.Add(r7);
   g.IsConfigured=false; await RepairSmsQueue.StageAsync(db,g,r7,p,RepairSmsEventType.Registration); var unconfigured=db.RepairSmsNotifications.Last();unconfigured.Id=8;g.IsConfigured=true;
   C(unconfigured.Status==RepairSmsStatus.NotConfigured && !await dispatcher.ProcessOneAsync(),"unconfigured event never sent later");
   var r8=new RepairOrder {Id=8,CompanyId=1,PartyId=7,Number="RP-8",Status=RepairStatus.Received}; db.RepairOrders.Add(r8);
   await RepairSmsQueue.StageAsync(db,g,r8,p,RepairSmsEventType.Registration); var failed=db.RepairSmsNotifications.Last();failed.Id=9;
   g.Throw=true; await dispatcher.ProcessOneAsync();g.Throw=false;
   C(failed.Status==RepairSmsStatus.Unknown && !await dispatcher.ProcessOneAsync(),"thrown provider error is unknown and not retried");
   var r9=new RepairOrder {Id=9,CompanyId=1,PartyId=7,Number="RP-9",Status=RepairStatus.Received}; db.RepairOrders.Add(r9);
   await RepairSmsQueue.StageAsync(db,g,r9,p,RepairSmsEventType.Registration);var pending=db.RepairSmsNotifications.Last();pending.Id=10;
   g.IsEnabled=false;await dispatcher.ProcessOneAsync();g.IsEnabled=true;
   C(pending.Status==RepairSmsStatus.Disabled && !await dispatcher.ProcessOneAsync(),"disable before dispatch makes event terminal");
   var noMobile=new DbParty {Id=8,CompanyId=1,Mobile=null};db.Parties.Add(noMobile);
   var r10=new RepairOrder {Id=10,CompanyId=1,PartyId=8,Number="RP-10",Status=RepairStatus.Received};db.RepairOrders.Add(r10);
   await RepairSmsQueue.StageAsync(db,g,r10,noMobile,RepairSmsEventType.Registration);var invalid=db.RepairSmsNotifications.Last();invalid.Id=11;
   C(invalid.Status==RepairSmsStatus.InvalidMobile && !await dispatcher.ProcessOneAsync(),"missing mobile produces a terminal warning");
   var r11=new RepairOrder {Id=11,CompanyId=1,PartyId=7,Number="RP-11",Status=RepairStatus.Received};db.RepairOrders.Add(r11);
   await RepairSmsQueue.StageAsync(db,g,r11,p,RepairSmsEventType.Registration);var concurrent=db.RepairSmsNotifications.Last();concurrent.Id=12;
   g.Result=new(KavenegarSmsSendState.Accepted,42,1,200);g.Hold=new();g.Entered=new();calls=g.Calls;
   var first=dispatcher.ProcessOneAsync();await g.Entered.Task;
   C(!await dispatcher.ProcessOneAsync() && g.Calls==calls+1,"another worker does not claim a sending event");
   g.Hold.SetResult(true);await first;g.Hold=null;g.Entered=null;
   C(concurrent.Status==RepairSmsStatus.Accepted && g.Calls==calls+1,"one call after overlapping workers");
   C(RepairSmsPolicy.EventFor(true,RepairStatus.Received,RepairStatus.Received)==RepairSmsEventType.Registration,"registration event");
   C(RepairSmsPolicy.EventFor(false,RepairStatus.Received,RepairStatus.Ready)==RepairSmsEventType.Ready,"ready transition event");
   C(RepairSmsPolicy.EventFor(false,RepairStatus.Ready,RepairStatus.Ready)==null,"editing ready does not create an event");
   C(RepairSmsPolicy.EventFor(false,RepairStatus.Ready,RepairStatus.InProgress)==null,"leaving ready is not an event");
   C(RepairSmsPolicy.NormalizeMobile("۰۹۱۲-۳۴۵-۶۷۸۹")=="09123456789","Persian digits");
   C(RepairSmsPolicy.NormalizeMobile("+98 912 345 6789")=="09123456789","international Iranian mobile");
   C(RepairSmsPolicy.NormalizeMobile("00989123456789")=="09123456789","0098 Iranian mobile");
   C(RepairSmsPolicy.NormalizeMobile("9123456789")=="09123456789","Iranian mobile without zero");
   C(RepairSmsPolicy.NormalizeMobile("02112345678")==null,"reject landline");
   C(RepairSmsPolicy.NormalizeMobile("0912,0913")==null,"reject a list");
   C(RepairSmsPolicy.NormalizeMobile("0912abc3456789")==null,"reject letters");
   C(RepairSmsPolicy.NormalizeMobile(null)==null,"reject missing number");
   C(RepairSmsPolicy.InitialStatus(null,false,false)==RepairSmsStatus.InvalidMobile,"invalid even when disabled");
   C(RepairSmsPolicy.InitialStatus("09123456789",false,true)==RepairSmsStatus.Disabled,"disabled terminal");
   C(RepairSmsPolicy.InitialStatus("09123456789",true,false)==RepairSmsStatus.NotConfigured,"unconfigured terminal");
   C(RepairSmsPolicy.InitialStatus("09123456789",true,true)==RepairSmsStatus.Pending,"enabled pending");
   C(RepairSmsPolicy.Message(RepairSmsEventType.Registration,"RP-123").Contains("RP-123"),"registration number");
   C(RepairSmsPolicy.Message(RepairSmsEventType.Ready,"RP-123").Contains("RP-123"),"ready number");
   C(RepairSmsPolicy.Result(KavenegarSmsSendState.Accepted)==RepairSmsStatus.Accepted,"accepted is not delivered");
   C(RepairSmsPolicy.Result(KavenegarSmsSendState.Delivered)==RepairSmsStatus.Delivered,"delivered result");
   C(RepairSmsPolicy.Result(KavenegarSmsSendState.Unknown)==RepairSmsStatus.Unknown,"unknown result");
   C(RepairSmsPolicy.Result(KavenegarSmsSendState.Failed)==RepairSmsStatus.Failed,"failed result");
   var guardDb = new AppDbContext();
   var product = new Inventory.Api.Data.Product { Id=21, IsService=false };
   await ShopCommissionProductKindGuard.EnsureChangeAllowedAsync(guardDb, product, true);
   C(true,"product kind change without references remains allowed");
   guardDb.ShopWarrantyItems.Add(new ShopWarrantyItem { ProductId=21 });
   var rejected = false;
   try { await ShopCommissionProductKindGuard.EnsureChangeAllowedAsync(guardDb, product, true); }
   catch (InvalidOperationException) { rejected=true; }
   C(rejected,"product kind change remains blocked for warranty history");
   await ShopCommissionProductKindGuard.EnsureChangeAllowedAsync(guardDb, product, false);
   C(true,"saving the existing product kind remains allowed");
   var saleDb = new AppDbContext();
   saleDb.Transactions.Add(new Transaction { Type=TransactionType.Sale, ReferrerGoodsPercentSnapshot=10,
       Lines=new() { new TransactionLine { ProductId=21 } } });
   rejected=false;
   try { await ShopCommissionProductKindGuard.EnsureChangeAllowedAsync(saleDb, product, true); }
   catch (InvalidOperationException) { rejected=true; }
   C(rejected,"product kind change remains blocked for recorded commission");
   Console.WriteLine($"{count} checks passed");
 }
}
class Fake : IKavenegarSmsGateway {
 public bool IsEnabled {get;set;}=true; public bool IsConfigured {get;set;}=true; public int Calls; public string? Recipient,Message;
 public KavenegarSmsSendResult Result=new(KavenegarSmsSendState.Accepted,42,1,200);
 public bool Throw;
 public TaskCompletionSource<bool>? Hold, Entered;
 public async Task<KavenegarSmsSendResult> SendAsync(string r,string m) {
   Calls++;Recipient=r;Message=m;if(Throw) throw new Exception("fake failure");
   Entered?.TrySetResult(true);if(Hold is not null) await Hold.Task;return Result;
 }
}
