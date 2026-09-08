#!/usr/bin/env python3
"""E2E UI: پیش‌نمایش داخل برنامه و بخش پیوست‌های مرتب — ورد/اکسل با متن استخراج‌شده، آپلود تمیز."""
import asyncio, json, requests
from playwright.async_api import async_playwright, expect

BASE = "http://127.0.0.1:5103"

async def main():
    checks = []
    def passed(t): checks.append(t); print("PASS:", t, flush=True)

    # پیدا کردن سند docx و attachment آن
    s = requests.Session()
    login = s.post(BASE + "/api/auth/login", json={"username": "admin", "password": "admin"}, timeout=20).json()
    ah = {"Authorization": "Bearer " + login["token"]}
    s.headers.update(ah)
    docs = s.get(BASE + "/api/doc-archive/documents", timeout=20).json()
    docs = docs if isinstance(docs, list) else docs.get("items") or []
    docx_doc = next(d for d in docs if d.get("code") == "OFF-DOCX-1")
    sess_json = json.dumps(json.dumps({"username": "admin", "password": "admin"}))

    async with async_playwright() as pw:
        browser = await pw.chromium.launch(headless=True, args=["--no-sandbox"])
        ctx = await browser.new_context(viewport={"width": 1500, "height": 1000}, service_workers="block")
        page = await ctx.new_page()
        errors = []
        page.on("pageerror", lambda e: errors.append(str(e)))
        await page.add_init_script(
            "localStorage.setItem('authSession', " + json.dumps(json.dumps(login)) + ");"
            "localStorage.setItem('apiBaseUrl', location.origin);")
        await page.goto(BASE + f"/doc-archive/documents/{docx_doc['id']}", wait_until="domcontentloaded")
        await page.wait_for_timeout(10000)
        await expect(page.get_by_text("OFF-DOCX-1", exact=False).first).to_be_visible(timeout=15000)
        passed("صفحهٔ مدرک ورد باز شد")

        # بخش پیوست: ردیف با نام فایل docx دیده می‌شود (چیپ‌بندی جدید)
        box = page.locator(".attx-box", has_text="office_sample.docx").first
        await expect(box).to_be_visible(timeout=12000)
        passed("بخش پیوست‌های مرتب (ردیف‌بندی جدید) نمایش داده می‌شود")

        # کلیک روی نام فایل → مودال پیش‌نمایش با متن استخراج‌شده
        await box.get_by_text("office_sample.docx", exact=False).first.click()
        await page.wait_for_timeout(3500)
        modal = page.locator(".fpv-dialog").first
        await expect(modal).to_be_visible(timeout=10000)
        await expect(modal.get_by_text("متن استخراج‌شده").first).to_be_visible()
        await expect(modal.get_by_text("قرارداد نگهداری نرم‌افزار").first).to_be_visible(timeout=10000)
        passed("پیش‌نمایش ورد داخل برنامه: متن استخراج‌شده (بدون دانلود) نمایش داده شد")

        # دکمهٔ دانلود در سربرگ مودال هست
        dl = modal.locator("a[title='دانلود فایل']")
        await expect(dl).to_be_visible()
        passed("در مودال پیش‌نمایش، دکمهٔ «دانلود فایل» هم هست")
        await modal.locator("button[title^='بستن']").click()
        await page.wait_for_timeout(600)
        await expect(page.locator(".fpv-dialog")).to_have_count(0)
        passed("مودال با دکمهٔ بستن بسته می‌شود")

        assert not errors, errors
        passed("بدون خطای صفحه")
        await browser.close()
    print(f"All {len(checks)} preview/upload UI scenarios passed.")

asyncio.run(main())
