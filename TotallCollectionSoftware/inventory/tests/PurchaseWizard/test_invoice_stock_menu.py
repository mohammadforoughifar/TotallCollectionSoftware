"""Source wiring for the invoice stock menu; runtime logic is covered by OrderVat."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
FORM = (ROOT / 'src/Inventory.Client/Pages/Orders/OrderForm.razor').read_text(encoding='utf-8')
STOCK = (ROOT / 'src/Inventory.Client/Pages/Orders/InvoiceWarehouseStock.cs').read_text(encoding='utf-8')
COPY = (ROOT / 'src/Inventory.Client/Pages/Orders/InvoiceCopyTemplate.cs').read_text(encoding='utf-8')
SEARCH = (ROOT / 'src/Inventory.Client/Shared/SearchSelect.razor').read_text(encoding='utf-8')
CSS = (ROOT / 'src/Inventory.Client/Pages/Orders/OrderForm.razor.css').read_text(encoding='utf-8')
INDEX = (ROOT / 'src/Inventory.Client/wwwroot/index.html').read_text(encoding='utf-8')
CONTROLLER = (ROOT / 'src/Inventory.Api/Controllers/Sales/OrdersController.cs').read_text(encoding='utf-8')
DTOS = (ROOT / 'src/Inventory.Shared/Dtos/OperationDtos.cs').read_text(encoding='utf-8')
CLIENT = (ROOT / 'src/Inventory.Client/Services/InventoryApiServices.cs').read_text(encoding='utf-8')


class InvoiceStockMenuContract(unittest.TestCase):
    def test_product_options_in_both_invoice_types_render_the_stock_template(self):
        line = FORM.split('<SearchSelect Items="productLookup"', 1)[1].split('</SearchSelect>', 1)[0]
        self.assertIn('<ItemDetails Context="option">', line)
        self.assertIn('productOption.StockQuantity.HasValue', line)
        self.assertIn('موجودی این انبار:', line)
        self.assertIn('InvoiceProductOption.FormatStock(quantity)', line)
        self.assertIn('productOption.StockUnit', line)
        self.assertIn('productOption.IsStockWarning', line)
        self.assertIn('bi-exclamation-triangle-fill', line)
        self.assertIn('visually-hidden', line)
        self.assertIn('option.InfoText', line)  # services, loading and unavailable states
        self.assertIn('Type is TransactionType.Purchase or TransactionType.Sale', FORM)

    def test_shared_search_select_stays_compatible_without_a_template(self):
        self.assertIn('[Parameter] public RenderFragment<LookupItem>? ItemDetails', SEARCH)
        self.assertIn('@ItemDetails(it)', SEARCH)
        self.assertIn('else if (!string.IsNullOrWhiteSpace(it.InfoText))', SEARCH)
        self.assertIn('<small class="ss-item-info">@it.InfoText</small>', SEARCH)

    def test_warning_is_only_on_the_numeric_value_and_style_is_scoped(self):
        self.assertIn('class="invoice-stock-quantity @(productOption.IsStockWarning', FORM)
        self.assertIn('StockQuantity is <= 0m', STOCK)
        self.assertIn('.invoice-stock-quantity.is-warning', CSS)
        self.assertIn('color: #b91c1c; background: #fff1f2', CSS)
        self.assertIn('Inventory.Client.styles.css?v=7', INDEX)

    def test_snapshot_guards_loading_errors_pagination_and_late_responses(self):
        self.assertIn('var requestVersion = ++version;', STOCK)
        self.assertIn('if (requestVersion != version) return false;', STOCK)
        self.assertIn('if (requestVersion == version) IsLoading = false;', STOCK)
        self.assertIn('expectedTotal.Value != result.TotalCount', STOCK)
        self.assertIn('if (received == expectedTotal.Value) break;', STOCK)
        self.assertIn('if (result.Items.Count == 0) throw', STOCK)
        self.assertIn('item.WarehouseId != warehouseId', STOCK)
        self.assertIn('!quantities.TryAdd', STOCK)
        self.assertIn('!isService && stock.WarehouseId > 0 && stock.IsLoaded', STOCK)
        self.assertIn('stock.Quantities.TryGetValue(id, out var value) ? value : 0m', STOCK)
        self.assertIn('موجودی انبار نامشخص است', STOCK)
        changed = FORM.split('private async Task OnWarehouseChanged(int v)', 1)[1].split('private static void RecalcDiscount', 1)[0]
        self.assertIn('RebuildProductLookup();', changed.split('if (!await load) return;', 1)[0])
        self.assertIn('if (warehouseId > 0 && !warehouseStock.IsLoaded)', changed)
        self.assertIn('return; // an unavailable snapshot must not clear existing invoice lines', changed)
        self.assertIn('if (warehouseStock.IsLoaded)', FORM)
        rebuilding = FORM.split('private void RebuildProductLookup()', 1)[1].split('private async Task OnWarehouseChanged', 1)[0]
        self.assertIn('warehouseId > 0 && !warehouseStock.IsLoaded', rebuilding)
        self.assertIn('visible.Add(l.ProductId)', rebuilding)
        self.assertIn('InvoiceProductOption.Create(line.ProductId, name', rebuilding)

    def test_invoice_stock_endpoint_is_scoped_and_does_not_expose_costs(self):
        self.assertIn('[Inventory.Api.Infrastructure.ModulePermission("Orders")]', CONTROLLER)
        self.assertIn('[HttpGet("warehouse-stock")]', CONTROLLER)
        endpoint = CONTROLLER.split('public async Task<ActionResult<PagedResult<InvoiceStockItem>>> WarehouseStock(', 1)[1].split('پیشنهاد قیمت', 1)[0]
        self.assertIn('if (warehouseId <= 0) return BadRequest', endpoint)
        self.assertIn('GetStockAsync(warehouseId, null, false,', endpoint)
        self.assertIn('Math.Clamp(pageSize, 1, 10000)', endpoint)
        self.assertIn('ProductId = s.ProductId, Quantity = s.Quantity', endpoint)
        dto = DTOS.split('public class InvoiceStockItem', 1)[1].split('public class StockItem', 1)[0]
        self.assertNotIn('AvgCost', dto)
        self.assertNotIn('Price', dto)
        self.assertNotIn('Profit', dto)
        self.assertIn('api/orders/warehouse-stock?warehouseId=', CLIENT)
        self.assertIn('OrderSvc.GetWarehouseStockAsync(id, page, size)', FORM)
        self.assertNotIn('StockSvc.GetStockAsync', FORM)

    def test_services_and_old_invoice_options_do_not_gain_fake_stock(self):
        self.assertIn('isService ? "خدمات"', STOCK)
        self.assertIn('InvoiceProductOption.Create(l.ProductId, l.ProductName, l.Unit, IsServiceProduct(l.ProductId), warehouseStock)', FORM)
        self.assertIn('IsService = line.IsService', COPY)
        self.assertIn('CopyTemplate?.Lines.FirstOrDefault(l => l.ProductId == productId)?.IsService', FORM)
        self.assertIn('############################', STOCK)  # a small positive value must not print as zero


if __name__ == '__main__':
    unittest.main()
