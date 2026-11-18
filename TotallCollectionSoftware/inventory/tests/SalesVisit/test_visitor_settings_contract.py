"""Source contracts and SQLite schema checks (the API still needs runtime integration tests)."""
import re
import sqlite3
import unittest
from pathlib import Path

SRC = Path(__file__).resolve().parents[2] / 'src'
def read(path): return (SRC / path).read_text(encoding='utf-8')

class VisitorSettingsTests(unittest.TestCase):
    def test_sqlite_schema_is_idempotent_and_company_user_key_is_unique(self):
        source = read('Inventory.Api/Data/ShopVisitorDiscountSchemaV1.cs')
        sql = re.search(r'public const string Sqlite = @"(.*?)";', source, re.S).group(1)
        with sqlite3.connect(':memory:') as db:
            db.executescript(sql)
            db.executescript(sql)
            db.execute('INSERT INTO ShopVisitorDiscounts VALUES (1, 7, ?)', ('10',))
            db.execute('INSERT INTO ShopVisitorDiscounts VALUES (2, 7, ?)', ('20',))
            with self.assertRaises(sqlite3.IntegrityError):
                db.execute('INSERT INTO ShopVisitorDiscounts VALUES (1, 7, ?)', ('30',))
            self.assertEqual(db.execute('SELECT MaxDiscountPercent FROM ShopVisitorDiscounts WHERE CompanyId=2 AND UserId=7').fetchone()[0], '20')

    def test_settings_authorization_and_scope(self):
        source = read('Inventory.Api/Controllers/Catalog/ShopPrintSettingsController.cs')
        for required in ['CanAccessVisitorSettings(true)', 'Shop.PrintSettings.Manage',
                         'input.CompanyId != _db.ActiveCompanyId', 'ActiveVisitors(companyId)',
                         'u.IsActive', 'p.Action == "Create"', 'x.CompanyId == companyId']:
            self.assertIn(required, source)
        self.assertNotIn('entity.MaxDiscountPercent =', read('Inventory.Api/Services/Catalog/InventoryService.cs'))
        self.assertNotIn('fDiscountCap', read('Inventory.Client/Pages/Catalog/Referrers.razor'))

    def test_order_ownership_price_and_discount_checked_server_side(self):
        service = read('Inventory.Api/Services/SalesVisit/VisOrderService.cs')
        for required in ['o.CreatedByUserId == myUserId', 'if (mine) q = q.Where',
                         'ReferrerName = referrerName', 'var referrerName = actor.UserName;',
                         'x.UserId == entity.CreatedByUserId', 'l.DiscountAmount * 100m >',
                         'ShopPricingService.ResolveAsync(_db, productIds, DateTime.Today)',
                         'if (active == 0) throw', 'قیمت فروش تغییر کرده است']:
            self.assertIn(required, service)
        cartable = read('Inventory.Api/Services/Cartable/CartableService.cs')
        self.assertIn('o.CreatedByUserId == ctx.UserId', cartable)
        self.assertIn('Api.ListAsync(st, search, mine: true)', read('Inventory.Client/Pages/SalesVisit/MyVisits.razor'))


class SalesManagerDiscountPolicyTests(unittest.TestCase):
    def test_role_is_resolved_from_active_user_and_active_db_role(self):
        service = read('Inventory.Api/Services/SalesVisit/VisOrderService.cs')
        actor = service[service.index('public async Task<VisActor> ResolveActorAsync'):service.index('//  شرکت (کمپانی)')]
        for condition in ['dbUser?.IsActive == true', '_db.UserRoles.AsNoTracking()',
                          'ur.UserId == uid', 'role.IsActive && role.Name == "SalesManager"',
                          'IsSalesManager = isSalesManager']:
            self.assertIn(condition, actor)
        self.assertIn('IsSalesManager = actor.IsSalesManager', read('Inventory.Api/Controllers/SalesVisit/SalesVisitController.cs'))

    def test_save_and_submit_enforce_current_user_cap_without_approve_exemption(self):
        service = read('Inventory.Api/Services/SalesVisit/VisOrderService.cs')
        save = service[service.index('public async Task<VisOrderDto> SaveAsync'):service.index('public async Task<VisOrderDto> SubmitAsync')]
        submit = service[service.index('public async Task<VisOrderDto> SubmitAsync'):service.index('public async Task<VisOrderDto> ApproveAsync')]
        self.assertIn('var actorCap = actor.MaxDiscountPercent', save)
        self.assertIn('if (!actor.IsSalesManager && exceedsActorCap)', save)
        self.assertIn('l.DiscountAmount * 100m > l.Quantity * l.UnitPrice * actorCap', save)
        self.assertIn('var unitPrice = actor.CanEditVisitPrice ? l.UnitPrice', save)
        self.assertIn('if (!actor.IsSalesManager)', submit)
        self.assertIn('var cap = actor.MaxDiscountPercent', submit)
        self.assertNotIn('if (!actor.CanApprove && !actor.IsAdmin', save + submit)

    def test_ui_uses_same_role_flag(self):
        ui = read('Inventory.Client/Pages/SalesVisit/VisitForm.razor')
        self.assertIn('@if (me?.IsSalesManager == true)', ui)
        self.assertIn('Disabled="@(me?.CanEditVisitPrice != true)"', ui)
        self.assertIn('private bool OverLimit => me?.IsSalesManager != true', ui)
        self.assertIn('if (me?.IsSalesManager != true && me != null)', ui)
        self.assertIn('public bool IsSalesManager { get; set; }', read('Inventory.Shared/Dtos/SalesVisitDtos.cs'))

class VisitorReportAndPriceTests(unittest.TestCase):
    def test_report_is_filtered_in_query_before_all_aggregations(self):
        service = read('Inventory.Api/Services/SalesVisit/VisOrderService.cs')
        report = service[service.index('public async Task<VisRequestsReportDto> RequestsReportAsync'):service.index('private sealed record ReportLineRow')]
        self.assertIn('o.CompanyId == actor.CompanyId', report)
        self.assertIn('if (!actor.IsSalesManager)', report)
        self.assertIn('o.CreatedByUserId == actor.UserId', report)
        self.assertLess(report.index('o.CreatedByUserId == actor.UserId'), report.index('var orders = await query'))
        self.assertIn('ScopedToMe = !actor.IsSalesManager', report)
        self.assertNotIn('actor.CanSeeAll', report)
        self.assertIn('o.CreatedByUserId', report)
        self.assertIn('includeUnknown: true', report)

    def test_visitor_and_manager_combined_role_is_not_allowed_to_edit_price(self):
        service = read('Inventory.Api/Services/SalesVisit/VisOrderService.cs')
        self.assertIn('role.IsActive && role.Name == "SalesVisitor"', service)
        self.assertIn('CanEditVisitPrice => IsSalesManager && !IsSalesVisitor', service)
        self.assertIn('if (!actor.CanEditVisitPrice && l.UnitPrice != unitPrice)', service)
        self.assertIn('if (!actor.CanEditVisitPrice)', service[service.index('public async Task<VisOrderDto> SubmitAsync'):])
        ui = read('Inventory.Client/Pages/SalesVisit/VisitForm.razor')
        self.assertIn('ReadOnly="@(me?.CanEditVisitPrice != true)"', ui)
        self.assertIn('bg-light text-muted', ui)
        self.assertIn('Api.ProductPricesAsync(ids)', ui)
        self.assertIn('line.UnitPrice = prices[line.ProductId]', ui)
        self.assertIn('if (me?.CanEditVisitPrice != true) return;', ui)

    def test_visitor_can_reach_report_without_export_or_approve(self):
        controller = read('Inventory.Api/Controllers/SalesVisit/SalesVisitController.cs')
        self.assertIn('HasAnyAsync("Approve", "FinanceView", "View", "Create", "Export")', controller)
        for path in ['Inventory.Client/Layout/MainLayout.razor', 'Inventory.Client/Layout/NavMenu.razor']:
            src = read(path)
            start = src.index('sales/requests-report')
            self.assertIn('SalesVisit.View', src[max(0, start-400):start+500])

class MissingSalePriceWarningTests(unittest.TestCase):
    def test_warning_is_shown_for_selected_zero_price_and_in_lookup(self):
        ui = read('Inventory.Client/Pages/SalesVisit/VisitForm.razor')
        self.assertIn('data-missing-sale-price>', ui)
        self.assertIn('role="alert"', ui)
        self.assertIn('قیمت فروش این کالا ثبت نشده یا صفر است', ui)
        self.assertIn('data-missing-sale-price-option', ui)
        self.assertIn('line.ProductId > 0 && line.UnitPrice <= 0', ui)
        self.assertIn('product.SalePrice <= 0', ui)
        self.assertIn('Disabled="@(me?.CanEditVisitPrice != true)"', ui)

    def test_warning_does_not_add_a_zero_price_save_restriction(self):
        ui = read('Inventory.Client/Pages/SalesVisit/VisitForm.razor')
        save = ui[ui.index('private async Task SaveAsync(bool submit)'):ui.index('private async Task DoSaveAsync')]
        self.assertNotIn('MissingSalePrice(', save)

if __name__ == '__main__': unittest.main()
