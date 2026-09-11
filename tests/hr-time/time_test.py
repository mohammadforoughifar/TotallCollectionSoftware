#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""تست ماژول تکمیلی حضوروغیاب/زمان‌بندی: قوانین، موجودی، تصویب چندمرحله‌ای، اضافه‌کاری، روستِر، دستگاه."""
import sys, os, datetime
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "doc-archive"))
from lib import *

admin, _ = login("admin", "admin")

st, perms, _ = call("GET", "/api/permissions", admin)
perms = perms if isinstance(perms, list) else perms.get("items", [])
P = {f"{p['module']}.{p['action']}": p["id"] for p in perms}
need = ["LeaveRequests.Request", "LeaveRequests.Approve", "Attendance.ManageShifts", "Attendance.SelfCheckin",
        "HrCore.Read", "HrCore.Create", "HrCore.Update"]
check("پرمیشن‌های لازم سید شدند", all(k in P for k in need), str([k for k in need if k not in P]))


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


def days(n):
    return (datetime.date.today() + datetime.timedelta(days=n)).isoformat()


r_req = mkrole("TmReq", [P["LeaveRequests.Request"], P["Attendance.SelfCheckin"]])
r_hr = mkrole("TmHr", [P["LeaveRequests.Request"], P["LeaveRequests.Approve"],
                       P["Attendance.ManageShifts"], P["HrCore.Read"], P["HrCore.Create"], P["HrCore.Update"]])
t_emp, emp_uid = mkuser("tm_emp", "کارمند", [r_req])
t_hr, hr_uid = mkuser("tm_hr", "کارگزین", [r_hr])
t_boss, boss_uid = mkuser("tm_boss", "مدیر", [r_hr])

section("قوانین")
st, rules, _ = call("GET", "/api/hr-time/rules", t_hr)
check("خواندن قوانین با سید پیش‌فرض", st == 200 and rules.get("night.start") == "22:00", f"HTTP {st}")
st, _, _ = call("PUT", "/api/hr-time/rules", t_hr, {"mission.allowance.outer": "1500000"})
st, rules, _ = call("GET", "/api/hr-time/rules", t_hr)
check("ویرایش قانون حق ماموریت", rules.get("mission.allowance.outer") == "1500000")
st, _, _ = call("GET", "/api/hr-time/rules", t_emp)
check("کاربر عادی به قوانین دسترسی ندارد", st == 403, f"HTTP {st}")

section("پرونده و مدیر مستقیم")
st, co, _ = call("POST", "/api/hr-core/org/units", t_hr,
                 {"parentId": None, "type": 0, "code": "TMCO", "name": "شرکت زمان"})
st, boss_e, _ = call("POST", "/api/hr-core/employees", t_hr,
                     {"firstName": "مدیر", "lastName": "ت", "nationalCode": "1111111111",
                      "hireDate": days(-500)})
st, emp_e, _ = call("POST", "/api/hr-core/employees", t_hr,
                    {"firstName": "کارمند", "lastName": "ت", "nationalCode": "2222222222",
                     "hireDate": days(-100), "managerId": boss_e["id"]})
check("اتصال کارمند به مدیر", st == 200 and emp_e.get("managerName") == "مدیر ت", str(emp_e.get("managerName")))
call("POST", "/api/hr-time/user-links", t_hr, {"employeeId": boss_e["id"], "userId": boss_uid})
call("POST", "/api/hr-time/user-links", t_hr, {"employeeId": emp_e["id"], "userId": emp_uid})
st, mgr, _ = call("GET", "/api/hr-time/manager", t_emp)
check("تشخیص مدیر مستقیم", st == 200 and mgr.get("userId") == boss_uid, str(mgr))

section("موجودی مرخصی")
st, bal, _ = call("GET", "/api/hr-time/balances?year=1405", t_emp)
items = {b["category"]: b for b in bal["items"]} if st == 200 else {}
check("موجودی اولیه استحقاقی ۲۴ روز", items.get("Annual", {}).get("granted") == 24, str(items.get("Annual")))
st, _, _ = call("POST", "/api/hr-time/balances/grant", t_hr,
                {"userId": emp_uid, "year": 1405, "category": "Annual", "days": 30})
st, bal, _ = call("GET", f"/api/hr-time/balances?userId={emp_uid}&year=1405", t_hr)
items = {b["category"]: b for b in bal["items"]}
check("ویرایش سهمیه توسط کارگزینی", items["Annual"]["granted"] == 30, str(items["Annual"]["granted"]))

section("تصویب چندمرحله‌ای مرخصی")
st, lv, _ = call("POST", "/api/hr-time/leaves", t_emp,
                 {"type": "Daily", "startDate": days(10), "endDate": days(11),
                  "days": 2, "hours": 0, "reason": "تست", "category": "Annual"})
check("ثبت مرخصی استحقاقی", st == 200, f"HTTP {st} {lv}")
lid = lv["id"]
st, tl, _ = call("GET", f"/api/hr-time/steps?requestType=Leave&requestId={lid}", t_emp)
roles = [s["role"] for s in tl] if st == 200 else []
check("گردش دو مرحله‌ای مدیر+کارگزینی", roles == ["Manager", "Hr"], str(roles))
st, pend, _ = call("GET", "/api/hr-time/steps/pending", t_boss)
check("مدیر مرحله خود را می‌بیند", st == 200 and any(s["requestId"] == lid for s in pend), f"n={len(pend) if st==200 else '?'}")
hr_step = next(s["id"] for s in tl if s["role"] == "Hr")
mgr_step = next(s["id"] for s in tl if s["role"] == "Manager")
st, early, _ = call("POST", f"/api/hr-time/steps/{hr_step}/decide", t_hr, {"approve": True})
check("تأیید مرحله دوم قبل از اول رد می‌شود", st == 400, f"HTTP {st}")
st, _, _ = call("POST", f"/api/hr-time/steps/{mgr_step}/decide", t_boss, {"approve": True, "note": "باشه"})
check("تأیید مدیر", st == 200, f"HTTP {st}")
st, no, _ = call("POST", f"/api/hr-time/steps/{hr_step}/decide", t_emp, {"approve": True})
check("کارمند نمی‌تواند مرحله کارگزینی را تأیید کند", st == 400, f"HTTP {st}")
st, fin, _ = call("POST", f"/api/hr-time/steps/{hr_step}/decide", t_hr, {"approve": True})
check("تأیید نهایی کارگزینی", st == 200, str(fin))
st, mine, _ = call("GET", "/api/hr-time/leaves/mine", t_emp)
mine_lv = next(x for x in mine if x["id"] == lid)
check("وضعیت نهایی Approved", mine_lv["status"] == "Approved", mine_lv["status"])
st, bal, _ = call("GET", "/api/hr-time/balances?year=1405", t_emp)
used = next(b for b in bal["items"] if b["category"] == "Annual")["used"]
check("مصرف ۲ روز در موجودی", used == 2, f"used={used}")

section("سقف سهمیه")
st, _, _ = call("POST", "/api/hr-time/balances/grant", t_hr,
                {"userId": emp_uid, "year": 1405, "category": "Annual", "days": 2})
st, over, _ = call("POST", "/api/hr-time/leaves", t_emp,
                   {"type": "Daily", "startDate": days(20), "endDate": days(20),
                    "days": 1, "hours": 0, "category": "Annual"})
check("درخواست بیش از سهمیه رد می‌شود", st == 400, f"HTTP {st}")
st, _, _ = call("POST", "/api/hr-time/balances/grant", t_hr,
                {"userId": emp_uid, "year": 1405, "category": "Annual", "days": 30})

section("ماموریت و حق ماموریت")
st, mi, _ = call("POST", "/api/hr-time/leaves", t_emp,
                 {"type": "Mission", "startDate": days(30), "endDate": days(31),
                  "days": 2, "hours": 0, "destination": "مشهد", "missionKind": "Outer"})
check("ثبت ماموریت خارج شهر", st == 200, f"HTTP {st} {mi}")
st, mine, _ = call("GET", "/api/hr-time/leaves/mine", t_emp)
mine_mi = next(x for x in mine if x["id"] == mi["id"])
check("حق ماموریت ۲×۱۵۰۰۰۰۰", mine_mi.get("allowanceAmount") == 3000000,
      f"allowance={mine_mi.get('allowanceAmount')}")

section("اضافه‌کاری")
st, ot, _ = call("POST", "/api/hr-time/overtime", t_emp,
                 {"workDate": days(5), "startTime": "17:00", "endTime": "19:30",
                  "type": "Normal", "reason": "پروژه"})
check("ثبت اضافه‌کاری", st == 200 and ot.get("number", "").startswith("OT/"), f"HTTP {st} {ot}")
st, tl, _ = call("GET", f"/api/hr-time/steps?requestType=Overtime&requestId={ot['id']}", t_emp)
check("گردش اضافه‌کاری دو مرحله‌ای", [s["role"] for s in tl] == ["Manager", "Hr"])
ms = next(s["id"] for s in tl if s["role"] == "Manager")
hs = next(s["id"] for s in tl if s["role"] == "Hr")
call("POST", f"/api/hr-time/steps/{ms}/decide", t_boss, {"approve": True})
st, fin, _ = call("POST", f"/api/hr-time/steps/{hs}/decide", t_hr, {"approve": True})
check("تأیید نهایی اضافه‌کاری", st == 200, str(fin))
st, ot2, _ = call("POST", "/api/hr-time/overtime", t_emp,
                  {"workDate": days(6), "startTime": "17:00", "endTime": "18:00", "type": "Night"})
st, tl2, _ = call("GET", f"/api/hr-time/steps?requestType=Overtime&requestId={ot2['id']}", t_emp)
ms2 = next(s["id"] for s in tl2 if s["role"] == "Manager")
hs2 = next(s["id"] for s in tl2 if s["role"] == "Hr")
call("POST", f"/api/hr-time/steps/{ms2}/decide", t_boss, {"approve": True})
st, rej, _ = call("POST", f"/api/hr-time/steps/{hs2}/decide", t_hr, {"approve": False, "note": "نیاز نیست"})
st, lst, _ = call("GET", "/api/hr-time/overtime?status=Rejected", t_hr)
check("رد اضافه‌کاری با علت", st == 200 and any(o["id"] == ot2["id"] for o in lst), str(rej))

section("روستِر شیفت چرخشی")
st, sh, _ = call("POST", "/api/Attendance/shifts", t_hr,
                 {"name": "شیفت تست", "startTime": "08:00", "endTime": "16:30"})
sid = (sh.get("id") if isinstance(sh, dict) else None)
if not sid:
    st2, allsh, _ = call("GET", "/api/Attendance/shifts", t_hr)
    sid = next(s["id"] for s in allsh if s["name"] == "شیفت تست")
check("ساخت شیفت", bool(sid), f"sid={sid}")
st, gen, _ = call("POST", "/api/hr-time/roster/generate", t_hr,
                  {"userIds": [emp_uid], "from": days(1), "to": days(7),
                   "pattern": [sid], "startOffset": 0})
check("تولید روستِر ۷ روزه", st == 200 and gen.get("count") == 7, f"HTTP {st} {gen}")
st, ros, _ = call("GET", f"/api/hr-time/roster?userId={emp_uid}&from={days(1)}&to={days(7)}", t_hr)
check("خواندن روستِر", st == 200 and len(ros) == 7 and ros[0]["shiftName"] == "شیفت تست",
      f"n={len(ros) if st == 200 else '?'}")

section("ایمپورت دستگاه")
st, _, _ = call("POST", "/api/hr-time/device/mappings", t_hr,
                {"deviceCode": "GATE-1", "userCode": "D100", "systemUserId": emp_uid})
check("نگاشت کد دستگاه", st == 200, f"HTTP {st}")
csv = ("user_code,datetime,direction\n"
       f"D100,{days(-2)} 08:04,In\n"
       f"D100,{days(-2)} 16:40,Out\n"
       "D999,{} 09:00,In\n".format(days(-2)))
st, up, _ = call("POST", "/api/hr-time/device/upload?deviceCode=GATE-1", t_hr,
                 files={"file": ("punches.csv", csv.encode(), "text/csv")})
check("آپلود CSV ترددها", st == 200 and up.get("imported") == 3, f"HTTP {st} {up}")
st, ap, _ = call("POST", "/api/hr-time/device/apply", t_hr, {"from": days(-2), "to": days(-2)})
check("اعمال ترددها (۲ تردد + ۱ بدون نگاشت)", st == 200 and ap.get("punches") == 2 and "D999" in ap.get("unmapped", []),
      f"HTTP {st} {ap}")
st, hist, _ = call("GET", "/api/Attendance/my-history", t_emp)
rec = next((h for h in (hist if isinstance(hist, list) else hist.get("items", []))
            if str(h.get("workDate", "")).startswith(days(-2))), None)
check("رکورد کارکرد از دستگاه ساخته شد", rec is not None and (rec.get("workMinutes") or 0) > 400,
      str({k: rec.get(k) for k in ("workMinutes", "lateMinutes", "overtimeMinutes")} if rec else None))

print(f"\nPASS={len(PASS)} FAIL={len(FAIL)}")
sys.exit(1 if FAIL else 0)
