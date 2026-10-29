# -*- coding: utf-8 -*-
"""موتور متن راست‌به‌چپ برای ReportLab.

ReportLab متن راست‌به‌چپ نمی‌شناسد؛ اگر متنِ آماده‌شده (reshaped + reversed) به
Paragraph داده شود، شکستن خط و ترازبندی از چپ انجام می‌شود و ترتیب خطوط به‌هم می‌ریزد.
این ماژول شکستن خط را در ترتیب منطقی انجام می‌دهد و هر خط را در ترتیب نمایش رسم می‌کند.
"""
import arabic_reshaper
from bidi.algorithm import get_display
from reportlab.lib.colors import HexColor
from reportlab.pdfbase import pdfmetrics
from reportlab.platypus import Flowable

_RS = arabic_reshaper.ArabicReshaper(configuration={
    "delete_harakat": False,
    "support_ligatures": True,
    "delete_tatweel": False,
})

_WIDTH_CACHE = {}


def shape(text):
    try:
        return _RS.reshape(str(text))
    except Exception:
        return str(text)


def visual(text):
    """رشته در ترتیب نمایش (برای رسم)."""
    return get_display(shape(text), base_dir="R")


def w(text, font, size):
    key = (text, font, size)
    hit = _WIDTH_CACHE.get(key)
    if hit is None:
        hit = pdfmetrics.stringWidth(text, font, size)
        _WIDTH_CACHE[key] = hit
    return hit


class RT(Flowable):
    """پاراگراف فارسی با شکست خط در ترتیب منطقی و ترازبندی راست."""

    def __init__(self, text, font="Vazir", size=9.4, leading=None, align="right",
                 color="#20293A", indent=0.0, bullet=None, space_after=0.0,
                 keep_with_next=False):
        Flowable.__init__(self)
        self.text = "" if text is None else str(text)
        self.font = font
        self.size = size
        self.leading = leading or size * 1.72
        self.align = align
        self.color = HexColor(color) if isinstance(color, str) else color
        self.indent = indent
        self.bullet = bullet
        self.space_after = space_after
        self.keep_with_next = keep_with_next
        self._lines = []
        self._h = 0.0
        self._w = 0.0
        self._avail_text = 0.0
        self._bullet_w = 0.0

    # ------------------------------------------------------------ layout
    def getKeepWithNext(self):
        return self.keep_with_next

    def _space_w(self):
        return w(" ", self.font, self.size)

    def _layout(self, avail):
        avail = max(20.0, avail)
        bullet_w = w(shape(self.bullet + " "), self.font, self.size) if self.bullet else 0.0
        avail_text = avail - self.indent - bullet_w

        tokens = self.text.split()
        self._avail_text = avail_text
        self._bullet_w = bullet_w
        if not tokens:
            self._lines = []
            return

        sp = self._space_w()
        lines, cur, cur_w = [], [], 0.0
        for tok in tokens:
            tw = w(shape(tok), self.font, self.size)
            need = tw if not cur else cur_w + sp + tw
            if cur and need > avail_text:
                lines.append((cur, cur_w))
                cur, cur_w = [tok], tw
            else:
                cur.append(tok)
                cur_w = need
        if cur:
            lines.append((cur, cur_w))
        self._lines = lines

    def wrap(self, aw, ah):
        self._layout(aw)
        self._w = aw
        self._h = max(1, len(self._lines)) * self.leading + self.space_after
        return aw, self._h

    # ------------------------------------------------------------ render
    def draw(self):
        self._draw_at(self.canv, self._w, self._h - self.space_after)

    def _draw_at(self, c, right, top_y):
        c.saveState()
        c.setFillColor(self.color)
        c.setFont(self.font, self.size)
        y = top_y - self.size * 0.80
        sp = self._space_w()
        bullet_w = self._bullet_w
        avail_text = self._avail_text or right

        for idx, (toks, _natural) in enumerate(self._lines):
            is_last = idx == len(self._lines) - 1
            x_right = right - bullet_w - self.indent

            if self.align == "justify" and not is_last and len(toks) > 1:
                # ترازبندی دوطرفه: کشش فاصله‌ها در همان رشتهٔ نمایش
                vis_line = visual(" ".join(toks))
                need = avail_text - w(vis_line, self.font, self.size)
                segs = vis_line.split(" ")
                gaps = len(segs) - 1
                if need > 0.5 and gaps > 0 and sp > 0:
                    total_extra = int(round(need / sp))
                    base, rem = divmod(total_extra, gaps)
                    out = [segs[0]]
                    for i, sg in enumerate(segs[1:]):
                        k = 1 + base + (1 if i < rem else 0)
                        out.append(" " * k)
                        out.append(sg)
                    vis_line = "".join(out)
                c.drawRightString(x_right, y, vis_line)
            else:
                line_text = visual(" ".join(toks))
                if self.align == "center":
                    c.drawCentredString(x_right - avail_text / 2.0, y, line_text)
                else:
                    c.drawRightString(x_right, y, line_text)

            if idx == 0 and self.bullet:
                c.setFillColor(self.color)
                c.drawRightString(right, y, visual(self.bullet))
            y -= self.leading
        c.restoreState()


class TocRow(Flowable):
    """یک ردیف فهرست مطالب: عنوان راست، شمارهٔ صفحه چپ، نقطه‌چین میان آن‌ها."""

    def __init__(self, title, page, level=0, size=10.4):
        Flowable.__init__(self)
        self.title = title
        self.page = page
        self.level = level
        self.size = size if level == 0 else size - 1.3
        self.font = "VazirB" if level == 0 else "Vazir"
        self.leading = self.size * 1.95
        self.color = HexColor("#152238") if level == 0 else HexColor("#3A4353")

    def wrap(self, aw, ah):
        self._w = aw
        return aw, self.leading

    def draw(self):
        c = self.canv
        c.saveState()
        indent = 0 if self.level == 0 else 14
        num = fa_digits(self.page)
        num_w = w(num, self.font, self.size)
        title = visual(self.title)
        t_w = w(title, self.font, self.size)
        right = self._w
        y = (self.leading - self.size) / 2 + 1

        c.setFillColor(self.color)
        c.setFont(self.font, self.size)
        c.drawRightString(right - indent, y, title)

        c.setFillColor(HexColor("#8A91A0"))
        c.setFont("Vazir", self.size - 0.4)
        c.drawString(0, y, num)

        x_from = num_w + 4
        x_to = right - indent - min(t_w, right - indent - 30) - 6
        if x_to > x_from:
            c.setStrokeColor(HexColor("#C7CCD6"))
            c.setLineWidth(0.6)
            c.setDash(0.9, 2.4)
            c.line(x_from, y + 2.2, x_to, y + 2.2)
            c.setDash()
        c.restoreState()


def fa_digits(n):
    digits = "۰۱۲۳۴۵۶۷۸۹"
    return "".join(digits[int(ch)] if ch.isdigit() else ch for ch in str(n))
