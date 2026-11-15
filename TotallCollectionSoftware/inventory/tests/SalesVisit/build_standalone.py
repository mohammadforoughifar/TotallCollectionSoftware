"""نسخهٔ خودبسندهٔ پیشنمایش را از visit-forms-proposal.html میسازد.

همهٔ CSSها و فونتهای موردنیاز درون فایل جاسازی میشوند تا صفحه بدون سرور
و بدون هیچ درخواست شبکهای، مستقیم در نمایشگر فایل رندر شود.

    python3 inventory/tests/SalesVisit/build_standalone.py
"""
from pathlib import Path
import base64
import re

HERE = Path(__file__).resolve().parent
INV = HERE.parents[1]                       # .../inventory
SRC = INV / "src/Inventory.Client/wwwroot"
SOURCE = HERE / "visit-forms-proposal.html"
TARGET = HERE / "visit-forms-proposal-standalone.html"


def data_uri(path):
    assert path.exists(), f"فونت یافت نشد: {path}"
    return "data:font/woff2;base64," + base64.b64encode(path.read_bytes()).decode("ascii")


def strip_font_faces(css):
    return re.sub(r"@font-face\s*\{[^}]*\}", "", css, flags=re.S)


def main():
    src = SOURCE.read_text(encoding="utf-8")
    fonts = {
        "regular": data_uri(SRC / "fonts/Vazirmatn-Regular.woff2"),
        "bold": data_uri(SRC / "fonts/Vazirmatn-Bold.woff2"),
        "icons": data_uri(SRC / "icons/fonts/bootstrap-icons.woff2"),
    }
    faces = (
        "/* --- فونتهای جاسازیشده (بدون درخواست شبکهای) --- */\n"
        f"@font-face {{ font-family: 'Vazirmatn'; src: url('{fonts['regular']}') format('woff2'); "
        "font-weight: 400; font-style: normal; font-display: swap; }\n"
        f"@font-face {{ font-family: 'Vazirmatn'; src: url('{fonts['bold']}') format('woff2'); "
        "font-weight: 700; font-style: normal; font-display: swap; }\n"
        f"@font-face {{ font-display: block; font-family: 'bootstrap-icons'; "
        f"src: url('{fonts['icons']}') format('woff2'); }}\n"
    )

    css_files = [
        ("bootstrap.min.css", SRC / "css/bootstrap.min.css"),
        ("bootstrap-icons.css", SRC / "icons/bootstrap-icons.css"),
        ("app.css", SRC / "css/app.css"),
    ]
    combined = faces
    for label, path in css_files:
        css = strip_font_faces(path.read_text(encoding="utf-8"))
        combined += f"\n/* ==== {label} ==== */\n{css}\n"

    leftovers = [u for u in re.findall(r"url\(([^)]*)\)", combined)
                 if "data:" not in u and "fonts/" in u]
    assert not leftovers, f"ارجاع فونت نسبی باقی ماند: {leftovers[:5]}"

    links = re.findall(r'<link[^>]+rel="stylesheet"[^>]*>\s*', src)
    assert len(links) == 3, f"انتظار ۳ لینک CSS، یافت شد {len(links)}"
    out = src
    for link in links:
        out = out.replace(link, "", 1)
    assert out.count("<style>\n") >= 1
    out = out.replace("<style>\n", "<style>\n" + combined + "\n</style>\n<style>\n", 1)
    out = out.replace(
        "<title>فروش و ویزیتوری — نمای فرمها</title>",
        "<title>فروش و ویزیتوری — نمای فرمها (خودبسنده)</title>\n"
        "<!-- نسخهٔ خودبسنده: CSS و فونتها درون فایل جاسازی شدهاند. -->",
        1,
    )

    assert not re.findall(r'(?:href|src)="(?:https?://[^"]+|/[^"]+)"', out), "ارجاع خارجی باقی ماند"
    TARGET.write_text(out, encoding="utf-8")
    print(f"{TARGET.name}: {TARGET.stat().st_size:,} bytes — بدون ارجاع خارجی")


if __name__ == "__main__":
    main()
