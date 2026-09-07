#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""تست گزارش‌های خروجی آرشیو اسناد — با تمرکز بر نشت نکردن داده"""
import sys, zipfile, io, re
sys.path.insert(0, ".")
from lib import *

admin, _ = login("admin", "admin")

st, perms, _ = call("GET", "/api/permissions", admin)
perms = perms if isinstance(perms, list) else perms.get("items", [])
P = {f"{p['module']}.{p['action']}": p["id"] for p in perms}

def mkrole(n, pids):
    st, d, _ = call("POST", "/api/roles", admin, {"name": n, "permissionIds": pids})
    rid = d.get("id") if isinstance(d, dict) else None
    if not rid:                      # نقش از اجرای قبلی مانده
        st, rl, _ = call("GET", "/api/roles", admin)
        rl = rl if isinstance(rl, list) else rl.get("items", [])
        rid = next((r["id"] for r in rl if r["name"] == n), None)
    if rid:
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

section("آماده‌سازی")

doc_perms = [P["DocArchive.Read"], P["DocArchive.Create"], P["DocArchive.Delete"]]
export_p = P.get("DocArchive.Export")
check("پرمیشن DocArchive.Export وجود دارد", export_p is not None,
      f"id={export_p}" if export_p else "یافت نشد — بررسی لازم است")

r_mgr = mkrole("XpMgr", doc_perms + [P["DocArchive.Manage"]] + ([export_p] if export_p else []))
r_ltd = mkrole("XpLtd", doc_perms + ([export_p] if export_p else []))
r_noexp = mkrole("XpNoExp", doc_perms)

t_mgr, id_mgr = mkuser("xp_mgr", "مدیر", [r_mgr])
t_ltd, id_ltd = mkuser("xp_ltd", "محدود", [r_ltd])
t_nox, id_nox = mkuser("xp_nox", "بدونخروجی", [r_noexp])

# دو پوشه: یکی مشترک، یکی محرمانه
def mkfolder(name, perms):
    st, d, _ = call("POST", "/api/doc-archive/folders", t_mgr,
                    {"name": name, "parentId": None, "permissions": perms})
    if st == 200 and isinstance(d, dict) and d.get("id"): return d["id"]
    st, fl, _ = call("GET", "/api/doc-archive/folders", t_mgr)
    return next((f["id"] for f in fl if f["name"] == name), None) if isinstance(fl, list) else None

f_pub = mkfolder("عمومی", [{"userId": id_ltd, "level": 2, "canDownload": True}])
f_sec = mkfolder("محرمانه", [])
check("پوشه‌ها آماده‌اند", f_pub and f_sec, f"عمومی={f_pub} محرمانه={f_sec}")

def mkdoc(tok, fid, code, title, expire=None):
    body = {"folderId": fid, "code": code, "title": title,
            "approvers": [], "permissions": []}
    if expire: body["expireDate"] = expire
    st, d, _ = call("POST", "/api/doc-archive/documents", tok, body)
    if st == 200 and isinstance(d, dict): return d.get("id")
    # مدرک از اجرای قبلی مانده — شناسه‌اش را پیدا کن
    st, lst, _ = call("GET", "/api/doc-archive/documents?status=all", admin)
    if isinstance(lst, list):
        m = next((x for x in lst if x["code"] == code), None)
        if m: return m["id"]
    return None

d_pub1 = mkdoc(t_mgr, f_pub, "PUB-1", "سند عمومی یک", days(15))
d_pub2 = mkdoc(t_mgr, f_pub, "PUB-2", "سند عمومی دو", days(200))
d_sec1 = mkdoc(t_mgr, f_sec, "SEC-1", "سند فوق محرمانه", days(10))
d_sec2 = mkdoc(t_mgr, f_sec, "SEC-2", "سند محرمانه دو")
check("۴ مدرک در دو پوشه ساخته شد", all([d_pub1, d_pub2, d_sec1, d_sec2]))


section("۱) کاتالوگ گزارش‌ها")

st, cat, _ = call("GET", "/api/exp/reports", t_mgr)
keys = [r["key"] for r in cat] if isinstance(cat, list) else []
check("کاتالوگ برمی‌گردد", st == 200, f"{len(keys)} گزارش")
for k in ["doc-list", "doc-expiring", "doc-history", "doc-permissions", "doc-pending"]:
    check(f"گزارش «{k}» در کاتالوگ هست", k in keys)
check("گزارش‌های قبلی حذف نشده‌اند", "inv-products" in keys and "acc-ledger" in keys,
      f"مجموع {len(keys)}")


section("۲) تولید فایل سالم")

def get_file(tok, url):
    st, data, hdr = call("GET", url, tok, raw=True)
    return st, data, hdr

for key in ["doc-list", "doc-expiring", "doc-permissions", "doc-pending"]:
    st, data, hdr = get_file(t_mgr, f"/api/exp/report/{key}?format=pdf")
    ok = st == 200 and isinstance(data, bytes) and data[:4] == b"%PDF"
    check(f"PDF «{key}» تولید شد", ok,
          f"HTTP {st} — {len(data) if isinstance(data,bytes) else 0} بایت")

for key in ["doc-list", "doc-expiring"]:
    st, data, hdr = get_file(t_mgr, f"/api/exp/report/{key}?format=xlsx")
    ok = False
    if st == 200 and isinstance(data, bytes):
        try:
            zipfile.ZipFile(io.BytesIO(data)); ok = True
        except Exception: pass
    check(f"Excel «{key}» معتبر است", ok, f"HTTP {st}")

st, data, hdr = get_file(t_mgr, f"/api/exp/report/doc-history?format=pdf&id={d_pub1}")
check("PDF تاریخچه یک مدرک", st == 200 and data[:4] == b"%PDF", f"HTTP {st}")

st, d, _ = call("GET", "/api/exp/report/doc-history?format=pdf", t_mgr)
check("تاریخچه بدون شناسه مدرک رد شد", st == 400, str(d)[:60])


section("۳) 🔒 نشت اطلاعات — مهم‌ترین بخش")

def pdf_text(data):
    """استخراج تقریبی رشته‌های متنی از PDF برای بررسی نشت"""
    return data.decode("latin-1", "ignore")

def xlsx_text(data):
    try:
        z = zipfile.ZipFile(io.BytesIO(data))
        out = ""
        for n in z.namelist():
            if n.endswith(".xml"):
                out += z.read(n).decode("utf-8", "ignore")
        return out
    except Exception:
        return ""

# اکسل قابل اتکاتر است چون متن خام دارد
st, mgr_x, _ = get_file(t_mgr, "/api/exp/report/doc-list?format=xlsx")
st, ltd_x, _ = get_file(t_ltd, "/api/exp/report/doc-list?format=xlsx")
mgr_t, ltd_t = xlsx_text(mgr_x), xlsx_text(ltd_x)

check("مدیر مدارک عمومی را می‌بیند", "PUB-1" in mgr_t)
check("مدیر مدارک محرمانه را می‌بیند", "SEC-1" in mgr_t)
check("کاربر محدود مدارک عمومی را می‌بیند", "PUB-1" in ltd_t)
check("🔒 کاربر محدود SEC-1 را نمی‌بیند", "SEC-1" not in ltd_t,
      "نشت!" if "SEC-1" in ltd_t else "امن")
check("🔒 کاربر محدود SEC-2 را نمی‌بیند", "SEC-2" not in ltd_t,
      "نشت!" if "SEC-2" in ltd_t else "امن")

sub("گزارش انقضا هم نباید نشت کند")
st, ltd_e, _ = get_file(t_ltd, "/api/exp/report/doc-expiring?format=xlsx")
et = xlsx_text(ltd_e)
check("🔒 SEC-1 (۱۰ روز مانده) در گزارش انقضای کاربر محدود نیست",
      "SEC-1" not in et, "نشت!" if "SEC-1" in et else "امن")
check("PUB-1 (۱۵ روز مانده) هست", "PUB-1" in et)

sub("جعل پارامتر userId در query string")
st, forged, _ = get_file(t_ltd, "/api/exp/report/doc-list?format=xlsx&userId=1&isManager=true")
ft = xlsx_text(forged)
check("🔒 جعل userId اثری ندارد", "SEC-1" not in ft,
      "نشت!" if "SEC-1" in ft else "هویت از توکن خوانده می‌شود")

sub("تاریخچه مدرکی که دسترسی ندارد")
st, d, _ = call("GET", f"/api/exp/report/doc-history?format=pdf&id={d_sec1}", t_ltd)
check("🔒 تاریخچه مدرک محرمانه برای کاربر محدود رد شد", st in (400, 403), str(d)[:60])

sub("ماتریس دسترسی فقط برای مدیر")
st, data, _ = get_file(t_mgr, "/api/exp/report/doc-permissions?format=pdf")
check("مدیر ماتریس دسترسی را می‌گیرد", st == 200, f"HTTP {st}")
st, d, _ = call("GET", "/api/exp/report/doc-permissions?format=pdf", t_ltd)
check("🔒 کاربر غیرمدیر ماتریس دسترسی نمی‌گیرد", st in (400, 403), str(d)[:60])


section("۴) کنترل دسترسی endpoint")

st, d, _ = call("GET", "/api/exp/report/doc-list?format=pdf")
check("بدون توکن ⇒ 401", st == 401, f"HTTP {st}")

if export_p:
    st, d, _ = call("GET", "/api/exp/report/doc-list?format=pdf", t_nox)
    check("کاربر بدون مجوز Export ⇒ 403", st == 403, f"HTTP {st}")

st, d, _ = call("GET", "/api/exp/report/doc-nonexistent?format=pdf", t_mgr)
check("گزارش ناموجود ⇒ 404", st == 404, str(d)[:50])


section("۵) صحت محتوا")

st, mgr_x2, _ = get_file(t_mgr, "/api/exp/report/doc-list?format=xlsx&status=all")
t2 = xlsx_text(mgr_x2)
check("فیلتر status=all همه مدارک را می‌آورد",
      all(c in t2 for c in ["PUB-1", "PUB-2", "SEC-1", "SEC-2"]))

st, srch, _ = get_file(t_mgr, "/api/exp/report/doc-list?format=xlsx&search=PUB-1")
s2 = xlsx_text(srch)
check("فیلتر جستجو اعمال می‌شود", "PUB-1" in s2 and "SEC-1" not in s2)

# مدرک غیرفعال
call("PUT", f"/api/doc-archive/documents/{d_pub2}/active", t_mgr,
     {"isActive": False, "reason": "تست"})
st, act, _ = get_file(t_mgr, "/api/exp/report/doc-list?format=xlsx")
check("مدرک غیرفعال در فهرست پیش‌فرض نیست", "PUB-2" not in xlsx_text(act))
st, inact, _ = get_file(t_mgr, "/api/exp/report/doc-list?format=xlsx&status=inactive")
check("مدرک غیرفعال با status=inactive می‌آید", "PUB-2" in xlsx_text(inact))

sys.exit(summary())
