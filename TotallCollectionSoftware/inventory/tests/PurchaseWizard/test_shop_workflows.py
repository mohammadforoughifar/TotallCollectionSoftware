"""Source-level safeguards; the C# integration and live Blazor tests are still required."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'src'


def read(path):
    return (SRC / path).read_text(encoding='utf-8')


class ShopWorkflows(unittest.TestCase):
    def test_profit_requires_explicit_permission_even_with_shop_aliases(self):
        aliases = read('Inventory.Shared/ShopSections.cs').split('public static bool AliasAllows', 1)[1]
        self.assertLess(aliases.index('action.Equals("ViewProfit"'), aliases.index('foreach (var section in All)'))
        self.assertIn('StringComparison.OrdinalIgnoreCase', aliases)
        for page in ('OrderForm', 'Orders'):
            source = read(f'Inventory.Client/Pages/Orders/{page}.razor')
            self.assertIn('OrderSvc.CanViewProfitAsync()', source)
            self.assertIn('canSeeProfit = false', source)
        self.assertIn('api/orders/profit-access', read('Inventory.Client/Services/InventoryApiServices.cs'))
        controller = read('Inventory.Api/Controllers/Sales/OrdersController.cs')
        self.assertIn('(await _permissions.GetAsync(User)).Contains("Orders.ViewProfit")', controller)
        for field in ('order.TotalProfit = null', 'order.NetProfit = null', 'line.UnitCost = null', 'line.Profit = null'):
            self.assertIn(field, controller)
        self.assertIn('foreach (var order in result.Items) RemoveSaleProfit(order)', controller)
        self.assertIn('Ok(await ProtectSaleProfitAsync(order))', controller)
        for method in ('CreateOrderAsync', 'UpdateOrderAsync', 'EditSaleChequeOrderAsync'):
            before, after = controller.split(f'_service.{method}', 1)
            self.assertIn('var revealProfit = await', before.rsplit('[Http', 1)[-1])
            self.assertIn('revealProfit ? saved : RemoveSaleProfit(saved)', after.split('}', 1)[0])

    def test_copy_is_whitelisted_and_always_created_as_a_new_invoice(self):
        template = read('Inventory.Client/Pages/Orders/InvoiceCopyTemplate.cs')
        for name in ('WarehouseId', 'PartyId', 'ProductId', 'Quantity', 'Price', 'DiscountPercent', 'VatValue'):
            self.assertIn(f'{name} =', template)
        for name in ('source.Id', 'line.Id', 'source.Date', 'source.Cheques', 'source.Installments',
                     'source.CashTrackingNumber', 'source.TrsAccountId', 'source.ChequeChargeSnapshot'):
            self.assertNotIn(name, template)
        orders = read('Inventory.Client/Pages/Orders/Orders.razor')
        self.assertEqual(orders.count('data-order-copy-context'), 2)
        self.assertIn('data-hotkey="f7"', orders)
        self.assertIn('var full = await OrderSvc.GetAsync(source.Id)', orders)
        self.assertIn('copyTemplate = InvoiceCopyTemplate.From(full)', orders)
        self.assertIn('editingOrder = null;', orders.split('private async Task OpenCopy', 1)[1])
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        copy_branch = form.split('if (CopyTemplate is { } copy && copy.Type == Type)', 1)[1].split('catch (Exception ex)', 1)[0]
        for name in ('Id = l.Id', 'OriginalQty = l.Quantity', 'cashTrackingNumber', 'cheques =', 'installments =', 'saleChequeCharge ='):
            self.assertNotIn(name, copy_branch)
        self.assertIn('date = DateTime.Now', form)
        self.assertIn('saved = await OrderSvc.CreateAsync(cmd)', form)
        self.assertIn('activeWarehouseIds.Contains(warehouseId)', form)
        self.assertIn('activePartyIds.Contains(partyId)', form)
        self.assertIn('products.Any(p => p.Id == l.ProductId', form)
        for field in ('Quantity', 'Price', 'VatValue'):
            self.assertIn(f'ExactDecimal(l.{field})', copy_branch)
        self.assertIn('تخفیف ریالی اعشاری طبق قواعد ثبت جدید', form)
        self.assertIn('در فاکتور کپی‌شده، قیمت و درصد تخفیف', form)

    def test_store_hotkeys_are_scoped_to_active_tab_and_top_dialog(self):
        tabs = read('Inventory.Client/Layout/TabPages.razor')
        js = read('Inventory.Client/wwwroot/js/order-entry.js')
        self.assertIn('data-shop-scope=', tabs)
        self.assertIn('ShopSections.ForRoute(t.Key)', tabs)
        self.assertIn('.tabpane.on[data-shop-scope="true"]', js)
        self.assertIn('.modal.show, [data-shop-dialog]', js)
        self.assertIn("F1: 'f1', F2: 'f2', F3: 'f3', F4: 'f4', F7: 'f7', Escape: 'esc'", js)
        self.assertIn('actions.length === 1', js)
        self.assertIn("target.closest('[data-order-copy-context]')", js)
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        for hotkey, callback in (('f1', 'GoToPayment'), ('f2', 'AddLine'), ('f3', 'BackToInvoice'), ('f4', 'SaveAndPrint')):
            self.assertRegex(form, rf'data-hotkey="{hotkey}"[^>]*@onclick="{callback}"')
        self.assertRegex(form, r'data-pay-add-cheque data-hotkey="f2"[^>]*@onclick="AddCheque"')
        finance = read('Inventory.Client/Pages/SalesVisit/VisFinanceModal.razor')
        self.assertRegex(finance, r'data-fin-add-cheque data-hotkey="f2"[^>]*@onclick="AddCheque"')
        issue = read('Inventory.Client/Pages/Catalog/GoodsIssues.razor')
        self.assertRegex(issue, r'data-hotkey="f2"[^>]*@onclick="AddEmptyLine"')
        self.assertRegex(issue, r'data-hotkey="f1"[^>]*@onclick="Save"')
        repairs = read('Inventory.Client/Pages/Repairs/Repairs.razor')
        self.assertRegex(repairs, r'data-hotkey="f2"[^>]*@onclick="AddItem"')
        self.assertNotIn('data-hotkey="f3"', repairs)
        for name in ('VisitForm', 'VisitDetail', 'Approvals'):
            self.assertIn('data-shop-dialog', read(f'Inventory.Client/Pages/SalesVisit/{name}.razor'))

    def test_only_invoice_notifications_move_to_top_right(self):
        css = read('Inventory.Client/wwwroot/css/app.css')
        normal = css.split('.toast-container {', 1)[1].split('}', 1)[0]
        moved = css.split('.toast-container.toast-container-top-right {', 1)[1].split('}', 1)[0]
        self.assertIn('bottom: 1.25rem', normal)
        self.assertIn('left: 1.25rem', normal)
        self.assertIn('top: calc(1rem + var(--invoice-error-stack-offset, 0px))', moved)
        self.assertIn('right: 1rem', moved)
        self.assertIn('ResizeObserver', read('Inventory.Client/wwwroot/js/order-entry.js'))
        form_css = read('Inventory.Client/Pages/Orders/OrderForm.razor.css')
        error = form_css.split('.order-form-error {', 1)[1].split('}', 1)[0]
        self.assertIn('position: fixed', error)
        self.assertIn('right: 1rem', error)
        self.assertIn('Toast.ShowAtTopRight', read('Inventory.Client/Pages/Orders/OrderForm.razor'))
        self.assertIn('t.TopRight == topRight', read('Inventory.Client/Layout/ToastHost.razor'))


if __name__ == '__main__':
    unittest.main()
