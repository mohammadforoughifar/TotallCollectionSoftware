"""Source contracts for repair-row quantity presentation (not a browser/API test)."""
from pathlib import Path
import unittest

SRC = Path(__file__).resolve().parents[2] / 'src'
PAGE = (SRC / 'Inventory.Client/Pages/Repairs/Repairs.razor').read_text(encoding='utf-8')
SERVICE = (SRC / 'Inventory.Api/Services/Repairs/RepairService.cs').read_text(encoding='utf-8')
DTO = (SRC / 'Inventory.Shared/Dtos/RepairDtos.cs').read_text(encoding='utf-8')

class RepairQuantityDisplayTests(unittest.TestCase):
    def test_selected_and_reloaded_product_show_unit_and_kind_next_to_quantity(self):
        self.assertIn('value="@itemQty[i]"', PAGE)
        self.assertIn('واحد: @quantityProduct.Unit', PAGE)
        self.assertIn('QuantityKind(quantityProduct.AllowsFractionalQuantity)', PAGE)
        self.assertIn('await LoadSelectedProductsAsync()', PAGE)
        self.assertIn('itemProducts[i] = product;', PAGE)
        self.assertIn('itemProducts[i] = null;', PAGE)
        self.assertIn('فقط مقدار صحیح', PAGE)
        self.assertIn('مقدار اعشاری مجاز', PAGE)
        self.assertIn('نوع مقدار نامشخص', PAGE)

    def test_view_contains_quantity_unit_and_rule(self):
        self.assertIn('@i.Quantity.Qty()', PAGE)
        self.assertIn('i.ProductUnit', PAGE)
        self.assertIn('QuantityKind(i.ProductAllowsFractionalQuantity)', PAGE)
        self.assertIn('public string? ProductUnit', DTO)
        self.assertIn('public bool? ProductAllowsFractionalQuantity', DTO)
        self.assertIn('ProductUnit = i.ProductId.HasValue', SERVICE)
        self.assertIn('GroupBy(u => (u.CompanyId, u.Name.Trim()))', SERVICE)
        self.assertIn('ShopQuantityRules.DefaultAllowsFraction', SERVICE)

if __name__ == '__main__': unittest.main()
