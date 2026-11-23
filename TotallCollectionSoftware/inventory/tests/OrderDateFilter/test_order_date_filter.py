"""Source-level regression checks for the shared purchase/sale order filter."""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / 'src'
PAGE = (ROOT / 'Inventory.Client/Pages/Orders/Orders.razor').read_text(encoding='utf-8')
CONTROLLER = (ROOT / 'Inventory.Api/Controllers/Sales/OrdersController.cs').read_text(encoding='utf-8')


class OrderDateFilterTests(unittest.TestCase):
    def test_invalid_range_is_rejected_before_applying_or_resetting_page(self):
        apply = PAGE.split('private async Task ApplyFilter()', 1)[1].split('private async Task GoToPage', 1)[0]
        self.assertIn('from.Value.Date > to.Value.Date', apply)
        self.assertIn('Toast.Error(', apply)
        self.assertLess(apply.index('return;'), apply.index('appliedFrom = from;'))
        self.assertLess(apply.index('return;'), apply.index('page = 1;'))

    def test_reload_uses_applied_not_draft_filters(self):
        self.assertIn('GetOrdersAsync(txnType, appliedFrom, appliedTo, appliedPartyId, appliedWarehouseId, page, PageSize)', PAGE)
        for assignment in ('appliedFrom = from;', 'appliedTo = to;',
                           'appliedPartyId = filterPartyId;', 'appliedWarehouseId = filterWarehouseId;'):
            self.assertIn(assignment, PAGE)

    def test_api_rejects_reversed_dates_before_query(self):
        action = CONTROLLER.split('public async Task<ActionResult<PagedResult<Order>>> GetAll(', 1)[1].split('/// <summary>مجوز', 1)[0]
        self.assertIn('from.HasValue && to.HasValue && from.Value.Date > to.Value.Date', action)
        self.assertLess(action.index('return BadRequest('), action.index('_service.GetOrdersAsync('))


if __name__ == '__main__':
    unittest.main()
