"""SMS settings live in the database so they survive a publish, and the key stays write-only.

Source/configuration checks only; there is no .NET toolchain in this environment.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]


def read(path):
    return (ROOT / path).read_text(encoding='utf-8')


ENTITY = 'src/Inventory.Api/Entities/Core/AppSetting.cs'
SCHEMA = 'src/Inventory.Api/Data/AppSettingsSchemaV1.cs'
SERVICE = 'src/Inventory.Api/Services/Catalog/InventoryService.cs'
GATEWAY = 'src/Inventory.Api/Services/Notification/KavenegarSmsGateway.cs'
FACTORY = 'src/Inventory.Api/Services/Notification/KavenegarSmsGatewayFactory.cs'
DTO = 'src/Inventory.Shared/Dtos/CatalogDtos.cs'
PAGE = 'src/Inventory.Client/Pages/Settings/Index.razor'

FIELDS = ('KavenegarApiKey', 'KavenegarSender', 'KavenegarSdkSendEnabled')


class KavenegarDatabaseSettings(unittest.TestCase):
    def test_settings_entity_carries_the_three_sms_columns(self):
        entity = read(ENTITY)
        self.assertRegex(entity, r'string\?\s+KavenegarApiKey\s*\{\s*get;\s*set;\s*\}')
        self.assertRegex(entity, r'string\?\s+KavenegarSender\s*\{\s*get;\s*set;\s*\}')
        # Default-off: an upgraded installation must not start sending on its own.
        self.assertRegex(entity, r'bool\s+KavenegarSdkSendEnabled\s*\{\s*get;\s*set;\s*\}(?!\s*=\s*true)')

    def test_columns_are_added_automatically_on_both_providers(self):
        schema = read(SCHEMA)
        for field in FIELDS:
            # SQL Server: guarded by COL_LENGTH so a second startup is a no-op.
            self.assertRegex(schema, r"COL_LENGTH\(N'dbo\.AppSettings', N'" + field + r"'\) IS NULL")
            self.assertRegex(schema, r'ALTER TABLE dbo\.AppSettings ADD ' + field)
            # SQLite: guarded by the pragma lookup.
            self.assertIn('"AppSettings", "%s"' % field, schema)
            self.assertIn('ALTER TABLE AppSettings ADD COLUMN %s' % field, schema)
        # Still reached from the single startup call site, so no manual migration step.
        self.assertIn('AppSettingsSchemaV1.EnsureAsync', read('src/Inventory.Api/Data/DbInitializer.cs'))

    def test_api_never_returns_the_stored_key(self):
        service = read(SERVICE)
        projection = service[service.index('GetSettingsAsync'):service.index('SaveSettingsAsync')]
        # The read path hands back a mask, never the secret itself.
        self.assertIn('KavenegarApiKey = null', projection)
        self.assertIn('KavenegarApiKeyHint = MaskKey(', projection)
        self.assertNotRegex(projection, r'KavenegarApiKey\s*=\s*s\.KavenegarApiKey')
        # The mask keeps at most the last four characters.
        self.assertIn('trimmed[^4..]', service)

    def test_blank_key_keeps_the_stored_one_and_clearing_is_explicit(self):
        service = read(SERVICE)
        save = service[service.index('SaveSettingsAsync'):]
        # Because the key is never sent to the browser, an ordinary save posts it back
        # empty; that must not wipe the key.
        self.assertIn('if (dto.ClearKavenegarApiKey) s.KavenegarApiKey = null;', save)
        self.assertRegex(save, r'else if \(!string\.IsNullOrWhiteSpace\(dto\.KavenegarApiKey\)\)')
        self.assertIn('s.KavenegarSdkSendEnabled = dto.KavenegarSdkSendEnabled;', save)

    def test_settings_editor_and_worker_use_one_deterministic_global_row(self):
        ctx = read('src/Inventory.Api/Data/AppDbContext.cs')
        self.assertIn('GlobalSettings => AppSettings.OrderBy(s => s.Id)', ctx)
        editor = read(SERVICE).split('public async Task<AppSettings> GetSettingsAsync()', 1)[1].split('private static string? MaskKey', 1)[0]
        self.assertEqual(editor.count('_db.GlobalSettings.'), 2)  # GET and POST
        self.assertIn('await db.GlobalSettings.AsNoTracking()', read(FACTORY))

    def test_db_account_replaces_all_three_fields_without_mutating_file_options(self):
        factory = read(FACTORY)
        self.assertIn('row is null || string.IsNullOrWhiteSpace(row.KavenegarApiKey) ? file', factory)
        self.assertIn('file.WithAccount(row.KavenegarApiKey.Trim(), row.KavenegarSender?.Trim() ?? "", row.KavenegarSdkSendEnabled)', factory)
        # Reading a setting must not silently convert a DB failure into "file wins".
        failure = factory.split('catch (Exception ex) when (ex is not OperationCanceledException)', 1)[1].split('private static bool', 1)[0]
        self.assertIn('return null;', failure)
        self.assertNotIn('new KavenegarSmsGateway', failure)
        self.assertNotIn('ex.Message', failure)
        self.assertIn('log.LogWarning(', failure)

    def test_staging_and_each_worker_job_resolve_before_claim(self):
        for queue in ('ItAssets/WorkOrderSmsQueue.cs', 'Repairs/RepairSmsQueue.cs'):
            stage, dispatch = read('src/Inventory.Api/Services/' + queue).split('public async Task<bool> ProcessOneAsync(', 1)
            self.assertIn('await gateways.CreateAsync(', stage)
            self.assertLess(dispatch.index('if (id is null)'), dispatch.index('await gateways.CreateAsync(ct)'))
            self.assertLess(dispatch.index('if (gateway is null) return false;'), dispatch.index('var claimed ='))

    def test_dto_and_settings_page_expose_the_fields(self):
        dto = read(DTO)
        for field in FIELDS + ('KavenegarApiKeyHint', 'ClearKavenegarApiKey'):
            self.assertIn(field, dto)
        page = read(PAGE)
        for field in FIELDS:
            self.assertIn('model.%s' % field, page)
        # The key box must not echo the secret back into the DOM.
        key_input = re.search(r'<input[^>]*model\.KavenegarApiKey"[^>]*/>', page, re.S)
        self.assertIsNotNone(key_input, 'the API key input was not found on the settings page')
        self.assertIn('type="password"', key_input.group(0))
        self.assertNotIn('model.KavenegarApiKeyHint', key_input.group(0))


if __name__ == '__main__':
    unittest.main()
