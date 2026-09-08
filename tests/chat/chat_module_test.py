#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
آزمون جامع ماژول پیام‌رسان سازمانی (Telegram-Style Chat Module)
تست ایجاد گفتگوی خصوصی، گروه‌ها، ارسال و دریافت پیام، ریپلای، واکنش، پین، آپلود پیوست،
کارت‌های اشتراک ERP و رعایت کامل ماتریس امنیت و ایزولاسیون اعضا.
"""

import io
import json
import time
import requests

BASE_URL = "http://localhost:5100"

def log(status: bool, message: str):
    prefix = "\033[92m[PASS]\033[0m" if status else "\033[91m[FAIL]\033[0m"
    print(f"  {prefix} {message}")
    if not status:
        raise AssertionError(f"Test failed: {message}")

def main():
    print("\nشروع تست‌های جامع پیام‌رسان سازمانی مشابه تلگرام...")

    # ورود مدیر
    r = requests.post(f"{BASE_URL}/api/auth/login", json={"username": "admin", "password": "admin"})
    log(r.status_code == 200, "ورود کاربر admin موفق")
    token_admin = r.json()["token"]
    admin_id = r.json()["userId"]
    headers_admin = {"Authorization": f"Bearer {token_admin}"}

    ts = int(time.time())
    u1_name = f"user_chat1_{ts}"
    u2_name = f"user_chat2_{ts}"
    u3_name = f"user_chat3_{ts}"

    print("\n=== ۱) ساخت کاربران آزمایشی در نرم‌افزار ===")
    r = requests.post(f"{BASE_URL}/api/users", json={
        "username": u1_name, "password": "Pass123!@#", "firstName": "رضا", "lastName": "محمدی", "role": "Operator", "isActive": True
    }, headers=headers_admin)
    log(r.status_code == 200, f"ساخت کاربر {u1_name}")
    u1_id = r.json()["id"]

    r = requests.post(f"{BASE_URL}/api/users", json={
        "username": u2_name, "password": "Pass123!@#", "firstName": "سارا", "lastName": "احمدی", "role": "Operator", "isActive": True
    }, headers=headers_admin)
    log(r.status_code == 200, f"ساخت کاربر {u2_name}")
    u2_id = r.json()["id"]

    r = requests.post(f"{BASE_URL}/api/users", json={
        "username": u3_name, "password": "Pass123!@#", "firstName": "علی", "lastName": "حسینی", "role": "Operator", "isActive": True
    }, headers=headers_admin)
    log(r.status_code == 200, f"ساخت کاربر ثالث {u3_name}")
    u3_id = r.json()["id"]

    # لاگین کاربران
    t1 = requests.post(f"{BASE_URL}/api/auth/login", json={"username": u1_name, "password": "Pass123!@#"}).json()["token"]
    h1 = {"Authorization": f"Bearer {t1}"}

    t2 = requests.post(f"{BASE_URL}/api/auth/login", json={"username": u2_name, "password": "Pass123!@#"}).json()["token"]
    h2 = {"Authorization": f"Bearer {t2}"}

    t3 = requests.post(f"{BASE_URL}/api/auth/login", json={"username": u3_name, "password": "Pass123!@#"}).json()["token"]
    h3 = {"Authorization": f"Bearer {t3}"}

    print("\n=== ۲) استعلام کاربران نرم‌افزار جهت شروع چت ===")
    r = requests.get(f"{BASE_URL}/api/chat/users", headers=h1)
    log(r.status_code == 200, "دریافت لیست کاربران فعال نرم‌افزار توسط کاربر ۱")
    chat_users = r.json()
    log(any(u["id"] == u2_id for u in chat_users), "کاربر ۲ در لیست مخاطبین چت کاربر ۱ مشاهده می‌شود")
    log(all(u["id"] != u1_id for u in chat_users), "کاربر ۱ خودش در لیست مخاطبین چت قرار ندارد (صحیح)")

    print("\n=== ۳) گفتگوی خصوصی دونفره (Direct 1-on-1 Chat) ===")
    # کاربر ۱ چت خصوصی با کاربر ۲ باز می‌کند
    r = requests.post(f"{BASE_URL}/api/chat/conversations/direct", json={"targetUserId": u2_id}, headers=h1)
    log(r.status_code == 200, "ایجاد گفتگوی خصوصی دونفره")
    direct_conv = r.json()
    direct_id = direct_conv["id"]
    log(direct_conv["type"] == 1, "نوع گفتگو Direct (خصوصی) است")

    # کاربر ۲ دیالوگ‌های خود را چک می‌کند
    r = requests.get(f"{BASE_URL}/api/chat/conversations", headers=h2)
    convs_u2 = r.json()
    log(any(c["id"] == direct_id for c in convs_u2), "گفتگوی خصوصی ایجادشده در لیست دیالوگ‌های کاربر ۲ ظاهر شد")

    # کاربر ۱ پیام ارسال می‌کند
    r = requests.post(f"{BASE_URL}/api/chat/conversations/{direct_id}/messages", json={
        "text": "سلام سرکار خانم احمدی، پیش‌نویس قرارداد آماده است؟",
        "messageType": 1
    }, headers=h1)
    log(r.status_code == 200, "ارسال پیام متنی در گفتگوی خصوصی")
    m1 = r.json()
    m1_id = m1["id"]

    # کاربر ۲ پیام را چک می‌کند
    r = requests.get(f"{BASE_URL}/api/chat/conversations/{direct_id}/messages", headers=h2)
    msgs_u2 = r.json()
    log(len(msgs_u2) >= 1 and msgs_u2[-1]["text"] == "سلام سرکار خانم احمدی، پیش‌نویس قرارداد آماده است؟", "کاربر ۲ پیام ارسالی کاربر ۱ را دریافت کرد")
    log(not msgs_u2[-1]["isOutgoing"], "پرچم isOutgoing برای کاربر ۲ برابر False است (پیام دریافتی)")

    # بررسی شمارنده نخوانده‌های کاربر ۲
    r = requests.get(f"{BASE_URL}/api/chat/summary", headers=h2)
    log(r.json()["totalUnreadMessages"] >= 1, "شمارنده پیام‌های نخوانده کاربر ۲ افزایش یافت")

    # کاربر ۲ پیام را می‌خواند
    requests.post(f"{BASE_URL}/api/chat/conversations/{direct_id}/read", json={}, headers=h2)
    r = requests.get(f"{BASE_URL}/api/chat/summary", headers=h2)
    log(r.json()["totalUnreadMessages"] == 0, "پس از خواندن گفتگو، شمارنده نخوانده‌های کاربر ۲ صفر شد")

    # کاربر ۲ ریپلای می‌زند
    r = requests.post(f"{BASE_URL}/api/chat/conversations/{direct_id}/messages", json={
        "text": "بله مهندس، از طریق بایگانی پیوست شد.",
        "replyToMessageId": m1_id,
        "messageType": 1
    }, headers=h2)
    log(r.status_code == 200, "ارسال پاسخ (Reply) به پیام اول")
    m2 = r.json()
    log(m2["replyToMessageId"] == m1_id, "اطلاعات ریپلای در پیام ذخیره شد")

    print("\n=== ۴) ایجاد گروه کاری سازمانی (Group Chat) ===")
    r = requests.post(f"{BASE_URL}/api/chat/conversations/group", json={
        "title": f"کارگروه فنی پروژه {ts}",
        "description": "گروه هماهنگی تیم مهندسی و مالی",
        "memberUserIds": [u1_id, u2_id]
    }, headers=headers_admin)
    log(r.status_code == 200, "ایجاد گروه کاری جدید توسط مدیر")
    grp_conv = r.json()
    grp_id = grp_conv["id"]
    log(grp_conv["type"] == 2, "نوع گفتگو Group است")
    log(grp_conv["membersCount"] == 3, "تعداد اعضای گروه ۳ نفر (مدیر + کاربر ۱ + کاربر ۲) است")

    # مدیر پیام ارسال می‌کند
    r = requests.post(f"{BASE_URL}/api/chat/conversations/{grp_id}/messages", json={
        "text": "همکاران گرامی، جلسه هماهنگی فردا ساعت ۱۰ برگزار می‌شود.",
        "messageType": 1
    }, headers=headers_admin)
    log(r.status_code == 200, "ارسال پیام در گروه کاری")
    grp_msg1 = r.json()
    grp_msg1_id = grp_msg1["id"]

    # واکنش ایموجی (Emoji Reaction)
    r = requests.post(f"{BASE_URL}/api/chat/messages/{grp_msg1_id}/react", json={"emoji": "👍"}, headers=h1)
    log(r.status_code == 200, "ثبت واکنش 👍 توسط کاربر ۱")
    reactions = r.json()
    log("👍" in reactions and len(reactions["👍"]) == 1, "واکنش در داده‌های پیام ثبت شد")

    # ویرایش پیام توسط مدیر
    r = requests.put(f"{BASE_URL}/api/chat/messages/{grp_msg1_id}", json={
        "newText": "همکاران گرامی، جلسه هماهنگی فردا ساعت ۱۰:۳۰ در اتاق جلسات برگزار می‌شود."
    }, headers=headers_admin)
    log(r.status_code == 200, "ویرایش موفق پیام توسط فرستنده")
    edited_msg = r.json()
    log(edited_msg["isEdited"] and "۱۰:۳۰" in edited_msg["text"], "متن پیام ویرایش و پرچم IsEdited فعال شد")

    # پین پیام در گروه
    r = requests.post(f"{BASE_URL}/api/chat/messages/{grp_msg1_id}/pin", json={}, headers=headers_admin)
    log(r.status_code == 200, "سنجاق (Pin) کردن پیام در گروه")

    print("\n=== ۵) امنیت و ایزولاسیون کامل حریم خصوصی (Chat Privacy & Security) ===")
    # کاربر ۳ که عضو گروه نیست نباید بتواند پیام‌های گروه را ببیند
    r = requests.get(f"{BASE_URL}/api/chat/conversations/{grp_id}/messages", headers=h3)
    log(r.status_code in [400, 401, 403], "کاربر غیرعضو به تاریخچه پیام‌های گروه دسترسی ندارد (امنیت تایید شد)")

    # کاربر ۳ نباید بتواند در گفتگوی خصوصی کاربر ۱ و ۲ پیام ارسال کند
    r = requests.post(f"{BASE_URL}/api/chat/conversations/{direct_id}/messages", json={
        "text": "تلاش نفوذ غیرمجاز به چت دیگران"
    }, headers=h3)
    log(r.status_code in [400, 401, 403], "کاربر غیرعضو اجازه ارسال پیام در گفتگوی دیگران را ندارد")

    print("\n=== ۶) آپلود پیوست و اشتراک کارت سند ERP ===")
    # آپلود فایل
    dummy_file = io.BytesIO(b"Document Content for Chat Attachment")
    files = {"file": ("contract_spec.txt", dummy_file, "text/plain")}
    r = requests.post(f"{BASE_URL}/api/chat/upload", files=files, headers=h1)
    log(r.status_code == 200, "آپلود فایل پیوست در سرور چت")
    up_res = r.json()
    file_url = up_res["fileUrl"]

    # ارسال پیام به همراه پیوست و کارت سند ERP
    r = requests.post(f"{BASE_URL}/api/chat/conversations/{grp_id}/messages", json={
        "text": "مشخصات فنی و فایل قرارداد پیوست گردید.",
        "fileUrl": file_url,
        "fileName": "contract_spec.txt",
        "fileSizeBytes": 36,
        "fileContentType": "text/plain",
        "messageType": 3,
        "erpModule": "DocArchive",
        "erpEntityId": "100",
        "erpEntityTitle": "قرارداد احداث سد مخزنی",
        "erpEntitySummary": "کد سند: DOC-DAM-2026 • نسخه ۱ • تایید شده"
    }, headers=h1)
    log(r.status_code == 200, "ارسال پیام با پیوست فایل و کارت هوشمند سند ERP")
    att_msg = r.json()
    log(att_msg["erpModule"] == "DocArchive" and att_msg["fileName"] == "contract_spec.txt", "متاداده‌های پیوست و ERP در پیام ذخیره شدند")

    print("\n=======================================================")
    print("تمامی تست‌های ماژول چت سازمانی با موفقیت ۱۰۰٪ پاس شدند ✔")
    print("=======================================================\n")

if __name__ == "__main__":
    main()
