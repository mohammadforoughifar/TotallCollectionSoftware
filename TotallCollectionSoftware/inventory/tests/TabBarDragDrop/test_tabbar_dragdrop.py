"""جابجایی صفحات با کشیدن و رها کردن در نوار عنوان بالای صفحه (نوار تب‌ها).

مشکل گزارش‌شده: در نوار عنوان بالای صفحه امکان جابجایی صفحات با Drag & Drop
وجود نداشت / کار نمی‌کرد و کاربر نمی‌توانست ترتیب صفحات را با کشیدن و رها کردن
تغییر دهد.

علت‌های ریشه‌ای که این تست‌ها پوشش می‌دهند:
  • روی تب‌ها @ondragover فقط preventDefault داشت و هندلر نداشت؛ در Blazor
    بدون هندلر، لیسنر ساخته نمی‌شود و drop اصلاً رخ نمی‌دهد.
  • dataTransfer در Blazor در دسترس نیست؛ بدون setData در JS، فایرفاکس درگ
    را شروع نمی‌کند.
  • HTML5 DnD روی لمسی کار نمی‌کند؛ برای لمسی/قلمی Pointer Events لازم است.
  • رها کردن روی فضای خالی نوار (انتهای فهرست) پشتیبانی نمی‌شد.
  • بعد از درگ، clickِ مرورگر تب را فعال می‌کرد و ترتیبِ تازه را خراب می‌کرد.

رفتار مورد انتظار (این تست‌ها):
  • هر تب draggable است و dragstart/dragover/dragenter/dragleave/drop دارد.
  • dragover هم هندلر دارد و هم preventDefault (تا drop مجاز شود).
  • ردیف تب‌ها (strip) هم drop دارد تا تب به انتهای نوار برود.
  • tabs.js روی dragstart مقدار dataTransfer را ست می‌کند و برای لمسی
    Pointer Events (pointerdown/move/up) با فراخوانی OnTabDropped دارد.
  • TabService جابه‌جایی به ایندکس مشخص و با کلید را پشتیبانی می‌کند.
  • جابه‌جایی با صفحه‌کلید (Ctrl+←/→) و منوی راست‌کلیک هم ممکن است.
  • نشانگرِ بصری (cursor: grab و دستگیرهٔ ⠿) وجود دارد.
"""

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "src"
CLIENT = ROOT / "Inventory.Client"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class TabBarDragDropMarkupTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.bar = read(CLIENT / "Layout/TabBar.razor")

    def test_tabs_are_draggable(self):
        self.assertIn('draggable="true"', self.bar)

    def test_dragstart_and_dragend_are_wired(self):
        self.assertIn('@ondragstart="() => dragging = tab"', self.bar)
        self.assertIn("@ondragend=\"OnDragEnd\"", self.bar)

    def test_dragover_has_handler_and_prevent_default(self):
        # بدون هندلر، preventDefaultِ Blazor لیسنر نمی‌سازد و drop رخ نمی‌دهد.
        self.assertIn('@ondragover="() => dragOver = tab" @ondragover:preventDefault="true"', self.bar)

    def test_dragenter_and_dragleave_manage_indicator(self):
        self.assertIn('@ondragenter="() => dragOver = tab"', self.bar)
        self.assertIn('@ondragleave="() => OnTabDragLeave(tab)"', self.bar)

    def test_drop_on_tab_moves_it(self):
        self.assertIn('@ondrop="() => Drop(tab)"', self.bar)
        drop = self.bar[self.bar.index("private async Task Drop(TabItem target)"): ]
        drop = drop[: drop.index("\n    }")]
        self.assertIn("await Tabs.MoveAsync(dragging, target);", drop)

    def test_strip_accepts_drop_at_end(self):
        self.assertIn('@ondrop="DropAtEnd"', self.bar)
        end = self.bar[self.bar.index("private async Task DropAtEnd()"): ]
        end = end[: end.index("\n    }")]
        self.assertIn("await Tabs.MoveToAsync(dragging, Tabs.Tabs.Count - 1);", end)

    def test_tabs_have_stable_key_for_js(self):
        self.assertIn('data-tab-key="@tab.Key"', self.bar)

    def test_grip_handle_is_visible(self):
        self.assertIn('class="tab-grip"', self.bar)
        self.assertIn("bi-grip-vertical", self.bar)
        self.assertIn("برای تغییر ترتیب بکشید", self.bar)

    def test_click_after_drop_is_suppressed(self):
        self.assertIn("_lastDropUtc", self.bar)
        activate = self.bar[self.bar.index("private void Activate(TabItem t)"): ]
        activate = activate[: activate.index("\n    }")]
        self.assertIn("TotalMilliseconds < 350", activate)

    def test_keyboard_reorder_is_wired(self):
        self.assertIn('@onkeydown="(KeyboardEventArgs e) => OnTabKeyDown(tab, e)"', self.bar)
        self.assertIn('tabindex="0"', self.bar)
        key = self.bar[self.bar.index("private async Task OnTabKeyDown"): ]
        key = key[: key.index("\n    }")]
        self.assertIn("MoveByOffsetAsync(tab, +1)", key)
        self.assertIn("MoveByOffsetAsync(tab, -1)", key)

    def test_context_menu_can_move_tab(self):
        self.assertIn("انتقال به ابتدای نوار", self.bar)
        self.assertIn("انتقال به انتهای نوار", self.bar)
        self.assertIn("await Tabs.MoveToAsync(t, 0);", self.bar)

    def test_js_drag_is_enabled_once(self):
        self.assertIn('await JS.InvokeVoidAsync("appTabs.enableDrag", strip, _self);', self.bar)
        self.assertIn("[JSInvokable]", self.bar)
        self.assertIn("public async Task OnTabDropped(string sourceKey, int targetIndex)", self.bar)


class TabServiceMoveTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.svc = read(CLIENT / "Services/TabService.cs")

    def test_move_to_index_exists(self):
        self.assertIn("public async Task MoveToAsync(TabItem source, int targetIndex)", self.svc)
        body = self.svc[self.svc.index("public async Task MoveToAsync"): ]
        body = body[: body.index("\n    }")]
        self.assertIn("_tabs.RemoveAt(si);", body)
        self.assertIn("_tabs.Insert(insertAt, source);", body)
        self.assertIn("Math.Clamp(targetIndex, 0, _tabs.Count)", body)

    def test_move_by_key_for_js_callback(self):
        self.assertIn("public async Task MoveByKeyAsync(string sourceKey, int targetIndex)", self.svc)
        body = self.svc[self.svc.index("public async Task MoveByKeyAsync"): ]
        body = body[: body.index("\n    }")]
        self.assertIn("x.Key == sourceKey", body)
        self.assertIn("await MoveToAsync(source, targetIndex);", body)

    def test_move_by_offset_for_keyboard(self):
        self.assertIn("public async Task MoveByOffsetAsync(TabItem source, int offset)", self.svc)

    def test_move_async_supports_before_after(self):
        self.assertIn("public async Task MoveAsync(TabItem source, TabItem target, bool after = false)", self.svc)
        body = self.svc[self.svc.index("public async Task MoveAsync(TabItem source"): ]
        body = body[: body.index("\n    }")]
        self.assertIn("(after ? 1 : 0)", body)

    def test_every_move_persists_order(self):
        for name in ("MoveAsync(TabItem source", "MoveToAsync", "MoveByKeyAsync"):
            with self.subTest(method=name):
                body = self.svc[self.svc.index(f"public async Task {name}"): ]
                body = body[: body.index("\n    }")]
                # MoveByKey به MoveTo واگذار می‌کند و آنجا ذخیره می‌شود.
                self.assertTrue("await SaveAsync();" in body or "await MoveToAsync(" in body)


class TabsJsDragTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.js = read(CLIENT / "wwwroot/js/tabs.js")

    def test_enable_drag_exists(self):
        self.assertIn("enableDrag: function (el, dotNetRef)", self.js)

    def test_data_transfer_is_set_for_firefox(self):
        self.assertIn("e.dataTransfer.setData('text/plain', key)", self.js)
        self.assertIn("e.dataTransfer.effectAllowed = 'move'", self.js)

    def test_dragover_allows_drop(self):
        self.assertIn("el.addEventListener('dragover'", self.js)
        self.assertIn("e.preventDefault();", self.js)

    def test_pointer_drag_for_touch(self):
        self.assertIn("el.addEventListener('pointerdown'", self.js)
        self.assertIn("el.addEventListener('pointermove'", self.js)
        self.assertIn("el.addEventListener('pointerup'", self.js)
        self.assertIn("el.addEventListener('pointercancel'", self.js)
        self.assertIn("e.pointerType === 'mouse'", self.js)
        self.assertIn("closest('.tab-grip')", self.js)

    def test_pointer_drag_calls_back_to_blazor(self):
        self.assertIn("dotNetRef.invokeMethodAsync('OnTabDropped', key, idx)", self.js)

    def test_pointer_drag_suppresses_click(self):
        self.assertIn("suppressNextClick", self.js)

    def test_insertion_index_is_rtl_aware(self):
        self.assertIn("function insertionIndex(clientX, sourceEl)", self.js)
        self.assertIn("if (mid > clientX) idx++;", self.js)


class TabDragStyleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.css = read(CLIENT / "wwwroot/css/app.css")

    def test_grab_cursor(self):
        self.assertIn(".tabbar .tab { cursor: grab; }", self.css)
        self.assertIn(".tabbar .tab:active { cursor: grabbing; }", self.css)

    def test_grip_handle_style(self):
        self.assertIn(".tabbar .tab .tab-grip {", self.css)
        self.assertIn("touch-action: none;", self.css)

    def test_dragging_and_pointer_dragging_styles(self):
        self.assertIn(".tabbar .tab.dragging { opacity: .45; cursor: grabbing; }", self.css)
        self.assertIn(".tabbar .tab.pointer-dragging {", self.css)

    def test_keyboard_focus_is_visible(self):
        self.assertIn(".tabbar .tab:focus-visible {", self.css)


if __name__ == "__main__":
    unittest.main()
