#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""اجزای مشترک PDF فارسی (RTL) برای راهنماها — fpdf2 + Vazirmatn."""
from fpdf import FPDF
from fpdf.enums import XPos, YPos

FONT_DIR = "/home/user/.fonts"
MONO = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"

INK = (26, 32, 44)
MUTED = (99, 110, 114)
ACCENT = (12, 86, 140)
ACCENT_SOFT = (232, 241, 248)
OK = (22, 122, 70)
OK_SOFT = (231, 246, 238)
WARN = (176, 92, 0)
WARN_SOFT = (253, 243, 226)
RED = (214, 45, 32)
CODE_BG = (243, 244, 246)
FA_DIGITS = "۰۱۲۳۴۵۶۷۸۹"


def fa_num(x):
    return "".join(FA_DIGITS[int(d)] if d.isdigit() else d for d in str(x))


def has_persian(text: str) -> bool:
    return any("\u0600" <= ch <= "\u06FF" or ch == "\u200c" for ch in text)


class Guide(FPDF):
    def __init__(self, title="راهنما"):
        super().__init__(orientation="P", unit="mm", format="A4")
        self.doc_title = title
        self.set_auto_page_break(auto=True, margin=16)
        self.set_margins(16, 15, 16)
        self.add_font("vazir", "", f"{FONT_DIR}/Vazirmatn-Regular.ttf")
        self.add_font("vazir", "B", f"{FONT_DIR}/Vazirmatn-Bold.ttf")
        self.add_font("mono", "", MONO)
        try:
            self.set_text_shaping(True, direction="rtl")
        except Exception:
            pass

    # ---------- پایه ----------
    def shape_for(self, text):
        try:
            self.set_text_shaping(True, direction="rtl" if has_persian(text) else "ltr")
        except Exception:
            pass

    def footer(self):
        if self.page_no() == 1:
            return
        self.set_y(-12)
        self.set_font("vazir", "", 8)
        self.set_text_color(*MUTED)
        self.cell(0, 6, self.doc_title, align="R")
        self.set_xy(16, -12)
        self.cell(0, 6, f"صفحهٔ {fa_num(self.page_no() - 1)}", align="L")

    def cover_page(self, title, subtitle, intro, meta, footer_note):
        self.add_page()
        self.set_fill_color(*ACCENT)
        self.rect(0, 0, 210, 60, style="F")
        self.set_y(18)
        self.set_font("vazir", "B", 23)
        self.set_text_color(255, 255, 255)
        self.cell(0, 12, title, align="C", new_x=XPos.LMARGIN, new_y=YPos.NEXT)
        self.set_font("vazir", "", 12)
        self.cell(0, 8, subtitle, align="C", new_x=XPos.LMARGIN, new_y=YPos.NEXT)

        self.set_y(74)
        self.set_font("vazir", "", 11)
        self.set_text_color(*INK)
        self.shape_for(intro)
        self.multi_cell(0, 7.4, intro, align="R")
        self.ln(8)
        for k, v in meta:
            self.set_font("vazir", "B", 10)
            self.set_text_color(*MUTED)
            self.set_x(16)
            self.cell(30, 8, k + ":", align="R")
            self.set_font("vazir", "", 10)
            self.set_text_color(*INK)
            self.cell(0, 8, v, align="R", new_x=XPos.LMARGIN, new_y=YPos.NEXT)

        self.set_y(240)
        self.set_draw_color(*ACCENT)
        self.set_line_width(0.5)
        self.line(16, self.get_y(), 194, self.get_y())
        self.ln(4)
        self.set_font("vazir", "", 9)
        self.set_text_color(*MUTED)
        self.shape_for(footer_note)
        self.multi_cell(0, 6, footer_note, align="R")

    def h1(self, text, num=None):
        if self.get_y() > 250:
            self.add_page()
        self.ln(2)
        self.set_fill_color(*ACCENT)
        y = self.get_y()
        self.rect(195, y + 2.5, 3.2, 3.2, style="F")
        self.set_font("vazir", "B", 15)
        self.set_text_color(*ACCENT)
        title = f"{num}) {text}" if num else text
        self.set_x(16)
        self.shape_for(title)
        self.cell(174, 9, title, align="R", new_x=XPos.LMARGIN, new_y=YPos.NEXT)
        self.set_draw_color(210, 216, 222)
        self.set_line_width(0.3)
        self.line(16, self.get_y() + 1, 194, self.get_y() + 1)
        self.ln(4.5)

    def h2(self, text):
        if self.get_y() > 258:
            self.add_page()
        self.ln(1)
        self.set_font("vazir", "B", 11.5)
        self.set_text_color(*INK)
        self.shape_for(text)
        self.set_x(16)
        self.cell(178, 7, text, align="R", new_x=XPos.LMARGIN, new_y=YPos.NEXT)
        self.ln(0.5)

    def p(self, text, size=10, color=INK, gap=2.5):
        self.set_font("vazir", "", size)
        self.set_text_color(*color)
        self.shape_for(text)
        self.multi_cell(0, 6.5, text, align="R")
        self.ln(gap)

    def path_line(self, text):
        """مسیر صفحه/دستور به‌صورت لاتین در یک خط"""
        self.set_font("mono", "", 8.6)
        self.set_text_color(*MUTED)
        try:
            self.set_text_shaping(True, direction="ltr")
        except Exception:
            pass
        self.set_fill_color(*CODE_BG)
        self.set_x(16)
        self.cell(178, 6.2, " " + text, align="L", fill=True,
                  new_x=XPos.LMARGIN, new_y=YPos.NEXT)
        try:
            self.set_text_shaping(True, direction="rtl")
        except Exception:
            pass
        self.ln(2)

    def bullets(self, items, size=9.8):
        for it in items:
            y = self.get_y()
            self.set_fill_color(*ACCENT)
            self.ellipse(192.5, y + 2.3, 1.7, 1.7, style="F")
            self.set_font("vazir", "", size)
            self.set_text_color(*INK)
            self.shape_for(it)
            self.set_x(16)
            self.multi_cell(174, 6.2, it, align="R")
            self.ln(0.4)
        self.ln(1)

    def code(self, text, size=8.4):
        lines = text.strip("\n").split("\n")
        longest = max((len(x) for x in lines), default=1)
        size = max(6.2, min(size, 168.0 / max(longest * 0.60, 1)))
        h = len(lines) * (size * 0.52) + 6
        if self.get_y() + h > 268:
            self.add_page()
        y0 = self.get_y()
        self.set_fill_color(*CODE_BG)
        self.set_draw_color(214, 220, 226)
        self.set_line_width(0.2)
        self.rect(16, y0, 178, h, style="DF")
        try:
            self.set_text_shaping(True, direction="ltr")
        except Exception:
            pass
        self.set_font("mono", "", size)
        self.set_text_color(30, 30, 30)
        self.set_xy(19, y0 + 3)
        for line in lines:
            self.cell(172, size * 0.52, line, align="L", new_x=XPos.LMARGIN, new_y=YPos.NEXT)
            self.set_x(19)
        try:
            self.set_text_shaping(True, direction="rtl")
        except Exception:
            pass
        self.set_y(y0 + h + 3)

    def callout(self, kind, text, size=9.4):
        palette = {"info": (ACCENT, ACCENT_SOFT), "ok": (OK, OK_SOFT), "warn": (WARN, WARN_SOFT)}
        fg, bg = palette[kind]
        self.ln(1)
        approx = max(1, int(len(text) / 92) + 1)
        h = approx * 6.1 + 6
        if self.get_y() + h > 268:
            self.add_page()
        y0 = self.get_y()
        self.set_fill_color(*bg)
        self.rect(16, y0, 178, h, style="F")
        self.set_fill_color(*fg)
        self.rect(190, y0, 4, h, style="F")
        self.set_font("vazir", "", size)
        self.set_text_color(*INK)
        self.shape_for(text)
        self.set_xy(18, y0 + 3)
        self.multi_cell(166, 6.1, text, align="R")
        self.set_y(y0 + h + 3)

    def table(self, headers, rows, widths=None, size=9):
        total, n = 178, len(headers)
        widths = widths or [total / n] * n
        line_h = 5.6

        def header():
            self.set_font("vazir", "B", size)
            self.set_fill_color(*ACCENT)
            self.set_text_color(255, 255, 255)
            self.set_x(16)
            for head, w in zip(headers, widths):
                self.shape_for(head)
                self.cell(w, 8, head, align="C", fill=True)
            self.ln(8)
            self.set_text_color(*INK)

        if self.get_y() > 235:
            self.add_page()
        header()
        for idx, row in enumerate(rows):
            self.set_font("vazir", "", size)
            wrapped = []
            for cell_text, w in zip(row, widths):
                wrapped.append(self.multi_cell(w - 3, line_h, cell_text, align="R",
                                               dry_run=True, output="LINES"))
            h = max(len(x) for x in wrapped) * line_h + 3
            if self.get_y() + h > 272:
                self.add_page()
                header()
            y0 = self.get_y()
            if idx % 2 == 0:
                self.set_fill_color(248, 250, 252)
                self.rect(16, y0, total, h, style="F")
            x = 194
            for lines, w in zip(wrapped, widths):
                x -= w
                for j, line in enumerate(lines):
                    self.shape_for(line)
                    self.set_xy(x + 1.5, y0 + 1.5 + j * line_h)
                    self.cell(w - 3, line_h, line, align="R")
            self.set_draw_color(226, 232, 240)
            self.set_line_width(0.15)
            self.line(16, y0 + h, 194, y0 + h)
            self.set_y(y0 + h + 1)
        self.ln(3)

    def figure(self, img_path, width=176):
        from PIL import Image
        with Image.open(img_path) as im:
            w0, h0 = im.size
        h = width * h0 / w0
        if self.get_y() + h > 268:
            self.add_page()
        x = 16 + (178 - width) / 2
        y = self.get_y()
        self.set_draw_color(214, 220, 226)
        self.set_line_width(0.25)
        self.rect(x - 1, y - 1, width + 2, h + 2)
        self.image(img_path, x=x, y=y, w=width)
        self.set_y(y + h + 3)

    def legend(self, items, size=9.4):
        """راهنمای شماره‌های کادرهای قرمز روی تصویر."""
        for num, desc in items:
            if self.get_y() > 264:
                self.add_page()
            y = self.get_y()
            self.set_fill_color(*RED)
            self.ellipse(188.5, y + 0.6, 5.2, 5.2, style="F")
            self.set_font("vazir", "B", 7.6)
            self.set_text_color(255, 255, 255)
            self.set_xy(188.5, y + 1.5)
            self.cell(5.2, 3.4, fa_num(num), align="C")
            self.set_font("vazir", "", size)
            self.set_text_color(*INK)
            self.shape_for(desc)
            self.set_xy(16, y)
            self.multi_cell(170, 6.0, desc, align="R")
            self.ln(0.6)
        self.ln(2)
