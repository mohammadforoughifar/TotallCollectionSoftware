#!/usr/bin/env python3
"""عکس‌برداری از کارت «کاربران و دامنه» در صفحهٔ پیش‌نویس (برای راهنمای تصویری).

خروجی: .shots/um-01-users.png
پیش‌نیاز: سرور روی 127.0.0.1:5399
"""
import subprocess
import sys
from pathlib import Path

from playwright.sync_api import sync_playwright

BASE = "http://127.0.0.1:5399"
HDR = {"X-Forwarded-Proto": "https"}
OUT = Path("/home/user/.shots")
TOOLS = Path("/home/user/work/repo/TotallCollectionSoftware/support-panel/tools")


def approved_request() -> int:
    out = subprocess.run([sys.executable, str(TOOLS / "make-approved-request.py")],
                         capture_output=True, text=True, check=True)
    return int(out.stdout.strip().splitlines()[-1])


def main() -> None:
    rid = approved_request()
    OUT.mkdir(exist_ok=True)
    with sync_playwright() as p:
        browser = p.chromium.launch(args=["--no-sandbox"])
        ctx = browser.new_context(viewport={"width": 1380, "height": 950}, device_scale_factor=2,
                                  locale="fa-IR", extra_http_headers=HDR)
        page = ctx.new_page()
        page.goto(f"{BASE}/Login", wait_until="domcontentloaded")
        page.click("button:has-text('ورود سریع دمو')")
        page.wait_for_load_state("networkidle")
        page.goto(f"{BASE}/Requests/Draft?id={rid}", wait_until="networkidle")
        card = page.locator(".card", has_text="کاربران و دامنه").first
        page.select_option("#userMode", "Named")
        page.select_option("#appliesTo", "Global")
        card.scroll_into_view_if_needed()
        card.screenshot(path=str(OUT / "um-01-users.png"))
        print("پرونده:", rid, "| گزینه‌های حالت کاربر:",
              page.eval_on_selector_all("#userMode option", "els => els.map(e => e.textContent.trim())"))
        print("تصویر:", OUT / "um-01-users.png")
        ctx.close()
        browser.close()


if __name__ == "__main__":
    main()
