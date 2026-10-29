# -*- coding: utf-8 -*-
"""سازندهٔ PDF پروپوزال — قالب راست‌به‌چپ روی سربرگ رسمی شرکت فروغ آریا."""
import os

from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (BaseDocTemplate, CondPageBreak, Flowable, Frame,
                                NextPageTemplate, PageBreak, PageTemplate, Spacer,
                                Table, TableStyle)

import content as C
from rtltext import RT, TocRow, fa_digits, visual, w

HERE = os.path.dirname(os.path.abspath(__file__))
FONT_DIR = "/home/user/TotallCollectionSoftware/TotallCollectionSoftware/src/Inventory.Api/Resources/fonts"
LETTERHEAD = os.path.join(HERE, "letterhead.jpg")
LOGO = os.path.join(HERE, "logo.png")
OUT_PDF = os.path.join(HERE, "پروپوزال-فنی-تجاری-فروغ-آریا.pdf")

pdfmetrics.registerFont(TTFont("Vazir", os.path.join(FONT_DIR, "Vazirmatn-Regular.ttf")))
pdfmetrics.registerFont(TTFont("VazirB", os.path.join(FONT_DIR, "Vazirmatn-Bold.ttf")))

# --- رنگ‌های سربرگ شرکت (نمونه‌برداری‌شده از سربرگ رسمی) --------------------
NAVY = colors.HexColor("#1F2937")
NAVY_SOFT = colors.HexColor("#334155")
RED = colors.HexColor("#B91C32")
GRAY = colors.HexColor("#5C6474")
GRAY_LIGHT = colors.HexColor("#7C8698")
INK = "#20293A"
LINE = colors.HexColor("#CBD5E1")
LINE_SOFT = colors.HexColor("#DDE3EA")
BG_SOFT = colors.HexColor("#F3F4F6")
BG_BLUE = colors.HexColor("#F1F5F9")
BG_WARM = colors.HexColor("#FFF1F2")

PAGE_W, PAGE_H = A4            # 595.27 x 841.89
LM, RM = 52.0, 35.0            # مطابق سربرگ نمونه
TM, BM = 72.0, 40.0
CONTENT_W = PAGE_W - LM - RM   # 508.27

TOC = []
TOC_NOW = []


# ------------------------------------------------------------------ helpers
def P(text, size=9.4, leading=None, align="justify", color=INK, space_after=0,
      font="Vazir", keep=False):
    return RT(text, font=font, size=size, leading=leading, align=align, color=color,
              space_after=space_after, keep_with_next=keep)


def put(c, rt, width, x, y_top):
    """رسم یک RT در مختصات مطلق؛ y_top لبهٔ بالای متن است."""
    rt.wrap(width, 10000)
    rt.drawOn(c, x, y_top - rt._h)
    return rt._h


def rtext(c, text, size=9.2, y_top=0.0, right=None, width=None, font="Vazir",
          color=INK, leading=None, align="right"):
    right = PAGE_W - RM if right is None else right
    width = (right - LM) if width is None else width
    rt = RT(text, font=font, size=size, leading=leading, align=align, color=color)
    return put(c, rt, width, right - width, y_top)


class _CellFlow(RT):
    """متن سلول جدول — عرض مؤثر = عرض ستون منهای حاشیه."""

    def __init__(self, text, **kw):
        self._pad = kw.pop("pad", 6)
        super().__init__(text, **kw)

    def wrap(self, aw, ah):
        avail = max(18.0, aw - 2 * self._pad)
        self._layout(avail)
        self._w = avail
        self._h = max(1, len(self._lines)) * self.leading
        return aw, self._h + 0.1


def KV(headers, rows, widths, size=8.4, leading=13.6):
    """جدول با ستون منطقی اول در سمت راست."""
    rev_h = list(reversed(headers))
    rev_rows = [list(reversed(r)) for r in rows]
    rev_w = list(reversed(widths))
    total = sum(rev_w)

    data = [[_CellFlow(h, font="VazirB", size=size + 0.1, leading=leading, align="center",
                       color=colors.white, pad=6) for h in rev_h]]
    for r in rev_rows:
        data.append([_CellFlow(c, size=size, leading=leading, align="right", pad=6) for c in r])

    colw = [CONTENT_W * x / total for x in rev_w]
    t = Table(data, colWidths=colw, repeatRows=1, hAlign="RIGHT")
    style = [
        ("BACKGROUND", (0, 0), (-1, 0), NAVY),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("GRID", (0, 0), (-1, -1), 0.55, NAVY_SOFT),
    ]
    for i in range(1, len(data)):
        if i % 2 == 0:
            style.append(("BACKGROUND", (0, i), (-1, i), BG_SOFT))
    t.setStyle(TableStyle(style))
    return t


def steps_table(items):
    rows = []
    for i, (title, desc) in enumerate(items, 1):
        body = [
            RT(title, font="VazirB", size=8.9, leading=14, align="right", color="#1F2937"),
            Spacer(1, 1),
            RT(desc, size=8.5, leading=13.8, align="justify"),
        ]
        rows.append([body, _CellFlow(fa_digits(i), font="VazirB", size=12, align="center",
                                     color="#B91C32", leading=16, pad=2)])
    t = Table(rows, colWidths=[CONTENT_W - 28, 28], hAlign="RIGHT")
    t.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("TOPPADDING", (0, 0), (-1, -1), 4.5),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 5.5),
        ("LEFTPADDING", (0, 0), (-1, -1), 2),
        ("RIGHTPADDING", (0, 0), (-1, -1), 2),
        ("LINEBELOW", (0, 0), (-1, -2), 0.4, LINE_SOFT),
        ("LINEBEFORE", (1, 0), (1, -1), 0.4, LINE_SOFT),
    ]))
    return t


class LatinLine(Flowable):
    """خط لاتین با فاصله‌گذاری دلخواه — به‌صورت یک رشته رسم می‌شود."""

    def __init__(self, text, font="Vazir", size=7.6, leading=11, color="#B91C32"):
        Flowable.__init__(self)
        self.text = text
        self.font = font
        self.size = size
        self.leading = leading
        self.color = colors.HexColor(color)

    def wrap(self, aw, ah):
        self._w = aw
        return aw, self.leading

    def draw(self):
        c = self.canv
        c.saveState()
        c.setFillColor(self.color)
        c.setFont(self.font, self.size)
        c.drawRightString(self._w, (self.leading - self.size) / 2 + 1, self.text)
        c.restoreState()


class CalloutBox(Flowable):
    """جعبهٔ نکته — رسم کامل توسط خود Flowable برای کنترل دقیق چیدمان."""

    PAD_R = 16
    PAD_L = 14
    PAD_V = 9

    def __init__(self, title, text, tone="info"):
        Flowable.__init__(self)
        self.bar = NAVY_SOFT if tone == "info" else RED
        self.bg = BG_BLUE if tone == "info" else BG_WARM
        self._title = RT(title, font="VazirB", size=9.4, leading=15, align="right",
                         color=self.bar.hexval().replace("0x", "#"))
        self._text = RT(text, size=8.8, leading=14.8, align="justify")

    def wrap(self, aw, ah):
        inner = max(40.0, aw - 4 - self.PAD_R - self.PAD_L)
        self._title._layout(inner)
        self._title._w = inner
        self._text._layout(inner)
        self._text._w = inner
        self._w = aw
        th = max(1, len(self._title._lines)) * self._title.leading
        xh = max(1, len(self._text._lines)) * self._text.leading
        self._h = self.PAD_V * 2 + th + 3 + xh
        return aw, self._h

    def draw(self):
        c = self.canv
        c.saveState()
        c.setFillColor(self.bg)
        c.rect(0, 0, self._w, self._h, stroke=0, fill=1)
        c.setFillColor(self.bar)
        c.rect(self._w - 4, 0, 4, self._h, stroke=0, fill=1)
        c.restoreState()
        x_right = self._w - 4 - self.PAD_R
        top = self._h - self.PAD_V
        th = max(1, len(self._title._lines)) * self._title.leading
        self._title._draw_at(c, x_right, top)
        self._text._draw_at(c, x_right, top - th - 3)


def callout_box(title, text, tone="info"):
    return CalloutBox(title, text, tone)


def kpi_row(items):
    cells = []
    for val, lbl in items:
        cells.append([
            RT(val, font="VazirB", size=17, leading=23, align="center", color="#B91C32"),
            Spacer(1, 1),
            RT(lbl, size=7.9, leading=12.5, align="center", color="#5C6474"),
        ])
    n = len(cells)
    t = Table([list(reversed(cells))], colWidths=[CONTENT_W / n] * n, hAlign="RIGHT")
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), BG_BLUE),
        ("BOX", (0, 0), (-1, -1), 0.5, LINE),
        ("INNERGRID", (0, 0), (-1, -1), 0.5, LINE),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("TOPPADDING", (0, 0), (-1, -1), 9),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 9),
    ]))
    return t


# ------------------------------------------------------------------ bands
class H1Band(RT):
    def __init__(self, text, width):
        super().__init__(text, font="VazirB", size=13.2, leading=21, align="right",
                         color=colors.white, keep_with_next=True)
        self._tocmark = None

    def wrap(self, aw, ah):
        self._layout(aw - 40)
        self._w = aw
        self._h = max(len(self._lines) * self.leading + 15, 33)
        return aw, self._h

    def draw(self):
        c = self.canv
        c.setFillColor(NAVY)
        c.rect(0, 0, self._w, self._h, stroke=0, fill=1)
        c.setFillColor(RED)
        c.rect(self._w - 5, 0, 5, self._h, stroke=0, fill=1)
        n = len(self._lines)
        top = (self._h + n * self.leading) / 2.0
        self._draw_at(c, self._w - 18, top)


class H2Band(RT):
    def __init__(self, text, width):
        super().__init__(text, font="VazirB", size=11.2, leading=19, align="right",
                         color="#1F2937", keep_with_next=True)
        self._tocmark = None

    def wrap(self, aw, ah):
        self._layout(aw - 14)
        self._w = aw
        self._h = len(self._lines) * self.leading + 11
        return aw, self._h

    def draw(self):
        c = self.canv
        c.setFillColor(RED)
        c.rect(self._w - 3.4, 3, 3.4, self._h - 8, stroke=0, fill=1)
        c.setStrokeColor(LINE_SOFT)
        c.setLineWidth(0.7)
        c.line(0, 0.8, self._w, 0.8)
        n = len(self._lines)
        top = (self._h + n * self.leading) / 2.0 + 1.5
        self._draw_at(c, self._w - 15, top)



def meta_line(c, lbl, val, y, size=8.3, color=None):
    """خط «برچسب : مقدار» راست‌چین در بلوک شمال‌غربی سربرگ، بدون بیرون‌زدگی از کادر."""
    txt = visual(f"{lbl} : {val}")
    c.setFont("Vazir", size)
    width = w(txt, "Vazir", size)
    x_right = max(104.0, width + 10.0)
    c.setFillColor(color or colors.HexColor("#5C6474"))
    c.drawRightString(x_right, y, txt)


# ------------------------------------------------------------------ page deco
def draw_letterhead(c):
    """سربرگ رسمی شرکت، تمام‌صفحه و بدون حاشیه."""
    c.drawImage(ImageReader(LETTERHEAD), 0, 0, width=PAGE_W, height=PAGE_H,
                preserveAspectRatio=False, mask=None)


def on_cover(c, doc):
    c.saveState()
    draw_letterhead(c)

    # سرصفحهٔ شمال‌غربی: تاریخ / شماره / نسخه (هم‌سبک با سربرگ)
    meta = [("تاریخ", C.DOC["date_fa"]),
            ("شماره", C.DOC["doc_no"]),
            ("نسخه", C.DOC["version"])]
    y = PAGE_H - 26
    for lbl, val in meta:
        meta_line(c, lbl, val, y)
        y -= 25

    c.setFillColor(RED)
    c.setFont("VazirB", 11.5)
    c.drawCentredString(PAGE_W / 2, PAGE_H - 252, visual("پروپوزال فنی – تجاری"))

    c.setFillColor(NAVY)
    c.setFont("VazirB", 27)
    c.drawCentredString(PAGE_W / 2, PAGE_H - 300, visual("سامانهٔ یکپارچهٔ فروغ آریا"))

    c.setStrokeColor(RED)
    c.setLineWidth(2.0)
    c.line(PAGE_W / 2 - 118, PAGE_H - 315, PAGE_W / 2 + 118, PAGE_H - 315)

    c.setFillColor(GRAY)
    c.setFont("Vazir", 12.5)
    c.drawCentredString(PAGE_W / 2, PAGE_H - 344, visual("معرفی چهار ماژول کاربردی"))

    c.setFillColor(NAVY_SOFT)
    c.setFont("VazirB", 10)
    c.drawCentredString(PAGE_W / 2, PAGE_H - 378, visual(" | ".join(C.DOC["modules"])))

    # کارت شناسایی سند
    top = PAGE_H - 420
    box_h = 96.0
    c.setFillColor(colors.white)
    c.setStrokeColor(colors.HexColor("#E3E6EB"))
    c.setLineWidth(0.8)
    c.roundRect(LM, top - box_h, CONTENT_W, box_h, 3.5, stroke=1, fill=1)
    c.setFillColor(RED)
    c.rect(PAGE_W - RM - 3, top - box_h + 10, 3, box_h - 20, stroke=0, fill=1)

    rows = [("تهیه‌کننده", C.DOC["vendor"]),
            ("مخاطب", "مدیریت محترم شرکت ................................"),
            ("موضوع", "معرفی چهار ماژول: اطلاعات پایه، اتوماسیون اداری، دستور کار، آرشیو اسناد")]
    yy = top - 26
    for lbl, val in rows:
        c.setFillColor(GRAY_LIGHT)
        c.setFont("Vazir", 8.4)
        c.drawRightString(PAGE_W - RM - 16, yy, visual(lbl))
        c.setFillColor(NAVY if lbl != "موضوع" else NAVY_SOFT)
        c.setFont("VazirB" if lbl != "موضوع" else "Vazir", 9.6 if lbl != "موضوع" else 8.8)
        c.drawRightString(PAGE_W - RM - 74, yy, visual(val))
        yy -= 26

    # نوار شاخص‌ها
    strip = [("۴", "ماژول"), ("۳۵۶", "جدول داده"), ("۱۵۹۷", "سرویس وب"), ("۲۹۴", "صفحهٔ رابط کاربری")]
    sw = [104, 116, 128, 160]
    x0 = PAGE_W - RM
    c.setStrokeColor(colors.HexColor("#E3E6EB"))
    c.setLineWidth(0.7)
    for (val, lbl), wd in zip(strip, sw):
        c.setFillColor(NAVY)
        c.setFont("VazirB", 15)
        c.drawCentredString(x0 - wd / 2, 226, visual(val))
        c.setFillColor(GRAY_LIGHT)
        c.setFont("Vazir", 7.8)
        c.drawCentredString(x0 - wd / 2, 212, visual(lbl))
        x0 -= wd + 8
        if x0 - LM > 20:
            c.line(x0 + 4, 208, x0 + 4, 232)

    c.setFillColor(GRAY_LIGHT)
    c.setFont("Vazir", 7.8)
    c.drawRightString(PAGE_W - RM, 26,
                      visual("محرمانه — این سند برای مخاطب تعیین‌شده تهیه شده است"))
    c.drawString(LM, 26, C.DOC["date_en"])
    c.restoreState()


def on_body(c, doc):
    c.saveState()
    # نشان و سرصفحهٔ سبک برای صفحات داخلی
    lw = 56.0
    c.drawImage(ImageReader(LOGO), PAGE_W - RM - lw, PAGE_H - 62, width=lw, height=lw * 238 / 292,
                mask="auto")

    c.setFillColor(GRAY)
    c.setFont("Vazir", 8.2)
    c.drawString(LM, PAGE_H - 40, visual(f"{C.DOC['doc_no']}  •  {C.DOC['date_fa']}"))
    c.setFillColor(GRAY_LIGHT)
    c.setFont("Vazir", 7.9)
    c.drawString(LM, PAGE_H - 52, visual("پروپوزال فنی و تجاری — نرم‌افزار یکپارچه فروغ آریا"))

    c.setStrokeColor(LINE_SOFT)
    c.setLineWidth(0.6)
    c.line(LM, PAGE_H - 66, PAGE_W - RM, PAGE_H - 66)
    c.setFillColor(RED)
    c.rect(PAGE_W - RM - 60, PAGE_H - 67.6, 60, 1.6, stroke=0, fill=1)

    c.setStrokeColor(LINE_SOFT)
    c.setLineWidth(0.5)
    c.line(LM, 30, PAGE_W - RM, 30)
    c.setFillColor(GRAY_LIGHT)
    c.setFont("Vazir", 7.8)
    c.drawString(LM, 19, visual("محرمانه — شرکت فروغ آریا"))
    total = getattr(doc, "total_pages", None)
    label = f"صفحهٔ {fa_digits(doc.page)}" + (f" از {fa_digits(total)}" if total else "")
    c.drawRightString(PAGE_W - RM, 19, visual(label))
    c.restoreState()


# ------------------------------------------------------------------ price sheet
PRICE_DESC = {
    "اطلاعات پایه": "تعریف شرکت‌ها و شعب، ساختار سازمانی و واحدهای شمارهٔ نامه، کاربران، نقش‌ها و "
                    "سطوح دسترسی، ساختار شمارهٔ نامه، داده‌های پایه",
    "اتوماسیون اداری": "دبیرخانهٔ الکترونیکی، نامه‌های داخلی، وارده و صادره، ارجاع و گردش مکاتبات",
    "دستور کار": "ثبت، ارجاع، پیگیری، تقویم و گزارش دستورهای کار و پروژه‌های کاری",
    "آرشیو اسناد و مدارک": "بایگانی، طبقه‌بندی، جست‌وجو، سطح دسترسی و ثبت تاریخچهٔ اسناد و مدارک",
}


def price_table(c):
    """جدول قیمت با هندسهٔ دقیقاً مطابق نمونهٔ سربرگ‌دار."""
    cols_lr = [135.0, 225.0, 108.0, CONTENT_W - 468.0]  # مبلغ | شرح | ماژول | ردیف
    head_lr = ["مبلغ پیشنهادی\n(ریال)", "شرح خدمات", "ماژول", "ردیف"]
    data = [[_CellFlow(h, font="VazirB", size=7.7, leading=11.5, align="center",
                       color=colors.white, pad=5) for h in head_lr]]
    for i, m in enumerate(C.DOC["modules"], 1):
        desc = PRICE_DESC.get(m, "")
        data.append([
            _CellFlow("..................", size=8.4, leading=15, align="center", pad=5),
            _CellFlow(desc, size=7.4, leading=11.2, align="center", pad=6),
            _CellFlow(m, font="VazirB", size=8.2, leading=12, align="center", pad=6),
            _CellFlow(fa_digits(i), size=8.6, leading=12, align="center", pad=5),
        ])
    data.append([
        _CellFlow("..................", font="VazirB", size=9.3, leading=15, align="center", pad=5),
        _CellFlow("جمع کل پیشنهادی", font="VazirB", size=9.3, leading=15, align="right", pad=8),
        "", "",
    ])
    heights = [27] + [37] * len(C.DOC["modules"]) + [31]
    t = Table(data, colWidths=cols_lr, rowHeights=heights, hAlign="LEFT")
    style = [
        ("BACKGROUND", (0, 0), (-1, 0), NAVY),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("GRID", (0, 0), (-1, -1), 0.65, NAVY_SOFT),
        ("BACKGROUND", (0, 2), (-1, 2), BG_SOFT),
        ("BACKGROUND", (0, 4), (-1, 4), BG_SOFT),
        ("BACKGROUND", (0, 5), (-1, 5), BG_WARM),
        ("SPAN", (1, 5), (3, 5)),
    ]
    t.setStyle(TableStyle(style))
    t.wrapOn(c, CONTENT_W, PAGE_H)
    t.drawOn(c, LM, PAGE_H - 277 - sum(heights))


def draw_price_sheet(c):
    """صفحهٔ پیشنهاد قیمت روی سربرگ کامل شرکت (الگوی نمونهٔ یارکو)."""
    draw_letterhead(c)

    # تاریخ / شماره / پیوست (جای نقطه‌چین‌های سربرگ)
    meta = [("تاریخ", C.DOC["date_fa"]),
            ("شماره", C.DOC["doc_no"]),
            ("پیوست", "ندارد")]
    y = PAGE_H - 26
    for lbl, val in meta:
        meta_line(c, lbl, val, y, color=colors.HexColor("#334155"))
        y -= 25

    # عنوان
    c.setFillColor(NAVY)
    c.setFont("VazirB", 15)
    c.drawCentredString(PAGE_W / 2, PAGE_H - 152, visual("پیشنهاد قیمت و ارائه ماژول‌های نرم‌افزار"))
    c.setStrokeColor(RED)
    c.setLineWidth(1.3)
    c.line(180, PAGE_H - 158, 432, PAGE_H - 158)

    rtext(c, "مدیریت محترم شرکت ................................", size=10.5,
          y_top=PAGE_H - 172, font="VazirB", color=NAVY)
    rtext(c, "با سلام و احترام،", size=10, y_top=PAGE_H - 196)
    rtext(c, "پیرو درخواست آن شرکت محترم، پیشنهاد قیمت ماژول‌های مورد نیاز به شرح زیر تقدیم می‌گردد.",
          size=9.2, y_top=PAGE_H - 219)
    rtext(c, "دامنهٔ خدمات شامل ارائهٔ نرم‌افزار، راه‌اندازی اولیه و آموزش کاربران است.",
          size=9.2, y_top=PAGE_H - 236)

    price_table(c)

    rtext(c, "جمع کل به حروف : ..............................................................................",
          size=9.2, y_top=PAGE_H - 500, color="#334155")

    # جعبهٔ اعتبار پیشنهاد
    box_top, box_h = PAGE_H - 535, 52.0
    c.setFillColor(colors.HexColor("#F8FAFC"))
    c.setStrokeColor(LINE)
    c.setLineWidth(0.65)
    c.rect(LM, box_top - box_h, CONTENT_W - 4, box_h, stroke=1, fill=1)
    rtext(c, "اعتبار پیشنهاد:", size=9.2, y_top=box_top - 10, right=PAGE_W - RM - 16,
          font="VazirB", color=NAVY)
    rtext(c, "این پیشنهاد از تاریخ درج، به مدت ........ روز اعتبار دارد. مبالغ فوق به ریال درج شده‌اند.",
          size=8.5, y_top=box_top - 30, right=PAGE_W - RM - 16, color="#334155")

    rtext(c, "با احترام،", size=10, y_top=PAGE_H - 600, right=PAGE_W - 85)
    rtext(c, "مدیرعامل شرکت فروغ آریا", size=10.5, y_top=PAGE_H - 622, right=PAGE_W - 85,
          font="VazirB", color=NAVY)
    c.setStrokeColor(LINE)
    c.setLineWidth(0.55)
    c.line(360, PAGE_H - 679, 525, PAGE_H - 679)
    rtext(c, "محل مهر و امضا", size=7.5, y_top=PAGE_H - 686, right=PAGE_W - 132,
          color=GRAY_LIGHT)


def on_price(c, doc):
    c.saveState()
    draw_price_sheet(c)
    c.restoreState()


class Doc(BaseDocTemplate):
    def __init__(self, path, **kw):
        super().__init__(path, pagesize=A4,
                         leftMargin=LM, rightMargin=RM, topMargin=TM, bottomMargin=BM,
                         title="پروپوزال فنی–تجاری سامانه یکپارچه فروغ آریا",
                         author="شرکت فروغ آریا",
                         subject="پروپوزال چهار ماژول: اطلاعات پایه، اتوماسیون اداری، دستور کار، آرشیو اسناد",
                         creator="Forough Arya", **kw)
        self.total_pages = None
        self.addPageTemplates([
            PageTemplate(id="cover",
                         frames=[Frame(0, 0, PAGE_W, PAGE_H, 0, 0, 0, 0, id="cover")],
                         onPage=on_cover),
            PageTemplate(id="body",
                         frames=[Frame(LM, BM, CONTENT_W, PAGE_H - TM - BM, 0, 0, 0, 0,
                                       id="body")], onPage=on_body),
            PageTemplate(id="price",
                         frames=[Frame(0, 0, PAGE_W, PAGE_H, 0, 0, 0, 0, id="price")],
                         onPage=on_price),
        ])

    def afterFlowable(self, flowable):
        mark = getattr(flowable, "_tocmark", None)
        if mark:
            TOC_NOW.append((mark[0], mark[1], self.page))


# ------------------------------------------------------------------ story
def id_page():
    out = [H1Band("شناسنامهٔ سند", CONTENT_W), Spacer(1, 9)]
    rows = [
        ["عنوان سند", "پروپوزال فنی–تجاری معرفی «سامانهٔ یکپارچه فروغ آریا»"],
        ["موضوع", "معرفی چهار ماژول: اطلاعات پایه، اتوماسیون اداری، دستور کار، آرشیو اسناد و مدارک"],
        ["تهیه‌کننده", "شرکت فروغ آریا"],
        ["تاریخ صدور", f"{C.DOC['date_fa']}   —   میلادی: {C.DOC['date_en'].replace('-', '/')}"],
        ["شمارهٔ سند", C.DOC["doc_no"]],
        ["نسخه", C.DOC["version"]],
        ["وضعیت", "نسخهٔ ارائه به کارفرما — مبالغ صفحهٔ پایانی نیازمند تکمیل توسط واحد بازرگانی است"],
        ["مبنای محتوا", "استخراج مستقیم از کد پروژه: کنترلرها، موجودیت‌های داده، صفحات رابط کاربری و سرویس‌ها"],
    ]
    out.append(KV(["موضوع", "مقدار"], rows, [0.2, 0.8]))
    out.append(Spacer(1, 18))
    out.append(H1Band("فهرست مطالب", CONTENT_W))
    out.append(Spacer(1, 10))

    if TOC:
        for level, title, page in TOC:
            out.append(TocRow(title, page, level=level))
    else:
        out.append(RT("فهرست در اجرای نهایی تکمیل می‌شود.", size=9, color=GRAY))
    out.append(PageBreak())
    return out


def summary_page():
    out = [H1Band("خلاصهٔ مدیریتی", CONTENT_W), Spacer(1, 8)]
    out.append(P("این پروپوزال چهار ماژول از سامانهٔ یکپارچه فروغ آریا را معرفی می‌کند که در کنار هم "
                 "یک زنجیرهٔ کامل اداری می‌سازند:", space_after=6))
    out.append(KV(
        ["ماژول", "کارکرد اصلی", "ارزش کلیدی برای سازمان"],
        [
            ["اطلاعات پایه", "تعریف سازمان، کاربران، نقش‌ها، ساختار شمارهٔ نامه و دادهٔ مرجع",
             "یک‌بار تعریف، مصرف در همهٔ ماژول‌ها؛ مبنای امنیت و صحت داده"],
            ["اتوماسیون اداری", "گردش نامهٔ داخلی، وارده و صادره؛ ارجاع، امضا، دبیرخانه و بایگانی",
             "شفافیت گردش مکاتبات، پاسخ‌دهی مهلت‌دار و کاهش کاغذ"],
            ["دستور کار", "ثبت، محول‌سازی، پیگیری مهلت‌دار، چک‌لیست، پروژه و گزارش عملکرد",
             "تعهدات کاری قابل پیگیری و سنجش عینی به‌موقع بودن"],
            ["آرشیو اسناد و مدارک", "بایگانی درختی، دسترسی نفر‌به‌نفر، نسخه‌گذاری، گردش تأیید، جستجو",
             "پیدا شدن سریع مدارک و جلوگیری از افشا"],
        ],
        [0.19, 0.44, 0.37]))
    out.append(Spacer(1, 12))
    out.append(kpi_row([("۴", "ماژول معرفی‌شده"), ("۳۵۶", "جدول داده در پایگاه داده"),
                        ("۱۵۹۷", "سرویس وب در دسترس"), ("۲۹۴", "صفحهٔ رابط کاربری")]))
    out.append(Spacer(1, 12))
    out.append(P("معیار انتخاب این چهار ماژول، بیشترین اثر روزمره بر جریان کار سازمان است: هر نامه، "
                 "دستور کار و مدرک آرشیوی که در سامانه ثبت می‌شود، در یک پایگاه داده و زیر یک مدل "
                 "دسترسی واحد قرار می‌گیرد.", space_after=8))
    out.append(Spacer(1, 6))
    out.append(callout_box(
        "جمع‌بندی",
        "هر چهار ماژول روی یک پایگاه داده و یک مدل دسترسی مشترک کار می‌کنند؛ بنابراین گزارش‌های "
        "میان‌ماژولی، ارجاع متقابل نامه و کار، و پیوند تمدید مدرک با دستور کار بدون یکپارچه‌سازی "
        "اضافی در دسترس است. مرز دقیق قابلیت‌ها در بخش‌های ۲ تا ۵ و موارد نیازمند تصمیم در بخش ۸ "
        "آمده است."))
    out.append(Spacer(1, 10))
    out.append(P("توجه: مبالغ پیشنهاد در صفحهٔ پایانی عمداً خالی گذاشته شده است تا توسط واحد "
                 "بازرگانی تکمیل و تأیید شود.", size=8, color=GRAY, align="right"))
    out.append(Spacer(1, 14))
    out.append(RT("راهنمای مطالعهٔ سند", font="VazirB", size=10.6, leading=18, align="right",
                  color="#1F2937", space_after=4))
    out.append(Spacer(1, 4))
    out.append(KV(
        ["بخش", "چه چیزی در آن آمده است"],
        [
            ["بخش ۱", "معرفی کلی سامانه، دامنهٔ این سند، روش تدوین و اصول مشترک چهار ماژول"],
            ["بخش‌های ۲ تا ۵", "هر ماژول در یک بخش مستقل: هدف، قابلیت‌ها، گردش کار، نقش‌ها و دسترسی، "
                              "گزارش‌ها و ارتباط با سایر ماژول‌ها"],
            ["بخش ۶", "معماری، پشتهٔ فناوری، مدل داده، امنیت و کنترل دسترسی، استقرار و مقیاس‌پذیری"],
            ["بخش ۷ و ۸", "دامنهٔ خدمات و آموزش، و فهرست مواردی که پیش از قرارداد نیازمند تأیید کارفرماست"],
            ["صفحهٔ پایانی", "پیشنهاد قیمت چهار ماژول، جمع کل، اعتبار پیشنهاد و محل امضا"],
        ],
        [0.18, 0.82]))
    out.append(PageBreak())
    return out


def _block_flows(blk):
    """تبدیل یک بلوک محتوا به لیستی از Flowable."""
    kind = blk[0]
    out = []
    if kind == "h1":
        band = H1Band(blk[1], CONTENT_W)
        band._tocmark = (0, blk[1])
        out += [CondPageBreak(165), Spacer(1, 8), band, Spacer(1, 11)]
    elif kind == "h2":
        band = H2Band(blk[1], CONTENT_W)
        band._tocmark = (1, blk[1])
        out += [Spacer(1, 13), band, Spacer(1, 5)]
    elif kind == "h3":
        out += [RT(blk[1], font="VazirB", size=10.4, leading=18, align="right",
                   color="#1F2937", keep_with_next=True, space_after=2), Spacer(1, 3)]
    elif kind == "p":
        out.append(P(blk[1], space_after=7))
    elif kind == "lead":
        out.append(P(blk[1], font="VazirB", size=10.4, leading=18, color="#5C6474",
                     align="right", space_after=8))
    elif kind == "bullets":
        for it in blk[1]:
            out.append(RT(it, size=9.4, leading=16.2, align="justify", bullet="•",
                          indent=11, space_after=4))
        out.append(Spacer(1, 3))
    elif kind == "table":
        _, headers, rows, opt = blk
        out += [KV(headers, rows, opt["widths"]), Spacer(1, 9)]
    elif kind == "callout":
        out += [Spacer(1, 4), callout_box(blk[1], blk[2], blk[3]), Spacer(1, 8)]
    elif kind == "kpis":
        out += [Spacer(1, 3), kpi_row(blk[1]), Spacer(1, 10)]
    elif kind == "steps":
        out += [steps_table(blk[1]), Spacer(1, 9)]
    elif kind == "pagebreak":
        out.append(PageBreak())
    return out


def body_story():
    """سرِ عنوان‌ها با CondPageBreak محافظت می‌شود تا تنها در انتهای صفحه نماند."""
    blocks = C.BLOCKS
    out = []
    i = 0
    while i < len(blocks):
        blk = blocks[i]
        if blk[0] in ("h2", "h3"):
            out.append(CondPageBreak(92 if blk[0] == "h2" else 74))
            out.extend(_block_flows(blk))
            i += 1
            continue
        out.extend(_block_flows(blk))
        i += 1
    return out


def render(path, total_pages=None, toc_entries=None):
    global TOC, TOC_NOW
    TOC = toc_entries or []
    TOC_NOW = []
    doc = Doc(path)
    doc.total_pages = total_pages
    story = [Spacer(1, 1), NextPageTemplate("body"), PageBreak()]
    story.extend(id_page())
    story.extend(summary_page())
    story.extend(body_story())
    story.append(NextPageTemplate("price"))
    story.append(PageBreak())
    story.append(Spacer(1, 1))
    doc.build(story)
    return doc.page, list(TOC_NOW)


def add_bookmarks(path, entries):
    """نشانک‌های PDF (فهرست ناوبری) با عنوان فارسی منطقی."""
    try:
        import pymupdf
    except ImportError:
        return
    toc = [[1 if lvl == 0 else 2, title, page] for lvl, title, page in entries]
    doc = pymupdf.open(path)
    doc.set_toc(toc)
    doc.set_metadata({
        "title": "پروپوزال فنی–تجاری سامانه یکپارچه فروغ آریا",
        "author": "شرکت فروغ آریا",
        "subject": "اطلاعات پایه، اتوماسیون اداری، دستور کار، آرشیو اسناد و مدارک",
        "keywords": "پروپوزال، فروغ آریا، اتوماسیون اداری، دستور کار، آرشیو اسناد، اطلاعات پایه",
        "creator": "Forough Arya",
        "producer": "ReportLab + RTL engine",
    })
    doc.saveIncr()
    doc.close()


def main():
    tmp = os.path.join(HERE, "_build.pdf")
    total, entries = None, None
    for _ in range(5):
        n, new_entries = render(tmp, total, entries)
        if total == n and entries == new_entries:
            entries = new_entries
            break
        total, entries = n, new_entries
    render(OUT_PDF, total, entries)
    add_bookmarks(OUT_PDF, entries)
    print("pages:", total, "| toc:", len(entries))
    print("written:", OUT_PDF)


if __name__ == "__main__":
    main()
