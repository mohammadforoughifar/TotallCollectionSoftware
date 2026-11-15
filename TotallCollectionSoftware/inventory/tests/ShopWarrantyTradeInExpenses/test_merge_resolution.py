"""Source-level regressions for merging repair/warranty work with the newer shop UI.

These checks do not replace compiling Razor or running the .NET/EF integration tests.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8")


class RepairMergeResolutionTests(unittest.TestCase):
    def test_party_validation_and_warranty_history_guard_both_remain(self):
        src = read("src/Inventory.Api/Services/Catalog/InventoryService.cs")
        block = src.split("public async Task<Party> SavePartyAsync", 1)[1].split(
            "public async Task DeletePartyAsync", 1
        )[0]
        for expected in (
            "ShopIdentifierRules.ValidateParty(dto)",
            "BeginTransactionAsync(IsolationLevel.Serializable)",
            "entity.Type != dto.Type",
            "_db.ShopWarranties.AnyAsync",
            "_db.RepairOrders.AnyAsync",
            "await transaction.CommitAsync()",
            "await transaction.RollbackAsync()",
        ):
            self.assertIn(expected, block)
        self.assertLess(block.index("ShopIdentifierRules.ValidateParty(dto)"), block.index("await _db.SaveChangesAsync()"))
        self.assertEqual(block.count("entity.RegisteredAt ="), 1)

    def test_repair_details_keep_fractional_quantities_and_warranty_totals(self):
        ui = read("src/Inventory.Client/Pages/Repairs/Repairs.razor")
        table = ui.split('<h6 class="mb-0">کارها و قطعات مصرفی</h6>', 1)[1].split("</table>", 1)[0]
        self.assertIn("@i.Quantity.Qty()", table)
        self.assertIn("@RepairRowCost(viewing, i).Money()", table)
        self.assertIn("@RepairRowPrice(viewing, i).Money()", table)
        self.assertIn("RepairRowPrice(viewing, i) - RepairRowCost(viewing, i)", table)
        self.assertIn("item.EffectiveUnitCost", ui)
        self.assertIn("item.CustomerUnitPrice", ui)

    def test_both_quick_creation_and_warranty_credit_dialogs_remain(self):
        repairs = read("src/Inventory.Client/Pages/Repairs/Repairs.razor")
        warranty = read("src/Inventory.Client/Pages/Repairs/WarrantyTab.razor")
        for ui in (repairs, warranty):
            self.assertIn("<RepairCreditDialog", ui)
            self.assertIn("<TechnicianFormModal", ui)
            self.assertIn('<PartyFormModal ShopQuickCreate="true"', ui)
        self.assertIn('Visible="showTechnicianForm"', repairs)
        self.assertIn('Visible="showNewTechnician"', warranty)
        self.assertIn('Visible="showNewCustomer"', warranty)
        self.assertNotIn('@bind="techForm.', repairs)

    def test_warranty_fixture_includes_main_print_settings_dependency(self):
        fixture = read("tests/WarrantyChargeContracts/WarrantyChargeContracts.csproj")
        self.assertIn('Link="ShopPrintSettingsDto.cs"', fixture)
        self.assertIn('Link="SaleInvoicePrint.cs"', fixture)


if __name__ == "__main__":
    unittest.main()
