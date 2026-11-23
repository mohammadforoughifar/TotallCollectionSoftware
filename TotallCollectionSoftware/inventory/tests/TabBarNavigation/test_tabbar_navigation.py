"""دکمه‌های «صفحهٔ قبلی / بعدی» در نوار بالای صفحه (نوار تب‌ها).

مشکل گزارش‌شده: دو دکمهٔ ‹ و › در نواری که عنوان صفحه‌ها را نشان می‌دهد هیچ کاری
نمی‌کردند — آن‌ها فقط ردیف تب را افقی اسکرول می‌کردند و اگر ردیف پر نبود (یا اسکرول
جا نداشت) کاملاً بی‌اثر بودند.

رفتار مورد انتظار (این تست‌ها):
  • «قبلی/بعدی» صفحه‌ها را عوض می‌کند، نه فقط اسکرول.
  • مسیرِ قبلی/بعدی از تاریخچهٔ بازدید می‌آید، مثل مرورگر: رفتن به عقب شاخهٔ جلو را
    نگه می‌دارد و یک بازدید تازه شاخهٔ جلو را پاک می‌کند.
  • اگر مقصدی نباشد دکمه غیرفعال و کم‌رنگ است (نه کلیکِ بی‌اثر).
  • دکمه‌ها روی موبایل هم دیده می‌شوند.
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "src"
CLIENT = ROOT / "Inventory.Client"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class TabBarNavTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.bar = read(CLIENT / "Layout/TabBar.razor")
        cls.css = read(CLIENT / "wwwroot/css/app.css")

    def _button(self, nav: str) -> str:
        start = self.bar.index(f'data-tab-nav="{nav}"')
        begin = self.bar.rindex("<button", 0, start)
        end = self.bar.index("</button>", start)
        return self.bar[begin:end]

    def test_both_arrows_call_navigation_not_scrolling(self):
        prev, nxt = self._button("prev"), self._button("next")
        self.assertIn('@onclick="GoPrev"', prev)
        self.assertIn('@onclick="GoNext"', nxt)
        self.assertNotIn("Scroll(", prev)
        self.assertNotIn("Scroll(", nxt)

    def test_arrows_have_real_labels(self):
        self.assertIn('title="صفحهٔ قبلی"', self._button("prev"))
        self.assertIn('aria-label="صفحهٔ قبلی"', self._button("prev"))
        self.assertIn('title="صفحهٔ بعدی"', self._button("next"))
        self.assertIn('aria-label="صفحهٔ بعدی"', self._button("next"))

    def test_arrows_are_disabled_when_there_is_no_target(self):
        self.assertIn('disabled="@(PrevTab is null)"', self._button("prev"))
        self.assertIn('disabled="@(NextTab is null)"', self._button("next"))

    def test_handlers_delegate_to_tab_history(self):
        self.assertIn("private TabItem? PrevTab => Tabs.Previous;", self.bar)
        self.assertIn("private TabItem? NextTab => Tabs.Next;", self.bar)
        prev = self.bar[self.bar.index("private void GoPrev()"):]
        prev = prev[: prev.index("\n    }")]
        self.assertIn("Tabs.GoBack();", prev)
        nxt = self.bar[self.bar.index("private void GoNext()"):]
        nxt = nxt[: nxt.index("\n    }")]
        self.assertIn("Tabs.GoForward();", nxt)


class TabHistoryServiceTests(unittest.TestCase):
    """تاریخچهٔ بازدید در TabService — همان منطقی که دکمه‌ها را معنا می‌دهد."""

    @classmethod
    def setUpClass(cls):
        cls.svc = read(CLIENT / "Services/TabService.cs")

    def test_history_state_exists(self):
        self.assertIn("private readonly List<string> _history = new();", self.svc)
        self.assertIn("private int _historyIndex = -1;", self.svc)

    def test_previous_and_next_are_exposed(self):
        self.assertIn("public TabItem? Previous => HistoryAt(_historyIndex - 1);", self.svc)
        self.assertIn("public TabItem? Next => HistoryAt(_historyIndex + 1);", self.svc)
        self.assertIn("public void GoBack()", self.svc)
        self.assertIn("public void GoForward()", self.svc)

    def test_navigation_records_history(self):
        sync = self.svc[self.svc.index("public void Sync("):]
        sync = sync[: sync.index("\n    }\n")]
        self.assertIn("PushHistory(t.Key);", sync)

    def test_browser_style_branching(self):
        push = self.svc[self.svc.index("private void PushHistory("):]
        push = push[: push.index("\n    }\n")]
        # بازدید تازه، شاخهٔ «جلو» را پاک می‌کند
        self.assertIn("_history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);", push)
        self.assertIn("_history.Add(key);", push)
        # بازدید تکراریِ پشت‌سرهم دوبار ثبت نمی‌شود
        self.assertIn("_history[_historyIndex] == key) return;", push)

    def test_go_back_moves_the_index(self):
        back = self.svc[self.svc.index("public void GoBack()"):]
        back = back[: back.index("\n    }")]
        self.assertIn("MoveHistoryTo(_historyIndex - 1, tab)", back)
        forward = self.svc[self.svc.index("public void GoForward()"):]
        forward = forward[: forward.index("\n    }")]
        self.assertIn("MoveHistoryTo(_historyIndex + 1, tab)", forward)

    def test_closed_tabs_leave_the_history(self):
        for name in ("CloseAsync", "CloseOthersAsync", "CloseAllAsync"):
            with self.subTest(method=name):
                body = self.svc[self.svc.index(f"public async Task {name}("):]
                body = body[: body.index("\n    }\n")]
                self.assertIn("ForgetHistory(", body)
        trim = self.svc[self.svc.index("private void TrimExcess()"):]
        trim = trim[: trim.index("\n    }\n")]
        self.assertIn("ForgetHistory(victim.Key);", trim)

    def test_removing_a_tab_keeps_the_current_position(self):
        forget = self.svc[self.svc.index("private void ForgetHistory(string key)"):]
        forget = forget[: forget.index("\n    }\n")]
        self.assertIn("var current = _historyIndex >= 0", forget)
        self.assertIn("var at = _history.IndexOf(current);", forget)
        self.assertIn("_historyIndex = at >= 0 ? at : Math.Min(_historyIndex, _history.Count - 1);", forget)

    def test_history_is_bounded(self):
        self.assertIn("if (_history.Count > 50)", self.svc)

    def test_target_tab_must_still_be_open(self):
        at = self.svc[self.svc.index("private TabItem? HistoryAt(int index)"):]
        at = at[: at.index("\n    }\n")]
        self.assertIn("if (index < 0 || index >= _history.Count) return null;", at)
        self.assertIn("var tab = _tabs.FirstOrDefault(x => x.Key == key);", at)


class NavButtonStyleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.css = read(CLIENT / "wwwroot/css/app.css")

    def test_disabled_arrows_look_disabled(self):
        self.assertIn(".tabbar .tabnav:disabled { opacity: .32; cursor: default; }", self.css)

    def test_hover_does_not_style_disabled_arrows(self):
        self.assertIn(".tabbar .tabnav:hover:not(:disabled)", self.css)
        self.assertNotIn(".tabbar .tabnav:hover {", self.css)

    def test_arrows_are_visible_on_mobile(self):
        # آخرین قاعدهٔ موبایلِ نوار تب برنده است؛ دکمه‌ها نباید پنهان شوند.
        blocks = [block for block in self.css.split("@media (max-width: 991.98px)")
                  if ".tabbar .tabnav" in block]
        self.assertTrue(blocks, "قاعدهٔ موبایلِ نوار تب پیدا نشد")
        last = blocks[-1][: blocks[-1].index("\n}")]
        self.assertNotIn("display: none", last)
        self.assertIn(".tabbar .tabnav { width: 30px; display: inline-flex; }", last)

    def test_arrows_are_centered(self):
        self.assertIn(".tabbar .tabnav { display: inline-flex; align-items: center; justify-content: center; }",
                      self.css)


if __name__ == "__main__":
    unittest.main()
