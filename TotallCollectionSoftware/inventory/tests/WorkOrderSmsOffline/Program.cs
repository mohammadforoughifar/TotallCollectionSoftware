using System.Globalization;
using Inventory.Api.Data;
using Inventory.Api.Infrastructure;
using Inventory.Api.Services;
using Inventory.Api.Services.ItAssets;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.Extensions.Options;

static class Program
{
    static int checks, nextLog, nextJob;
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        checks++; Console.WriteLine("PASS: " + message);
    }
    static WorkOrderLog Log(AppDbContext db, WorkOrder order, string action)
    {
        var log = new WorkOrderLog { Id=++nextLog, OrderId=order.Id, Action=action }; db.WorkOrderLogs.Add(log); return log;
    }
    static void Ids(AppDbContext db)
    {
        foreach (var job in db.WorkOrderSmsNotifications.Where(j=>j.Id==0)) job.Id=++nextJob;
    }
    static WorkOrder Order(AppDbContext db, int id, int? company, int owner, DateTime due, params int[] assigned)
    {
        var order=new WorkOrder {Id=id,CompanyId=company,OwnerUserId=owner,Number=$"WO/1405/{id:0000}",Title="سرویس دستگاه",DueAt=due};
        db.WorkOrders.Add(order);
        foreach(var user in assigned) db.WorkOrderAssignees.Add(new WorkOrderAssignee {OrderId=id,UserId=user});
        return order;
    }
    static async Task Main()
    {
        var db=new AppDbContext(); var gateway=new FakeGateway(); var options=new KavenegarSmsOptions();
        var dispatcher=new WorkOrderSmsDispatcher(db,gateway,new OptionsWrapper<KavenegarSmsOptions>(options));
        var service=new WorkOrderSmsService(db,gateway,new OptionsWrapper<KavenegarSmsOptions>(options),new FakeLogger<WorkOrderSmsService>());
        var due=new PersianCalendar().ToDateTime(1405,8,27,14,30,0,0);
        var requestKey=WorkOrderRequestIdentity.Key("create",1,2,0," retry-id ");
        Check(requestKey.Length==64 && requestKey==WorkOrderRequestIdentity.Key("create",1,2,0,"retry-id"),
            "request ID is trimmed and stored only as a bounded hash");
        Check(requestKey!=WorkOrderRequestIdentity.Key("create",2,2,0,"retry-id") &&
              requestKey!=WorkOrderRequestIdentity.Key("create",1,3,0,"retry-id") &&
              requestKey!=WorkOrderRequestIdentity.Key("edit",1,2,0,"retry-id") &&
              requestKey!=WorkOrderRequestIdentity.Key("create",1,2,3,"retry-id"),
            "request ID is scoped to company, actor, operation and order");
        var payload=WorkOrderRequestIdentity.Fingerprint("reply",new {Done=true,Text="متن اول"});
        Check(WorkOrderRequestIdentity.SamePayload(payload,WorkOrderRequestIdentity.Fingerprint("reply",new {Done=true,Text="متن اول"})) &&
              !WorkOrderRequestIdentity.SamePayload(payload,WorkOrderRequestIdentity.Fingerprint("reply",new {Done=true,Text="متن دوم"})) &&
              !WorkOrderRequestIdentity.SamePayload(payload,WorkOrderRequestIdentity.Fingerprint("assign",new {Done=true,Text="متن اول"})) &&
              !WorkOrderRequestIdentity.SamePayload(null,null),
            "same request content matches; changed content, different operation and absent hashes do not");
        Check(WorkOrderRequestIdentity.ReplyIsUnchanged(due, true, "انجام شد", true, " انجام شد ") &&
              WorkOrderRequestIdentity.ReplyIsUnchanged(due, false, null, false, " ") &&
              !WorkOrderRequestIdentity.ReplyIsUnchanged(null, true, "انجام شد", true, "انجام شد") &&
              !WorkOrderRequestIdentity.ReplyIsUnchanged(due, true, "انجام شد", false, "انجام شد") &&
              !WorkOrderRequestIdentity.ReplyIsUnchanged(due, true, "انجام شد", true, "متن تازه"),
            "only an active, identical reply is a no-op; a rejection or changed response can be resubmitted");
        Check(WorkOrderSmsPolicy.DeadlineToken(due)=="۲۷آبان۱۴۰۵ساعت۱۴و۳۰", "exact Persian no-space deadline token");
        Check(WorkOrderSmsPolicy.DeadlineToken(due.AddSeconds(1))=="۲۷آبان۱۴۰۵ساعت۱۴و۳۰و۰۱ثانیه" &&
              WorkOrderSmsPolicy.DeadlineToken(due.AddTicks(1))=="۲۷آبان۱۴۰۵ساعت۱۴و۳۰و۰۰ممیز۰۰۰۰۰۰۱ثانیه" &&
              WorkOrderSmsPolicy.DeadlineToken(due.AddTicks(1234000))=="۲۷آبان۱۴۰۵ساعت۱۴و۳۰و۰۰ممیز۱۲۳۴ثانیه",
            "seconds and fractional seconds are transmitted exactly, never silently truncated");
        Check(WorkOrderSmsPolicy.DeadlineToken(DateTime.MinValue) is null, "dates outside the Persian calendar are rejected");
        Check(WorkOrderSmsPolicy.DeadlineToken(due, 1, 3)=="۲۷آبان۱۴۰۵ساعت۱۴و۳۰تکرارروزانه۳نوبت" &&
              WorkOrderSmsPolicy.DeadlineToken(due, 2, 12)=="۲۷آبان۱۴۰۵ساعت۱۴و۳۰تکرارهفتگی۱۲نوبت" &&
              WorkOrderSmsPolicy.DeadlineToken(due, 3, 2)=="۲۷آبان۱۴۰۵ساعت۱۴و۳۰تکرارماهانه۲نوبت" &&
              WorkOrderSmsPolicy.DeadlineToken(due, 1, 1)=="۲۷آبان۱۴۰۵ساعت۱۴و۳۰",
            "the first deadline and daily/weekly/monthly series summary fit in the single no-space token");
        Check(WorkOrderSmsPolicy.TitleToken("سرویس دستگاه") == "سرویس دستگاه" &&
              WorkOrderSmsPolicy.TitleToken(new string('ا',100)) == new string('ا',100),
            "compatible titles including the length boundary are kept exactly");
        var longTitle = new string('ا',101);
        Check(WorkOrderSmsPolicy.TitleToken(longTitle) == new string('ا',97) + "..." && longTitle.Length == 101,
            "long title is shortened only in the SMS token with three dots");
        var eightSpaces = "یک دو سه چهار پنج شش هفت هشت نه";
        var manyWords = eightSpaces + " ده";
        Check(WorkOrderSmsPolicy.TitleToken(eightSpaces) == eightSpaces &&
              WorkOrderSmsPolicy.TitleToken(manyWords) == eightSpaces + "..." && manyWords.EndsWith("ده"),
            "eight spaces fit; the ninth triggers an explicit ellipsis without changing the title");
        var emojiBoundary = new string('ا',96) + "😀" + "abc";
        Check(WorkOrderSmsPolicy.TitleToken(emojiBoundary) == new string('ا',96) + "...",
            "title shortening never splits a Unicode text element");
        Check(WorkOrderSmsPolicy.TitleToken("عنوان_ناسازگار") is null &&
              WorkOrderSmsPolicy.TitleToken("عنوان\nدستور") is null &&
              WorkOrderSmsPolicy.TitleToken(new string('ا',101) + "_") is null &&
              WorkOrderSmsPolicy.TitleToken("نام\ud800") is null,
            "invalid characters, including beyond the truncation boundary and malformed Unicode, are rejected");
        var number=WorkOrderSmsPolicy.NumberTokens("WO/1405/000123");
        Check(number?.Year=="1405" && number?.Serial=="000123", "exact number components preserve leading zeros");
        foreach(var invalid in new[]{"WO/1405/1/2","WO/۱۴۰۵/123","WO/1405/X","WO/405/123","X/1405/123","WO/1405/"})
            Check(WorkOrderSmsPolicy.NumberTokens(invalid) is null,"invalid work-order number refused: "+invalid);
        Check(KavenegarSmsValidation.NormalizeMobile("+98 912 345 6789")=="09123456789" &&
              KavenegarSmsValidation.NormalizeMobile("۰۹۱۲-۳۴۵-۶۷۸۹")=="09123456789" &&
              KavenegarSmsValidation.NormalizeMobile("0912,0913") is null &&
              KavenegarSmsValidation.NormalizeMobile("02112345678") is null,"safe normalization and phone deduplication");
        Check(options.WorkOrderRegistrationTemplate=="WorkOrderRegistration" &&
              options.WorkOrderReferralTemplate=="WorkOrderReferral" &&
              options.WorkOrderReplyTemplate=="WorkOrderReply" && !options.SdkSendEnabled,
            "proposed names for the reported approved templates are configured while the SDK remains default off");
        // Simulate an unconfigured installation to verify that blank names are terminal
        // and are not replayed if an operator later supplies the approved names.
        options.WorkOrderRegistrationTemplate=""; options.WorkOrderReferralTemplate=""; options.WorkOrderReplyTemplate="";
        Check(WorkOrderSmsPolicy.MatchesLog(WorkOrderSmsEventType.Registration,"Created") &&
              WorkOrderSmsPolicy.MatchesLog(WorkOrderSmsEventType.Referral,"Edited") &&
              WorkOrderSmsPolicy.MatchesLog(WorkOrderSmsEventType.Referral,"AssigneesAdded") &&
              WorkOrderSmsPolicy.MatchesLog(WorkOrderSmsEventType.Reply,"Reply") &&
              !WorkOrderSmsPolicy.MatchesLog(WorkOrderSmsEventType.Registration,"Edited") &&
              !WorkOrderSmsPolicy.MatchesLog(WorkOrderSmsEventType.Referral,"Created") &&
              !WorkOrderSmsPolicy.MatchesLog(WorkOrderSmsEventType.Reply,"SubReply"),
            "registration, referral and reply match only their approved business events");
        Check(WorkOrderSmsPolicy.RequiresDeadline(WorkOrderSmsEventType.Registration) &&
              WorkOrderSmsPolicy.RequiresDeadline(WorkOrderSmsEventType.Referral) &&
              !WorkOrderSmsPolicy.RequiresDeadline(WorkOrderSmsEventType.Reply),
            "registration and referral require the deadline, reply does not");
        Check(WorkOrderSmsPolicy.InitialStatus("09120000003",true,true,true,true,"شرکت الف","WO/1405/123","عنوان",DateTime.MinValue,WorkOrderSmsEventType.Registration)==WorkOrderSmsStatus.InvalidDeadline &&
              WorkOrderSmsPolicy.InitialStatus("09120000003",true,true,true,true,"شرکت الف","WO/1405/123","عنوان",DateTime.MinValue,WorkOrderSmsEventType.Referral)==WorkOrderSmsStatus.InvalidDeadline &&
              WorkOrderSmsPolicy.InitialStatus("09120000003",true,true,true,true,"شرکت الف","WO/1405/123","عنوان",DateTime.MinValue,WorkOrderSmsEventType.Reply)==WorkOrderSmsStatus.Pending,
            "only registration and referral reject an unavailable deadline");
        db.SystemCompanies.Add(new SystemCompany {Id=1,Name="شرکت الف"});
        db.SystemCompanies.Add(new SystemCompany {Id=2,Name="شرکت ب"});
        var permission=new CompanySmsSetting {CompanyId=1,ModuleKey=SmsModuleCatalog.WorkOrders,Enabled=true};
        db.CompanySmsSettings.Add(permission);
        db.CompanySmsSettings.Add(new CompanySmsSetting {CompanyId=2,ModuleKey=SmsModuleCatalog.ShopRepairs,Enabled=true});
        foreach(var u in new[]{
            new User{Id=1,Username="owner",Mobile="۰۹۱۲۳۴۵۶۷۸۹"},
            new User{Id=2,Username="same",Mobile="+98 912 345 6789"},
            new User{Id=3,Username="other",Mobile="09120000003"},
            new User{Id=4,Username="foreign",Mobile="09120000004"},
            new User{Id=5,Username="missing",Mobile="02112345678"},
            new User{Id=6,Username="shop2",Mobile="09120000006"},
            new User{Id=9,Username="foreignShared",Mobile="09120000010"},
            new User{Id=10,Username="eligibleShared",Mobile="09120000010"},
            new User{Id=11,Username="legacy",Mobile="09120000011"},
            new User{Id=12,Username="admin",Role="Admin",Mobile="09120000012"}}) db.Users.Add(u);
        foreach(var uid in new[]{1,2,3,5,10}) db.UserCompanyAccesses.Add(new UserCompanyAccess {UserId=uid,CompanyId=1});
        foreach(var uid in new[]{4,6,9}) db.UserCompanyAccesses.Add(new UserCompanyAccess {UserId=uid,CompanyId=2});
        db.SystemUsers.Add(new SystemUser {Username="legacy",CompanyId=1});
        Check(await WorkOrderSmsCompanyAccess.HasAccessAsync(db,db.Users.Single(u=>u.Id==11),1),"legacy single company respected");
        Check(await WorkOrderSmsCompanyAccess.HasAccessAsync(db,db.Users.Single(u=>u.Id==12),1),"unrestricted legacy admin fallback");
        db.UserCompanyAccesses.Add(new UserCompanyAccess {UserId=12,CompanyId=2});
        Check(!await WorkOrderSmsCompanyAccess.HasAccessAsync(db,db.Users.Single(u=>u.Id==12),1),"admin with company rows is restricted like middleware");
        Check((await CompanySmsServicePolicy.GetEnabledRevisionAsync(db,2,SmsModuleCatalog.WorkOrders)) is null,
            "company 2 repair permission does not enable work orders");

        var original=Order(db,1,1,1,due,1,2,3,4,5,9,10);
        var first=Log(db,original,"Created");
        var targets=new[]{1,2,3,4,5,9,10};
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,original,first,targets,WorkOrderSmsEventType.Registration); Ids(db);
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,original,first,targets,WorkOrderSmsEventType.Registration);
        var initial=db.WorkOrderSmsNotifications.Where(n=>n.EventLogId==first.Id).ToList();
        Check(initial.Count==5,"one event per normalized shared phone and per invalid user, no duplicate stage");
        Check(initial.Single(n=>n.RecipientUserId==1).Status==WorkOrderSmsStatus.NotConfigured &&
              initial.Single(n=>n.RecipientUserId==3).Status==WorkOrderSmsStatus.NotConfigured,
            "blank templates are terminal and include creator if selected");
        Check(initial.Single(n=>n.RecipientUserId==4).Status==WorkOrderSmsStatus.NoCompanyAccess &&
              initial.Single(n=>n.RecipientUserId==5).Status==WorkOrderSmsStatus.InvalidMobile,
            "foreign company and landline cannot receive SMS");
        Check(initial.Any(n=>n.RecipientUserId==10) && initial.All(n=>n.RecipientUserId!=9),
            "eligible user sharing a phone wins over an ineligible user");
        options.WorkOrderRegistrationTemplate="WorkOrderRegistration";
        options.WorkOrderReferralTemplate="WorkOrderReferral";
        options.WorkOrderReplyTemplate="WorkOrderReply";
        Check(!await dispatcher.ProcessOneAsync() && gateway.Calls==0,"configuring templates never replays earlier events");
        var disabledOrder=Order(db,31,1,1,due,3);
        var disabledCreation=Log(db,disabledOrder,"Created");
        var disabledReferral=Log(db,disabledOrder,"Edited");
        var disabledReply=Log(db,disabledOrder,"Reply");
        gateway.IsEnabled=false;
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,disabledOrder,disabledCreation,new[]{3},WorkOrderSmsEventType.Registration);
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,disabledOrder,disabledReferral,new[]{3},WorkOrderSmsEventType.Referral);
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,disabledOrder,disabledReply,new[]{1},WorkOrderSmsEventType.Reply); Ids(db);
        Check(db.WorkOrderSmsNotifications.Count(n=>n.WorkOrderId==31 && n.Status==WorkOrderSmsStatus.Disabled)==3 &&
              !await dispatcher.ProcessOneAsync() && gateway.Calls==0,
            "approved names alone cannot bypass the disabled SDK for registration, referral or reply");
        gateway.IsEnabled=true;
        Check(!await dispatcher.ProcessOneAsync() && gateway.Calls==0,
            "enabling the SDK later does not replay events created while disabled");

        var second=Order(db,2,1,1,due,1,2,3); var secondLog=Log(db,second,"Created");
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,second,secondLog,new[]{1,2,3},WorkOrderSmsEventType.Registration); Ids(db);
        var pending=db.WorkOrderSmsNotifications.Where(n=>n.EventLogId==secondLog.Id).ToList();
        Check(pending.Count==2 && pending.All(n=>n.Status==WorkOrderSmsStatus.Pending) && gateway.Calls==0,
            "stage queues one per phone without network calls");
        await dispatcher.ProcessOneAsync();
        Check(gateway.Calls==1 && gateway.Recipient=="09123456789" && gateway.Year=="1405" && gateway.Serial=="0002" &&
              gateway.Title=="سرویس دستگاه" && gateway.Deadline=="۲۷آبان۱۴۰۵ساعت۱۴و۳۰" && gateway.Company=="شرکت الف" && gateway.Template=="WorkOrderRegistration",
            "creation uses creator's exact order, current deadline and company");
        Check(pending[0].Status==WorkOrderSmsStatus.Accepted && pending[0].Recipient is null,
            "provider acceptance is not delivery and stored phone is cleared");
        gateway.Result=new(KavenegarSmsSendState.Delivered,43,10,200); await dispatcher.ProcessOneAsync();
        Check(pending[1].Status==WorkOrderSmsStatus.Delivered && gateway.Calls==2,"delivered is a distinct state");
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,second,secondLog,new[]{1,2,3},WorkOrderSmsEventType.Registration);
        Check(db.WorkOrderSmsNotifications.Count(n=>n.EventLogId==secondLog.Id)==2 && !await dispatcher.ProcessOneAsync(),
            "completed operation cannot be requeued after phone deletion");

        gateway.Result=new(KavenegarSmsSendState.Accepted,44,1,200);
        var sub=Order(db,30,1,1,due,3); sub.ParentOrderId=2;
        var subCreated=Log(db,sub,"Created");
        var mismatched=false;
        try { await WorkOrderSmsQueue.StageAsync(db,gateway,options,sub,subCreated,new[]{3},WorkOrderSmsEventType.Referral); }
        catch (InvalidOperationException) { mismatched=true; }
        Check(mismatched && !db.WorkOrderSmsNotifications.Any(n=>n.EventLogId==subCreated.Id),
            "a new sub-order cannot accidentally stage the referral template");
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,sub,subCreated,new[]{3},WorkOrderSmsEventType.Registration); Ids(db);
        await dispatcher.ProcessOneAsync();
        Check(gateway.Template=="WorkOrderRegistration" && gateway.Title=="سرویس دستگاه" &&
              gateway.Deadline=="۲۷آبان۱۴۰۵ساعت۱۴و۳۰" &&
              db.WorkOrderSmsNotifications.Count(n=>n.EventLogId==subCreated.Id)==1,
            "a new sub-order receives one registration with title and deadline");

        options.WorkOrderReferralTemplate="";
        var unavailable=Log(db,second,"Edited");
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,second,unavailable,new[]{3},WorkOrderSmsEventType.Referral); Ids(db);
        var unavailableJob=db.WorkOrderSmsNotifications.Single(n=>n.EventLogId==unavailable.Id);
        Check(unavailableJob.Status==WorkOrderSmsStatus.NotConfigured && unavailableJob.Recipient is null &&
              !await dispatcher.ProcessOneAsync(),
            "a missing referral template cannot fall back to the registration template");
        options.WorkOrderReferralTemplate="WorkOrderReferral";
        Check(!await dispatcher.ProcessOneAsync(), "later referral-template configuration cannot replay the earlier event");

        var edited=Log(db,second,"Edited");
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,second,edited,new[]{3,4},WorkOrderSmsEventType.Referral); Ids(db);
        var editedJobs=db.WorkOrderSmsNotifications.Where(n=>n.EventLogId==edited.Id).ToList();
        await dispatcher.ProcessOneAsync();
        Check(editedJobs.Count==1 && editedJobs[0].RecipientUserId==3 && gateway.Template=="WorkOrderReferral" &&
              gateway.Title=="سرویس دستگاه" && gateway.Deadline=="۲۷آبان۱۴۰۵ساعت۱۴و۳۰",
            "an edit referral sends its own template only to an actually assigned new recipient");
        var workflow=Log(db,second,"AssigneesAdded");
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,second,workflow,new[]{1,2},WorkOrderSmsEventType.Referral); Ids(db);
        await dispatcher.ProcessOneAsync();
        Check(db.WorkOrderSmsNotifications.Count(n=>n.EventLogId==workflow.Id)==1 && gateway.Template=="WorkOrderReferral",
            "workflow additions use referral and shared phones receive only one message");

        var child=Order(db,3,1,3,due,2); var reply=Log(db,child,"Reply");
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,child,reply,new[]{1,3},WorkOrderSmsEventType.Reply); Ids(db);
        var replyJobs=db.WorkOrderSmsNotifications.Where(n=>n.EventLogId==reply.Id).ToList();
        Check(replyJobs.Count==1 && replyJobs[0].RecipientUserId==3,"reply stages only immediate order owner, not ancestor");
        await dispatcher.ProcessOneAsync();
        Check(gateway.Recipient=="09120000003" && gateway.Template=="WorkOrderReply" && gateway.Deadline is null && gateway.Title=="سرویس دستگاه" && gateway.Serial=="0003",
            "reply sends title, number and company to immediate owner, not reply body");
        var self=Order(db,4,1,1,due,1);var selfReply=Log(db,self,"Reply");
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,self,selfReply,new[]{1},WorkOrderSmsEventType.Reply); Ids(db);
        await dispatcher.ProcessOneAsync();
        Check(gateway.Recipient=="09123456789","owner receives their own reply event when owner is also recipient");

        var otherCompany=Order(db,5,2,6,due,6);var otherLog=Log(db,otherCompany,"Created");
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,otherCompany,otherLog,new[]{6},WorkOrderSmsEventType.Registration); Ids(db);
        var otherJob=db.WorkOrderSmsNotifications.Single(n=>n.EventLogId==otherLog.Id);
        Check(otherJob.Status==WorkOrderSmsStatus.ModuleDisabled && otherJob.Recipient is null,"other company's repair opt-in does not permit this SMS");
        var p2=new CompanySmsSetting {CompanyId=2,ModuleKey=SmsModuleCatalog.WorkOrders,Enabled=true};db.CompanySmsSettings.Add(p2);
        Check(!await dispatcher.ProcessOneAsync(),"later company enable does not revive disabled events");

        async Task<WorkOrderSmsNotification> Stage(int id,int company,int owner,string action,int target)
        {
            var o=Order(db,id,company,owner,due,target);var l=Log(db,o,action);
            var kind=action switch {
                "Created" => WorkOrderSmsEventType.Registration,
                "Edited" or "AssigneesAdded" => WorkOrderSmsEventType.Referral,
                "Reply" => WorkOrderSmsEventType.Reply,
                _ => throw new InvalidOperationException("Unknown test event")
            };
            await WorkOrderSmsQueue.StageAsync(db,gateway,options,o,l,new[]{target},kind); Ids(db);
            return db.WorkOrderSmsNotifications.Single(n=>n.EventLogId==l.Id);
        }
        var uncertain=await Stage(6,1,1,"Created",3); gateway.Result=new(KavenegarSmsSendState.Unknown);
        var before=gateway.Calls; await dispatcher.ProcessOneAsync();
        Check(uncertain.Status==WorkOrderSmsStatus.Unknown && uncertain.Recipient is null && gateway.Calls==before+1 &&
              !await dispatcher.ProcessOneAsync(),"ambiguous provider response never retried");
        var abandoned=await Stage(7,1,1,"Created",3);abandoned.Status=WorkOrderSmsStatus.Sending;abandoned.AttemptStartedAtUtc=DateTime.UtcNow.AddMinutes(-11);
        await dispatcher.MarkAbandonedUnknownAsync(DateTime.UtcNow);
        Check(abandoned.Status==WorkOrderSmsStatus.Unknown && abandoned.Recipient is null && !await dispatcher.ProcessOneAsync(),
            "abandoned in-flight event becomes unknown, not pending");
        var thrown=await Stage(8,1,1,"Created",3);gateway.Throw=true;await dispatcher.ProcessOneAsync();gateway.Throw=false;
        Check(thrown.Status==WorkOrderSmsStatus.Unknown && !await dispatcher.ProcessOneAsync(),"provider exception is unknown, never retried");
        var moved=await Stage(9,1,1,"Created",3);db.Users.Single(u=>u.Id==3).Mobile="09120009999";before=gateway.Calls;
        await dispatcher.ProcessOneAsync();db.Users.Single(u=>u.Id==3).Mobile="09120000003";
        Check(moved.Status==WorkOrderSmsStatus.Cancelled && gateway.Calls==before,"changed mobile cancels event");
        var changed=await Stage(10,1,1,"Created",3);db.WorkOrders.Single(w=>w.Id==10).DueAt=DateTime.MinValue;
        await dispatcher.ProcessOneAsync();
        Check(changed.Status==WorkOrderSmsStatus.InvalidDeadline && gateway.Calls==before,"invalid current deadline is not sent");
        var precise=await Stage(23,1,1,"Created",3);db.WorkOrders.Single(w=>w.Id==23).DueAt=due.AddTicks(1);
        gateway.Result=new(KavenegarSmsSendState.Accepted,43,1,200);
        await dispatcher.ProcessOneAsync();
        Check(precise.Status==WorkOrderSmsStatus.Accepted && gateway.Deadline=="۲۷آبان۱۴۰۵ساعت۱۴و۳۰و۰۰ممیز۰۰۰۰۰۰۱ثانیه",
            "a changed sub-minute deadline is sent exactly");
        before=gateway.Calls;
        var foreign=await Stage(11,1,1,"Created",3);foreign.CompanyId=2;await dispatcher.ProcessOneAsync();
        Check(foreign.Status==WorkOrderSmsStatus.Cancelled && gateway.Calls==before,"cross-company job cannot send");
        var removed=await Stage(12,1,1,"Created",3);
        db.WorkOrderAssignees.Local.Remove(db.WorkOrderAssignees.Single(a=>a.OrderId==12&&a.UserId==3));await dispatcher.ProcessOneAsync();
        Check(removed.Status==WorkOrderSmsStatus.Cancelled && gateway.Calls==before,"removed assignee cannot receive queued event");
        var noAccess=await Stage(13,1,1,"Created",3);
        var access=db.UserCompanyAccesses.Single(a=>a.UserId==3&&a.CompanyId==1);db.UserCompanyAccesses.Local.Remove(access);
        await dispatcher.ProcessOneAsync();db.UserCompanyAccesses.Add(access);
        Check(noAccess.Status==WorkOrderSmsStatus.NoCompanyAccess && gateway.Calls==before,"revoked recipient access stops dispatch");
        var oldRevision=await Stage(14,1,1,"Created",3);permission.Enabled=false;permission.Revision++;permission.Enabled=true;permission.Revision++;
        await dispatcher.ProcessOneAsync();
        Check(oldRevision.Status==WorkOrderSmsStatus.ModuleDisabled && gateway.Calls==before,"off/on permission revision cannot revive a pending event");
        var one=await Stage(15,1,1,"Created",3);var two=await Stage(16,2,6,"Created",6);
        await new WorkOrderSmsRevocationHandler(db).CancelPendingAsync(1,DateTime.UtcNow,CancellationToken.None);
        Check(one.Status==WorkOrderSmsStatus.ModuleDisabled && one.Recipient is null && two.Status==WorkOrderSmsStatus.Pending,
            "revocation cancels only pending work orders of the selected company");
        gateway.Result=new(KavenegarSmsSendState.Accepted,45,1,200);await dispatcher.ProcessOneAsync();
        Check(two.Status==WorkOrderSmsStatus.Accepted && gateway.Company=="شرکت ب", "other company's queue remains independent");

        var concurrent=await Stage(17,1,1,"Created",3);gateway.Hold=new();gateway.Entered=new();before=gateway.Calls;
        var inFlight=dispatcher.ProcessOneAsync();await gateway.Entered.Task;
        Check(!await dispatcher.ProcessOneAsync() && gateway.Calls==before+1,"another worker cannot claim a sending job");
        gateway.Hold.SetResult(true);await inFlight;gateway.Hold=null;gateway.Entered=null;
        Check(concurrent.Status==WorkOrderSmsStatus.Accepted && gateway.Calls==before+1,"overlapping workers result in one provider request");

        var longOrder=Order(db,20,1,1,due,3); longOrder.Title = new string('ا',101);
        var longLog=Log(db,longOrder,"Created");
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,longOrder,longLog,new[]{3},WorkOrderSmsEventType.Registration); Ids(db);
        await dispatcher.ProcessOneAsync();
        Check(gateway.Title == new string('ا',97) + "..." && longOrder.Title.Length == 101,
            "worker sends the ellipsized assignment title without changing the stored order title");
        var longReply=Log(db,longOrder,"Reply");
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,longOrder,longReply,new[]{1},WorkOrderSmsEventType.Reply); Ids(db);
        await dispatcher.ProcessOneAsync();
        Check(gateway.Title == new string('ا',97) + "..." && gateway.Deadline is null && longOrder.Title.Length == 101,
            "reply also uses the ellipsized title without the deadline or a title change");
        var invalidOrder=Order(db,21,1,1,due,3); invalidOrder.Title = "عنوان_ناسازگار";
        var invalidLog=Log(db,invalidOrder,"Created");
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,invalidOrder,invalidLog,new[]{3},WorkOrderSmsEventType.Registration); Ids(db);
        before=gateway.Calls;
        Check(db.WorkOrderSmsNotifications.Single(n=>n.EventLogId==invalidLog.Id).Status==WorkOrderSmsStatus.InvalidTitle &&
              !await dispatcher.ProcessOneAsync() && gateway.Calls==before,
            "an invalid title is visible and never sent or altered");
        var titleChanged=await Stage(22,1,1,"Created",3);
        db.WorkOrders.Single(w=>w.Id==22).Title="عنوان_ناسازگار";
        await dispatcher.ProcessOneAsync();
        Check(titleChanged.Status==WorkOrderSmsStatus.InvalidTitle && gateway.Calls==before,
            "the worker revalidates the current title before contacting the provider");

        var series=Order(db,24,1,1,due,3); series.Recurrence=2; series.RecurrenceSeriesId="series-24";
        var future1=Order(db,25,1,1,due.AddDays(7),3); future1.Recurrence=2; future1.RecurrenceSeriesId=series.RecurrenceSeriesId;
        var future2=Order(db,26,1,1,due.AddDays(14),3); future2.Recurrence=2; future2.RecurrenceSeriesId=series.RecurrenceSeriesId;
        var seriesLog=Log(db,series,"Created"); Log(db,future1,"Scheduled"); Log(db,future2,"Scheduled");
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,series,seriesLog,new[]{3},WorkOrderSmsEventType.Registration); Ids(db);
        before=gateway.Calls; await dispatcher.ProcessOneAsync();
        Check(gateway.Calls==before+1 && !await dispatcher.ProcessOneAsync() &&
              gateway.Deadline=="۲۷آبان۱۴۰۵ساعت۱۴و۳۰تکرارهفتگی۳نوبت" &&
              db.WorkOrderSmsNotifications.Count(n=>n.WorkOrderId==24 || n.WorkOrderId==25 || n.WorkOrderId==26)==1,
            "a recurring series sends one message summarizing its first deadline, cadence and count, not future events");
        var later=Log(db,series,"AssigneesAdded");
        await WorkOrderSmsQueue.StageAsync(db,gateway,options,series,later,new[]{3},WorkOrderSmsEventType.Referral); Ids(db);
        await dispatcher.ProcessOneAsync();
        Check(gateway.Deadline=="۲۷آبان۱۴۰۵ساعت۱۴و۳۰" && gateway.Template=="WorkOrderReferral",
            "later referral uses its own template and does not falsely summarize future occurrences");

        var transactional=Order(db,18,1,1,due,3);var transLog=Log(db,transactional,"Created");var tx=new FakeTransaction(db);
        db.FailNextSave=true;var staged=await service.TryStageAsync(tx,transactional,transLog,new[]{3},WorkOrderSmsEventType.Registration,1);
        Check(!staged && tx.Created && tx.RolledBack && db.WorkOrders.Any(w=>w.Id==18) &&
              !db.WorkOrderSmsNotifications.Any(n=>n.EventLogId==transLog.Id),"savepoint failure skips SMS without removing the business record");
        var brokenTx=new FakeTransaction(db) {FailRollback=true}; db.FailNextSave=true;
        var rollbackFailed=false;
        try { await service.TryStageAsync(brokenTx,transactional,transLog,new[]{3},WorkOrderSmsEventType.Registration,1); }
        catch (InvalidOperationException) { rollbackFailed=true; }
        Check(rollbackFailed && brokenTx.RolledBack, "failed savepoint rollback is propagated so the caller cannot commit partial SMS rows");
        db.WorkOrderSmsNotifications.Local.RemoveAll(n=>n.EventLogId==transLog.Id); // Fake caller aborts its transaction.
        var noSavepoint=new FakeTransaction(db) {SupportsSavepoints=false};
        Check(!await service.TryStageAsync(noSavepoint,transactional,transLog,new[]{3},WorkOrderSmsEventType.Registration,1) && !noSavepoint.Created,
            "unsupported savepoint skips optional staging");
        Check(!await service.TryStageAsync(new FakeTransaction(db),transactional,transLog,new[]{3},WorkOrderSmsEventType.Registration,2),
            "authorized company must match event company");
        db.ActiveCompanyId=2;
        Check(!await service.TryStageAsync(new FakeTransaction(db),transactional,transLog,new[]{3},WorkOrderSmsEventType.Registration,1),
            "a caller-supplied matching company cannot bypass the different active context");
        db.ActiveCompanyId=0;
        Check(!await service.TryStageAsync(new FakeTransaction(db),transactional,transLog,new[]{3},WorkOrderSmsEventType.Registration,1),
            "a fail-closed active-company context cannot stage SMS");
        db.ActiveCompanyId=null;
        Check(!await service.TryStageAsync(new FakeTransaction(db),transactional,transLog,new[]{3},WorkOrderSmsEventType.Registration,1),
            "missing active-company context cannot stage SMS");
        db.ActiveCompanyId=1;
        var legacy=Order(db,19,null,1,due,3);var legacyLog=Log(db,legacy,"Created");
        Check(!await service.TryStageAsync(new FakeTransaction(db),legacy,legacyLog,new[]{3},WorkOrderSmsEventType.Registration,1) &&
              !db.WorkOrderSmsNotifications.Any(n=>n.EventLogId==legacyLog.Id),"null company is never guessed");

        // Execute the actual ActiveCompanyMiddleware with fake HTTP and fake EF. These
        // checks do not replace an ASP.NET/real-EF tenant isolation integration test.
        var tenantDb=new AppDbContext(); var tenantGateway=new FakeGateway();
        var tenantOptions=new KavenegarSmsOptions {WorkOrderRegistrationTemplate="WorkOrderRegistration"};
        var tenantService=new WorkOrderSmsService(tenantDb,tenantGateway,new OptionsWrapper<KavenegarSmsOptions>(tenantOptions),new FakeLogger<WorkOrderSmsService>());
        var tenantDispatcher=new WorkOrderSmsDispatcher(tenantDb,tenantGateway,new OptionsWrapper<KavenegarSmsOptions>(tenantOptions));
        var companyA=new SystemCompany {Id=101,Name="شرکت الف"};
        var companyB=new SystemCompany {Id=102,Name="شرکت ب"};
        tenantDb.SystemCompanies.Add(companyA);tenantDb.SystemCompanies.Add(companyB);
        tenantDb.CompanySmsSettings.Add(new CompanySmsSetting {CompanyId=101,ModuleKey=SmsModuleCatalog.WorkOrders,Enabled=true});
        tenantDb.CompanySmsSettings.Add(new CompanySmsSetting {CompanyId=102,ModuleKey=SmsModuleCatalog.WorkOrders,Enabled=true});
        tenantDb.Users.Add(new User {Id=101,Username="actor",Mobile="09120000101"});
        tenantDb.Users.Add(new User {Id=102,Username="recipient",Mobile="09120000102"});
        var actorA=new UserCompanyAccess {UserId=101,CompanyId=101,Company=companyA,IsDefault=true};
        var actorB=new UserCompanyAccess {UserId=101,CompanyId=102,Company=companyB};
        tenantDb.UserCompanyAccesses.Add(actorA);tenantDb.UserCompanyAccesses.Add(actorB);
        tenantDb.UserCompanyAccesses.Add(new UserCompanyAccess {UserId=102,CompanyId=102,Company=companyB});
        HttpContext Request(string? selected,bool authenticated=true)
        {
            var context=new HttpContext();
            if(selected is not null) context.Request.Headers[ActiveCompanyMiddleware.HeaderName]=new[]{selected};
            if(authenticated) context.User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]{
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,"101"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name,"actor"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role,"Operator")},"test"));
            return context;
        }
        async Task Resolve(HttpContext context)
        {
            var middleware=new ActiveCompanyMiddleware(c => {
                tenantDb.ActiveCompanyId=c.Items.TryGetValue(ActiveCompanyMiddleware.ItemKey,out var value) && value is int id ? id : null;
                return Task.CompletedTask;
            },new FakeLogger<ActiveCompanyMiddleware>());
            await middleware.InvokeAsync(context,tenantDb);
        }
        // A server form selecting company B sends that selection as this request's header,
        // independent of the global company A (covered by CompanyRequestOffline).
        var context=Request("102");await Resolve(context);
        Check(tenantDb.ActiveCompanyId==102,"middleware resolves the separately selected, authorized server company");
        var tenantOrder=Order(tenantDb,40,102,101,due,102);var tenantLog=Log(tenantDb,tenantOrder,"Created");
        Check(await tenantService.TryStageAsync(new FakeTransaction(tenantDb),tenantOrder,tenantLog,new[]{102},WorkOrderSmsEventType.Registration,tenantDb.ActiveCompanyId),
            "matching resolved company can stage its own registration");
        Ids(tenantDb); tenantDb.ActiveCompanyId=null;
        Check(await tenantDispatcher.ProcessOneAsync() && tenantGateway.Company=="شرکت ب" && tenantGateway.Template=="WorkOrderRegistration",
            "background dispatch uses the event company and registration template after middleware-scoped staging");
        context=Request("102");await Resolve(context);
        var deactivatedOrder=Order(tenantDb,42,102,101,due,102);var deactivatedLog=Log(tenantDb,deactivatedOrder,"Created");
        Check(await tenantService.TryStageAsync(new FakeTransaction(tenantDb),deactivatedOrder,deactivatedLog,new[]{102},WorkOrderSmsEventType.Registration,tenantDb.ActiveCompanyId),
            "registration can be staged while its company is active");
        Ids(tenantDb);var deactivatedJob=tenantDb.WorkOrderSmsNotifications.Single(n=>n.EventLogId==deactivatedLog.Id);
        companyB.IsActive=false;tenantDb.ActiveCompanyId=null;var beforeDeactivation=tenantGateway.Calls;
        await tenantDispatcher.ProcessOneAsync();companyB.IsActive=true;
        Check(tenantGateway.Calls==beforeDeactivation && deactivatedJob.Recipient is null &&
              deactivatedJob.Status is WorkOrderSmsStatus.NoCompanyAccess or WorkOrderSmsStatus.ModuleDisabled,
            "deactivating the original company before dispatch prevents the network request");

        tenantDb.UserCompanyAccesses.Local.Remove(actorB);
        context=Request("102");await Resolve(context);
        var forbiddenOrder=Order(tenantDb,41,102,101,due,102);var forbiddenLog=Log(tenantDb,forbiddenOrder,"Created");
        Check(tenantDb.ActiveCompanyId==101 &&
              !await tenantService.TryStageAsync(new FakeTransaction(tenantDb),forbiddenOrder,forbiddenLog,new[]{102},WorkOrderSmsEventType.Registration,tenantDb.ActiveCompanyId) &&
              !tenantDb.WorkOrderSmsNotifications.Any(n=>n.EventLogId==forbiddenLog.Id),
            "middleware fallback after an unauthorized company header cannot stage the foreign order's SMS");
        tenantDb.UserCompanyAccesses.Add(actorB);companyB.IsActive=false;
        context=Request("102");await Resolve(context);
        Check(tenantDb.ActiveCompanyId==101 &&
              !await tenantService.TryStageAsync(new FakeTransaction(tenantDb),forbiddenOrder,forbiddenLog,new[]{102},WorkOrderSmsEventType.Registration,tenantDb.ActiveCompanyId),
            "middleware ignores an inactive company even when an access row exists");
        companyB.IsActive=true;
        tenantDb.UserCompanyAccesses.Local.Remove(actorA);tenantDb.UserCompanyAccesses.Local.Remove(actorB);
        context=Request("102");await Resolve(context);
        Check(tenantDb.ActiveCompanyId==0 &&
              !await tenantService.TryStageAsync(new FakeTransaction(tenantDb),forbiddenOrder,forbiddenLog,new[]{102},WorkOrderSmsEventType.Registration,102),
            "an authenticated user with no active company access fails closed even if the caller supplies the resource company");
        context=Request("102",authenticated:false);await Resolve(context);
        Check(tenantDb.ActiveCompanyId is null &&
              !await tenantService.TryStageAsync(new FakeTransaction(tenantDb),forbiddenOrder,forbiddenLog,new[]{102},WorkOrderSmsEventType.Registration,102),
            "an unauthenticated context cannot supply an authorized company to the staging service");
        Console.WriteLine($"Work-order SMS offline: {checks} checks passed.");
    }
}

sealed class FakeLogger<T>:ILogger<T> {public void LogWarning(string format,params object[] args) {}}
sealed class FakeGateway:IKavenegarSmsGateway
{
    public bool IsEnabled {get;set;}=true; public bool IsConfigured {get;set;}=true; public bool IsLookupConfigured {get;set;}=true;
    public int Calls; public string? Recipient,Year,Serial,Company,Title,Deadline,Template; public bool Throw;
    public KavenegarSmsSendResult Result=new(KavenegarSmsSendState.Accepted,42,1,200);
    public TaskCompletionSource<bool>? Hold,Entered;
    public Task<KavenegarSmsSendResult> SendAsync(string recipient,string message)=>throw new Exception("wrong direct path");
    public Task<KavenegarSmsSendResult> SendLookupAsync(string recipient,string token,string company,string template)=>throw new Exception("wrong repair path");
    public Task<KavenegarSmsSendResult> SendLookupWithToken20Async(string recipient,string token,string company,string token20,string template)=>throw new Exception("wrong repair path");
    public async Task<KavenegarSmsSendResult> SendWorkOrderLookupAsync(string recipient,string year,string serial,string company,string title,string? deadline,string template)
    {
        Calls++;Recipient=recipient;Year=year;Serial=serial;Company=company;Title=title;Deadline=deadline;Template=template;
        if(Throw) throw new Exception("ambiguous test error");
        Entered?.TrySetResult(true);if(Hold is not null) await Hold.Task;return Result;
    }
}
