"""Source integration checks; they do not replace a Razor or database build."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8")


def section(text, start, end):
    return text.split(start, 1)[1].split(end, 1)[0]


class RepairStatusMenuContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.ui = read("src/Inventory.Client/Pages/Repairs/Repairs.razor")
        cls.badge = read("src/Inventory.Client/Pages/Repairs/RepairStatusBadge.razor")
        cls.appearance = read("src/Inventory.Client/Pages/Repairs/RepairStatusAppearance.cs")
        cls.api = read("src/Inventory.Api/Services/Repairs/RepairService.cs")
        cls.css = read("src/Inventory.Client/wwwroot/css/app.css")
        cls.js = read("src/Inventory.Client/wwwroot/js/repair-status-menu.js")
        cls.index = read("src/Inventory.Client/wwwroot/index.html")

    def test_status_is_the_control_in_both_list_layouts(self):
        for view in ('desktop', 'mobile'):
            self.assertIn(f'ToggleStatusMenu(r, "{view}")', self.ui)
            self.assertIn(f'StatusAnchorId(r.Id, "{view}")', self.ui)
        self.assertGreaterEqual(self.ui.count('<RepairStatusBadge Status="@r.Status"'), 2)
        for expected in ('<button type="button"', 'aria-haspopup="menu"', 'aria-expanded=',
                         'aria-controls=', 'repair-status-chevron', '@onclick="OnClick"',
                         '@RepairStatusAppearance.Text(Status)', '@if (Editable)'):
            self.assertIn(expected, self.badge)
        self.assertIn('Editable="@CanChangeStatus(r)"', self.ui)
        self.assertNotIn('در انتظار مشتری', self.ui + self.badge)
        self.assertIn('RepairStatus.Ready => "bi-hourglass-split"', self.appearance)
        self.assertIn('RepairStatus.Delivered => "bi-check-circle-fill"', self.appearance)
        self.assertIn('.repair-status-delivered .bi { color:', self.css)

    def test_original_actions_and_sms_summaries_remain(self):
        desktop = section(self.ui, '@* ---------- نمای جدولی', '@* ---------- نمای کارتی')
        mobile = section(self.ui, '@* ---------- نمای کارتی', '@if (total > 0)')
        for block in (desktop, mobile):
            for method in ('OpenView(r)', 'PrintDeliveryReceipt(r)', 'PrintInvoice(r)',
                           'OpenEdit(r)', 'OpenInvoice(r)', 'Delete(r)', 'creditRepair = r'):
                self.assertIn(method, block)
            self.assertIn('SmsSummary(r)', block)
        self.assertIn('class="m-card-actions repair-card-actions"', mobile)
        self.assertIn('.repair-card-actions { flex-wrap: wrap; }', self.css)
        self.assertIn('private void NotifyRepairSms(', self.ui)
        self.assertIn('NotifyRepairSms(saved, RepairSmsEventType.Ready)', self.ui)
        self.assertIn('NotifyRepairSms(saved, eventType)', self.ui)

    def test_finalized_or_read_only_repairs_are_not_editable(self):
        guard = section(self.ui, 'private bool CanChangeStatus(', 'private string StatusAnchorId(')
        for expected in ('ShopAccess.CanManage(Auth, "Repairs")', 'Auth.HasDirect("Repairs.Create")',
                         'r.InvoiceTransactionId is not > 0', '!r.WarrantyProcessed',
                         'RepairStatus.Received', 'RepairStatus.InProgress', 'RepairStatus.Ready',
                         'RepairStatus.Cancelled'):
            self.assertIn(expected, guard)
        self.assertNotIn('RepairStatus.Delivered', guard)
        self.assertIn('disabled="@(menuRepair.Status == RepairStatus.Cancelled)"', self.ui)
        self.assertIn('!r.WarrantyProcessed && r.Status != RepairStatus.Delivered', self.ui)
        self.assertIn('!viewing.WarrantyProcessed && viewing.Status != RepairStatus.Delivered', self.ui)

    def test_delivery_opens_real_workflow_never_direct_status_mutation(self):
        choose = section(self.ui, 'private async Task SelectStatusAsync(', '// ---------------- فرم ----------------')
        delivery = section(choose, 'if (choice == RepairStatus.Delivered)', 'if (choice == RepairStatus.Cancelled')
        self.assertIn('await OpenInvoice(r)', delivery)
        self.assertNotIn('SetStatusAsync', delivery)
        self.assertNotRegex(choose, r'r\.Status\s*=(?!=)')
        self.assertIn('await RepairSvc.SetStatusAsync(r.Id, choice)', choose)
        self.assertLess(choose.index('statusBusyId = r.Id'), choose.index('await ReleaseStatusMenuAsync(version, restoreFocus: true);', choose.index('statusBusyId = r.Id')))
        self.assertIn('if (choice == r.Status)', choose)
        self.assertIn('"confirm"', choose)  # An accidental cancellation is not immediate.
        self.assertIn('await Load()', choose)
        invoice = section(self.ui, 'private async Task DoInvoice()', 'private async Task PrintDeliveryReceipt(')
        self.assertLess(invoice.index('await RepairSvc.InvoiceAsync('), invoice.index('ReplaceRepairInList(res)'))
        self.assertNotIn('Status = RepairStatus.Delivered', invoice)
        self.assertIn('if (saving) return;', invoice)
        self.assertIn('OnClose="CloseInvoice"', self.ui)
        self.assertIn('disabled="@saving">انصراف', self.ui)
        self.assertIn('invoiceOpening || saving || invoicing is not null', self.ui)

    def test_server_still_guards_and_stages_ready_inside_transaction(self):
        status = section(self.api, 'public async Task<RepairOrderDto> SetStatusAsync(',
                         'public async Task<RepairOrderDto> InvoiceAsync(')
        self.assertIn('RepairStatus.Received or RepairStatus.InProgress or RepairStatus.Ready or RepairStatus.Cancelled', status)
        self.assertIn('r.Status == RepairStatus.Delivered || r.InvoiceTransactionId is > 0 || r.WarrantyProcessed', status)
        self.assertIn('RepairSmsPolicy.EventFor(false, previousStatus, r.Status)', status)
        self.assertLess(status.index('RepairSmsQueue.StageAsync('), status.index('transaction.CommitAsync()'))
        self.assertIn('previousStatus == RepairStatus.Cancelled) r.DeliveredAt = null', status)
        save = section(self.api, 'public async Task<RepairOrderDto> SaveRepairAsync(',
                       'public async Task DeleteRepairAsync(')
        self.assertIn('previousStatus == RepairStatus.Cancelled && entity.Status != RepairStatus.Cancelled', save)
        self.assertIn('entity.DeliveredAt = null', save)
        self.assertIn('entity.Status == RepairStatus.Delivered || entity.InvoiceTransactionId is > 0 || entity.WarrantyProcessed', save)
        delete = section(self.api, 'public async Task DeleteRepairAsync(', 'public async Task<RepairOrderDto> SetStatusAsync(')
        self.assertIn('r.Status == RepairStatus.Delivered || r.InvoiceTransactionId is > 0 || r.WarrantyProcessed', delete)
        invoice = section(self.api, 'public async Task<RepairOrderDto> InvoiceAsync(',
                          'private async Task<int> WarrantyInvoiceIdAsync(')
        self.assertIn('if (r.Status == RepairStatus.Delivered)', invoice)
        self.assertLess(invoice.index('if (r.Status == RepairStatus.Delivered)'), invoice.index('CreateOrderAsync('))
        self.assertIn('r.Status = RepairStatus.Delivered', invoice)
        self.assertIn('ConsumeWarrantyStockAsync(', invoice)
        self.assertIn('CreateOrderAsync(', invoice)
        stage = invoice.index('RepairSmsQueue.StageAsync(')
        self.assertIn('RepairSmsEventType.Delivery', invoice[stage:])
        self.assertLess(invoice.index('return previous;'), stage)  # Idempotent/historical deliveries are not backfilled.
        self.assertLess(invoice.index('r.Status = RepairStatus.Delivered'), stage)
        self.assertLess(invoice.index('CreateOrderAsync('), stage)
        self.assertLess(invoice.index('ConsumeWarrantyStockAsync('), stage)
        self.assertLess(stage, invoice.index('var result = await ToDtoAsync(r)', stage))
        self.assertLess(stage, invoice.index('transaction.CommitAsync()', stage))
        self.assertNotIn('SendLookupAsync(', invoice)  # The provider is called only by the worker.
        self.assertIn('NotifyRepairSms(res, RepairSmsEventType.Delivery)', self.ui)

    def test_menu_is_outside_scrolling_containers_and_loaded_for_published_app(self):
        list_end = self.ui.index('@* منو بیرون از جدول')
        self.assertGreater(list_end, self.ui.index('<Pager Page="page"'))
        self.assertLess(list_end, self.ui.index('@* ---------------- فرم پذیرش'))
        self.assertIn('position: fixed; z-index: 4500', self.css)
        self.assertIn('max-width: calc(100vw - 16px)', self.css)
        self.assertIn('repair-status-menu.js?v=1', self.index)
        self.assertIn('css/app.css?v=20', self.index)
        for expected in ('pointerdown', "event.key === 'Escape'", "event.key === 'Tab'",
                         "'ArrowDown'", "'ArrowUp'", 'MutationObserver', "'scroll'", "'resize'",
                         'preventScroll: true', 'state.observer.disconnect()'):
            self.assertIn(expected, self.js)


if __name__ == '__main__':
    unittest.main()
