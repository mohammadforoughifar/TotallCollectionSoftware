#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
آزمون جامع ایزولاسیون نوتیفیکیشن‌ها و جستجوی پیشرفته / OCR در ماژول آرشیو اسناد
"""

import os
import sys
import time
import requests
import io
from zipfile import ZipFile

BASE_URL = os.environ.get("API_BASE", "http://localhost:5100")

def log(ok, msg):
    symbol = "  [PASS]" if ok else "  [FAIL]"
    print(f"{symbol} {msg}")
    if not ok:
        sys.exit(1)

def get_token(username, password):
    r = requests.post(f"{BASE_URL}/api/auth/login", json={"username": username, "password": password})
    if r.status_code == 200:
        return r.json().get("token")
    return None

def main():
    print("شروع آزمون ایزولاسیون نوتیفیکیشن‌ها و جستجوی پیشرفته / OCR...")
    admin_token = get_token("admin", "admin")
    log(admin_token is not None, "ورود کاربر admin موفق")

    headers_admin = {"Authorization": f"Bearer {admin_token}"}

    # =========================================================================
    # ۱) آزمون ایزولاسیون نوتیفیکیشن‌ها بین کاربران مختلف (Notification Isolation)
    # =========================================================================
    print("\n=== ۱) بررسی ایزولاسیون نوتیفیکیشن‌ها بین کاربران ===")

    # ساخت دو کاربر آزمایشی
    ts = int(time.time())
    u1_name = f"user_ntf_a_{ts}"
    u2_name = f"user_ntf_b_{ts}"

    r = requests.post(f"{BASE_URL}/api/users", json={
        "username": u1_name, "password": "Pass123!@#", "firstName": "کاربر", "lastName": "الف", "role": "Operator", "isActive": True
    }, headers=headers_admin)
    log(r.status_code == 200, f"ساخت کاربر {u1_name}")
    
    r = requests.post(f"{BASE_URL}/api/users", json={
        "username": u2_name, "password": "Pass123!@#", "firstName": "کاربر", "lastName": "ب", "role": "Operator", "isActive": True
    }, headers=headers_admin)
    log(r.status_code == 200, f"ساخت کاربر {u2_name}")

    r_users = requests.get(f"{BASE_URL}/api/users", headers=headers_admin).json()
    u1_id = next(u["id"] for u in r_users if u["username"] == u1_name)
    u2_id = next(u["id"] for u in r_users if u["username"] == u2_name)

    token_u1 = get_token(u1_name, "Pass123!@#")
    token_u2 = get_token(u2_name, "Pass123!@#")
    headers_u1 = {"Authorization": f"Bearer {token_u1}"}
    headers_u2 = {"Authorization": f"Bearer {token_u2}"}

    # در ابتدا هر دو کاربر باید ۰ نوتیف داشته باشند
    r1 = requests.get(f"{BASE_URL}/api/notifications", headers=headers_u1)
    r2 = requests.get(f"{BASE_URL}/api/notifications", headers=headers_u2)
    log(r1.status_code == 200 and len(r1.json()) == 0, "کاربر الف در ابتدا هیچ نوتیفیکیشنی ندارد (۰ عدد)")
    log(r2.status_code == 200 and len(r2.json()) == 0, "کاربر ب در ابتدا هیچ نوتیفیکیشنی ندارد (۰ عدد)")

    # ساخت یک پوشه و یک مدرک با تاییدکننده = کاربر الف (u1)
    r = requests.post(f"{BASE_URL}/api/doc-archive/folders", json={"name": f"پوشه نوتیف {ts}"}, headers=headers_admin)
    fid = r.json()["id"]

    r = requests.post(f"{BASE_URL}/api/doc-archive/documents", json={
        "folderId": fid,
        "title": f"مدرک تست نوتیف {ts}",
        "code": f"NTF-DOC-{ts}",
        "approvers": [{"userId": u1_id, "order": 1}],
        "firstVersionActive": False
    }, headers=headers_admin)
    log(r.status_code == 200, "ثبت مدرک در آرشیو با انتصاب تاییدکننده به کاربر الف")

    # بررسی نوتیفیکیشن‌ها: کاربر الف باید نوتیف گردش دریافت کرده باشد، ولی کاربر ب نباید چیزی ببیند!
    r1 = requests.get(f"{BASE_URL}/api/notifications", headers=headers_u1)
    r2 = requests.get(f"{BASE_URL}/api/notifications", headers=headers_u2)
    n1 = r1.json()
    n2 = r2.json()

    log(len(n1) == 1, f"کاربر الف دقیقاً ۱ نوتیفیکیشن دریافت کرد: «{n1[0]['title']}»")
    log(len(n2) == 0, "کاربر ب هیچ نوتیفیکیشنی از کاربر الف دریافت نکرد (ایزولاسیون ۱۰۰٪)")

    # کاربر ب نباید بتواند نوتیفیکیشن کاربر الف را بخواند یا حذف کند
    ntf_id = n1[0]["id"]
    r_hack = requests.post(f"{BASE_URL}/api/notifications/{ntf_id}/read", headers=headers_u2)
    log(r_hack.status_code in [404, 401, 403], "کاربر ب اجازه خواندن یا دستکاری نوتیفیکیشن کاربر الف را ندارد (امنیت تایید شد)")

    # =========================================================================
    # ۲) آزمون جستجوی هوشمند و یکپارچه در محتوا و OCR (DocArchive Smart Search)
    # =========================================================================
    print("\n=== ۲) آزمون جستجوی پیشرفته و یکپارچه تمام‌متن / OCR ===")

    # ثبت یک سند با پیوست Word و پیوست متنی
    r = requests.post(f"{BASE_URL}/api/doc-archive/documents", json={
        "folderId": fid,
        "title": f"پروژه سد مخزنی شهید سلیمانی {ts}",
        "code": f"DAM-PRJ-{ts}",
        "customerCode": "MOE-WATER-2026",
        "description": "اسناد هیدرولیک و ژئوتکنیک",
        "firstVersionActive": True
    }, headers=headers_admin)
    log(r.status_code == 200, "ثبت مدرک سد مخزنی")
    doc_id = r.json()["id"]

    doc_detail = requests.get(f"{BASE_URL}/api/doc-archive/documents/{doc_id}", headers=headers_admin).json()
    v1_id = doc_detail["versions"][0]["id"]

    # ساخت فایل متنی با واژگان کلیدی خاص
    txt_content = f"مشخصات فنی توربین‌های هیدروالکتریک مدل کاپلان ۲۵۰ مگاوات شماره قرارداد {ts}"
    txt_file = io.BytesIO(txt_content.encode('utf-8'))
    r = requests.post(f"{BASE_URL}/api/attachments/DocVersion/{v1_id}",
                      files={"file": ("turbine_specs.txt", txt_file, "text/plain")},
                      headers=headers_admin)
    log(r.status_code == 200, "بارگذاری فایل مشخصات فنی توربین")

    # ساخت فایل Word ساختگی (.docx)
    docx_xml = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
        <w:body>
            <w:p><w:r><w:t>گزارش آزمایشگاه ژئوتکنیک خاک و سنگ پی سد شرکت مهندسین مشاور آب نیرو</w:t></w:r></w:p>
            <w:p><w:r><w:t>ضریب اطمینان پایداری برابر با ۱.۸۵ محاسبه گردید شناسه {ts}.</w:t></w:r></w:p>
        </w:body>
    </w:document>"""
    docx_stream = io.BytesIO()
    with ZipFile(docx_stream, 'w') as z:
        z.writestr("word/document.xml", docx_xml.encode('utf-8'))
        z.writestr("[Content_Types].xml", b"<Types></Types>")
    docx_stream.seek(0)

    r = requests.post(f"{BASE_URL}/api/attachments/DocVersion/{v1_id}",
                      files={"file": ("geotech_report.docx", docx_stream, "application/vnd.openxmlformats-officedocument.wordprocessingml.document")},
                      headers=headers_admin)
    log(r.status_code == 200, "بارگذاری فایل گزارش ژئوتکنیک Word")

    # زمان کوتاه برای پردازش ایندکس
    time.sleep(1)

    # تست ۱: جستجوی عمومی در کادر اصلی (Search) با کلمه موجود فقط در محتوای فایل متنی
    r = requests.post(f"{BASE_URL}/api/doc-archive/search", json={
        "search": "هیدروالکتریک"
    }, headers=headers_admin)
    log(r.status_code == 200 and len(r.json()) >= 1, "جستجوی هوشمند در کادر اصلی: کلمه «هیدروالکتریک» موجود در متن فایل پیدا شد")
    doc_found = next((d for d in r.json() if d["id"] == doc_id), None)
    log(doc_found is not None, "سند مربوطه در نتایج جستجوی عمومی قرار دارد")
    log(doc_found is not None and doc_found.get("contentSnippet") is not None, f"اسنیپت استخراج‌شده: «{doc_found.get('contentSnippet', '')}»")

    # تست ۲: جستجو با کاراکترهای عربی / نرمال‌سازی («ژئوتكنيك» با ک عربی و ی عربی به جای ژئوتکنیک)
    r = requests.post(f"{BASE_URL}/api/doc-archive/search", json={
        "search": "ژئوتكنيك آب نيرو" # با ک و ی عربی
    }, headers=headers_admin)
    log(r.status_code == 200 and len(r.json()) >= 1, "جستجو با حروف عربی (ك، ي) و نرمال‌سازی خودکار موفق بود")

    # تست ۳: جستجوی ترکیبی چندواژه‌ای (Multi-term token search)
    r = requests.post(f"{BASE_URL}/api/doc-archive/search", json={
        "search": "ضریب اطمینان پایداری"
    }, headers=headers_admin)
    log(r.status_code == 200 and any(d["id"] == doc_id for d in r.json()), "جستجوی چندواژه‌ای عبارت «ضریب اطمینان پایداری» در فایل Word موفق بود")

    # تست ۴: فیلتر اختصاصی نوع فایل (Word Only)
    r = requests.post(f"{BASE_URL}/api/doc-archive/search", json={
        "search": "مهندسین مشاور",
        "fileTypes": ["word"]
    }, headers=headers_admin)
    log(r.status_code == 200 and any(d["id"] == doc_id for d in r.json()), "فیلتر ترکیبی (جستجو در متن + فقط اسناد Word) موفق بود")

    print("\n==========================================")
    print("تمامی تست‌های ایزولاسیون نوتیفیکیشن و جستجوی پیشرفته / OCR با موفقیت ۱۰۰٪ پاس شدند ✔")
    print("==========================================")

if __name__ == "__main__":
    main()
