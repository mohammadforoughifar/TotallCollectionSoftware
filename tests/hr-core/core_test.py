#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""تست ماژول هسته پرسنلی (کارگزینی): واحدها، پرسنل، قرارداد، حکم، داشبورد."""
import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "doc-archive"))
from lib import *
from urllib.parse import quote

admin, _ = login("admin", "admin")

st, perms, _ = call("GET", "/api/permissions", admin)
perms = perms if isinstance(perms, list) else perms.get("items", [])
P = {f"{p['module']}.{p['action']}": p["id"] for p in perms}
check("پرمیشن‌های HrCore سید شدند",
      all(k in P for k in ["HrCore.Read", "HrCore.Create", "HrCore.Update", "HrCore.Delete", "HrCore.Manage"]),
      str([k for k in P if k.startswith("HrCore.")]))


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


r_hr = mkrole("HrMgr", [P["HrCore.Read"], P["HrCore.Create"], P["HrCore.Update"],
                        P["HrCore.Delete"], P["HrCore.Manage"]])
r_none = mkrole("HrNone", [])
t_hr, _ = mkuser("hr_mgr", "کارگزین", [r_hr])
t_out, _ = mkuser("hr_out", "غریبه", [r_none])

section("ساختار سازمانی")
st, co, _ = call("POST", "/api/hr-core/org/units", t_hr,
                 {"parentId": None, "type": 0, "code": "CO", "name": "شرکت نمونه"})
check("ثبت شرکت", st == 200, f"HTTP {st}")
st, dep, _ = call("POST", "/api/hr-core/org/units", t_hr,
                  {"parentId": co["id"], "type": 2, "code": "IT", "name": "فناوری اطلاعات"})
check("ثبت دپارتمان زیرمجموعه", st == 200, f"HTTP {st}")
st, dup, _ = call("POST", "/api/hr-core/org/units", t_hr,
                  {"parentId": None, "type": 1, "code": "IT", "name": "تکراری"})
check("کد واحد تکراری رد می‌شود", st == 400, f"HTTP {st}")
st, tree, _ = call("GET", "/api/hr-core/org/tree", t_hr)
check("درخت چارت", st == 200 and len(tree) == 1 and len(tree[0]["children"]) == 1,
      f"roots={len(tree) if st == 200 else '?'})")

section("پرسنل")
st, e1, _ = call("POST", "/api/hr-core/employees", t_hr,
                 {"firstName": "علی", "lastName": "رضایی", "nationalCode": "1234567890",
                  "mobile": "09120000000", "hireDate": days(-400), "orgUnitId": dep["id"],
                  "postTitle": "کارشناس", "employmentType": 1, "baseSalary": 150000000})
check("ثبت پرسنل با کد خودکار", st == 200 and e1["code"] == "1001", f"code={e1.get('code')}")
st, e2, _ = call("POST", "/api/hr-core/employees", t_hr,
                 {"code": "2001", "firstName": "سارا", "lastName": "محمدی",
                  "nationalCode": "0987654321", "hireDate": days(-100),
                  "orgUnitId": dep["id"], "managerId": e1["id"],
                  "postTitle": "کارشناس ارشد", "employmentType": 0, "baseSalary": 200000000})
check("ثبت پرسنل دوم با مدیر", st == 200 and e2["managerName"] == "علی رضایی",
      e2.get("managerName"))
st, bad, _ = call("POST", "/api/hr-core/employees", t_hr,
                  {"firstName": "تکراری", "lastName": "تست", "nationalCode": "1234567890"})
check("کد ملی تکراری رد می‌شود", st == 400, f"HTTP {st}")
st, bad, _ = call("POST", "/api/hr-core/employees", t_hr,
                  {"firstName": "بد", "lastName": "تست", "nationalCode": "123"})
check("کد ملی نامعتبر رد می‌شود", st == 400, f"HTTP {st}")
st, sr, _ = call("GET", "/api/hr-core/employees?q=" + quote("سارا"), t_hr)
check("جستجوی پرسنل", st == 200 and sr["total"] == 1, f"total={sr.get('total')}")

section("قرارداد و هشدار پایان")
import datetime as _dt
end = (_dt.date.today() + _dt.timedelta(days=10)).isoformat()
st, c1, _ = call("POST", "/api/hr-core/contracts", t_hr,
                 {"employeeId": e1["id"], "contractNo": "C-1405-01", "type": 1,
                  "startDate": days(-300), "endDate": end, "baseSalary": 150000000,
                  "jobTitle": "کارشناس", "orgUnitId": dep["id"]})
check("ثبت قرارداد", st == 200, f"HTTP {st}")
st, exp, _ = call("GET", "/api/hr-core/contracts/expiring?days=30", t_hr)
check("هشدار قرارداد رو به پایان", st == 200 and len(exp) == 1 and exp[0]["daysToEnd"] == 10,
      f"n={len(exp) if st == 200 else '?'}")

section("حکم و اجرا")
st, d1, _ = call("POST", "/api/hr-core/decrees", t_hr,
                 {"employeeId": e1["id"], "decreeNo": "H-1", "type": 1,
                  "effectiveDate": days(0), "newPostTitle": "کارشناس ارشد",
                  "newBaseSalary": 180000000})
check("ثبت حکم ارتقا", st == 200 and not d1["isApplied"], f"HTTP {st}")
st, ap, _ = call("POST", f"/api/hr-core/decrees/{d1['id']}/apply", t_hr)
check("اجرای حکم", st == 200 and ap["isApplied"], f"HTTP {st}")
st, e1b, _ = call("GET", f"/api/hr-core/employees/{e1['id']}", t_hr)
check("پرونده با حکم به‌روز شد",
      e1b["postTitle"] == "کارشناس ارشد" and e1b["baseSalary"] == 180000000,
      f"{e1b['postTitle']} / {e1b['baseSalary']}")
st, _, _ = call("POST", f"/api/hr-core/decrees/{d1['id']}/apply", t_hr)
check("اجرای دوباره رد می‌شود", st == 400, f"HTTP {st}")
st, _, _ = call("PUT", f"/api/hr-core/decrees/{d1['id']}", t_hr,
                {"employeeId": e1["id"], "decreeNo": "H-1x", "type": 1})
check("ویرایش حکم اجراشده رد می‌شود", st == 400, f"HTTP {st}")

section("داشبورد و دسترسی‌ها")
st, dash, _ = call("GET", "/api/hr-core/dashboard", t_hr)
check("داشبورد", st == 200 and dash["activeEmployees"] == 2 and dash["expiringContracts"] == 1,
      f"emp={dash.get('activeEmployees')} exp={dash.get('expiringContracts')}")
st, _, _ = call("GET", "/api/hr-core/employees", t_out)
check("بدون مجوز 403", st == 403, f"HTTP {st}")
st, _, _ = call("DELETE", f"/api/hr-core/org/units/{dep['id']}", t_hr)
check("حذف واحد دارای پرسنل رد می‌شود", st == 400, f"HTTP {st}")

print("\nتمام تست‌های کارگزینی اجرا شد.")
