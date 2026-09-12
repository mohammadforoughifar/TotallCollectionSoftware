#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""تست فاز ۱ قانونی: مرخصی‌های ماده ۷۳، سقف ذخیره ۹ روز، مشاغل سخت، عیدی، آزمایشی، کد ملی، حداقل‌مزد."""
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
         "firstName": first, "lastName": "ق", "isActive": True, "roleIds": roles})
    st, us, _ = call("GET", "/api/users", admin)
    ul = us if isinstance(us, list) else us.get("items", [])
    uid = next(x["id"] for x in ul if x["username"] == u)
    tok, _ = login(u, "Pass!2345")
    return tok, uid


def days(n):
    return (datetime.date.today() + datetime.timedelta(days=n)).isoformat()


r_hr = mkrole("LgHr", [P["LeaveRequests.Request"], P["LeaveRequests.Approve"],
                       P["HrCore.Read"], P["HrCore.Create"], P["HrCore.Update"],
                       P["HrPay.Read"], P["HrPay.Manage"]])
t_hr, hr_uid = mkuser("lg_hr", "کارگزین", [r_hr])
t_emp, emp_uid = mkuser("lg_emp", "کارمند", [mkrole("LgReq", [P["LeaveRequests.Request"]])])

section("اعتبارسنجی کد ملی")
st, v, _ = call("GET", "/api/hr-time/validate-national/0024576816", t_hr)
check("کد معتبر قبول", st == 200 and v.get("valid") is True, str(v))
st, v, _ = call("GET", "/api/hr-time/validate-national/1234567890", t_hr)
check("کد نامعتبر رد", st == 200 and v.get("valid") is False, str(v))
st, v, _ = call("GET", "/api/hr-time/validate-national/0000000000", t_hr)
check("کد تکراری‌رقم رد", st == 200 and v.get("valid") is False, str(v))
st, bad, _ = call("POST", "/api/hr-time/employees", t_hr,
                  {"firstName": "x", "lastName": "y", "nationalCode": "1234567890",
                   "hireDate": days(-100)})
check("ثبت پرسنل با کد نامعتبر رد می‌شود", st == 400, f"HTTP {st}")

section("پرسنل + حداقل‌مزد مصوب")
st, emp, _ = call("POST", "/api/hr-time/employees", t_hr,
                  {"firstName": "کارمند", "lastName": "ق", "nationalCode": "0024576816",
                   "hireDate": days(-400), "baseSalary": 120000000})
check("ثبت پرسنل با کد معتبر", st == 200, f"HTTP {st} {emp}")
eid = emp["id"]
call("POST", "/api/hr-time/user-links", t_hr, {"employeeId": eid, "userId": emp_uid})
st, mw, _ = call("POST", "/api/hr-time/minwages", t_hr,
                 {"year": 1405, "monthlyWage": 103909270})
check("ثبت حداقل‌مزد ۱۴۰۵", st == 200 and mw.get("dailyWage") == round(103909270 / 30),
      f"daily={mw.get('dailyWage')}")
st, mwl, _ = call("GET", "/api/hr-time/minwages", t_hr)
check("فهرست حداقل‌مزد", st == 200 and any(m["year"] == 1405 for m in mwl))

section("مرخصی‌های ماده ۷۳ و حج")
st, lv, _ = call("POST", "/api/hr-time/leaves", t_emp,
                 {"type": "Daily", "startDate": days(10), "endDate": days(12),
                  "reason": "ازدواج", "category": "Marriage"})
check("مرخصی ازدواج ۳ روزه", st == 200, f"HTTP {st} {lv}")
st, over, _ = call("POST", "/api/hr-time/leaves", t_emp,
                   {"type": "Daily", "startDate": days(20), "endDate": days(20),
                    "category": "Marriage"})
check("روز چهارم ازدواج رد می‌شود", st == 400, f"HTTP {st}")
st, lv2, _ = call("POST", "/api/hr-time/leaves", t_emp,
                  {"type": "Daily", "startDate": days(30), "endDate": days(31),
                   "category": "Bereavement"})
check("مرخصی فوت ۲ روزه", st == 200, f"HTTP {st}")
st, bal, _ = call("GET", "/api/hr-time/balances?year=1405", t_emp)
items = {b["category"]: b for b in bal["items"]}
check("موجودی دسته‌های جدید", items.get("Marriage", {}).get("pending") == 3
      and items.get("Bereavement", {}).get("pending") == 2
      and items.get("Hajj", {}).get("granted") == 30, str({k: items.get(k) for k in ("Marriage", "Bereavement", "Hajj")}))

section("سقف ذخیره ۹ روز (ماده ۶۶)")
call("POST", "/api/hr-time/balances/grant", t_hr,
     {"userId": emp_uid, "year": 1405, "category": "Annual", "days": 30})
st, co, _ = call("POST", "/api/hr-time/balances/carryover", t_hr,
                 {"userId": emp_uid, "year": 1405})
check("انتقال محدود به ۹ روز", st == 200 and co.get("carry") == 9 and co.get("nextGrant") == 33,
      f"{co}")
st, bal2, _ = call("GET", f"/api/hr-time/balances?userId={emp_uid}&year=1406", t_hr)
ann = next(b for b in bal2["items"] if b["category"] == "Annual")
check("سهمیه ۱۴۰۶ = ۲۴ + ۹", ann["granted"] == 33, f"granted={ann['granted']}")

section("مشاغل سخت و زیان‌آور")
call("POST", "/api/hr-pay/profiles", t_hr,
     {"employeeId": eid, "childrenCount": 0, "isHardJob": True})
st, run, _ = call("POST", "/api/hr-pay/runs", t_hr, {"year": 1405, "month": 6})
rid = run["id"]
st, calc, _ = call("POST", f"/api/hr-pay/runs/{rid}/calculate", t_hr)
st, slips, _ = call("GET", f"/api/hr-pay/runs/{rid}/slips", t_hr)
s = slips[0]
exp_er = round(s["insuranceableAmount"] * 0.27)
check("بیمه کارفرما ۲۷٪ (۲۰+۳+۴)", s["employerInsurance"] == exp_er,
      f"er={s['employerInsurance']} exp={exp_er}")
call("POST", "/api/hr-pay/profiles", t_hr, {"employeeId": eid, "childrenCount": 0, "isHardJob": False})

section("عیدی سالانه")
st, eydi, _ = call("POST", "/api/hr-pay/runs/eydi", t_hr, {"year": 1405})
check("ساخت و محاسبه عیدی", st == 200 and eydi.get("slipCount") == 1, f"{eydi}")
st, eslips, _ = call("GET", f"/api/hr-pay/runs/{eydi['id']}/slips", t_hr)
e = eslips[0]
exp_full = min(2 * s["grossEarnings"], 3 * 103909270)
check("عیدی = ۲×آخرین حقوق تا سقف ۳×حداقل", e["grossEarnings"] == round(exp_full),
      f"eydi={e['grossEarnings']} exp={round(exp_full)}")
check("عیدی بدون بیمه", e["insuranceAmount"] == 0 and e["employerInsurance"] == 0)

section("دوره آزمایشی (ماده ۱۱)")
st, pr, _ = call("POST", "/api/hr-time/probations", t_hr,
                 {"employeeId": eid, "startDate": days(-80), "months": 3, "skillLevel": "Skilled"})
check("شروع آزمایشی ۳ ماهه", st == 200, f"HTTP {st} {pr}")
st, badm, _ = call("POST", "/api/hr-time/probations", t_hr,
                   {"employeeId": eid, "startDate": days(0), "months": 2})
check("مدت نامعتبر رد می‌شود", st == 400, f"HTTP {st}")
st, soon, _ = call("GET", "/api/hr-time/probations?endingWithinDays=30", t_hr)
check("هشدار اتمام آزمایشی", st == 200 and any(x["employeeId"] == eid for x in soon),
      f"n={len(soon) if st == 200 else '?'}")
st, done, _ = call("POST", f"/api/hr-time/probations/{pr['id']}/complete", t_hr,
                   {"pass": True, "note": "قبول"})
check("اتمام با قبولی", st == 200 and done.get("status") == "Passed", str(done))

section("ارفاق شیردهی (ماده ۷۸)")
st, nb, _ = call("POST", "/api/hr-time/nursing", t_hr,
                 {"employeeId": eid, "childBirthDate": days(-100)})
check("ثبت شیردهی (فرزند زیر ۲ سال)", st == 200, f"HTTP {st}")
st, old, _ = call("POST", "/api/hr-time/nursing", t_hr,
                  {"employeeId": eid, "childBirthDate": days(-800)})
check("فرزند بالای ۲ سال رد می‌شود", st == 400, f"HTTP {st}")
st, ob, _ = call("GET", f"/api/hr-time/obligation?userId={emp_uid}", t_hr)
check("موظفی با کسر ۱۲۰ دقیقه شیردهی", st == 200 and ob.get("nursingMin") == 120
      and ob.get("requiredMin") == ob.get("scheduledMin") - 120, str(ob))

print(f"\nPASS={len(PASS)} FAIL={len(FAIL)}")
sys.exit(1 if FAIL else 0)
