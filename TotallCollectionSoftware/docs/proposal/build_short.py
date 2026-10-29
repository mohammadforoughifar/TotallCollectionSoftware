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
             ("TOPPADDING", (0, 0), (-1, -1), 3),
             ("BOTTOMPADDING", (0, 0), (-1, -1), 3)]
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
    items = [("خلاصهٔ مدیریتی و چالش‌های رایج", "۲"),
             ("گردش کار، دسترسی و گزارش‌ها", "۳"),
             ("زمان‌بندی اجرا، گارانتی و پشتیبانی", "۴"),
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

    y = para(c, y, "سازمان‌ها در این چهار حوزه معمولاً با چالش‌های مشابهی روبه‌رو می‌شوند. جدول زیر "
                   "این چالش‌ها را در برابر پاسخ سامانه قرار می‌دهد؛ دامنهٔ دقیق آن برای این شرکت در "
                   "جلسهٔ نیازسنجی نهایی می‌شود.")
    y -= 13

    y = table(c, y, ["ماژول", "چالش رایج در سازمان‌ها", "پاسخ سامانه"], [
        ["اطلاعات پایه", "پراکندگی داده‌های پایه و تعریف دسترسی‌ها در فایل‌های جداگانه",
         "یک منبع واحد از سازمان، نقش‌ها، ساختار شمارهٔ نامه و تنظیمات"],
        ["اتوماسیون اداری", "وابستگی گردش مکاتبات به کاغذ و پیگیری تلفنی وضعیت نامه",
         "نامهٔ داخلی، وارده و صادره با ارجاع، مهلت، پیش‌نویس و امضای الکترونیکی"],
        ["دستور کار", "دشواری پیگیری تعهدات کاری، مهلت‌ها و مسئول هر کار",
         "ثبت، ارجاع، اولویت، مهلت، چک‌لیست، تکرار و گزارش عملکرد افراد"],
        ["آرشیو اسناد", "پراکندگی مدارک و نبود سابقهٔ مشخص از دسترسی به آن‌ها",
         "بایگانی درختی، دسترسی نفر‌به‌نفر، نسخه‌گذاری، گردش تأیید و جستجو"],
    ], widths=[0.18, 0.38, 0.44], size=8.1, leading=12.6, first_col_bold=True)
    y -= 13

    y = box(c, y, "یک سامانه، یک منبع داده، یک مدل دسترسی",
            "دسترسی بر پایهٔ نقش تعریف می‌شود: هر نقش همان کارهایی را می‌بیند که به او سپرده شده؛ "
            "هم در فهرست‌ها و هم در خود رکورد اعمال می‌شود و هر عملیات حساس در سابقهٔ سامانه "
            "ثبت می‌ماند.")
    y -= 11

    y = box(c, y, "دربارهٔ محصول",
            "این چهار ماژول بخشی از یک سامانهٔ کامل و آمادهٔ بهره‌برداری‌اند، نه طرح اولیه؛ "
            "آمار بهره‌برداری و امکان بازدید از سامانه در جلسهٔ فنی ارائه می‌شود.")
    y -= 12

    y -= 4
    y = para(c, y, "چرا این چهار ماژول، به این ترتیب", font="VazirB", size=9.4, color=NAVY,
             align="right")
    y -= 4
    for q in [
        "اطلاعات پایه اول می‌آید، چون بدون سازمان، کاربر و نقش، هیچ ماژول دیگری قابل راه‌اندازی نیست.",
        "اتوماسیون اداری بیشترین حجم کار روزمرهٔ سازمان را می‌گیرد و بیشترین صرفه‌جویی را دارد.",
        "دستور کار، خروجی نامه‌ها را به تعهد قابل پیگیری تبدیل می‌کند: هر کار یک مسئول، یک مهلت و یک وضعیت دارد.",
        "آرشیو اسناد، سابقهٔ مکاتبات و مدارک سازمان را نگه می‌دارد؛ بدون آن، این سابقه در فایل‌های پراکنده می‌ماند.",
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
    y = band(c, TOP, "گردش کار، دسترسی و گزارش‌ها")
    y -= 12

    y = para(c, y, "گردش نامه، نمونه‌ای از کار روزمره", font="VazirB", size=9.2, color=NAVY,
             align="right")
    y -= 3
    y = para(c, y, "ثبت نامه ← انتخاب گیرندگان و سطح محرمانگی ← ارسال ← ارجاع با مهلت ← رویت و "
                   "پاسخ ← تصمیم و امضا ← ثبت دبیرخانه ← بایگانی. زمان، انجام‌دهنده و سابقهٔ هر "
                   "گام در سامانه می‌ماند.", size=8.6)
    y -= 6
    y = para(c, y, "به همین ترتیب، دستور کار از ثبت و ارجاع تا مهلت، چک‌لیست و بستن کار پیش "
                   "می‌رود؛ و مدرک آرشیوی از ثبت و بارگذاری نسخه تا تأیید، فعال‌سازی و در صورت "
                   "نیاز تمدید.", size=8.6)
    y -= 12

    y = para(c, y, "دسترسی‌ها", font="VazirB", size=9.2, color=NAVY, align="right")
    y -= 3
    y = para(c, y, "دسترسی هر کاربر بر پایهٔ نقش او تعیین می‌شود و فقط همان عملیات را شامل می‌شود: "
                   "مدیر سامانه، دبیرخانه، کارشناس و مدیر آرشیو نمونه‌هایی از این نقش‌ها هستند. "
                   "برای مدارک، سطح دسترسی جداگانه در پوشه‌ها تعریف می‌شود؛ بنابراین یک کاربر "
                   "می‌تواند در یک پوشهٔ مشخص «فقط مشاهده» و در پوشه‌ای دیگر «ثبت و ویرایش» داشته "
                   "باشد. هر عملیات حساس در سابقهٔ سامانه ثبت می‌شود.", size=8.6)
    y -= 12

    y = para(c, y, "آنچه مدیر در گزارش‌ها می‌بیند", font="VazirB", size=9.2, color=NAVY,
             align="right")
    y -= 3
    y = table(c, y, ["گزارش", "خروجی"], [
        ["گردش مکاتبات", "وضعیت نامه‌ها بر اساس مرحله، واحد سازمانی و مهلت"],
        ["کارتابل و ارجاع‌ها", "نامه‌های در انتظار اقدام و پاسخ‌های عقب‌افتاده"],
        ["دستورهای کار", "کارهای باز، سررسیدشده و عملکرد افراد"],
        ["بایگانی اسناد", "فهرست مدارک، نسخه‌ها و سابقهٔ دسترسی و چاپ"],
        ["سابقهٔ عملیات", "چه کسی، چه زمانی، چه تغییری داده است"],
    ], widths=[0.24, 0.76], size=7.9, leading=11.8, first_col_bold=True)
    y -= 7
    y = para(c, y, "اطلاع‌رسانی و خروجی: ایمیل سازمانی، اعلان در مرورگر و موبایل، پیامک برای "
                   "دستور کار، و خروجی اکسل و PDF از فهرست‌ها و گزارش‌ها.", size=7.7, color=GRAY)
    y -= 10

    y = table(c, y, ["در دامنهٔ این پیشنهاد", "خارج از دامنه"], [
        ["چهار ماژول: اطلاعات پایه، اتوماسیون اداری، دستور کار، آرشیو اسناد",
         "ماژول‌های مالی، انبار، فروش، منابع انسانی و سایر بخش‌های سامانه"],
        ["استقرار، تعریف داده‌های پایه، آموزش کاربران کلیدی",
         "ورود انبوه داده‌های تاریخی (در صورت نیاز، جداگانه برآورد می‌شود)"],
        ["گارانتی و پشتیبانی طبق جدول صفحهٔ ۴",
         "توسعهٔ سفارشی و تغییر فرایندها خارج از قابلیت‌های موجود"],
    ], widths=[0.42, 0.58], size=7.8, leading=11.8)
    y -= 10

    y = box(c, y, "یک روز کاری با سامانه",
            "کارتابل و دستورهای امروز، پاسخ و ارجاع، ثبت مدرک در پوشهٔ مربوط، و در پایان روز "
            "گزارش عملکرد و نامه‌های در انتظار امضا — همه با یک ورود.")
    footer(c, 3)


# ------------------------------------------------------------------ page 4
def page_delivery(c):
    draw_letterhead(c)
    header_fields(c)
    y = band(c, TOP, "زمان‌بندی اجرا، گارانتی و پشتیبانی")
    y -= 11

    y = table(c, y, ["فاز", "خروجی", "مدت (برآورد)"], [
        ["۱ — زیرساخت", "استقرار، پایگاه داده، اطلاعات پایه، نقش‌ها و ساختار شمارهٔ نامه",
         "۲ هفته"],
        ["۲ — اتوماسیون اداری", "گردش نامهٔ داخلی، وارده و صادره، دبیرخانه، بایگانی اداری",
         "۳ تا ۴ هفته"],
        ["۳ — دستور کار", "دستورها، چک‌لیست، تکرار، پروژه و گزارش عملکرد", "۲ هفته"],
        ["۴ — آرشیو اسناد", "پوشه‌بندی، سطوح دسترسی، نسخه‌گذاری، گردش تأیید و جستجو",
         "۳ تا ۴ هفته"],
    ], widths=[0.24, 0.58, 0.18], size=8.0, leading=12.0, first_col_bold=True)
    y -= 5
    y = para(c, y, "مجموع برآورد اجرا با احتساب هم‌پوشانی فازها: ۸ تا ۱۲ هفته از تاریخ شروع. این "
                   "بازه پس از نهایی‌شدن تعداد کاربران، واحدها و حجم داده‌های اولیه به برنامهٔ "
                   "تفصیلی تبدیل می‌شود.", size=7.9, color=NAVY_SOFT)
    y -= 11

    y = para(c, y, "امنیت و کنترل دسترسی", font="VazirB", size=9.2, color=NAVY, align="right")
    y -= 3
    for line in [
        "ورود با نام کاربری و رمز؛ اعتبارسنجی نشست در هر درخواست و قطع نشست غیرفعال.",
        "هر مجوز به نقش تخصیص می‌یابد و پیش از اجرای عملیات بررسی می‌شود.",
        "ثبت کاربر، زمان، عملیات و نتیجه در سابقهٔ سامانه؛ پوشاندن مقادیر حساس.",
        "برای مدارک: چهار سطح دسترسی در پوشه، مجوز دانلود جدا و نشان‌گذاری نسخهٔ چاپی.",
    ]:
        y = para(c, y, "•  " + line, size=8.3, leading=13.6, color=INK)
        y -= 1
    y -= 9

    y = table(c, y, ["خدمات پس از تحویل", "شرایط پیشنهادی"], [
        ["گارانتی", "۳ ماه از تحویل نهایی؛ رفع ایراد بدون هزینه"],
        ["پشتیبانی سالانه", "۱۵٪ مبلغ نرم‌افزار در سال؛ شامل رفع اشکال، به‌روزرسانی و پاسخ تلفنی"],
        ["زمان پاسخ‌گویی (SLA)", "پاسخ اولیه حداکثر یک روز کاری؛ ایرادهای متوقف‌کننده در همان روز"],
        ["مجوز استفاده (لایسنس)", "سازمانی و بدون محدودیت تعداد کاربر، برای یک شرکت و شعب آن"],
        ["توسعهٔ سفارشی", "نرخ ساعتی ۳٬۵۰۰٬۰۰۰ ریال با برآورد کتبی پیش از شروع کار"],
    ], widths=[0.26, 0.74], size=7.9, leading=11.8, first_col_bold=True)
    y -= 5
    y = para(c, y, "شرایط بالا پیشنهادی است و در قرارداد نهایی قابل مذاکره است.",
             size=7.5, color=GRAY)
    y -= 10

    y = table(c, y, ["تصمیم مدیریتی مورد نیاز", "گزینه‌ها"], [
        ["سطح دسترسی به تنظیمات سامانه", "فقط مدیر سامانه، یا بخشی از آن برای مدیران واحدها"],
        ["سطح اعتبارسنجی امضای الکترونیکی", "امضای ترسیمی، یا افزودن تأیید هویت امضاکننده"],
        ["دامنهٔ اتصال آرشیو اسناد", "آرشیو مستقل، یا اتصال مدارک به نامه و دستور کار"],
        ["تعداد کاربران و واحدهای سازمانی", "فهرست کاربران، نقش‌ها و واحدهای راه‌اندازی اولیه"],
    ], widths=[0.34, 0.66], size=7.8, leading=11.4, first_col_bold=True)
    y -= 6
    y = para(c, y, "نتیجهٔ این چهار تصمیم، در زمان‌بندی اجرا اثر دارد.", size=7.5, color=GRAY)
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

    y = para(c, y, "مدیرعامل محترم شرکت یاران هم‌افزای کویر", size=9.6, font="VazirB",
             color=NAVY, align="right")
    y -= 3
    y = para(c, y, "با سلام و احترام، دامنهٔ خدمات شامل ارائهٔ نرم‌افزار، راه‌اندازی اولیه و آموزش "
                   "کاربران است. همهٔ مبالغ این صفحه به ریال و برای هر ماژول جداگانه اعلام شده است. "
                   "زمان‌بندی اجرا ۸ تا ۱۲ هفته و جزئیات پشتیبانی در صفحهٔ ۴ آمده است.")
    y -= 12

    # ردیف‌ها در ترتیب منطقی: ردیف | ماژول | شرح خدمات | مبلغ پایه | تخفیف | مبلغ نهایی
    rows = [
        ["۱", "اطلاعات پایه",
         "سازمان و شرکت‌ها، کاربران و نقش‌ها، ساختار شمارهٔ نامه، تنظیمات و سابقهٔ عملیات",
         "۴۳۰٬۰۰۰٬۰۰۰", "۱۳۰٬۰۰۰٬۰۰۰", "۳۰۰٬۰۰۰٬۰۰۰"],
        ["۲", "اتوماسیون اداری", "نامهٔ داخلی، وارده و صادره، ارجاع، امضا و دبیرخانه",
         "۱٬۰۰۰٬۰۰۰٬۰۰۰", "—", "۱٬۰۰۰٬۰۰۰٬۰۰۰"],
        ["۳", "دستور کار", "ثبت، ارجاع، مهلت، چک‌لیست، پروژه و گزارش عملکرد",
         "۴۵۰٬۰۰۰٬۰۰۰", "—", "۴۵۰٬۰۰۰٬۰۰۰"],
        ["۴", "آرشیو اسناد و مدارک", "بایگانی، دسترسی، نسخه‌گذاری، گردش تأیید و جستجو",
         "۲٬۵۰۰٬۰۰۰٬۰۰۰", "—", "۲٬۵۰۰٬۰۰۰٬۰۰۰"],
    ]
    headers = ["ردیف", "ماژول", "شرح خدمات", "مبلغ پایه (ریال)", "تخفیف (ریال)",
               "مبلغ نهایی (ریال)"]
    widths = [0.07, 0.17, 0.33, 0.16, 0.13, 0.14]

    from build_short import _Cell as CellFlow
    data = [[CellFlow(h, font="VazirB", size=7.7, leading=11.5, align="center",
                      color=colors.white, pad=4) for h in reversed(headers)]]
    for r in rows:
        rev = list(reversed(r))
        data.append([
            CellFlow(rev[0], font="VazirB", size=8.3, leading=12.6, align="center", pad=4),
            CellFlow(rev[1], font="VazirB", size=7.8, leading=11.6, align="center", pad=4),
            CellFlow(rev[2], size=7.6, leading=12.0, align="right", pad=4),
            CellFlow(rev[3], size=7.7, leading=12.4, align="center", pad=4),
            CellFlow(rev[4], size=7.7, leading=12.4, align="center", pad=4),
            CellFlow(rev[5], font="VazirB", size=7.9, leading=12.4, align="center", pad=4),
        ])

    totals = ["", "جمع کل", "جمع مبالغ چهار ماژول", "۴٬۳۸۰٬۰۰۰٬۰۰۰", "۱۳۰٬۰۰۰٬۰۰۰", "۴٬۲۵۰٬۰۰۰٬۰۰۰"]
    after = ["", "با احتساب پشتیبانی سال اول", "۴٬۲۵۰٬۰۰۰٬۰۰۰ + ۶۳۷٬۵۰۰٬۰۰۰", "—", "—",
             "۴٬۸۸۷٬۵۰۰٬۰۰۰"]
    rev = list(reversed(totals))
    data.append([
        CellFlow(rev[0], font="VazirB", size=8.4, leading=13, align="center", pad=4),
        CellFlow(rev[1], font="VazirB", size=8.4, leading=13, align="right", pad=6),
        CellFlow(rev[2], size=7.8, leading=12, align="right", pad=4),
        CellFlow(rev[3], size=7.8, leading=12.4, align="center", pad=4),
        CellFlow(rev[4], size=7.8, leading=12.4, align="center", pad=4),
        CellFlow(rev[5], font="VazirB", size=8.6, leading=13, align="center", pad=4),
    ])

    rev = list(reversed(after))
    data.append([
        CellFlow(rev[0], font="VazirB", size=8.2, leading=12.6, align="center", pad=4),
        CellFlow(rev[1], font="VazirB", size=8.0, leading=12.0, align="right", pad=6),
        CellFlow(rev[2], size=7.4, leading=11.2, align="right", pad=4),
        CellFlow(rev[3], size=7.8, leading=12.4, align="center", pad=4),
        CellFlow(rev[4], size=7.8, leading=12.4, align="center", pad=4),
        CellFlow(rev[5], font="VazirB", size=8.4, leading=12.8, align="center", pad=4),
    ])

    t = Table(data, colWidths=[CONTENT_W * x for x in reversed(widths)], hAlign="LEFT")
    style = [("BACKGROUND", (0, 0), (-1, 0), NAVY),
             ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
             ("GRID", (0, 0), (-1, -1), 0.5, LINE),
             ("BACKGROUND", (0, 2), (-1, 2), BG_SOFT),
             ("BACKGROUND", (0, 4), (-1, 4), BG_SOFT),
             ("BACKGROUND", (0, 5), (-1, 5), BG_WARM),
             ("BACKGROUND", (0, 6), (-1, 6), BG_BLUE),
             ("TOPPADDING", (0, 0), (-1, -1), 4), ("BOTTOMPADDING", (0, 0), (-1, -1), 4)]
    t.setStyle(TableStyle(style))
    t.wrapOn(c, CONTENT_W, 10000)
    t.drawOn(c, LM, y - t._height)
    y -= t._height + 6

    y = para(c, y, "تخفیف ۳۰٪ به مناسبت خرید بستهٔ چهار ماژول، یکجا و بر مبلغ پایهٔ ماژول "
                   "اطلاعات پایه اعمال شده است (۱۳۰٬۰۰۰٬۰۰۰ ریال). مجوز استفاده سازمانی و بدون "
                   "محدودیت تعداد کاربر است؛ پشتیبانی سالانه معادل ۱۵٪ مبلغ نرم‌افزار "
                   "(۶۳۷٬۵۰۰٬۰۰۰ ریال) و سال اول اختیاری است.", size=7.5, color=GRAY)
    y -= 8

    y = para(c, y, "جمع کل به حروف : چهار میلیارد و دویست و پنجاه میلیون ریال",
             size=8.6, color=NAVY_SOFT, align="right")
    y -= 14

    y = box(c, y, "شرایط پیشنهاد",
            "اعتبار این پیشنهاد ۳۰ روز از تاریخ درج است. پرداخت در سه مرحله پیشنهاد می‌شود و "
            "نیازمند توافق طرفین است. زمان‌بندی اجرا ۸ تا ۱۲ هفته، مشروط به تعیین تعداد کاربران "
            "و واحدها در جلسهٔ شروع است.")
    y -= 18

    y = table(c, y, ["مرحلهٔ پرداخت", "زمان", "سهم", "مبلغ (ریال)"], [
        ["پیش‌پرداخت", "پس از امضای قرارداد", "۴۰٪", "۱٬۷۰۰٬۰۰۰٬۰۰۰"],
        ["تحویل ماژول‌ها", "پس از تحویل و آموزش", "۴۰٪", "۱٬۷۰۰٬۰۰۰٬۰۰۰"],
        ["راه‌اندازی نهایی", "پس از پذیرش نهایی", "۲۰٪", "۸۵۰٬۰۰۰٬۰۰۰"],
    ], widths=[0.24, 0.40, 0.12, 0.24], size=8.0, leading=12.2, first_col_bold=True)
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
    c.setTitle("معرفی چهار ماژول نرم‌افزاری | شرکت فروغ آریا")
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
