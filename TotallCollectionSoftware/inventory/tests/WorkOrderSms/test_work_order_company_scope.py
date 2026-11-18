"""Source-level regression checks; not a substitute for EF/API integration tests."""
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2] / 'src'
def read(path): return (ROOT / path).read_text(encoding='utf-8')
def section(src, start, end): return src[src.index(start):src.index(end, src.index(start))]

class WorkOrderCompanyScopeTests(unittest.TestCase):
    def test_targets_filtered_before_paging_and_sms_status_scoped(self):
        src = read('Inventory.Api/Controllers/ItAssets/WorkOrdersController.cs')
        targets = section(src, 'public async Task<IActionResult> Targets(', '/// <summary>وضعیت فعال')
        self.assertIn('ActiveCompanyIdAsync()', targets)
        self.assertLess(targets.index('EligibleUsers(_db, companyId.Value)'), targets.index('ToPageListAsync'))
        self.assertIn('WorkOrderAllowedAssignees', targets)
        status = section(src, 'public async Task<IActionResult> CompanySmsStatus(', '/// <summary>لیست مجاز')
        self.assertIn('HasAsync("Create")', status)
        self.assertIn('ActiveCompanyIdAsync()', status)
        self.assertIn('CompanySmsServicePolicy.GetEnabledRevisionAsync', status)
        self.assertIn('SmsModuleCatalog.WorkOrders', status)
        self.assertIn('return Ok(new { enabled })', status)
    def test_create_edit_add_enforce_company_access(self):
        src = read('Inventory.Api/Controllers/ItAssets/WorkOrdersController.cs')
        for begin, end in [('public async Task<IActionResult> Create(', 'public async Task<IActionResult> Update('),
                           ('public async Task<IActionResult> Update(', 'public class ListFilterDto'),
                           ('public async Task<IActionResult> AddAssignees(', '// ================== همگام')]:
            action = section(src, begin, end)
            self.assertIn('EligibleUsers(_db, companyId.Value)', action)
            self.assertIn('شرکت فعال', action)
        helper = read('Inventory.Api/Services/ItAssets/WorkOrderSmsQueue.cs')
        self.assertIn('EligibleUsers(db, companyId).AnyAsync', helper)
        self.assertIn('.Distinct().Count() == 1', helper)
    def test_ui_no_unscoped_prefill_or_stale_target_list(self):
        ui = read('Inventory.Client/Pages/ItAssets/WorkOrders.razor')
        self.assertIn('/api/workorders/sms-status', ui)
        self.assertIn('companySmsEnabled == true', ui)
        self.assertIn('companySmsEnabled == false', ui)
        self.assertIn('bi-check-circle-fill', ui)
        self.assertIn('bi-x-circle-fill', ui)
        self.assertNotIn('عنوانِ بیشتر از محدودیت الگو', ui)
        self.assertNotIn('targets.Add(new TargetOpt', ui)
        self.assertIn('unavailableTargets = extra.Where', ui)
        self.assertIn('selTargets.Any(id => !targets.Any', ui)
        self.assertIn('targets = new(); unavailableTargets = new(); companySmsEnabled = null;', ui)

if __name__ == '__main__': unittest.main()
