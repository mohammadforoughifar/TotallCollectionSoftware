"""Display-only module rename contracts; no server/browser or destination DB access."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
CLIENT = ROOT / 'src/Inventory.Client'

def source(path):
    return (CLIENT / path).read_text(encoding='utf-8')

class ModuleBranding(unittest.TestCase):
    def test_six_navigation_module_names(self):
        nav = source('Layout/NavMenu.razor')
        for name in ['اتوماسیون اداری توکا', 'دستورکار رایکا', 'پیام رسان میها', 'فروشگاهی کایر', 'مودیان تیسا']:
            self.assertIn(name + '</span>', nav)
        self.assertIn('ERP فیکا</span>', source('Shared/ErpWorkspaceNav.razor'))

    def test_primary_titles_and_tabs_match(self):
        work = source('Pages/ItAssets/WorkOrders.razor')
        self.assertIn('<PageTitle>دستورکار رایکا</PageTitle>', work)
        self.assertIn('Layout.SetTitle("دستورکار رایکا")', work)
        self.assertIn('پیام رسان میها', source('Pages/Chat/ChatPage.razor'))
        self.assertIn('<PageTitle>مودیان تیسا — نمای کلی</PageTitle>', source('Pages/Moadian/MoadianDashboard.razor'))
        self.assertIn('<PageTitle>فروشگاهی کایر — راهنمای کاربری</PageTitle>', source('Pages/Shop/ShopGuide.razor'))
        tabs = source('Services/TabService.cs')
        self.assertIn('"دستورکار رایکا"', tabs)
        self.assertIn('"مودیان تیسا — نمای کلی"', tabs)

    def test_permission_labels_use_new_names(self):
        roles = source('Pages/Settings/Roles.razor')
        for name in ['اتوماسیون اداری توکا', 'دستورکار رایکا', 'ERP فیکا', 'پیام رسان میها', 'فروشگاهی کایر', 'مودیان تیسا']:
            self.assertIn(name, roles)
        for key in ['WorkOrders', 'Moadian', 'Chat', 'InnerLetters', 'OutgoingLetters']:
            self.assertIn(f'"{key}"', roles)

    def test_module_route_and_license_ids_stay_stable(self):
        text = source('Services/LicenseMenuMap.cs')
        for route, code, title in [('work-orders', 'workorders', 'دستورکار رایکا'), ('moadian', 'moadian', 'مودیان تیسا'), ('letters', 'letters', 'اتوماسیون اداری توکا'), ('chat', 'chat', 'پیام رسان میها')]:
            self.assertIn(f'new("{route}", "{code}", "{title}")', text)
        codes = (ROOT / 'src/Inventory.Licensing/Modules/ModuleCodes.cs').read_text(encoding='utf-8')
        for key, code, title in [('WorkOrders', 'workorders', 'دستورکار رایکا'), ('Letters', 'letters', 'اتوماسیون اداری توکا'), ('Chat', 'chat', 'پیام رسان میها'), ('Moadian', 'moadian', 'مودیان تیسا')]:
            self.assertIn(f'public const string {key} = "{code}";', codes)
            self.assertRegex(codes, rf'new\({key},\s+"{title}"\)')

    def test_saved_incoming_letter_source_value_stays_compatible(self):
        text = source('Pages/Office/Letters/Incoming/ComposeIncomingModal.razor')
        self.assertIn('<option value="اتوماسیون اداری">اتوماسیون اداری توکا</option>', text)

    def test_auth_guards_and_routes_are_not_brand_names(self):
        nav = source('Layout/NavMenu.razor')
        for guard in ['Auth.CanSee("Chat")', 'Auth.CanSee("InnerLetters")', 'Auth.CanSee("OutgoingLetters")', 'Auth.CanSee("WorkOrders")']:
            self.assertIn(guard, nav)
        for path in ['chat', 'work-orders', 'moadian']:
            self.assertIn(f'href="{path}"', nav)
        for title in ['اتوماسیون اداری توکا', 'دستورکار رایکا', 'ERP فیکا', 'پیام رسان میها', 'فروشگاهی کایر', 'مودیان تیسا']:
            self.assertNotIn(f'Auth.Has("{title}")', nav)

    def test_government_service_reference_not_rebranded(self):
        # Product name changes; legal/external tax-service terminology remains accurate.
        roles = source('Pages/Settings/Roles.razor')
        self.assertIn('"ارسال صورتحساب به سامانه مودیان"', roles)

if __name__ == '__main__':
    unittest.main()
