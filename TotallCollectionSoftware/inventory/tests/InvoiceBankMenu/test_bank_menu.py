"""Contracts for the bank/account list of the purchase invoice form.

Bug: in «ثبت فاکتور خرید» the bank list did not open under its own field; it
appeared in the middle of the page, and only once the invoice had more than
three item rows. Expected: the menu opens at its designated place and its
behaviour does not depend on how many items the invoice has.

Root cause this test locks down: the invoice combos took their position once,
at focus time (`uiRect` + `Popover.Below`), wrote it as inline `top/left`, and
never recomputed it. Adding item rows makes the modal body scrollable, the
anchor moves, and the now-stale inline coordinates leave the menu floating in
the middle of the page. The JS then measured "space below the field" against the
whole window instead of the visible modal box, so a tall form pushed the menu
wherever it fit.

The fix: the position is recomputed from the live anchor on every render and on
every layout change (scroll / resize / row added), the menu is hidden until that
computation succeeds, and the placement is clamped to the visible modal box.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]


def read(path):
    return (ROOT / 'src' / path).read_text(encoding='utf-8')


JS = read('Inventory.Client/wwwroot/js/order-entry.js')
SEARCH = read('Inventory.Client/Shared/SearchSelect.razor')
SEARCH_STR = read('Inventory.Client/Shared/SearchSelectStr.razor')
CSS = read('Inventory.Client/wwwroot/css/app.css')
INDEX = read('Inventory.Client/wwwroot/index.html')
FORM = read('Inventory.Client/Pages/Orders/OrderForm.razor')
PICKER = read('Inventory.Client/Shared/DatePicker.razor')


def block(source, start, end):
    return source.split(start, 1)[1].split(end, 1)[0]


class MenuIsMeasuredFromTheLiveField(unittest.TestCase):
    def test_placement_uses_the_current_anchor_rect_not_a_cached_one(self):
        place = block(JS, 'function placeSearchMenu(anchor, menu) {', '\n    // نخستین تلاش')
        self.assertIn('const a = anchor.getBoundingClientRect();', place)
        # هیچ مختصات ذخیره‌شده‌ای خوانده نمی‌شود
        self.assertNotIn('_mTop', place)
        self.assertNotIn('dataset.left', place)

    def test_placement_is_clamped_to_the_visible_modal_box(self):
        place = block(JS, 'function placeSearchMenu(anchor, menu) {', '\n    // نخستین تلاش')
        self.assertIn('const box = menuClipBox(anchor, menu);', place)
        self.assertIn('Math.max(box.left, Math.min(left, box.right - width))', place)
        self.assertIn('const below = Math.max(0, box.bottom - a.bottom - 4);', place)
        self.assertIn('const above = Math.max(0, a.top - box.top - 4);', place)
        clip = block(JS, 'function menuClipBox(anchor, menu) {', '\n    }\n')
        self.assertIn("anchor.closest('.modal-content')", clip)
        self.assertIn('window.innerWidth', clip)

    def test_a_hidden_or_offscreen_field_hides_the_menu(self):
        place = block(JS, 'function placeSearchMenu(anchor, menu) {', '\n    // نخستین تلاش')
        self.assertIn('if (!measurable(anchor)) { hideMenu(menu); return false; }', place)
        self.assertIn('if (a.bottom < box.top || a.top > box.bottom) { hideMenu(menu); return false; }', place)
        self.assertIn('if (height <= 0) { hideMenu(menu); return false; }', place)

    def test_rtl_aligns_the_menu_with_the_anchor_right_edge(self):
        place = block(JS, 'function placeSearchMenu(anchor, menu) {', '\n    // نخستین تلاش')
        self.assertIn('const left = isRtl(anchor) ? a.right - width : a.left;', place.replace('let left', 'const left'))


class MenuFollowsLayoutChanges(unittest.TestCase):
    def test_scroll_resize_and_growing_form_reposition_open_menus(self):
        self.assertIn("window.addEventListener('scroll', placeOpenMenus, true);", JS)
        self.assertIn("window.addEventListener('resize', placeOpenMenus);", JS)
        self.assertIn('new window.ResizeObserver(placeOpenMenus)', JS)
        self.assertIn("const body = modal.querySelector('.modal-body');", JS)
        self.assertIn('menuLayoutObserver.observe(modal)', JS)
        self.assertIn('menuLayoutObserver.observe(body)', JS)
        # مشاهدهٔ مودال هر بار تکرار می‌شود تا فرمِ تازه‌ساخته‌شده هم پوشش بگیرد
        self.assertIn('if (!menuLayoutObserver) menuLayoutObserver = new window.ResizeObserver(placeOpenMenus);', JS)

    def test_place_soon_retries_once_the_row_layout_is_ready(self):
        self.assertIn('function placeSearchMenuSoon(anchor, menu) {', JS)
        self.assertIn('window.requestAnimationFrame(function () { placeSearchMenu(anchor, menu); });', JS)
        self.assertIn('placeSoon: function (anchor, menu) { watchMenuLayout(menu); placeSearchMenuSoon(anchor, menu); },', JS)


class MenuIsHiddenUntilPlaced(unittest.TestCase):
    def test_constrained_menu_starts_hidden(self):
        for name, source in (('SearchSelect', SEARCH), ('SearchSelectStr', SEARCH_STR)):
            with self.subTest(component=name):
                self.assertIn('data-menu-placed="@(ConstrainToModal ? null : "true")"', source)
                self.assertIn("(ConstrainToModal ? \"visibility:hidden;\" : \"\")", source,
                              'منو تا محاسبهٔ جای درست نباید با مختصات پیش‌فرض دیده شود')

    def test_css_hides_a_menu_that_has_not_been_placed(self):
        self.assertIn('.ss-menu[data-order-constrained="true"]:not([data-menu-placed]) { visibility: hidden; }', CSS)
        self.assertIn("menu.setAttribute('data-menu-placed', 'true');", JS)
        self.assertIn("menu.removeAttribute('data-menu-placed');", JS)

    def test_menu_placeholder_has_no_fake_viewport_coordinates(self):
        # تا اندازه‌گیری، منو نباید با مختصات صفرِ گوشهٔ صفحه رندر شود
        self.assertIn("menu.style.visibility = 'hidden';", JS.split('const naturalHeight', 1)[0])


class ComponentsRepositionOnEveryRender(unittest.TestCase):
    def test_both_combos_place_the_menu_after_every_render(self):
        for name, source in (('SearchSelect', SEARCH), ('SearchSelectStr', SEARCH_STR)):
            with self.subTest(component=name):
                self.assertIn('if (open && ConstrainToModal)', source)
                self.assertIn('await JS.InvokeVoidAsync("appMenu.placeSoon", _input, _menu);', source)
                self.assertNotIn('orderEntry.placeSearchMenu', source,
                                 'مسیر قدیمی فقط یک‌بار و بدون مشاهدهٔ چیدمان جای منو را می‌گذاشت')

    def test_invoice_bank_fields_are_wired_to_the_constrained_menu(self):
        # «بانک/صندوق» نقدی و «بانک» هر چک در فرم خرید
        self.assertIn('Placeholder="انتخاب بانک…" ShowAllOnFocus="true" ConstrainToModal="true"', FORM)
        self.assertIn('Placeholder="انتخاب حساب بانکی…" ShowAllOnFocus="true" ConstrainToModal="true"', FORM)
        # پیش از این هم منوی داخلِ مودال پوشش نامرئی نمی‌سازد (کلیک روی «تعریف بانک» باید کار کند)
        self.assertIn('@if (!ConstrainToModal)', SEARCH)
        self.assertIn('@if (!ConstrainToModal)', SEARCH_STR)

    def test_quick_bank_dialog_picker_uses_the_same_menu(self):
        quick = read('Inventory.Client/Shared/QuickBankModal.razor')
        self.assertIn('Placeholder="انتخاب از لیست بانک‌ها" ShowAllOnFocus="true" ConstrainToModal="true"', quick)


class CacheBusting(unittest.TestCase):
    def test_published_assets_carry_the_new_version(self):
        self.assertRegex(INDEX, r'js/order-entry\.js\?v=(?:1[2-9]|[2-9][0-9])')
        self.assertRegex(INDEX, r'css/app\.css\?v=(?:2[1-9]|[3-9][0-9])')


if __name__ == '__main__':
    unittest.main()
