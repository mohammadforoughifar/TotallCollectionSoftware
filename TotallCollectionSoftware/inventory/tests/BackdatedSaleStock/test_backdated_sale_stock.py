"""فروش عقب‌تاریخ‌خورده نباید همان قلم را دوبار بفروشد.

گزارش کاربر: «در فاکتور فروش اگر بیشتر از موجودی آن کالا در آن تاریخ وارد شد، چک
شه که بعد از آن تاریخ فروشی نداشته باشیم... موجودی بعد از آن تاریخ انتخابی هم باید
چک شود و اگر آن یک دانه کالا موجود نبود نباید بگذار برایش فاکتور فروش زد.»

منطق: موجودی «تا تاریخ سند» فقط گذشته را می‌بیند. اگر کالا بعد از تاریخ سند فروش
رفته باشد، فاکتور عقب‌تاریخ‌خورده همان مقدار را «دوباره» می‌فروشد. حد مجاز ثبت =
کمینهٔ موجودی از پایان همان روز تا آخر (forward-safe availability).

این تست قرارداد متنی است و جایگزین بیلد دات‌نت و تست زندهٔ API نیست (SDK در این
محیط نصب نیست).

    cd inventory
    python3 tests/BackdatedSaleStock/test_backdated_sale_stock.py -v
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'
API = ROOT / 'Inventory.Api'
CLIENT = ROOT / 'Inventory.Client'
SHARED = ROOT / 'Inventory.Shared'

SERVICE = (API / 'Services/Catalog/InventoryService.cs').read_text(encoding='utf-8')
IFACE = (API / 'Services/Catalog/IInventoryService.cs').read_text(encoding='utf-8')
ORDERS_CTRL = (API / 'Controllers/Sales/OrdersController.cs').read_text(encoding='utf-8')
DTOS = (SHARED / 'Dtos/OperationDtos.cs').read_text(encoding='utf-8')
SNAPSHOT = (CLIENT / 'Pages/Orders/InvoiceWarehouseStock.cs').read_text(encoding='utf-8')
FORM = (CLIENT / 'Pages/Orders/OrderForm.razor').read_text(encoding='utf-8')


def section(text, start, end):
    """قطعهٔ بین دو نشانه؛ خطای روشن اگر نشانه پیدا نشود."""
    assert start in text, f'missing start marker: {start}'
    rest = text.split(start, 1)[1]
    assert end in rest, f'missing end marker: {end}'
    return rest.split(end, 1)[0]


class ServerContract(unittest.TestCase):
    """سرور مقدار «قابل ثبت» را حساب و در ثبت/ویرایش اعمال می‌کند."""

    def test_service_exposes_forward_safe_availability(self):
        self.assertIn('Task<Dictionary<(int WarehouseId, int ProductId), decimal>> GetSellableQuantitiesAsync(', IFACE)
        self.assertIn('public async Task<Dictionary<(int WarehouseId, int ProductId), decimal>> GetSellableQuantitiesAsync(', SERVICE)

    def test_forward_replay_keeps_the_lowest_balance(self):
        body = section(SERVICE, 'public async Task<Dictionary<(int WarehouseId, int ProductId), decimal>> GetSellableQuantitiesAsync(',
                       '/// پیام خطای موجودی با تفکیک')
        # نقطهٔ شروع: ماندهٔ تاریخ سند، نه تجمیع فعلی
        self.assertIn('var lowest = await GetStockQuantitiesAsOfAsync(asOfDay, warehouseId, productIds, excludeTransactionId);', body)
        # گردش اسناد پس از تاریخ سند
        self.assertIn('Where(t => t.Date >= end)', body)
        # گردش ماژول انبارداری پس از تاریخ سند
        self.assertIn('_db.InvLedger.AsNoTracking().Where(e => e.Date >= end)', body)
        # تنظیم موجودی (Initial/Adjustment) «reset» است نه افزایش
        self.assertIn('var isReset = t.Type is not (TransactionType.Purchase or TransactionType.SaleReturn', body)
        self.assertIn('var next = m.Reset ? m.Delta : running + m.Delta;', body)
        # کمینهٔ مسیر، جدا از ماندهٔ جاری
        self.assertIn('var balance = new Dictionary<(int WarehouseId, int ProductId), decimal>(lowest);', body)
        self.assertIn('if (!lowest.TryGetValue(m.Key, out var floor) || next < floor) lowest[m.Key] = next;', body)
        # سند در حال ویرایش نباید دوبار حساب شود
        self.assertIn('excludeTransactionId', body)

    def test_sale_create_uses_sellable_not_dated_stock(self):
        body = section(SERVICE, 'if (cmd.Type == TransactionType.Sale && !settings.AllowNegativeStock)',
                       '// معرف سند فروش')
        self.assertIn('var asOfStock = await GetStockQuantitiesAsOfAsync(date, cmd.WarehouseId, productIds);', body)
        self.assertIn('var sellableStock = await GetSellableQuantitiesAsync(date, cmd.WarehouseId, productIds);', body)
        self.assertIn('if (sellable < quantity)', body)
        self.assertIn('SellableStockMessage(name, atDate, sellable)', body)
        # سطرهای تکراری یک کالا با هم جمع می‌شوند، نه سطر‌به‌سطر
        self.assertIn('.GroupBy(l => l.ProductId)', body)
        self.assertIn('var quantity = group.Sum(l => l.Quantity);', body)

    def test_sale_update_paths_also_check_later_consumption(self):
        self.assertIn('var updateSellable = await GetSellableQuantitiesAsync(updateAsOf, cmd.WarehouseId, updateIds, txn.Id);', SERVICE)
        self.assertIn('var sellableStock = await GetSellableQuantitiesAsync(editDate, cmd.WarehouseId, productIds, saleId);', SERVICE)
        self.assertEqual(SERVICE.count('SellableStockMessage(name, atDate, sellable)'), 3)

    def test_message_separates_dated_stock_from_later_sales(self):
        helper = section(SERVICE, 'private static string SellableStockMessage(',
                         'public async Task<PagedResult<StockItem>> GetStockAsync(')
        self.assertIn('var later = atDate - sellable;', helper)
        self.assertIn('بعد از این تاریخ', helper)
        self.assertIn('عدد قابل ثبت است', helper)

    def test_stock_endpoint_reports_sellable_quantity(self):
        self.assertIn('var sellableQuantities = await _service.GetSellableQuantitiesAsync(asOfDate, warehouseId);', ORDERS_CTRL)
        self.assertIn('SellableQuantity = sellableQuantities.TryGetValue((s.WarehouseId, s.ProductId), out var sellable)', ORDERS_CTRL)
        dto = section(DTOS, 'public class InvoiceStockItem', '/// <summary>موجودی فعلی یک کالا در یک انبار</summary>')
        self.assertIn('public decimal Quantity { get; set; }', dto)
        self.assertIn('public decimal SellableQuantity { get; set; }', dto)


class ClientContract(unittest.TestCase):
    """فرم فروش پیش از ثبت، همان قاعده را با عدد دقیق نشان می‌دهد و جلوی ثبت را می‌گیرد."""

    def test_snapshot_keeps_sellable_quantities(self):
        self.assertIn('public IReadOnlyDictionary<int, decimal> Sellables { get; private set; }', SNAPSHOT)
        self.assertIn('public decimal SellableFor(int productId)', SNAPSHOT)
        self.assertIn('sellables.TryAdd(item.ProductId, item.SellableQuantity)', SNAPSHOT)
        self.assertIn('public decimal? SellableQuantity { get; private init; }', SNAPSHOT)
        self.assertIn('public bool IsSellableShort => SoldAfterDate > 0m;', SNAPSHOT)

    def test_form_blocks_over_sellable_quantity(self):
        validator = section(FORM, 'private bool ValidateSellableStock(', 'private async Task SaveProforma()')
        self.assertIn('if (Type != TransactionType.Sale || IsProforma || warehouseId <= 0) return true;', validator)
        self.assertIn('if (!warehouseStock.IsLoaded || allowNegativeStock == true) return true;', validator)
        self.assertIn('var sellable = warehouseStock.SellableFor(group.Key) + own;', validator)
        self.assertIn('if (requested <= sellable) continue;', validator)
        self.assertIn('ShowFormError(later > 0', validator)
        self.assertIn('if (!ValidateSellableStock(validLines)) return false;', FORM)

    def test_editing_does_not_double_count_its_own_lines(self):
        helper = section(FORM, 'private decimal OwnInvoiceQuantity(', 'private string ProductLabel(')
        self.assertIn('IsEdit && EditOrder is not null', helper)
        self.assertIn('EditOrder.Lines.Where(l => l.ProductId == productId).Sum(l => l.Quantity)', helper)
        validator = section(FORM, 'private bool ValidateSellableStock(', 'private async Task SaveProforma()')
        self.assertIn('var own = OwnInvoiceQuantity(group.Key);', validator)

    def test_negative_stock_setting_is_respected(self):
        self.assertIn('@inject ISettingsService SettingsSvc', FORM)
        self.assertIn('private bool? allowNegativeStock;', FORM)
        self.assertIn('allowNegativeStock = (await SettingsSvc.GetAsync())?.AllowNegativeStock ?? false;', FORM)
        # نامشخص بودن تنظیمات نباید فروش سالم را ببندد
        self.assertIn('catch { allowNegativeStock = null; }', FORM)

    def test_row_and_picker_show_the_sellable_limit(self):
        self.assertIn('data-sellable-stock="@l.ProductId"', FORM)
        self.assertIn('قابل ثبت با احتساب فروش بعد از', FORM)
        self.assertIn('data-sellable-short="@option.Id"', FORM)


class AvailabilityMirror(unittest.TestCase):
    """آینهٔ الگوریتم: کمینهٔ موجودی از پایان روز سند به بعد."""

    @staticmethod
    def sellable(balance_at_date, moves):
        """moves: [(delta, is_reset)] به ترتیب تاریخ، فقط گردش بعد از تاریخ سند."""
        running = balance_at_date
        lowest = running
        for delta, is_reset in moves:
            running = delta if is_reset else running + delta
            lowest = min(lowest, running)
        return lowest

    def test_extra_unit_already_sold_after_the_date_is_blocked(self):
        # موجودی تا تاریخ سند ۴؛ بعد از آن ۱ عدد فروش رفته ⇒ فقط ۳ عدد قابل ثبت است.
        sellable = self.sellable(4, [(-1, False)])
        self.assertEqual(
            self.sellable(4, [(-1, False)]), 3,
            'a unit sold after the invoice date cannot be sold again by a backdated invoice')
        self.assertTrue(5 > sellable, 'requesting 5 on a date with 4 in stock must be rejected')
        self.assertTrue(3 <= sellable, 'requesting 3 stays within the forward-safe amount')

    def test_whole_stock_sold_later_leaves_nothing(self):
        self.assertEqual(self.sellable(4, [(-4, False)]), 0)

    def test_later_purchase_never_raises_the_floor(self):
        # فروش ۱ بعد از تاریخ (۳) و سپس خرید ۱۰: کمینه همان ۳ می‌ماند.
        self.assertEqual(self.sellable(4, [(-1, False), (10, False)]), 3)

    def test_reset_is_not_an_increase(self):
        # تنظیم موجودی = «reset»؛ نباید مثل افزایش جمع شود.
        self.assertEqual(self.sellable(4, [(-4, False), (20, True)]), 0)

    def test_own_invoice_is_added_back_while_editing(self):
        # در ویرایش، مقدار خود سند از موجودی کنار گذاشته می‌شود (سرور: excludeTransactionId).
        stock_at_date, own, later_sale = 4, 4, 1
        sellable_with_own = (stock_at_date - own - later_sale) + own
        self.assertEqual(sellable_with_own, stock_at_date - later_sale)


if __name__ == '__main__':
    unittest.main()
