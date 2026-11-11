"""Source contracts and isolated date/status policy examples; not a .NET integration test."""
from pathlib import Path
from datetime import datetime, timedelta
import unittest
ROOT=Path(__file__).resolve().parents[2]/'src'
SERVICE=(ROOT/'Inventory.Api/Services/Catalog/InventoryService.cs').read_text()
PAGE=(ROOT/'Inventory.Client/Pages/Shop/ShopCreditManagement.razor').read_text()
HOME=(ROOT/'Inventory.Client/Shared/AdminHome.razor').read_text()
DASH=SERVICE.split('public async Task<AdminDashboard> GetAdminDashboardAsync',1)[1].split('public async Task<List<ReferrerProductItem>>',1)[0]
class DashboardFilterTests(unittest.TestCase):
    def test_four_cards_are_native_keyboard_buttons(self):
        self.assertEqual(PAGE.count('class="fin-kpi-card kpi-filter'),4)
        for key in ('inhand','bounced','pending','collected'):
            self.assertIn(f'ApplyQuickFilter("{key}")',PAGE)
            self.assertIn(f'IsQuickFilterActive("{key}")',PAGE)
        self.assertIn('.kpi-filter:focus-visible',PAGE)
    def test_card_switches_tab_and_clears_stale_search(self):
        method=PAGE.split('private void ApplyQuickFilter',1)[1].split('private void SetTab',1)[0]
        self.assertIn('chequeSearch = installmentSearch = chequeTxnTypeFilter = ""',method)
        self.assertIn('activeTab = "installments"; installmentPaidFilter = "false"',method)
        self.assertIn('chequeStatusFilter = ((int)ChequeStatus.Cleared).ToString(); installmentPaidFilter = "true"',method)
        self.assertIn('ApplyChequeFilter(); ApplyInstallmentFilter();',method)
        self.assertIn('private void ResetQuickFilters()',method)
    def test_inhand_does_not_include_endorsed_or_legacy_cleared(self):
        filt=PAGE.split('private void ApplyChequeFilter()',1)[1].split('private void ApplyInstallmentFilter()',1)[0]
        self.assertIn('c.Status != ChequeStatus.Endorsed',filt)
        self.assertIn('c.Status != ChequeStatus.Cleared',filt)
    def test_alerts_use_full_today_and_only_customer_installments(self):
        self.assertIn('var tomorrow = today.AddDays(1)',DASH)
        self.assertIn('t.DueDate < tomorrow',DASH)
        self.assertIn('i.DueDate < tomorrow',DASH)
        self.assertIn('t.Type != TransactionType.Sale) continue',DASH)
        credit=DASH.split('var creditTxns =',1)[1].split('var overdueIds',1)[0]
        self.assertNotIn('PaymentMethod.Credit ||',credit)
        self.assertIn('t.Type == TransactionType.Sale',credit)
    def test_alerts_have_real_sources_and_exclude_terminal_states(self):
        openq=DASH.split('var openCheques =',1)[1].split('if (uncleanedCheques.Count',1)[0]
        for token in ('_db.Cheques.AsNoTracking()', 'ChequeStatus.Cleared', 'ChequeStatus.Cancelled', 'ChequeStatus.Endorsed', '!cancelledAll.Contains', 'c.Status == ChequeStatus.Bounced'):
            self.assertIn(token,openq)
        self.assertNotIn('_db.TrsCheques',openq)
        self.assertIn('else if (t?.Type == TransactionType.Sale)',DASH)
    def test_empty_state_distinguishes_future_from_no_records(self):
        for prop in ('PendingReceivedChequesCount','PendingIssuedChequesCount'):
            self.assertIn(prop,DASH)
            self.assertIn(prop,HOME)
        self.assertIn('موعد آن‌ها در آینده است',HOME)
        self.assertIn('بارگذاری داشبورد کامل نشد',HOME)
    def test_date_status_policy_examples(self):
        today=datetime(2026,11,9);tomorrow=today+timedelta(days=1)
        # Names here describe a policy fixture, not database entities.
        cases=[('today-midday',today+timedelta(hours=12),'inhand',False,False,True),
               ('tomorrow',tomorrow,'inhand',False,False,False),
               ('future-bounced',tomorrow,'bounced',False,False,True),
               ('legacy-cleared',today,'cleared',False,False,False),
               ('cleared-flag',today,'inhand',True,False,False),
               ('endorsed',today,'endorsed',False,False,False),
               ('cancelled-sale',today,'inhand',False,True,False)]
        for name,due,status,cleared,cancelled,expected in cases:
            actual=not cleared and status not in ('cleared','cancelled','endorsed') and not cancelled and (due<tomorrow or status=='bounced')
            self.assertEqual(actual,expected,name)
if __name__=='__main__':unittest.main()
