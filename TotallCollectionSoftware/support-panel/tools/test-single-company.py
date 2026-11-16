#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
آزمون «هر لایسنس = یک شرکت» روی پنل در حال اجرا.

چه چیزی را ثابت می‌کند؟
  ۱) شرکت (شناسه + نام) از «داخل فایل درخواست» خوانده می‌شود و در پرونده ثبت می‌گردد.
  ۲) صفحهٔ پیش‌نویس، شرکت را **فقط-خواندنی** نشان می‌دهد (کارت «یک شرکت — یک لایسنس»)
     و امکان عوض کردن شناسه از فرم را نمی‌دهد.
  ۳) مقدار ارسالی دستی `companies` با شناسهٔ دیگر، هویت شرکت را تغییر نمی‌دهد.
  ۴) سرویس پیش‌نویس، پیش‌نویس چندشرکتی را رد می‌کند (وقتی درخواست شرکت ندارد).

اجرا (پنل باید بالا باشد):
    BASE=http://127.0.0.1:5399 USER=admin PASS=... python3 tools/test-single-company.py
در حالت پیش‌نمایش، به‌جای PASS می‌توانید دکمهٔ «ورود سریع دمو» را در مرورگر بزنید؛
این اسکریپت فقط با نام کاربری/گذرواژه کار می‌کند.
"""
import html
import json
import os
import re
import sys

import requests

BASE = os.environ.get("BASE", "http://127.0.0.1:5399").rstrip("/")
USER = os.environ.get("USER", "admin")
PASS = os.environ.get("PASS", "")
INSTALLATION = os.environ.get("INSTALLATION", "single-company-guard-01")

PASSED = FAILED = 0


def ok(msg):
    global PASSED
    PASSED += 1
    print(f"  ✔ {msg}")


def bad(msg):
    global FAILED
    FAILED += 1
    print(f"  ✖ {msg}")


def token(html):
    tag = re.search(r'<input[^>]*name="__RequestVerificationToken"[^>]*>', html)
    if not tag:
        raise RuntimeError("توکن آنتی‌فورجری در صفحه پیدا نشد")
    return re.search(r'value="([^"]+)"', tag.group(0)).group(1)


def alerts(page):
    return [re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", "", a))).strip()
            for a in re.findall(r'<div class="alert[^"]*">(.*?)</div>', page, re.S)]


def main():
    s = requests.Session()
    login = s.get(f"{BASE}/Login", timeout=20)
    r = s.post(f"{BASE}/Login", timeout=20, data={
        "userName": USER, "password": PASS,
        "__RequestVerificationToken": token(login.text)})
    if "/Login" in r.url:
        print("✖ ورود ناموفق — USER/PASS را بدهید.")
        return 2

    # ---------- ۱) ثبت درخواست با شرکت داخل فایل
    payload = {
        "requestVersion": 2, "requestType": "NewActivation",
        "installationId": INSTALLATION, "customerName": "مشتری آزمون تک‌شرکتی",
        "contactName": "آزمون", "contactPhone": "09120000000",
        "companyId": "C-9001", "companyName": "شرکت آزمون یک",
        "deployment": "OnPrem", "interestedModules": ["letters"], "users": 5,
    }
    page = s.get(f"{BASE}/Requests/Ingest", timeout=20)
    r = s.post(f"{BASE}/Requests/Ingest", timeout=60, data={
        "__RequestVerificationToken": token(page.text), "mode": "paste",
        "rawChannel": "Manual", "pastedJson": json.dumps(payload, ensure_ascii=False)})
    m = re.search(r"[?&]id=(\d+)", r.url)
    if not m:
        bad("ثبت درخواست: به پرونده هدایت نشد")
        return 1
    rid = m.group(1)
    ok(f"درخواست ثبت شد (id={rid})")

    details = s.get(f"{BASE}/Requests/Details?id={rid}", timeout=20)
    ok("پرونده شرکتِ فایل درخواست را نشان می‌دهد") if "C-9001" in details.text \
        else bad("پرونده شناسهٔ شرکت را نشان نداد")

    # وصل‌کردن مشتری موجود (پیش‌نیاز پیش‌نویس) — مثل کاری که پشتیبان در پرونده می‌کند
    cust = re.search(r'<select id="customerId" name="customerId">(.*?)</select>', details.text, re.S)
    if cust:
        options = [v for v in re.findall(r'<option value="(\d+)"', cust.group(1)) if v != "0"]
        if options:
            linked = s.post(f"{BASE}/Requests/Details?id={rid}&handler=LinkCustomer", timeout=30, data={
                "__RequestVerificationToken": token(details.text), "customerId": options[0]})
            ok("مشتری به پرونده وصل شد") if any("وصل" in a for a in alerts(linked.text)) \
                else bad("وصل‌کردن مشتری انجام نشد")
            details = s.get(f"{BASE}/Requests/Details?id={rid}", timeout=20)

    # ---------- ۲) پیش‌نویس: کارت فقط-خواندنی
    draft = s.get(f"{BASE}/Requests/Draft?id={rid}", timeout=20)
    ok("کارت «یک شرکت — یک لایسنس» در پیش‌نویس هست") if "company-single" in draft.text \
        else bad("کارت تک‌شرکتی در پیش‌نویس نیست")
    ok("فیلد متنی شرکت‌ها حذف شده (جلوگیری از ویرایش شناسه)") if 'id="companies"' not in draft.text \
        else bad("همچنان textarea شرکت‌ها وجود دارد")
    ok("شناسهٔ شرکت داخل صفحه آمده") if "C-9001" in draft.text else bad("شناسهٔ شرکت در صفحه نیست")

    # ---------- ۳) تلاش برای عوض کردن شرکت از فرم
    form = [("__RequestVerificationToken", token(draft.text)), ("id", rid),
            ("termMode", "months"), ("months", "12"), ("deployment", "OnPrem"),
            ("userMode", "Named"), ("userMax", "10"), ("appliesTo", "Global"),
            ("modules", "letters"), ("companies", "C-XXXX | شرکت قلابی | 3"),
            ("companySeats", "4"), ("note", "آزمون تک‌شرکتی")]
    saved = s.post(f"{BASE}/Requests/Draft?id={rid}&handler=Save", timeout=60, data=form)
    ok("ذخیره پاسخ موفق داد") if any("ذخیره شد" in a for a in alerts(saved.text)) \
        else bad(f"ذخیره ناموفق: {alerts(saved.text)[:1]}")

    after = s.get(f"{BASE}/Requests/Draft?id={rid}", timeout=20)
    ok("هویت شرکت تغییر نکرد (همان شرکت فایل درخواست)") \
        if "C-9001" in after.text and "C-XXXX" not in after.text \
        else bad("شناسهٔ شرکت با ورودی دستی عوض شد!")
    ok("سقف کاربر شرکت ذخیره شد") if 'value="4"' in after.text else bad("سقف کاربر شرکت ذخیره نشد")

    print(f"\nنتیجه: PASSED={PASSED} FAILED={FAILED}")
    return 0 if FAILED == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
