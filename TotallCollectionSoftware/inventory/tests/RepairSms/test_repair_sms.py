"""Offline SQLite schema/claim behavior and source contracts; no provider calls."""
from pathlib import Path
import re
import sqlite3
import tempfile
import threading
import unittest

ROOT = Path(__file__).resolve().parents[2]
def read(path):
    return (ROOT / path).read_text(encoding='utf-8')

SCHEMA = read('src/Inventory.Api/Data/RepairSmsSchemaV1.cs')
SQLITE = re.search(r'public const string Sqlite = @"(.*?)";', SCHEMA, re.S).group(1)

class SchemaTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.execute('PRAGMA foreign_keys=ON')
        self.db.execute('CREATE TABLE RepairOrders (Id INTEGER PRIMARY KEY, CompanyId INTEGER, PartyId INTEGER)')
        self.db.execute('INSERT INTO RepairOrders VALUES (1, 1, 2)')
        self.db.executescript(SQLITE)
        self.db.executescript(SQLITE)

    def tearDown(self): self.db.close()

    def insert(self, kind=1, status=1):
        self.db.execute('INSERT INTO RepairSmsNotifications (CompanyId,RepairOrderId,PartyId,EventType,Status,Recipient,CreatedAtUtc) VALUES (1,1,2,?,?,?,?)',
                        (kind, status, '09123456789', '2026-11-01 00:00:00'))

    def test_schema_idempotent_unique_per_order_and_event(self):
        self.insert(1); self.insert(2); self.insert(3)
        with self.assertRaises(sqlite3.IntegrityError): self.insert(1)
        with self.assertRaises(sqlite3.IntegrityError): self.insert(2)
        with self.assertRaises(sqlite3.IntegrityError): self.insert(3)
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM RepairSmsNotifications').fetchone()[0], 3)

    def test_repair_delete_cascades_notifications(self):
        self.insert(); self.db.execute('DELETE FROM RepairOrders WHERE Id=1')
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM RepairSmsNotifications').fetchone()[0], 0)

    def test_rollback_has_no_orphan_event(self):
        self.db.commit()
        self.db.execute('BEGIN')
        self.insert()
        self.db.rollback()
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM RepairSmsNotifications').fetchone()[0], 0)

    def test_terminal_or_unknown_cannot_be_claimed_as_pending(self):
        self.insert(status=6)
        self.insert(kind=3, status=14)  # Existing integer columns accept the new delivery/name values.
        n = self.db.execute('UPDATE RepairSmsNotifications SET Status=2 WHERE Status=1').rowcount
        self.assertEqual(n, 0)
        self.assertEqual(self.db.execute('SELECT Status FROM RepairSmsNotifications ORDER BY Id').fetchall(), [(6,), (14,)])

    def test_delivery_and_outbox_rollback_together_in_sqlite(self):
        self.db.execute('ALTER TABLE RepairOrders ADD COLUMN Status INTEGER NOT NULL DEFAULT 2')
        self.db.commit()
        self.db.execute('BEGIN')
        self.db.execute('UPDATE RepairOrders SET Status=3 WHERE Id=1')
        self.insert(kind=3)
        self.db.rollback()
        self.assertEqual(self.db.execute('SELECT Status FROM RepairOrders WHERE Id=1').fetchone()[0], 2)
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM RepairSmsNotifications').fetchone()[0], 0)

    def test_competing_workers_claim_once(self):
        with tempfile.TemporaryDirectory() as d:
            path = str(Path(d) / 'queue.sqlite')
            db = sqlite3.connect(path)
            db.execute('CREATE TABLE RepairOrders (Id INTEGER PRIMARY KEY)')
            db.execute('INSERT INTO RepairOrders VALUES (1)')
            db.executescript(SQLITE)
            db.execute("INSERT INTO RepairSmsNotifications (RepairOrderId,PartyId,EventType,Status,CreatedAtUtc) VALUES (1,2,3,1,'2026-11-01')")
            db.commit(); db.close()
            barrier = threading.Barrier(2); results = []; errors = []
            def claim():
                try:
                    c = sqlite3.connect(path, timeout=5)
                    barrier.wait()
                    changed = c.execute('UPDATE RepairSmsNotifications SET Status=2 WHERE Id=1 AND Status=1').rowcount
                    c.commit(); results.append(changed); c.close()
                except Exception as e: errors.append(e)
            threads = [threading.Thread(target=claim) for _ in range(2)]
            for t in threads: t.start()
            for t in threads: t.join()
            self.assertFalse(errors)
            self.assertEqual(sorted(results), [0, 1])

class RepairContracts(unittest.TestCase):
    def test_both_repair_paths_stage_inside_business_transaction(self):
        src = read('src/Inventory.Api/Services/Repairs/RepairService.cs')
        for start, end in [('SaveRepairAsync(RepairOrderDto dto)', 'DeleteRepairAsync(int id)'),
                           ('SetStatusAsync(int id, RepairStatus status)', 'InvoiceAsync(int id')]:
            method = src[src.index(start):src.index(end)]
            self.assertIn('RepairSmsPolicy.EventFor(', method)
            self.assertIn('RepairSmsQueue.StageAsync(', method)
            self.assertLess(method.index('RepairSmsQueue.StageAsync('), method.index('transaction.CommitAsync()'))
            self.assertNotIn('SendAsync(', method)
        self.assertIn('if (dto.Id != 0) entity.Status = dto.Status', src)
        invoice = src[src.index('InvoiceAsync(int id'):src.index('private async Task<int> WarrantyInvoiceIdAsync(')]
        stage = invoice.index('RepairSmsQueue.StageAsync(')
        self.assertIn('RepairSmsEventType.Delivery', invoice[stage:])
        self.assertLess(invoice.index('return previous;'), stage)
        self.assertLess(invoice.index('r.Status = RepairStatus.Delivered'), stage)
        self.assertLess(stage, invoice.index('transaction.CommitAsync()', stage))
        self.assertNotIn('SendLookupAsync(', invoice)

    def test_outbox_model_has_db_enforced_uniqueness_tenant_and_cascade(self):
        ctx = read('src/Inventory.Api/Data/AppDbContext.cs')
        self.assertIn('new { x.RepairOrderId, x.EventType }).IsUnique()', ctx)
        self.assertIn('Entity<RepairSmsNotification>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId)', ctx)
        self.assertIn('HasForeignKey(x => x.RepairOrderId).OnDelete(DeleteBehavior.Cascade)', ctx)
        self.assertIn('CREATE UNIQUE INDEX', SCHEMA)
        initializer = read('src/Inventory.Api/Data/DbInitializer.cs')
        self.assertEqual(initializer.count('RepairSmsSchemaV1.EnsureAsync(db)'), 2)
        self.assertIn('RepairSmsSchemaV1.SqlServer', read('src/Inventory.Api/Migrations/20261109100000_AddRepairSmsNotifications.cs'))

    def test_no_provider_retry_and_customer_scope(self):
        src = read('src/Inventory.Api/Services/Repairs/RepairSmsQueue.cs')
        self.assertIn('party.Mobile', src)
        self.assertNotIn('Technician', src)
        self.assertNotIn('party.Phone', src)
        self.assertIn('r.CompanyId == job.CompanyId && r.PartyId == job.PartyId', src)
        self.assertIn('p.CompanyId == job.CompanyId', src)
        self.assertIn('NormalizeMobile(party.Mobile) != job.Recipient', src)
        self.assertIn('repair.Status == RepairStatus.Ready', src)
        self.assertIn('repair.Status == RepairStatus.Delivered', src)
        self.assertIn('repair.InvoiceTransactionId is > 0 || repair.WarrantyProcessed', src)
        self.assertIn('KavenegarSmsValidation.Token20(party.Name)', src)
        self.assertIn('gateway.SendLookupWithToken20Async(job.Recipient, token, companyName!, party.Name, template!)', src)
        self.assertIn('n.Status == RepairSmsStatus.Pending', src)
        self.assertIn('n.Status == RepairSmsStatus.Sending && n.AttemptStartedAtUtc < cutoff', src)
        self.assertIn('SetProperty(n => n.Status, RepairSmsStatus.Unknown)', src)
        self.assertNotIn('BeginTransaction', src)
        self.assertNotIn('ex.Message', src)

    def test_rules_do_not_queue_disabled_or_invalid_mobile(self):
        policy = read('src/Inventory.Api/Services/Repairs/RepairSmsPolicy.cs')
        self.assertIn('previous != RepairStatus.Ready && current == RepairStatus.Ready', policy)
        self.assertIn('RepairSmsStatus.InvalidMobile', policy)
        self.assertIn('!gatewayEnabled ? RepairSmsStatus.Disabled', policy)
        self.assertIn('!lookupConfigured ? RepairSmsStatus.NotConfigured', policy)
        self.assertIn('RepairSmsStatus.Unknown', policy)
        self.assertIn('RepairSmsStatus.Accepted', policy)
        self.assertIn('RepairSmsStatus.Delivered', policy)
        self.assertIn('NumberToken(number)', policy)
        self.assertIn('RepairSmsEventType.Delivery => options.RepairDeliveredTemplate', policy)
        self.assertIn('RepairSmsStatus.InvalidCustomerName', policy)

    def test_delivery_enum_template_and_sdk_default(self):
        dtos = read('src/Inventory.Shared/Dtos/RepairDtos.cs')
        self.assertRegex(dtos, r'Registration\s*=\s*1,\s*Ready\s*=\s*2,\s*Delivery\s*=\s*3')
        self.assertRegex(dtos, r'InvalidRepairNumber\s*=\s*13,\s*InvalidCustomerName\s*=\s*14')
        options = read('src/Inventory.Api/Services/ItAssets/KavenegarSmsOptions.cs')
        self.assertIn('RepairDeliveredTemplate { get; set; } = "RepairDelivered"', options)
        settings = read('src/Inventory.Api/appsettings.json')
        block = re.search(r'"KavenegarSms"\s*:\s*\{([^}]*)\}', settings, re.S).group(1)
        # Never include the block in an assertion failure: an existing key may be present.
        self.assertTrue(bool(re.search(r'"SdkSendEnabled"\s*:\s*false', block)))
        self.assertTrue(bool(re.search(r'"RepairDeliveredTemplate"\s*:\s*"RepairDelivered"', block)))
        doc = read('docs/KAVENEGAR-SDK-FA.md')
        self.assertIn('%token20، مشتری گرامی، سفارش شما با شماره RP-%token تحویل داده شد.  '
                      'از اعتماد شما به مجموعه %token10 صمیمانه سپاسگزاریم.   '
                      'سامانه یکپارچه فروغ آریا', doc)

    def test_details_show_only_compact_result_and_returned_code(self):
        ui = read('src/Inventory.Client/Pages/Repairs/Repairs.razor')
        section = ui.split('<section class="border rounded p-3 mb-3" aria-label="وضعیت پیامک مشتری">', 1)[1].split('</section>', 1)[0]
        self.assertIn('SmsEventName(sms.EventType)', section)
        self.assertIn('SmsDetailStatus(sms.Status)', section)
        self.assertIn('SmsDetailCode(sms)', section)
        self.assertIn('SmsDetailSuccess(sms.Status)', section)
        success = section.split('@if (SmsDetailSuccess(sms.Status))', 1)[1].split('else', 1)[0]
        self.assertIn('bi-check-circle-fill', success)
        self.assertIn('class="visually-hidden">@SmsDetailStatus(sms.Status)', success)
        self.assertNotIn('returnedCode.Value', success)
        self.assertIn('var returnedCode = SmsDetailCode(sms);', section)
        self.assertIn('returnedCode.HasValue', section)
        self.assertIn('@(returnedCode.Value)', section)
        self.assertNotRegex(section, r'@code\b')  # Razor would interpret this as a directive.
        self.assertIn('RefreshSmsStatus', section)
        self.assertNotIn('SmsStatusText(', section)
        self.assertNotIn('ProviderCode', section)
        self.assertNotIn('ProviderMessageId', section)
        self.assertNotIn('شناسه کاوه‌نگار', section)
        self.assertNotIn('کد پاسخ', section)
        self.assertNotIn('تحویل نهایی نیست', section)
        self.assertNotIn('خودکار دوباره ارسال نمی‌شود', section)
        self.assertIn('پیامکی ثبت نشده است.', section)
        dto = read('src/Inventory.Shared/Dtos/RepairDtos.cs')
        service = read('src/Inventory.Api/Services/Repairs/RepairService.cs')
        self.assertIn('int? ProviderMessageStatus', dto)
        self.assertIn('ProviderMessageStatus = n.ProviderMessageStatus', service)

    def test_operator_can_see_all_states_and_refresh(self):
        ui = read('src/Inventory.Client/Pages/Repairs/Repairs.razor')
        for value in ('InvalidMobile', 'Disabled', 'NotConfigured', 'Accepted', 'Delivered', 'Failed', 'Unknown', 'Cancelled', 'ModuleDisabled', 'InvalidCompanyName', 'InvalidRepairNumber', 'InvalidCustomerName'):
            self.assertIn('RepairSmsStatus.' + value, ui)
        self.assertIn('RefreshSmsStatus', ui)
        self.assertIn('تحویل قطعی نیست', ui)
        self.assertIn('Toast.Warning(message)', ui)

if __name__ == '__main__': unittest.main()
