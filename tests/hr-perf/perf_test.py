#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""تست فاز ۴: دوره ارزیابی، شاخص‌ها، نمره‌دهی، کارنامه و پاداش در حقوق."""
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
         "firstName": first, "lastName": "ع", "isActive": True, "roleIds": roles})
    st, us, _ = call("GET", "/api/users", admin)
    ul = us if isinstance(us, list) else us.get("items", [])
    uid = next(x["id"] for x in ul if x["username"] == u)
    tok, _ = login(u, "Pass!2345")
    return tok, uid


def days(n):
    return (datetime.date.today() + datetime.timedelta(days=n)).isoformat()


r_hr = mkrole("PfHr", [P["LeaveRequests.Approve"], P["HrCore.Read"], P["HrCore.Create"],
                       P["HrPay.Read"], P["HrPay.Manage"]])
t_hr, hr_uid = mkuser("pf_hr", "کارگزین", [r_hr])
t_u1, u1 = mkuser("pf_u1", "یک", [mkrole("PfR", [P["LeaveRequests.Request"]])])

section("پرسنل و دوره")
st, e1, _ = call("POST", "/api/hr-core/employees", t_hr,
                 {"firstName": "یک", "lastName": "ع", "nationalCode": "7777777777",
                  "hireDate": days(-200), "baseSalary": 100000000})
check("پرونده", st == 200, f"HTTP {st}")
eid = e1["id"]
call("POST", "/api/hr-time/user-links", t_hr, {"employeeId": eid, "userId": u1})
st, p1, _ = call("POST", "/api/hr-perf/periods", t_hr,
                 {"title": "ارزیابی ۱۴۰۵", "year": 1405, "startDate": days(-300),
                  "endDate": days(60), "bonusMonthSalary": 1, "minScoreForBonus": 60})
check("ساخت دوره", st == 200 and p1.get("status") == "Draft", f"{p1}")
pid = p1["id"]
st, k1, _ = call("POST", "/api/hr-perf/kpis", t_hr,
                 {"periodId": pid, "code": "K1", "title": "بهره‌وری", "weight": 60, "maxScore": 100})
st, k2, _ = call("POST", "/api/hr-perf/kpis", t_hr,
                 {"periodId": pid, "code": "K2", "title": "نظم", "weight": 40, "maxScore": 100,
                  "category": "Behavioral"})
check("دو شاخص ۶۰/۴۰", st == 200, f"HTTP {st}")
st, op, _ = call("POST", f"/api/hr-perf/periods/{pid}/status", t_hr, {"status": "Open"})
check("باز کردن دوره", st == 200 and op.get("status") == "Open", f"{op}")
st, p2, _ = call("POST", "/api/hr-perf/periods", t_hr,
                 {"title": "بد", "year": 1404, "startDate": days(-600), "endDate": days(-400)})
call("POST", "/api/hr-perf/kpis", t_hr, {"periodId": p2["id"], "code": "X", "title": "x", "weight": 50})
call("POST", "/api/hr-perf/kpis", t_hr, {"periodId": p2["id"], "code": "Y", "title": "y", "weight": 40})
st, bad, _ = call("POST", f"/api/hr-perf/periods/{p2['id']}/status", t_hr, {"status": "Open"})
check("باز کردن با جمع وزن ۹۰ رد می‌شود", st == 400, f"HTTP {st}")

section("نمره‌دهی و کارنامه")
st, sc, _ = call("POST", "/api/hr-perf/scores", t_hr,
                 {"periodId": pid, "employeeId": eid,
                  "items": [{"kpiId": k1["id"], "score": 90}, {"kpiId": k2["id"], "score": 80}]})
check("ثبت نمرات", st == 200 and sc.get("count") == 2, f"{sc}")
st, over, _ = call("POST", "/api/hr-perf/scores", t_hr,
                   {"periodId": pid, "employeeId": eid,
                    "items": [{"kpiId": k1["id"], "score": 150}]})
check("نمره بالای سقف رد می‌شود", st == 400, f"HTTP {st}")
st, comp, _ = call("POST", "/api/hr-perf/results/compute", t_hr,
                   {"periodId": pid, "employeeId": eid})
check("محاسبه: ۸۶ و گرید B", st == 200 and comp.get("totalScore") == 86
      and comp.get("grade") == "B" and comp.get("bonusAmount") == 86000000, f"{comp}")
st, fin, _ = call("POST", "/api/hr-perf/results/finalize", t_hr,
                  {"periodId": pid, "employeeId": eid, "payYear": 1405, "payMonth": 6})
check("قطعی با پرداخت در ۶/۱۴۰۵", st == 200 and fin.get("status") == "Final"
      and fin.get("payYear") == 1405 and fin.get("payMonth") == 6, f"{fin}")

section("پاداش در فیش حقوق")
st, run, _ = call("POST", "/api/hr-pay/runs", t_hr, {"year": 1405, "month": 6})
rid = run["id"]
call("POST", f"/api/hr-pay/runs/{rid}/calculate", t_hr)
st, slips, _ = call("GET", f"/api/hr-pay/runs/{rid}/slips", t_hr)
emp_slip = next(x for x in slips if "یک" in (x.get("employeeName") or ""))
st, det, _ = call("GET", f"/api/hr-pay/slips/{emp_slip['id']}", t_hr)
ln = next((l for l in det.get("lines", []) if l.get("Code") == "SYS_PERF"), None)
check("سطر پاداش ۸۶ میلیون", ln is not None and ln.get("Amount") == 86000000, str(ln))
st, res, _ = call("GET", f"/api/hr-perf/periods/{pid}/results", t_hr)
check("فهرست کارنامه", st == 200 and len(res) == 1 and res[0]["status"] == "Final", f"n={len(res) if st == 200 else '?'}")
st, summ, _ = call("GET", f"/api/hr-perf/periods/{pid}/summary", t_hr)
check("خلاصه دوره", st == 200 and summ.get("employees") == 1 and summ.get("avgScore") == 86
      and summ.get("countB") == 1 and summ.get("totalBonus") == 86000000
      and summ.get("finalCount") == 1, f"{summ}")

section("کنترل‌های دوره بسته و ناقص")
st, _, _ = call("POST", f"/api/hr-perf/periods/{pid}/status", t_hr, {"status": "Closed"})
st, sc2, _ = call("POST", "/api/hr-perf/scores", t_hr,
                  {"periodId": pid, "employeeId": eid,
                   "items": [{"kpiId": k1["id"], "score": 50}]})
check("نمره‌دهی در دوره بسته رد می‌شود", st == 400, f"HTTP {st}")
st, ro, _ = call("POST", "/api/hr-perf/results/reopen", t_hr,
                 {"periodId": pid, "employeeId": eid})
check("برگرداندن در دوره بسته رد می‌شود", st == 400, f"HTTP {st}")
st, e2, _ = call("POST", "/api/hr-core/employees", t_hr,
                 {"firstName": "دو", "lastName": "ع", "nationalCode": "8888888888",
                  "hireDate": days(-200), "baseSalary": 100000000})
call("POST", f"/api/hr-perf/periods/{pid}/status", t_hr, {"status": "Open"})
st, _, _ = call("POST", "/api/hr-perf/scores", t_hr,
                {"periodId": pid, "employeeId": e2["id"],
                 "items": [{"kpiId": k1["id"], "score": 70}]})
st, fin2, _ = call("POST", "/api/hr-perf/results/finalize", t_hr,
                   {"periodId": pid, "employeeId": e2["id"], "payYear": 1405, "payMonth": 6})
check("قطعی با نمره ناقص رد می‌شود", st == 400, f"HTTP {st}")
st, _, _ = call("DELETE", f"/api/hr-perf/periods/{p2['id']}", t_hr)
check("حذف دوره پیش‌نویس", st == 200, f"HTTP {st}")
st, _, _ = call("DELETE", f"/api/hr-perf/periods/{pid}", t_hr)
check("حذف دوره باز رد می‌شود", st == 400, f"HTTP {st}")

print(f"\nPASS={len(PASS)} FAIL={len(FAIL)}")
sys.exit(1 if FAIL else 0)
