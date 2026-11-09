using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Repairs;
using Inventory.Api.Services.ItAssets;
using Inventory.Api.Services.Core;
using Inventory.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using DbParty = Inventory.Api.Data.Party;
class Test {
 static int count; static void C(bool c,string n){if(!c) throw new Exception(n);count++; Console.WriteLine("PASS "+n);}
 static async Task Main() {
   var db=new AppDbContext(); var g=new Fake(); var config=new KavenegarSmsOptions();
   var dispatcher=new RepairSmsDispatcher(db,g,new OptionsWrapper<KavenegarSmsOptions>(config));
   db.SystemCompanies.Add(new SystemCompany { Id=1, Name="فروشگاه اول" });
   db.SystemCompanies.Add(new SystemCompany { Id=2, Name="فروشگاه دوم" });
   var permission=new CompanySmsSetting { CompanyId=1, ModuleKey=SmsModuleCatalog.ShopRepairs, Enabled=true };
   db.CompanySmsSettings.Add(permission);
   var p=new DbParty {Id=7,CompanyId=1,Mobile="۰۹۱۲۳۴۵۶۷۸۹"}; db.Parties.Add(p);
   var r=new RepairOrder {Id=1,CompanyId=1,PartyId=7,Number="RP-1",Status=RepairStatus.Received}; db.RepairOrders.Add(r);
   await RepairSmsQueue.StageAsync(db,g,config,r,p,RepairSmsEventType.Registration);
   db.RepairSmsNotifications.Single().Id=1;
   await RepairSmsQueue.StageAsync(db,g,config,r,p,RepairSmsEventType.Registration);
   C(db.RepairSmsNotifications.Count()==1,"duplicate stage prevented");
   C(db.RepairSmsNotifications.Single().Status==RepairSmsStatus.Pending,"pending when enabled");
   C(g.Calls==0,"stage has no provider call");
   await dispatcher.ProcessOneAsync();
   C(g.Calls==1 && g.Recipient=="09123456789" && g.Token=="1" && g.CompanyName=="فروشگاه اول" && g.Template=="RepairAccept","correct recipient and number");
   C(db.RepairSmsNotifications.Single().Status==RepairSmsStatus.Accepted,"accepted status persisted");
   C(!await dispatcher.ProcessOneAsync() && g.Calls==1,"no duplicate after accepted");
   r.Status=RepairStatus.Ready; await RepairSmsQueue.StageAsync(db,g,config,r,p,RepairSmsEventType.Ready);
   db.RepairSmsNotifications.Last().Id=2; g.Result=new(KavenegarSmsSendState.Unknown);
   await dispatcher.ProcessOneAsync();
   C(db.RepairSmsNotifications.Last().Status==RepairSmsStatus.Unknown && g.Calls==2,"unknown persisted");
   C(!await dispatcher.ProcessOneAsync() && g.Calls==2,"unknown not retried");
   var r2=new RepairOrder {Id=2,CompanyId=1,PartyId=7,Number="RP-2",Status=RepairStatus.Received}; db.RepairOrders.Add(r2);
   g.IsEnabled=false; await RepairSmsQueue.StageAsync(db,g,config,r2,p,RepairSmsEventType.Registration);
   db.RepairSmsNotifications.Last().Id=3; g.IsEnabled=true;
   C(db.RepairSmsNotifications.Last().Status==RepairSmsStatus.Disabled && !await dispatcher.ProcessOneAsync(),"disabled event not sent later");
   var r3=new RepairOrder {Id=3,CompanyId=1,PartyId=7,Number="RP-3",Status=RepairStatus.Received}; db.RepairOrders.Add(r3);
   await RepairSmsQueue.StageAsync(db,g,config,r3,p,RepairSmsEventType.Registration); db.RepairSmsNotifications.Last().Id=4;
   p.Mobile="09120000000"; var calls=g.Calls; await dispatcher.ProcessOneAsync();
   C(db.RepairSmsNotifications.Last().Status==RepairSmsStatus.Cancelled && g.Calls==calls,"changed mobile cancels");
   var r4=new RepairOrder {Id=4,CompanyId=1,PartyId=7,Number="RP-4",Status=RepairStatus.Received}; db.RepairOrders.Add(r4);
   await RepairSmsQueue.StageAsync(db,g,config,r4,p,RepairSmsEventType.Registration); var lost=db.RepairSmsNotifications.Last();lost.Id=5;
   lost.Status=RepairSmsStatus.Sending;lost.AttemptStartedAtUtc=DateTime.UtcNow.AddMinutes(-11);
   await dispatcher.MarkAbandonedUnknownAsync(DateTime.UtcNow);
   C(lost.Status==RepairSmsStatus.Unknown && !await dispatcher.ProcessOneAsync(),"abandoned send not retried");
   var r5=new RepairOrder {Id=5,CompanyId=1,PartyId=7,Number="RP-5",Status=RepairStatus.Received}; db.RepairOrders.Add(r5);
   await RepairSmsQueue.StageAsync(db,g,config,r5,p,RepairSmsEventType.Registration); var bad=db.RepairSmsNotifications.Last(); bad.Id=6;bad.CompanyId=2;
   await dispatcher.ProcessOneAsync(); C(bad.Status==RepairSmsStatus.Cancelled && g.Calls==calls,"cross-company mismatch never sent");
   var r6=new RepairOrder {Id=6,CompanyId=1,PartyId=7,Number="RP-6",Status=RepairStatus.Ready}; db.RepairOrders.Add(r6);
   await RepairSmsQueue.StageAsync(db,g,config,r6,p,RepairSmsEventType.Ready); var stale=db.RepairSmsNotifications.Last();stale.Id=7;
   r6.Status=RepairStatus.InProgress; await dispatcher.ProcessOneAsync();
   C(stale.Status==RepairSmsStatus.Cancelled && g.Calls==calls,"ready event cancelled when no longer ready");
   var r7=new RepairOrder {Id=7,CompanyId=1,PartyId=7,Number="RP-7",Status=RepairStatus.Received}; db.RepairOrders.Add(r7);
   g.IsLookupConfigured=false; await RepairSmsQueue.StageAsync(db,g,config,r7,p,RepairSmsEventType.Registration); var unconfigured=db.RepairSmsNotifications.Last();unconfigured.Id=8;g.IsLookupConfigured=true;
   C(unconfigured.Status==RepairSmsStatus.NotConfigured && !await dispatcher.ProcessOneAsync(),"unconfigured event never sent later");
   var r8=new RepairOrder {Id=8,CompanyId=1,PartyId=7,Number="RP-8",Status=RepairStatus.Received}; db.RepairOrders.Add(r8);
   await RepairSmsQueue.StageAsync(db,g,config,r8,p,RepairSmsEventType.Registration); var failed=db.RepairSmsNotifications.Last();failed.Id=9;
   g.Throw=true; await dispatcher.ProcessOneAsync();g.Throw=false;
   C(failed.Status==RepairSmsStatus.Unknown && !await dispatcher.ProcessOneAsync(),"thrown provider error is unknown and not retried");
   var r9=new RepairOrder {Id=9,CompanyId=1,PartyId=7,Number="RP-9",Status=RepairStatus.Received}; db.RepairOrders.Add(r9);
   await RepairSmsQueue.StageAsync(db,g,config,r9,p,RepairSmsEventType.Registration);var pending=db.RepairSmsNotifications.Last();pending.Id=10;
   g.IsEnabled=false;await dispatcher.ProcessOneAsync();g.IsEnabled=true;
   C(pending.Status==RepairSmsStatus.Disabled && !await dispatcher.ProcessOneAsync(),"disable before dispatch makes event terminal");
   var noMobile=new DbParty {Id=8,CompanyId=1,Mobile=null};db.Parties.Add(noMobile);
   var r10=new RepairOrder {Id=10,CompanyId=1,PartyId=8,Number="RP-10",Status=RepairStatus.Received};db.RepairOrders.Add(r10);
   await RepairSmsQueue.StageAsync(db,g,config,r10,noMobile,RepairSmsEventType.Registration);var invalid=db.RepairSmsNotifications.Last();invalid.Id=11;
   C(invalid.Status==RepairSmsStatus.InvalidMobile && !await dispatcher.ProcessOneAsync(),"missing mobile produces a terminal warning");
   var r11=new RepairOrder {Id=11,CompanyId=1,PartyId=7,Number="RP-11",Status=RepairStatus.Received};db.RepairOrders.Add(r11);
   await RepairSmsQueue.StageAsync(db,g,config,r11,p,RepairSmsEventType.Registration);var concurrent=db.RepairSmsNotifications.Last();concurrent.Id=12;
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
   C(RepairSmsPolicy.InitialStatus(null,false,false,false,"شرکت","RP-1")==RepairSmsStatus.InvalidMobile,"invalid even when disabled");
   C(RepairSmsPolicy.InitialStatus("09123456789",false,true,true,"شرکت","RP-1")==RepairSmsStatus.ModuleDisabled,"module permission required");
   C(RepairSmsPolicy.InitialStatus("09123456789",true,false,true,"شرکت","RP-1")==RepairSmsStatus.Disabled,"gateway disabled terminal");
   C(RepairSmsPolicy.InitialStatus("09123456789",true,true,false,"شرکت","RP-1")==RepairSmsStatus.NotConfigured,"unconfigured terminal");
   C(RepairSmsPolicy.InitialStatus("09123456789",true,true,true,"شرکت","RP-1")==RepairSmsStatus.Pending,"enabled pending");
   C(RepairSmsPolicy.NumberToken("RP-0001")=="0001", "leading zeroes preserved in number token");
   C(RepairSmsPolicy.NumberToken("RP-123-4")==null && RepairSmsPolicy.NumberToken("OTHER-123")==null, "legacy incompatible numbers rejected");
   C(RepairSmsPolicy.TemplateName(RepairSmsEventType.Registration,config)=="RepairAccept" &&
     RepairSmsPolicy.TemplateName(RepairSmsEventType.Ready,config)=="RepairReady","separate template names");
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
   // The same policy is enforced for the exact event company AND integration key.
   db.CompanySmsSettings.Add(new CompanySmsSetting { CompanyId=2, ModuleKey="Shop.Future", Enabled=true });
   C((await CompanySmsServicePolicy.GetEnabledRevisionAsync(db,2,SmsModuleCatalog.ShopRepairs)) is null &&
     (await CompanySmsServicePolicy.GetEnabledRevisionAsync(db,2,"Shop.Future")) is null, "other/unknown module is not repair permission");
   var p2=new DbParty {Id=22,CompanyId=2,Mobile="09120000002"}; db.Parties.Add(p2);
   var other=new RepairOrder {Id=21,CompanyId=2,PartyId=22,Number="RP-0042",Status=RepairStatus.Received}; db.RepairOrders.Add(other);
   await RepairSmsQueue.StageAsync(db,g,config,other,p2,RepairSmsEventType.Registration); var denied=db.RepairSmsNotifications.Last();denied.Id=13;
   C(denied.Status==RepairSmsStatus.ModuleDisabled && !await dispatcher.ProcessOneAsync(),"company 2 disabled is terminal");
   var permission2=new CompanySmsSetting { CompanyId=2, ModuleKey=SmsModuleCatalog.ShopRepairs, Enabled=true };db.CompanySmsSettings.Add(permission2);
   C(!await dispatcher.ProcessOneAsync(),"enabling does not revive previous disabled event");
   var own=new RepairOrder {Id=22,CompanyId=1,PartyId=7,Number="RP-0022",Status=RepairStatus.Received};db.RepairOrders.Add(own);
   var theirs=new RepairOrder {Id=23,CompanyId=2,PartyId=22,Number="RP-0023",Status=RepairStatus.Received};db.RepairOrders.Add(theirs);
   await RepairSmsQueue.StageAsync(db,g,config,own,p,RepairSmsEventType.Registration);var pendingOwn=db.RepairSmsNotifications.Last();pendingOwn.Id=14;
   await RepairSmsQueue.StageAsync(db,g,config,theirs,p2,RepairSmsEventType.Registration);var pendingTheirs=db.RepairSmsNotifications.Last();pendingTheirs.Id=15;
   permission.Enabled=false;await new RepairSmsRevocationHandler(db).CancelPendingAsync(1,DateTime.UtcNow,CancellationToken.None);
   C(pendingOwn.Status==RepairSmsStatus.ModuleDisabled && pendingOwn.Recipient==null && pendingTheirs.Status==RepairSmsStatus.Pending,
     "revocation terminates only pending messages for company 1");
   permission.Enabled=true;calls=g.Calls;await dispatcher.ProcessOneAsync();
   C(g.Calls==calls+1 && g.CompanyName=="فروشگاه دوم" && g.Token=="0023" && !await dispatcher.ProcessOneAsync(),
     "re-enable does not revive cancelled job; company 2 retains its name/number");
   var after=new RepairOrder {Id=24,CompanyId=1,PartyId=7,Number="RP-0024",Status=RepairStatus.Received};db.RepairOrders.Add(after);
   await RepairSmsQueue.StageAsync(db,g,config,after,p,RepairSmsEventType.Registration);var postClaim=db.RepairSmsNotifications.Last();postClaim.Id=16;
   permission.Enabled=false;calls=g.Calls;await dispatcher.ProcessOneAsync();permission.Enabled=true;
   C(postClaim.Status==RepairSmsStatus.ModuleDisabled && g.Calls==calls && !await dispatcher.ProcessOneAsync(),
     "dispatcher rechecks company permission and does not retry after re-enable");
   var company=db.SystemCompanies.Single(c=>c.Id==1);company.Name=new string('ا',101);
   var nameBad=new RepairOrder {Id=25,CompanyId=1,PartyId=7,Number="RP-0025",Status=RepairStatus.Received};db.RepairOrders.Add(nameBad);
   await RepairSmsQueue.StageAsync(db,g,config,nameBad,p,RepairSmsEventType.Registration); var nameEvent=db.RepairSmsNotifications.Last();nameEvent.Id=17;
   C(nameEvent.Status==RepairSmsStatus.InvalidCompanyName,"overlong company name is a terminal warning");
   company.Name="فروشگاه اول";
   var legacy=new RepairOrder {Id=26,CompanyId=1,PartyId=7,Number="OLD-26",Status=RepairStatus.Received};db.RepairOrders.Add(legacy);
   await RepairSmsQueue.StageAsync(db,g,config,legacy,p,RepairSmsEventType.Registration);var legacyEvent=db.RepairSmsNotifications.Last();legacyEvent.Id=18;
   C(legacyEvent.Status==RepairSmsStatus.InvalidRepairNumber,"legacy number is not silently changed");
   var changedName=new RepairOrder {Id=27,CompanyId=1,PartyId=7,Number="RP-0027",Status=RepairStatus.Received};db.RepairOrders.Add(changedName);
   await RepairSmsQueue.StageAsync(db,g,config,changedName,p,RepairSmsEventType.Registration);var changedNameEvent=db.RepairSmsNotifications.Last();changedNameEvent.Id=19;
   company.Name="نام\nنامعتبر";calls=g.Calls;await dispatcher.ProcessOneAsync();company.Name="فروشگاه اول";
   C(changedNameEvent.Status==RepairSmsStatus.InvalidCompanyName && g.Calls==calls,"name checked again before sending");
   C(KavenegarSmsValidation.CompanyToken("نام فارسی & + = فروشگاه") &&
     !KavenegarSmsValidation.CompanyToken("یک دو سه چهار پنج شش هفت") &&
     !KavenegarSmsValidation.CompanyToken("نام_شرکت"), "company lookup constraints checked without truncation");
   g.IsConfigured=false; g.IsLookupConfigured=true;
   var lookupOnly=new RepairOrder {Id=28,CompanyId=1,PartyId=7,Number="RP-0028",Status=RepairStatus.Received};db.RepairOrders.Add(lookupOnly);
   await RepairSmsQueue.StageAsync(db,g,config,lookupOnly,p,RepairSmsEventType.Registration);var lookupEvent=db.RepairSmsNotifications.Last();lookupEvent.Id=20;
   await dispatcher.ProcessOneAsync(); C(lookupEvent.Status==RepairSmsStatus.Accepted,"lookup does not require ordinary sender");
   // Exercise the real settings controller against lightweight in-memory shims (not EF/HTTP).
   db.ActiveCompanyId=1;db.CurrentUserName="tester";
   var context=new ControllerContext { HttpContext=new DefaultHttpContext {
      User=new ClaimsPrincipal(new ClaimsIdentity(new[] {new Claim(ClaimTypes.NameIdentifier,"10")},"test")) } };
   var controller=new CompanySmsSettingsController(db,new TestPermissions("SmsSettings.Update"),
      new ICompanySmsRevocationHandler[] {new RepairSmsRevocationHandler(db)}) {ControllerContext=context};
   var get=await controller.Get(CancellationToken.None) as OkObjectResult;
   var dto=get?.Value as CompanySmsSettingsDto;
   C(dto?.CompanyId==1 && dto.CompanyName=="فروشگاه اول" && dto.Modules.Single().Key==SmsModuleCatalog.ShopRepairs,
      "settings read shows active company and integrated module");
   SaveCompanySmsSettingsDto Choice(int id,bool enabled)=>new() {CompanyId=id,Modules=new() {new() {Key=SmsModuleCatalog.ShopRepairs,Enabled=enabled}}};
   C(await controller.Save(Choice(2,false),CancellationToken.None) is ConflictObjectResult && permission2.Enabled,
      "payload cannot change another company");
   var readOnly=new CompanySmsSettingsController(db,new TestPermissions("SmsSettings.Read"),
      new ICompanySmsRevocationHandler[] {new RepairSmsRevocationHandler(db)}) {ControllerContext=context};
   C(await readOnly.Get(CancellationToken.None) is OkObjectResult &&
      await readOnly.Save(Choice(1,false),CancellationToken.None) is ForbidResult,"read-only permission cannot update");
   var noPermission=new CompanySmsSettingsController(db,new TestPermissions(),
      new ICompanySmsRevocationHandler[] {new RepairSmsRevocationHandler(db)}) {ControllerContext=context};
   C(await noPermission.Get(CancellationToken.None) is ForbidResult,"settings require their own permission");
   var noHook=new CompanySmsSettingsController(db,new TestPermissions("SmsSettings.Update"),
      Array.Empty<ICompanySmsRevocationHandler>()) {ControllerContext=context};
   C(await noHook.Save(Choice(1,true),CancellationToken.None) is ObjectResult {StatusCode:500},"module without revocation hook fails closed");
   var unknown=new SaveCompanySmsSettingsDto {CompanyId=1,Modules=new() {new() {Key="Shop.Future",Enabled=true}}};
   C(await controller.Save(unknown,CancellationToken.None) is BadRequestObjectResult,"unknown module cannot be activated through API");
   var finalOrder=new RepairOrder {Id=29,CompanyId=1,PartyId=7,Number="RP-0029",Status=RepairStatus.Received};db.RepairOrders.Add(finalOrder);
   await RepairSmsQueue.StageAsync(db,g,config,finalOrder,p,RepairSmsEventType.Registration);var finalEvent=db.RepairSmsNotifications.Last();finalEvent.Id=21;
   C(await controller.Save(Choice(1,false),CancellationToken.None) is OkObjectResult &&
      !permission.Enabled && finalEvent.Status==RepairSmsStatus.ModuleDisabled && permission2.Enabled,
      "controller revokes only active company's pending events");
   C(await controller.Save(Choice(1,true),CancellationToken.None) is OkObjectResult && !await dispatcher.ProcessOneAsync(),
      "controller re-enable never resurrects cancelled events");
   var oldOrder=new RepairOrder {Id=30,CompanyId=1,PartyId=7,Number="RP-0030",Status=RepairStatus.Received};db.RepairOrders.Add(oldOrder);
   var preUpgrade=new RepairSmsNotification {Id=22,CompanyId=1,RepairOrderId=30,PartyId=7,
      EventType=RepairSmsEventType.Registration,Status=RepairSmsStatus.Pending,Recipient=RepairSmsPolicy.NormalizeMobile(p.Mobile),PermissionRevision=null};
   db.RepairSmsNotifications.Add(preUpgrade);calls=g.Calls;await dispatcher.ProcessOneAsync();
   C(preUpgrade.Status==RepairSmsStatus.ModuleDisabled && g.Calls==calls,"pre-upgrade pending without permission revision cannot be sent");
   var cycleOrder=new RepairOrder {Id=31,CompanyId=1,PartyId=7,Number="RP-0031",Status=RepairStatus.Received};db.RepairOrders.Add(cycleOrder);
   await RepairSmsQueue.StageAsync(db,g,config,cycleOrder,p,RepairSmsEventType.Registration);var cycleEvent=db.RepairSmsNotifications.Last();cycleEvent.Id=23;
   permission.Enabled=false;permission.Revision++;permission.Enabled=true;permission.Revision++;
   calls=g.Calls;await dispatcher.ProcessOneAsync();
   C(cycleEvent.Status==RepairSmsStatus.ModuleDisabled && g.Calls==calls,"old revision remains ineligible after off/on cycle");
   var sameOrder=new RepairOrder {Id=32,CompanyId=1,PartyId=7,Number="RP-0032",Status=RepairStatus.Received};db.RepairOrders.Add(sameOrder);
   await RepairSmsQueue.StageAsync(db,g,config,sameOrder,p,RepairSmsEventType.Registration);var sameEvent=db.RepairSmsNotifications.Last();sameEvent.Id=24;
   var sameRevision=permission.Revision;
   C(await controller.Save(Choice(1,true),CancellationToken.None) is OkObjectResult && permission.Revision==sameRevision,
      "saving unchanged enabled permission does not invalidate the queue");
   await dispatcher.ProcessOneAsync();C(sameEvent.Status==RepairSmsStatus.Accepted,"unchanged permission keeps the new event eligible");
   config.RepairAcceptTemplate="Invalid_Name";
   var badTemplate=new RepairOrder {Id=33,CompanyId=1,PartyId=7,Number="RP-0033",Status=RepairStatus.Received};db.RepairOrders.Add(badTemplate);
   await RepairSmsQueue.StageAsync(db,g,config,badTemplate,p,RepairSmsEventType.Registration);var badTemplateEvent=db.RepairSmsNotifications.Last();badTemplateEvent.Id=25;
   config.RepairAcceptTemplate="RepairAccept";
   C(badTemplateEvent.Status==RepairSmsStatus.NotConfigured && !await dispatcher.ProcessOneAsync(),"invalid template is terminal even after configuration is fixed");
   var changedNumber=new RepairOrder {Id=34,CompanyId=1,PartyId=7,Number="RP-0034",Status=RepairStatus.Received};db.RepairOrders.Add(changedNumber);
   await RepairSmsQueue.StageAsync(db,g,config,changedNumber,p,RepairSmsEventType.Registration);var changedNumberEvent=db.RepairSmsNotifications.Last();changedNumberEvent.Id=26;
   changedNumber.Number="RP-34-OLD";calls=g.Calls;await dispatcher.ProcessOneAsync();
   C(changedNumberEvent.Status==RepairSmsStatus.InvalidRepairNumber && g.Calls==calls,"incompatible number is checked again before sending");
   var company2=db.SystemCompanies.Single(c=>c.Id==2);company2.IsActive=false;
   C((await CompanySmsServicePolicy.GetEnabledRevisionAsync(db,2,SmsModuleCatalog.ShopRepairs)) is null,
      "inactive company fails closed even with its module enabled");
   company2.IsActive=true;
   Console.WriteLine($"{count} checks passed");
 }
}
class Fake : IKavenegarSmsGateway {
 public bool IsEnabled {get;set;}=true; public bool IsConfigured {get;set;}=true; public bool IsLookupConfigured {get;set;}=true; public int Calls; public string? Recipient,Token,CompanyName,Template;
 public KavenegarSmsSendResult Result=new(KavenegarSmsSendState.Accepted,42,1,200);
 public bool Throw;
 public TaskCompletionSource<bool>? Hold, Entered;
 public Task<KavenegarSmsSendResult> SendAsync(string r,string m) => throw new Exception("wrong ordinary send path");
 public async Task<KavenegarSmsSendResult> SendLookupAsync(string r,string token,string name,string template) {
   Calls++;Recipient=r;Token=token;CompanyName=name;Template=template;if(Throw) throw new Exception("fake failure");
   Entered?.TrySetResult(true);if(Hold is not null) await Hold.Task;return Result;
 }
}

class TestPermissions(params string[] permissions) : IEffectivePermissions {
 public Task<HashSet<string>> GetAsync(ClaimsPrincipal user) => Task.FromResult(permissions.ToHashSet(StringComparer.OrdinalIgnoreCase));
 public Task<bool> IsLegacyAdminAsync(ClaimsPrincipal user) => Task.FromResult(false);
 public Task<bool> CanSeeModuleAsync(ClaimsPrincipal user,string module) => Task.FromResult(false);
 public Task<List<int>> MyRoleIdsAsync(int userId) => Task.FromResult(new List<int>());
}
