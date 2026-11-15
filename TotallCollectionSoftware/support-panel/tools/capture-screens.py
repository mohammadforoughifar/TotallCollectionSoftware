#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""گرفتن اسکرین‌شات از صفحه‌های پنل + کادرگذاری روی عناصر کلیدی (برای راهنمای تصویری)."""
import json
import os
import re
from pathlib import Path

from playwright.sync_api import sync_playwright
from PIL import Image, ImageDraw, ImageFont

BASE = "http://127.0.0.1:5399"
SHOTS = Path("/home/user/.shots")
SHOTS.mkdir(exist_ok=True)
W, H = 1380, 900
DSR = 2
FONT_BOLD = "/home/user/.fonts/Vazirmatn-Bold.ttf"
STEPS = []


def annotate(img_path, boxes, out_path):
    im = Image.open(img_path).convert("RGB")
    d = ImageDraw.Draw(im)
    try:
        font = ImageFont.truetype(FONT_BOLD, 26)
    except Exception:
        font = ImageFont.load_default()
    for (x, y, w, h, label) in boxes:
        pad = 4
        xy = [x * DSR - pad, y * DSR - pad, (x + w) * DSR + pad, (y + h) * DSR + pad]
        xy = [max(0, xy[0]), max(0, xy[1]), min(im.width - 1, xy[2]), min(im.height - 1, xy[3])]
        for t in range(4):
            d.rounded_rectangle([xy[0] - t, xy[1] - t, xy[2] + t, xy[3] + t],
                                radius=9, outline=(214, 45, 32))
        cx = min(max(xy[2] - 4, 28), im.width - 28)
        cy = max(xy[1] - 4, 28)
        r = 21
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(214, 45, 32), outline=(255, 255, 255), width=3)
        txt = str(label)
        bb = d.textbbox((0, 0), txt, font=font)
        d.text((cx - (bb[2] - bb[0]) / 2, cy - (bb[3] - bb[1]) / 2 - 4), txt, fill=(255, 255, 255), font=font)
    im.save(out_path, "PNG", optimize=True)


def box_of(loc):
    try:
        if loc.count() == 0:
            return None
        b = loc.first.bounding_box()
        if not b or b["width"] < 2 or b["height"] < 2:
            return None
        return (b["x"], b["y"], b["width"], b["height"])
    except Exception:
        return None


def shot(page, name, title, notes, targets):
    boxes, legend = [], []
    for num, sel, desc in targets:
        b = box_of(page.locator(sel))
        if b:
            boxes.append((*b, num))
            legend.append([num, desc])
        else:
            legend.append([num, desc])
    raw = SHOTS / f"{name}-raw.png"
    page.screenshot(path=str(raw), full_page=False)
    annotate(raw, boxes, SHOTS / f"{name}.png")
    STEPS.append({"name": name, "title": title, "notes": notes, "legend": legend,
                  "img": str(SHOTS / f"{name}.png")})
    print(f"  ✓ {name}: {title} ({len(boxes)}/{len(targets)} کادر)")


def main():
    with sync_playwright() as p:
        browser = p.chromium.launch()
        ctx = browser.new_context(viewport={"width": W, "height": H}, device_scale_factor=DSR,
                                  locale="fa-IR",
                                  extra_http_headers={"X-Forwarded-Proto": "https"})
        page = ctx.new_page()

        # ---------- ۰۱ ورود
        page.goto(f"{BASE}/Login", wait_until="networkidle")
        shot(page, "01-login", "ورود به پنل", [
            "در محیط واقعی با نام کاربری و گذرواژه وارد می‌شوید؛ در اولین ورود، تعویض گذرواژه اجباری است.",
            "در حالت پیش‌نمایش (فقط برای دمو) دکمهٔ «ورود سریع دمو» هم دیده می‌شود.",
        ], [
            (1, "input#userName", "نام کاربری"),
            (2, "input#password", "گذرواژه"),
            (3, "form:not(:has-text('دمو')) button[type=submit]", "دکمهٔ ورود"),
            (4, "form:has-text('ورود سریع دمو') button", "ورود سریع دمو (فقط پیش‌نمایش)"),
        ])

        page.locator("form:has-text('ورود سریع دمو') button").first.click()
        page.wait_for_url(lambda u: "/Login" not in u, timeout=20000)
        page.wait_for_load_state("networkidle")
        print("  ↪ وارد شدیم:", page.url)

        # ---------- ۰۲ داشبورد
        page.goto(f"{BASE}/", wait_until="networkidle")
        shot(page, "02-dashboard", "داشبورد", [
            "چهار شمارندهٔ کاری (در انتظار بررسی، آمادهٔ امضا، تحویل‌شده، تمدید نزدیک).",
            "نوار رنگی زیر آن وضعیت آمادگی امضا را می‌گوید: سبز = ابزار + کلیدها در دسترس، زرد = علت دقیق نوشته شده.",
            "جدول پایین، کارهای باز را نشان می‌دهد و ستون آخر همان پرونده را باز می‌کند.",
        ], [
            (1, ".page-head a[href*='Ingest']", "ثبت درخواست جدید"),
            (2, ".page-head a[href='/Requests']", "صف درخواست‌ها"),
            (3, ".alert", "وضعیت آمادگی امضا (کلید خصوصی/عمومی + ابزار)"),
            (4, "table", "کارهای باز"),
        ])

        # ---------- ۰۳ صف درخواست‌ها
        page.goto(f"{BASE}/Requests", wait_until="networkidle")
        shot(page, "03-requests", "صف درخواست‌ها", [
            "هر ردیف: شمارهٔ درخواست، مشتری، وضعیت و تاریخ ثبت (شمسی/تهران).",
            "ستون «بازکردن» پرونده را باز می‌کند؛ از همان‌جا همهٔ گام‌های بعدی شروع می‌شود.",
        ], [
            (1, "table", "فهرست درخواست‌ها"),
            (2, "table tbody tr:first-child a[href*='Details']", "بازکردن پرونده"),
            (3, ".page-head a[href*='Ingest']", "ثبت درخواست جدید"),
        ])

        # شناسهٔ درخواست «تأییدشده» برای دیدن فرم امضا
        req_id = None
        rows = page.locator("table tbody tr")
        for i in range(rows.count()):
            row = rows.nth(i)
            link = row.locator("a[href*='Details']")
            if link.count() == 0:
                continue
            m = re.search(r"id=(\d+)", link.first.get_attribute("href") or "")
            if not m:
                continue
            if req_id is None:
                req_id = m.group(1)
            if "تأیید" in row.inner_text():
                req_id = m.group(1)
                break
        req_id = os.environ.get("REQ_ID") or req_id or "1"
        print("  ↪ شناسهٔ پرونده:", req_id)

        # ---------- ۰۴ ثبت درخواست
        page.goto(f"{BASE}/Requests/Ingest", wait_until="networkidle")
        shot(page, "04-ingest", "ثبت درخواست مشتری (ورودی JSON)", [
            "فایل JSON مشتری را می‌چسبانید یا فایل را انتخاب می‌کنید؛ ورود دستی هم ممکن است.",
            "«پیش‌نمایش» چیزی ذخیره نمی‌کند؛ فقط شناسهٔ نصب و مشخصات را نشان می‌دهد. ذخیره، شمارهٔ درخواست می‌سازد.",
        ], [
            (1, "select[name='mode']", "روش ورود: چسباندن / آپلود فایل / دستی"),
            (2, "textarea", "متن JSON درخواست"),
            (3, "button:has-text('پیش‌نمایش')", "پیش‌نمایش (بدون ذخیره)"),
            (4, "button:has-text('ذخیره')", "ذخیره در صف"),
        ])

        # ---------- ۰۵ پرونده
        page.goto(f"{BASE}/Requests/Details?id={req_id}", wait_until="networkidle")
        shot(page, "05-details", "پروندهٔ درخواست", [
            "همهٔ مسیرهای چرخه از این صفحه شروع می‌شود: مشتری → تأیید هویت → تأییدشده → پیش‌نویس → امضا → تحویل.",
            "دکمه‌های بالای صفحه گام‌آگاه‌اند: تا وضعیت «تأییدشده» نشود، دکمهٔ «پیش‌نویس امضا» نمایش داده نمی‌شود؛ کارت «قدم بعدی» می‌گوید الان چه کاری لازم است.",
            "دکمه‌ها بر اساس نقش هم فیلتر می‌شوند (اپراتور دکمهٔ امضا ندارد).",
        ], [
            (1, ".card:has-text('قدم بعدی')", "کارت «قدم بعدی» (۶ گام با نشان انجام‌شدن)"),
            (2, ".page-head .btn.ghost[href*='Draft']", "پیش‌نویس امضا"),
            (3, ".page-head .btn[href*='Sign']", "امضا"),
            (4, ".card:has-text('تغییر وضعیت')", "تغییر وضعیت / وصل‌کردن مشتری"),
        ])

        # ---------- ۰۶ هویت (ثبت واقعی، چون امضا به آن وابسته است)
        page.goto(f"{BASE}/Requests/Identity?id={req_id}", wait_until="networkidle")
        shot(page, "06-identity", "بررسی هویت مشتری", [
            "روش تأیید (تلفن/ایمیل/پیامک/حضوری) و یادداشت بررسی ثبت می‌شود و در ممیزی می‌ماند.",
            "تا این گام ثبت نشود، صفحهٔ امضا اجازهٔ صدور نمی‌دهد.",
        ], [
            (1, "select", "روش تأیید هویت"),
            (2, "textarea, input[name='note']", "یادداشت بررسی"),
            (3, "button:has-text('ثبت تأیید هویت')", "ثبت بررسی هویت"),
        ])
        try:
            tok = page.locator("input[name='__RequestVerificationToken']").first.get_attribute("value")
            resp = page.request.post(f"{BASE}/Requests/Identity?id={req_id}", form={
                "__RequestVerificationToken": tok,
                "id": req_id,
                "method": "Phone",
                "verifiedWith": page.locator("input[name='verifiedWith']").first.input_value(),
                "verifiedAt": page.locator("input[name='verifiedAt']").first.input_value(),
                "note": "تأیید تلفنی با مدیر IT مشتری — نمونهٔ راهنما",
            })
            body = resp.text()
            ok = "ثبت شد" in body or "تأیید" in body
            print("  ↪ ثبت هویت:", resp.status, "| ok:", ok)
        except Exception as e:
            print("  ! ثبت هویت:", e)

        # ---------- ۰۷ پیش‌نویس (پر کردن + ذخیره)
        page.goto(f"{BASE}/Requests/Draft?id={req_id}", wait_until="networkidle")
        shot(page, "07-draft", "پیش‌نویس لایسنس", [
            "بستهٔ آماده (FIN/HR/WAREHOUSE/OFFICE) یا ماژول‌های صریح، مدت، حالت استقرار و سقف کاربر.",
            "«پیش‌نمایش ابزار» خروجی LicenseTool را بدون امضا نشان می‌دهد؛ «ذخیره» مبنای امضا می‌شود.",
        ], [
            (1, "select[name='presets']", "بستهٔ آماده"),
            (2, "select[name='termMode']", "حالت مدت: ماه‌شمار / تاریخ پایان / دائمی"),
            (3, "input[name='months']", "مدت (ماه)"),
            (4, "select[name='deployment']", "حالت استقرار: OnPrem یا Saas"),
            (5, "input[name='userMax']", "سقف کاربر"),
            (6, "button:has-text('ذخیره')", "ذخیرهٔ پیش‌نویس"),
        ])
        try:
            tok = page.locator("input[name='__RequestVerificationToken']").first.get_attribute("value")
            pairs = [("__RequestVerificationToken", tok), ("id", req_id), ("presets", "FIN"),
                     ("termMode", "months"), ("months", "12"), ("until", ""), ("updatesUntil", ""),
                     ("deployment", "OnPrem"), ("userMode", "Named"), ("userMax", "10"),
                     ("exempt", ""), ("appliesTo", "Global"), ("note", "پیش‌نویس نمونهٔ راهنما")]
            resp = page.request.post(f"{BASE}/Requests/Draft?id={req_id}&handler=Save", form=dict(pairs))
            print("  ↪ ذخیرهٔ پیش‌نویس:", resp.status,
                  "| هشدار:", "مشتری" in resp.text() and "مشتری این درخواست مشخص نیست" in resp.text())
        except Exception as e:
            print("  ! ذخیرهٔ پیش‌نویس:", e)

        # ---------- ۰۸ امضا (فرم را پر می‌کنیم و واقعاً امضا می‌شود)
        page.goto(f"{BASE}/Requests/Sign?id={req_id}", wait_until="networkidle")
        shot(page, "08-sign", "امضا و صدور فایل .lic", [
            "Key ID باید با کلیدی که مسیرش در تنظیمات است یکی باشد؛ گذرواژهٔ کلید فقط برای همین عملیات است.",
            "تیک بازبینی اجباری است؛ تیک «فوگرفتن» لایسنس‌های جاری همین نصب را باطل می‌کند.",
            "پس از امضا، پنل فایل را فوراً با کلید عمومی بازبینی می‌کند و رخداد SIGN در ممیزی ثبت می‌شود.",
        ], [
            (1, "input[name='keyId']", "Key ID کلید امضا"),
            (2, "input[name='keyPassword']", "گذرواژهٔ کلید خصوصی"),
            (3, "input[name='confirm']", "تیک بازبینی نهایی (اجباری)"),
            (4, "input[name='supersede']", "فوگرفتن لایسنس‌های جاری همین نصب"),
            (5, "button:has-text('امضا و صدور')", "امضا و صدور"),
        ])
        signed = False
        try:
            tok = page.locator("input[name='__RequestVerificationToken']").first.get_attribute("value")
            resp = page.request.post(f"{BASE}/Requests/Sign?id={req_id}", form={
                "__RequestVerificationToken": tok,
                "id": req_id,
                "keyId": "dev-test",
                "keyPassword": os.environ.get("LICENSETOOL_KEY_PASSWORD", ""),
                "confirm": "on",
            })
            body = resp.text()
            signed = "خطا" not in body[:600] and ("صادر" in body or "سریال" in body or "امضا" in body)
            print("  ↪ امضا:", resp.status, "| موفق:", signed)
        except Exception as e:
            print("  ! امضا:", e)

        # ---------- ۰۹ تحویل
        page.goto(f"{BASE}/Requests/Deliver?id={req_id}", wait_until="networkidle")
        shot(page, "09-deliver", "تحویل لایسنس به مشتری", [
            "سه روش: پیش‌نویس .eml (بدون SMTP هم کار می‌کند)، ارسال با SMTP، یا ثبت تحویل دستی.",
            "هر سه، گیرنده و روش را در پرونده ثبت می‌کنند و وضعیت را به «تحویل‌شده» می‌برند.",
        ], [
            (1, "button:has-text('eml')", "ساخت پیش‌نویس ایمیل (.eml)"),
            (2, "input[name='recipient']", "گیرنده"),
            (3, "button:has-text('ارسال ایمیل')", "ارسال با SMTP (اگر تنظیم شده باشد)"),
            (4, "button:has-text('ثبت تحویل')", "ثبت تحویل دستی"),
        ])

        # ---------- ۱۰ فهرست لایسنس‌ها
        page.goto(f"{BASE}/Licenses", wait_until="networkidle")
        shot(page, "10-licenses", "لایسنس‌های صادرشده", [
            "هر ردیف: سریال، مشتری، شناسهٔ نصب، کلید امضا، اعتبار و وضعیت.",
            "از همین صفحه فایل .lic دانلود یا جزئیات باز می‌شود.",
        ], [
            (1, "table", "فهرست لایسنس‌ها"),
            (2, "table tbody tr:first-child a[href*='Details']", "جزئیات و دانلود .lic"),
        ])

        # ---------- ۱۱ جزئیات لایسنس
        page.goto(f"{BASE}/Licenses", wait_until="networkidle")
        link = page.locator("table tbody tr:first-child a[href*='Details']")
        if link.count():
            link.first.click()
            page.wait_for_load_state("networkidle")
        shot(page, "11-license-detail", "جزئیات لایسنس و دانلود فایل", [
            "سریال، شناسهٔ نصب، ماژول‌ها، بازهٔ اعتبار و هش فایل.",
            "همین فایل را برای مشتری می‌فرستید؛ هر تغییری در آن، امضا را باطل می‌کند.",
        ], [
            (1, "a[href*='Download'], button:has-text('دانلود')", "دانلود فایل .lic"),
            (2, "table, .card", "خلاصهٔ لایسنس"),
        ])

        # ---------- ۱۲ تمدیدها
        page.goto(f"{BASE}/Renewals", wait_until="networkidle")
        shot(page, "12-renewals", "تمدیدهای نزدیک (۶۰ روز)", [
            "لایسنس‌هایی که تا ۶۰ روز آینده منقضی می‌شوند؛ برای پیگیری تمدید و ارسال پیشنهاد.",
        ], [(1, "table, .card", "فهرست تمدیدها")])

        # ---------- ۱۳ ممیزی
        page.goto(f"{BASE}/Audit", wait_until="networkidle")
        shot(page, "13-audit", "ممیزی زنجیره‌هش", [
            "هر رخداد (ورود، تأیید، امضا، تحویل…) به هش ردیف قبلی زنجیر می‌شود.",
            "دکمهٔ «بررسی زنجیره» سالم بودن کل زنجیره را می‌سنجد؛ هر دست‌کاری لو می‌رود.",
        ], [
            (1, "button:has-text('بررسی')", "بررسی زنجیره"),
            (2, "table", "رخدادها"),
        ])

        # ---------- ۱۴ پشتیبان
        page.goto(f"{BASE}/Backup", wait_until="networkidle")
        shot(page, "14-backup", "پشتیبان‌گیری", [
            "پیش از هر تغییر بزرگ یک پشتیبان بگیرید؛ فایل‌ها در پوشهٔ backups ذخیره می‌شوند.",
        ], [
            (1, "button:has-text('پشتیبان')", "ساخت پشتیبان"),
            (2, "table", "پشتیبان‌های موجود"),
        ])

        # ---------- ۱۵ تنظیمات
        page.goto(f"{BASE}/Settings", wait_until="networkidle")
        shot(page, "15-settings", "تنظیمات: کاربران، SMTP، بسته‌ها", [
            "کاربران و نقش‌ها (Operator / Signer / Admin)، تنظیم SMTP و بسته‌های آماده.",
            "گذرواژهٔ SMTP فقط از متغیر محیطی خوانده می‌شود و در این صفحه ذخیره نمی‌شود.",
        ], [(1, ".card", "کارت‌های تنظیمات")])

        browser.close()

    (SHOTS / "steps.json").write_text(json.dumps(STEPS, ensure_ascii=False, indent=2), encoding="utf-8")
    print("تعداد گام:", len(STEPS), "| امضا انجام شد:", signed)


if __name__ == "__main__":
    main()
