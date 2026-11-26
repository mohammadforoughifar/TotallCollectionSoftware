"""قراردادهای منبع برای نوار فیلتر و بارگذاری فهرست بیجک.

هدف: «اعمال فیلتر» نباید اسپینر بی‌پایان بگذارد و فهرست نباید کل جدول را
با همهٔ سطرهایش بخواند. همچنین ترتیب نوار فیلتر باید جستجو → مشتری (فشرده)
→ بازهٔ تاریخ → اعمال فیلتر باشد.

این تست‌ها قرارداد متنی‌اند و جایگزین بیلد Razor، تست زندهٔ API و تست مرورگر
نیستند (SDK دات‌نت در این محیط موجود نیست).

    cd inventory
    python3 tests/GoodsIssueFilter/test_goods_issue_filter.py -v
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'
PAGE = (ROOT / 'Inventory.Client/Pages/Catalog/GoodsIssues.razor').read_text(encoding='utf-8')
I_CLIENT = (ROOT / 'Inventory.Client/Services/IInventoryApi.cs').read_text(encoding='utf-8')
CLIENT_SVC = (ROOT / 'Inventory.Client/Services/InventoryApiServices.cs').read_text(encoding='utf-8')
SERVICE = (ROOT / 'Inventory.Api/Services/Catalog/GoodsIssueService.cs').read_text(encoding='utf-8')
CONTROLLER = (ROOT / 'Inventory.Api/Controllers/Catalog/GoodsIssuesController.cs').read_text(encoding='utf-8')
SCHEMA = (ROOT / 'Inventory.Api/Data/GoodsIssueSchemaV1.cs').read_text(encoding='utf-8')
DBCONTEXT = (ROOT / 'Inventory.Api/Data/AppDbContext.cs').read_text(encoding='utf-8')


def section(text, start, end):
    return text.split(start, 1)[1].split(end, 1)[0]


class FilterBarLayout(unittest.TestCase):
    """ترتیب و اندازهٔ فیلترها: جستجو اول، مشتری فشرده‌تر، بعد تاریخ‌ها."""

    def setUp(self):
        self.bar = section(PAGE, '<div class="gi-filters">', '@* ---------- فهرست حواله‌ها ---------- *@')

    def test_search_comes_first(self):
        self.assertLess(self.bar.index('gi-filter-search'), self.bar.index('gi-filter-party'))

    def test_customer_comes_before_dates(self):
        self.assertLess(self.bar.index('gi-filter-party'), self.bar.index('از تاریخ'))
        self.assertLess(self.bar.index('از تاریخ'), self.bar.index('تا تاریخ'))

    def test_apply_button_is_last(self):
        self.assertLess(self.bar.index('تا تاریخ'), self.bar.index('btn-apply-filter'))
        self.assertIn('id="btn-apply-filter"', self.bar)

    def test_search_field_is_the_wide_one_and_customer_is_compact(self):
        css = section(PAGE, '<style>', '</style>')
        self.assertIn('.gi-filter-search { flex: 1 1 320px; }', css)
        self.assertIn('.gi-filter-party { flex: 0 1 200px; max-width: 240px; }', css)
        self.assertIn('.gi-filter-date { flex: 0 1 135px; max-width: 160px; }', css)
        # موبایل: هر فیلتر یک ردیف کامل
        self.assertIn('@media (max-width: 767.98px)', css)
        self.assertIn('.gi-filter-search, .gi-filter-party, .gi-filter-date { flex: 1 1 100%; max-width: none; }', css)

    def test_apply_button_reports_progress_without_hiding_the_list_forever(self):
        self.assertIn('disabled="@loading"', self.bar)
        self.assertIn('spinner-border spinner-border-sm', self.bar)


class ClientLoadIsBounded(unittest.TestCase):
    """بارگذاری فهرست: await شده، لغوپذیر، نسخه‌دار و با سقف انتظار."""

    def setUp(self):
        self.load = section(PAGE, 'private async Task Load()', 'private static readonly TimeSpan LoadTimeout')

    def test_apply_filter_is_awaited_not_fire_and_forget(self):
        # علت اصلی اسپینر بی‌پایان: `_ = Load()` — رندر پایانی هرگز اجرا نمی‌شد.
        self.assertNotIn('_ = Load()', PAGE)
        self.assertIn('private async Task ApplyFilter() => await Load();', PAGE)
        self.assertIn('@onclick="ApplyFilter"', PAGE)

    def test_enter_key_awaits_the_same_load(self):
        self.assertIn('if (e.Key == "Enter") await Load();', PAGE)

    def test_every_load_cancels_the_previous_one(self):
        self.assertIn('var version = ++loadVersion;', self.load)
        self.assertIn('loadCts?.Cancel();', self.load)
        self.assertIn('var cts = new CancellationTokenSource(LoadTimeout);', self.load)

    def test_token_reaches_the_service(self):
        self.assertIn('IssueSvc.GetAsync(filterPartyId > 0 ? filterPartyId : null, from, to, search, cts.Token)', self.load)

    def test_stale_response_never_overwrites_the_list(self):
        self.assertIn('if (version != loadVersion) return;', self.load)
        self.assertIn('catch (OperationCanceledException) when (version != loadVersion)', self.load)

    def test_timeout_ends_the_spinner_with_a_clear_message(self):
        self.assertIn('private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(45);', PAGE)
        self.assertIn('LoadTimeoutMessage', PAGE)
        self.assertIn('Toast.Error(LoadTimeoutMessage);', self.load)
        self.assertIn('اگر', PAGE)

    def test_spinner_is_cleared_for_the_current_version(self):
        self.assertIn('if (version == loadVersion)', self.load)
        self.assertIn('loading = false;', self.load)

    def test_failures_empty_the_list_and_show_the_message(self):
        self.assertIn('catch (Exception ex)', self.load)
        self.assertIn('Toast.Error(ex.Message);', self.load)
        self.assertIn('items = new();', self.load)

    def test_leave_page_cancels_the_request(self):
        self.assertIn('@implements IDisposable', PAGE)
        dispose = section(PAGE, 'public void Dispose()', 'private async Task OnFilterPartyChanged')
        self.assertIn('++loadVersion;', dispose)
        self.assertIn('loadCts?.Cancel();', dispose)

    def test_record_actions_do_not_hide_the_list_spinner(self):
        # loading مخصوص فهرست است؛ ویرایش/مشاهده/حذف/چاپ با busy کار می‌کنند.
        self.assertIn('private bool busy;', PAGE)
        for method in ('private async Task OpenEdit(GoodsIssueDto g)', 'private async Task View(GoodsIssueDto g)',
                       'private async Task Print(GoodsIssueDto g)'):
            body = section(PAGE, method, 'finally {')
            self.assertIn('busy = true;', body)
            self.assertNotIn('loading = true;', body)

    def test_filter_bar_does_not_wait_for_heavy_lookups(self):
        init = section(PAGE, 'protected override async Task OnInitializedAsync()', 'private async Task LoadLookups()')
        self.assertIn('var lookups = LoadLookups();', init)
        self.assertLess(init.index('await Load();'), init.index('await lookups;'))
        lookups = section(PAGE, 'private async Task LoadLookups()', '/// <summary>')
        self.assertNotIn('await LoadProductOptionsAsync();', lookups)
        # فهرست کالاها همان‌جا که لازم است (فرم/انتخاب‌گر) خوانده می‌شود.
        self.assertIn('await LoadProductOptionsAsync();', section(PAGE, 'private async Task OpenNew()', 'private async Task OpenEdit'))


class ServerQueryIsBounded(unittest.TestCase):
    """سرور فقط تازه‌ترین‌ها را با سطرهایشان می‌خواند، نه کل جدول را."""

    def setUp(self):
        self.query = section(SERVICE, 'public async Task<List<GoodsIssueDto>> GetAsync(int? partyId = null',
                             'public async Task<GoodsIssueDto?> GetAsync(int id)')

    def test_list_is_capped(self):
        self.assertIn('private const int MaxListResults = 200;', SERVICE)
        self.assertIn('.Select(g => g.Id).Take(MaxListResults).ToListAsync(ct);', self.query)

    def test_lines_are_included_only_for_the_capped_ids(self):
        self.assertNotIn('_db.GoodsIssues.Include(g => g.Lines).AsNoTracking().AsQueryable()', self.query)
        self.assertIn('.Where(g => ids.Contains(g.Id)).ToListAsync(ct);', self.query)
        # ترتیب نهایی همان ترتیب تازه‌ترین‌هاست (Include ترتیب را حفظ نمی‌کند).
        self.assertIn('return ids.Where(byId.ContainsKey).Select(id => ToDto(byId[id])).ToList();', self.query)

    def test_cancellation_flows_from_controller_to_ef(self):
        self.assertIn('CancellationToken ct = default);', SERVICE)
        self.assertIn('CancellationToken ct = default)', CONTROLLER)
        self.assertIn('_service.GetAsync(partyId, from, to, search, ct)', CONTROLLER)

    def test_client_service_sends_the_token(self):
        self.assertIn('CancellationToken ct = default);', I_CLIENT)
        self.assertIn('_api.GetAsync<List<GoodsIssueDto>>("api/goods-issues?" + string.Join("&", query), ct)', CLIENT_SVC)


class Indexes(unittest.TestCase):
    """ایندکس مرکب شرکت/تاریخ برای هر دو موتور دیتابیس."""

    def test_sql_server_index_is_created_idempotently(self):
        self.assertIn("IX_GoodsIssues_CompanyId_Date", SCHEMA)
        self.assertIn("object_id = OBJECT_ID(N'dbo.GoodsIssues')", SCHEMA)

    def test_sqlite_index_is_created_idempotently(self):
        self.assertIn('CREATE INDEX IF NOT EXISTS IX_GoodsIssues_CompanyId_Date ON GoodsIssues (CompanyId, Date);', SCHEMA)

    def test_model_declares_the_index(self):
        self.assertIn('mb.Entity<GoodsIssue>().HasIndex(g => new { g.CompanyId, g.Date });', DBCONTEXT)


class BehaviourMirror(unittest.TestCase):
    """آینهٔ منطق بارگذاری: پاسخ کهنه/تایم‌اوت، اسپینر را روی داده‌ی اشتباه نمی‌گذارد."""

    class Loader:
        def __init__(self):
            self.version = 0
            self.loading = False
            self.items = []
            self.errors = []
            self.timeout_message = 'timeout'

        def start(self):
            self.version += 1
            self.loading = True
            return self.version

        def succeed(self, version, rows):
            if version != self.version:
                return
            self.items = rows
            self.loading = False

        def timeout(self, version):
            if version != self.version:
                return
            self.items = []
            self.loading = False
            self.errors.append(self.timeout_message)

        def fail(self, version, message):
            if version != self.version:
                return
            self.items = []
            self.loading = False
            self.errors.append(message)

    def test_latest_load_wins_and_stops_the_spinner(self):
        loader = self.Loader()
        first = loader.start()
        second = loader.start()
        loader.succeed(first, [{'id': 'stale'}])
        self.assertEqual(loader.items, [])           # پاسخ کهنه دور ریخته شد
        self.assertTrue(loader.loading)              # اسپینر برای درخواست تازه روشن است
        loader.succeed(second, [{'id': 'fresh'}])
        self.assertEqual(loader.items, [{'id': 'fresh'}])
        self.assertFalse(loader.loading)

    def test_timeout_clears_the_spinner_and_keeps_the_previous_rows_out(self):
        loader = self.Loader()
        v = loader.start()
        loader.timeout(v)
        self.assertFalse(loader.loading)
        self.assertEqual(loader.items, [])
        self.assertEqual(len(loader.errors), 1)

    def test_stale_timeout_is_silent(self):
        loader = self.Loader()
        first = loader.start()
        loader.start()
        loader.timeout(first)
        self.assertEqual(loader.errors, [])
        self.assertTrue(loader.loading)

    def test_failure_is_reported_once(self):
        loader = self.Loader()
        v = loader.start()
        loader.fail(v, 'connection lost')
        self.assertEqual(loader.errors, ['connection lost'])
        self.assertFalse(loader.loading)


class DateRange(unittest.TestCase):
    def test_invalid_range_keeps_the_spinner_off(self):
        load = section(PAGE, 'private async Task Load()', 'private static readonly TimeSpan LoadTimeout')
        guard = load.split('var version = ++loadVersion;', 1)[0]
        self.assertIn('DateRangeFilter.IsInvalid(from, to)', guard)
        self.assertIn('DateRangeFilter.InvalidMessage', guard)


if __name__ == '__main__':
    unittest.main()
