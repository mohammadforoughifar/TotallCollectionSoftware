#!/usr/bin/env python3
"""بازبینی چشمی دکمه‌های صفحهٔ درخواست پس از «تأیید هویت».

خروجی: .shots/v2-*.png  (۱) با هویت و وضعیت «بررسی» → دکمهٔ «تأیید درخواست»
                         (۲) وضعیت «تأییدشده» → دکمه‌های «پیش‌نویس امضا» و «امضا»
                         (۳) پس از امضا → «تحویل لایسنس»
پیش‌نیاز: سرور روی 127.0.0.1:5399
"""
import json
import os
import re
import time
from pathlib import Path

from playwright.sync_api import sync_playwright

BASE = "http://127.0.0.1:5399"
OUT = Path("/home/user/.shots")
OUT.mkdir(exist_ok=True)
HDR = {"X-Forwarded-Proto": "https"}          # کوکی Secure فقط با این هدر روی HTTP کار می‌کند


def main() -> None:
    with sync_playwright() as p:
        browser = p.chromium.launch(args=["--no-sandbox"])
        ctx = browser.new_context(viewport={"width": 1380, "height": 900}, device_scale_factor=2,
                                  locale="fa-IR", extra_http_headers=HDR)
        page = ctx.new_page()

        page.goto(f"{BASE}/Login", wait_until="domcontentloaded")
        page.click("button:has-text('ورود سریع دمو')")
        page.wait_for_load_state("networkidle")

        # ۱) پروندهٔ تازه
        inst = "btncheck-" + time.strftime("%H%M%S")
        payload = {
            "requestVersion": 2, "requestType": "NewActivation", "installationId": inst,
            "customerName": "شرکت آزمون دکمه", "contactName": "مدیر امین",
            "contactPhone": "02100000002", "customerNote": "آزمون دکمهٔ پیش‌نویس",
            "requestedAt": "2026-11-15T00:00:00Z",
        }
        page.goto(f"{BASE}/Requests/Ingest", wait_until="domcontentloaded")
        token = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', page.content()).group(1)
        resp = page.request.post(f"{BASE}/Requests/Ingest?handler=Save",
                                 headers=HDR,
                                 form={"__RequestVerificationToken": token, "mode": "paste",
                                       "rawChannel": "Paste",
                                       "pastedJson": json.dumps(payload, ensure_ascii=False)})
        # Playwright ریدایرکت‌ها را دنبال می‌کند؛ شناسهٔ پروندهٔ تازه از صف درخواست‌ها خوانده می‌شود
        page.goto(f"{BASE}/Requests", wait_until="domcontentloaded")
        rid = int(re.search(r"/Requests/Details\?id=(\d+)", page.content()).group(1))
        print("پرونده:", rid)

        # ۲) مشتری + شروع بررسی (از رابط کاربری، مثل کاربر واقعی)
        page.goto(f"{BASE}/Requests/Details?id={rid}", wait_until="domcontentloaded")
        page.select_option("#customerId", "2")
        page.click("button:has-text('وصل کن')")
        page.wait_for_load_state("domcontentloaded")
        page.select_option("#to", "Reviewing")
        page.click("form button:has-text('اعمال')")
        page.wait_for_load_state("domcontentloaded")

        # ۳) ثبت تأیید هویت با فرم خود صفحه
        page.goto(f"{BASE}/Requests/Identity?id={rid}", wait_until="domcontentloaded")
        page.fill("#verifiedWith", "مدیر امین")
        page.fill("#note", "تأیید تلفنی — آزمون دکمه‌ها")
        page.click("button:has-text('ثبت تأیید هویت')")
        page.wait_for_load_state("domcontentloaded")

        # (۱) هویت ثبت شده، وضعیت «بررسی» ⇒ باید دکمهٔ «تأیید درخواست» دیده شود
        page.goto(f"{BASE}/Requests/Details?id={rid}", wait_until="networkidle")
        page.screenshot(path=str(OUT / "v2-01-identity-to-approve.png"), full_page=False, clip={"x": 0, "y": 0, "width": 1380, "height": 660})
        print("۱) دکمه‌های صفحه‌سر:", page.eval_on_selector_all(
            ".page-head .btn", "els => els.map(e => e.textContent.trim())"))

        # (۲) زدن «تأیید درخواست» ⇒ باید «پیش‌نویس امضا» و «امضا» ظاهر شوند
        page.click(".page-head button:has-text('تأیید درخواست')")
        page.wait_for_load_state("networkidle")
        page.screenshot(path=str(OUT / "v2-02-approved-draft-sign.png"), full_page=False, clip={"x": 0, "y": 0, "width": 1380, "height": 660})
        print("۲) دکمه‌های صفحه‌سر:", page.eval_on_selector_all(
            ".page-head .btn", "els => els.map(e => e.textContent.trim())"))

        # ۴) پیش‌نویس (فرم خود صفحه) و امضا
        page.goto(f"{BASE}/Requests/Draft?id={rid}", wait_until="domcontentloaded")
        token = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', page.content()).group(1)
        page.request.post(f"{BASE}/Requests/Draft?id={rid}&handler=Save", max_redirects=0, headers=HDR, form={
            "__RequestVerificationToken": token, "id": str(rid), "presets": "FIN",
            "termMode": "months", "months": "12", "until": "", "updatesUntil": "",
            "deployment": "OnPrem", "userMode": "Named", "userMax": "10",
            "exempt": "", "appliesTo": "Global", "note": "پیش‌نویس آزمون دکمه‌ها",
        })
        page.goto(f"{BASE}/Requests/Sign?id={rid}", wait_until="domcontentloaded")
        token = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', page.content()).group(1)
        sign = page.request.post(f"{BASE}/Requests/Sign?id={rid}", max_redirects=0, headers=HDR, form={
            "__RequestVerificationToken": token, "id": rid, "confirm": "on",
            "keyId": "dev-test", "keyPassword": os.environ.get("LICENSETOOL_KEY_PASSWORD", ""),
            "note": "امضای آزمون دکمه‌ها",
        })
        body = sign.text()
        serial = re.search(r"LIC-\d{8}-[0-9A-F]{6}", body)
        ok = "سند با موفقیت امضا و بازبینی شد" in body
        print("امضا موفق:", ok, "| سریال:", serial.group(0) if serial else "—")

        page.goto(f"{BASE}/Requests/Details?id={rid}", wait_until="networkidle")
        page.screenshot(path=str(OUT / "v2-03-issued-deliver.png"), full_page=False, clip={"x": 0, "y": 0, "width": 1380, "height": 660})
        print("۳) دکمه‌های صفحه‌سر:", page.eval_on_selector_all(
            ".page-head .btn", "els => els.map(e => e.textContent.trim())"))

        ctx.close()
        browser.close()


if __name__ == "__main__":
    main()
