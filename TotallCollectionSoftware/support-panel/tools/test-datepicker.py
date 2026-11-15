#!/usr/bin/env python3
"""آزمون تقویم شمسی (jdatepicker) در صفحه‌های «تأیید هویت» و «پیش‌نویس».

۱) باز کردن تقویم با کلیک روی فیلد تاریخ، انتخاب یک روز، بررسی مقدار نوشته‌شده
۲) ثبت واقعی فرم با تاریخ انتخاب‌شده (سرور باید بپذیرد)
۳) همین کار برای «تاریخ پایان» و «به‌روزرسانی تا» و ذخیرهٔ پیش‌نویس
خروجی تصویری: .shots/dp-*.png
"""
import html
import re
import sys
from pathlib import Path

from playwright.sync_api import sync_playwright

sys.path.insert(0, str(Path(__file__).parent))
from importlib import import_module

BASE = "http://127.0.0.1:5399"
HDR = {"X-Forwarded-Proto": "https"}
OUT = Path("/home/user/.shots")
OUT.mkdir(exist_ok=True)


def approved_request() -> int:
    mod = import_module("make-approved-request".replace("-", "_")) if False else None
    import subprocess
    out = subprocess.run([sys.executable, str(Path("/home/user/work/repo/TotallCollectionSoftware/support-panel/tools/make-approved-request.py"))],
                         capture_output=True, text=True, check=True)
    return int(out.stdout.strip().splitlines()[-1])


def pick_date(page, selector: str, day: str) -> str:
    page.click(selector)
    page.wait_for_selector(".jdp", state="visible")
    print("   تقویم باز شد:", page.inner_text(".jdp-title"))
    page.click(f".jdp-grid .jdp-day[data-day='{day}']")
    page.wait_for_selector(".jdp", state="detached")
    return page.input_value(selector)


def main() -> None:
    rid = approved_request()
    print("پرونده:", rid)
    with sync_playwright() as p:
        browser = p.chromium.launch(args=["--no-sandbox"])
        ctx = browser.new_context(viewport={"width": 1380, "height": 900}, device_scale_factor=2,
                                  locale="fa-IR", extra_http_headers=HDR)
        page = ctx.new_page()
        page.goto(f"{BASE}/Login", wait_until="domcontentloaded")
        page.click("button:has-text('ورود سریع دمو')")
        page.wait_for_load_state("networkidle")

        # ── ۱) تأیید هویت: انتخاب تاریخ با تقویم
        page.goto(f"{BASE}/Requests/Identity?id={rid}", wait_until="networkidle")
        value = pick_date(page, "#verifiedAt", "23")
        print("۱) تاریخ انتخاب‌شده:", value)
        page.click("#verifiedAt")                       # برای تصویر، تقویم را باز می‌گذاریم
        page.wait_for_selector(".jdp")
        if Path("/usr/share/fonts").exists():
            page.wait_for_timeout(200)
        page.screenshot(path=str(OUT / "dp-01-identity.png"), clip={"x": 0, "y": 40, "width": 1380, "height": 560})
        page.keyboard.press("Escape")
        page.fill("#note", "تأیید تلفنی — تاریخ از تقویم")
        page.click("button:has-text('ثبت تأیید هویت')")
        page.wait_for_load_state("domcontentloaded")
        body = page.content()
        ok = "تأیید هویت ثبت شد" in body
        err = re.search(r'<div class="alert error">(.*?)</div>', body, re.S)
        print("   ثبت با تاریخ تقویم:", "موفق" if ok else "ناموفق: " + (html.unescape(re.sub(r'<[^>]+>', '', err.group(1))).strip() if err else "?"))

        # ── ۲) پیش‌نویس: تاریخ پایان و به‌روزرسانی با تقویم (حالت مدت = تاریخ پایان)
        page.goto(f"{BASE}/Requests/Draft?id={rid}", wait_until="networkidle")
        page.select_option("#termMode", "until")
        until = pick_date(page, "#until", "15")
        updates = pick_date(page, "#updatesUntil", "10")
        print("۲) تاریخ پایان:", until, "| به‌روزرسانی تا:", updates)
        page.click("#until")
        page.wait_for_selector(".jdp")
        page.screenshot(path=str(OUT / "dp-02-draft.png"), clip={"x": 0, "y": 40, "width": 1380, "height": 620})
        page.keyboard.press("Escape")
        page.click("button:has-text('ذخیره')")
        page.wait_for_load_state("domcontentloaded")
        body = page.content()
        print("   ذخیرهٔ پیش‌نویس:", "موفق" if "پیش‌نویس ذخیره شد" in body else "بررسی کنید")
        print("   مقدار بازگشتی از سرور:", page.input_value("#until"), "|", page.input_value("#updatesUntil"))
        page.screenshot(path=str(OUT / "dp-03-onprem-saas.png"), clip={"x": 0, "y": 40, "width": 1380, "height": 700})

        ctx.close()
        browser.close()


if __name__ == "__main__":
    main()
