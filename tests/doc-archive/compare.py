#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import sys
sys.path.insert(0, ".")
from lib import *

admin, _ = login("admin", "admin")

st, perms, _ = call("GET", "/api/permissions", admin)
perms = perms if isinstance(perms, list) else perms.get("items", [])
P = {f"{p['module']}.{p['action']}": p["id"] for p in perms}

def mkrole(n, pids):
    st, d, _ = call("POST", "/api/roles", admin, {"name": n, "permissionIds": pids})
    rid = d.get("id")
    call("PUT", f"/api/roles/{rid}", admin, {"name": n, "permissionIds": pids})
    return rid

def mkuser(u, first, roles):
    call("POST", "/api/users", admin, {"username": u, "password": "Pass!2345",
         "firstName": first, "lastName": "ت", "isActive": True, "roleIds": roles})
    st, us, _ = call("GET", "/api/users", admin)
    ul = us if isinstance(us, list) else us.get("items", [])
    uid = next(x["id"] for x in ul if x["username"] == u)
    tok, _ = login(u, "Pass!2345")
    return tok, uid

r_full = mkrole("CmpFull", [P["DocArchive.Read"], P["DocArchive.Create"],
                            P["DocArchive.Delete"], P["DocArchive.Manage"]])
r_read = mkrole("CmpRead", [P["DocArchive.Read"]])
t_own, id_own = mkuser("cmp_owner", "مالک", [r_full])
t_app, id_app = mkuser("cmp_appr", "تاییدکننده", [r_full])
t_out, id_out = mkuser("cmp_out", "غریبه", [r_read])

st, d, _ = call("POST", "/api/doc-archive/folders", t_own,
                {"name": "مقایسه", "parentId": None, "permissions": []})
fid = d["id"]

st, d, _ = call("POST", "/api/doc-archive/documents", t_own,
                {"folderId": fid, "code": "CMP-1", "title": "سند مقایسه",
                 "expireDate": days(100), "approvers": [], "permissions": []})
doc = d["id"]
st, d, _ = call("GET", f"/api/doc-archive/documents/{doc}", t_own)
v1 = d["versions"][0]["id"]

PDF = b"%PDF-1.4\ntrailer<</Root 1 0 R>>\n%%EOF\n"
call("POST", f"/api/attachments/DocVersion/{v1}", t_own,
     files={"file": ("shared.pdf", PDF, "application/pdf")})
call("POST", f"/api/attachments/DocVersion/{v1}", t_own,
     files={"file": ("only-in-v1.pdf", PDF, "application/pdf")})

# ورژن ۲: عنوان و انقضا عوض، تاییدکننده اضافه، پیوست متفاوت
st, d, _ = call("POST", "/api/doc-archive/documents/versions", t_own,
                {"documentId": doc, "title": "عنوان بازنگری‌شده",
                 "changeNote": "بازنگری کامل بند ۵",
                 "expireDate": days(300),
                 "approverUserIds": [id_app]})
v2 = d["id"]
call("POST", f"/api/attachments/DocVersion/{v2}", t_own,
     files={"file": ("shared.pdf", PDF, "application/pdf")})
call("POST", f"/api/attachments/DocVersion/{v2}", t_own,
     files={"file": ("new-in-v2.pdf", PDF, "application/pdf")})


section("مقایسه دو ورژن")

st, c, _ = call("GET", f"/api/doc-archive/documents/{doc}/compare?from={v1}&to={v2}", t_own)
check("endpoint مقایسه پاسخ داد", st == 200, f"HTTP {st}")
check("اطلاعات مدرک برگشت", c["documentCode"] == "CMP-1", c.get("documentTitle"))
check("ورژن چپ = قدیمی‌تر", c["left"]["versionNo"] == 1, f"v{c['left']['versionNo']}")
check("ورژن راست = جدیدتر", c["right"]["versionNo"] == 2, f"v{c['right']['versionNo']}")

F = {r["field"]: r for r in c["fields"]}
print("\n  \033[1mتفاوت فیلدها:\033[0m")
for r in c["fields"]:
    mark = {"changed": "~", "added": "+", "removed": "-", "same": " "}[r["kind"]]
    print(f"    {mark} {r['field']:32} {str(r['left'])[:22]:24} → {str(r['right'])[:22]}")

check("تغییر «عنوان» تشخیص داده شد", F["عنوان"]["kind"] in ("changed", "added"),
      f"{F['عنوان']['left']} → {F['عنوان']['right']}")
check("تغییر «تاریخ انقضا» تشخیص داده شد", F["تاریخ انقضا"]["kind"] == "changed",
      f"{F['تاریخ انقضا']['left']} → {F['تاریخ انقضا']['right']}")
check("تغییر «شرح تغییر» تشخیص داده شد", F["شرح تغییر"]["kind"] in ("changed", "added"))
check("«ثبت‌کننده» یکسان علامت same خورد", F["ثبت‌کننده"]["kind"] == "same",
      F["ثبت‌کننده"]["left"])

print("\n  \033[1mنفرات گردش:\033[0m")
for r in c["approvers"]:
    print(f"    [{r['kind']:8}] {r['field']:16} {r['left']} → {r['right']}")
check("تاییدکننده اضافه‌شده با kind=added", 
      any(r["kind"] == "added" for r in c["approvers"]),
      f"{len(c['approvers'])} ردیف")

print("\n  \033[1mپیوست‌ها:\033[0m")
for r in c["attachments"]:
    print(f"    [{r['kind']:8}] {r['field']}")
A = {r["field"]: r["kind"] for r in c["attachments"]}
check("پیوست مشترک same", A.get("shared.pdf") == "same")
check("پیوست فقط در ورژن ۱ ⇒ added", A.get("only-in-v1.pdf") == "added",
      f"kind={A.get('only-in-v1.pdf')}")
check("پیوست فقط در ورژن ۲ ⇒ removed", A.get("new-in-v2.pdf") == "removed",
      f"kind={A.get('new-in-v2.pdf')}")

check("شمارش تغییرات > 0", c["changeCount"] > 0, f"{c['changeCount']} تغییر")
expected = (sum(1 for r in c["fields"] if r["kind"] != "same")
            + sum(1 for r in c["approvers"] if r["kind"] != "same")
            + sum(1 for r in c["attachments"] if r["kind"] != "same"))
check("شمارش با مجموع ردیف‌های متفاوت همخوان است",
      c["changeCount"] == expected, f"api={c['changeCount']} محاسبه={expected}")
check("رویدادهای بین دو ورژن برگشت", len(c["between"]) > 0,
      f"{len(c['between'])} رویداد")


sub("ترتیب معکوس باید همان نتیجه را بدهد")
st, c2, _ = call("GET", f"/api/doc-archive/documents/{doc}/compare?from={v2}&to={v1}", t_own)
check("مقایسه معکوس هم کار می‌کند", st == 200)
check("سیستم خودش ترتیب را اصلاح کرد",
      c2["left"]["versionNo"] == 1 and c2["right"]["versionNo"] == 2,
      f"left=v{c2['left']['versionNo']} right=v{c2['right']['versionNo']}")
check("نتیجه یکسان با حالت مستقیم", c2["changeCount"] == c["changeCount"])


sub("حالت‌های خطا")
st, d, _ = call("GET", f"/api/doc-archive/documents/{doc}/compare?from={v1}&to={v1}", t_own)
check("مقایسه ورژن با خودش رد شد", st == 400, str(d)[:55])

st, d, _ = call("GET", f"/api/doc-archive/documents/{doc}/compare?from={v1}&to=99999", t_own)
check("ورژن ناموجود رد شد", st == 404, str(d)[:55])

st, d2, _ = call("POST", "/api/doc-archive/documents", t_own,
                 {"folderId": fid, "code": "CMP-2", "title": "سند دیگر",
                  "approvers": [], "permissions": []})
doc2 = d2["id"]
st, dd, _ = call("GET", f"/api/doc-archive/documents/{doc2}", t_own)
other_v = dd["versions"][0]["id"]
st, d, _ = call("GET", f"/api/doc-archive/documents/{doc}/compare?from={v1}&to={other_v}", t_own)
check("ورژن متعلق به مدرک دیگر رد شد", st == 404, str(d)[:60])

st, d, _ = call("GET", f"/api/doc-archive/documents/{doc}/compare?from={v1}&to={v2}", t_out)
check("کاربر بدون دسترسی به مدرک ⇒ 403", st == 403, f"HTTP {st}")

st, d, _ = call("GET", f"/api/doc-archive/documents/{doc}/compare?from={v1}&to={v2}")
check("بدون توکن ⇒ 401", st == 401, f"HTTP {st}")

sub("مدرک داخل سطل بازیافت")
# اول گردش باز را تعیین تکلیف می‌کنیم، وگرنه حذف مسدود است
call("POST", "/api/doc-archive/documents/approval", t_app,
     {"versionId": v2, "approve": True, "comment": "تایید برای آزادسازی حذف"})
st, dl, _ = call("DELETE", f"/api/doc-archive/documents/{doc}", t_own)
check("مدرک واقعاً به سطل رفت", st == 200, f"HTTP {st} {str(dl)[:40]}")
st, d, _ = call("GET", f"/api/doc-archive/documents/{doc}/compare?from={v1}&to={v2}", t_own)
check("مقایسه مدرک حذف‌شده رد شد", st == 404, str(d)[:55])
call("PUT", f"/api/doc-archive/documents/{doc}/restore", t_own)
st, d, _ = call("GET", f"/api/doc-archive/documents/{doc}/compare?from={v1}&to={v2}", t_own)
check("پس از بازگردانی دوباره کار می‌کند", st == 200, f"HTTP {st}")

sub("سطح دسترسی «مشاهده» کافی نیست")
call("PUT", f"/api/doc-archive/documents/{doc}/permissions", t_own,
     {"isPublic": False, "publicCanDownload": False,
      "items": [{"userId": id_out, "level": 1, "canDownload": False}]})
t_out2, _ = login("cmp_out", "Pass!2345")
st, d, _ = call("GET", f"/api/doc-archive/documents/{doc}/compare?from={v1}&to={v2}", t_out2)
check("سطح View برای مقایسه کافی نیست ⇒ 403", st == 403, str(d)[:55])

call("PUT", f"/api/doc-archive/documents/{doc}/permissions", t_own,
     {"isPublic": False, "publicCanDownload": False,
      "items": [{"userId": id_out, "level": 2, "canDownload": False}]})
t_out3, _ = login("cmp_out", "Pass!2345")
st, d, _ = call("GET", f"/api/doc-archive/documents/{doc}/compare?from={v1}&to={v2}", t_out3)
check("سطح Read اجازه مقایسه می‌دهد", st == 200, f"HTTP {st}")

sys.exit(summary())
