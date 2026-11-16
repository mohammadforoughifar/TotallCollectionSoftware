"""Offline SQLite DDL/concurrency and source contracts; never contacts an SMS provider."""
from pathlib import Path
import re
import sqlite3
import tempfile
import threading
import unittest

ROOT = Path(__file__).resolve().parents[2]

def read(name):
    return (ROOT / name).read_text(encoding='utf-8')

SCHEMA = read('src/Inventory.Api/Data/WorkOrderSmsSchemaV1.cs')
SQLITE = re.search(r'public const string Sqlite = @"(.*?)";', SCHEMA, re.S).group(1)

def setup(db):
    db.execute('PRAGMA foreign_keys=ON')
    db.executescript('''
        CREATE TABLE WorkOrders (Id INTEGER PRIMARY KEY, CompanyId INTEGER);
        CREATE TABLE WorkOrderLogs (Id INTEGER PRIMARY KEY, OrderId INTEGER NOT NULL,
          Action TEXT NOT NULL, CreatedAt TEXT NOT NULL);
        INSERT INTO WorkOrders VALUES (1,1),(2,2);
        INSERT INTO WorkOrderLogs VALUES (1,1,'Created','2026-01-01'),(2,2,'Created','2026-01-01');
    ''')
    # Match EnsureAsync: inspect each column before the additive ALTER.
    for col in ('RequestKey', 'PayloadFingerprint'):
        if col not in [r[1] for r in db.execute('PRAGMA table_info(WorkOrderLogs)')]:
            db.execute(f'ALTER TABLE WorkOrderLogs ADD COLUMN [{col}] TEXT NULL')
    db.executescript(SQLITE)
    db.executescript(SQLITE)

class WorkOrderSmsSqliteTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        setup(self.db)

    def tearDown(self):
        self.db.close()

    def event(self, order, log, user, phone_key, company=1, status=1, recipient='09123456789'):
        self.db.execute('''INSERT INTO WorkOrderSmsNotifications
            (CompanyId,WorkOrderId,EventLogId,RecipientUserId,RecipientKey,EventType,Status,PermissionRevision,Recipient,CreatedAtUtc)
            VALUES (?,?,?,?,?,1,?,1,?,'now')''', (company,order,log,user,phone_key,status,recipient))

    def test_schema_is_additive_and_never_enqueues_history(self):
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM WorkOrderSmsNotifications').fetchone()[0], 0)
        self.assertEqual(self.db.execute('SELECT RequestKey,PayloadFingerprint FROM WorkOrderLogs WHERE Id=1').fetchone(), (None,None))
        self.assertIn('REFERENCES [WorkOrderLogs]', SQLITE)
        self.assertNotIn('INSERT INTO', SQLITE.upper())

    def test_one_normalized_phone_per_operation_even_after_completion(self):
        self.event(1,1,1,'same-phone')
        with self.assertRaises(sqlite3.IntegrityError): self.event(1,1,2,'same-phone')
        self.db.execute("UPDATE WorkOrderSmsNotifications SET Status=3,Recipient=NULL WHERE EventLogId=1")
        with self.assertRaises(sqlite3.IntegrityError): self.event(1,1,2,'same-phone')
        self.db.execute("INSERT INTO WorkOrderLogs (Id,OrderId,Action,CreatedAt) VALUES (3,1,'Edited','now')")
        self.event(1,3,2,'same-phone')  # A distinct, later business operation.
        with self.assertRaises(sqlite3.IntegrityError): self.event(99,1,3,'foreign-order')
        with self.assertRaises(sqlite3.IntegrityError): self.event(1,99,3,'foreign-log')
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM WorkOrderSmsNotifications').fetchone()[0],2)

    def test_durable_request_key_is_unique_but_legacy_rows_remain_null(self):
        self.db.execute("UPDATE WorkOrderLogs SET RequestKey='hashed-request' WHERE Id=1")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("UPDATE WorkOrderLogs SET RequestKey='hashed-request' WHERE Id=2")
        self.assertIsNone(self.db.execute('SELECT RequestKey FROM WorkOrderLogs WHERE Id=2').fetchone()[0])
        indexes = {row[1] for row in self.db.execute('PRAGMA index_list(WorkOrderLogs)')}
        self.assertIn('IX_WorkOrderLogs_PayloadFingerprint_CreatedAt', indexes)

    def test_concurrent_identical_request_keys_cannot_commit_two_business_events(self):
        # Exercise the real SQLite constraints with separate connections. This models the
        # serializable write boundary; it is not a test of EF or SQL Server.
        with tempfile.TemporaryDirectory() as temp:
            path = str(Path(temp) / 'requests.sqlite')
            db = sqlite3.connect(path); setup(db); db.commit(); db.close()
            barrier=threading.Barrier(2); results=[]; errors=[]
            def create(order_id):
                conn=sqlite3.connect(path, timeout=10)
                try:
                    barrier.wait()
                    conn.execute('BEGIN IMMEDIATE')
                    existing=conn.execute("SELECT OrderId,PayloadFingerprint FROM WorkOrderLogs WHERE RequestKey='same-key'").fetchone()
                    if existing:
                        results.append(('duplicate',existing))
                    else:
                        conn.execute('INSERT INTO WorkOrders VALUES (?,1)',(order_id,))
                        conn.execute("INSERT INTO WorkOrderLogs (OrderId,Action,CreatedAt,RequestKey,PayloadFingerprint) VALUES (?,'Created','now','same-key','same-payload')",(order_id,))
                        results.append(('created',order_id))
                    conn.commit()
                except Exception as ex:
                    errors.append(ex); conn.rollback()
                finally: conn.close()
            threads=[threading.Thread(target=create,args=(i,)) for i in (3,4)]
            for t in threads: t.start()
            for t in threads: t.join()
            self.assertEqual(errors,[])
            self.assertEqual(sorted(r[0] for r in results),['created','duplicate'])
            with sqlite3.connect(path) as check:
                self.assertEqual(check.execute("SELECT COUNT(*) FROM WorkOrderLogs WHERE RequestKey='same-key'").fetchone()[0],1)
                self.assertEqual(check.execute('SELECT COUNT(*) FROM WorkOrders').fetchone()[0],3)

    def test_savepoint_failure_does_not_rollback_business_rows(self):
        self.db.commit()
        self.db.execute('BEGIN')
        self.db.execute('INSERT INTO WorkOrders VALUES (3,1)')
        self.db.execute("INSERT INTO WorkOrderLogs (Id,OrderId,Action,CreatedAt,RequestKey) VALUES (3,3,'Created','now','request-3')")
        self.db.execute('SAVEPOINT WorkOrderSmsStage')
        self.event(3,3,1,'phone')
        with self.assertRaises(sqlite3.IntegrityError): self.event(3,3,2,'phone')
        self.db.execute('ROLLBACK TO WorkOrderSmsStage')
        self.db.execute('RELEASE WorkOrderSmsStage')
        self.db.commit()
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM WorkOrders WHERE Id=3').fetchone()[0],1)
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM WorkOrderLogs WHERE RequestKey=?',('request-3',)).fetchone()[0],1)
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM WorkOrderSmsNotifications WHERE WorkOrderId=3').fetchone()[0],0)

    def test_revocation_only_pending_events_of_selected_company(self):
        self.event(1,1,1,'a',1,1); self.event(2,2,2,'b',2,1); self.event(1,1,3,'c',1,2)
        self.db.execute('UPDATE WorkOrderSmsNotifications SET Status=11, Recipient=NULL WHERE CompanyId=1 AND Status=1')
        self.assertEqual(self.db.execute('SELECT CompanyId,Status,Recipient FROM WorkOrderSmsNotifications ORDER BY Id').fetchall(),
                         [(1,11,None),(2,1,'09123456789'),(1,2,'09123456789')])

    def test_two_connections_can_claim_pending_only_once(self):
        with tempfile.TemporaryDirectory() as temp:
            path = str(Path(temp) / 'queue.sqlite')
            db = sqlite3.connect(path); setup(db)
            db.execute("""INSERT INTO WorkOrderSmsNotifications
              (CompanyId,WorkOrderId,EventLogId,RecipientUserId,RecipientKey,EventType,Status,CreatedAtUtc)
              VALUES (1,1,1,1,'phone',1,1,'now')""")
            db.commit(); db.close()
            barrier=threading.Barrier(2); counts=[]; errors=[]
            def claim():
                try:
                    conn=sqlite3.connect(path, timeout=10)
                    barrier.wait()
                    result=conn.execute('UPDATE WorkOrderSmsNotifications SET Status=2 WHERE Id=1 AND Status=1').rowcount
                    conn.commit(); conn.close(); counts.append(result)
                except Exception as ex: errors.append(ex)
            threads=[threading.Thread(target=claim) for _ in range(2)]
            for t in threads: t.start()
            for t in threads: t.join()
            self.assertEqual(errors, [])
            self.assertEqual(sorted(counts), [0,1])
            with sqlite3.connect(path) as check:
                self.assertEqual(check.execute('SELECT Status FROM WorkOrderSmsNotifications').fetchone()[0],2)

class WorkOrderSmsSourceContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.controller=read('src/Inventory.Api/Controllers/ItAssets/WorkOrdersController.cs')
        cls.queue=read('src/Inventory.Api/Services/ItAssets/WorkOrderSmsQueue.cs')
        cls.service=read('src/Inventory.Api/Services/ItAssets/WorkOrderSmsService.cs')
        cls.gateway=read('src/Inventory.Api/Services/Notification/KavenegarSmsGateway.cs')

    def section(self, start, end):
        return self.controller[self.controller.index(start):self.controller.index(end)]

    def test_create_edit_workflow_and_reply_stage_before_commit(self):
        for start,end,event in [
          ('Task<IActionResult> Create(', 'Task<IActionResult> Update(', 'createdLog'),
          ('Task<IActionResult> Update(', 'private static string ToFa', 'editedLog'),
          ('Task<IActionResult> Reply(', 'private async Task<List<WorkOrder>> LoadAncestorsAsync', 'replyLog'),
          ('Task<IActionResult> AddAssignees(', 'Task<IActionResult> SyncLetterFlow(', 'addedLog')]:
            section=self.section(start,end)
            self.assertIn('IsolationLevel.Serializable',section)
            self.assertIn('TryStageSmsAsync(',section)
            self.assertIn(event,section)
            self.assertLess(section.index('TryStageSmsAsync('),section.rindex('tx.CommitAsync()'))
            self.assertNotIn('SendNewWorkOrderAsync',section)
        create=self.section('Task<IActionResult> Create(', 'Task<IActionResult> Update(')
        self.assertIn('users.Select(u => u.Id).ToArray(), WorkOrderSmsEventType.Registration',create) # Includes creator and sub-orders.
        self.assertIn('MaterializeAsync(wo)',create)
        update=self.section('Task<IActionResult> Update(', 'private static string ToFa')
        self.assertIn('editedLog, addedUserIds, WorkOrderSmsEventType.Referral',update)
        add=self.section('Task<IActionResult> AddAssignees(', 'Task<IActionResult> SyncLetterFlow(')
        self.assertIn('addedLog, users.Select(u => u.Id).ToArray(), WorkOrderSmsEventType.Referral',add)
        reply=self.section('Task<IActionResult> Reply(', 'private async Task<List<WorkOrder>> LoadAncestorsAsync')
        self.assertIn('new[] { wo.OwnerUserId }, WorkOrderSmsEventType.Reply',reply)
        self.assertIn('Log(ancestor.Id, "SubReply"',reply)
        self.assertLess(reply.index('Log(ancestor.Id, "SubReply"'), reply.index('tx.CommitAsync()', reply.index('Log(ancestor.Id, "SubReply"')))
        self.assertIn('WorkOrderRequestIdentity.ReplyIsUnchanged(asg.RepliedAt, asg.Done, asg.ReplyText, dto.Done, dto.Text)',reply)
        self.assertIn('replyLog.RequestKey = key',reply)

    def test_no_legacy_gateway_bypass_and_savepoint_failure_isolated(self):
        self.assertNotIn('/sms/send.json',self.service)
        self.assertNotIn('HttpClient',self.service)
        self.assertIn('transaction.SupportsSavepoints',self.service)
        self.assertIn('CreateSavepointAsync',self.service)
        self.assertIn('RollbackToSavepointAsync',self.service)
        self.assertIn('throw new InvalidOperationException("Work-order SMS staging could not be safely rolled back."',self.service)
        self.assertIn('entry.State = EntityState.Detached',self.service)
        self.assertIn('authorizedCompanyId is not > 0 || order.CompanyId != authorizedCompanyId',self.service)
        self.assertIn('db.ActiveCompanyId != authorizedCompanyId',self.service)
        self.assertNotIn('dto.SendSms',self.controller)
        self.assertIn('smsStageFailed = !smsStaged',self.controller)
        for start,end in [
          ('Task<IActionResult> Create(', 'Task<IActionResult> Update('),
          ('Task<IActionResult> Update(', 'private static string ToFa'),
          ('Task<IActionResult> Reply(', 'private async Task<List<WorkOrder>> LoadAncestorsAsync'),
          ('Task<IActionResult> AddAssignees(', 'Task<IActionResult> SyncLetterFlow(')]:
            section = self.section(start, end)
            self.assertLess(section.rindex('tx.CommitAsync()'), section.index('TryAfterCommitAsync('))
            self.assertIn('notificationsFailed = !notificationsOk', section)
        for start,end in [
          ('Task<IActionResult> Create(', 'Task<IActionResult> Update('),
          ('Task<IActionResult> Update(', 'private static string ToFa'),
          ('Task<IActionResult> AddAssignees(', 'Task<IActionResult> SyncLetterFlow(')]:
            section = self.section(start, end)
            self.assertIn('new List<DeferredLetterNotice>()', section)
            self.assertLess(section.rindex('tx.CommitAsync()'), section.index('NotifyDeferredLetterAccessAsync(letterNotices)'))

    def test_server_alert_creation_uses_authorized_resource_company(self):
        server=read('src/Inventory.Api/Controllers/ItAssets/ServersController.cs')
        section=server[server.index('Task<IActionResult> CreateWorkOrderFromAlert('):server.index('public sealed class SaveDto')]
        self.assertLess(section.index('BeginTransactionAsync'),section.index('FindAllowedAlertAsync(id)'))
        self.assertLess(section.index('FindAllowedAlertAsync(id)'),section.index('_workOrderSms.TryStageAsync('))
        self.assertLess(section.index('_workOrderSms.TryStageAsync('),section.index('transaction.CommitAsync()'))
        self.assertIn('CompanyId = server.CompanyId',section)
        self.assertIn('WorkOrderSmsEventType.Registration, _db.ActiveCompanyId',section)
        self.assertIn('if (alert.WorkOrderId is > 0)',section)
        self.assertIn('smsStageFailed = !smsStaged',section)
        policy=read('src/Inventory.Api/Services/Notification/CompanySmsServicePolicy.cs')
        self.assertIn('s.CompanyId == companyId.Value && s.ModuleKey == moduleKey',policy)
        self.assertNotIn('IgnoreQueryFilters()',policy)
        self.assertIn('n.CompanyId == order.CompanyId && n.EventLogId == log.Id',self.queue)
        ui=read('src/Inventory.Client/Pages/ItAssets/ServerManagement.razor')
        self.assertIn('PostForCompanyAsync<WorkOrderCreated>',ui)
        self.assertIn('result?.SmsStageFailed == true',ui)
        self.assertIn('result?.NotificationsFailed == true',ui)
        client=read('src/Inventory.Client/Services/ApiClient.cs')
        self.assertIn('req.Headers.TryAddWithoutValidation("X-Company-Id", companyId.Value.ToString',client)
        self.assertIn('if (req.Headers.Contains("X-Company-Id")) return;',client)

    def test_server_alert_rejects_oversized_title_instead_of_changing_it(self):
        server=read('src/Inventory.Api/Controllers/ItAssets/ServersController.cs')
        section=server[server.index('Task<IActionResult> CreateWorkOrderFromAlert('):server.index('public sealed class SaveDto')]
        self.assertLess(section.index('if (title.Length > 200)'),section.index('_db.WorkOrders.Add(wo)'))
        self.assertIn('return BadRequest',section[section.index('if (title.Length > 200)'):section.index('var wo = new WorkOrder')])
        self.assertIn('Title = title,',section)
        self.assertNotIn('title[..200]',section)

    def test_company_revision_and_one_claim_no_retry(self):
        self.assertIn('SmsModuleCatalog.WorkOrders',self.queue)
        self.assertIn('PermissionRevision = revision',self.queue)
        self.assertGreaterEqual(self.queue.count('IsEnabledForRevisionAsync('),2)
        self.assertIn('w.CompanyId == job.CompanyId',self.queue)
        self.assertIn('order.OwnerUserId == job.RecipientUserId',self.queue)
        self.assertIn('n.Status == WorkOrderSmsStatus.Pending',self.queue)
        self.assertIn('SetProperty(n => n.Status, WorkOrderSmsStatus.Sending)',self.queue)
        self.assertIn('SetProperty(n => n.Status, WorkOrderSmsStatus.Unknown)',self.queue)
        self.assertIn('gateway.SendWorkOrderLookupAsync(',self.queue)
        self.assertNotIn('gateway.SendAsync(',self.queue)
        self.assertNotIn('ReplyText',self.queue)
        self.assertIn('companyName, order.Number, order.Title, order.DueAt, kind',self.queue)
        self.assertIn('WorkOrderSmsPolicy.TitleToken(order?.Title)',self.queue)
        self.assertIn('WorkOrderSmsStatus.InvalidTitle',self.queue)
        self.assertIn('companyName!, title, deadline, template!',self.queue)

    def test_schema_and_registration(self):
        ctx=read('src/Inventory.Api/Data/AppDbContext.cs')
        program=read('src/Inventory.Api/Program.cs')
        catalog=read('src/Inventory.Shared/SmsModuleCatalog.cs')
        self.assertIn('HasIndex(n => new { n.EventLogId, n.RecipientKey }).IsUnique()',ctx)
        self.assertIn('HasIndex(l => l.RequestKey).IsUnique()',ctx)
        self.assertIn('Entity<WorkOrderSmsNotification>().HasQueryFilter',ctx)
        self.assertIn('WorkOrderSmsRevocationHandler',program)
        self.assertIn('WorkOrderSmsWorker',program)
        self.assertIn('"WorkOrders"',catalog)
        self.assertIn('WorkOrderSmsSchemaV1.EnsureAsync(db)',read('src/Inventory.Api/Data/DbInitializer.cs'))
        self.assertIn('WorkOrderSmsSchemaV1.SqlServer',read('src/Inventory.Api/Migrations/20261110102000_AddWorkOrderSmsNotifications.cs'))
        self.assertNotIn('INSERT INTO',SCHEMA.upper())

    def test_request_keys_recurrence_and_safe_provider_tokens(self):
        self.assertIn('RequestKey("create", companyId.Value, MyUserId, 0',self.controller)
        for operation in ('edit', 'assign', 'reply'):
            self.assertIn(f'RequestKey("{operation}", wo.CompanyId ?? 0, MyUserId, id',self.controller)
        self.assertGreaterEqual(self.controller.count('WorkOrderRequestIdentity.SamePayload(hit.Fingerprint, fingerprint)'),4)
        for log in ('createdLog', 'editedLog', 'addedLog', 'replyLog', 'unchangedLog'):
            self.assertIn(f'{log}.PayloadFingerprint = fingerprint',self.controller)
        self.assertIn('WorkOrderRequestIdentity.ReplyIsUnchanged(asg.RepliedAt, asg.Done, asg.ReplyText, dto.Done, dto.Text)',self.controller)
        self.assertIn('ReplyUnchanged',self.controller)
        self.assertIn('AssigneesUnchanged',self.controller)
        self.assertIn('IgnoreQueryFilters().AsNoTracking()',self.controller)
        scheduler=read('src/Inventory.Api/Services/ItAssets/WorkOrderSchedulingService.cs')
        self.assertIn('CompanyId = first.CompanyId',scheduler)
        self.assertNotIn('WorkOrderSmsQueue.StageAsync',scheduler)
        self.assertIn('Uri.EscapeDataString(year)',self.gateway)
        self.assertIn('Uri.EscapeDataString(serial)',self.gateway)
        self.assertIn('Uri.EscapeDataString(deadline)',self.gateway)
        self.assertIn('Uri.EscapeDataString(title)',self.gateway)
        self.assertIn('KavenegarSmsValidation.Token20(title)',self.gateway)
        self.assertIn('KavenegarSmsValidation.WorkOrderDeadlineToken(deadline)',self.gateway)
        policy=read('src/Inventory.Api/Services/ItAssets/WorkOrderSmsPolicy.cs')
        self.assertIn('Token20Characters(title)',policy)
        self.assertIn('StringInfo.GetTextElementEnumerator(title)',policy)
        self.assertIn('shortened + "..."',policy)
        options=read('src/Inventory.Api/Services/ItAssets/KavenegarSmsOptions.cs')
        for name, value in (('WorkOrderRegistrationTemplate', 'WorkOrderRegistration'),
                            ('WorkOrderReferralTemplate', 'WorkOrderReferral'),
                            ('WorkOrderReplyTemplate', 'WorkOrderReply')):
            self.assertRegex(options,rf'{name}\s*\{{\s*get;\s*set;\s*\}}\s*=\s*"{value}"')
        self.assertNotIn('WorkOrderAssignmentTemplate', options)
        self.assertIn('WorkOrderSmsEventType.Registration => action == "Created"', policy)
        self.assertIn('WorkOrderSmsEventType.Referral => action is "Edited" or "AssigneesAdded"', policy)
        self.assertIn('WorkOrderSmsEventType.Reply => action == "Reply"', policy)
        self.assertIn('WorkOrderSmsPolicy.RequiresDeadline(job.EventType)', self.queue)
        self.assertIn('job.EventType == WorkOrderSmsEventType.Registration && eventLog == "Created"', self.queue)

    def test_proposed_names_for_reported_approved_templates_are_configured_without_enabling_sdk(self):
        settings=read('src/Inventory.Api/appsettings.json')
        block=re.search(r'"KavenegarSms"\s*:\s*\{([^}]*)\}',settings,re.S)
        self.assertIsNotNone(block, 'Kavenegar settings section is missing')
        # Assert only the named, non-secret properties; do not print the settings block.
        for key,value in (('WorkOrderRegistrationTemplate','WorkOrderRegistration'),
                          ('WorkOrderReferralTemplate','WorkOrderReferral'),
                          ('WorkOrderReplyTemplate','WorkOrderReply')):
            match=re.search(r'"'+key+r'"\s*:\s*"([^"]*)"',block.group(1))
            self.assertEqual(match.group(1) if match else None,value)
        enabled=re.search(r'"SdkSendEnabled"\s*:\s*(true|false)',block.group(1))
        self.assertEqual(enabled.group(1) if enabled else None,'false')

    def test_reported_approved_templates_start_with_company_and_end_with_signature(self):
        doc=read('docs/KAVENEGAR-SDK-FA.md')
        block=doc[doc.index('## الگوها و رفتار دستور کار'):]
        texts=re.findall(r'```text\n(.*?)\n```',block,re.S)
        self.assertGreaterEqual(len(texts),3)
        registration,referral,reply=texts[:3]
        self.assertEqual(registration, '%token10\nکاربر گرامی، دستور کار «%token20» به شماره WO/%token/%token2 برای شما ثبت شد.\nمهلت انجام: %token3\nسامانه یکپارچه فروغ آریا')
        self.assertEqual(referral, '%token10\nکاربر گرامی، دستور کار «%token20» به شماره WO/%token/%token2 به شما ارجاع شد.\nمهلت انجام: %token3\nسامانه یکپارچه فروغ آریا')
        self.assertEqual(reply, '%token10\nکاربر گرامی، برای دستور کار «%token20» به شماره WO/%token/%token2 پاسخ ثبت شد.\nسامانه یکپارچه فروغ آریا')
        for name in ('WorkOrderRegistration', 'WorkOrderReferral', 'WorkOrderReply'):
            self.assertIn(f'`{name}`', block)

    def test_sms_results_are_compact_visible_and_consistent_with_repairs(self):
        ui=read('src/Inventory.Client/Pages/ItAssets/WorkOrders.razor')
        area=ui.split('<details class="wo-block mt-3" open>',1)[1].split('</details>',1)[0]
        self.assertIn('وضعیت پیامک‌های این دستور',area)
        self.assertIn('SmsEventLabel(item.EventType) — @item.RecipientName',area)
        self.assertIn('WorkOrderSmsAppearance.ErrorCode(item)',area)
        self.assertIn('text-success',area)
        self.assertIn('text-danger',area)
        self.assertIn('title="@WorkOrderSmsAppearance.Text(item.Status)"',area)
        success=area.split('@if (WorkOrderSmsAppearance.IsSuccess(item.Status))',1)[1].split('else',1)[0]
        self.assertIn('bi-check-circle-fill',success)
        self.assertIn('class="visually-hidden">@WorkOrderSmsAppearance.Text(item.Status)',success)
        self.assertNotIn('returnedCode.Value',success)
        self.assertIn('returnedCode.HasValue',area)
        self.assertIn('<bdi class="num" dir="ltr">@(returnedCode.Value)</bdi>',area)
        self.assertNotRegex(area,r'@code\b')  # Razor treats @code as a directive.
        self.assertIn('پذیرش درگاه به معنی تحویل نهایی نیست',area)
        self.assertIn('RefreshSmsEvents',area)

    def test_owner_only_status_ui_and_no_synchronous_success_claim(self):
        endpoint=self.section('Task<IActionResult> SmsEvents(', 'Task<IActionResult> Description(')
        self.assertIn('w.CompanyId == companyId.Value',endpoint)
        self.assertIn('order.OwnerUserId != MyUserId',endpoint)
        self.assertNotIn('n.Recipient,',endpoint)
        self.assertIn('n.Status != WorkOrderSmsStatus.Failed ? null',endpoint)
        self.assertIn('n.ProviderCode is not null and not 200 ? n.ProviderCode : n.ProviderMessageStatus',endpoint)
        ui=read('src/Inventory.Client/Pages/ItAssets/WorkOrders.razor')
        self.assertNotIn('@bind="sendSms"',ui)
        self.assertNotIn('@bind="gdAddSms"',ui)
        self.assertIn('clientRequestId = requestId',ui)
        self.assertIn('clientRequestId = newRequestId',ui)
        self.assertIn('پذیرش درگاه به معنی تحویل نهایی نیست',ui)
        self.assertIn('SmsStageFailed',ui)
        self.assertIn('NotificationsFailed',ui)
        appearance=read('src/Inventory.Client/Pages/ItAssets/WorkOrderSmsAppearance.cs')
        self.assertIn('WorkOrderSmsStatus.InvalidTitle',appearance)
        self.assertIn('WorkOrderSmsEventType.Registration => "ثبت دستور"',ui)
        self.assertIn('WorkOrderSmsEventType.Referral => "ارجاع"',ui)
        self.assertIn('sms-events',ui)
        self.assertIn('var smsDetailVersion = ++smsEventsVersion;',ui)
        self.assertIn('LoadSmsEvents(w.Id, smsDetailVersion)',ui)
        self.assertIn('detail?.Id != id || smsEventsVersion != expectedVersion',ui)
        self.assertGreaterEqual(ui.count('detail?.Id == id && smsEventsVersion == requestVersion'),3)

if __name__ == '__main__':
    unittest.main()
