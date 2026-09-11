#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""تست لاگ چاپ: نسخه چاپی واترمارک‌دار + ثبت لاگ + دسترسی‌ها."""
import sys
sys.path.insert(0, ".")
from lib import *
from fpdf import FPDF
from PIL import Image
import io

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

r_full = mkrole("PrtFull", [P["DocArchive.Read"], P["DocArchive.Create"],
                             P["DocArchive.Delete"], P["DocArchive.Manage"]])
r_read = mkrole("PrtRead", [P["DocArchive.Read"]])
t_own, id_own = mkuser("prt_owner", "چاپچی", [r_full])
t_out, id_out = mkuser("prt_out", "غریبه", [r_read])

st, d, _ = call("POST", "/api/doc-archive/folders", t_own,
                {"name": "چاپ", "parentId": None, "permissions": []})
fid = d["id"]
st, d, _ = call("POST", "/api/doc-archive/documents", t_own,
                {"folderId": fid, "code": "PRT-1", "title": "سند چاپ",
                 "expireDate": days(100), "approvers": [], "permissions": []})
doc = d["id"]
st, d, _ = call("GET", f"/api/doc-archive/documents/{doc}", t_own)
v1 = d["versions"][0]["id"]

# PDF واقعی دوصفحه‌ای + عکس + متن
pdf = FPDF()
pdf.add_page(); pdf.set_font("Helvetica", size=20); pdf.cell(text="Print me page 1")
pdf.add_page(); pdf.cell(text="Print me page 2")
PDF = bytes(pdf.output())
img = Image.new("RGB", (400, 300), "navy")
buf = io.BytesIO(); img.save(buf, "PNG"); PNG = buf.getvalue()
TXT = "just text".encode()

call("POST", f"/api/attachments/DocVersion/{v1}", t_own,
     files={"file": ("doc.pdf", PDF, "application/pdf")})
call("POST", f"/api/attachments/DocVersion/{v1}", t_own,
     files={"file": ("pic.png", PNG, "image/png")})
call("POST", f"/api/attachments/DocVersion/{v1}", t_own,
     files={"file": ("note.txt", TXT, "text/plain")})
st, atts, _ = call("GET", f"/api/attachments/DocVersion/{v1}", t_own)
A = {a["fileName"]: a["id"] for a in atts}

section("چاپ PDF و عکس")
st, body, hdr = call("GET", f"/api/doc-archive/print/file/{A['doc.pdf']}", t_own, raw=True)
check("چاپ PDF موفق", st == 200, f"HTTP {st}")
check("نوع خروجی PDF", "pdf" in hdr.get("Content-Type", ""), hdr.get("Content-Type"))
check("هدر X-Print-Logged", hdr.get("X-Print-Logged") == "1", hdr.get("X-Print-Logged"))
check("بایت خروجی با اصل فرق دارد (واترمارک خورده)", body != PDF, f"{len(PDF)} -> {len(body)}")
check("خروجی همچنان PDF معتبر است", body[:5] == b"%PDF-", body[:8])

st, body2, hdr2 = call("GET", f"/api/doc-archive/print/file/{A['pic.png']}", t_own, raw=True)
check("چاپ PNG موفق", st == 200, f"HTTP {st}")

section("خطاها و دسترسی‌ها")
st, e, _ = call("GET", f"/api/doc-archive/print/file/{A['note.txt']}", t_own)
check("چاپ TXT رد می‌شود (400)", st == 400, f"HTTP {st}")
st, _, _ = call("GET", f"/api/doc-archive/print/file/{A['doc.pdf']}", t_out)
check("غریبه اجازه چاپ ندارد (403)", st == 403, f"HTTP {st}")
st, _, _ = call("GET", f"/api/doc-archive/print/logs/{doc}", t_out)
check("غریبه لاگ چاپ را نمی‌بیند (403)", st == 403, f"HTTP {st}")
st, _, _ = call("GET", "/api/doc-archive/print/recent", t_out)
check("گزارش سراسری فقط برای مدیر (403)", st in (403,), f"HTTP {st}")

section("صحت لاگ")
st, logs, _ = call("GET", f"/api/doc-archive/print/logs/{doc}", t_own)
check("دو چاپ در لاگ ثبت شد", st == 200 and len(logs) == 2, f"HTTP {st} n={len(logs) if st==200 else '?'}")
pdf_log = next(l for l in logs if l["fileName"] == "doc.pdf")
check("واترمارک شامل «چاپ» است", "چاپ" in pdf_log["watermarkText"], pdf_log["watermarkText"][:40])
check("نام چاپ‌کننده در واترمارک", "چاپچی" in pdf_log["watermarkText"], pdf_log["watermarkText"][:40])
check("تعداد صفحات PDF درست (2)", pdf_log["pageCount"] == 2, pdf_log["pageCount"])
st, d, _ = call("GET", f"/api/doc-archive/documents/{doc}", t_own)
acts = [l["action"] for l in d["logs"]]
check("رویداد Print در تاریخچه مدرک", "Print" in acts, acts)
st, rec, _ = call("GET", "/api/doc-archive/print/recent", t_own)
check("گزارش سراسری چاپ‌ها", st == 200 and len(rec) >= 2, f"HTTP {st}")

print("\nتمام تست‌های چاپ اجرا شد.")
