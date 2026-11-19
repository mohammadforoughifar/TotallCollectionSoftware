"""قراردادهای منبع برای اقلام بیجک: هم انتخاب از انبار، هم ورود دستی.

این تست‌ها قرارداد متنی‌اند و جایگزین بیلد Razor، تست زندهٔ API و تست مرورگر
نیستند. SDK دات‌نت در این محیط موجود نیست.

    cd inventory
    python3 tests/GoodsIssueItems/test_goods_issue_items.py -v
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'
API = ROOT / 'Inventory.Api'
CLIENT = ROOT / 'Inventory.Client'
SHARED = ROOT / 'Inventory.Shared'

PAGE = (CLIENT / 'Pages/Catalog/GoodsIssues.razor').read_text(encoding='utf-8')
SERVICE = (API / 'Services/Catalog/GoodsIssueService.cs').read_text(encoding='utf-8')
CONTROLLER = (API / 'Controllers/Catalog/GoodsIssuesController.cs').read_text(encoding='utf-8')
DTOS = (SHARED / 'Dtos/GoodsIssueDtos.cs').read_text(encoding='utf-8')
I_CLIENT = (CLIENT / 'Services/IInventoryApi.cs').read_text(encoding='utf-8')
CLIENT_SVC = (CLIENT / 'Services/InventoryApiServices.cs').read_text(encoding='utf-8')
GUIDE = (CLIENT / 'Pages/Shop/ShopGuide.razor').read_text(encoding='utf-8')


def section(text, start, end):
    return text.split(start, 1)[1].split(end, 1)[0]


class ServerContracts(unittest.TestCase):
    """سرور برای سطر «انبار» شناسهٔ کالا می‌گیرد و سطر «دستی» نام آزاد."""

    def test_product_options_are_served_with_stock(self):
        self.assertIn('Task<List<GoodsIssueProductOptionDto>> GetProductOptionsAsync();', SERVICE)
        options = section(SERVICE, 'public async Task<List<GoodsIssueProductOptionDto>> GetProductOptionsAsync()',
                          '/// <summary>سقف فهرست انتخاب کالا')
        self.assertIn('_inventory.GetProductsAsync(null, false, 1, MaxProductOptions)', options)
        self.assertIn('.Where(p => !p.IsService)', options)
        self.assertIn('TotalStock = p.TotalStock', options)
        self.assertIn('AllowsFractionalQuantity = p.AllowsFractionalQuantity', options)
        self.assertIn('GoodsIssueService(AppDbContext db, IInventoryService inventory)', SERVICE)

    def test_endpoint_lives_behind_the_goods_issue_permission(self):
        self.assertIn('[Inventory.Api.Infrastructure.ModulePermission("GoodsIssues")]', CONTROLLER)
        self.assertIn('[HttpGet("product-options")]', CONTROLLER)
        endpoint = section(CONTROLLER, '[HttpGet("product-options")]', '[HttpPost]')
        self.assertIn('_service.GetProductOptionsAsync()', endpoint)
        # مسیر باید پیش از «{id:int}» تعریف شود تا با پارامتر عددی قاطی نشود.
        self.assertLess(CONTROLLER.index('[HttpGet("product-options")]'), CONTROLLER.index('[HttpGet("{id:int}")]'))

    def test_save_keeps_manual_lines_and_snapshots_picked_names(self):
        save = section(SERVICE, 'public async Task<GoodsIssueDto> SaveAsync(GoodsIssueCommand cmd)',
                       'GoodsIssue entity;')
        # سطر دستی شناسه ندارد و باید همچنان پذیرفته شود.
        self.assertIn('cmd.Lines.Where(l => l.ProductId > 0)', save)
        self.assertIn('!p.IsService', save)
        self.assertIn('line.ProductName = chosenProducts.TryGetValue(line.ProductId, out var productName)', save)
        self.assertIn('کالای انتخاب‌شده از انبار یافت نشد؛ از فهرست دوباره انتخاب کنید.', save)
        # اعتبارسنجی نام پس از پر شدن نام سطر انبار انجام می‌شود.
        self.assertLess(save.index('line.ProductName = chosenProducts'), save.index('نام کالای هر سطر را وارد کنید.'))
        self.assertIn('ProductId = l.ProductId,', SERVICE)

    def test_dto_carries_product_and_stock_fields(self):
        self.assertIn('public class GoodsIssueProductOptionDto', DTOS)
        for member in ('public int Id { get; set; }', 'public string Code { get; set; } = "";',
                       'public string Name { get; set; } = "";', 'public string Unit { get; set; } = "عدد";',
                       'public decimal TotalStock { get; set; }', 'public string? WarehouseName { get; set; }',
                       'public bool? AllowsFractionalQuantity { get; set; }', 'public bool IsActive { get; set; } = true;'):
            with self.subTest(member=member):
                self.assertIn(member, DTOS)
        self.assertIn('public string DisplayName => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} — {Name}";', DTOS)


class ClientContracts(unittest.TestCase):
    """فرم بیجک هم انتخاب‌گر انبار دارد و هم ورودی نام دستی."""

    def test_client_service_exposes_the_endpoint(self):
        self.assertIn('Task<List<GoodsIssueProductOptionDto>> GetProductOptionsAsync();', I_CLIENT)
        self.assertIn('api/goods-issues/product-options', CLIENT_SVC)

    def test_each_line_has_a_warehouse_picker_and_a_manual_name_field(self):
        # انتخاب‌گر انبار
        self.assertIn('<SearchSelect Items="productLookup"', PAGE)
        self.assertIn('Value="line.ProductId"', PAGE)
        self.assertIn('ValueChanged="id => OnProductPickedAsync(idx, line, id)"', PAGE)
        self.assertIn('Placeholder="از انبار انتخاب کنید…"', PAGE)
        # ورود دستی همچنان سرجایش است و در حالت انتخاب‌شده هم ویرایش‌پذیر می‌ماند
        self.assertIn('id="gi-name-@idx"', PAGE)
        self.assertIn('@oninput="e => SetName(line, e.Value?.ToString())"', PAGE)
        self.assertIn('disabled="@viewing" placeholder="نام کالا…"', PAGE)
        self.assertNotIn('@bind="line.ProductName" disabled="@viewing" readonly', PAGE)
        # هدر ستون‌ها هر دو روش را نشان می‌دهد
        self.assertIn('<th style="width:230px">از انبار</th>', PAGE)
        self.assertIn('نام کالا <span class="fw-400 text-muted fs-8">(دستی)</span>', PAGE)

    def test_picking_fills_name_and_quantity_and_focuses_quantity(self):
        pick = section(PAGE, 'private async Task OnProductPickedAsync(', '/// <summary>Enter روی نام کالا')
        self.assertIn('line.ProductId = chosen.Id;', pick)
        self.assertIn('line.ProductName = chosen.Name;', pick)
        self.assertIn('if (line.Quantity <= 0) line.Quantity = 1;', pick)
        self.assertIn('focusById", $"gi-qty-{idx}"', pick)
        self.assertIn('line.ProductId = 0;   // پاک‌کردن انتخاب → همان نام دستی باقی می‌ماند', pick)

    def test_manual_rename_detaches_the_selected_product(self):
        set_name = section(PAGE, 'private void SetName(GoodsIssueLineDto line, string? text)', '/// <summary>\n    /// انتخاب کالا از موجودی انبار')
        self.assertIn('line.ProductName = text ?? "";', set_name)
        self.assertIn('if (line.ProductId <= 0) return;', set_name)
        self.assertIn('!string.Equals(line.ProductName.Trim(), chosen.Name, StringComparison.Ordinal)', set_name)
        self.assertIn('line.ProductId = 0;', set_name)

    def test_options_load_never_blocks_manual_entry(self):
        loader = section(PAGE, 'private async Task LoadProductOptionsAsync(bool force = false)', 'private static string ProductInfoText')
        self.assertIn('await IssueSvc.GetProductOptionsAsync() ?? new()', loader)
        self.assertIn('productOptions = new();', loader)
        self.assertIn('productLookup = new();', loader)
        self.assertIn('کالا را دستی وارد کنید', loader)
        self.assertIn('await LoadProductOptionsAsync();', PAGE)          # در ساخت حوالهٔ تازه
        self.assertIn('if (productOptionsLoading || (productLookup.Count > 0 && !force)) return;', loader)

    def test_stock_hint_and_over_stock_warning(self):
        self.assertIn('private GoodsIssueProductOptionDto? SelectedProduct(GoodsIssueLineDto line)', PAGE)
        self.assertIn('private bool OverStock(GoodsIssueLineDto line)', PAGE)
        self.assertIn('موجودی انبار: @Fa.Digits(chosen.TotalStock.ToString("#,##0.###")) @chosen.Unit', PAGE)
        self.assertIn('بیش از موجودی', PAGE)
        # بیجک چیزی از موجودی کم نمی‌کند؛ هشدار فقط راهنمای انتخاب است.
        self.assertIn('بیجک موجودی را کم نمی‌کند', PAGE)

    def test_guide_documents_both_ways(self):
        self.assertIn('دو روش افزودن قلم', GUIDE)
        self.assertIn('از فهرست کالاهای انبار انتخاب کنید', GUIDE)
        self.assertIn('نام کالا را دستی تایپ کنید', GUIDE)


class BehaviourMirror(unittest.TestCase):
    """آینهٔ منطق فرم: انتخاب از انبار، ویرایش نام و پاک‌کردن انتخاب."""

    class Line:
        def __init__(self):
            self.product_id = 0
            self.name = ""
            self.quantity = 0

    PRODUCTS = {7: 'کیبورد بی‌سیم'}

    def pick(self, line, product_id):
        name = self.PRODUCTS.get(product_id)
        if name is None:
            line.product_id = 0
            return
        line.product_id = product_id
        line.name = name
        if line.quantity <= 0:
            line.quantity = 1

    def type_name(self, line, text):
        line.name = text or ""
        if line.product_id <= 0:
            return
        chosen = self.PRODUCTS.get(line.product_id)
        if chosen is None or line.name.strip() != chosen:
            line.product_id = 0

    def test_pick_then_rename_falls_back_to_manual(self):
        line = self.Line()
        self.pick(line, 7)
        self.assertEqual((line.product_id, line.name, line.quantity), (7, 'کیبورد بی‌سیم', 1))
        self.type_name(line, 'کیبورد بی‌سیم')
        self.assertEqual(line.product_id, 7)          # همان نام → انتخاب حفظ می‌شود
        self.type_name(line, 'کیبورد بی‌سیم مشکی')
        self.assertEqual(line.product_id, 0)          # نام دستی → سطر آزاد
        self.assertEqual(line.name, 'کیبورد بی‌سیم مشکی')

    def test_manual_only_line_is_untouched(self):
        line = self.Line()
        self.type_name(line, 'پیچ گوشتی')
        self.assertEqual((line.product_id, line.name), (0, 'پیچ گوشتی'))
        self.pick(line, 999)                          # کالای ناموجود
        self.assertEqual(line.product_id, 0)


if __name__ == '__main__':
    unittest.main()
