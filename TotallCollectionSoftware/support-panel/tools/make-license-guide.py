#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
سازندهٔ PDF راهنمای «امضای لایسنس و تحویل به مشتری».
خروجی: /home/user/راهنمای-امضا-و-تحویل-لایسنس.pdf
فونت: Vazirmatn (RTL) — فونت‌ها در ~/.fonts
"""
from fpdf import FPDF
from fpdf.enums import XPos, YPos

FONT_DIR = "/home/user/.fonts"
OUT = "/home/user/راهنمای-امضا-و-تحویل-لایسنس.pdf"

PERSIAN_RE = None


def has_persian(text: str) -> bool:
    return any("\u0600" <= ch <= "\u06FF" or ch == "\u200c" for ch in text)

INK = (26, 32, 44)
MUTED = (99, 110, 114)
ACCENT = (12, 86, 140)
ACCENT_SOFT = (232, 241, 248)
OK = (22, 122, 70)
OK_SOFT = (231, 246, 238)
WARN = (176, 92, 0)
WARN_SOFT = (253, 243, 226)
CODE_BG = (243, 244, 246)


class Guide(FPDF):
    def __init__(self):
        super().__init__(orientation="P", unit="mm", format="A4")
        self.set_auto_page_break(auto=True, margin=18)
        self.set_margins(16, 16, 16)
        self.add_font("vazir", "", f"{FONT_DIR}/Vazirmatn-Regular.ttf")
        self.add_font("vazir", "B", f"{FONT_DIR}/Vazirmatn-Bold.ttf")
        self.add_font("mono", "", "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf")
        try:
            self.set_text_shaping(True, direction="rtl")
        except Exception:
            pass
        self.rtl = True
        self.cover = True

    def shape_for(self, text):
        """جهت شکل‌دهی را بر اساس محتوا انتخاب می‌کند (متن لاتین ⇒ LTR)."""
        try:
            self.set_text_shaping(True, direction="rtl" if has_persian(text) else "ltr")
        except Exception:
            pass

    # ---------- ابزارهای پایه ----------
    def footer(self):
        if self.page_no() == 1:
            return
        self.set_y(-13)
        self.set_font("vazir", "", 8)
        self.set_text_color(*MUTED)
        self.cell(0, 6, "راهنمای امضا و تحویل لایسنس — پنل پشتیبانی لایسنس", align="R")
        self.set_xy(16, -13)
        digits = "۰۱۲۳۴۵۶۷۸۹"
        num = "".join(digits[int(d)] for d in str(self.page_no() - 1))
        self.cell(0, 6, f"صفحهٔ {num}", align="L")

    def cover_page(self):
        self.add_page()
        self.cover = True
        self.set_fill_color(*ACCENT)
        self.rect(0, 0, 210, 62, style="F")
        self.set_y(20)
        self.set_font("vazir", "B", 24)
        self.set_text_color(255, 255, 255)
        self.cell(0, 12, "امضای لایسنس و تحویل به مشتری", align="C",
                  new_x=XPos.LMARGIN, new_y=YPos.NEXT)
        self.set_font("vazir", "", 12)
        self.cell(0, 8, "راهنمای گام‌به‌گام پنل پشتیبانی لایسنس — نسخهٔ ۱٫۰", align="C",
                  new_x=XPos.LMARGIN, new_y=YPos.NEXT)

        self.set_y(80)
        self.set_font("vazir", "", 11.5)
        self.set_text_color(*INK)
        intro = ("این راهنما دو طرف یک فرایند را نشان می‌دهد: از لحظه‌ای که درخواست مشتری "
                 "در پنل ثبت می‌شود تا لحظه‌ای که فایل «.lic» امضاشده را مشتری در نرم‌افزار "
                 "خود فعال می‌کند. هر گام با مسیر صفحه، نام دکمه‌ها و خطاهای احتمالی آمده است.")
        self.multi_cell(0, 7.5, intro, align="R")
        self.ln(6)

        rows = [
            ("مخاطب", "کارشناس پشتیبانی (Operator) و امضاکننده (Signer)"),
            ("پیش‌نیاز", "پنل نصب‌شده + جفت‌کلید امضا + ابزار LicenseTool"),
            ("خروجی نهایی", "یک فایل .lic امضاشده + رکورد لایسنس در پنل + ممیزی زنجیره‌هش"),
            ("تاریخ", "۱۴۰۵/۰۸/۲۳"),
        ]
        self.set_font("vazir", "", 10)
        for k, v in rows:
            self.set_font("vazir", "B", 10)
            self.set_text_color(*MUTED)
            label = k + ": "
            self.set_x(16)
            self.cell(28, 8, label, align="R")
            self.set_font("vazir", "", 10)
            self.set_text_color(*INK)
            self.cell(0, 8, v, align="R", new_x=XPos.LMARGIN, new_y=YPos.NEXT)

        self.set_y(232)
        self.set_draw_color(*ACCENT)
        self.set_line_width(0.5)
        self.line(16, self.get_y(), 194, self.get_y())
        self.ln(4)
        self.set_font("vazir", "", 9)
        self.set_text_color(*MUTED)
        self.multi_cell(0, 6, "تنها منبع امضا، ابزار LicenseTool با کلید خصوصی است؛ "
                              "هیچ کلید خصوصی یا گذرواژه‌ای داخل پنل، پایگاه‌داده یا این سند ذخیره نمی‌شود.",
                        align="R")
        self.cover = False

    # ---------- عناصر محتوایی ----------
    def h1(self, text, num=None):
        if self.get_y() > 250:
            self.add_page()
        self.ln(3)
        self.set_fill_color(*ACCENT)
        y = self.get_y()
        self.rect(195, y + 2.5, 3.2, 3.2, style="F")
        self.set_font("vazir", "B", 15)
        self.set_text_color(*ACCENT)
        title = f"{num}) {text}" if num else text
        self.set_x(16)
        self.cell(174, 9, title, align="R", new_x=XPos.LMARGIN, new_y=YPos.NEXT)
        self.set_draw_color(210, 216, 222)
        self.set_line_width(0.3)
        self.line(16, self.get_y() + 1, 194, self.get_y() + 1)
        self.ln(5)

    def h2(self, text):
        if self.get_y() > 258:
            self.add_page()
        self.ln(1.5)
        self.set_font("vazir", "B", 11.5)
        self.set_text_color(*INK)
        self.cell(0, 7, text, align="R", new_x=XPos.LMARGIN, new_y=YPos.NEXT)
        self.ln(1)

    def p(self, text, size=10, color=INK, gap=2.5):
        self.set_font("vazir", "", size)
        self.set_text_color(*color)
        self.shape_for(text)
        self.multi_cell(0, 6.6, text, align="R")
        self.ln(gap)

    def bullets(self, items, size=10):
        self.set_font("vazir", "", size)
        self.set_text_color(*INK)
        for it in items:
            y = self.get_y()
            self.set_fill_color(*ACCENT)
            self.ellipse(192.5, y + 2.4, 1.7, 1.7, style="F")
            self.set_x(16)
            self.shape_for(it)
            self.multi_cell(174, 6.4, it, align="R")
            self.ln(0.6)
        self.ln(1.5)

    def steps(self, items, size=10):
        """گام‌های شماره‌دار (اعداد فارسی سمت راست)."""
        digits = "۰۱۲۳۴۵۶۷۸۹"
        self.set_font("vazir", "", size)
        for i, it in enumerate(items, 1):
            num = "".join(digits[int(d)] for d in str(i))
            y = self.get_y()
            self.set_fill_color(*ACCENT_SOFT)
            self.set_draw_color(*ACCENT)
            self.set_line_width(0.25)
            self.ellipse(190.5, y + 0.9, 5.4, 5.4, style="DF")
            self.set_font("vazir", "B", 8)
            self.set_text_color(*ACCENT)
            self.set_xy(190.5, y + 1.9)
            self.cell(5.4, 3.4, num, align="C")
            self.set_font("vazir", "", size)
            self.set_text_color(*INK)
            self.set_xy(16, y)
            self.shape_for(it)
            self.multi_cell(172, 6.4, it, align="R")
            self.ln(1.2)
        self.ln(1)

    def code(self, text, size=8.6):
        self.ln(1)
        lines = text.strip("\n").split("\n")
        # کوچک‌کردن خودکار فونت تا بلندترین خط در کادر جا شود (عرض کادر ۱۷۸mm)
        longest = max((len(x) for x in lines), default=1)
        fit = 168.0 / max(longest * 0.60, 1)
        size = max(6.4, min(size, fit))
        w = 178
        h = len(lines) * (size * 0.52) + 6
        if self.get_y() + h > 270:
            self.add_page()
        y0 = self.get_y()
        self.set_fill_color(*CODE_BG)
        self.set_draw_color(214, 220, 226)
        self.set_line_width(0.2)
        self.rect(16, y0, w, h, style="DF")
        try:
            self.set_text_shaping(True, direction="ltr")
        except Exception:
            pass
        self.set_font("mono", "", size)
        self.set_text_color(30, 30, 30)
        self.set_xy(19, y0 + 3)
        for line in lines:
            self.cell(w - 6, size * 0.52, line, align="L", new_x=XPos.LMARGIN, new_y=YPos.NEXT)
            self.set_x(19)
        try:
            self.set_text_shaping(True, direction="rtl")
        except Exception:
            pass
        self.set_y(y0 + h + 3)

    def callout(self, kind, text, size=9.5):
        palette = {"info": (ACCENT, ACCENT_SOFT), "ok": (OK, OK_SOFT), "warn": (WARN, WARN_SOFT)}
        fg, bg = palette[kind]
        self.ln(1)
        self.set_font("vazir", "", size)
        # ارتفاع تخمینی
        approx_lines = max(1, int(len(text) / 95) + 1)
        h = approx_lines * 6.2 + 6
        if self.get_y() + h > 270:
            self.add_page()
        y0 = self.get_y()
        self.set_fill_color(*bg)
        self.rect(16, y0, 178, h, style="F")
        self.set_fill_color(*fg)
        self.rect(190, y0, 4, h, style="F")
        self.set_xy(18, y0 + 3)
        self.set_text_color(*INK)
        try:
            self.set_text_shaping(True, direction="rtl")
        except Exception:
            pass
        self.multi_cell(166, 6.2, text, align="R")
        self.set_y(y0 + h + 3)

    def table(self, headers, rows, widths=None, size=9):
        total = 178
        n = len(headers)
        widths = widths or [total / n] * n
        line_h = 5.6

        def draw_header():
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
        draw_header()

        for idx, row in enumerate(rows):
            self.set_font("vazir", "", size)
            wrapped = []
            for cell_text, w in zip(row, widths):
                lines = self.multi_cell(w - 3, line_h, cell_text, align="R",
                                        dry_run=True, output="LINES")
                wrapped.append(lines)
            n_lines = max(len(x) for x in wrapped)
            h = n_lines * line_h + 3

            if self.get_y() + h > 272:
                self.add_page()
                draw_header()

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


g = Guide()
g.cover_page()

# ---------------------------------------------------------------- فهرست
g.add_page()
g.h1("فهرست")
toc = [
    ("۱", "نمای کلی: از درخواست تا فعال‌سازی", "۳"),
    ("۲", "پیش‌نیازهای یک‌باره (کلید، ابزار، پیکربندی)", "۴"),
    ("۳", "گام ۱ — ثبت درخواست مشتری در پنل", "۵"),
    ("۴", "گام ۲ — بررسی هویت و تأیید درخواست", "۶"),
    ("۵", "گام ۳ — ساخت پیش‌نویس لایسنس (بسته، مدت، ماژول، کاربر)", "۷"),
    ("۶", "گام ۴ — امضا و صدور فایل .lic", "۸"),
    ("۷", "گام ۵ — تحویل به مشتری و بستن پرونده", "۹"),
    ("۸", "سمت مشتری: گرفتن درخواست، فعال‌سازی و استفاده", "۱۰"),
    ("۹", "تمدید، ارتقا و «فوگرفتن» لایسنس قبلی", "۱۱"),
    ("۱۰", "خطاهای رایج و عیب‌یابی", "۱۲"),
    ("۱۱", "چک‌لیست امنیتی و کاری", "۱۳"),
    ("پ", "پیوست: مسیرها،نقاط پایانی API و دستورهای CLI", "۱۴"),
]
digits_fa = "۰۱۲۳۴۵۶۷۸۹"
for num, title, page in toc:
    y0 = g.get_y()
    # شمارهٔ بخش (ستون راست)
    g.set_font("vazir", "B", 10)
    g.set_text_color(*ACCENT)
    g.set_xy(184, y0)
    g.cell(10, 9, str(num), align="C")
    # عنوان
    g.set_font("vazir", "", 10.5)
    g.set_text_color(*INK)
    g.set_xy(38, y0)
    g.cell(144, 9, title, align="R")
    # شمارهٔ صفحه (ستون چپ)
    g.set_font("vazir", "", 9)
    g.set_text_color(*MUTED)
    g.set_xy(16, y0)
    page_fa = "".join("۰۱۲۳۴۵۶۷۸۹"[int(d)] if d.isdigit() else d for d in str(page))
    g.cell(18, 9, page_fa, align="L")
    # خط نقطه‌چین بین عنوان و شمارهٔ صفحه
    g.set_draw_color(212, 218, 224)
    g.set_line_width(0.15)
    g.set_dash_pattern(dash=0.6, gap=1.6)
    g.line(36, y0 + 6.8, 182, y0 + 6.8)
    g.set_dash_pattern()
    g.set_y(y0 + 9)
g.ln(4)
g.callout("info", "اصطلاح‌ها: «پنل» = برنامهٔ SupportPanel روی ماشین پشتیبانی (فقط localhost). "
                  "«مشتری» = نصب نرم‌افزار انبارداری روی سرور خودش. «.lic» = فایل JSON امضاشده که "
                  "مشتری آن را بارگذاری می‌کند.")

# ---------------------------------------------------------------- ۱
g.add_page()
g.h1("نمای کلی: از درخواست تا فعال‌سازی", 1)
g.p("لایسنس یک فایل JSON است که با کلید خصوصی پشتیبانی امضا می‌شود و به «شناسهٔ نصب» همان سرور "
    "مشتری گره می‌خورد. هیچ نرم‌افزار مشتری‌ای کلید خصوصی ندارد؛ فقط کلید عمومی (برای بازبینی امضا) "
    "در بیلد مشتری است. چرخهٔ کامل هفت گام دارد:")
g.table(
    ["گام", "چه کسی", "چه چیزی رد و بدل می‌شود", "نتیجه"],
    [
        ["۱", "مشتری", "فایل درخواست (license-request-*.json)", "درخواست ثبت‌شده در صف پنل"],
        ["۲", "پشتیبانی", "بررسی تماس/هویت مشتری", "وضعیت: تأییدشده (Approved)"],
        ["۳", "پشتیبانی", "بسته، مدت، ماژول‌ها، سقف کاربر", "پیش‌نویس ذخیره‌شده"],
        ["۴", "امضاکننده", "کلید خصوصی + گذرواژهٔ کلید", "فایل .lic امضاشده + سریال"],
        ["۵", "پشتیبانی", "ایمیل/دستی/دانلود", "وضعیت: تحویل‌شده (Delivered)"],
        ["۶", "مشتری", "بارگذاری .lic در نرم‌افزار", "فعال‌سازی بدون ری‌استارت"],
        ["۷", "پشتیبانی", "بستن پرونده و یادآوری تمدید", "وضعیت: بسته (Closed)"],
    ],
    widths=[14, 24, 78, 62],
)
g.h2("چرخهٔ وضعیت درخواست")
g.code("New -> Reviewing -> (NeedsInfo <-> Reviewing) -> Approved -> Issued -> Delivered -> Closed\n"
       "terminal branches: Rejected | Cancelled | Duplicate")
g.callout("warn", "امضا فقط از وضعیت «تأییدشده» ممکن است و فقط با نقش Signer/Admin. اگر دکمهٔ امضا را "
                  "نمی‌بینید، یعنی یا نقش کاربر Operator است یا درخواست هنوز تأیید نشده.")

# ---------------------------------------------------------------- ۲
g.add_page()
g.h1("پیش‌نیازهای یک‌باره (کلید، ابزار، پیکربندی)", 2)
g.p("این بخش فقط یک‌بار روی ماشین پشتیبانی انجام می‌شود.")
g.steps([
    "جفت‌کلید بسازید (بیرون مخزن؛ گذرواژهٔ کلید را خودتان انتخاب می‌کنید):",
])
g.code("LICENSETOOL_KEY_PASSWORD='<key-passphrase>' dotnet LicenseTool.dll keygen \\\n"
       "    --key-id sup-2026a --out-dir /secure/keys")
g.p("خروجی: sup-2026a.private.pem (فقط مالک بخواند، chmod 600) و sup-2026a.pem (کلید عمومی). "
    "کلید عمومی باید در بیلد مشتری (PublicKeyRing) باشد؛ کلید خصوصی هرگز از ماشین پشتیبانی بیرون نمی‌رود.",
    size=9.5)
g.steps([
    "مسیرها را در appsettings.json یا متغیر محیطی بدهید: Signer:PrivateKeyPath، Signer:PublicKeyPath، Signer:KeyId.",
    "مطمئن شوید presets.json کنار فایل اجرایی LicenseTool است (بسته‌های آماده FIN/HR/WAREHOUSE/OFFICE).",
    "پنل را اجرا کنید: bash run.sh (یا کلیک روی SupportPanel.sln و F5).",
    "وارد شوید (admin). در اولین ورود، تعویض گذرواژهٔ آغازین اجباری است.",
])
g.callout("ok", "صحت نصب را از داشبورد ببینید: اگر پیام سبز «ابزار امضا و کلیدها در دسترس‌اند» آمد، "
                "همه‌چیز آماده است. پیام زرد یعنی ابزار، کلید خصوصی یا کلید عمومی پیدا نشده — همان‌جا "
                "علت دقیق نوشته شده است.")
g.h2("پیکربندی حداقلی")
g.table(
    ["کلید", "نمونه", "توضیح"],
    [
        ["Signer:PrivateKeyPath", "/secure/keys/sup-2026a.private.pem", "کلید امضا (راز)"],
        ["Signer:PublicKeyPath", "/secure/keys/sup-2026a.pem", "کلید عمومی برای بازبینی فوری"],
        ["Signer:KeyId", "sup-2026a", "شناسهٔ کلید در فایل لایسنس"],
        ["Signer:ToolPath", "tools/LicenseTool.dll", "خالی بگذارید تا خودکار پیدا شود"],
        ["ConnectionStrings:Panel", "Data Source=data/support.db", "SQLite پیش‌فرض پنل"],
    ],
    widths=[52, 66, 60],
)
g.callout("info", "گذرواژهٔ کلید خصوصی هیچ‌جا ذخیره نمی‌شود: در هر امضا از شما پرسیده می‌شود و فقط از راه "
                  "متغیر محیطی به فرایند ابزار می‌رود (نه آرگومان خط فرمان، نه پایگاه‌داده، نه لاگ).")

# ---------------------------------------------------------------- ۳
g.add_page()
g.h1("گام ۱ — ثبت درخواست مشتری در پنل", 3)
g.p("مشتری اول فایل درخواست را می‌گیرد و برای شما می‌فرستد (روش گرفتنش در بخش ۸ آمده است). "
    "سپس در پنل:")
g.steps([
    "از داشبورد دکمهٔ «ثبت درخواست» را بزنید (مسیر: /Requests/Ingest).",
    "روش ورود را انتخاب کنید: چسباندن متن JSON، آپلود فایل .json، یا ورود دستی اطلاعات.",
    "دکمهٔ «پیش‌نمایش» را بزنید. پنل شناسهٔ نصب، نام مشتری و نوع درخواست را نشان می‌دهد و "
    "هیچ چیزی ذخیره نمی‌کند.",
    "اگر تکراری بودن مشکوک است (همان شناسهٔ نصب با درخواست باز)، پنل هشدار می‌دهد؛ می‌توانید "
    "درخواست را «تکراری» علامت بزنید تا به پروندهٔ قبلی وصل شود.",
    "دکمهٔ «ذخیره» را بزنید. شمارهٔ درخواست (مثل REQ-1405-0001) ساخته می‌شود و وضعیت New است.",
])
g.p("نمونهٔ حداقلی فایل درخواست (مقادیر فارسی هم مجاز است؛ فقط شناسهٔ نصب باید دقیق باشد):", size=9.5)
g.code('{\n  "requestVersion": 2,\n  "requestType": "NewActivation",\n  "installationId": "8f2c...e41b",\n  "customerName": "Sample Trading Co.",\n  "contactName": "IT Admin",\n  "contactPhone": "02100000000",\n  "requestedAt": "2026-11-14T08:00:00Z"\n}')
g.callout("warn", "شناسهٔ نصب، قلب اتصال لایسنس به سرور مشتری است. اگر مشتری سرور را عوض کرده یا "
                  "پایگاه‌دادهٔ نصب را از دست داده، شناسهٔ نصب عوض می‌شود و لایسنس قبلی روی نصب جدید "
                  "کار نمی‌کند؛ در این حالت نوع درخواست را «Rehost» بزنید.")

# ---------------------------------------------------------------- ۴
g.add_page()
g.h1("گام ۲ — بررسی هویت و تأیید درخواست", 4)
g.steps([
    "از صف درخواست‌ها (/Requests) ردیف مورد نظر را باز کنید: /Requests/Details?id=N.",
    "اگر لازم است وضعیت را به «در بررسی» ببرید و یادداشت داخلی بنویسید.",
    "در بخش «بررسی هویت» (/Requests/Identity) روش تأیید هویت را ثبت کنید: تماس تلفنی، ایمیل، "
    "پیامک یا حضوری. این رکورد در پرونده و در ممیزی می‌ماند.",
    "اگر اطلاعات ناقص است، پیام «نیاز به اطلاعات» بفرستید (NeedsInfo) — تا پاسخ مشتری پرونده باز می‌ماند.",
    "با دکمهٔ «تأیید»، درخواست به وضعیت Approved می‌رود. از این لحظه دکمهٔ پیش‌نویس و سپس امضا فعال است.",
])
g.callout("info", "قاعدهٔ سخت‌گیرانه: امضا فقط از Approved و فقط با نقش Signer یا Admin. تغییر هر "
                  "فیلد کلیدی بعد از امضا ممکن نیست؛ برای اصلاح باید لایسنس جدید صادر شود.")

# ---------------------------------------------------------------- ۵
g.add_page()
g.h1("گام ۳ — ساخت پیش‌نویس لایسنس", 5)
g.p("مسیر: /Requests/Draft?id=N — پیش‌نویس، «دستور ساخت» لایسنس است و مبنای امضا همان پروفایل "
    "ذخیره‌شده است، نه ورودی لحظه‌ای فرم.")
g.table(
    ["فیلد", "مقادیر مجاز", "نکته"],
    [
        ["بستهٔ آماده", "FIN | HR | WAREHOUSE | OFFICE", "میان‌بُر انتخاب چند ماژول"],
        ["ماژول‌های صریح", "کد ماژول‌ها", "در صورت نیاز، علاوه بر بسته"],
        ["حالت مدت", "months | until | perpetual", "ماه‌شمار / تاریخ پایان شمسی / دائمی"],
        ["مدت یا تاریخ", "مثلاً ۱۲ ماه یا ۱۴۰۶/۱۲/۲۹", "تاریخ‌ها شمسی و به وقت تهران"],
        ["به‌روزرسانی تا", "تاریخ شمسی", "جدا از اعتبار اجرا؛ برای دورهٔ آپدیت"],
        ["حالت استقرار", "OnPrem | Saas", "باید با نصب مشتری یکی باشد"],
        ["حالت کاربر", "Named | Concurrent | None", "همراه با سقف کاربر و کاربران مستثنا"],
        ["دامنه", "Global | Modules", "اعمال سقف روی همه یا ماژول‌های انتخابی"],
    ],
    widths=[42, 58, 78],
)
g.steps([
    "مقادیر را پر کنید و «پیش‌نمایش ابزار» را بزنید: ابزار LicenseTool بدون امضا خروجی JSON را نشان می‌دهد.",
    "خروجی پیش‌نمایش را با درخواست مشتری مقایسه کنید (شناسهٔ نصب و نام مشتری باید یکی باشند).",
    "دکمهٔ «ذخیره» را بزنید. پروفایل در پرونده ذخیره می‌شود و در مراحل بعد قابل بازبینی است.",
])
g.callout("warn", "مقدار deployment فقط OnPrem یا Saas است؛ نوشتن OnPremise باعث خطای ابزار (کد ۲) می‌شود.")

# ---------------------------------------------------------------- ۶
g.add_page()
g.h1("گام ۴ — امضا و صدور فایل .lic", 6)
g.p("مسیر: /Requests/Sign?id=N — حساس‌ترین گام. اینجا برای اولین بار کلید خصوصی وارد می‌شود.")
g.steps([
    "صفحه را باز کنید. پنل وضعیت کلیدها و ابزار را نشان می‌دهد؛ اگر چیزی پیدا نشود، همان‌جا نوشته می‌شود.",
    "Key ID را وارد کنید (مثلاً sup-2026a). باید با شناسهٔ کلیدی که مسیرش را داده‌اید یکی باشد.",
    "گذرواژهٔ کلید خصوصی را وارد کنید. اگر کلید بدون گذرواژه است، خالی بگذارید.",
    "تیک «پیش‌نویس و خلاصهٔ نهایی را بازبینی کردم» را بزنید — بدون آن امضا انجام نمی‌شود.",
    "اگر نصب قبلاً لایسنس فعال دارد و می‌خواهید نسخهٔ قدیمی باطل شود، تیک «لایسنس‌های جاری این نصب "
    "فوگرفته شوند» را بزنید.",
    "دکمهٔ «امضا و صدور» را بزنید.",
])
g.h2("پشت صحنه چه اتفاقی می‌افتد؟")
g.bullets([
    "ابزار LicenseTool با آرگومان‌های issue ساخته‌شده اجرا می‌شود و فایل JSON را با کلید خصوصی امضا می‌کند.",
    "پنل بلافاصله فایل را با کلید عمومی بازبینی می‌کند: امضا معتبر است؟ شناسهٔ نصب می‌خواند؟",
    "اگر بازبینی رد شود، فایل دور ریخته می‌شود و پرونده در وضعیت قبلی می‌ماند (هیچ نیمه‌کاری ذخیره نمی‌شود).",
    "در صورت موفقیت: رکورد لایسنس با سریال، کلید، هش SHA256 و تاریخ‌ها ثبت می‌شود، وضعیت درخواست "
    "«صادرشده» می‌شود و رخداد SIGN در ممیزی زنجیره‌هش می‌نشیند.",
    "فایل در پوشهٔ کاری داده‌ها ذخیره می‌شود: data/licenses/<سریال>.lic",
])
g.h2("بعد از امضا چه چیزی نباید تغییر کند؟")
g.bullets([
    "فایل .lic دست‌نخورده بماند؛ هر تغییری امضا را باطل می‌کند (مشتری خطای LICENSE_SIGNATURE_INVALID می‌گیرد).",
    "سریال لایسنس تکراری نمی‌شود؛ پنل و نرم‌افزار مشتری هر دو سریال‌های تکراری را رد می‌کنند.",
    "گذرواژهٔ کلید را جایی ننویسید — نه در ایمیل، نه در تیکت، نه در فایل کنار لایسنس.",
])
g.callout("ok", "خروجی این گام: یک ردیف لایسنس در /Licenses با سریال یکتا و یک فایل .lic قابل تحویل.")

# ---------------------------------------------------------------- ۷
g.add_page()
g.h1("گام ۵ — تحویل به مشتری و بستن پرونده", 7)
g.p("مسیر: /Requests/Deliver?id=N — سه روش تحویل وجود دارد؛ هر سه در پرونده ثبت می‌شوند.")
g.table(
    ["روش", "چه می‌کند", "چه زمانی"],
    [
        ["پیش‌نویس .eml", "یک فایل ایمیل آمادهٔ ارسال (با متن قالب) در outbox می‌سازد", "وقتی SMTP ندارید یا باید از Outlook بفرستید"],
        ["ارسال ایمیل (SMTP)", "مستقیم از پنل با سرور SMTP تنظیم‌شده می‌فرستد", "وقتی SMTP در تنظیمات پیکربندی شده است"],
        ["ثبت تحویل دستی", "گیرنده و یادداشت را ثبت می‌کند", "وقتی لایسنس را با تلگرام/فلش/تلفن می‌دهید"],
    ],
    widths=[40, 84, 54],
)
g.steps([
    "روش تحویل را انتخاب و اجرا کنید. اگر SMTP تنظیم نشده باشد، دکمهٔ ارسال غیرفعال است و پیام روشن نشان داده می‌شود.",
    "گیرنده را دقیق ثبت کنید؛ همین رکورد مبنای پیگیری بعدی است.",
    "وضعیت خودکار به Delivered می‌رود. پس از اطمینان از فعال‌سازی موفق در سمت مشتری، پرونده را ببندید (Closed).",
])
g.h2("چیزی که همراه لایسنس برای مشتری می‌فرستید")
g.bullets([
    "فایل .lic (همان سریال، بدون تغییر)",
    "یک جمله: «فایل را در بخش لایسنس نرم‌افزار بارگذاری کنید؛ نیازی به ری‌استارت نیست.»",
    "تاریخ اعتبار و تاریخ یادآوری تمدید (۶۰ روز قبل از انقضا)",
    "در صورت نیاز، دستور جایگزین (API) برای بارگذاری — در بخش ۸ آمده است",
])

# ---------------------------------------------------------------- ۸
g.add_page()
g.h1("سمت مشتری: گرفتن درخواست، فعال‌سازی و استفاده", 8)
g.h2("۸.۱ گرفتن فایل درخواست")
g.p("نرم‌افزار مشتری، شناسهٔ نصب خودش را می‌داند. دو راه برای گرفتن فایل درخواست وجود دارد:")
g.code("# option 1: the \"Download request file\" button in the customer app\n"
       "# option 2: call the API on the customer server directly\n"
       "curl -OJ https://<customer-server>/api/license/request-file")
g.p("خروجی: فایلی به نام license-request-XXXXXXXX.json که فقط شناسهٔ نصب، نام مشتری، نام ماشین و "
    "نسخهٔ محصول در آن است. این فایل برای مشتری هیچ راز یا اطلاعات حساسی ندارد و برای شما «چالش» است "
    "که لایسنس را به همان نصب گره می‌زند.", size=9.5)
g.h2("۸.۲ ارسال به پشتیبانی")
g.p("مشتری همین فایل را با ایمیل/تیکت برای شما می‌فرستد. شما آن را در پنل ثبت می‌کنید (گام ۱).")
g.h2("۸.۳ بارگذاری لایسنس امضاشده (فعال‌سازی)")
g.code("# via UI: License section -> \"Upload license file\" -> pick the .lic -> confirm\n\n"
       "# via API:\n"
       "curl -F 'file=@customer.lic' https://<customer-server>/api/license/activate")
g.p("نرم‌افزار به‌ترتیب این کارها را انجام می‌دهد:", size=9.5)
g.bullets([
    "بازبینی امضا با کلید عمومی داخل بیلد (بدون هیچ تماس با اینترنت یا سرور پشتیبانی).",
    "اعتبارسنجی ساختار سند و تطابق installationId با شناسهٔ نصب همین سرور.",
    "بررسی تکراری نبودن سریال نسبت به لایسنس‌های فعال.",
    "ذخیرهٔ لایسنس، ابطال کش و اعمال فوری — بدون ری‌استارت سرویس.",
])
g.callout("ok", "پس از فعال‌سازی، ماژول‌های خریداری‌شده بدون ری‌استارت باز می‌شوند. اگر ماژولی باز نشد، "
                "اول «وضعیت لایسنس» را ببینید؛ معمولاً آن ماژول در پیش‌نویس انتخاب نشده است.")
g.h2("۸.۴ بررسی وضعیت")
g.code("curl https://<customer-server>/api/license/status\n"
       "curl https://<customer-server>/api/license/health\n"
       "# on the customer machine, show the keys embedded in this build:\n"
       "LicenseCheck --keys")
g.h2("۸.۵ مشتری از کدام امکانات استفاده می‌کند؟")
g.table(
    ["وضعیت", "یعنی چه", "کار مشتری"],
    [
        ["فعال", "امضا معتبر و تاریخ اعتبار نگذشته", "استفادهٔ عادی"],
        ["هشدار تمدید", "کمتر از ۶۰ روز تا انقضا", "درخواست تمدید بفرستد"],
        ["منقضی", "تاریخ پایان گذشته", "نرم‌افزار محدود می‌شود؛ لایسنس جدید لازم است"],
        ["شناسهٔ نصب ناهمخوان", "فایل برای سرور دیگری صادر شده", "درخواست Rehost"],
        ["امضای نامعتبر", "فایل دست‌کاری شده یا کلید ناشناخته", "فایل را دوباره از پشتیبانی بگیرد"],
    ],
    widths=[36, 76, 66],
)

# ---------------------------------------------------------------- ۹
g.add_page()
g.h1("تمدید، ارتقا و «فوگرفتن» لایسنس قبلی", 9)
g.steps([
    "در صفحهٔ «تمدیدها» (/Renewals) لایسنس‌های نزدیک به انقضا (۶۰ روز) فهرست می‌شوند.",
    "برای تمدید، مشتری معمولاً فایل درخواست تازه نمی‌دهد؛ همان شناسهٔ نصب کافی است. درخواست جدید با نوع Renewal ثبت کنید.",
    "در پیش‌نویس، مدت تازه (مثلاً ۱۲ ماه از امروز یا تاریخ پایان جدید) و همان ماژول‌ها را بگذارید.",
    "هنگام امضا، تیک «لایسنس‌های جاری این نصب فوگرفته شوند» را بزنید تا نسخهٔ قبلی باطل شود.",
    "لایسنس جدید را با همان روش تحویل بفرستید؛ مشتری فقط فایل جدید را بارگذاری می‌کند (بدون حذف قبلی).",
])
g.h2("ارتقا (ماژول یا سقف کاربر بیشتر)")
g.bullets([
    "درخواست نوع AddModule یا QuotaPack ثبت کنید.",
    "در پیش‌نویس، ماژول‌های قبلی را هم نگه دارید؛ لایسنس جدید جایگزین کامل قبلی است، نه مکمل آن.",
    "پس از بارگذاری، ماژول تازه بی‌درنگ فعال می‌شود.",
])
g.h2("جابه‌جایی سرور (Rehost)")
g.p("اگر شناسهٔ نصب عوض شده، لایسنس قبلی کار نمی‌کند. مشتری فایل درخواست تازه می‌فرستد؛ شما با "
    "نوع Rehost و با تیک فوگرفتن، لایسنس تازه صادر می‌کنید.")

# ---------------------------------------------------------------- ۱۰
g.add_page()
g.h1("خطاهای رایج و عیب‌یابی", 10)
g.table(
    ["نشانه", "علت معمول", "راه‌حل"],
    [
        ["«امضا آماده نیست: کلید خصوصی پیدا نشد»", "Signer:PrivateKeyPath خالی یا فایل جابه‌جا شده", "مسیر را درست کنید؛ در حالت F5، کلید را کنار پوشهٔ کلیدها بگذارید"],
        ["«ابزار LicenseTool پیدا نشد»", "بیلد/کپی ابزار انجام نشده", "پروژه را دوباره Build کنید (هدف BuildAndCopyLicenseTool) یا ToolPath را بدهید"],
        ["ابزار کد ۲ برمی‌گرداند", "مقدار غیرمجاز در پیش‌نویس یا نبود presets.json", "پیام خود ابزار را بخوانید؛ OnPremise را OnPrem کنید"],
        ["«پیش‌نویس ذخیره نشده»", "مرحلهٔ Save انجام نشده", "در صفحهٔ Draft دوباره ذخیره کنید و مقادیر را ببینید"],
        ["مشتری: LICENSE_SIGNATURE_INVALID", "فایل تغییر کرده یا کلید عمومی در بیلد نیست", "فایل را از پنل دوباره دانلود کنید"],
        ["مشتری: INSTALLATION_MISMATCH", "لایسنس برای نصب دیگری صادر شده", "Rehost با فایل درخواست تازه"],
        ["مشتری: LICENSE_FILE_TOO_LARGE", "فایل بیش از ۲۵۶ کیلوبایت", "فایل اشتباه است؛ همان .lic صادرشده را بفرستید"],
        ["ارسال ایمیل ممکن نیست", "SMTP تنظیم نشده", "از پیش‌نویس .eml یا ثبت تحویل دستی استفاده کنید"],
        ["دکمهٔ امضا دیده نمی‌شود", "نقش Operator یا وضعیت غیر Approved", "با Signer/Admin وارد شوید و درخواست را تأیید کنید"],
    ],
    widths=[52, 60, 66],
)

# ---------------------------------------------------------------- ۱۱
g.add_page()
g.h1("چک‌لیست امنیتی و کاری", 11)
g.h2("قبل از هر امضا")
g.bullets([
    "شناسهٔ نصب در پیش‌نویس با فایل درخواست مشتری یکی است.",
    "مدت و ماژول‌ها با سفارش/فاکتور یکی است؛ تاریخ‌ها شمسی و به وقت تهران.",
    "حالت استقرار (OnPrem/Saas) با نصب مشتری می‌خواند.",
    "اگر تمدید است، تیک فوگرفتن زده شده تا دو لایسنس فعال هم‌زمان نماند.",
])
g.h2("امنیت کلید و رازها")
g.bullets([
    "کلید خصوصی فقط روی ماشین پشتیبانی، با دسترسی ۶۰۰ و نسخهٔ پشتیبان آفلاین.",
    "گذرواژهٔ کلید و رمز SMTP فقط از متغیر محیطی؛ هرگز در فایل پیکربندی، تیکت یا ایمیل.",
    "فایل .lic یک سند امضاشده است؛ ویرایش دستی آن را باطل می‌کند.",
    "پنل فقط روی localhost بالا بیاید؛ برای دسترسی دور، از تونل امن استفاده کنید.",
])
g.h2("بعد از هر امضا و تحویل")
g.bullets([
    "از صفحهٔ «ممیزی» یک‌بار «بررسی زنجیره» بزنید تا سالم بودن زنجیرهٔ هش تأیید شود.",
    "از صفحهٔ «پشتیبان» نسخهٔ پشتیبان بگیرید (پیش از هر تغییر بزرگ هم).",
    "پرونده را بعد از تأیید فعال‌سازی در سمت مشتری ببندید (Closed).",
    "تاریخ تمدید را در تقویم کاری ثبت کنید (۶۰ روز قبل از انقضا).",
])
g.callout("info", "پنل پایگاه‌دادهٔ مستقل خودش را دارد (SQLite یا SQL Server) و هیچ ارتباطی با پایگاه‌دادهٔ "
                  "نرم‌افزار مشتری ندارد؛ پس امضای لایسنس هیچ ریسکی برای داده‌های مشتری ایجاد نمی‌کند.")

# ---------------------------------------------------------------- پیوست
g.add_page()
g.h1("پیوست: مسیرها،نقاط پایانی API و دستورهای CLI", "پ")
g.h2("صفحه‌های پنل")
g.table(
    ["مسیر", "کار"],
    [
        ["/", "داشبورد: شمارنده‌ها، وضعیت امضا، کارهای باز"],
        ["/Requests", "صف درخواست‌ها"],
        ["/Requests/Ingest", "ثبت درخواست از JSON/آپلود/دستی"],
        ["/Requests/Details?id=N", "پروندهٔ درخواست"],
        ["/Requests/Identity?id=N", "ثبت بررسی هویت"],
        ["/Requests/Draft?id=N", "پیش‌نویس لایسنس + پیش‌نمایش ابزار"],
        ["/Requests/Sign?id=N", "امضا و صدور (نقش Signer/Admin)"],
        ["/Requests/Deliver?id=N", "تحویل: .eml، SMTP، دستی"],
        ["/Licenses · /Renewals · /Customers", "لایسنس‌ها، تمدیدها، مشتریان"],
        ["/Audit · /Backup · /Settings", "ممیزی زنجیره‌هش، پشتیبان، تنظیمات"],
    ],
    widths=[62, 116],
)
g.h2("نقاط پایانی APIی نرم‌افزار مشتری (روی سرور خودش)")
g.table(
    ["متد و مسیر", "کار"],
    [
        ["GET /api/license/request-file", "دانلود فایل درخواست"],
        ["POST /api/license/activate", "بارگذاری .lic (multipart، فیلد file)"],
        ["GET /api/license/status", "وضعیت کامل لایسنس و ماژول‌ها"],
        ["GET /api/license/health", "سلامت لایسنس برای مانیتورینگ"],
        ["GET /api/license/installation", "شناسهٔ نصب و اطلاعات ماشین"],
        ["GET /api/license/grants", "فهرست لایسنس‌های اعمال‌شده"],
    ],
    widths=[70, 108],
)
g.h2("دستورهای CLI")
g.code("# create a key pair (outside the repo)\n"
       "LICENSETOOL_KEY_PASSWORD='<pass>' dotnet LicenseTool.dll keygen --key-id sup-2026a --out-dir /secure/keys\n\n"
       "# issue directly without the panel (for testing)\n"
       "LICENSETOOL_KEY_PASSWORD='<pass>' dotnet LicenseTool.dll issue \\\n"
       "    --request request.json --profile profile.json --key-id sup-2026a \\\n"
       "    --private-key /secure/keys/sup-2026a.private.pem --out out.lic --yes\n\n"
       "# verify the signature with the public key\n"
       "dotnet LicenseTool.dll verify --license out.lic --public-key /secure/keys/sup-2026a.pem\n\n"
       "# on the customer machine: keys known to this build\n"
       "LicenseCheck --keys")
g.h2("پرونده‌های کاری پنل")
g.table(
    ["مسیر", "محتوا"],
    [
        ["data/support.db", "پایگاه‌دادهٔ پنل (درخواست‌ها، لایسنس‌ها، ممیزی)"],
        ["data/licenses/<سریال>.lic", "فایل‌های امضاشده"],
        ["data/outbox/", "پیش‌نویس‌های .eml"],
        ["data/backups/", "پشتیبان‌های پایگاه‌داده"],
        ["data/keys/", "کلیدهای Data Protection خود پنل (نه کلید امضا)"],
    ],
    widths=[62, 116],
)
g.ln(2)
g.callout("ok", "این راهنما همراه خود پروژه در مخزن است؛ اگر مراحل پنل تغییر کرد، همین سند را به‌روز کنید.")

g.output(OUT)
print("PDF ساخته شد:", OUT)
