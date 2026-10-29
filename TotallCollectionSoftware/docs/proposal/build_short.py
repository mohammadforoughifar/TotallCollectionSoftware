# -*- coding: utf-8 -*-
"""نسخهٔ مدیریتی (۵ صفحه) — راهکار یکپارچهٔ اداری، روی سربرگ رسمی فروغ آریا."""
import os

from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import Flowable, Table, TableStyle

from rtltext import RT, fa_digits, visual, w

HERE = os.path.dirname(os.path.abspath(__file__))
FONT_DIR = "/home/user/TotallCollectionSoftware/TotallCollectionSoftware/src/Inventory.Api/Resources/fonts"
LETTERHEAD = os.path.join(HERE, "letterhead.jpg")
OUT_PDF = os.path.join(HERE, "پروپوزال-فروغ-آریا-۵صفحه.pdf")

pdfmetrics.registerFont(TTFont("Vazir", os.path.join(FONT_DIR, "Vazirmatn-Regular.ttf")))
pdfmetrics.registerFont(TTFont("VazirB", os.path.join(FONT_DIR, "Vazirmatn-Bold.ttf")))

NAVY = colors.HexColor("#1F2937")
NAVY_SOFT = colors.HexColor("#334155")
RED = colors.HexColor("#B91C32")
GRAY = colors.HexColor("#5B6472")
GRAY_LIGHT = colors.HexColor("#7C8698")
INK = "#20293A"
LINE = colors.HexColor("#CBD5E1")
BG_SOFT = colors.HexColor("#F4F5F7")
BG_BLUE = colors.HexColor("#F1F5F9")
BG_WARM = colors.HexColor("#FEF2F3")

PAGE_W, PAGE_H = A4
LM, RM = 52.0, 56.0
CONTENT_W = PAGE_W - LM - RM
TOP = PAGE_H - 178          # آغاز ناحیهٔ متن (زیر سربرگ)
BOTTOM = 152                # پایان ناحیهٔ متن (بالای مثلث‌های سربرگ)

META_X = 83.7
META_Y0 = 803.5
META_STEP = 26.6

DOC = {
    "date_fa": "۱۴۰۵/۰۸/۰۷",
    "doc_no": "۱۴۰۵/۰۸-۰۱-پ",
    "attachment": "ندارد",
    "vendor": "شرکت فروغ آریا",
}


# ------------------------------------------------------------------ primitives
def put(c, rt, width, x, y_top):
    rt.wrap(width, 10000)
    rt.drawOn(c, x, y_top - rt._h)
    return y_top - rt._h


def para(c, y, text, size=9.2, leading=None, font="Vazir", color=INK, align="justify",
         width=None, x=None):
    width = CONTENT_W if width is None else width
    x = LM if x is None else x
    rt = RT(text, font=font, size=size, leading=leading or size * 1.70, color=color, align=align)
    return put(c, rt, width, x, y)


def draw_letterhead(c):
    c.drawImage(ImageReader(LETTERHEAD), 0, 0, width=PAGE_W, height=PAGE_H,
                preserveAspectRatio=False, mask=None)


def header_fields(c):
    texts = [visual(f"تاریخ : {DOC['date_fa']}"),
             visual(f"شماره : {DOC['doc_no']}"),
             visual(f"پیوست : {DOC['attachment']}")]
    c.setFont("Vazir", 8.3)
    widest = max(w(t, "Vazir", 8.3) for t in texts)
    x_right = max(META_X, widest + 10)
    y = META_Y0
    for t in texts:
        c.setFillColor(colors.HexColor("#333B4A"))
        c.setFont("Vazir", 8.3)
        c.drawRightString(x_right, y, t)
        y -= META_STEP


def footer(c, page, total=5):
    c.setFont("Vazir", 7.6)
    c.setFillColor(GRAY_LIGHT)
    c.drawString(LM, 46, visual("شرکت فروغ آریا — محرمانه"))
    c.drawString(LM, 33, visual(f"صفحهٔ {fa_digits(page)} از {fa_digits(total)}"))


def footer_note(c, text):
    c.setFont("Vazir", 7.6)
    c.setFillColor(GRAY_LIGHT)
    c.drawString(LM, 46, visual(text))


def band(c, y, text, h=25.0, sub=None):
    """نوار عنوان بخش، با لبهٔ قرمز در سمت راست."""
    c.setFillColor(NAVY)
    c.rect(LM, y - h, CONTENT_W, h, stroke=0, fill=1)
    c.setFillColor(RED)
    c.rect(LM + CONTENT_W - 4, y - h, 4, h, stroke=0, fill=1)
    c.setFillColor(colors.white)
    c.setFont("VazirB", 10.6)
    c.drawRightString(LM + CONTENT_W - 14, y - h + 8.2, visual(text))
    return y - h


def table(c, y, headers, rows, widths, size=8.3, leading=13.2, head_bg=NAVY, zebra=True,
          first_col_bold=False):
    """جدول راست‌به‌چپ با ستون اولِ منطقی در سمت راست."""
    from build_short import _Cell   # noqa: F401  (self-reference برای وضوح)

    rev_h = list(reversed(headers))
    rev_rows = [list(reversed(r)) for r in rows]
    rev_w = list(reversed(widths))
    total = sum(rev_w)

    data = [[_Cell(h, font="VazirB", size=size, leading=leading, align="center",
                   color=colors.white, pad=5) for h in rev_h]]
    for r in rev_rows:
        data.append([_Cell(t, size=size, leading=leading, align="right", pad=5,
                           font="VazirB" if (first_col_bold and idx == len(r) - 1) else "Vazir")
                     for idx, t in enumerate(r)])
    t = Table(data, colWidths=[CONTENT_W * x / total for x in rev_w], hAlign="LEFT")
    style = [("BACKGROUND", (0, 0), (-1, 0), head_bg),
             ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
             ("GRID", (0, 0), (-1, -1), 0.5, LINE),
             ("TOPPADDING", (0, 0), (-1, -1), 4),
             ("BOTTOMPADDING", (0, 0), (-1, -1), 4)]
    if zebra:
        for i in range(1, len(data)):
            if i % 2 == 1:
                style.append(("BACKGROUND", (0, i), (-1, i), BG_SOFT))
    t.setStyle(TableStyle(style))
    t.wrapOn(c, CONTENT_W, 10000)
    t.drawOn(c, LM, y - t._height)
    return y - t._height


class _Cell(RT):
    def __init__(self, text, **kw):
        self._pad = kw.pop("pad", 5)
        super().__init__(text, **kw)

    def wrap(self, aw, ah):
        avail = max(18.0, aw - 2 * self._pad)
        self._layout(avail)
        self._w = avail
        self._h = max(1, len(self._lines)) * self.leading
        return aw, self._h + 0.1


class Box(Flowable):
    """جعبهٔ نکته با نوار رنگی سمت راست."""

    def __init__(self, title, text, tone="info", width=CONTENT_W):
        Flowable.__init__(self)
        self.bar = NAVY_SOFT if tone == "info" else RED
        self.bg = BG_BLUE if tone == "info" else BG_WARM
        self.title = title
        self.width = width
        self.t = RT(title, font="VazirB", size=8.8, leading=13.6, align="right",
                    color="#1F2937") if title else None
        self.x = RT(text, size=8.3, leading=13.4, align="justify")

    def wrap(self, aw, ah):
        inner = self.width - 22
        self._w = self.width
        h = 0
        if self.t:
            self.t.wrap(inner, 1000)
            h += self.t._h + 3
        self.x.wrap(inner, 1000)
        h += self.x._h + 12
        self._h = h
        return self.width, h

    def draw(self):
        c = self.canv
        c.setFillColor(self.bg)
        c.rect(0, 0, self._w, self._h, stroke=0, fill=1)
        c.setFillColor(self.bar)
        c.rect(self._w - 3.5, 0, 3.5, self._h, stroke=0, fill=1)
        y = self._h - 7
        if self.t:
            self.t.drawOn(c, 10, y - self.t._h)
            y -= self.t._h + 3
        self.x.drawOn(c, 10, y - self.x._h)


def box(c, y, title, text, tone="info"):
    b = Box(title, text, tone)
    b.wrapOn(c, CONTENT_W, 10000)
    b.drawOn(c, LM, y - b._h)
    return y - b._h


def kpi_strip(c, y, items, height=44.0):
    n = len(items)
    wc = CONTENT_W / n
    for i, (val, lbl) in enumerate(items):
        x = LM + (n - 1 - i) * wc
        c.setFillColor(BG_BLUE)
        c.rect(x + 1.5, y - height, wc - 3, height, stroke=0, fill=1)
        c.setFillColor(RED)
        c.setFont("VazirB", 13.5)
        c.drawCentredString(x + wc / 2, y - 22, visual(val))
        c.setFillColor(GRAY)
        c.setFont("Vazir", 7.4)
        c.drawCentredString(x + wc / 2, y - 35, visual(lbl))
    return y - height


# ------------------------------------------------------------------ page 1
def page_cover(c):
    """جلد: عنوان سند، فهرست چهار ماژول با یک خط توضیح، و جملهٔ تعهد."""
    draw_letterhead(c)
    header_fields(c)

    y = TOP - 160
    c.setFillColor(NAVY)
    c.setFont("VazirB", 22)
    c.drawCentredString(PAGE_W / 2, y, visual("معرفی چهار ماژول نرم‌افزاری"))

    c.setStrokeColor(RED)
    c.setLineWidth(2.2)
    c.line(PAGE_W / 2 - 92, y - 12, PAGE_W / 2 + 92, y - 12)

    c.setFillColor(GRAY)
    c.setFont("Vazir", 10.6)
    c.drawCentredString(PAGE_W / 2, y - 32,
                        visual("مکاتبات، دستور کار و اسناد سازمان در یک سامانه"))

    # برچسب بخش
    c.setFillColor(GRAY_LIGHT)
    c.setFont("Vazir", 8.2)
    c.drawRightString(LM + CONTENT_W, y - 66, visual("ماژول‌های پیشنهادی"))
    lbl_w = w(visual("ماژول‌های پیشنهادی"), "Vazir", 8.2)
    c.setStrokeColor(colors.HexColor("#E3E7ED"))
    c.setLineWidth(0.6)
    c.line(LM, y - 64, LM + CONTENT_W - lbl_w - 12, y - 64)

    # چهار ماژول با یک خط توضیح
    mods = [
        ("اطلاعات پایه", "سازمان، کاربران، نقش‌ها و دسترسی‌ها"),
        ("اتوماسیون اداری", "نامهٔ داخلی، وارده و صادره، دبیرخانه"),
        ("دستور کار", "ارجاع، مهلت، پیگیری و گزارش عملکرد"),
        ("آرشیو اسناد و مدارک", "بایگانی، نسخه‌گذاری، تأیید و جستجو"),
    ]
    y -= 78
    gap = 9.0
    bw = (CONTENT_W - 3 * gap) / 4
    bh = 86.0
    for i, (name, desc) in enumerate(mods):
        x = LM + (3 - i) * (bw + gap)
        c.setFillColor(colors.white)
        c.setStrokeColor(colors.HexColor("#DCE1E8"))
        c.setLineWidth(0.8)
        c.roundRect(x, y - bh, bw, bh, 2.5, stroke=1, fill=1)
        c.setFillColor(RED)
        c.rect(x + bw - 2.6, y - bh + 8, 2.6, bh - 16, stroke=0, fill=1)

        c.setFillColor(NAVY)
        c.setFont("VazirB", 9.0)
        c.drawCentredString(x + bw / 2 - 1.5, y - 23, visual(name))

        c.setStrokeColor(colors.HexColor("#EDEFF3"))
        c.setLineWidth(0.6)
        c.line(x + 10, y - 31, x + bw - 12, y - 31)

        rt = RT(desc, size=7.4, leading=11.4, align="center", color="#5B6472")
        rt.wrap(bw - 18, 100)
        rt.drawOn(c, x + 9, y - 34 - rt._h)

    # جملهٔ تعهد
    y -= bh + 56
    c.setFillColor(NAVY_SOFT)
    c.setFont("Vazir", 10.2)
    c.drawCentredString(PAGE_W / 2, y, visual("یک سامانه، یک پایگاه داده و یک مدل دسترسی"))
    y -= 19
    c.setFillColor(GRAY)
    c.setFont("Vazir", 9.4)
    c.drawCentredString(PAGE_W / 2, y,
                        visual("برای گردش مکاتبات، پیگیری تعهدات و نگهداری امن مدارک"))

    # فهرست سند
    y -= 30
    c.setFillColor(GRAY_LIGHT)
    c.setFont("Vazir", 7.9)
    c.drawRightString(LM + CONTENT_W, y, visual("در این سند"))
    y -= 15
    items = [("خلاصهٔ مدیریتی و تصمیم‌های مورد انتظار", "۲"),
             ("معرفی چهار ماژول، گردش کار و دسترسی‌ها", "۳"),
             ("اجرا، امنیت و دامنهٔ تحویل", "۴"),
             ("پیشنهاد قیمت و شرایط پرداخت", "۵")]
    for title, page in items:
        c.setFillColor("#3A4353")
        c.setFont("Vazir", 8.4)
        c.drawRightString(LM + CONTENT_W, y, visual(title))
        c.setFillColor(GRAY_LIGHT)
        c.setFont("Vazir", 8.0)
        c.drawString(LM, y, page)
        tw = w(visual(title), "Vazir", 8.4)
        c.setStrokeColor(colors.HexColor("#DDE2E9"))
        c.setLineWidth(0.5)
        c.setDash(0.8, 2.2)
        c.line(LM + 16, y + 2.4, LM + CONTENT_W - tw - 8, y + 2.4)
        c.setDash()
        y -= 15

    # خط پایانی
    y -= 74
    c.setStrokeColor(colors.HexColor("#E1E5EB"))
    c.setLineWidth(0.7)
    c.line(LM + CONTENT_W * 0.18, y, LM + CONTENT_W * 0.82, y)
    c.setFillColor(GRAY_LIGHT)
    c.setFont("Vazir", 8.6)
    c.drawCentredString(PAGE_W / 2, y - 16,
                        visual(f"{DOC['vendor']}  •  {DOC['date_fa']}"))

    footer(c, 1)


# ------------------------------------------------------------------ page 2
def page_summary(c):
    draw_letterhead(c)
    header_fields(c)
    y = band(c, TOP, "خلاصهٔ مدیریتی")
    y -= 14

    y = para(c, y, "سازمان امروز چهار گلوگاه تکراری دارد: داده‌های پایه در فایل‌های پراکنده، گردش "
                   "کاغذی نامه‌ها، دستورهای کاری که پیگیری نمی‌شوند و مدارکی که در زمان نیاز پیدا "
                   "نمی‌شوند. این چهار ماژول همان چهار گلوگاه را هدف می‌گیرند — نه بیشتر و نه "
                   "چیز دیگری. همه روی یک پایگاه داده و یک مدل دسترسی کار می‌کنند؛ بنابراین گزارش "
                   "میان‌ماژولی و ارجاع متقابل نامه و کار، بدون یکپارچه‌سازی اضافی در دسترس است.")
    y -= 13

    y = table(c, y, ["ماژول", "امروز", "در سامانه"], [
        ["اطلاعات پایه", "دادهٔ سازمان، کاربران و دسترسی‌ها در فایل‌های جدا",
         "یک منبع واحد از سازمان، نقش‌ها، ساختار شمارهٔ نامه و تنظیمات"],
        ["اتوماسیون اداری", "گردش کاغذی، پیگیری تلفنی، امضای دستی",
         "نامهٔ داخلی، وارده و صادره با ارجاع، مهلت، پیش‌نویس و امضای الکترونیکی"],
        ["دستور کار", "دستور شفاهی و پیگیری نشده",
         "ثبت، ارجاع، اولویت، مهلت، چک‌لیست، تکرار و گزارش عملکرد افراد"],
        ["آرشیو اسناد", "مدرک پراکنده و خطر افشا",
         "بایگانی درختی، دسترسی نفر‌به‌نفر، نسخه‌گذاری، گردش تأیید و جستجو"],
    ], widths=[0.18, 0.34, 0.48], size=8.1, leading=12.6, first_col_bold=True)
    y -= 14

    y = box(c, y, "یک سامانه، یک منبع داده، یک مدل دسترسی",
            "دسترسی بر پایهٔ نقش تعریف می‌شود: هر نقش دقیقاً همان کارهایی را می‌بیند که باید؛ "
            "هم در فهرست‌ها و هم در خود رکورد اعمال می‌شود و هر عملیات حساس در سابقهٔ سامانه "
            "ثبت می‌ماند.")
    y -= 12

    y = para(c, y, "این چهار ماژول بخشی از یک محصول آزموده‌شده‌اند که امروز در بهره‌برداری "
                   "است؛ بنابراین پروژه با نمونهٔ اولیه شروع نمی‌شود.")
    y -= 4
    y = para(c, y, "چرا این چهار ماژول، به این ترتیب", font="VazirB", size=9.4, color=NAVY,
             align="right")
    y -= 4
    for q in [
        "اطلاعات پایه اول می‌آید، چون بدون سازمان، کاربر و نقش، هیچ ماژول دیگری قابل راه‌اندازی نیست.",
        "اتوماسیون اداری بیشترین حجم کار روزمرهٔ سازمان را می‌گیرد و بیشترین صرفه‌جویی را دارد.",
        "دستور کار، خروجی نامه‌ها را به تعهد قابل پیگیری تبدیل می‌کند و عملکرد افراد را سنجش‌پذیر می‌سازد.",
        "آرشیو اسناد، حافظهٔ سازمان است؛ بدون آن، مکاتبات و دستورها در فایل‌های پراکنده گم می‌شوند.",
    ]:
        y = para(c, y, "•  " + q, size=8.8, leading=14.6, color=INK)
        y -= 2
    y -= 10
    y = box(c, y, "سه تصمیم مورد انتظار از مدیریت",
            "۱) تأیید دامنهٔ چهار ماژول  ۲) تأیید ترتیب و زمان‌بندی فازها  ۳) تأیید مبلغ و شرایط "
            "پرداخت (صفحهٔ ۵). پس از این سه مورد، برنامهٔ اجرایی تفصیلی تقدیم می‌شود.")
    footer(c, 2)


# ------------------------------------------------------------------ page 3
def page_modules(c):
    draw_letterhead(c)
    header_fields(c)
    y = band(c, TOP, "چهار ماژول در یک نگاه")
    y -= 14

    y = table(c, y, ["ماژول", "قابلیت‌های کلیدی", "ارزش برای مدیریت"], [
        ["اطلاعات پایه",
         "سازمان، شرکت‌ها و واحدها · کاربران و نقش‌ها · ساختار شمارهٔ نامه و سربرگ · "
         "تنظیمات سامانه و لاگ عملیات",
         "راه‌اندازی سریع و امنیت قابل حسابرسی"],
        ["اتوماسیون اداری",
         "نامهٔ داخلی، وارده و صادره · ارجاع با مهلت و رویت · پیش‌نویس و امضا · "
         "دبیرخانه، بایگانی اداری و صورت‌جلسه",
         "شفافیت گردش مکاتبات و پاسخ‌گویی مهلت‌دار"],
        ["دستور کار",
         "ثبت، ارجاع و اولویت · مهلت، یادآور و تکرار · چک‌لیست و زیردستور · "
         "پروژهٔ کاری و گزارش عملکرد افراد",
         "تعهدات کاری قابل پیگیری و سنجش"],
        ["آرشیو اسناد و مدارک",
         "پوشه‌بندی درختی و دسترسی چهارسطحی · نسخه‌گذاری و گردش تأیید · "
         "OCR، جستجوی معنایی و ورود انبوه · چاپ واترمارک‌دار",
         "پیدا شدن سریع مدرک و جلوگیری از افشا"],
    ], widths=[0.19, 0.53, 0.28], size=8.0, leading=12.8, first_col_bold=True)
    y -= 14

    y = para(c, y, "گردش نامه، نمونه‌ای از کار روزمره", font="VazirB", size=9.4, color=NAVY,
             align="right")
    y -= 4
    y = para(c, y, "ثبت نامه ← انتخاب گیرندگان و سطح محرمانگی ← ارسال و اعلان ← ارجاع با مهلت ← "
                   "رویت و پاسخ ← تصمیم و امضا ← ثبت دبیرخانه (شمارهٔ ثبت، روش ارسال، کد "
                   "پیگیری) ← بایگانی. هر گام زمان، انجام‌دهنده و سابقهٔ خود را نگه می‌دارد.")
    y -= 12

    y = table(c, y, ["نقش", "دسترسی نمونه"], [
        ["مدیر سامانه", "تعریف کاربر، نقش و دسترسی؛ مشاهدهٔ لاگ عملیات"],
        ["مدیر / دبیرخانه", "تمام نامه‌ها، امضا، ثبت دبیرخانه و بایگانی"],
        ["کارشناس", "نامه‌های مرتبط با خود، ارجاع، پیش‌نویس و پاسخ"],
        ["مدیر آرشیو", "دسترسی کامل به پوشه‌ها، گردش تأیید و مجوز دانلود"],
    ], widths=[0.22, 0.78], size=8.2, leading=12.8, first_col_bold=True)
    y -= 12

    y = para(c, y, "ارتباط با سامانه‌های دیگر", font="VazirB", size=9.4, color=NAVY,
             align="right")
    y -= 4
    y = para(c, y, "ارسال نامه با ایمیل سازمانی · اعلان زنده و اعلان مرورگر (Web Push) · "
                   "پیام‌رسان بله/ایتا و پیامک برای دستور کار · خروجی Excel و PDF از گزارش‌ها و "
                   "فهرست‌ها · نسخهٔ نصب‌شدنی روی موبایل و دسکتاپ (PWA) با صف آفلاین آرشیو.")
    y -= 10

    y = box(c, y, "مسیر کاربر در یک روز کاری",
            "صبح: کارتابل، نامه‌های ارجاع‌شده با مهلت و دستورهای امروز. در طول روز: پاسخ و ارجاع، "
            "ثبت مدرک در پوشهٔ مربوط و ثبت دستور برای همکار. پایان روز: گزارش عملکرد و "
            "نامه‌های در انتظار امضا. همه از یک صفحهٔ ورود و با همان سطح دسترسی.")
    footer(c, 3)


# ------------------------------------------------------------------ page 4
def page_delivery(c):
    draw_letterhead(c)
    header_fields(c)
    y = band(c, TOP, "اجرا، امنیت و دامنهٔ تحویل")
    y -= 10

    y = table(c, y, ["فاز", "خروجی"], [
        ["۱ — زیرساخت", "استقرار، پایگاه داده، اطلاعات پایه، نقش‌ها و ساختار شمارهٔ نامه"],
        ["۲ — اتوماسیون اداری", "گردش نامهٔ داخلی، وارده و صادره، دبیرخانه، بایگانی اداری"],
        ["۳ — دستور کار", "دستورها، چک‌لیست، تکرار، پروژه و گزارش عملکرد"],
        ["۴ — آرشیو اسناد", "پوشه‌بندی، سطوح دسترسی، نسخه‌گذاری، گردش تأیید و جستجو"],
    ], widths=[0.24, 0.76], size=8.1, leading=12.2, first_col_bold=True)
    y -= 9

    y = table(c, y, ["امنیت و کنترل دسترسی", "توضیح"], [
        ["احراز هویت", "ورود با نام کاربری و رمز؛ اعتبارسنجی نشست در هر درخواست"],
        ["دسترسی نقش‌محور", "هر مجوز به نقش تخصیص می‌یابد و پیش از اجرا در سرور بررسی می‌شود"],
        ["لاگ عملیات", "ثبت کاربر، زمان، عملیات، مسیر و نتیجه؛ پوشاندن مقادیر حساس"],
        ["دسترسی مدرک", "چهار سطح پوشه با زنجیرهٔ ارثبری، مجوز دانلود جدا و واترمارک چاپ"],
    ], widths=[0.24, 0.76], size=8.0, leading=12.4, first_col_bold=True)
    y -= 9

    y = box(c, y, "پیش از قرارداد، این موارد نیازمند تصمیم کارفرماست",
            "۱) بررسی «فقط مدیر» روی سرویس‌های مدیریت نقش و مجوز  ۲) مجوز خواندن فهرست واحدهای "
            "سازمانی که در فهرست مجوزها تعریف نشده  ۳) فعال‌سازی پیوند مدرک به رکوردهای سایر "
            "ماژول‌ها  ۴) سطح اعتبارسنجی امضای بیومتریک. تسویه و دامنهٔ دقیق این موارد در "
            "جلسهٔ فنی نهایی می‌شود.", tone="warn")
    y -= 9

    y = para(c, y, "دامنهٔ تحویل: استقرار، تعریف داده‌های پایه، آموزش کاربران کلیدی و رفع اشکال "
                   "دورهٔ راه‌اندازی؛ توسعهٔ سفارشی جداگانه قیمت‌گذاری می‌شود.", size=8.8)
    y -= 9

    y = table(c, y, ["آموزش و پشتیبانی", "شرح"], [
        ["مدیر سامانه", "تعریف سازمان، کاربر، نقش و دسترسی؛ ساختار شمارهٔ نامه"],
        ["دبیرخانه", "ثبت و ارسال نامه، ارجاع، امضا، ثبت دبیرخانه و بایگانی"],
        ["کاربران", "کارتابل، پیش‌نویس، دستور کار و جستجوی مدارک"],
        ["پشتیبانی راه‌اندازی", "رفع اشکال و همراهی در دورهٔ استقرار"],
    ], widths=[0.26, 0.74], size=8.2, leading=12.4, first_col_bold=True)
    y -= 10

    y = box(c, y, "نتیجهٔ مورد انتظار در پایان فازها",
            "مکاتبات بدون کاغذ و با سابقهٔ کامل گردش، هر دستور کار با یک مسئول و یک مهلت، و هر "
            "مدرک در چند ثانیه با سطح دسترسی درست — همه در یک سامانه و با یک ورود.")
    footer(c, 4)


# ------------------------------------------------------------------ page 5
def page_price(c):
    draw_letterhead(c)
    header_fields(c)

    y = TOP - 10
    c.setFillColor(NAVY)
    c.setFont("VazirB", 15)
    c.drawCentredString(PAGE_W / 2, y, visual("پیشنهاد قیمت"))
    c.setStrokeColor(RED)
    c.setLineWidth(1.6)
    c.line(PAGE_W / 2 - 62, y - 8, PAGE_W / 2 + 62, y - 8)
    y -= 30

    y = para(c, y, "مدیرعامل محترم شرکت ................................", size=9.6, font="VazirB",
             color=NAVY, align="right")
    y -= 3
    y = para(c, y, "با سلام و احترام، دامنهٔ خدمات شامل ارائهٔ نرم‌افزار، راه‌اندازی اولیه و آموزش "
                   "کاربران است. مبالغ به ریال و برای هر ماژول جداگانه اعلام شده است.")
    y -= 12

    rows = [
        ["۱", "اطلاعات پایه",
         "سازمان و شرکت‌ها، کاربران و نقش‌ها، ساختار شمارهٔ نامه، تنظیمات و لاگ عملیات",
         "۳۰۰٬۰۰۰٬۰۰۰"],
        ["۲", "اتوماسیون اداری", "نامهٔ داخلی، وارده و صادره، ارجاع، امضا و دبیرخانه",
         "۱٬۰۰۰٬۰۰۰٬۰۰۰"],
        ["۳", "دستور کار", "ثبت، ارجاع، مهلت، چک‌لیست، پروژه و گزارش عملکرد",
         "۴۵۰٬۰۰۰٬۰۰۰"],
        ["۴", "آرشیو اسناد و مدارک", "بایگانی، دسترسی، نسخه‌گذاری، گردش تأیید و جستجو",
         "۲٬۵۰۰٬۰۰۰٬۰۰۰"],
    ]
    rev = [list(reversed(r)) for r in rows]

    from build_short import _Cell as CellFlow
    data = [[CellFlow(h, font="VazirB", size=8.0, leading=12.5, align="center",
                      color=colors.white, pad=5)
             for h in reversed(["ردیف", "ماژول", "شرح خدمات", "مبلغ (ریال)"])]]
    for r in rev:
        data.append([
            CellFlow(r[0], font="VazirB", size=8.6, leading=13.5, align="center", pad=5),
            CellFlow(r[1], size=7.9, leading=12.4, align="justify", pad=5),
            CellFlow(r[2], font="VazirB", size=8.3, leading=12.6, align="right", pad=5),
            CellFlow(r[3], size=8.0, leading=12.5, align="center", pad=5),
        ])
    widths = [CONTENT_W * 0.16, CONTENT_W * 0.44, CONTENT_W * 0.22, CONTENT_W * 0.18]
    t = Table(data, colWidths=widths, hAlign="LEFT")
    style = [("BACKGROUND", (0, 0), (-1, 0), NAVY),
             ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
             ("GRID", (0, 0), (-1, -1), 0.5, LINE),
             ("BACKGROUND", (0, 2), (-1, 2), BG_SOFT),
             ("BACKGROUND", (0, 4), (-1, 4), BG_SOFT),
             ("TOPPADDING", (0, 0), (-1, -1), 5), ("BOTTOMPADDING", (0, 0), (-1, -1), 5)]
    t.setStyle(TableStyle(style))
    t.wrapOn(c, CONTENT_W, 10000)
    t.drawOn(c, LM, y - t._height)
    y -= t._height + 5

    y = para(c, y, "مبلغ ماژول اطلاعات پایه با اعمال ۳۰٪ تخفیف ۳۰ میلیون تومان (۳۰۰ میلیون ریال) "
                   "اعلام شده است؛ سایر مبالغ بر اساس تعرفهٔ جاری ماژول‌ها است.",
             size=7.6, color=GRAY, align="right")
    y -= 8

    total = Table([[CellFlow("۴٬۲۵۰٬۰۰۰٬۰۰۰", font="VazirB", size=8.8, leading=14,
                             align="center", pad=5),
                    CellFlow("جمع کل پیشنهادی (ریال)", font="VazirB", size=9.0, leading=14,
                             align="right", pad=8)]],
                  colWidths=[CONTENT_W * 0.18, CONTENT_W * 0.82], hAlign="LEFT")
    total.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, -1), BG_WARM),
                               ("GRID", (0, 0), (-1, -1), 0.5, LINE),
                               ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
                               ("TOPPADDING", (0, 0), (-1, -1), 5),
                               ("BOTTOMPADDING", (0, 0), (-1, -1), 5)]))
    total.wrapOn(c, CONTENT_W, 1000)
    total.drawOn(c, LM, y - total._height)
    y -= total._height + 8

    y = para(c, y, "جمع کل به حروف : چهار میلیارد و دویست و پنجاه میلیون ریال",
             size=8.6, color=NAVY_SOFT, align="right")
    y -= 14

    y = box(c, y, "شرایط پیشنهاد",
            "اعتبار این پیشنهاد ........ روز از تاریخ درج است. پرداخت در سه مرحله (شروع، "
            "تحویل ماژول‌ها، راه‌اندازی نهایی) پیشنهاد می‌شود و نیازمند توافق طرفین است. "
            "زمان‌بندی اجرا پس از تعیین دامنه و تعداد کاربران اعلام می‌شود.")
    y -= 18

    y = table(c, y, ["مرحلهٔ پرداخت", "زمان", "سهم"], [
        ["پیش‌پرداخت", "پس از امضای قرارداد", "۴۰٪"],
        ["تحویل ماژول‌ها", "پس از تحویل و آموزش", "۴۰٪"],
        ["راه‌اندازی نهایی", "پس از پذیرش نهایی", "۲۰٪"],
    ], widths=[0.24, 0.56, 0.20], size=8.0, leading=12.2, first_col_bold=True)
    y -= 34

    c.setFillColor(NAVY)
    c.setFont("VazirB", 9.4)
    c.drawRightString(PAGE_W - 118, y, visual("با احترام، مدیرعامل شرکت فروغ آریا"))
    c.setStrokeColor(LINE)
    c.setLineWidth(0.6)
    c.line(PAGE_W - 250, y - 34, PAGE_W - 118, y - 34)
    c.setFillColor(GRAY_LIGHT)
    c.setFont("Vazir", 7.4)
    c.drawRightString(PAGE_W - 160, y - 46, visual("محل مهر و امضا"))
    footer(c, 5)


# ------------------------------------------------------------------ build
def main():
    from reportlab.pdfgen import canvas as pdfcanvas

    c = pdfcanvas.Canvas(OUT_PDF, pagesize=A4)
    c.setTitle("پیشنهاد همکاری — دفتر کار دیجیتال | شرکت فروغ آریا")
    c.setAuthor("شرکت فروغ آریا")
    c.setSubject("چهار ماژول: اطلاعات پایه، اتوماسیون اداری، دستور کار، آرشیو اسناد و مدارک")
    c.setCreator("Forough Arya")

    for i, fn in enumerate((page_cover, page_summary, page_modules, page_delivery, page_price), 1):
        fn(c)
        c.showPage()
    c.save()

    import pymupdf
    d = pymupdf.open(OUT_PDF)
    d.set_toc([[1, "دفتر کار دیجیتال — جلد", 1],
               [1, "خلاصهٔ مدیریتی", 2],
               [1, "چهار ماژول در یک نگاه", 3],
               [1, "اجرا، امنیت و دامنهٔ تحویل", 4],
               [1, "پیشنهاد قیمت", 5]])
    d.saveIncr()
    print("pages:", len(pymupdf.open(OUT_PDF)))
    print("written:", OUT_PDF)


if __name__ == "__main__":
    main()
