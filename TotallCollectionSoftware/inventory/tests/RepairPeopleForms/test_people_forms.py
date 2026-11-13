"""Source regression contracts; no live Blazor/API integration."""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[2]/'src/Inventory.Client'
PAGES=ROOT/'Pages/Repairs'
MAIN=(PAGES/'Repairs.razor').read_text()
TECH=(PAGES/'TechnicianFormModal.razor').read_text()
class RepairPeopleTests(unittest.TestCase):
    def test_customer_uses_existing_canonical_form_everywhere(self):
        for name in ('Repairs','WarrantyTab','TradeInTab'):
            page=(PAGES/f'{name}.razor').read_text()
            self.assertIn('<PartyFormModal ShopQuickCreate="true"',page)
            self.assertIn('Type="PartyType.Customer"',page)
            self.assertIn('تعریف مشتری جدید',page)
            self.assertIn('form.PartyId = created.Id',page)
            self.assertNotIn('PartySvc.CreateAsync',page)
    def test_admission_and_manager_share_technician_form(self):
        self.assertIn('@onclick="OpenNewTechnician"',MAIN)
        self.assertIn('@onclick="OpenManagedTechnician"',MAIN)
        self.assertIn('<TechnicianFormModal',MAIN)
        self.assertNotIn('@bind="techForm.',MAIN)
        self.assertNotIn('private async Task SaveTech()',MAIN)
        self.assertIn('selectCreatedTechnician && created.IsActive',MAIN)
        self.assertIn('techIdStr = created.Id.ToString()',MAIN)
        self.assertIn('<TechnicianFormModal', (PAGES/'WarrantyTab.razor').read_text())
    def test_technician_validates_and_saves_through_existing_service(self):
        self.assertIn('string.IsNullOrWhiteSpace(technician.Name)',TECH)
        self.assertIn('if (saving || !CanManage) return',TECH)
        self.assertIn('await RepairSvc.SaveTechnicianAsync(technician)',TECH)
        self.assertIn('await OnSaved.InvokeAsync(saved)',TECH)
        self.assertIn('ShopAccess.CanManage(Auth, "Repairs")',TECH)
    def test_cancel_works_on_copy_and_parent_draft_is_not_reset(self):
        self.assertIn('Visible && !wasVisible',TECH)
        self.assertIn('new Technician {',TECH)
        close=TECH.split('private async Task Close()')[1].split('private async Task Save()')[0]
        self.assertNotIn('SaveTechnician',close)
        saved=MAIN.split('private async Task OnTechnicianSaved')[1].split('private async Task ToggleTech')[0]
        self.assertNotIn('form =',saved)
        self.assertNotIn('showForm = false',saved)
        self.assertIn('technicians.Add(created)',saved)
    def test_forms_follow_shared_modal_and_grid_conventions(self):
        party=(ROOT/'Shared/PartyFormModal.razor').read_text()
        for token in ('<Modal ', '<ChildContent>', '<Footer>', 'row g-3', 'class="form-label"', 'class="form-control"', 'btn btn-light','btn btn-primary'):
            self.assertIn(token,TECH)
            self.assertIn(token,party)
if __name__=='__main__':unittest.main()
