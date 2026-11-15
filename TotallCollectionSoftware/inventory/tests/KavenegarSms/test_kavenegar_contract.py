"""Source/configuration checks only; see the C# offline SDK test for runtime behavior."""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'src'


def read(path):
    return (ROOT / path).read_text(encoding='utf-8')


class KavenegarFoundationContracts(unittest.TestCase):
    def test_official_package_is_pinned_only_in_server_project(self):
        api = ET.parse(SRC / 'Inventory.Api/Inventory.Api.csproj')
        packages = [p for p in api.findall('.//PackageReference') if p.get('Include') == 'Kavenegar']
        self.assertEqual([(p.get('Include'), p.get('Version')) for p in packages], [('Kavenegar', '2.0.1')])
        for name in ('Inventory.Client', 'Inventory.Shared'):
            project = ET.parse(SRC / name / f'{name}.csproj')
            self.assertFalse(any(p.get('Include') == 'Kavenegar' for p in project.findall('.//PackageReference')))

    def test_new_gateway_is_registered_for_repair_outbox_without_controller_or_other_modules(self):
        program = read('src/Inventory.Api/Program.cs')
        self.assertIn('AddScoped<IKavenegarSmsGateway, KavenegarSmsGateway>()', program)
        uses = []
        for path in SRC.rglob('*'):
            if path.is_file() and path.suffix in ('.cs', '.razor') and 'IKavenegarSmsGateway' in path.read_text(encoding='utf-8'):
                uses.append(path.relative_to(ROOT).as_posix())
        self.assertEqual(set(uses), {
            'src/Inventory.Api/Program.cs',
            'src/Inventory.Api/Services/Notification/KavenegarSmsGateway.cs',
            'src/Inventory.Api/Services/Repairs/RepairService.cs',
            'src/Inventory.Api/Services/Repairs/RepairSmsQueue.cs',
        })

    def test_sdk_gateway_is_disabled_by_default_and_key_is_blank(self):
        options = read('src/Inventory.Api/Services/ItAssets/KavenegarSmsOptions.cs')
        settings = read('src/Inventory.Api/appsettings.json')
        block = re.search(r'"KavenegarSms"\s*:\s*\{([^}]*)\}', settings, re.S).group(1)
        self.assertRegex(block, r'"ApiKey"\s*:\s*""')
        self.assertRegex(block, r'"SdkSendEnabled"\s*:\s*false')
        self.assertRegex(options, r'bool\s+SdkSendEnabled\s*\{\s*get;\s*set;\s*\}(?!\s*=\s*true)')
        gateway = read('src/Inventory.Api/Services/Notification/KavenegarSmsGateway.cs')
        self.assertLess(gateway.index('if (!IsEnabled)'), gateway.index('var api = new KavenegarApi'))
        self.assertLess(gateway.index('if (!IsConfigured)'), gateway.index('var api = new KavenegarApi'))

    def test_existing_sms_services_keep_their_own_configuration_and_registration(self):
        program = read('src/Inventory.Api/Program.cs')
        work = read('src/Inventory.Api/Services/ItAssets/WorkOrderSmsService.cs')
        profile = read('src/Inventory.Api/Services/Core/UserContactOtpDelivery.cs')
        self.assertIn('IWorkOrderSmsService, Inventory.Api.Services.ItAssets.WorkOrderSmsService', program)
        self.assertIn('IUserContactOtpDelivery, Inventory.Api.Services.Core.UserContactOtpDelivery', program)
        self.assertIn('ISmsSender, Inventory.Api.Services.FaCom.ConfigSmsSender', program)
        self.assertNotIn('SdkSendEnabled', work)
        self.assertNotIn('SdkSendEnabled', profile)
        self.assertIn('/sms/send.json', work)
        self.assertIn('/sms/send.json', profile)

    def test_sdk_uses_non_redirecting_transport_and_does_not_log_secrets(self):
        gateway = read('src/Inventory.Api/Services/Notification/KavenegarSmsGateway.cs')
        self.assertIn('AllowAutoRedirect = false', gateway)
        self.assertIn('ActivityHeadersPropagator = null', gateway)
        self.assertIn('UseCookies = false', gateway)
        self.assertIn('Uri.EscapeDataString(_options.ApiKey.Trim())', gateway)
        self.assertIn('new KavenegarApi(key, http)', gateway)
        self.assertIn('api.SendAsync(', gateway)
        self.assertIn('api.VerifyLookupAsync(', gateway)
        self.assertIn('Uri.EscapeDataString(companyName)', gateway)
        self.assertIn('IsLookupConfigured', gateway)
        self.assertNotIn('CreateClient(', gateway)
        self.assertNotIn('ex.Message', gateway)
        self.assertNotRegex(gateway, r'Log(?:Information|Warning|Error|Debug)\s*\(')
        self.assertIn('capture.ProviderCode != 200', gateway)
        self.assertIn('KavenegarSmsSendState.Unknown', gateway)

    def test_sdk_fixture_uses_only_a_fake_handler(self):
        program = read('tests/KavenegarSms/Program.cs')
        self.assertIn('sealed class FakeHandler : HttpMessageHandler', program)
        self.assertIn('new(Options.Create(options), () => fake)', program)
        self.assertNotIn('SocketsHttpHandler', program)
        self.assertIn('HTTP 200 must not hide a provider-level rejection', program)
        self.assertIn('automatic retry', program)
        self.assertIn('Form decode preserves Persian', program)


if __name__ == '__main__':
    unittest.main()
