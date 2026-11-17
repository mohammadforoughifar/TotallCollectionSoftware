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
CACHE = 'src/Inventory.Api/Services/ItAssets/KavenegarSmsSettingsCache.cs'
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

    def test_gateway_prefers_the_cache_and_falls_back_to_the_file(self):
        gateway = read(GATEWAY)
        # Cached value first, appsettings.json second.
        self.assertIn('_cache?.Current ?? _file', gateway)
        # The synchronous surface the queues and tests rely on is unchanged.
        for prop in ('bool IsEnabled', 'bool IsConfigured', 'bool IsLookupConfigured'):
            self.assertIn(prop, gateway)

    def test_sync_gateway_path_never_touches_the_database(self):
        """The regression this guards: a blocking query behind IsEnabled starved the thread
        pool, and the resulting failure was misreported as "SMS disabled" on the work order."""
        gateway = read(GATEWAY)
        for forbidden in ('AppDbContext', 'db.AppSettings', 'FirstOrDefault(',
                          'AsNoTracking', 'Microsoft.EntityFrameworkCore'):
            self.assertNotIn(forbidden, gateway,
                             'the gateway must not reach the database on its synchronous path')

    def test_cache_reads_async_logs_failures_and_keeps_last_good_value(self):
        cache = read(CACHE)
        # Async only; a synchronous query here is what caused the outage.
        self.assertIn('await db.AppSettings', cache)
        self.assertIn('FirstOrDefaultAsync', cache)
        self.assertNotIn('.FirstOrDefault()', cache)
        # A failed read must be visible and must not disable sending.
        self.assertRegex(cache, r'catch \(Exception ex\)')
        self.assertIn('_log.LogWarning(ex', cache)
        failure_block = cache[cache.index('catch (Exception ex)'):]
        self.assertNotIn('_stored = null', failure_block.split('public void Apply')[0],
                         'a failed refresh must keep the last known good settings')
        # An empty key means "nothing stored", so the file values apply again.
        self.assertIn('string.IsNullOrEmpty(key)', cache)

    def test_cache_is_a_singleton_refreshed_in_the_background_and_primed_at_startup(self):
        program = read('src/Inventory.Api/Program.cs')
        self.assertIn('AddSingleton<Inventory.Api.Services.ItAssets.KavenegarSmsSettingsCache>', program)
        self.assertIn('AddHostedService<Inventory.Api.Services.ItAssets.KavenegarSmsSettingsRefresher>', program)
        # Primed right after the migrations so the first request sees real settings.
        self.assertIn('smsCache.RefreshAsync(', program)
        self.assertLess(program.index('DbInitializer.InitializeAsync(app);'),
                        program.index('smsCache.RefreshAsync('))

    def test_saving_settings_updates_the_cache_immediately(self):
        service = read(SERVICE)
        self.assertIn('_smsCache.Apply(s.KavenegarApiKey, s.KavenegarSender, s.KavenegarSdkSendEnabled', service)
        save = service[service.index('SaveSettingsAsync'):]
        self.assertLess(save.index('await _db.SaveChangesAsync();'), save.index('_smsCache.Apply('))

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
