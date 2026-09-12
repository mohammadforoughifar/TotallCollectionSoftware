#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""تست فاز ۲: کارکرد ماهانه، تأیید اضافه‌کار محاسباتی، اتصال به حقوق، بستن ماه."""
import sys, os, datetime
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "doc-archive"))
from lib import *


def g2j(gy, gm, gd):
    """میلادی به شمسی (الگوریتم استاندارد jalaali)."""
    breaks = [-61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178]
    gy2 = gy - 1600
    gd2 = gd - 1
    g_day_no = 365 * gy2 + (gy2 + 3) // 4 - (gy2 + 99) // 100 + (gy2 + 399) // 400
    for i in range(gm - 1):
        g_day_no += [31, 29 if (gy % 4 == 0 and gy % 100 != 0) or gy % 400 == 0 else 28,
                     31, 30, 31, 30, 31, 31, 30, 31, 30, 31][i]
    g_day_no += gd2
    j_day_no = g_day_no - 79
    j_np = j_day_no // 12053
    j_day_no %= 12053
    jy = 979 + 33 * j_np + 4 * (j_day_no // 1461)
    j_day_no %= 1461
    if j_day_no >= 366:
        jy += (j_day_no - 1) // 365
        j_day_no = (j_day_no - 1) % 365
    jm = 0
    for i, d in enumerate([31, 31, 31, 31, 31, 31, 30, 30, 30, 30, 30, 29]):
        if j_day_no < d:
            jm = i + 1
            break
        j_day_no -= d
    return jy, jm, j_day_no + 1


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
         "firstName": first, "lastName": "ک", "isActive": True, "roleIds": roles})
    st, us, _ = call("GET", "/api/users", admin)
    ul = us if isinstance(us, list) else us.get("items", [])
    uid = next(x["id"] for x in ul if x["username"] == u)
    tok, _ = login(u, "Pass!2345")
    return tok, uid


def days(n):
    return (datetime.date.today() + datetime.timedelta(days=n)).isoformat()


# چهارشنبه گذشته = روز کاری مطمئن
tgt = datetime.date.today()
while tgt.weekday() != 2 or tgt >= datetime.date.today():
    tgt -= datetime.timedelta(days=1)
TGT = tgt.isoformat()
JY, JM, _ = g2j(tgt.year, tgt.month, tgt.day)

r_req = mkrole("AtReq", [P["LeaveRequests.Request"], P["Attendance.SelfCheckin"]])
r_hr = mkrole("AtHr", [P["LeaveRequests.Request"], P["LeaveRequests.Approve"],
                       P["Attendance.ManageShifts"], P["HrCore.Read"], P["HrCore.Create"], P["HrCore.Update"],
                       P["HrPay.Read"], P["HrPay.Manage"]])
t_emp, emp_uid = mkuser("at_emp", "کارمند", [r_req])
t_hr, hr_uid = mkuser("at_hr", "کارگزین", [r_hr])
t_boss, boss_uid = mkuser("at_boss", "مدیر", [r_hr])

section("پرونده، مدیر و شیفت")
st, boss_e, _ = call("POST", "/api/hr-core/employees", t_hr,
                     {"firstName": "مدیر", "lastName": "ک", "nationalCode": "3333333333",
                      "hireDate": days(-500)})
st, emp_e, _ = call("POST", "/api/hr-core/employees", t_hr,
                    {"firstName": "کارمند", "lastName": "ک", "nationalCode": "4444444444",
                     "hireDate": days(-100), "managerId": boss_e["id"], "baseSalary": 110000000})
check("پرونده با حقوق پایه", st == 200, f"HTTP {st}")
eid = emp_e["id"]
call("POST", "/api/hr-time/user-links", t_hr, {"employeeId": boss_e["id"], "userId": boss_uid})
call("POST", "/api/hr-time/user-links", t_hr, {"employeeId": eid, "userId": emp_uid})
st, sh, _ = call("POST", "/api/Attendance/shifts", t_hr,
                 {"name": "شیفت فاز۲", "startTime": "08:00", "endTime": "16:30"})
sid = sh.get("id") if isinstance(sh, dict) else None
check("ساخت شیفت", bool(sid), f"sid={sid}")
st, gen, _ = call("POST", "/api/hr-time/roster/generate", t_hr,
                  {"userIds": [emp_uid], "from": TGT, "to": TGT,
                   "pattern": [sid], "startOffset": 0})
check("روستِر روز هدف", st == 200 and gen.get("count") == 1, f"{gen}")

section("تردد و کارکرد ماهانه")
call("POST", "/api/hr-time/device/mappings", t_hr,
     {"deviceCode": "GATE-2", "userCode": "A200", "systemUserId": emp_uid})
csv = ("user_code,datetime,direction\n"
       f"A200,{TGT} 07:55,In\n"
       f"A200,{TGT} 19:00,Out\n")
st, up, _ = call("POST", "/api/hr-time/device/upload?deviceCode=GATE-2", t_hr,
                 files={"file": ("p.csv", csv.encode(), "text/csv")})
check("آپلود تردد", st == 200 and up.get("imported") == 2, f"{up}")
st, ap, _ = call("POST", "/api/hr-time/device/apply", t_hr, {"from": TGT, "to": TGT})
check("اعمال تردد", st == 200 and ap.get("punches") == 2, f"{ap}")
st, att, _ = call("GET", f"/api/hr-time/attendance?userId={emp_uid}&jy={JY}&jm={JM}", t_hr)
row = next((d for d in att.get("days", []) if str(d.get("date", "")).startswith(TGT)), None)
check("سطر کارکرد: حاضر + ۱۵۰ دقیقه عادی معلق",
      row is not None and row.get("status") == "حاضر" and row.get("otN") == 150
      and row.get("otH") == 0 and row.get("otStatus") == "Pending",
      str({k: row.get(k) for k in ("status", "otN", "otH", "otNt", "otStatus")} if row else None))
st, mine, _ = call("GET", f"/api/hr-time/attendance?jy={JY}&jm={JM}", t_emp)
check("کارمند کارکرد خودش را می‌بیند", st == 200 and mine.get("userId") == emp_uid)

section("گیت تأیید پیش از حقوق")
st, agg0, _ = call("GET", f"/api/hr-pay/attendance-month?employeeId={eid}&year={JY}&month={JM}", t_hr)
check("بدون تأیید: اضافه‌کار حقوقی صفر", st == 200 and agg0.get("otH") == 0, str({k: agg0.get(k) for k in ("otH", "otHHol", "otHNight")}))
st, run, _ = call("POST", "/api/hr-pay/runs", t_hr, {"year": JY, "month": JM})
rid = run["id"]
call("POST", f"/api/hr-pay/runs/{rid}/calculate", t_hr)
st, slips, _ = call("GET", f"/api/hr-pay/runs/{rid}/slips", t_hr)
emp_slip = next(x for x in slips if "کارمند" in (x.get("employeeName") or x.get("EmployeeName") or ""))
st, det, _ = call("GET", f"/api/hr-pay/slips/{emp_slip['id']}", t_hr)
ot_line = next((l for l in det.get("lines", []) if (l.get("code") or l.get("Code")) == "OT_NORMAL"), None)
check("سطر اضافه‌کاری فیش صفر است", ot_line is not None and (ot_line.get("amount") if "amount" in ot_line else ot_line.get("Amount")) == 0,
      str(ot_line))

section("تأیید و ورود به حقوق")
st, dec, _ = call("POST", "/api/hr-time/ot-approvals/decide", t_hr,
                  {"userId": emp_uid, "date": TGT, "approve": True, "n": 150, "h": 0, "ni": 0})
check("تأیید ۱۵۰ دقیقه", st == 200 and dec.get("status") == "Approved", f"{dec}")
st, agg1, _ = call("GET", f"/api/hr-pay/attendance-month?employeeId={eid}&year={JY}&month={JM}", t_hr)
check("پس از تأیید: ۲/۵ ساعت عادی", agg1.get("otH") == 2.5, f"otH={agg1.get('otH')}")
call("POST", f"/api/hr-pay/runs/{rid}/calculate", t_hr)
st, slips, _ = call("GET", f"/api/hr-pay/runs/{rid}/slips", t_hr)
emp_slip = next(x for x in slips if "کارمند" in (x.get("employeeName") or x.get("EmployeeName") or ""))
st, det, _ = call("GET", f"/api/hr-pay/slips/{emp_slip['id']}", t_hr)
ot_line = next((l for l in det.get("lines", []) if (l.get("code") or l.get("Code")) == "OT_NORMAL"), None)
exp = round(2.5 * (110000000 / 220) * 1.4)
got = ot_line.get("amount") if ot_line and "amount" in ot_line else (ot_line or {}).get("Amount")
check("مبلغ اضافه‌کاری فیش", got == exp, f"got={got} exp={exp}")

section("بستن ماه")
st, cls, _ = call("POST", "/api/hr-time/attend-close", t_hr, {"jy": JY, "jm": JM, "closed": True})
check("بستن ماه", st == 200 and cls.get("isClosed") is True, f"{cls}")
st, dec2, _ = call("POST", "/api/hr-time/ot-approvals/decide", t_hr,
                   {"userId": emp_uid, "date": TGT, "approve": True, "n": 150, "h": 0, "ni": 0})
check("تأیید در ماه بسته رد می‌شود", st == 400, f"HTTP {st}")
st, ap2, _ = call("POST", "/api/hr-time/device/apply", t_hr, {"from": TGT, "to": TGT})
check("اعمال تردد در ماه بسته رد می‌شود", st == 400, f"HTTP {st}")
st, _, _ = call("POST", "/api/hr-time/attend-close", t_hr, {"jy": JY, "jm": JM, "closed": False})
st, dec3, _ = call("POST", "/api/hr-time/ot-approvals/decide", t_hr,
                   {"userId": emp_uid, "date": TGT, "approve": True, "n": 120, "h": 0, "ni": 0})
check("پس از بازگشایی: تأیید با مقدار اصلاح‌شده", st == 200 and dec3.get("apprNormalMin") == 120,
      f"{dec3}")

section("درخواست دستی → حقوق")
def same_month(d):
    y, m, _ = g2j(d.year, d.month, d.day)
    return y == JY and m == JM
tgt2d = tgt + datetime.timedelta(days=7)
if not same_month(tgt2d):
    tgt2d = tgt - datetime.timedelta(days=7)
assert same_month(tgt2d), "no same-month Wednesday found"
tgt2 = tgt2d.isoformat()
st, ot, _ = call("POST", "/api/hr-time/overtime", t_emp,
                 {"workDate": tgt2, "startTime": "17:00", "endTime": "19:00",
                  "type": "Normal", "reason": "پروژه"})
check("ثبت درخواست دستی", st == 200, f"HTTP {st}")
st, tl, _ = call("GET", f"/api/hr-time/steps?requestType=Overtime&requestId={ot['id']}", t_emp)
ms = next(s["id"] for s in tl if s["role"] == "Manager")
hs = next(s["id"] for s in tl if s["role"] == "Hr")
call("POST", f"/api/hr-time/steps/{ms}/decide", t_boss, {"approve": True})
st, fin, _ = call("POST", f"/api/hr-time/steps/{hs}/decide", t_hr, {"approve": True})
check("تأیید نهایی درخواست دستی", st == 200, str(fin))
st, lst, _ = call("GET", f"/api/hr-time/ot-approvals?userId={emp_uid}&jy={JY}&jm={JM}", t_hr)
man = next((a for a in lst if str(a.get("workDate", "")).startswith(tgt2)), None)
check("ردیف Manual تأییدشده ساخته شد",
      man is not None and man.get("source") == "Manual" and man.get("status") == "Approved"
      and man.get("apprNormalMin") == 120, str(man))
st, agg2, _ = call("GET", f"/api/hr-pay/attendance-month?employeeId={eid}&year={JY}&month={JM}", t_hr)
check("جمع حقوقی: ۲ + ۲ ساعت", agg2.get("otH") == 4.0, f"otH={agg2.get('otH')}")

print(f"\nPASS={len(PASS)} FAIL={len(FAIL)}")
sys.exit(1 if FAIL else 0)
