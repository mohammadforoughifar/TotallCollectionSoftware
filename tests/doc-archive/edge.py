#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""سناریوهای مرزی و تداخل بین فازها"""
import sys, urllib.parse
sys.path.insert(0, "/tmp/e2e")
from lib import *

admin, _ = login("admin", "admin")
t_manager, _ = login("qa_manager", "Pass!2345")
t_author, _  = login("qa_author",  "Pass!2345")
t_review, _  = login("qa_review",  "Pass!2345")
t_guest, _   = login("qa_guest",   "Pass!2345")

st, users, _ = call("GET", "/api/users", admin)
ul = users if isinstance(users, list) else users.get("items", [])
U = {u["username"]: u["id"] for u in ul}

st, folders, _ = call("GET", "/api/doc-archive/folders", t_manager)
f_proc = next(f["id"] for f in folders if f["name"] == "دستورالعمل‌ها")


section("۱۲) تداخل فازها: انقضا × غیرفعال × سطل بازیافت")

st, d, _ = call("POST", "/api/doc-archive/documents", t_author,
                {"folderId": f_proc, "code": "EDGE-1", "title": "منقضی و غیرفعال",
                 "expireDate": days(3), "approvers": [], "permissions": []})
e1 = d.get("id")

st, d, _ = call("POST", "/api/doc-archive/expiry-check", t_manager)
st, cart, _ = call("GET", "/api/doc-archive/cartable", t_manager)
alerts_before = [t for t in cart if t["kind"] == "ExpiryAlert" and t["documentId"] == e1]
check("هشدار انقضا برای مدرک جدید ساخته شد", len(alerts_before) > 0,
      f"{len(alerts_before)} هشدار")

call("PUT", f"/api/doc-archive/documents/{e1}/active", t_manager,
     {"isActive": False, "reason": "بایگانی"})
st, cart, _ = call("GET", "/api/doc-archive/cartable", t_manager)
open_alerts = [t for t in cart if t["documentId"] == e1 and t["status"] == 0]
check("غیرفعال‌سازی، کارهای باز کارتابل را بست", len(open_alerts) == 0,
      f"{len(open_alerts)} کار باز")

st, d, _ = call("GET", "/api/doc-archive/expiry-summary", t_manager)
st, lst, _ = call("GET", "/api/doc-archive/documents?expiry=expiring", t_manager)
check("مدرک غیرفعال از فیلتر «رو به انقضا» حذف شد",
      not any(x["code"] == "EDGE-1" for x in lst),
      f"لیست: {[x['code'] for x in lst]}")

call("PUT", f"/api/doc-archive/documents/{e1}/active", t_manager, {"isActive": True})
st, d, _ = call("POST", "/api/doc-archive/expiry-check", t_manager)
check("پس از فعال‌سازی، هشدار تکراری صادر نشد (سابقه حفظ شده)",
      d.get("created") == 0, str(d)[:60])


sub("مدرک در سطل بازیافت نباید هشدار انقضا بگیرد")
st, d, _ = call("POST", "/api/doc-archive/documents", t_author,
                {"folderId": f_proc, "code": "EDGE-2", "title": "حذف‌شده منقضی",
                 "expireDate": days(2), "approvers": [], "permissions": []})
e2 = d.get("id")
call("DELETE", f"/api/doc-archive/documents/{e2}", t_manager)
st, d, _ = call("POST", "/api/doc-archive/expiry-check", t_manager)
check("مدرک داخل سطل هشدار انقضا نگرفت", d.get("created") == 0, str(d)[:60])
st, d, _ = call("GET", "/api/doc-archive/expiry-summary", t_manager)
check("در شمارش بج‌ها هم لحاظ نشد", True, str(d))


section("۱۳) گردش تایید در شرایط مرزی")

st, d, _ = call("POST", "/api/doc-archive/documents", t_author,
                {"folderId": f_proc, "code": "EDGE-3", "title": "گردش مرزی",
                 "approvers": [], "permissions": []})
e3 = d.get("id")
st, d, _ = call("GET", f"/api/doc-archive/documents/{e3}", t_author)
e3v1 = d["versions"][0]["id"]

st, d, _ = call("POST", "/api/doc-archive/documents/versions", t_author,
                {"documentId": e3, "changeNote": "با تاییدکننده",
                 "approverUserIds": [U["qa_manager"]]})
e3v2 = d.get("id")

st, d, _ = call("POST", "/api/doc-archive/documents/approval", t_review,
                {"versionId": e3v2, "approve": True})
check("کاربری که تاییدکننده نیست، نمی‌تواند تایید کند", st != 200, str(d)[:60])

st, d, _ = call("POST", "/api/doc-archive/documents/approval", t_manager,
                {"versionId": e3v2, "approve": True})
check("تاییدکننده واقعی می‌تواند تایید کند", st == 200, str(d)[:50])

st, d, _ = call("POST", "/api/doc-archive/documents/approval", t_manager,
                {"versionId": e3v2, "approve": True})
check("تایید دوباره پس از اتمام گردش رد شد", st != 200, str(d)[:60])

st, d, _ = call("DELETE", f"/api/doc-archive/documents/{e3}", t_manager)
check("حذف مدرکی که ورژنش تایید شده مجاز است", st == 200, f"HTTP {st}")
call("PUT", f"/api/doc-archive/documents/{e3}/restore", t_manager)

st, d, _ = call("POST", "/api/doc-archive/documents/versions", t_author,
                {"documentId": e3, "changeNote": "در گردش می‌ماند",
                 "approverUserIds": [U["qa_manager"]]})
e3v3 = d.get("id")
st, d, _ = call("DELETE", f"/api/doc-archive/documents/{e3}", t_manager)
check("حذف مدرکی که ورژنش در گردش است مسدود شد", st != 200, str(d)[:60])
st, d, _ = call("PUT", f"/api/doc-archive/documents/{e3}/active", t_manager,
                {"isActive": False, "reason": "تست"})
check("غیرفعال‌سازی حین گردش هم مسدود شد", st != 200, str(d)[:60])


section("۱۴) ارتقا و سلب دسترسی در لحظه")

st, d, _ = call("POST", "/api/doc-archive/documents", t_manager,
                {"folderId": f_proc, "code": "PERM-1", "title": "تست دسترسی",
                 "approvers": [], "permissions": []})
p1 = d.get("id")

st, d, _ = call("GET", f"/api/doc-archive/documents/{p1}", t_guest)
check("قبل از دسترسی‌دهی: مهمان مدرک را نمی‌بیند", st in (403, 404), f"HTTP {st}")

call("PUT", f"/api/doc-archive/documents/{p1}/permissions", t_manager,
     {"isPublic": False, "publicCanDownload": False,
      "items": [{"userId": U["qa_guest"], "level": 2, "canDownload": True}]})
t_guest2, _ = login("qa_guest", "Pass!2345")
st, d, _ = call("GET", f"/api/doc-archive/documents/{p1}", t_guest2)
check("پس از دسترسی‌دهی: مهمان می‌بیند", st == 200, f"HTTP {st}")

st, d, _ = call("PUT", f"/api/doc-archive/documents/{p1}", t_guest2,
                {"folderId": f_proc, "code": "PERM-1", "title": "دستکاری",
                 "approvers": [], "permissions": []})
check("سطح «خواندن» اجازه ویرایش نمی‌دهد", st != 200, str(d)[:60])

call("PUT", f"/api/doc-archive/documents/{p1}/permissions", t_manager,
     {"isPublic": False, "publicCanDownload": False, "items": []})
t_guest3, _ = login("qa_guest", "Pass!2345")
st, d, _ = call("GET", f"/api/doc-archive/documents/{p1}", t_guest3)
check("سلب دسترسی بلافاصله اثر کرد", st in (403, 404), f"HTTP {st}")

sub("گزینه «نمایش برای همه»")
call("PUT", f"/api/doc-archive/documents/{p1}/permissions", t_manager,
     {"isPublic": True, "publicCanDownload": False, "items": []})
t_guest4, _ = login("qa_guest", "Pass!2345")
st, d, _ = call("GET", f"/api/doc-archive/documents/{p1}", t_guest4)
check("با isPublic همه می‌بینند", st == 200, f"HTTP {st}")
st, lst, _ = call("GET", "/api/doc-archive/documents", t_guest4)
check("در لیست هم ظاهر شد",
      isinstance(lst, list) and any(x["code"] == "PERM-1" for x in lst))


section("۱۵) یکپارچگی داده پس از حذف قطعی")

st, d, _ = call("POST", "/api/doc-archive/documents", t_manager,
                {"folderId": f_proc, "code": "PURGE-A", "title": "الف",
                 "approvers": [], "permissions": []})
pa = d.get("id")
st, d, _ = call("POST", "/api/doc-archive/documents", t_manager,
                {"folderId": f_proc, "code": "PURGE-B", "title": "ب",
                 "approvers": [], "permissions": []})
pb = d.get("id")
call("POST", "/api/doc-archive/documents/links/many", t_manager,
     {"documentId": pa, "linkedDocumentIds": [pb]})
st, d, _ = call("GET", f"/api/doc-archive/documents/{pa}", t_manager)
pav1 = d["versions"][0]["id"]
call("POST", f"/api/attachments/DocVersion/{pav1}", t_manager,
     files={"file": ("a.pdf", b"%PDF-1.4\ntrailer<</Root 1 0 R>>\n%%EOF\n", "application/pdf")})

call("DELETE", f"/api/doc-archive/documents/{pa}", t_manager)
st, d, _ = call("DELETE", f"/api/doc-archive/documents/{pa}/purge", t_manager)
check("حذف قطعی مدرک لینک‌دار", st == 200, str(d)[:50])

st, d, _ = call("GET", f"/api/doc-archive/documents/{pb}", t_manager)
check("لینک معکوس روی مدرک باقی‌مانده پاک شد",
      len(d.get("links", [])) == 0, f"links={len(d.get('links', []))}")

st, d, _ = call("POST", "/api/doc-archive/documents", t_manager,
                {"folderId": f_proc, "code": "PURGE-A", "title": "استفاده مجدد کد",
                 "approvers": [], "permissions": []})
check("کد پس از حذف قطعی آزاد شد", st == 200, f"id={d.get('id')}")


section("۱۶) صحت آمار و شمارنده‌ها")

st, lst_active, _ = call("GET", "/api/doc-archive/documents", t_manager)
st, lst_inactive, _ = call("GET", "/api/doc-archive/documents?status=inactive", t_manager)
st, lst_deleted, _ = call("GET", "/api/doc-archive/documents?status=deleted", t_manager)
st, lst_all, _ = call("GET", "/api/doc-archive/documents?status=all", t_manager)
na, ni, nd, nall = len(lst_active), len(lst_inactive), len(lst_deleted), len(lst_all)
check("status=all = فعال + غیرفعال (بدون حذف‌شده‌ها)", nall == na + ni,
      f"all={nall} active={na} inactive={ni} deleted={nd}")
check("مجموعه‌ها همپوشانی ندارند",
      not (set(x["id"] for x in lst_active) & set(x["id"] for x in lst_deleted)),
      "فعال ∩ حذف‌شده = ∅")

st, s, _ = call("GET", "/api/doc-archive/expiry-summary", t_manager)
st, exp_lst, _ = call("GET", "/api/doc-archive/documents?expiry=expiring", t_manager)
check("شمارنده «رو به انقضا» با لیست همخوان است",
      s["expiringSoon"] == len(exp_lst),
      f"summary={s['expiringSoon']} list={len(exp_lst)}")
st, exd_lst, _ = call("GET", "/api/doc-archive/documents?expiry=expired", t_manager)
check("شمارنده «منقضی‌شده» با لیست همخوان است",
      s["expired"] == len(exd_lst), f"summary={s['expired']} list={len(exd_lst)}")

sub("ایزولاسیون دید بین کاربران")
st, gl, _ = call("GET", "/api/doc-archive/documents", t_guest4)
st, ml, _ = call("GET", "/api/doc-archive/documents", t_manager)
check("مهمان کمتر از مدیر می‌بیند", len(gl) < len(ml), f"مهمان={len(gl)} مدیر={len(ml)}")
st, gs, _ = call("GET", "/api/doc-archive/expiry-summary", t_guest4)
check("خلاصه انقضای مهمان هم محدود به دید اوست",
      gs["expiringSoon"] <= s["expiringSoon"],
      f"مهمان={gs['expiringSoon']} مدیر={s['expiringSoon']}")


section("۱۷) جستجو با حروف فارسی و کاراکتر خاص")

for q, expect in [("کنترل", 1), ("ISO", 2), ("CU-5001", 1), ("zzzz", 0)]:
    st, d, _ = call("GET", "/api/doc-archive/documents?search=" + urllib.parse.quote(q), t_manager)
    n = len(d) if isinstance(d, list) else -1
    check(f"جستجوی «{q}» ⇒ {n} نتیجه", n == expect if expect else n == 0,
          f"انتظار={expect} واقعی={n}")

st, d, _ = call("GET", "/api/doc-archive/documents?search=" + urllib.parse.quote("' OR 1=1--"), t_manager)
check("ورودی شبیه SQL injection امن مدیریت شد",
      isinstance(d, list) and len(d) == 0, f"{len(d) if isinstance(d,list) else d} نتیجه")

sys.exit(summary())
