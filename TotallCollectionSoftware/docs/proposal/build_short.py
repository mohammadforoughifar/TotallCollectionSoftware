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
    """عنوان سند، یک خط مخاطب‌محور، و امضای شرکت."""
    draw_letterhead(c)
    header_fields(c)

    y = TOP - 150
    c.setFillColor(NAVY)
    c.setFont("VazirB", 24)
    c.drawCentredString(PAGE_W / 2, y, visual("معرفی چهار ماژول نرم‌افزاری"))

    c.setStrokeColor(RED)
    c.setLineWidth(2.4)
    c.line(PAGE_W / 2 - 96, y - 14, PAGE_W / 2 + 96, y - 14)

    rt = RT("برای مدیرعاملی که می‌خواهد گردش مکاتبات، تعهدات کاری و مدارک سازمان را در یک "
            "سامانه، با یک ورود و یک سطح دسترسی ببیند.",
            size=11.6, leading=21, align="center", color="#3A4353")
    rt.wrap(CONTENT_W - 60, 200)
    rt.drawOn(c, LM + 30, y - 44 - rt._h)

    # خط پایانی: شرکت و تاریخ
    y2 = BOTTOM + 78
    c.setStrokeColor(colors.HexColor("#E1E5EB"))
    c.setLineWidth(0.7)
    c.line(LM + CONTENT_W * 0.16, y2, LM + CONTENT_W * 0.84, y2)
    c.setFillColor(GRAY_LIGHT)
    c.setFont("Vazir", 9.2)
    c.drawCentredString(PAGE_W / 2, y2 - 20, visual(f"{DOC['vendor']}  •  {DOC['date_fa']}"))
    c.setFont("Vazir", 8.0)
    c.drawCentredString(PAGE_W / 2, y2 - 36, visual("محرمانه — ویژهٔ مدیرعامل"))

    footer(c, 1)


# ------------------------------------------------------------------ page 2
def page_summary(c):
    draw_letterhead(c)
    header_fields(c)
    y = band(c, TOP, "خلاصهٔ مدیریتی")
    y -= 15

    y = para(c, y, "در بیشتر سازمان‌ها، داده‌های پایه در فایل‌های پراکنده نگه داشته می‌شود، گردش "
                   "نامه‌ها به کاغذ و پیگیری تلفنی وابسته است، تعهدات کاری مهلت و مسئول مشخصی "
                   "ندارد و مدارک در پوشه‌های شخصی می‌ماند. این پیشنهاد یک سامانهٔ واحد برای هر "
                   "چهار حوزه ارائه می‌کند: نامه‌ها با گردش و مهلت مشخص جابه‌جا می‌شوند، هر دستور "
                   "کار یک مسئول و یک سررسید دارد، و هر مدرک در جای خود و با سطح دسترسی تعیین‌شده "
                   "نگهداری می‌شود. چون هر چهار ماژول روی یک پایگاه داده کار می‌کنند، گزارش "
                   "میان‌ماژولی و ارجاع بین نامه و کار بدون یکپارچه‌سازی اضافی انجام می‌شود.",
             size=10.0, leading=18.6)
    y -= 24

    y = table(c, y, ["ماژول", "چه چیزی به سازمان می‌دهد"], [
        ["اطلاعات پایه", "یک منبع واحد از سازمان، واحدها، کاربران و دسترسی‌ها؛ مبنای کار سه ماژول دیگر"],
        ["اتوماسیون اداری", "گردش نامهٔ داخلی، وارده و صادره با ارجاع، مهلت، پیش‌نویس، امضا و دبیرخانه"],
        ["دستور کار", "ثبت و ارجاع تعهدات کاری با مهلت، اولویت، چک‌لیست و گزارش عملکرد"],
        ["آرشیو اسناد و مدارک", "نگهداری مدارک در ساختار پوشه‌ای، با نسخه‌گذاری، گردش تأیید و جستجو"],
    ], widths=[0.22, 0.78], size=9.5, leading=16.6, first_col_bold=True)
    y -= 24

    y = box(c, y, "سابقهٔ محصول",
            "این چهار ماژول بخشی از یک سامانهٔ کامل و آمادهٔ بهره‌برداری‌اند، نه طرح اولیه؛ "
            "آمار بهره‌برداری و امکان بازدید از سامانه در جلسهٔ فنی ارائه می‌شود.")
    y -= 24

    y = para(c, y, "سه تصمیم مورد انتظار از مدیریت", font="VazirB", size=10.2, color=NAVY,
             align="right")
    y -= 8
    for q in [
        "تأیید دامنهٔ چهار ماژول و ترتیب اجرای فازها (صفحهٔ ۴).",
        "تعیین فهرست کاربران و واحدهای سازمانی برای راه‌اندازی اولیه.",
        "تأیید مبلغ، تخفیف و شرایط پرداخت (صفحهٔ ۵).",
    ]:
        y = para(c, y, "•  " + q, size=9.4, leading=17.0, color=INK)
        y -= 4
    footer(c, 2)


# ------------------------------------------------------------------ page 3
def page_modules(c):
    draw_letterhead(c)
    header_fields(c)
    y = band(c, TOP, "چهار ماژول و فایدهٔ هر یک")
    y -= 16

    blocks = [
        ("۱. اطلاعات پایه",
         ["سازمان، واحدها، کاربران و سطوح دسترسی یک‌بار تعریف می‌شوند و همهٔ ماژول‌های دیگر از "
          "همان تعریف استفاده می‌کنند؛ بنابراین دادهٔ تکراری و دسترسی‌های ناهمگون از بین می‌رود.",
          "شمارهٔ نامه، سربرگ و تنظیمات هر شرکت به‌صورت مرکزی مدیریت می‌شود و تغییر ساختار "
          "سازمانی نیازی به تغییر سامانه ندارد.",
          "هر عملیات حساس با کاربر، زمان و شرح ثبت می‌شود؛ سابقهٔ تغییرات برای حسابرسی در "
          "دسترس است."]),
        ("۲. اتوماسیون اداری",
         ["نامهٔ داخلی، وارده و صادره در یک گردش مشخص حرکت می‌کند؛ هر گیرنده می‌داند چه چیزی، "
          "تا چه زمانی و با چه اولویتی باید انجام دهد.",
          "ارجاع، رویت، پاسخ، پیش‌نویس و امضا در سامانه ثبت می‌شود و شمارهٔ ثبت دبیرخانه به‌طور "
          "خودکار صادر می‌گردد؛ پیگیری وضعیت نامه دیگر تلفنی نیست.",
          "بایگانی اداری و صورت‌جلسه‌ها هم در همین محیط نگهداری می‌شوند تا سابقهٔ مکاتبات یکجا "
          "بماند."]),
        ("۳. دستور کار",
         ["هر دستور با مسئول، مهلت، اولویت و وضعیت ثبت می‌شود؛ کارهای باز و سررسیدشده در یک "
          "فهرست دیده می‌شوند.",
          "چک‌لیست، زیردستور و تکرار دوره‌ای، کارهای چندمرحله‌ای و کارهای جاری (مثل یادآوری‌های "
          "ماهانه) را پوشش می‌دهد.",
          "گزارش عملکرد افراد و پروژه‌های کاری، مبنای ارزیابی به‌موقع انجام‌شدن تعهدات است."]),
        ("۴. آرشیو اسناد و مدارک",
         ["مدارک در ساختار پوشه‌ای سازمان نگهداری می‌شوند و دسترسی هر فرد در هر پوشه جداگانه "
          "تعیین می‌شود؛ مدرک محرمانه فقط برای افراد مجاز قابل مشاهده است.",
          "نسخه‌گذاری و گردش تأیید، سابقهٔ تغییر متن و تأییدکنندگان را نگه می‌دارد و نسخهٔ معتبر "
          "مدرک مشخص است.",
          "جستجو در عنوان، برچسب و متن مدارک، پیدا کردن سند را از جست‌وجوی دستی در پوشه‌ها "
          "بی‌نیاز می‌کند."]),
    ]

    for title, lines in blocks:
        c.setFillColor(NAVY)
        c.setFont("VazirB", 10.3)
        c.drawRightString(LM + CONTENT_W, y, visual(title))
        c.setStrokeColor(colors.HexColor("#E3E7ED"))
        c.setLineWidth(0.6)
        c.line(LM, y - 5, LM + CONTENT_W, y - 5)
        y -= 18
        for ln in lines:
            y = para(c, y, "•  " + ln, size=9.0, leading=15.6, color=INK)
            y -= 3
        y -= 12

    y = box(c, y, "جمع‌بندی",
            "چهار ماژول روی یک پایگاه داده و یک مدل دسترسی کار می‌کنند؛ به همین دلیل ارجاع "
            "نامه به دستور کار، پیوست مدرک به یک پرونده و گزارش‌گیری از هر چهار حوزه، بدون "
            "سامانهٔ واسط انجام می‌شود.")
    footer(c, 3)


# ------------------------------------------------------------------ page 4
def page_delivery(c):
    draw_letterhead(c)
    header_fields(c)
    y = band(c, TOP, "اجرا، آموزش و پشتیبانی")
    y -= 12

    y = table(c, y, ["فاز", "خروجی", "بازهٔ زمانی"], [
        ["۱ — زیرساخت", "استقرار، اطلاعات پایه، کاربران، نقش‌ها و ساختار شمارهٔ نامه", "۲ تا ۳ هفته"],
        ["۲ — اتوماسیون اداری", "گردش نامهٔ داخلی، وارده و صادره، دبیرخانه و بایگانی اداری", "۳ تا ۴ هفته"],
        ["۳ — دستور کار", "دستورها، چک‌لیست، تکرار، پروژه و گزارش عملکرد", "۲ هفته"],
        ["۴ — آرشیو اسناد", "پوشه‌بندی، سطوح دسترسی، نسخه‌گذاری، گردش تأیید و جستجو", "۳ تا ۴ هفته"],
    ], widths=[0.24, 0.60, 0.16], size=8.6, leading=13.4, first_col_bold=True)
    y -= 5
    y = para(c, y, "مجموع برآورد اجرا با احتساب هم‌پوشانی فازها: ۸ تا ۱۲ هفته از تاریخ شروع.",
             size=8.8, color=NAVY_SOFT, font="VazirB")
    y -= 10

    y = para(c, y, "آموزش", font="VazirB", size=9.4, color=NAVY, align="right")
    y -= 2
    y = para(c, y, "آموزش در پایان هر فاز و به‌صورت حضوری یا آنلاین برگزار می‌شود: مدیر سامانه "
                   "(تعریف کاربر و دسترسی، ساختار شمارهٔ نامه)، دبیرخانه (ثبت، ارسال، ارجاع، امضا و "
                   "بایگانی) و کاربران (کارتابل، پیش‌نویس، دستور کار و جستجوی مدارک). راهنمای "
                   "کاربری تحویل داده می‌شود.", size=8.9, leading=15.4)
    y -= 10

    y = para(c, y, "پشتیبانی بعد از راه‌اندازی", font="VazirB", size=9.4, color=NAVY, align="right")
    y -= 2
    y = table(c, y, ["خدمت", "شرایط پیشنهادی"], [
        ["گارانتی", "۳ ماه از تحویل نهایی؛ رفع ایراد بدون هزینه"],
        ["پشتیبانی سالانه", "۱۵٪ مبلغ نرم‌افزار در سال؛ رفع اشکال، به‌روزرسانی و پاسخ تلفنی"],
        ["زمان پاسخ‌گویی", "پاسخ اولیه حداکثر یک روز کاری؛ ایراد متوقف‌کننده در همان روز"],
        ["توسعهٔ سفارشی", "پس از برآورد کتبی و تأیید کارفرما، جدا از این پیشنهاد"],
    ], widths=[0.26, 0.74], size=8.5, leading=13.2, first_col_bold=True)
    y -= 10

    y = para(c, y, "امنیت در سه خط", font="VazirB", size=9.4, color=NAVY, align="right")
    y -= 2
    for line in [
        "دسترسی هر کاربر بر پایهٔ نقش تعیین می‌شود و پیش از هر عملیات بررسی می‌گردد.",
        "ورود با نام کاربری و رمز انجام می‌شود و نشست غیرفعال به‌صورت خودکار قطع می‌گردد.",
        "هر عملیات حساس با کاربر، زمان و شرح ثبت می‌شود و برای مدارک، سطح دسترسی پوشه‌ای اعمال می‌گردد.",
    ]:
        y = para(c, y, "•  " + line, size=8.8, leading=14.6, color=INK)
        y -= 3
    y -= 12

    y = box(c, y, "سه سؤال که پاسخشان در جلسهٔ شروع لازم است",
            "۱) دسترسی به تنظیمات سامانه فقط برای مدیر سامانه باشد یا بخشی از آن به مدیران واحدها "
            "هم داده شود؟  ۲) امضای الکترونیکی با امضای ترسیمی کافی است یا تأیید هویت اضافه شود؟  "
            "۳) آرشیو اسناد مستقل کار کند یا مدارک به نامه‌ها و دستورهای کار متصل شوند؟")
    footer(c, 4)


# ------------------------------------------------------------------ page 5
def page_price(c):
    draw_letterhead(c)
    header_fields(c)

    y = TOP - 12
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
                   "کاربران است. همهٔ مبالغ به ریال و برای هر ماژول جداگانه اعلام شده است. "
                   "زمان‌بندی اجرا ۸ تا ۱۲ هفته و جزئیات پشتیبانی در صفحهٔ ۴ آمده است.", size=8.9)
    y -= 12

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
    rev = list(reversed(totals))
    data.append([
        CellFlow(rev[0], font="VazirB", size=8.4, leading=13, align="center", pad=4),
        CellFlow(rev[1], font="VazirB", size=8.4, leading=13, align="right", pad=6),
        CellFlow(rev[2], size=7.8, leading=12, align="right", pad=4),
        CellFlow(rev[3], size=7.8, leading=12.4, align="center", pad=4),
        CellFlow(rev[4], size=7.8, leading=12.4, align="center", pad=4),
        CellFlow(rev[5], font="VazirB", size=8.6, leading=13, align="center", pad=4),
    ])

    after = ["", "با احتساب پشتیبانی سال اول", "۴٬۲۵۰٬۰۰۰٬۰۰۰ + ۶۳۷٬۵۰۰٬۰۰۰", "—", "—",
             "۴٬۸۸۷٬۵۰۰٬۰۰۰"]
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
    c.setSubject("اطلاعات پایه، اتوماسیون اداری، دستور کار، آرشیو اسناد و مدارک")
    c.setCreator("Forough Arya")

    for i, fn in enumerate((page_cover, page_summary, page_modules, page_delivery, page_price), 1):
        fn(c)
        c.showPage()
    c.save()

    import pymupdf
    d = pymupdf.open(OUT_PDF)
    d.set_toc([[1, "جلد", 1],
               [1, "خلاصهٔ مدیریتی", 2],
               [1, "چهار ماژول و فایدهٔ هر یک", 3],
               [1, "اجرا، آموزش و پشتیبانی", 4],
               [1, "پیشنهاد قیمت", 5]])
    d.saveIncr()
    print("pages:", len(pymupdf.open(OUT_PDF)))
    print("written:", OUT_PDF)


if __name__ == "__main__":
    main()
