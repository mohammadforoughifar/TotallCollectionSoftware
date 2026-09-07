#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
تست یکپارچه‌سازی سیستم‌های جدید آرشیو اسناد:
۱) مدیریت تگ‌ها و الصاق به مدارک
۲) استخراج تمام‌متن (Full-Text) از فایل‌های متنی، Word و PDF
۳) پردازش OCR و ثبت در ایندکس
۴) فیلترهای ترکیبی و پیشرفته (Multi-Filter)
۵) بازگردانی اسنیپت (Snippet) و هایلایت در نتایج جستجو
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
    ts = int(time.time())
    print("\n=== ۱) تست مدیریت تگ‌ها (Tags CRUD) ===")
    r = requests.get(f"{BASE_URL}/api/doc-archive/tags", headers=headers())
    log(r.status_code == 200 and len(r.json()) >= 6, f"دریافت تگ‌های پیش‌فرض ({len(r.json())} تگ)")

    # ساخت تگ جدید
    r = requests.post(f"{BASE_URL}/api/doc-archive/tags", json={
        "name": f"پروژه عسلویه {ts}",
        "color": "#0284c7",
        "description": "اسناد مربوط به فاز ۲۴"
    }, headers=headers())
    log(r.status_code == 200, "ساخت تگ جدید «پروژه عسلویه»")
    new_tag = r.json()
    new_tag_id = new_tag.get("id")

    # ویرایش تگ
    r = requests.put(f"{BASE_URL}/api/doc-archive/tags/{new_tag_id}", json={
        "name": f"پروژه عسلویه و کنگان {ts}",
        "color": "#0369a1",
        "description": "اسناد فاز ۲۴ و ۲۵"
    }, headers=headers())
    log(r.status_code == 200, "ویرایش تگ جدید")

    print("\n=== ۲) ساخت پوشه و مدرک با تگ‌ها ===")
    r = requests.post(f"{BASE_URL}/api/doc-archive/folders", json={"name": f"قراردادهای پیمانکاری {ts}"}, headers=headers())
    log(r.status_code == 200, "ساخت پوشه قراردادها")
    folder_id = r.json()["id"]

    # ثبت مدرک با ۲ تگ
    r = requests.post(f"{BASE_URL}/api/doc-archive/documents", json={
        "folderId": folder_id,
        "title": f"قرارداد احداث خط لوله انتقال گاز {ts}",
        "code": f"CNT-GAS-{ts}",
        "customerCode": "CUST-9921",
        "description": "پروژه کلیدی منطقه پارس جنوبی",
        "tagIds": [new_tag_id, 1], # tag 1 = قرارداد
        "firstVersionActive": True
    }, headers=headers())
    log(r.status_code == 200, "ثبت مدرک جدید همراه با تگ‌ها")
    doc_id = r.json()["id"]

    # بررسی جزئیات مدرک
    r = requests.get(f"{BASE_URL}/api/doc-archive/documents/{doc_id}", headers=headers())
    doc_detail = r.json()
    log(len(doc_detail.get("tags", [])) >= 2, f"تگ‌های مدرک در جزئیات تایید شدند ({len(doc_detail.get('tags', []))} تگ)")
    v1_id = doc_detail["versions"][0]["id"]

    print("\n=== ۳) ساخت و آپلود فایل‌های متنی و Word برای استخراج متن ===")
    # ساخت فایل متنی
    txt_content = "متن محرمانه: شماره مجوز حفاری ۴۴۸۲۹ و تاییدیه مهندسی خط لوله گاز صادر گردید."
    files = {"file": ("license.txt", io.BytesIO(txt_content.encode("utf-8")), "text/plain")}
    r = requests.post(f"{BASE_URL}/api/attachments/DocVersion/{v1_id}", files=files, headers=headers())
    log(r.status_code == 200, "آپلود فایل متنی پیوست به ورژن ۱")

    # ساخت فایل Word (.docx) ساده در حافظه
    docx_buf = io.BytesIO()
    with zipfile.ZipFile(docx_buf, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr('[Content_Types].xml', '<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="xml" ContentType="application/xml"/><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
        z.writestr('_rels/.rels', '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
        doc_xml = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
    <w:p><w:r><w:t>صورت‌جلسه تحویل زمین کارگاه به پیمانکار شرکت آریا سازه</w:t></w:r></w:p>
    <w:p><w:r><w:t>مبلغ برآورد اولیه پروژه برابر ۱۲ میلیارد ریال می‌باشد.</w:t></w:r></w:p>
  </w:body>
</w:document>'''
        z.writestr('word/document.xml', doc_xml)
    docx_buf.seek(0)

    files = {"file": ("minutes.docx", docx_buf, "application/vnd.openxmlformats-officedocument.wordprocessingml.document")}
    r = requests.post(f"{BASE_URL}/api/attachments/DocVersion/{v1_id}", files=files, headers=headers())
    log(r.status_code == 200, "آپلود فایل Word (.docx) پیوست به ورژن ۱")

    # ساخت فایل تصویری اسکن‌شده برای OCR
    from PIL import Image, ImageDraw
    img = Image.new('RGB', (600, 160), color=(255, 255, 255))
    d = ImageDraw.Draw(img)
    d.text((25, 30), "WARRANTY GUARANTEE CODE 887766", fill=(0, 0, 0))
    d.text((25, 80), "SPECIAL SPECIFICATION ATTACHMENT", fill=(0, 0, 0))
    img_buf = io.BytesIO()
    img.save(img_buf, format="PNG")
    img_buf.seek(0)

    files = {"file": ("scan_guarantee.png", img_buf, "image/png")}
    r = requests.post(f"{BASE_URL}/api/attachments/DocVersion/{v1_id}", files=files, headers=headers())
    log(r.status_code == 200, "آپلود تصویر اسکن‌شده برای پردازش OCR")

    # فرصت کوتاه برای اتمام پردازش پس‌زمینه ایندکس
    time.sleep(1.5)

    print("\n=== ۴) بررسی ایندکس متنی و نتایج استخراج ===")
    r = requests.get(f"{BASE_URL}/api/doc-archive/documents/{doc_id}/extracted-texts", headers=headers())
    log(r.status_code == 200, "دریافت متون استخراج‌شده مدرک")
    extracted_list = r.json()
    log(len(extracted_list) >= 3, f"تعداد متون استخراج‌شده: {len(extracted_list)}")

    found_word = any(x["sourceType"] == "Word" and "صورت‌جلسه تحویل زمین" in x["extractedText"] for x in extracted_list)
    log(found_word, "استخراج متن از فایل Word (.docx) با موفقیت انجام شد")

    found_txt = any(x["sourceType"] == "Text" and "شماره مجوز حفاری" in x["extractedText"] for x in extracted_list)
    log(found_txt, "استخراج متن از فایل متنی با موفقیت انجام شد")

    found_ocr = any(x["sourceType"] == "ImageOcr" and "GUARANTEE" in x["extractedText"] for x in extracted_list)
    log(found_ocr, "استخراج متن و OCR تصویر با موفقیت انجام شد")

    print("\n=== ۵) تست جستجوی تمام‌متن (Full-Text Search) با Snippet ===")
    # ۱. جستجوی عبارت داخل فایل Word
    r = requests.post(f"{BASE_URL}/api/doc-archive/search", json={
        "contentSearch": "آریا سازه",
        "status": "active"
    }, headers=headers())
    hits = r.json()
    log(len(hits) > 0 and hits[0]["id"] == doc_id, "جستجوی تمام‌متن واژه «آریا سازه» در فایل Word")
    if len(hits) > 0:
        snip = hits[0].get("contentSnippet", "")
        log("آریا سازه" in snip or "تحویل زمین" in snip, f"اسنیپت استخراج‌شده: «{snip}»")

    # ۲. جستجوی واژه داخل فایل متنی
    r = requests.post(f"{BASE_URL}/api/doc-archive/search", json={
        "contentSearch": "مجوز حفاری",
        "status": "active"
    }, headers=headers())
    hits = r.json()
    log(len(hits) > 0 and hits[0]["id"] == doc_id, "جستجوی تمام‌متن واژه «مجوز حفاری» در فایل متنی")

    # ۳. جستجوی واژه استخراج‌شده توسط OCR
    r = requests.post(f"{BASE_URL}/api/doc-archive/search", json={
        "contentSearch": "GUARANTEE",
        "status": "active"
    }, headers=headers())
    hits = r.json()
    log(len(hits) > 0 and hits[0]["id"] == doc_id, "جستجوی تمام‌متن واژه «GUARANTEE» استخراج‌شده با OCR")

    print("\n=== ۶) تست فیلترهای ترکیبی (Multi-Filter) ===")
    # فیلتر ترکیبی: تگ + نوع فایل + وضعیت
    r = requests.post(f"{BASE_URL}/api/doc-archive/search", json={
        "tagIds": [new_tag_id],
        "fileTypes": ["word"],
        "status": "active",
        "includeSubfolders": True
    }, headers=headers())
    log(len(r.json()) == 1, "فیلتر ترکیبی (تگ اختصاصی + فقط فایل Word + فعال)")

    # فیلتر با شرط نامنطبق (باید ۰ نتیجه بدهد)
    r = requests.post(f"{BASE_URL}/api/doc-archive/search", json={
        "contentSearch": "کلمه_غیرموجود_۹۹۹",
        "status": "active"
    }, headers=headers())
    log(len(r.json()) == 0, "عدم تطابق کلمه ناموجود در محتوای اسناد")

    print("\n=== ۷) تست اجرای دستی OCR و بازایندکس کل آرشیو ===")
    if len(extracted_list) > 0:
        att_id = extracted_list[0]["attachmentId"]
        r = requests.post(f"{BASE_URL}/api/doc-archive/attachments/{att_id}/ocr", headers=headers())
        log(r.status_code == 200 and r.json().get("success"), "اجرای مجدد OCR / استخراج متن روی پیوست")

    r = requests.post(f"{BASE_URL}/api/doc-archive/reindex", headers=headers())
    log(r.status_code == 200 and r.json().get("processedCount") >= 2, f"بازایندکس کلیه اسناد: {r.json().get('message')}")

    print("\n==========================================")
    print(f"نتیجه نهایی: {passed} تست موفق | {failed} تست ناموفق")
    print("==========================================")
    return failed == 0

if __name__ == "__main__":
    if login():
        ok = run_tests()
        sys.exit(0 if ok else 1)
    else:
        sys.exit(1)
