"""قرارداد سورس برای «نسیه‌های خرید» در داشبورد مالی؛ آزمون EF/Blazor نیست."""
from pathlib import Path
import unittest

SRC = Path(__file__).resolve().parents[2] / 'src'
HOME = (SRC / 'Inventory.Client/Shared/AdminHome.razor').read_text()
SERVICE = (SRC / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text()
DTO = (SRC / 'Inventory.Shared/Dtos/OperationDtos.cs').read_text()
ADMIN = SERVICE.split('public async Task<AdminDashboard> GetAdminDashboardAsync()', 1)[1] \
    .split('public async Task<List<ReferrerProductItem>>', 1)[0]


class DashboardPurchaseCreditsTests(unittest.TestCase):
    def test_dto_exposes_purchase_credit_list_and_total(self):
        self.assertIn('public List<DebtorItem> OpenPurchaseCredits', DTO)
        self.assertIn('public decimal OpenPurchaseCreditsTotal', DTO)

    def test_service_reads_purchase_documents_only(self):
        block = ADMIN.split('// ---------- نسیه‌های خرید (بدهی ما به تأمین‌کننده) ----------', 1)[1] \
            .split('// چک‌های دریافتی از مشتری و چک‌های پرداختی ما', 1)[0]
        self.assertIn('t.Type == TransactionType.Purchase', block)
        self.assertIn('t.PartyId != null', block)
        self.assertNotIn('IgnoreQueryFilters', block)

    def test_service_reuses_confirmed_credit_receipts_and_keeps_open_only(self):
        block = ADMIN.split('// ---------- نسیه‌های خرید (بدهی ما به تأمین‌کننده) ----------', 1)[1] \
            .split('// چک‌های دریافتی از مشتری و چک‌های پرداختی ما', 1)[0]
        # پرداخت‌های نسیه ثبت‌شده (بدون لغو) از سهم نسیه کسر می‌شوند
        self.assertIn('_db.ShopOrderCreditReceipts.AsNoTracking()', block)
        self.assertIn('r.ReversalDate == null', block)
        # سهم نسیه = مبلغ − نقد − چک − اقساط − پرداخت نسیه (همان اجزای پرداخت ترکیبی)
        for part in ('t.Amount', 'CashOf(t)', 'ChequeOf(t)', 'InstOf(t)', 'purchaseCreditPaid.GetValueOrDefault(t.Id)'):
            self.assertIn(part, block)
        # سقف ماندهٔ ناخالص، تا سند قدیمیِ تسویه‌شده به‌اشتباه «نسیهٔ باز» نشود
        self.assertIn('t.Amount - Math.Max(0m, t.SettledAmount)', block)
        self.assertIn('Math.Min(creditShare, unpaidGross)', block)
        self.assertIn('if (outstanding == 0) continue;', block)
        self.assertIn('dash.OpenPurchaseCreditsTotal = dash.OpenPurchaseCredits.Sum', block)

    def test_dashboard_has_sale_and_purchase_credit_tabs(self):
        self.assertIn('private string creditTab = "sale";', HOME)
        for key in ('sale', 'purchase'):
            self.assertIn(f'creditTab = "{key}"', HOME)
        self.assertIn('نسیه‌های فروش', HOME)
        self.assertIn('نسیه‌های خرید', HOME)
        self.assertIn('admin.OpenPurchaseCredits', HOME)
        self.assertIn('admin.OpenPurchaseCreditsTotal', HOME)
        # تب فروش همان لیست قبلی را نگه می‌دارد؛ رگرسیون داده‌ای ایجاد نشود
        self.assertIn('admin.OverdueDebtors', HOME)

    def test_empty_purchase_tab_explains_how_to_register(self):
        self.assertIn('نسیهٔ خریدِ پرداخت‌نشده‌ای ثبت نشده است', HOME)
        self.assertIn('orders/purchase', HOME)


if __name__ == '__main__':
    unittest.main()
