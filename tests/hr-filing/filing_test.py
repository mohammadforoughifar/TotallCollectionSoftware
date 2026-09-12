#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""تست فاز ۳: کنترل پیش‌ارسال، ثبت ارسال لیست بیمه/مالیات/بانک، تشخیص تغییر پس از ارسال."""
import sys, os, datetime
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "doc-archive"))
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
         "firstName": first, "lastName": "ف", "isActive": True, "roleIds": roles})
    st, us, _ = call("GET", "/api/users", admin)
    ul = us if isinstance(us, list) else us.get("items", [])
    uid = next(x["id"] for x in ul if x["username"] == u)
    tok, _ = login(u, "Pass!2345")
    return tok, uid


def days(n):
    return (datetime.date.today() + datetime.timedelta(days=n)).isoformat()


r_hr = mkrole("FlHr", [P["LeaveRequests.Approve"], P["HrCore.Read"], P["HrCore.Create"],
                       P["HrPay.Read"], P["HrPay.Manage"]])
t_hr, hr_uid = mkuser("fl_hr", "کارگزین", [r_hr])
t_u1, u1 = mkuser("fl_u1", "یک", [mkrole("FlR", [P["LeaveRequests.Request"]])])
t_u2, u2 = mkuser("fl_u2", "دو", [mkrole("FlR2", [P["LeaveRequests.Request"]])])

section("پرسنل و دوره")
st, e1, _ = call("POST", "/api/hr-core/employees", t_hr,
                 {"firstName": "یک", "lastName": "ف", "nationalCode": "5555555555",
                  "hireDate": days(-200), "baseSalary": 100000000})
st, e2, _ = call("POST", "/api/hr-core/employees", t_hr,
                 {"firstName": "دو", "lastName": "ف", "nationalCode": "6666666666",
                  "hireDate": days(-200), "baseSalary": 120000000})
check("دو پرونده", st == 200, f"HTTP {st}")
call("POST", "/api/hr-time/user-links", t_hr, {"employeeId": e1["id"], "userId": u1})
call("POST", "/api/hr-time/user-links", t_hr, {"employeeId": e2["id"], "userId": u2})
call("POST", "/api/hr-pay/profiles", t_hr,
     {"employeeId": e1["id"], "childrenCount": 0, "insuranceNo": "INS-1",
      "bankName": "ملی", "iban": "IR111111111111111111111111", "accountNo": "1"})
call("POST", "/api/hr-pay/profiles", t_hr, {"employeeId": e2["id"], "childrenCount": 0})
st, run, _ = call("POST", "/api/hr-pay/runs", t_hr, {"year": 1405, "month": 6})
rid = run["id"]
st, calc, _ = call("POST", f"/api/hr-pay/runs/{rid}/calculate", t_hr)
check("محاسبه دوره با ۲ فیش", st == 200 and calc.get("slipCount") == 2, f"{calc}")

section("کنترل پیش‌ارسال")
st, pre, _ = call("GET", f"/api/hr-pay/filings/preflight?runId={rid}&kind=Insurance", t_hr)
check("دوره تأییدنشده خطا می‌دهد",
      st == 200 and pre.get("canFile") is False and any("تأیید" in e for e in pre.get("errors", [])),
      str(pre.get("errors")))
call("POST", f"/api/hr-pay/runs/{rid}/status", t_hr, {"status": "Approved"})
st, pre, _ = call("GET", f"/api/hr-pay/filings/preflight?runId={rid}&kind=Insurance", t_hr)
check("کمبود شماره بیمه خطا می‌دهد",
      any("بیمه" in e for e in pre.get("errors", [])), str(pre.get("errors")))
st, fb, _ = call("POST", "/api/hr-pay/filings", t_hr, {"runId": rid, "kind": "Insurance"})
check("ارسال با خطا رد می‌شود", st == 400, f"HTTP {st}")
call("POST", "/api/hr-pay/profiles", t_hr,
     {"employeeId": e2["id"], "childrenCount": 0, "insuranceNo": "INS-2"})
st, pre, _ = call("GET", f"/api/hr-pay/filings/preflight?runId={rid}&kind=Insurance", t_hr)
check("پس از تکمیل: آماده ارسال", pre.get("canFile") is True and len(pre.get("errors")) == 0,
      f"errors={pre.get('errors')} warnings={pre.get('warnings')}")
st, preb, _ = call("GET", f"/api/hr-pay/filings/preflight?runId={rid}&kind=Bank", t_hr)
check("بانک شبای نفر دوم را می‌خواهد", any("شبا" in e for e in preb.get("errors", [])),
      str(preb.get("errors")))

section("ثبت ارسال و صحت‌سنجی")
st, f1, _ = call("POST", "/api/hr-pay/filings", t_hr,
                 {"runId": rid, "kind": "Insurance", "receiptNo": "RC-100"})
check("ثبت ارسال بیمه", st == 200 and len(f1.get("sha256", "")) == 64 and f1.get("version") == 1
      and f1.get("receiptNo") == "RC-100" and f1.get("slipCount") == 2, f"{f1}")
st, f2, _ = call("POST", "/api/hr-pay/filings", t_hr, {"runId": rid, "kind": "Tax"})
check("ثبت ارسال مالیات", st == 200, f"HTTP {st}")
st, ver, _ = call("GET", f"/api/hr-pay/filings/verify?runId={rid}&kind=Insurance", t_hr)
check("صحت‌سنجی سالم", st == 200 and ver.get("match") is True, f"{ver}")

section("تشخیص تغییر پس از ارسال")
st, items, _ = call("GET", "/api/hr-pay/items", t_hr)
bon = next(i for i in items if i.get("code") == "BON")
call("POST", "/api/hr-pay/empitems", t_hr,
     {"employeeId": e1["id"], "itemId": bon["id"], "amount": 30000000})
call("POST", f"/api/hr-pay/runs/{rid}/calculate", t_hr)
call("POST", f"/api/hr-pay/runs/{rid}/status", t_hr, {"status": "Approved"})  # محاسبه مجدد → پیش‌نویس
st, ver2, _ = call("GET", f"/api/hr-pay/filings/verify?runId={rid}&kind=Insurance", t_hr)
check("پس از محاسبه مجدد: عدم تطابق",
      ver2.get("match") is False and len(ver2.get("diffs", [])) > 0, f"{ver2}")
st, f3, _ = call("POST", "/api/hr-pay/filings", t_hr, {"runId": rid, "kind": "Insurance"})
check("ارسال مجدد نسخه ۲", st == 200 and f3.get("version") == 2, f"v={f3.get('version')}")
st, ver3, _ = call("GET", f"/api/hr-pay/filings/verify?runId={rid}&kind=Insurance", t_hr)
check("پس از ارسال مجدد: سالم", ver3.get("match") is True)
st, lst, _ = call("GET", f"/api/hr-pay/filings?runId={rid}", t_hr)
check("بایگانی ۲ ردیف", st == 200 and len(lst) == 2
      and {x["kind"] for x in lst} == {"Insurance", "Tax"}, f"n={len(lst) if st == 200 else '?'}")

print(f"\nPASS={len(PASS)} FAIL={len(FAIL)}")
sys.exit(1 if FAIL else 0)
