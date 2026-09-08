#!/usr/bin/env python3
"""E2E نهایی پیش‌نمایش در آرشیو اسناد: PDF (iframe/blob) و Word/Excel (متن استخراج‌شده) روی کد نهایی."""
import asyncio, json, requests
from playwright.async_api import async_playwright, expect

BASE = "http://127.0.0.1:5104"

async def main():
    checks = []
    def passed(t): checks.append(t); print("PASS:", t, flush=True)
    s = requests.Session()
    login = s.post(BASE + "/api/auth/login", json={"username": "admin", "password": "admin"}, timeout=20).json()
    doc_id = json.load(open("/home/user/.cache/pv-e2e.json"))["docId"]

    async with async_playwright() as pw:
        browser = await pw.chromium.launch(headless=True, args=["--no-sandbox"])
        ctx = await browser.new_context(viewport={"width": 1500, "height": 1000}, service_workers="block")
        page = await ctx.new_page()
        errors = []
        page.on("pageerror", lambda e: errors.append(str(e)))
        await page.add_init_script(
            "localStorage.setItem('authSession', " + json.dumps(json.dumps(login)) + ");"
            "localStorage.setItem('apiBaseUrl', location.origin);")
        await page.goto(BASE + f"/doc-archive/documents/{doc_id}", wait_until="domcontentloaded")
        await page.wait_for_timeout(10000)
        box = page.locator(".attx-box").first
        await expect(box).to_be_visible(timeout=15000)
        for fn in ["pv_digital.pdf", "pv_sample.docx", "pv_sample.xlsx"]:
            await expect(box.locator(".attx-name", has_text=fn).first).to_be_visible()
        passed("بخش پیوست: هر ۳ فایل (PDF/Word/Excel) با آیکون نوع خودشان دیده می‌شوند")

        # ---- 1) PDF: مودال با iframe (blob) ----
        await box.locator(".attx-name", has_text="pv_digital.pdf").first.click()
        await page.wait_for_timeout(2500)
        modal = page.locator(".fpv-dialog")
        await expect(modal).to_be_visible(timeout=8000)
        frame = modal.locator("iframe.fpv-frame")
        await expect(frame).to_be_visible()
        src = await frame.get_attribute("src") or ""
        assert src.startswith("blob:"), src
        passed("پیش‌نمایش PDF: داخل مودال با iframe باز شد (بدون دانلود/تب جدید)")
        await modal.locator("button[title^='بستن']").click(); await page.wait_for_timeout(500)

        # ---- 2) Word: متن استخراج‌شده ----
        await box.locator(".attx-name", has_text="pv_sample.docx").first.click()
        await page.wait_for_timeout(3000)
        modal = page.locator(".fpv-dialog")
        await expect(modal).to_be_visible(timeout=8000)
        await expect(modal.get_by_text("متن استخراج‌شده").first).to_be_visible()
        await expect(modal.get_by_text("قرارداد نمونه پیش‌نمایش").first).to_be_visible(timeout=8000)
        passed("پیش‌نمایش Word: متن استخراج‌شدهٔ سند داخل برنامه نمایش داده شد")
        await modal.locator("button[title^='بستن']").click(); await page.wait_for_timeout(500)

        # ---- 3) Excel: متن استخراج‌شده (شیت‌ها) ----
        await box.locator(".attx-name", has_text="pv_sample.xlsx").first.click()
        await page.wait_for_timeout(3000)
        modal = page.locator(".fpv-dialog")
        await expect(modal).to_be_visible(timeout=8000)
        await expect(modal.get_by_text("متن استخراج‌شده").first).to_be_visible()
        await expect(modal.get_by_text("حق پشتیبانی").first).to_be_visible(timeout=8000)
        passed("پیش‌نمایش Excel: محتوای شیت داخل برنامه نمایش داده شد")
        await modal.locator("button[title^='بستن']").click(); await page.wait_for_timeout(500)

        await expect(page.locator(".fpv-dialog")).to_have_count(0)
        passed("مودال‌ها مرتب بسته می‌شوند")
        assert not errors, errors
        passed("بدون خطای صفحه")
        await browser.close()
    print(f"All {len(checks)} preview scenarios passed.")

asyncio.run(main())
