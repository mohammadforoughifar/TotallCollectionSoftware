"""Source-contract regressions for editing opening balances (ویرایش مانده‌های اول دوره).

Run: PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/OpeningBalanceEdit

The suite pins three things the request depends on:

* every one of the four tabs of the opening-balance module exposes an edit action;
* the decision "edit enabled / disabled" comes from the shared rule, so the
  client button and the server guard can never disagree;
* the server edits the very same document (PUT with the same id) and refuses
  any document that already has settlement history (cash, credit receipt, cheque
  collection, paid installment) or is cancelled.
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'src'


def read(path):
    return (SRC / path).read_text(encoding='utf-8')


RULE = read('Inventory.Shared/OpeningBalanceEdit.cs')
PAGE = read('Inventory.Client/Pages/Shop/OpeningBalanceWizard.razor')
SERVICE = read('Inventory.Api/Services/Catalog/InventoryService.cs')
CONTROLLER = read('Inventory.Api/Controllers/Sales/OrdersController.cs')
CLIENT_SVC = read('Inventory.Client/Services/InventoryApiServices.cs')
CLIENT_API = read('Inventory.Client/Services/IInventoryApi.cs')
DTOS = read('Inventory.Shared/Dtos/OperationDtos.cs')
IFACE = read('Inventory.Api/Services/Catalog/IInventoryService.cs')
SEARCH = read('Inventory.Shared/PersianSearch.cs')
INDEX = read('Inventory.Client/wwwroot/index.html')

# پیام در C# روی دو خط با «+» به هم چسبیده است؛ هر دو نیمه جدا بررسی می‌شوند.
LOCKED_HEAD = 'این مانده اول دوره گردش تسویه، دریافت یا وصول دارد؛ ویرایش آن غیرفعال است. '
LOCKED_TAIL = 'ابتدا گردش را برگشت یا اصلاح کنید.'


class SharedRuleContract(unittest.TestCase):
    def test_rule_lives_in_shared_project(self):
        self.assertIn('namespace Inventory.Shared;', RULE)
        self.assertIn('public static class OpeningBalanceEdit', RULE)

    def test_single_persian_reason_for_settled_documents(self):
        self.assertIn(f'"{LOCKED_HEAD}" +\n        "{LOCKED_TAIL}";', RULE)
        self.assertIn('public string? BlockedReason => IsCancelled ? LockedCancelledMessage', RULE)
        self.assertIn(': !IsUntouched ? LockedAfterSettlementMessage', RULE)
        self.assertIn('public static bool CanEdit(SettlementState state) => state.BlockedReason is null;', RULE)

    def test_cancelled_document_is_never_editable(self):
        self.assertIn('public bool IsUntouched =>\n            !HasCashHistory && !HasCreditReceipts && !HasChequeHistory && !HasInstallmentHistory;',
                      RULE)
        self.assertIn('public const string LockedCancelledMessage =', RULE)

    def test_stock_opening_cash_is_cost_not_settlement(self):
        # جزء نقدی موجودی اولیه بهای تمام‌شده است، پس نباید ویرایش را قفل کند.
        self.assertIn('public static bool CashLedgerBlocksEdit(string? description) => !IsOpeningStock(description);',
                      RULE)
        self.assertIn('hasCashHistory && CashLedgerBlocksEdit(description)', RULE)

    def test_nature_and_cheque_kind_are_locked(self):
        self.assertIn('public const string NatureLockedMessage =', RULE)
        self.assertIn('public const string ChequeKindLockedMessage =', RULE)
        self.assertIn('public static bool ChequeStatusIsEditable(ChequeStatus status) =>\n        status is ChequeStatus.InHand or ChequeStatus.InCollection;', RULE)

    def test_marker_helpers_never_double_prefix(self):
        self.assertIn('public static string StripMarker(string? description)', RULE)
        self.assertIn('if (body.StartsWith("[OPENING:", StringComparison.Ordinal)) body = StripMarker(body);', RULE)
        self.assertIn('public static string Compose(string prefix, string? text, string fallback)', RULE)

    def test_sections_match_the_history_classification(self):
        self.assertIn('public enum OpeningBalanceSection', RULE)
        for member in ('Party = 1', 'Cheque = 2', 'Installment = 3', 'Stock = 4'):
            self.assertIn(member, RULE)
        self.assertIn('public static OpeningBalanceSection SectionOf(string? description, PaymentMethod paymentMethod,', RULE)
        # همان ترتیب دسته‌بندی فهرست سابقه: اقساط، سپس موجودی، سپس طرف حساب.
        self.assertLess(RULE.index('description.StartsWith(InstallmentPrefix'),
                        RULE.index('if (IsOpeningStock(description)) return OpeningBalanceSection.Stock;'))
        self.assertLess(RULE.index('if (IsOpeningStock(description)) return OpeningBalanceSection.Stock;'),
                        RULE.index('description.StartsWith(PartyPrefix'))

    def test_installment_split_keeps_the_total(self):
        self.assertIn('public static decimal[] SplitInstallments(decimal total, int count)', RULE)
        self.assertIn('amounts[i] = i == count - 1 ? total - each * (count - 1) : each;', RULE)


class EditApiContract(unittest.TestCase):
    def test_state_and_update_endpoints_exist(self):
        self.assertIn('[HttpGet("{id:int}/opening-edit-state")]', CONTROLLER)
        self.assertIn('[HttpPut("{id:int}/opening-balance")]', CONTROLLER)
        self.assertIn('[Inventory.Api.Infrastructure.RequirePermission("Orders", "Update")]', CONTROLLER)

    def test_update_keeps_the_same_document(self):
        # روش انتخابی «به‌روزرسانی همان سند» است، نه لغو و ثبت مجدد.
        self.assertNotIn('CreateOrderAsync', SERVICE.split('public async Task<Order> UpdateOpeningBalanceAsync', 1)[1]
                         .split('private static void ApplyOpeningPartyEdit', 1)[0])

    def test_service_interface_is_extended(self):
        self.assertIn('Task<OpeningBalanceEditState> GetOpeningBalanceEditStateAsync(int id);', IFACE)
        self.assertIn('Task<Order> UpdateOpeningBalanceAsync(int id, OpeningBalanceEditCommand cmd, string? user = null);', IFACE)

    def test_client_forwards_both_calls(self):
        self.assertIn('Task<OpeningBalanceEditState> GetOpeningEditStateAsync(int id);', CLIENT_API)
        self.assertIn('Task<Order> UpdateOpeningBalanceAsync(int id, OpeningBalanceEditCommand cmd);', CLIENT_API)
        self.assertIn('_api.GetAsync<OpeningBalanceEditState>($"api/orders/{id}/opening-edit-state");', CLIENT_SVC)
        self.assertIn('_api.PutAsync<Order>($"api/orders/{id}/opening-balance", cmd);', CLIENT_SVC)

    def test_command_only_carries_values_not_nature(self):
        command = DTOS.split('public class OpeningBalanceEditCommand', 1)[1]
        for field in ('Amount', 'Date', 'Description', 'InstallmentCount', 'Quantity'):
            self.assertIn(f'public decimal {field}' if field in ('Amount', 'Quantity')
                          else f'{field}', command)
        # ماهیت/نوع چک از دستور ویرایش حذف شده‌اند: یعنی از این مسیر تغییر نمی‌کنند.
        self.assertNotIn('IsDebtor', command)
        self.assertNotIn('ChequeKind', command)
        self.assertIn('public bool NatureLocked { get; set; }', DTOS)


class ServerGuardContract(unittest.TestCase):
    def setUp(self):
        self.update = SERVICE.split('public async Task<Order> UpdateOpeningBalanceAsync', 1)[1]

    def test_refuses_the_same_documents_as_the_button(self):
        self.assertIn('var state = await ReadOpeningBalanceEditStateAsync(id);', self.update)
        self.assertLess(self.update.index('OpeningBalanceEdit.BlockedReason(state.Settlement)'),
                        self.update.index('ApplyOpeningPartyEdit(txn, cmd);'),
                        'ویرایش باید پیش از هر تغییری رد شود')

    def test_every_settlement_source_is_checked(self):
        settlement = SERVICE.split('private async Task<OpeningBalanceEdit.SettlementState> ReadSettlementStateAsync', 1)[1] \
            .split('public async Task<Order> UpdateOpeningBalanceAsync', 1)[0]
        for source in ('ShopOrderCashEntries', 'ShopOrderCreditReceipts', 'SaleChequeReceipts',
                       'ShopExpenseCheques', 'SaleChequeInstallmentReceipts', 'SaleChequeCancellations'):
            self.assertIn(source, settlement)
        self.assertIn('txn.Installments.Any(i => i.IsPaid)', settlement)
        self.assertIn('c.IsCleared || c.Status != ChequeStatus.InHand', settlement)

    def test_transaction_is_serializable_and_rolled_back(self):
        self.assertIn('BeginTransactionAsync(IsolationLevel.Serializable)', self.update)
        self.assertIn('await trx.RollbackAsync();', self.update)
        self.assertIn('_db.ChangeTracker.Clear();', self.update)

    def test_party_edit_keeps_it_a_credit_opening_sale_or_purchase(self):
        party = SERVICE.split('private static void ApplyOpeningPartyEdit', 1)[1].split('private void ApplyOpeningInstallmentEdit', 1)[0]
        self.assertIn('txn.PaymentMethod = PaymentMethod.Credit;', party)
        self.assertIn('txn.SettledAmount = 0m;', party)
        self.assertIn('سررسید نسیه نمی‌تواند پیش از تاریخ سند افتتاحیه باشد', party)
        self.assertIn('line.Quantity = 1m;', party)

    def test_installment_edit_rebuilds_only_unpaid_schedule(self):
        installment = SERVICE.split('private void ApplyOpeningInstallmentEdit', 1)[1].split('private async Task<List<(int Wh, int Pid)>> ApplyOpeningStockEditAsync', 1)[0]
        self.assertIn('OpeningBalanceEdit.SplitInstallments(cmd.Amount, cmd.InstallmentCount)', installment)
        self.assertIn('_db.Installments.RemoveRange(txn.Installments.ToList());', installment)
        self.assertIn('IsPaid = false,', installment)
        self.assertIn('start.AddMonths(i * cmd.IntervalMonths)', installment)

    def test_stock_edit_validates_unit_and_recalculates_both_pairs(self):
        stock = SERVICE.split('private async Task<List<(int Wh, int Pid)>> ApplyOpeningStockEditAsync', 1)[1] \
            .split('public async Task<PagedResult<Order>> GetOrdersAsync', 1)[0]
        self.assertIn('ShopQuantityRules.IsWhole(cmd.Quantity)', stock)
        self.assertIn('ResolveUnitRulesAsync(new[] { product })', stock)
        self.assertIn('return new List<(int Wh, int Pid)> { (oldWarehouse, oldProduct), (txn.WarehouseId, line.ProductId) };',
                      stock)
        # گردش نقدی موجودی اولیه با مبلغ جدید هم‌گام می‌شود.
        self.assertIn('txn.CashAmount = txn.Amount;', stock)
        self.assertIn('txn.SettledAmount = txn.CashAmount;', stock)

    def test_cash_ledger_is_verified_before_mutation(self):
        self.assertIn('var previousCash = txn.ShopCashLedgerEnabled\n                ? await ShopOrderCashLedger.VerifyCurrentAsync(_db, txn) : null;',
                      self.update)
        self.assertIn('await ShopOrderCashLedger.SyncAfterEditAsync(_db, txn, previousCash, user);', self.update)
        self.assertLess(self.update.index('ShopOrderCashLedger.VerifyCurrentAsync'),
                        self.update.index('ShopOrderCashLedger.SyncAfterEditAsync'))


class WizardContract(unittest.TestCase):
    def test_every_tab_has_an_edit_action(self):
        for tab in ('parties', 'cheques', 'installments', 'stock'):
            with self.subTest(tab=tab):
                self.assertIn(f'data-opening-edit="{tab}"', PAGE)
        self.assertEqual(PAGE.count('ویرایش\n                                                        </button>'), 0)

    def test_edit_buttons_are_disabled_by_the_shared_rule(self):
        self.assertIn('disabled="@(!CanEditPartyRow(item))"', PAGE)
        self.assertIn('disabled="@(!OpeningBalanceEdit.CanEdit(InstallmentState(o)))"', PAGE)
        self.assertIn('disabled="@(OpeningBalanceEdit.ChequeBlockedReason(ch.Status) is not null)"', PAGE)
        self.assertIn('disabled="@(sl.IsCancelled)"', PAGE)
        self.assertIn('private static bool CanEditPartyRow(PartyBalanceItem item) => OpeningBalanceEdit.CanEdit(PartyState(item));', PAGE)

    def test_rows_keep_the_document_id(self):
        self.assertIn('Id = order.Id,\n                    PartyId = order.PartyId', PAGE)
        self.assertIn('OrderId = order.Id, WarehouseId = order.WarehouseId, ProductId = line.ProductId,', PAGE)

    def test_save_calls_the_update_endpoint_and_reloads(self):
        save = PAGE.split('private async Task SaveOpeningEditAsync()', 1)[1].split('private OpeningBalanceEditCommand? BuildEditCommand()', 1)[0]
        self.assertIn('await OrderSvc.UpdateOpeningBalanceAsync(editOrderId, cmd);', save)
        self.assertIn('await LoadOpeningHistoryAsync();', save)
        self.assertNotIn('OrderSvc.CreateAsync', save)

    def test_server_state_overrides_the_optimistic_button(self):
        refresh = PAGE.split('private async Task RefreshEditStateAsync(int orderId)', 1)[1] \
            .split('private async Task SaveOpeningEditAsync()', 1)[0]
        self.assertIn('await OrderSvc.GetOpeningEditStateAsync(orderId);', refresh)
        self.assertIn('editState = new OpeningBalanceEditState { CanEdit = false, BlockedReason = ex.Message };', refresh)
        # نتیجه فقط برای همان ردیفِ بازِ فرم اعمال می‌شود تا پاسخ دیررس ردیف دیگر را قفل نکند.
        self.assertEqual(refresh.count('if (editOrderId == orderId && showEditModal)'), 3)

    def test_cheque_rows_use_the_cheque_ledger_state_not_a_missing_order(self):
        refresh = PAGE.split('private async Task RefreshEditStateAsync(int orderId)', 1)[1] \
            .split('private async Task SaveOpeningEditAsync()', 1)[0]
        cheque_branch = refresh.split('if (editKind == "cheques")', 1)[1].split('try', 1)[0]
        self.assertIn('OpeningBalanceEdit.ChequeBlockedReason(editCheque.Status)', cheque_branch)
        self.assertNotIn('OrderSvc.GetOpeningEditStateAsync', cheque_branch)

    def test_edit_form_never_offers_the_locked_nature(self):
        modal = PAGE.split('Visible="showEditModal"', 1)[1].split('<style>', 1)[0]
        self.assertIn('data-opening-edit-nature', modal)
        self.assertIn('ماهیت مانده پس از ثبت تغییر نمی‌کند.', modal)
        self.assertIn('نوع چک پس از ثبت تغییر نمی‌کند.', modal)
        # فرم ویرایش، سگمنت انتخاب ماهیت را تکرار نمی‌کند.
        self.assertNotIn('partyBalance.IsDebtor = true', modal)

    def test_blocked_rows_show_the_reason_instead_of_the_form(self):
        self.assertIn('data-opening-edit-blocked', PAGE)
        self.assertIn('<div>@editState.BlockedReason</div>', PAGE)

    def test_cheque_edit_reuses_the_shop_identifier_rules(self):
        cheque = PAGE.split('private async Task SaveChequeEditAsync()', 1)[1]
        self.assertIn('ShopIdentifierRules.ValidateCheque(editCheque)', cheque)
        self.assertIn('await ChequeSvc.SaveShopAsync(editCheque);', cheque)


class SelectArrowContract(unittest.TestCase):
    """فلش کمبوباکس نباید روی متن فارسی بیفتد (صفحه RTL است، بوت‌استرپ LTR)."""

    def test_every_select_in_the_module_uses_the_module_class(self):
        body = PAGE.split('<style>', 1)[0]
        self.assertEqual(body.count('<select'), 5)
        for chunk in body.split('<select')[1:]:
            tag = chunk.split('>', 1)[0]
            self.assertIn('op-input', tag, tag)

    def test_arrow_is_mirrored_to_the_left_and_text_is_padded(self):
        css = PAGE.split('<style>', 1)[1]
        rule = css.split('select.op-input,', 1)[1].split('}', 1)[0]
        self.assertIn('background-position: left 0.75rem center;', rule)
        # در RTL padding-inline-end سمت چپ است؛ همان‌جا باید جای فلش خالی شود.
        self.assertIn('padding-inline-start: 0.75rem;', rule)
        self.assertIn('padding-inline-end: 2.25rem;', rule)
        self.assertIn('direction: rtl;', rule)
        # رفع اشکال فقط داخل ماژول است؛ فایل بوت‌استرپ جهانی دست نمی‌خورد.
        self.assertNotIn('bootstrap.rtl', INDEX)

    def test_module_keeps_one_style_block(self):
        self.assertEqual(PAGE.count('<style>'), 1)
        self.assertEqual(PAGE.count('</style>'), 1)


class FormParityContract(unittest.TestCase):
    """هر چیزی که در فرم افزودن هست، در فرم ویرایش همان بخش هم باید باشد."""

    def setUp(self):
        self.modal = PAGE.split('Visible="showEditModal"', 1)[1].split('<style>', 1)[0]

    def test_party_edit_has_every_create_field(self):
        for snippet in ('مبلغ مانده افتتاحیه (ریال)',
                        'op-money-wrapper @(editParty.IsDebtor ? "border-danger-glow" : "border-success-glow")',
                        'op-currency-tag">ریال',
                        '(editParty.Amount / 10).ToString("N0")',
                        'تاریخ سند افتتاحیه',
                        'سررسید تسویه نسیه',
                        'placeholder="مثال: مانده افتتاحیه انتقال از سال مالی قبل / طلب قدیمی"'):
            with self.subTest(snippet=snippet):
                self.assertIn(snippet, self.modal)

    def test_cheque_edit_has_every_create_field(self):
        for snippet in ('شناسه صیادی (۱۶ رقم)',
                        'ShopDigitsInput @bind-Value="editCheque.SayadId"',
                        'MaxDigits="16" ValidLengths="@(new[] { 16 })"',
                        'data-opening-edit-bank',
                        'حساب بانکی صادرکننده (دسته چک شرکت)',
                        'مبلغ این چک در زمان وصول از موجودی این حساب بانکی کسر خواهد شد.',
                        'تاریخ سررسید',
                        'نام یا کد شعبه',
                        'placeholder="بابت مانده حساب اول دوره / فاکتورهای سال قبل"'):
            with self.subTest(snippet=snippet):
                self.assertIn(snippet, self.modal)

    def test_installment_edit_matches_the_create_layout(self):
        for snippet in ('مبلغ میانگین هر قسط:',
                        'data-opening-edit-preview',
                        '(editInstallmentTotal / editInstallmentCount).ToString("N0")',
                        'فاصله زمانی اقساط',
                        'ماهانه (هر ۱ ماه)',
                        'تعداد اقساط',
                        'placeholder="مثال: اقساط باقی‌مانده خرید کالای سال قبل"',
                        'maxlength="400"'):
            with self.subTest(snippet=snippet):
                self.assertIn(snippet, self.modal)

    def test_stock_edit_matches_the_create_layout(self):
        for snippet in ('انبار مقصد کالا',
                        'کالای مورد نظر',
                        'بهای تمام‌شده واحد (ریال)',
                        '@EditStockQuantityHint',
                        'placeholder="ثبت موجودی افتتاحیه اول دوره انبار"'):
            with self.subTest(snippet=snippet):
                self.assertIn(snippet, self.modal)

    def test_quantity_hint_uses_the_selected_product(self):
        self.assertIn('private string EditStockQuantityHint => stockProducts.FirstOrDefault(p => p.Id == editStockProductId)', PAGE)


class SearchContract(unittest.TestCase):
    """جست‌وجو در هر چهار بخش: کادر، فیلتر و شمارش نتیجه."""

    TABS = ('parties', 'cheques', 'installments', 'stock')

    def test_each_section_has_a_search_box(self):
        for tab in self.TABS:
            with self.subTest(tab=tab):
                self.assertIn(f'data-opening-search="{tab}"', PAGE)

    def test_each_section_filters_its_own_history(self):
        for filtered, declaration in (
                ('FilteredPartyBalances', 'private List<PartyBalanceItem> FilteredPartyBalances => recentPartyBalances.Where('),
                ('FilteredCheques', 'private List<TrsCheque> FilteredCheques => chequesList.Where('),
                ('FilteredInstallmentOrders', 'private List<Order> FilteredInstallmentOrders => recentInstallmentOrders.Where('),
                ('FilteredStockLines', 'private List<OpeningStockLine> FilteredStockLines => recentStockLines.Where(')):
            with self.subTest(filtered=filtered):
                self.assertIn(declaration, PAGE)
                # جدول هم همان فهرست فیلترشده را رندر می‌کند، نه فهرست کامل را.
                self.assertIn(f'@foreach (var ', PAGE)
                self.assertIn(f' in {filtered})', PAGE)

    def test_search_uses_the_shared_persian_normaliser(self):
        self.assertIn('PersianSearch.Matches(', PAGE)
        self.assertIn('namespace Inventory.Shared;', SEARCH)
        self.assertIn('public static class PersianSearch', SEARCH)
        self.assertIn("'\\u064a' or '\\u0649' => '\\u06cc'", SEARCH)
        self.assertIn("'\\u0643' => '\\u06a9'", SEARCH)

    def test_empty_query_shows_everything(self):
        self.assertIn('var tokens = Tokenize(query);\n        if (tokens.Length == 0) return true;', SEARCH)

    def test_clear_button_and_result_count(self):
        body = PAGE.split('<style>', 1)[0]
        self.assertEqual(body.count('class="op-search-clear"'), 4)
        self.assertEqual(body.count('op-search-box'), 4)
        self.assertEqual(PAGE.count('SearchSummary(Filtered'), 4)
        for tab in ('Party', 'Cheque', 'Installment', 'Stock'):
            with self.subTest(tab=tab):
                self.assertIn(f'Clear{tab}Search', PAGE)

    def test_search_text_is_normalised_for_money_and_dates(self):
        self.assertIn('case DateTime date:', SEARCH)
        self.assertIn('case decimal money:', SEARCH)
        self.assertIn('money.ToString("#,0.##", CultureInfo.InvariantCulture)', SEARCH)
        self.assertIn('Fa.Digits(shown)', PAGE)

    def test_cheque_history_is_not_truncated_behind_the_search(self):
        self.assertNotIn('FilteredCheques.Take(10)', PAGE)
        self.assertIn('ChequeSvc.GetAllAsync(pageSize: 200)', PAGE)


class ExistingBehaviourIsUntouched(unittest.TestCase):
    def test_creation_paths_are_unchanged(self):
        for call in ('@onclick="SavePartyBalanceAsync"', '@onclick="SaveChequeAsync"',
                     '@onclick="GenerateInstallmentsAsync"', '@onclick="SaveOpeningStockAsync"'):
            self.assertIn(call, PAGE)
        self.assertIn('await OrderSvc.CreateAsync(cmd);', PAGE)
        self.assertIn('Description = "[OPENING:PARTY] " +', PAGE)


if __name__ == '__main__':
    unittest.main()
