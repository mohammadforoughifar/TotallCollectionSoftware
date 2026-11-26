"""پیش‌نمایش ایستای «کاردکس ریالی کالا» پس از رفع اشکال.

این فایل بخشی از برنامه نیست؛ فقط برای بازبینی خروجی گزارش است: همان مارک‌آپ و
همان کلاس‌های CSS تولیدی (bootstrap + آیکن + app.css) درون فایل جاسازی می‌شود،
بنابراین چیزی که در مرورگر دیده می‌شود همان چیزی است که کاربر در برنامه می‌بیند.

    python3 inventory/tests/RialKardexReport/build_preview.py
"""
from pathlib import Path
import base64
import re

HERE = Path(__file__).resolve().parent
SRC = HERE.parents[1] / "src/Inventory.Client/wwwroot"


def data_uri(path: Path) -> str:
    return "data:font/woff2;base64," + base64.b64encode(path.read_bytes()).decode("ascii")


def strip_font_faces(css: str) -> str:
    return re.sub(r"@font-face\s*\{[^}]*\}", "", css, flags=re.S)


def icon_font_css() -> str:
    css = (SRC / "icons/bootstrap-icons.css").read_text(encoding="utf-8")
    return re.sub(r"src:\s*url\([^)]*\)[^;]*;",
                  f"src: url('{data_uri(SRC / 'icons/fonts/bootstrap-icons.woff2')}') format('woff2');",
                  css, count=1)


ROWS = [
    # تاریخ، سند، نوع، طرف‌حساب/شرح، وارده(مقدار/فی/مبلغ)، صادره(مقدار/فی/مبلغ)، مانده(مقدار/میانگین/ارزش)
    ("1405/07/02", "RC-0001", "رسید خرید — انبار مرکزی", "شرکت پخش آریا", "خرید اولیه لپ‌تاپ",
     "10", "42,000,000", "420,000,000", "", "", "", "10", "42,000,000", "420,000,000"),
    ("1405/07/11", "SL-0007", "حواله فروش — انبار مرکزی", "آقای محمدی", "فاکتور فروش ۱۰۴۲",
     "", "", "", "3", "42,000,000", "126,000,000", "7", "42,000,000", "294,000,000"),
    ("1405/07/18", "SL-0011", "حواله فروش — انبار مرکزی", "خانم رضایی", "فاکتور فروش ۱۰۵۸",
     "", "", "", "2", "42,000,000", "84,000,000", "5", "42,000,000", "210,000,000"),
    ("1405/08/01", "RC-0009", "رسید خرید — انبار مرکزی", "شرکت پخش آریا", "خرید مکمل با بهای بالاتر",
     "5", "46,000,000", "230,000,000", "", "", "", "10", "44,000,000", "440,000,000"),
    ("1405/08/06", "SR-0003", "برگشت از فروش — انبار مرکزی", "آقای محمدی", "برگشت یک عدد",
     "1", "45,000,000", "45,000,000", "", "", "", "11", "44,090,909", "485,000,000"),
]

ROW_TMPL = """<tr>
  <td class="text-center num text-secondary fs-8">{idx}</td>
  <td class="num text-secondary fs-8">{date}</td>
  <td><span class="fw-700 text-dark num">{doc}</span><div class="text-secondary fs-9">{doctype}</div></td>
  <td class="fs-8"><div class="fw-700 text-dark">{party}</div><div class="text-secondary fs-9">{desc}</div></td>
  <td class="text-center num text-emerald fw-700 border-start">{inq}</td>
  <td class="text-center num text-secondary fs-8">{inp}</td>
  <td class="text-center num text-emerald fw-700 border-end">{inv}</td>
  <td class="text-center num text-rose fw-700">{outq}</td>
  <td class="text-center num text-secondary fs-8">{outp}</td>
  <td class="text-center num text-rose fw-700 border-end">{outv}</td>
  <td class="text-center num fw-800 text-indigo">{balq}</td>
  <td class="text-center num text-secondary fs-8">{balavg}</td>
  <td class="text-center num fw-800 text-indigo">{balv}</td>
</tr>"""


def page() -> str:
    rows = "\n".join(
        ROW_TMPL.format(idx="۱۲۳۴۵"[i], date=r[0], doc=r[1], doctype=r[2], party=r[3], desc=r[4],
                        inq=r[5], inp=r[6], inv=r[7], outq=r[8], outp=r[9], outv=r[10],
                        balq=r[11], balavg=r[12], balv=r[13])
        for i, r in enumerate(ROWS))

    return f"""<!DOCTYPE html>
<html lang="fa" dir="rtl">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>پیش‌نمایش کاردکس ریالی کالا</title>
<style>/* CSS تولیدی پروژه (bootstrap + آیکن + app.css) درون فایل جاسازی شده است */</style>
<style>
body {{ background: #f6f8fb; padding: 18px; }}
.demo-note {{ background:#eff6ff; border:1px solid #bfdbfe; color:#1e3a8a; border-radius:12px;
  padding:.7rem 1rem; font-size:.8rem; margin-bottom:14px; line-height:1.7; }}
</style>
</head>
<body>
<div class="demo-note">
  <b>پیش‌نمایش فقط برای بازبینی —</b>
  کاردکس ریالی حالا گردش «سامانهٔ فروشگاهی» و «دفتر انبارداری» را با هم می‌خواند و ماندهٔ
  مقداری/ریالی هر ردیف را به‌ترتیب رخداد می‌سازد. ردیف‌های فروشگاهی لینک سند انبار ندارند
  و به‌صورت متن ساده نمایش داده می‌شوند.
</div>
<div class="fin-page-container">
  <div class="fin-header mb-4">
    <div class="d-flex flex-column flex-md-row justify-content-between align-items-start align-items-md-center gap-3">
      <div class="d-flex align-items-center gap-3">
        <div class="fin-header-icon bg-indigo-gradient"><i class="bi bi-currency-exchange"></i></div>
        <div>
          <div class="d-flex align-items-center gap-2">
            <h1 class="h4 fw-800 text-dark mb-0">کاردکس ریالی و مقداری کالا</h1>
            <span class="badge rounded-pill fin-badge-indigo">حسابداری انبار</span>
          </div>
          <div class="text-secondary fs-8 mt-1">
            رهگیری گردش ورودی، خروجی، فی تمام‌شده و ارزش دفتری مانده کالا بر پایه اسناد قطعی
          </div>
        </div>
      </div>
      <button class="btn btn-outline-secondary fin-btn-pill"><i class="bi bi-printer-fill me-1"></i> چاپ رسمی کاردکس</button>
    </div>
  </div>

  <div class="fin-card mb-4">
    <div class="fin-card-body p-3">
      <div class="row g-2.5 align-items-end">
        <div class="col-12 col-md-4">
          <label class="form-label fs-8 fw-700 text-dark mb-1.5">انتخاب کالا <span class="text-danger">*</span></label>
          <input class="form-control" value="LP-0007 — لپ‌تاپ HP 250 G8" />
        </div>
        <div class="col-6 col-md-3">
          <label class="form-label fs-8 fw-700 text-dark mb-1.5">انبار</label>
          <select class="form-select fin-input"><option>انبار مرکزی</option></select>
        </div>
        <div class="col-6 col-md-2">
          <label class="form-label fs-8 fw-700 text-dark mb-1.5">از تاریخ</label>
          <input class="form-control num" value="1405/07/01" />
        </div>
        <div class="col-6 col-md-2">
          <label class="form-label fs-8 fw-700 text-dark mb-1.5">تا تاریخ</label>
          <input class="form-control num" value="1405/08/30" />
        </div>
        <div class="col-6 col-md-1">
          <button class="btn btn-primary fin-btn-pill w-100"><i class="bi bi-search"></i></button>
        </div>
      </div>
    </div>
  </div>

  <div class="fin-card mb-4">
    <div class="fin-card-body p-3.5">
      <div class="d-flex flex-wrap align-items-center justify-content-between gap-3">
        <div class="d-flex align-items-center gap-3">
          <div class="fin-avatar bg-indigo-subtle text-indigo" style="width:48px;height:48px;"><i class="bi bi-box-seam fs-4"></i></div>
          <div>
            <div class="fw-800 text-dark fs-6">لپ‌تاپ HP 250 G8</div>
            <div class="text-secondary fs-8 num mt-0.5">کد: LP-0007 — واحد سنجش: عدد — انبار: انبار مرکزی</div>
          </div>
        </div>
        <span class="badge bg-indigo-subtle text-indigo rounded-pill px-3 py-2 fs-8 fw-700">
          <i class="bi bi-calculator me-1"></i> روش قیمت‌گذاری: میانگین موزون
        </span>
      </div>
    </div>
  </div>

  <div class="row g-3 mb-4">
    <div class="col-12 col-sm-6 col-xl-3">
      <div class="fin-kpi-card border-slate">
        <div class="d-flex justify-content-between align-items-start">
          <div>
            <span class="fin-kpi-title text-slate">مانده ابتدای دوره</span>
            <div class="fin-kpi-value text-dark num mt-1">۱۰ <small class="fs-9 text-secondary">عدد</small></div>
            <div class="fin-kpi-unit">ارزش: ۴۲۰,۰۰۰,۰۰۰ ریال</div>
          </div>
          <div class="fin-kpi-icon-box bg-slate-subtle text-slate"><i class="bi bi-hourglass-top"></i></div>
        </div>
        <div class="fin-kpi-footer mt-3 pt-2"><span class="text-secondary fs-9">موجودی افتتاحیه بازه</span></div>
      </div>
    </div>
    <div class="col-12 col-sm-6 col-xl-3">
      <div class="fin-kpi-card border-emerald">
        <div class="d-flex justify-content-between align-items-start">
          <div>
            <span class="fin-kpi-title text-emerald">جمع کل وارده انبار</span>
            <div class="fin-kpi-value text-emerald num mt-1">۶ <small class="fs-9 text-secondary">عدد</small></div>
            <div class="fin-kpi-unit">ارزش: ۲۷۵,۰۰۰,۰۰۰ ریال</div>
          </div>
          <div class="fin-kpi-icon-box bg-emerald-subtle text-emerald"><i class="bi bi-box-arrow-in-down"></i></div>
        </div>
        <div class="fin-kpi-footer mt-3 pt-2"><span class="badge bg-emerald-subtle text-emerald rounded-pill px-2.5">رسیدها و برگشت‌ها</span></div>
      </div>
    </div>
    <div class="col-12 col-sm-6 col-xl-3">
      <div class="fin-kpi-card border-rose">
        <div class="d-flex justify-content-between align-items-start">
          <div>
            <span class="fin-kpi-title text-rose">جمع کل صادره انبار</span>
            <div class="fin-kpi-value text-rose num mt-1">۵ <small class="fs-9 text-secondary">عدد</small></div>
            <div class="fin-kpi-unit">ارزش: ۲۱۰,۰۰۰,۰۰۰ ریال</div>
          </div>
          <div class="fin-kpi-icon-box bg-rose-subtle text-rose"><i class="bi bi-box-arrow-up"></i></div>
        </div>
        <div class="fin-kpi-footer mt-3 pt-2"><span class="badge bg-rose-subtle text-rose rounded-pill px-2.5">حواله‌های خروج و فروش</span></div>
      </div>
    </div>
    <div class="col-12 col-sm-6 col-xl-3">
      <div class="fin-kpi-card border-indigo">
        <div class="d-flex justify-content-between align-items-start">
          <div>
            <span class="fin-kpi-title text-indigo">مانده پایان دوره</span>
            <div class="fin-kpi-value text-indigo num mt-1">۱۱ <small class="fs-9 text-secondary">عدد</small></div>
            <div class="fin-kpi-unit">ارزش دفتری: ۴۸۵,۰۰۰,۰۰۰ ریال</div>
          </div>
          <div class="fin-kpi-icon-box bg-indigo-subtle text-indigo"><i class="bi bi-boxes"></i></div>
        </div>
        <div class="fin-kpi-footer mt-3 pt-2"><span class="badge bg-indigo-subtle text-indigo rounded-pill px-2.5">موجودی نهایی انبار</span></div>
      </div>
    </div>
  </div>

  <div class="fin-card">
    <div class="fin-card-header d-flex justify-content-between align-items-center p-3 border-bottom">
      <div class="d-flex align-items-center gap-2">
        <i class="bi bi-table text-primary fs-5"></i>
        <span class="fw-800 text-dark">ریز تراکنش‌ها و گردش ریالی کالا</span>
      </div>
      <span class="badge bg-indigo-subtle text-indigo rounded-pill px-3 py-1.5 num fs-8">۵ گردش ثبت‌شده</span>
    </div>
    <div class="fin-card-body p-0">
      <div class="table-responsive">
        <table class="table table-hover align-middle mb-0 fin-table">
          <thead class="bg-light">
            <tr>
              <th rowspan="2" class="text-secondary fs-8 fw-700 py-3 text-center" style="width:40px">#</th>
              <th rowspan="2" class="text-secondary fs-8 fw-700 py-3">تاریخ سند</th>
              <th rowspan="2" class="text-secondary fs-8 fw-700 py-3">شماره و نوع سند</th>
              <th rowspan="2" class="text-secondary fs-8 fw-700 py-3">طرف‌حساب و شرح</th>
              <th colspan="3" class="text-center text-emerald fs-8 fw-700 py-2 border-start border-end" style="background:#ecfdf5">وارده (ورود به انبار)</th>
              <th colspan="3" class="text-center text-rose fs-8 fw-700 py-2 border-end" style="background:#fff1f2">صادره (خروج از انبار)</th>
              <th colspan="3" class="text-center text-indigo fs-8 fw-700 py-2" style="background:#eef2ff">مانده مقداری و ریالی</th>
            </tr>
            <tr>
              <th class="text-center text-emerald fs-8 fw-600 border-start" style="background:#f0fdf4">مقدار</th>
              <th class="text-center text-emerald fs-8 fw-600" style="background:#f0fdf4">فی (ریال)</th>
              <th class="text-center text-emerald fs-8 fw-600 border-end" style="background:#f0fdf4">مبلغ کل (ریال)</th>
              <th class="text-center text-rose fs-8 fw-600" style="background:#fff1f2">مقدار</th>
              <th class="text-center text-rose fs-8 fw-600" style="background:#fff1f2">فی (ریال)</th>
              <th class="text-center text-rose fs-8 fw-600 border-end" style="background:#fff1f2">مبلغ کل (ریال)</th>
              <th class="text-center text-indigo fs-8 fw-600" style="background:#f5f7ff">مقدار مانده</th>
              <th class="text-center text-indigo fs-8 fw-600" style="background:#f5f7ff">میانگین فی</th>
              <th class="text-center text-indigo fs-8 fw-600" style="background:#f5f7ff">ارزش مانده</th>
            </tr>
          </thead>
          <tbody>
            <tr class="bg-light fw-700">
              <td class="text-center num text-secondary">—</td>
              <td colspan="3" class="text-dark">مانده ابتدای دوره (افتتاحیه)</td>
              <td colspan="6" class="border-start border-end"></td>
              <td class="text-center num fw-800 text-indigo">۱۰</td>
              <td class="text-center num text-secondary">۴۲,۰۰۰,۰۰۰</td>
              <td class="text-center num fw-800 text-indigo">۴۲۰,۰۰۰,۰۰۰</td>
            </tr>
            {rows}
          </tbody>
          <tfoot class="bg-light fw-800">
            <tr>
              <td colspan="4" class="text-end text-dark py-3">جمع کل گردش دوره:</td>
              <td class="text-center num text-emerald border-start">۶</td>
              <td></td>
              <td class="text-center num text-emerald border-end">۲۷۵,۰۰۰,۰۰۰</td>
              <td class="text-center num text-rose">۵</td>
              <td></td>
              <td class="text-center num text-rose border-end">۲۱۰,۰۰۰,۰۰۰</td>
              <td class="text-center num text-indigo fs-7">۱۱</td>
              <td></td>
              <td class="text-center num text-indigo fs-7">۴۸۵,۰۰۰,۰۰۰</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </div>
  </div>
</div>
</body>
</html>
"""


def main():
    reg = data_uri(SRC / "fonts/Vazirmatn-Regular.woff2")
    bold = data_uri(SRC / "fonts/Vazirmatn-Bold.woff2")
    faces = (f"@font-face {{ font-family:'Vazirmatn'; src:url('{reg}') format('woff2'); font-weight:400; font-display:swap; }}"
             f"@font-face {{ font-family:'Vazirmatn'; src:url('{bold}') format('woff2'); font-weight:700; font-display:swap; }}")
    css = faces
    css += "\n/* ==== bootstrap ==== */\n" + strip_font_faces((SRC / "css/bootstrap.min.css").read_text(encoding="utf-8"))
    css += "\n/* ==== bootstrap-icons ==== */\n" + icon_font_css()
    css += "\n/* ==== app.css ==== */\n" + strip_font_faces((SRC / "css/app.css").read_text(encoding="utf-8"))

    html = page().replace(
        "/* CSS تولیدی پروژه (bootstrap + آیکن + app.css) درون فایل جاسازی شده است */", css, 1)
    out = HERE / "rial-kardex-preview.html"
    out.write_text(html, encoding="utf-8")
    leftovers = [u for u in re.findall(r"url\(([^)]*)\)", html) if "data:" not in u and "fonts/" in u]
    assert not leftovers, leftovers[:5]
    print(f"{out.name}: {out.stat().st_size:,} bytes")


if __name__ == "__main__":
    main()
