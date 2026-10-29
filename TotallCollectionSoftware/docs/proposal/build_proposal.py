# -*- coding: utf-8 -*-
"""سازندهٔ PDF پروپوزال — قالب راست‌به‌چپ با موتور متن اختصاصی."""
import os

from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (BaseDocTemplate, CondPageBreak, Flowable, Frame,
                                KeepTogether, NextPageTemplate, PageBreak, PageTemplate,
                                Spacer, Table, TableStyle)

import content as C
from rtltext import RT, TocRow, fa_digits, visual, w

HERE = os.path.dirname(os.path.abspath(__file__))
FONT_DIR = "/home/user/TotallCollectionSoftware/TotallCollectionSoftware/src/Inventory.Api/Resources/fonts"
OUT_PDF = os.path.join(HERE, "پروپوزال-فنی-تجاری-فروغ-آریا.pdf")

pdfmetrics.registerFont(TTFont("Vazir", os.path.join(FONT_DIR, "Vazirmatn-Regular.ttf")))
pdfmetrics.registerFont(TTFont("VazirB", os.path.join(FONT_DIR, "Vazirmatn-Bold.ttf")))

NAVY = colors.HexColor("#152238")
NAVY_SOFT = colors.HexColor("#26344F")
RED = colors.HexColor("#B01E2E")
GRAY = colors.HexColor("#5C6474")
GRAY_LIGHT = colors.HexColor("#8A91A0")
INK = "#20293A"
LINE = colors.HexColor("#DCE0E8")
BG_SOFT = colors.HexColor("#F6F7FA")
BG_BLUE = colors.HexColor("#EEF3FB")
BG_WARM = colors.HexColor("#FBF1F2")

PAGE_W, PAGE_H = A4
LM = RM = 18 * mm
TM = 26 * mm
BM = 20 * mm
CONTENT_W = PAGE_W - LM - RM

TOC = []
TOC_NOW = []


# ------------------------------------------------------------------ helpers
def P(text, size=9.4, leading=None, align="justify", color=INK, space_after=0,
      font="Vazir", keep=False):
    return RT(text, font=font, size=size, leading=leading, align=align, color=color,
              space_after=space_after, keep_with_next=keep)


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
        ("GRID", (0, 0), (-1, -1), 0.4, LINE),
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
            RT(title, font="VazirB", size=8.9, leading=14, align="right", color="#152238"),
            Spacer(1, 1),
            RT(desc, size=8.5, leading=13.8, align="justify"),
        ]
        rows.append([body, _CellFlow(fa_digits(i), font="VazirB", size=12, align="center",
                                     color="#B01E2E", leading=16, pad=2)])
    t = Table(rows, colWidths=[CONTENT_W - 28, 28], hAlign="RIGHT")
    t.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("TOPPADDING", (0, 0), (-1, -1), 4.5),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 5.5),
        ("LEFTPADDING", (0, 0), (-1, -1), 2),
        ("RIGHTPADDING", (0, 0), (-1, -1), 2),
        ("LINEBELOW", (0, 0), (-1, -2), 0.4, LINE),
        ("LINEBEFORE", (1, 0), (1, -1), 0.4, LINE),
    ]))
    return t


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


class LatinLine(Flowable):
    """خط لاتین با فاصله‌گذاری دلخواه — به‌صورت یک رشته رسم می‌شود."""

    def __init__(self, text, font="Vazir", size=7.6, leading=11, color="#B01E2E", align="right"):
        Flowable.__init__(self)
        self.text = text
        self.font = font
        self.size = size
        self.leading = leading
        self.color = colors.HexColor(color)
        self.align = align

    def wrap(self, aw, ah):
        self._w = aw
        return aw, self.leading

    def draw(self):
        c = self.canv
        c.saveState()
        c.setFillColor(self.color)
        c.setFont(self.font, self.size)
        y = (self.leading - self.size) / 2 + 1
        x = self._w - w(self.text, self.font, self.size)
        c.drawRightString(self._w, y, self.text)
        c.restoreState()


def callout_box(title, text, tone="info"):
    return CalloutBox(title, text, tone)


def kpi_row(items):
    cells = []
    for val, lbl in items:
        cells.append([
            RT(val, font="VazirB", size=17, leading=23, align="center", color="#B01E2E"),
            Spacer(1, 1),
            RT(lbl, size=7.9, leading=12.5, align="center", color="#5C6474"),
        ])
    n = len(cells)
    w = CONTENT_W / n
    t = Table([list(reversed(cells))], colWidths=[w] * n, hAlign="RIGHT")
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), BG_SOFT),
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
                         color="#152238", keep_with_next=True)
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
        c.setStrokeColor(LINE)
        c.setLineWidth(0.7)
        c.line(0, 0.8, self._w, 0.8)
        n = len(self._lines)
        top = (self._h + n * self.leading) / 2.0 + 1.5
        self._draw_at(c, self._w - 15, top)


# ------------------------------------------------------------------ page deco
def draw_bg_pattern(c):
    c.saveState()
    c.setFillColor(colors.HexColor("#F4F5F7"))
    step = 34
    for i in range(-int(PAGE_H), int(PAGE_W) + int(PAGE_H), step):
        p = c.beginPath()
        p.moveTo(i, PAGE_H)
        p.lineTo(i + 17, PAGE_H)
        p.lineTo(i + 17 + PAGE_H * 0.55, 0)
        p.lineTo(i + PAGE_H * 0.55, 0)
        p.close()
        c.drawPath(p, stroke=0, fill=1)
    c.restoreState()


def on_cover(c, doc):
    c.saveState()
    draw_bg_pattern(c)

    c.setFillColor(NAVY)
    c.rect(0, 0, PAGE_W, 15 * mm, stroke=0, fill=1)
    c.setFillColor(RED)
    c.rect(0, 15 * mm, 58 * mm, 2.4 * mm, stroke=0, fill=1)

    c.setFillColor(NAVY)
    c.setFont("VazirB", 30)
    c.drawRightString(PAGE_W - 20 * mm, PAGE_H - 30 * mm, visual("فروغ آریا"))
    c.setFillColor(RED)
    c.setFont("Vazir", 9)
    c.drawRightString(PAGE_W - 20 * mm, PAGE_H - 36.5 * mm, "F O R O U G H   A R Y A")
    c.setStrokeColor(RED)
    c.setLineWidth(1.1)
    c.line(PAGE_W - 20 * mm - 52 * mm, PAGE_H - 40 * mm, PAGE_W - 20 * mm, PAGE_H - 40 * mm)

    meta = [f"تاریخ : {C.DOC['date_fa']}", f"شماره سند : {C.DOC['doc_no']}",
            f"نسخه : {C.DOC['version']}"]
    y = PAGE_H - 28 * mm
    c.setFont("Vazir", 9)
    for line in meta:
        c.setFillColor(GRAY)
        c.drawRightString(62 * mm, y, visual(line))
        y -= 6 * mm

    c.setFillColor(RED)
    c.setFont("VazirB", 11.5)
    c.drawCentredString(PAGE_W / 2, PAGE_H * 0.635, visual("پروپوزال فنی – تجاری"))
    c.setFillColor(NAVY)
    c.setFont("VazirB", 25)
    c.drawCentredString(PAGE_W / 2, PAGE_H * 0.565, visual("سامانهٔ یکپارچهٔ فروغ آریا"))
    c.setFillColor(GRAY)
    c.setFont("Vazir", 13)
    c.drawCentredString(PAGE_W / 2, PAGE_H * 0.515, visual("معرفی چهار ماژول کاربردی"))
    c.setStrokeColor(RED)
    c.setLineWidth(2.2)
    c.line(PAGE_W / 2 - 42 * mm, PAGE_H * 0.485, PAGE_W / 2 + 42 * mm, PAGE_H * 0.485)
    c.setFillColor(NAVY_SOFT)
    c.setFont("VazirB", 11)
    c.drawCentredString(PAGE_W / 2, PAGE_H * 0.44, visual(" | ".join(C.DOC["modules"])))

    c.setFillColor(colors.white)
    c.setStrokeColor(colors.HexColor("#D8DCE4"))
    c.setLineWidth(0.8)
    c.roundRect(30 * mm, 52 * mm, PAGE_W - 60 * mm, 34 * mm, 4 * mm, stroke=1, fill=1)
    c.setFillColor(RED)
    c.rect(PAGE_W - 30 * mm - 3, 56 * mm, 3, 26 * mm, stroke=0, fill=1)

    c.setFillColor(GRAY_LIGHT)
    c.setFont("Vazir", 8.4)
    c.drawRightString(PAGE_W - 36 * mm, 77 * mm, visual("تهیه‌کننده"))
    c.drawRightString(PAGE_W - 36 * mm, 64 * mm, visual("مخاطب"))
    c.setFillColor(NAVY)
    c.setFont("VazirB", 10.5)
    c.drawRightString(PAGE_W - 62 * mm, 77 * mm, visual(C.DOC["vendor"]))
    c.setFillColor(colors.HexColor("#33405A"))
    c.setFont("Vazir", 9.6)
    c.drawRightString(PAGE_W - 62 * mm, 64 * mm,
                      visual("مدیریت محترم شرکت ................................"))

    c.setFillColor(colors.white)
    c.setFont("Vazir", 7.8)
    c.drawRightString(PAGE_W - 20 * mm, 6 * mm,
                      visual("محرمانه — این سند برای مخاطب تعیین‌شده تهیه شده است"))
    c.drawString(20 * mm, 6 * mm, C.DOC["date_en"])
    c.restoreState()


def on_body(c, doc):
    c.saveState()
    top = PAGE_H - 15 * mm
    c.setFillColor(NAVY)
    c.setFont("VazirB", 8.4)
    c.drawRightString(PAGE_W - LM, top,
                      visual("نرم‌افزار یکپارچه فروغ آریا   |   پروپوزال فنی و تجاری"))
    c.setFillColor(GRAY_LIGHT)
    c.setFont("Vazir", 7.8)
    c.drawString(LM, top, visual(f"{C.DOC['doc_no']}  •  {C.DOC['date_fa']}"))
    c.setStrokeColor(LINE)
    c.setLineWidth(0.6)
    c.line(LM, top - 3.2 * mm, PAGE_W - RM, top - 3.2 * mm)
    c.setFillColor(RED)
    c.rect(PAGE_W - RM - 14 * mm, top - 3.7 * mm, 14 * mm, 1.1 * mm, stroke=0, fill=1)

    c.setStrokeColor(LINE)
    c.setLineWidth(0.5)
    c.line(LM, 13 * mm, PAGE_W - RM, 13 * mm)
    c.setFillColor(GRAY_LIGHT)
    c.setFont("Vazir", 7.8)
    c.drawRightString(PAGE_W - RM, 8.6 * mm, visual("محرمانه — شرکت فروغ آریا"))
    total = getattr(doc, "total_pages", None)
    label = f"صفحهٔ {fa_digits(doc.page)}" + (f" از {fa_digits(total)}" if total else "")
    c.drawCentredString(PAGE_W / 2, 8.6 * mm, visual(label))
    c.restoreState()


def on_price(c, doc):
    c.saveState()
    c.saveState()
    c.translate(0, PAGE_H - 62 * mm)
    c.setFillColor(colors.HexColor("#F3F4F6"))
    for i in range(-6, 30):
        p = c.beginPath()
        p.moveTo(i * 16, 62 * mm)
        p.lineTo(i * 16 + 9, 62 * mm)
        p.lineTo(i * 16 + 9 + 34, 0)
        p.lineTo(i * 16 + 34, 0)
        p.close()
        c.drawPath(p, stroke=0, fill=1)
    c.restoreState()

    c.setFillColor(colors.HexColor("#2B2F36"))
    p = c.beginPath()
    p.moveTo(PAGE_W, 0)
    p.lineTo(PAGE_W, 46 * mm)
    p.lineTo(PAGE_W - 62 * mm, 0)
    p.close()
    c.drawPath(p, stroke=0, fill=1)
    c.setFillColor(RED)
    p = c.beginPath()
    p.moveTo(PAGE_W, 26 * mm)
    p.lineTo(PAGE_W, 46 * mm)
    p.lineTo(PAGE_W - 30 * mm, 26 * mm)
    p.close()
    c.drawPath(p, stroke=0, fill=1)

    c.setFillColor(colors.HexColor("#F1F2F4"))
    p = c.beginPath()
    p.moveTo(0, 0)
    p.lineTo(0, 12 * mm)
    p.lineTo(18 * mm, 0)
    p.close()
    c.drawPath(p, stroke=0, fill=1)
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
            PageTemplate(id="cover", frames=[Frame(0, 0, PAGE_W, PAGE_H, 0, 0, 0, 0, id="cover")],
                         onPage=on_cover),
            PageTemplate(id="body", frames=[Frame(LM, BM, CONTENT_W, PAGE_H - TM - BM, 0, 0, 0, 0,
                                                  id="body")], onPage=on_body),
            PageTemplate(id="price", frames=[Frame(20 * mm, 16 * mm, PAGE_W - 40 * mm,
                                                   PAGE_H - 32 * mm, 0, 0, 0, 0, id="price")],
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
                  color="#152238", space_after=4))
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
                   color="#152238", keep_with_next=True, space_after=2), Spacer(1, 3)]
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


def _height(flows, width=CONTENT_W):
    total = 0.0
    for f in flows:
        try:
            if hasattr(f, "reserve_space"):
                total += f.reserve_space
            else:
                total += f.wrap(width, PAGE_H)[1]
        except Exception:
            total += 20
    return total


FRAME_H = PAGE_H - TM - BM


def body_story():
    """سرِ عنوان‌ها (h2/h3) با بلوک بعدی در یک KeepTogether قرار می‌گیرد تا تنها نماند."""
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


def price_page():
    out = []
    W = PAGE_W - 40 * mm

    brand = [
        RT("فروغ آریا", font="VazirB", size=24, leading=30, align="right", color="#152238"),
        LatinLine("F O R O U G H   A R Y A", size=7.6, leading=11, color="#B01E2E"),
    ]
    metas = [
        RT(f"تاریخ : {C.DOC['date_fa']}", size=8.6, leading=15, color="#5C6474", align="right"),
        RT(f"شماره : {C.DOC['doc_no']}", size=8.6, leading=15, color="#5C6474", align="right"),
        RT("پیوست : ـــــــــــــــــ", size=8.6, leading=15, color="#5C6474", align="right"),
    ]
    head = Table([[metas, brand]], colWidths=[W * 0.55, W * 0.45])
    head.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 0), ("RIGHTPADDING", (0, 0), (-1, -1), 0),
    ]))
    out.append(head)
    out.append(Spacer(1, 24))

    out.append(RT("پیشنهاد قیمت و ارائه ماژول‌های نرم‌افزار", font="VazirB", size=16.5,
                  leading=26, align="center", color="#152238"))
    out.append(Spacer(1, 4))
    rule = Table([[""]], colWidths=[W * 0.42], rowHeights=[2.2], hAlign="CENTER")
    rule.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, -1), RED),
                              ("LEFTPADDING", (0, 0), (-1, -1), 0),
                              ("RIGHTPADDING", (0, 0), (-1, -1), 0)]))
    out.append(rule)
    out.append(Spacer(1, 20))

    out.append(RT("مدیریت محترم شرکت ................................", font="VazirB",
                  size=10.5, leading=18, align="right", color="#152238"))
    out.append(Spacer(1, 10))
    out.append(RT("با سلام و احترام،", size=9.4, leading=16, align="right"))
    out.append(Spacer(1, 3))
    out.append(P("پیرو درخواست آن شرکت محترم، پیشنهاد قیمت ماژول‌های مورد نیاز به شرح زیر تقدیم "
                 "می‌گردد. دامنهٔ خدمات شامل ارائهٔ نرم‌افزار، راه‌اندازی اولیه و آموزش کاربران است.",
                 space_after=12))

    desc = {
        "اطلاعات پایه": "تعریف شرکت‌ها و شعب، ساختار سازمانی و واحدهای شمارهٔ نامه، کاربران، "
                        "نقش‌ها و سطوح دسترسی، ساختار شمارهٔ نامه، داده‌های پایهٔ کالا و انبار و "
                        "طرف حساب، تنظیمات سامانه و لاگ عملیات",
        "اتوماسیون اداری": "دبیرخانهٔ الکترونیکی، نامه‌های داخلی و وارده و صادره، ارجاع و گردش "
                          "مکاتبات، امضای الکترونیکی و ثبت دبیرخانه، بایگانی اداری، ایمیل سازمانی "
                          "و صورت‌جلسه",
        "دستور کار": "ثبت، ارجاع و پیگیری دستورها، مهلت و اولویت، تکرار و تقویم، چک‌لیست و "
                     "زیردستور، پروژه‌های کاری و گزارش عملکرد افراد",
        "آرشیو اسناد و مدارک": "بایگانی درختی، دسترسی نفر‌به‌نفر با چهار سطح، نسخه‌گذاری و گردش "
                              "تأیید، جستجو و OCR و جستجوی معنایی، چاپ واترمارک‌دار و ورود انبوه",
    }
    hdr = ["ردیف", "ماژول", "شرح خدمات", "مبلغ پیشنهادی (ریال)"]
    rows = [[_CellFlow(h, font="VazirB", size=8.6, leading=14, align="center",
                       color=colors.white, pad=6) for h in reversed(hdr)]]
    for i, m in enumerate(C.DOC["modules"], 1):
        rows.append([
            _CellFlow("..................", size=9, leading=15, align="center", pad=6),
            _CellFlow(desc[m], size=8.2, leading=13.4, align="justify", pad=6),
            _CellFlow(m, font="VazirB", size=8.9, leading=15, align="right", pad=6),
            _CellFlow(fa_digits(i), size=9, leading=15, align="center", pad=6),
        ])
    t = Table(rows, colWidths=[W * 0.23, W * 0.50, W * 0.18, W * 0.09], hAlign="RIGHT")
    style = [
        ("BACKGROUND", (0, 0), (-1, 0), NAVY),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("GRID", (0, 0), (-1, -1), 0.5, colors.HexColor("#C9CED8")),
    ]
    for i in range(1, len(rows)):
        if i % 2 == 0:
            style.append(("BACKGROUND", (0, i), (-1, i), BG_SOFT))
    t.setStyle(TableStyle(style))
    out.append(t)

    total = Table([[
        _CellFlow("..................", font="VazirB", size=9.4, leading=15, align="center", pad=6),
        _CellFlow("جمع کل پیشنهادی", font="VazirB", size=9.6, leading=15, align="right", pad=6),
    ]], colWidths=[W * 0.23, W * 0.77], hAlign="RIGHT")
    total.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), BG_WARM),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("GRID", (0, 0), (-1, -1), 0.5, colors.HexColor("#C9CED8")),
        ("TOPPADDING", (0, 0), (-1, -1), 4), ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
    ]))
    out.append(total)
    out.append(Spacer(1, 7))
    out.append(RT("مبالغ این جدول توسط واحد بازرگانی تکمیل و پیش از ارسال نهایی تأیید می‌شود.",
                  size=7.6, leading=12, color="#8A91A0", align="right"))
    out.append(Spacer(1, 14))

    out.append(RT("جمع کل به حروف : ..............................................................",
                  size=9.2, leading=16, color="#5C6474", align="right"))
    out.append(Spacer(1, 16))

    validity = Table([[[
        RT("اعتبار پیشنهاد:", font="VazirB", size=9, leading=15, align="right", color="#152238"),
        Spacer(1, 2),
        RT("این پیشنهاد از تاریخ درج، به مدت ........ روز اعتبار دارد. مبالغ فوق به ریال و بر "
           "اساس دامنهٔ خدمات مندرج در این سند است.", size=8.4, leading=14.4, align="justify"),
    ]]], colWidths=[W])
    validity.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), colors.HexColor("#F7F8FA")),
        ("BOX", (0, 0), (-1, -1), 0.6, colors.HexColor("#DDE1E9")),
        ("LEFTPADDING", (0, 0), (-1, -1), 12), ("RIGHTPADDING", (0, 0), (-1, -1), 12),
        ("TOPPADDING", (0, 0), (-1, -1), 10), ("BOTTOMPADDING", (0, 0), (-1, -1), 10),
    ]))
    out.append(validity)
    out.append(Spacer(1, 26))

    sign = Table([[
        "",
        [RT("با احترام،", size=9.6, leading=17, color="#5C6474", align="right"),
         RT("مدیرعامل شرکت فروغ آریا", font="VazirB", size=10, leading=19,
            color="#152238", align="right"),
         Spacer(1, 26),
         RT("محل مهر و امضا", size=8.2, leading=13, color="#8A91A0", align="right")]
    ]], colWidths=[W * 0.45, W * 0.55])
    sign.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "BOTTOM"),
        ("LEFTPADDING", (0, 0), (-1, -1), 0), ("RIGHTPADDING", (0, 0), (-1, -1), 0),
    ]))
    out.append(sign)
    return out


def render(path, total_pages=None, toc_entries=None):
    global TOC, TOC_NOW
    TOC = toc_entries or []
    TOC_NOW = []
    doc = Doc(path)
    doc.total_pages = total_pages
    story = []
    story.append(NextPageTemplate("body"))
    story.append(PageBreak())
    story.extend(id_page())
    story.extend(summary_page())
    story.extend(body_story())
    story.append(NextPageTemplate("price"))
    story.append(PageBreak())
    story.extend(price_page())
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
