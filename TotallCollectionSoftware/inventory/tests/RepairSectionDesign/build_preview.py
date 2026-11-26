"""پیش‌نمایش ایستا برای «زبان طراحی یکپارچهٔ بخش تعمیرات».

هدف: نشان‌دادن چیدمان و استایل مشترکِ فرم‌های تعمیرات / گارانتی / طرح تعویض،
دقیقاً با همان کلاس‌های CSS تولیدی (app.css) — بدون نیاز به سرور، مرورگر Blazor
یا پایگاه‌داده. این فایل بخشی از اپ نیست؛ فقط برای بازبینی طراحی است.

    python3 inventory/tests/RepairSectionDesign/build_preview.py
"""
from pathlib import Path
import base64
import re
import shutil

HERE = Path(__file__).resolve().parent
INV = HERE.parents[1]
SRC = INV / "src/Inventory.Client/wwwroot"

SCRIPTS = f"{HERE}/../SalesVisit/build_standalone.py"
FALLBACK_STANDALONE = HERE / "../SalesVisit/visit-forms-proposal-standalone.html"


def data_uri(path: Path) -> str:
    return "data:font/woff2;base64," + base64.b64encode(path.read_bytes()).decode("ascii")


def strip_font_faces(css: str) -> str:
    return re.sub(r"@font-face\s*\{[^}]*\}", "", css, flags=re.S)


def icon_font_css() -> str:
    """فونت آیکن‌ها با مسیر نسبی تعریف شده؛ اینجا مطلق/جاسازی می‌شود."""
    css = (SRC / "icons/bootstrap-icons.css").read_text(encoding="utf-8")
    css = re.sub(r'src:\s*url\([^)]*\)[^;]*;',
                 f"src: url('{data_uri(SRC / 'icons/fonts/bootstrap-icons.woff2')}') format('woff2');",
                 css, count=1)
    return css


def body() -> str:
    device_card = """
            <div class="repair-device-card">
                <div class="repair-block-head">
                    <span class="repair-block-index">1</span>
                    <h6>دستگاه 1</h6>
                    <button type="button" class="btn btn-sm btn-light me-auto" title="حذف دستگاه">
                        <i class="bi bi-trash text-danger"></i>
                    </button>
                </div>
                <div class="row g-2">
                    <div class="col-md-4"><label class="form-label">نوع دستگاه *</label><input class="form-control" value="لپ‌تاپ" /></div>
                    <div class="col-md-4"><label class="form-label">برند و مدل</label><input class="form-control" value="Asus VivoBook" /></div>
                    <div class="col-md-4"><label class="form-label">سریال دستگاه</label><input class="form-control num" dir="ltr" value="SN-993201" /></div>
                    <div class="col-md-6"><label class="form-label">ایراد اعلامی مشتری</label><textarea class="form-control" rows="2">روشن نمی‌شود</textarea></div>
                    <div class="col-md-6"><label class="form-label">لوازم همراه</label><input class="form-control" placeholder="شارژر، کیف…" /></div>
                </div>
            </div>"""

    head = """
    <div class="page-head">
        <div>
            <h1>پذیرش تعمیرات</h1>
            <p class="lead">پذیرش دستگاه، کارها و قطعات مصرفی — وضعیت، تحویل و فاکتور</p>
        </div>
        <div class="d-flex flex-wrap gap-2 me-auto">
            <button class="btn btn-outline-primary"><i class="bi bi-bar-chart-line"></i> گزارش و نمودارها</button>
            <button class="btn btn-primary"><i class="bi bi-plus-lg"></i> پذیرش جدید</button>
        </div>
    </div>"""

    intake = f"""
    <div class="card mb-4"><div class="card-header"><i class="bi bi-tools text-primary"></i>
        <span class="card-title">فرم پذیرش تعمیرات — بازطراحی‌شده با زبان مشترک گارانتی</span></div>
        <div class="card-body">
        <div class="repair-form">
            <section class="repair-block">
                <div class="repair-block-head">
                    <i class="bi bi-clipboard2-check text-primary"></i>
                    <h6>اطلاعات مشترک پذیرش</h6>
                </div>
                <div class="row g-3">
                    <div class="col-md-4">
                        <label class="form-label">مشتری (صاحب همه دستگاه‌ها) <span class="text-danger">*</span></label>
                        <div class="d-flex gap-2 align-items-stretch">
                            <div class="flex-grow-1" style="min-width:0"><input class="form-control" value="آقای رضایی" /></div>
                            <button type="button" class="btn btn-new-party"><i class="bi bi-person-plus-fill"></i><span class="d-none d-sm-inline">جدید</span></button>
                        </div>
                        <div class="fs-8 text-muted mt-1 num" dir="ltr"><i class="bi bi-telephone"></i> 09121234567</div>
                    </div>
                    <div class="col-md-4">
                        <label class="form-label">تعمیرکار</label>
                        <div class="d-flex gap-2 align-items-stretch">
                            <div class="flex-grow-1" style="min-width:0">
                                <select class="form-select"><option>— بعداً تعیین می‌شود —</option></select>
                            </div>
                            <button type="button" class="btn btn-new-party"><i class="bi bi-person-plus-fill"></i><span class="d-none d-sm-inline">جدید</span></button>
                        </div>
                    </div>
                    <div class="col-md-4"><label class="form-label">تاریخ ورود (پذیرش)</label><input class="form-control num" value="1405/09/05" /></div>
                    <div class="col-12"><label class="form-label">یادداشت پذیرش</label><input class="form-control" /></div>
                </div>
            </section>

            <section class="repair-block">
                <div class="repair-block-head">
                    <i class="bi bi-pc-display-horizontal text-primary"></i>
                    <h6>دستگاه‌های پذیرش‌شده</h6>
                    <span class="badge badge-soft-primary">1</span>
                    <button type="button" class="btn btn-sm btn-outline-primary me-auto"><i class="bi bi-plus-lg"></i> افزودن دستگاه</button>
                </div>
                {device_card}
            </section>

            <section class="repair-block">
                <div class="repair-block-head">
                    <i class="bi bi-list-check text-primary"></i>
                    <h6>کارها و قطعات مصرفی</h6>
                    <span class="badge badge-soft-primary">2</span>
                    <button type="button" class="btn btn-sm btn-outline-primary me-auto"><i class="bi bi-plus-lg"></i> ردیف جدید (F2)</button>
                </div>
                <div class="table-responsive">
                    <table class="table align-middle">
                        <thead><tr><th>شرح کار</th><th>قطعه از انبار</th><th style="width:90px">مقدار</th><th>هزینه داخلی</th><th>قیمت فروش</th><th>سود ردیف</th><th></th></tr></thead>
                        <tbody>
                            <tr>
                                <td><input class="form-control form-control-sm" value="تعویض خمیر سیلیکون" /></td>
                                <td><input class="form-control form-control-sm" placeholder="جستجوی قطعه…" /></td>
                                <td><input class="form-control form-control-sm num" dir="ltr" value="1" /></td>
                                <td><input class="form-control form-control-sm num" dir="ltr" value="120,000" /></td>
                                <td><input class="form-control form-control-sm num" dir="ltr" value="350,000" /></td>
                                <td class="text-start num text-success">230,000</td>
                                <td class="text-center"><button class="btn btn-sm btn-light"><i class="bi bi-x-lg text-danger"></i></button></td>
                            </tr>
                            <tr>
                                <td><input class="form-control form-control-sm" value="باتری CMOS" /></td>
                                <td><input class="form-control form-control-sm" value="BAT-CMOS" /></td>
                                <td><input class="form-control form-control-sm num" dir="ltr" value="1" /></td>
                                <td><input class="form-control form-control-sm num" dir="ltr" value="90,000" /></td>
                                <td><input class="form-control form-control-sm num" dir="ltr" value="150,000" /></td>
                                <td class="text-start num text-success">60,000</td>
                                <td class="text-center"><button class="btn btn-sm btn-light"><i class="bi bi-x-lg text-danger"></i></button></td>
                            </tr>
                        </tbody>
                    </table>
                </div>
                <div class="repair-total">
                    <div>جمع هزینه: <b class="num">210,000</b></div>
                    <div>جمع دریافتی: <b class="num text-primary">500,000</b></div>
                    <div>سود: <b class="num text-success">290,000</b></div>
                </div>
            </section>
        </div>
        <div class="modal-footer mt-3 px-0 pb-0">
            <button type="button" class="btn btn-light">انصراف (Esc)</button>
            <button type="button" class="btn btn-outline-secondary"><i class="bi bi-card-checklist ms-1"></i> ذخیره و چاپ فیش تحویل (F4)</button>
            <button type="button" class="btn btn-primary me-auto">ذخیره (F1)</button>
        </div>
        </div>
    </div>"""

    warranty = f"""
    <div class="card mb-4"><div class="card-header"><i class="bi bi-shield-check text-success"></i>
        <span class="card-title">فرم پذیرش گارانتی (همان الگو — مرجع طراحی)</span></div>
        <div class="card-body">
        <div class="repair-form">
            <div class="row g-3">
                <div class="col-md-4"><label class="form-label">مشتری *</label>
                    <div class="d-flex gap-2"><div class="flex-grow-1" style="min-width:0"><input class="form-control" value="خانم احمدی" /></div>
                    <button type="button" class="btn btn-outline-primary btn-sm">جدید</button></div></div>
                <div class="col-md-4"><label class="form-label">تعمیرکار</label>
                    <div class="d-flex gap-2"><div class="flex-grow-1" style="min-width:0"><select class="form-select"><option>— بعداً تعیین می‌شود —</option></select></div>
                    <button type="button" class="btn btn-outline-primary btn-sm">جدید</button></div></div>
                <div class="col-md-4"><label class="form-label">تاریخ پذیرش *</label><input class="form-control num" value="1405/09/05" /></div>
                <div class="col-md-6"><label class="form-label">انبار برداشت *</label><select class="form-select"><option>انبار اصلی</option></select></div>
                <div class="col-md-6"><label class="form-label">یادداشت</label><input class="form-control" /></div>
            </div>
            <div class="repair-block">
                <div class="repair-block-head">
                    <i class="bi bi-pc-display-horizontal text-primary"></i>
                    <h6>دستگاه‌های پذیرش‌شده</h6>
                    <span class="badge badge-soft-primary">1</span>
                    <button type="button" class="btn btn-sm btn-outline-primary me-auto"><i class="bi bi-plus-lg"></i> افزودن دستگاه</button>
                </div>
                {device_card}
            </div>
            <div class="repair-block">
                <div class="repair-block-head">
                    <i class="bi bi-list-check text-primary"></i>
                    <h6>کارها و قطعات مشترک پذیرش</h6>
                    <span class="badge badge-soft-primary">1</span>
                    <button type="button" class="btn btn-sm btn-outline-primary me-auto"><i class="bi bi-plus-lg"></i> افزودن ردیف (F2)</button>
                </div>
                <div class="row g-2 align-items-start">
                    <div class="col-md-3"><label class="form-label">شرح کار یا قطعه *</label><input class="form-control form-control-sm" value="تعویض هارد" /></div>
                    <div class="col-md-3"><label class="form-label">کالای انبار (اختیاری)</label><input class="form-control form-control-sm" placeholder="انتخاب کالا" /></div>
                    <div class="col-6 col-md-1"><label class="form-label">مقدار</label><input class="form-control form-control-sm num" dir="ltr" value="1" /></div>
                    <div class="col-6 col-md-2"><label class="form-label">هزینه داخلی واحد</label><input class="form-control form-control-sm num" dir="ltr" value="800,000" /></div>
                    <div class="col-md-2"><label class="form-label">تصمیم گارانتی *</label><select class="form-select form-select-sm"><option>پذیرفته</option></select></div>
                    <div class="col-md-1"><label class="form-label d-none d-md-block">&nbsp;</label><button class="btn btn-sm btn-light w-100"><i class="bi bi-x-lg text-danger"></i></button></div>
                </div>
            </div>
            <div class="repair-total">
                <div>مبلغ مشتری در زمان تحویل: <b class="num text-primary">0</b> ریال</div>
                <div class="text-muted">هزینه داخلی جداگانه ثبت می‌شود.</div>
            </div>
        </div>
        <div class="modal-footer mt-3 px-0 pb-0">
            <button type="button" class="btn btn-light">انصراف (Esc)</button>
            <button type="button" class="btn btn-primary me-auto">ذخیره پیش‌نویس (F1)</button>
        </div>
        </div>
    </div>"""

    tradein = f"""
    <div class="card"><div class="card-header"><i class="bi bi-arrow-left-right text-warning"></i>
        <span class="card-title">فرم طرح تعویض (همان الگو)</span></div>
        <div class="card-body">
        <div class="repair-form">
            <div class="repair-block">
                <div class="repair-block-head">
                    <i class="bi bi-person-vcard text-primary"></i>
                    <h6>مشتری و تاریخ</h6>
                </div>
                <div class="row g-3">
                    <div class="col-md-6"><label class="form-label">مشتری *</label>
                        <div class="d-flex gap-2"><div class="flex-grow-1" style="min-width:0"><input class="form-control" value="آقای کریمی" /></div>
                        <button type="button" class="btn btn-outline-primary btn-sm">جدید</button></div></div>
                    <div class="col-md-6"><label class="form-label">تاریخ پذیرش *</label><input class="form-control num" value="1405/09/05" /></div>
                </div>
            </div>
            <div class="repair-block">
                <div class="repair-block-head">
                    <i class="bi bi-pc-display-horizontal text-primary"></i>
                    <h6>دستگاه و ارزش تعیین‌شده</h6>
                </div>
                <div class="row g-3">
                    <div class="col-md-6"><label class="form-label">نوع دستگاه *</label><input class="form-control" value="لپ‌تاپ" /></div>
                    <div class="col-md-6"><label class="form-label">مدل</label><input class="form-control" value="HP 250 G8" /></div>
                    <div class="col-md-6"><label class="form-label">سریال</label><input class="form-control num" dir="ltr" value="CND12345" /></div>
                    <div class="col-md-6"><label class="form-label">ارزش تعیین‌شده (ریال) *</label><input class="form-control num" dir="ltr" value="8,500,000" /></div>
                    <div class="col-12"><label class="form-label">یادداشت</label><input class="form-control" /></div>
                </div>
            </div>
        </div>
        <div class="modal-footer mt-3 px-0 pb-0">
            <button type="button" class="btn btn-light">انصراف (Esc)</button>
            <button type="button" class="btn btn-primary me-auto">ذخیره پیش‌نویس (F1)</button>
        </div>
        </div>
    </div>"""

    return f"""<!DOCTYPE html>
<html lang="fa" dir="rtl">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>بخش تعمیرات — یکپارچه‌سازی طراحی فرم‌ها</title>
<style>
/* CSS تولیدی پروژه (bootstrap + آیکن + app.css) درون فایل جاسازی شده است */
</style>
<style>
body {{ background: #f5f7fa; padding: 1.25rem; }}
.demo-note {{ max-width: 1100px; margin: 0 auto 1rem; font-size: .85rem; color: #475569; }}
.demo-wrap {{ max-width: 1100px; margin: 0 auto 1.5rem; background: #fff; border-radius: 16px; padding: 1rem 1.1rem 1.25rem; box-shadow: 0 8px 30px rgba(15,23,42,.06); }}
</style>
</head>
<body>
<div class="demo-note">
    <b>پیش‌نمایش طراحی (فقط بازبینی) —</b> هر سه فرم بخش تعمیرات از یک زبان مشترک استفاده می‌کنند:
    بلوک‌های ساده با سربرگ آیکن خطی + شمارنده، کارت دستگاه آرام، جمع‌بندی تک‌نواره و فوتر یکسان
    (<span class="num">انصراف</span> راست، دکمهٔ اصلی <span class="num">primary</span> چپ).
</div>
<div class="demo-wrap">{head}{intake}{warranty}{tradein}</div>
</body>
</html>
"""


def main():
    fonts = {
        "regular": data_uri(SRC / "fonts/Vazirmatn-Regular.woff2"),
        "bold": data_uri(SRC / "fonts/Vazirmatn-Bold.woff2"),
    }
    faces = (
        f"@font-face {{ font-family: 'Vazirmatn'; src: url('{fonts['regular']}') format('woff2'); "
        "font-weight: 400; font-display: swap; }"
        f"@font-face {{ font-family: 'Vazirmatn'; src: url('{fonts['bold']}') format('woff2'); "
        "font-weight: 700; font-display: swap; }"
    )
    css = faces + "\n/* ==== bootstrap ==== */\n" + strip_font_faces(
        (SRC / "css/bootstrap.min.css").read_text(encoding="utf-8"))
    css += "\n/* ==== bootstrap-icons ==== */\n" + icon_font_css()
    css += "\n/* ==== app.css ==== */\n" + strip_font_faces(
        (SRC / "css/app.css").read_text(encoding="utf-8"))

    html = body().replace(
        "/* CSS تولیدی پروژه (bootstrap + آیکن + app.css) درون فایل جاسازی شده است */",
        css, 1)
    out = HERE / "repair-section-design-preview.html"
    out.write_text(html, encoding="utf-8")
    leftovers = [u for u in re.findall(r"url\(([^)]*)\)", html) if "data:" not in u and "fonts/" in u]
    assert not leftovers, leftovers[:5]
    print(f"{out.name}: {out.stat().st_size:,} bytes")


if __name__ == "__main__":
    main()
