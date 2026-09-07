#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
تست یکپارچه‌سازی دو قابلیت جدید آرشیو اسناد و مدارک:
۱) دانلود درختی پوشه‌ها در قالب فایل ZIP (Folder Tree Export) به همراه شناسنامه اکسل
۲) یکپارچه‌سازی کامل آرشیو با ماژول‌های سامانه (ERP Integration)
"""

import os
import sys
import io
import time
import requests
import zipfile

BASE_URL = os.environ.get("API_BASE", "http://127.0.0.1:5100")
token = None

passed = 0
failed = 0

def log(ok, msg):
    global passed, failed
    if ok:
        passed += 1
        print(f"  [PASS] {msg}")
    else:
        failed += 1
        print(f"  [FAIL] {msg}", file=sys.stderr)

def login():
    global token
    r = requests.post(f"{BASE_URL}/api/auth/login", json={"username": "admin", "password": "admin"})
    if r.status_code == 200:
        token = r.json().get("token")
        log(True, "ورود کاربر admin موفق")
        return True
    else:
        log(False, f"ورود کاربر admin ناموفق: {r.text}")
        return False

def headers():
    return {"Authorization": f"Bearer {token}"}

def run_tests():
    print("\n=== ۱) آماده‌سازی ساختار پوشه‌های درختی و مدارک ===")
    ts = int(time.time())
    root_name = f"پروژه‌های عمرانی {ts}"
    sub_name = f"فاز ۱ — پالایشگاه {ts}"

    # ۱. ساخت پوشه ریشه پروژه
    r = requests.post(f"{BASE_URL}/api/doc-archive/folders", json={
        "name": root_name,
        "description": "پوشه اصلی پروژه‌های سال ۱۴۰۵",
        "isPublic": True,
        "publicCanDownload": True
    }, headers=headers())
    log(r.status_code == 200, f"ساخت پوشه ریشه «{root_name}»")
    root_folder_id = r.json()["id"]

    # ۲. ساخت زیرپوشه فاز ۱
    r = requests.post(f"{BASE_URL}/api/doc-archive/folders", json={
        "name": sub_name,
        "parentId": root_folder_id,
        "description": "اسناد فاز اول",
        "isPublic": True,
        "publicCanDownload": True
    }, headers=headers())
    log(r.status_code == 200, f"ساخت زیرپوشه «{sub_name}»")
    sub_folder_id = r.json()["id"]

    # ۳. ثبت سند اول در زیرپوشه
    doc1_code = f"DOC-TEST-PRJ-{int(time.time())}"
    r = requests.post(f"{BASE_URL}/api/doc-archive/documents", json={
        "folderId": sub_folder_id,
        "title": "نقشه تاسیسات مکانیکی و پایپینگ",
        "code": doc1_code,
        "customerCode": "CUST-PL-101",
        "description": "نقشه تفصیلی خطوط لوله",
        "isPublic": True,
        "publicCanDownload": True
    }, headers=headers())
    log(r.status_code == 200, f"ثبت سند «{doc1_code}» در زیرپوشه")
    doc1_id = r.json()["id"]

    # بارگذاری پیوست متنی و PDF برای نسخه ۱ سند
    r = requests.get(f"{BASE_URL}/api/doc-archive/documents/{doc1_id}", headers=headers())
    v1_id = r.json()["versions"][0]["id"]

    sample_text = "نقشه تاسیسات مکانیکی فاز اول پالایشگاه با فشار کاری ۱۶ بار"
    r = requests.post(f"{BASE_URL}/api/attachments/DocVersion/{v1_id}",
                      files={"file": ("piping_specs.txt", sample_text.encode("utf-8"), "text/plain")},
                      headers=headers())
    log(r.status_code == 200, "بارگذاری پیوست piping_specs.txt برای سند")

    print("\n=== ۲) تست دانلود درختی ZIP (Folder Tree ZIP Export) ===")
    # تست خروجی ZIP برای پوشه با زیرپوشه‌ها
    zip_url = f"{BASE_URL}/api/doc-archive/folders/{root_folder_id}/export-zip?includeSubfolders=true&onlyActiveVersions=true&includeManifest=true"
    r = requests.get(zip_url, headers=headers())
    log(r.status_code == 200, "دریافت فایل ZIP پوشه درختی با موفقیت")
    log(r.headers.get("Content-Type", "").startswith("application/zip"), "نوع محتوای بازگشتی application/zip است")

    # بررسی ساختار فایل ZIP در حافظه
    with zipfile.ZipFile(io.BytesIO(r.content)) as z:
        file_list = z.namelist()
        print(f"  [INFO] فایل‌های درون ZIP: {file_list}")

        has_manifest_excel = "_فهرست_شناسنامه_اسناد.xlsx" in file_list
        log(has_manifest_excel, "فایل اکسل شناسنامه اسناد (_فهرست_شناسنامه_اسناد.xlsx) در ریشه ZIP موجود است")

        has_readme = "_راهنمای_محتوای_بایگانی.txt" in file_list
        log(has_readme, "فایل راهنمای محتوای بایگانی (_راهنمای_محتوای_بایگانی.txt) موجود است")

        # بررسی وجود فایل پیوست در مسیر درختی صحیح
        has_attachment_path = any("piping_specs.txt" in path for path in file_list)
        log(has_attachment_path, "پیوست در مسیر سلسله‌مراتبی پوشه‌ها درون ZIP قرار دارد")

    # تست خروجی کل آرشیو ZIP
    all_zip_url = f"{BASE_URL}/api/doc-archive/export-zip?includeSubfolders=true&onlyActiveVersions=true&includeManifest=true"
    r = requests.get(all_zip_url, headers=headers())
    log(r.status_code == 200, "اکسپورت درختی کل آرشیو مجاز در قالب ZIP")

    print("\n=== ۳) تست یکپارچه‌سازی آرشیو با ماژول‌های ERP (ERP Integration) ===")
    # ۳.۱ تست لوک‌آپ موجودیت‌های ماژول‌ها
    modules_to_test = ["Projects", "Hr", "ItAssets", "Invoicing", "KarFarma", "Repairs"]
    for mod in modules_to_test:
        r = requests.get(f"{BASE_URL}/api/doc-archive/entity-lookups/{mod}", headers=headers())
        log(r.status_code == 200, f"لوک‌آپ و جستجوی موجودیت‌های ماژول {mod} (یافت‌شده: {len(r.json())} رکورد)")

    # ۳.۲ ثبت اتصال بین سند آرشیو و یک پروژه ERP
    r = requests.post(f"{BASE_URL}/api/doc-archive/entity-links", json={
        "documentId": doc1_id,
        "module": "Projects",
        "entityId": 101,
        "entityCode": "PRJ-2026-101",
        "entityTitle": "پروژه احداث خط لوله فاز ۱",
        "note": "پیوست نقشه اجرایی مصوب کارفرما"
    }, headers=headers())
    log(r.status_code == 200, "اتصال سند آرشیو به پروژه PRJ-2026-101 در ماژول Projects")
    link_id = r.json()["id"]

    # ۳.۳ دریافت اسناد متصل به پروژه از دیدگاه ماژول Projects
    r = requests.get(f"{BASE_URL}/api/doc-archive/entity-links/Projects/101", headers=headers())
    log(r.status_code == 200 and len(r.json()) == 1, "دریافت اسناد آرشیو متصل به پروژه ۱۰۱")
    linked_doc_item = r.json()[0]
    log(linked_doc_item["documentId"] == doc1_id, "سند متصل با شناسه سند ایجادشده مطابقت دارد")
    log(len(linked_doc_item["attachments"]) >= 1, "اطلاعات پیوست‌های سند در پنل ERP در دسترس است")

    # ۳.۴ تست ثبت سریع سند آرشیو و اتصال خودکار از داخل ماژول (Quick Create Linked Doc)
    quick_doc_code = f"DOC-QUICK-HR-{int(time.time())}"
    r = requests.post(f"{BASE_URL}/api/doc-archive/entity-links/quick-create", json={
        "folderId": sub_folder_id,
        "title": "قرارداد کارگزینی پرسنل",
        "code": quick_doc_code,
        "customerCode": "EMP-9001",
        "description": "قرارداد استخدامی سال جاری",
        "module": "Hr",
        "entityId": 1,
        "entityCode": "admin",
        "entityTitle": "کاربر مدیر سیستم",
        "linkNote": "قرارداد اصلی امضاشده"
    }, headers=headers())
    log(r.status_code == 200, f"ثبت سریع سند «{quick_doc_code}» و اتصال خودکار به کاربر در ماژول Hr")
    quick_res = r.json()
    quick_doc_id = quick_res["documentId"]
    quick_link_id = quick_res["linkId"]

    # بررسی اتصال ایجادشده برای کاربر Hr
    r = requests.get(f"{BASE_URL}/api/doc-archive/entity-links/Hr/1", headers=headers())
    log(r.status_code == 200 and len(r.json()) >= 1, "دریافت اسناد آرشیو متصل به پرونده کاربر در ماژول Hr")

    # ۳.۵ تست دریافت جزئیات مدرک و بررسی وجود EntityLinks در DocumentDto
    r = requests.get(f"{BASE_URL}/api/doc-archive/documents/{doc1_id}", headers=headers())
    log(r.status_code == 200 and len(r.json().get("entityLinks", [])) >= 1, "نمایش اتصالات ERP در جزئیات کامل مدرک (DocumentView)")

    # ۳.۶ فیلتر مدارک آرشیو بر اساس ماژول متصل ERP در جستجوی پیشرفته
    r = requests.post(f"{BASE_URL}/api/doc-archive/search", json={
        "linkedModule": "Projects"
    }, headers=headers())
    log(r.status_code == 200, "جستجو و فیلتر مدارک آرشیو با شرط اتصال به ماژول Projects")
    matched_prj_docs = [d for d in r.json() if d["id"] == doc1_id]
    log(len(matched_prj_docs) >= 1, "سند متصل به پروژه در نتایج فیلتر ماژول قرار دارد")

    # ۳.۷ حذف اتصال و بررسی لاگ تاریخچه
    r = requests.delete(f"{BASE_URL}/api/doc-archive/entity-links/{link_id}", headers=headers())
    log(r.status_code == 200, "حذف اتصال سند از پروژه ERP")

    r = requests.get(f"{BASE_URL}/api/doc-archive/documents/{doc1_id}", headers=headers())
    logs = r.json().get("logs", [])
    has_erp_log = any("ERPLink" in l.get("action", "") for l in logs)
    log(has_erp_log, "ثبت رویدادهای ERPLinkAdded و ERPLinkRemoved در تاریخچه سند")

if __name__ == "__main__":
    print("شروع تست‌های دانلود درختی ZIP و یکپارچه‌سازی ERP...")
    if not login():
        sys.exit(1)
    run_tests()
    print(f"\n==========================================")
    print(f"نتیجه تست‌ها: {passed} موفق | {failed} ناموفق")
    print(f"==========================================")
    if failed > 0:
        sys.exit(1)
