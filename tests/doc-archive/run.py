#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
تست یکپارچه ماژول آرشیو اسناد و مدارک
سناریو: شرکتی که بایگانی ISO و قراردادهایش را دیجیتال می‌کند.
"""
import sys
sys.path.insert(0, "/tmp/e2e")
from lib import *

section("آماده‌سازی: کاربران و نقش‌ها")

admin, admin_id = login("admin", "admin")

# --- پرمیشن‌ها ---
st, perms, _ = call("GET", "/api/permissions", admin)
perms = perms if isinstance(perms, list) else perms.get("items", [])
P = {f"{p['module']}.{p['action']}": p["id"] for p in perms}
doc_read, doc_create = P["DocArchive.Read"], P["DocArchive.Create"]
doc_delete, doc_manage = P["DocArchive.Delete"], P["DocArchive.Manage"]
check("پرمیشن‌های DocArchive وجود دارند",
      all([doc_read, doc_create, doc_delete, doc_manage]),
      f"Read={doc_read} Create={doc_create} Delete={doc_delete} Manage={doc_manage}")

def mkrole(name, pids):
    st, d, _ = call("POST", "/api/roles", admin, {"name": name, "permissionIds": pids})
    if st != 200:
        st2, all_roles, _ = call("GET", "/api/roles", admin)
        rl = all_roles if isinstance(all_roles, list) else all_roles.get("items", [])
        rid = next((r["id"] for r in rl if r["name"] == name), None)
        call("PUT", f"/api/roles/{rid}", admin, {"name": name, "permissionIds": pids})
        return rid
    rid = d.get("id")
    call("PUT", f"/api/roles/{rid}", admin, {"name": name, "permissionIds": pids})
    return rid

def mkuser(username, pwd, first, last, roles):
    call("POST", "/api/users", admin,
         {"username": username, "password": pwd, "firstName": first,
          "lastName": last, "isActive": True, "roleIds": roles})
    st, users, _ = call("GET", "/api/users", admin)
    ul = users if isinstance(users, list) else users.get("items", [])
    uid = next((u["id"] for u in ul if u["username"] == username), None)
    tok, _ = login(username, pwd)
    return tok, uid

r_manager = mkrole("QA_Manager", [doc_read, doc_create, doc_delete, doc_manage])
r_staff   = mkrole("QA_Staff",   [doc_read, doc_create])
r_viewer  = mkrole("QA_Viewer",  [doc_read])

t_manager, id_manager = mkuser("qa_manager", "Pass!2345", "مریم", "مدیری", [r_manager])
t_author,  id_author  = mkuser("qa_author",  "Pass!2345", "علی",  "نویسنده", [r_staff])
t_review,  id_review  = mkuser("qa_review",  "Pass!2345", "سارا", "بازرس",  [r_staff])
t_guest,   id_guest   = mkuser("qa_guest",   "Pass!2345", "حسن",  "مهمان",  [r_viewer])

check("۴ کاربر با نقش‌های متفاوت ساخته شد",
      all([id_manager, id_author, id_review, id_guest]),
      f"manager={id_manager} author={id_author} review={id_review} guest={id_guest}")


# ══════════════════════════════════════════════════════════════════
section("۱) ساخت درخت پوشه و دسترسی‌دهی")

st, d, _ = call("POST", "/api/doc-archive/folders", t_manager,
                {"name": "مستندات ISO", "parentId": None,
                 "permissions": [{"userId": id_author, "level": 3, "canDownload": True},
                                 {"userId": id_review, "level": 2, "canDownload": True}]})
f_iso = d.get("id") if st == 200 else None
check("پوشه ریشه «مستندات ISO» ساخته شد", st == 200, f"id={f_iso}")

st, d, _ = call("POST", "/api/doc-archive/folders", t_manager,
                {"name": "دستورالعمل‌ها", "parentId": f_iso, "permissions": []})
f_proc = d.get("id") if st == 200 else None
check("زیرپوشه ساخته شد", st == 200, f"id={f_proc}")

st, d, _ = call("POST", "/api/doc-archive/folders", t_manager,
                {"name": "سطح سوم", "parentId": f_proc, "permissions": []})
f_deep = d.get("id") if st == 200 else None
check("پوشه سطح سوم (تودرتو) ساخته شد", st == 200, f"id={f_deep}")

# ارث‌بری دسترسی
st, folders, _ = call("GET", "/api/doc-archive/folders", t_author)
ids = [f["id"] for f in folders] if isinstance(folders, list) else []
check("ارث‌بری دسترسی: نویسنده زیرپوشه‌ها را می‌بیند",
      f_proc in ids and f_deep in ids, f"دیده: {ids}")

st, folders_g, _ = call("GET", "/api/doc-archive/folders", t_guest)
gids = [f["id"] for f in folders_g] if isinstance(folders_g, list) else []
check("کاربر بدون دسترسی، پوشه‌ها را نمی‌بیند", f_iso not in gids, f"دیده: {gids}")


# ══════════════════════════════════════════════════════════════════
section("۲) ساخت مدرک با همه فیلدها")

st, d, _ = call("POST", "/api/doc-archive/documents", t_author,
                {"folderId": f_proc, "code": "ISO-9001", "customerCode": "CU-5001",
                 "title": "دستورالعمل کنترل کیفیت", "description": "نسخه اولیه",
                 "expireDate": days(400),
                 "approvers": [{"userId": id_manager, "userName": "qa_manager"}],
                 "permissions": []})
doc1 = d.get("id") if st == 200 else None
check("مدرک با کد، کد مشتری، انقضا و نفرات تایید ساخته شد", st == 200, f"id={doc1}")

st, d, _ = call("POST", "/api/doc-archive/documents", t_author,
                {"folderId": f_proc, "code": "ISO-9001", "title": "تکراری",
                 "approvers": [], "permissions": []})
check("کد تکراری رد شد", st != 200, str(d)[:60])

st, d, _ = call("POST", "/api/doc-archive/documents", t_author,
                {"folderId": f_proc, "code": "", "title": "بدون کد",
                 "approvers": [], "permissions": []})
check("کد خالی رد شد", st != 200, str(d)[:60])

st, d, _ = call("POST", "/api/doc-archive/documents", t_guest,
                {"folderId": f_proc, "code": "HACK-1", "title": "نفوذی",
                 "approvers": [], "permissions": []})
check("کاربر بدون دسترسی نوشتن نمی‌تواند مدرک بسازد", st in (401, 403), f"HTTP {st}")

# مدرک دوم برای لینک
st, d, _ = call("POST", "/api/doc-archive/documents", t_author,
                {"folderId": f_proc, "code": "ISO-9002", "customerCode": "CU-5002",
                 "title": "فرم بازرسی", "expireDate": days(20),
                 "approvers": [], "permissions": []})
doc2 = d.get("id") if st == 200 else None
check("مدرک دوم ساخته شد", st == 200, f"id={doc2}")

st, d, _ = call("GET", f"/api/doc-archive/documents/{doc1}", t_author)
v1 = d["versions"][0]["id"] if st == 200 and d.get("versions") else None
check("ورژن ۱ خودکار ساخته شد", v1 is not None,
      f"versionNo={d['versions'][0]['versionNo'] if v1 else '?'}")
check("نفرات تایید روی ورژن ۱ کپی شدند",
      len(d["versions"][0].get("approvers", [])) > 0,
      f"{len(d['versions'][0].get('approvers', []))} نفر")

# چون مدرک با نفرات تایید ساخته شد، ورژن ۱ از ابتدا در گردش است.
# برای ادامه سناریو اول این گردش را تعیین تکلیف می‌کنیم.
v1_status = d["versions"][0]["status"]
check("ورژن ۱ به‌دلیل داشتن نفرات تایید، خودکار وارد گردش شد",
      v1_status == 1, f"status={v1_status}")
call("POST", "/api/doc-archive/documents/approval", t_manager,
     {"versionId": v1, "approve": True, "comment": "تایید اولیه"})
st, d, _ = call("GET", f"/api/doc-archive/documents/{doc1}", t_author)
check("ورژن ۱ پس از تایید فعال شد",
      d["versions"][0]["isActive"] and d["versions"][0]["status"] == 2,
      f"active={d['versions'][0]['isActive']}")


# ══════════════════════════════════════════════════════════════════
section("۳) لینک چندتایی مدارک")

st, d, _ = call("POST", "/api/doc-archive/documents/links/many", t_author,
                {"documentId": doc1, "linkedDocumentIds": [doc2]})
check("لینک چندتایی انجام شد", st == 200, str(d)[:50])

st, d, _ = call("GET", f"/api/doc-archive/documents/{doc2}", t_author)
check("لینک معکوس خودکار ساخته شد",
      any(l["linkedDocumentId"] == doc1 or l.get("documentId") == doc1
          for l in d.get("links", [])),
      f"links={len(d.get('links', []))}")

st, d, _ = call("POST", "/api/doc-archive/documents/links/many", t_author,
                {"documentId": doc1, "linkedDocumentIds": [doc2]})
check("لینک تکراری رد شد", st != 200 or "قبلا" in str(d) or "قبلاً" in str(d), str(d)[:60])


# ══════════════════════════════════════════════════════════════════
section("۴) پیوست + امنیت دسترسی (فاز ۵)")

PDF = (b"%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n"
       b"2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n"
       b"3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 99 99]>>endobj\n"
       b"trailer<</Root 1 0 R>>\n%%EOF\n")

st, d, _ = call("POST", f"/api/attachments/DocVersion/{v1}", t_author,
                files={"file": ("procedure.pdf", PDF, "application/pdf")})
check("آپلود پیوست روی ورژن", st == 200, f"HTTP {st}")

st, atts, _ = call("GET", f"/api/attachments/DocVersion/{v1}", t_author)
att_id = atts[0]["id"] if isinstance(atts, list) and atts else None
check("پیوست در لیست ظاهر شد", att_id is not None,
      f"canPreview={atts[0]['canPreview'] if att_id else '?'}")

st, _, _ = call("GET", f"/api/attachments/download/{att_id}")
check("دانلود بدون توکن مسدود است (رخنه بسته)", st == 401, f"HTTP {st}")

st, _, _ = call("GET", f"/api/attachments/preview/{att_id}")
check("پیش‌نمایش بدون توکن مسدود است", st == 401, f"HTTP {st}")

st, _, _ = call("GET", f"/api/attachments/download/{att_id}", t_guest)
check("کاربر بی‌ربط به پیوست دسترسی ندارد", st == 403, f"HTTP {st}")

# دسترسی «فقط مشاهده» روی مدرک
call("PUT", f"/api/doc-archive/documents/{doc1}/permissions", t_manager,
     {"isPublic": False, "publicCanDownload": False,
      "items": [{"userId": id_guest, "level": 1, "canDownload": False}]})
t_guest, _ = login("qa_guest", "Pass!2345")

st, d, h = call("GET", f"/api/attachments/preview/{att_id}", t_guest, raw=True)
check("کاربر «فقط مشاهده» پیش‌نمایش می‌بیند", st == 200,
      f"inline={'inline' in h.get('Content-Disposition','')}")

st, d, _ = call("GET", f"/api/attachments/download/{att_id}", t_guest)
check("کاربر «فقط مشاهده» دانلود نمی‌تواند", st == 403, str(d)[:55])

st, atts_g, _ = call("GET", f"/api/attachments/DocVersion/{v1}", t_guest)
check("پرچم canDownload برای او false است",
      isinstance(atts_g, list) and atts_g and atts_g[0]["canDownload"] is False,
      f"canDownload={atts_g[0]['canDownload'] if atts_g else '?'}")


# ══════════════════════════════════════════════════════════════════
section("۵) گردش تایید، فریز و فعال‌سازی خودکار")

st, d, _ = call("POST", "/api/doc-archive/documents/versions", t_author,
                {"documentId": doc1, "changeNote": "بازنگری بند ۴",
                 "approverUserIds": [id_manager, id_review],
                 "activateOnApprove": True})
v2 = d.get("id") if st == 200 else None
check("ورژن ۲ با گردش تایید ثبت شد", st == 200, f"id={v2}")

st, d, _ = call("GET", f"/api/doc-archive/documents/{doc1}", t_author)
ver2 = next((v for v in d["versions"] if v["id"] == v2), None)
check("ورژن ۲ فریز شد (InReview)",
      ver2 and ver2["isFrozen"] and ver2["status"] == 1,
      f"frozen={ver2['isFrozen']} status={ver2['status']}")

st, d, _ = call("POST", "/api/doc-archive/documents/versions", t_author,
                {"documentId": doc1, "changeNote": "حین گردش", "approverUserIds": []})
check("ورژن جدید حین گردش مسدود شد", st != 200, str(d)[:60])

st, cart, _ = call("GET", "/api/doc-archive/cartable", t_manager)
mine = [t for t in cart if t["kind"] == "Approval" and t["versionId"] == v2]
check("کار تایید در کارتابل مدیر آمد", len(mine) > 0, f"{len(mine)} کار")

st, cart_r, _ = call("GET", "/api/doc-archive/cartable", t_review)
check("کار تایید در کارتابل بازرس هم آمد",
      any(t["versionId"] == v2 for t in cart_r), f"{len(cart_r)} کار")

# تایید اول
call("POST", "/api/doc-archive/documents/approval", t_manager,
     {"versionId": v2, "approve": True, "comment": "تایید مدیر"})
st, d, _ = call("GET", f"/api/doc-archive/documents/{doc1}", t_author)
ver2 = next((v for v in d["versions"] if v["id"] == v2), None)
check("با یک تایید هنوز فعال نشده (گردش موازی)",
      ver2["status"] == 1 and not ver2["isActive"],
      f"status={ver2['status']} active={ver2['isActive']}")

# تایید دوم
call("POST", "/api/doc-archive/documents/approval", t_review,
     {"versionId": v2, "approve": True, "comment": "تایید بازرس"})
st, d, _ = call("GET", f"/api/doc-archive/documents/{doc1}", t_author)
ver2 = next((v for v in d["versions"] if v["id"] == v2), None)
ver1 = next((v for v in d["versions"] if v["id"] == v1), None)
check("پس از تایید همه، ورژن ۲ خودکار فعال شد",
      ver2["status"] == 2 and ver2["isActive"],
      f"status={ver2['status']} active={ver2['isActive']}")
check("سیاست تک‌ورژن: ورژن ۱ غیرفعال شد",
      not ver1["isActive"], f"v1.active={ver1['isActive']}")

st, cart, _ = call("GET", "/api/doc-archive/cartable", t_manager)
check("کار کارتابل پس از تایید بسته شد",
      not any(t["kind"] == "Approval" and t["versionId"] == v2 and t["status"] == 0
              for t in cart))

# --- سناریو رد ---
st, d, _ = call("POST", "/api/doc-archive/documents/versions", t_author,
                {"documentId": doc1, "changeNote": "پیشنهاد رد‌شونده",
                 "approverUserIds": [id_manager, id_review]})
v3 = d.get("id")
call("POST", "/api/doc-archive/documents/approval", t_review,
     {"versionId": v3, "approve": False, "comment": "مغایرت دارد"})
st, d, _ = call("GET", f"/api/doc-archive/documents/{doc1}", t_author)
ver3 = next((v for v in d["versions"] if v["id"] == v3), None)
check("یک رد ⇒ کل ورژن Rejected", ver3["status"] == 3, f"status={ver3['status']}")
check("ورژن رد‌شده فریز باز شد", not ver3["isFrozen"], f"frozen={ver3['isFrozen']}")
ver2 = next((v for v in d["versions"] if v["id"] == v2), None)
check("ورژن فعال قبلی دست‌نخورده ماند", ver2["isActive"])


# ══════════════════════════════════════════════════════════════════
section("۶) کارتابل: اعلان ورژن و مدرک مرتبط")

st, cart, _ = call("GET", "/api/doc-archive/cartable", t_manager)
check("اعلان ورژن جدید برای دارنده دسترسی کامل",
      any(t["kind"] == "VersionNotice" for t in cart),
      f"{sum(1 for t in cart if t['kind']=='VersionNotice')} مورد")
check("کار «به‌روزرسانی مدرک مرتبط» ساخته شد",
      any(t["kind"] == "RelatedUpdate" for t in cart),
      f"{sum(1 for t in cart if t['kind']=='RelatedUpdate')} مورد")

rel = next((t for t in cart if t["kind"] == "RelatedUpdate" and t["status"] == 0), None)
if rel:
    st, _, _ = call("PUT", f"/api/doc-archive/cartable/{rel['id']}/done", t_manager)
    check("بستن دستی کار کارتابل", st == 200, f"HTTP {st}")


# ══════════════════════════════════════════════════════════════════
section("۷) چندورژن هم‌زمان فعال")

st, d, _ = call("POST", "/api/doc-archive/documents", t_author,
                {"folderId": f_proc, "code": "MULTI-1", "title": "چندورژنی",
                 "allowMultipleActiveVersions": True,
                 "approvers": [], "permissions": []})
docm = d.get("id")
st, d, _ = call("GET", f"/api/doc-archive/documents/{docm}", t_author)
mv1 = d["versions"][0]["id"]
st, d, _ = call("POST", "/api/doc-archive/documents/versions", t_author,
                {"documentId": docm, "changeNote": "دوم", "approverUserIds": []})
mv2 = d.get("id")
call("PUT", f"/api/doc-archive/documents/versions/{mv1}/active?active=true", t_author)
call("PUT", f"/api/doc-archive/documents/versions/{mv2}/active?active=true", t_author)
st, d, _ = call("GET", f"/api/doc-archive/documents/{docm}", t_author)
act = [v["versionNo"] for v in d["versions"] if v["isActive"]]
check("دو ورژن هم‌زمان فعال ماندند", len(act) >= 2, f"فعال‌ها: {act}")


# ══════════════════════════════════════════════════════════════════
section("۸) هشدار انقضا (فاز ۳)")

st, d, _ = call("GET", "/api/doc-archive/expiry-summary", t_manager)
check("خلاصه انقضا برمی‌گردد", st == 200, str(d))
before = d.get("expiringSoon", 0)

st, d, _ = call("POST", "/api/doc-archive/expiry-check", t_manager)
check("اجرای دستی بررسی انقضا", st == 200, str(d)[:70])

st, cart, _ = call("GET", "/api/doc-archive/cartable", t_manager)
exp_tasks = [t for t in cart if t["kind"] == "ExpiryAlert"]
check("هشدار انقضا در کارتابل ساخته شد", len(exp_tasks) > 0,
      f"{len(exp_tasks)} هشدار")

st, d, _ = call("POST", "/api/doc-archive/expiry-check", t_manager)
check("اجرای دوباره هشدار تکراری نمی‌سازد", d.get("created") == 0, str(d)[:60])

st, _, _ = call("POST", "/api/doc-archive/expiry-check", t_author)
check("کاربر بدون Manage نمی‌تواند بررسی انقضا بزند", st == 403, f"HTTP {st}")

st, lst, _ = call("GET", "/api/doc-archive/documents?expiry=expiring", t_manager)
check("فیلتر «رو به انقضا» کار می‌کند",
      isinstance(lst, list) and any(x["code"] == "ISO-9002" for x in lst),
      f"{len(lst) if isinstance(lst,list) else '?'} مدرک")


# ══════════════════════════════════════════════════════════════════
section("۹) وضعیت فعال/غیرفعال (فاز ۲)")

st, d, _ = call("PUT", f"/api/doc-archive/documents/{doc2}/active", t_manager,
                {"isActive": False, "reason": "منسوخ شد"})
check("غیرفعال‌سازی مدرک", st == 200, str(d)[:50])

st, d, _ = call("POST", "/api/doc-archive/documents/versions", t_author,
                {"documentId": doc2, "changeNote": "تلاش", "approverUserIds": []})
check("ورژن جدید روی مدرک غیرفعال مسدود", st != 200, str(d)[:55])

st, d, _ = call("POST", "/api/doc-archive/documents/links/many", t_author,
                {"documentId": doc1, "linkedDocumentIds": [doc2]})
check("لینک به مدرک غیرفعال مسدود", st != 200, str(d)[:55])

st, lst, _ = call("GET", "/api/doc-archive/documents", t_manager)
check("مدرک غیرفعال از لیست پیش‌فرض حذف شد",
      not any(x["code"] == "ISO-9002" for x in lst))
st, lst, _ = call("GET", "/api/doc-archive/documents?status=inactive", t_manager)
check("در زیرمنوی غیرفعال‌ها دیده می‌شود",
      any(x["code"] == "ISO-9002" for x in lst))

call("PUT", f"/api/doc-archive/documents/{doc2}/active", t_manager, {"isActive": True})
st, d, _ = call("POST", "/api/doc-archive/documents/versions", t_author,
                {"documentId": doc2, "changeNote": "پس از فعال‌سازی",
                 "approverUserIds": []})
check("پس از فعال‌سازی مجدد، عملیات آزاد شد", st == 200, f"id={d.get('id')}")


# ══════════════════════════════════════════════════════════════════
section("۱۰) سطل بازیافت و تاریخچه (فاز ۴)")

st, _, _ = call("DELETE", f"/api/doc-archive/documents/{docm}", t_manager)
check("انتقال به سطل بازیافت", st == 200, f"HTTP {st}")

st, lst, _ = call("GET", "/api/doc-archive/documents?status=deleted", t_manager)
trash = [x for x in lst if x["code"] == "MULTI-1"] if isinstance(lst, list) else []
check("در سطل بازیافت دیده می‌شود", len(trash) == 1,
      f"حذف‌کننده={trash[0]['deletedByName'] if trash else '?'}")

st, atts, _ = call("GET", f"/api/attachments/DocVersion/{v1}", t_manager)
st_del, _, _ = call("GET", f"/api/attachments/download/{att_id}", t_manager)
call("DELETE", f"/api/doc-archive/documents/{doc1}", t_manager)
st, _, _ = call("GET", f"/api/attachments/download/{att_id}", t_manager)
check("پیوست مدرکِ داخل سطل قابل دانلود نیست", st == 403, f"HTTP {st}")
call("PUT", f"/api/doc-archive/documents/{doc1}/restore", t_manager)
st, _, _ = call("GET", f"/api/attachments/download/{att_id}", t_manager)
check("پس از بازگردانی، پیوست دوباره در دسترس است", st == 200, f"HTTP {st}")

st, d, _ = call("GET", f"/api/doc-archive/documents/{doc1}", t_manager)
actions = [l["action"] for l in d.get("logs", [])]
for a in ["Create", "NewVersion", "Approved", "Link", "Permissions", "Delete", "Restore"]:
    check(f"تاریخچه شامل «{a}»", a in actions)

st, _, _ = call("DELETE", f"/api/doc-archive/documents/{docm}/purge", t_author)
check("حذف قطعی بدون Manage مسدود", st == 403, f"HTTP {st}")

st, d, _ = call("DELETE", f"/api/doc-archive/documents/{docm}/purge", t_manager)
check("حذف قطعی با Manage انجام شد", st == 200, str(d)[:55])
st, _, _ = call("GET", f"/api/doc-archive/documents/{docm}", t_manager)
check("مدرک واقعاً حذف شد", st == 404, f"HTTP {st}")


# ══════════════════════════════════════════════════════════════════
section("۱۱) جستجو و lookups")

st, lst, _ = call("GET", "/api/doc-archive/documents?search=CU-5001", t_manager)
check("جستجو با کد مشتری", any(x["code"] == "ISO-9001" for x in lst))
st, lst, _ = call("GET", "/api/doc-archive/documents?search=کنترل", t_manager)
check("جستجو با بخشی از عنوان", len(lst) > 0, f"{len(lst)} نتیجه")
st, lk, _ = call("GET", "/api/doc-archive/lookups", t_manager)
check("lookups کاربران و مدارک را برمی‌گرداند",
      len(lk.get("users", [])) > 0 and isinstance(lk.get("documents"), list),
      f"users={len(lk.get('users',[]))} docs={len(lk.get('documents',[]))}")

sys.exit(summary())
