#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""تست حقوق و دستمزد: فرمول‌ساز، محاسبه از کارکرد، بیمه/مالیات، وام، قفل، سند، خروجی‌ها، تسویه."""
import sys, os, datetime
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "doc-archive"))
from lib import *

admin, _ = login("admin", "admin")

st, perms, _ = call("GET", "/api/permissions", admin)
perms = perms if isinstance(perms, list) else perms.get("items", [])
P = {f"{p['module']}.{p['action']}": p["id"] for p in perms}
check("پرمیشن‌های HrPay سید شدند", "HrPay.Read" in P and "HrPay.Manage" in P,
      str([k for k in P if k.startswith("HrPay.")]))


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


r_pay = mkrole("PayMgr", [P["HrPay.Read"], P["HrPay.Manage"],
                          P["HrCore.Read"], P["HrCore.Create"], P["HrCore.Update"],
                          P["LeaveRequests.Approve"]])
r_none = mkrole("PayNone", [])
t_mgr, mgr_uid = mkuser("pay_mgr", "حقوق", [r_pay])
t_emp, emp_uid = mkuser("pay_emp", "کارمند", [r_none])
t_out, _ = mkuser("pay_out", "غریبه", [r_none])

section("فرمول‌ساز")
st, items, _ = call("GET", "/api/hr-pay/items", t_mgr)
codes = [i["code"] for i in items] if st == 200 else []
check("سید آیتم‌های پیش‌فرض", st == 200 and len(items) >= 9 and "BASE_HOKM" in codes, f"n={len(items) if st==200 else '?'}")
st, t1, _ = call("POST", "/api/hr-pay/items/test", t_mgr, {"formula": "BASE*DAYS_PAID/30"})
check("تست فرمول پایه", st == 200 and t1.get("value") == 100000000, str(t1))
st, t2, _ = call("POST", "/api/hr-pay/items/test", t_mgr, {"formula": "IF(CHILDREN>=1,BON,MASKAN)+ROUND(OT_H*2)"})
check("تست IF و ROUND", st == 200 and t2.get("value") == 22000000 + 20, str(t2))
st, bad, _ = call("POST", "/api/hr-pay/items/test", t_mgr, {"formula": "BASE/0"})
check("تقسیم بر صفر خطا می‌دهد", st == 400, f"HTTP {st}")
st, bad, _ = call("POST", "/api/hr-pay/items/test", t_mgr, {"formula": "FOO+1"})
check("متغیر ناشناخته خطا می‌دهد", st == 400, f"HTTP {st}")
st, _, _ = call("POST", "/api/hr-pay/items", t_mgr, {"code": "نامعتبر", "title": "x"})
check("کد فارسی رد می‌شود", st == 400, f"HTTP {st}")
st, it, _ = call("POST", "/api/hr-pay/items", t_mgr,
                 {"code": "BONUSX", "title": "پاداش تست", "kind": "Earning",
                  "category": "Mazaya", "formula": "BASE*0.1", "sortOrder": 34})
check("ساخت آیتم فرمولی", st == 200, f"HTTP {st}")
call("DELETE", f"/api/hr-pay/items/{it['id']}", t_mgr)
st, _, _ = call("GET", "/api/hr-pay/items", t_emp)
check("بدون مجوز 403", st == 403, f"HTTP {st}")

section("پرسنل و پروفایل")
st, emp, _ = call("POST", "/api/hr-core/employees", t_mgr,
                  {"firstName": "کارمند", "lastName": "تست", "nationalCode": "3333333333",
                   "hireDate": days(-730), "baseSalary": 120000000})
check("ثبت پرسنل با پایه ۱۲۰م", st == 200, f"HTTP {st}")
call("POST", "/api/hr-time/user-links", t_mgr, {"employeeId": emp["id"], "userId": emp_uid})
eid = emp["id"]
st, pf, _ = call("POST", "/api/hr-pay/profiles", t_mgr,
                 {"employeeId": eid, "childrenCount": 2, "insuranceNo": "INS-1",
                  "bankName": "ملی", "iban": "IR123", "accountNo": "456"})
check("پروفایل حقوقی (۲ اولاد)", st == 200, f"HTTP {st}")

section("قوانین و پلکان")
st, rules, _ = call("GET", "/api/hr-pay/rules", t_mgr)
check("قوانین پیش‌فرض حقوق", st == 200 and rules.get("pay.ins.employee") == "7", f"HTTP {st}")
call("PUT", "/api/hr-pay/rules", t_mgr, {"pay.bon": "20000000", "pay.workshop.code": "WS-77"})
st, br, _ = call("GET", "/api/hr-pay/brackets?year=1405", t_mgr)
for b in br:
    call("DELETE", f"/api/hr-pay/brackets/{b['id']}", t_mgr)
call("POST", "/api/hr-pay/brackets", t_mgr, {"year": 1405, "fromAmount": 0, "toAmount": 50000000, "rate": 0})
call("POST", "/api/hr-pay/brackets", t_mgr, {"year": 1405, "fromAmount": 50000000, "toAmount": 0, "rate": 10})
st, br, _ = call("GET", "/api/hr-pay/brackets?year=1405", t_mgr)
check("پلکان ساده ۲ پله‌ای", st == 200 and len(br) == 2, f"n={len(br) if st==200 else '?'}")

section("وام و معوقه و علی‌الحساب")
st, ln, _ = call("POST", "/api/hr-pay/loans", t_mgr,
                 {"employeeId": eid, "kind": "Loan", "amount": 10000000,
                  "installments": 5, "startYear": 1405, "startMonth": 6})
check("ثبت وام ۵ قسطه", st == 200 and ln.get("monthlyAmount") == 2000000, f"HTTP {st} {ln}")
st, _, _ = call("POST", "/api/hr-pay/arrears", t_mgr,
                {"employeeId": eid, "year": 1405, "month": 6, "amount": 500000, "title": "معوقه تست"})
check("ثبت معوقه", st == 200, f"HTTP {st}")
st, _, _ = call("POST", "/api/hr-pay/onaccounts", t_mgr,
                {"employeeId": eid, "year": 1405, "month": 6, "amount": 1000000})
check("ثبت علی‌الحساب", st == 200, f"HTTP {st}")

section("محاسبه حقوق")
st, run, _ = call("POST", "/api/hr-pay/runs", t_mgr, {"year": 1405, "month": 6})
check("ساخت دوره ۶/۱۴۰۵", st == 200, f"HTTP {st}")
rid = run["id"]
st, run2, _ = call("POST", f"/api/hr-pay/runs/{rid}/calculate", t_mgr)
check("محاسبه دوره", st == 200 and run2.get("slipCount") == 1, f"HTTP {st} {run2}")
st, slips, _ = call("GET", f"/api/hr-pay/runs/{rid}/slips", t_mgr)
s = slips[0]
import json as _json
lines = {l["Code"]: l["Amount"] for l in _json.loads(s["detailsJson"])}
exp_gross = 120000000 + 20000000 + 9000000 + lines["OLAD"] + 500000
check("ناخالص = پایه+بن+مسکن+اولاد+معوقه", s["grossEarnings"] == exp_gross,
      f"gross={s['grossEarnings']} olad={lines['OLAD']}")
check("ذخیره سنوات در ناخالص نیست", lines.get("SANAVAT") == 10000000 and s["grossEarnings"] == exp_gross)
check("بیمه ۷٪ مشمول (بدون اولاد)", s["insuranceAmount"] == round((exp_gross - lines["OLAD"]) * 0.07),
      f"ins={s['insuranceAmount']}")
check("مالیات پلکانی ۱۰٪", s["taxAmount"] == round((exp_gross - lines["OLAD"] - 50000000) * 0.10),
      f"tax={s['taxAmount']}")
check("قسط وام و علی‌الحساب در کسور", lines.get("SYS_LOAN") == 2000000 and lines.get("SYS_ONACC") == 1000000,
      f"loan={lines.get('SYS_LOAN')} onacc={lines.get('SYS_ONACC')}")
check("روزکرد کامل ۳۰", s["daysPaid"] == 30, f"days={s['daysPaid']}")
check("خالص = ناخالص − کسور", s["netPay"] == s["grossEarnings"] - s["taxAmount"] - s["insuranceAmount"] - s["otherDeductions"],
      f"net={s['netPay']}")

section("ارجاع آیتم به آیتم")
st, ref, _ = call("POST", "/api/hr-pay/items", t_mgr,
                  {"code": "REFCHK", "title": "ارجاع", "formula": "BON*2", "sortOrder": 35})
call("POST", f"/api/hr-pay/runs/{rid}/calculate", t_mgr)
st, slips, _ = call("GET", f"/api/hr-pay/runs/{rid}/slips", t_mgr)
lines = {l["Code"]: l["Amount"] for l in _json.loads(slips[0]["detailsJson"])}
check("فرمول با ارجاع به آیتم دیگر", lines.get("REFCHK") == 2 * lines.get("BON", 0), f"ref={lines.get('REFCHK')}")
call("DELETE", f"/api/hr-pay/items/{ref['id']}", t_mgr)
call("POST", f"/api/hr-pay/runs/{rid}/calculate", t_mgr)
st, slips, _ = call("GET", f"/api/hr-pay/runs/{rid}/slips", t_mgr)

section("فیش من و دسترسی")
st, mine, _ = call("GET", "/api/hr-pay/slips/my", t_emp)
check("فیش من", st == 200 and len(mine) == 1 and mine[0]["netPay"] == slips[0]["netPay"], f"n={len(mine) if st==200 else '?'}")
st, det, _ = call("GET", f"/api/hr-pay/slips/{slips[0]['id']}", t_emp)
check("جزئیات فیش خود", st == 200 and det.get("runYear") == 1405, f"HTTP {st}")
st, _, _ = call("GET", f"/api/hr-pay/slips/{slips[0]['id']}", t_out)
check("غریبه فیش را نمی‌بیند", st == 403, f"HTTP {st}")

section("تأیید و قفل")
st, _, _ = call("POST", f"/api/hr-pay/runs/{rid}/status", t_mgr, {"status": "Approved"})
check("تأیید دوره", st == 200, f"HTTP {st}")
st, _, _ = call("POST", f"/api/hr-pay/runs/{rid}/status", t_mgr, {"status": "Locked"})
check("قفل دوره", st == 200, f"HTTP {st}")
st, loans, _ = call("GET", "/api/hr-pay/loans", t_mgr)
check("قسط اول هنگام قفل اعمال شد", loans[0]["paidCount"] == 1, f"paid={loans[0]['paidCount']}")
st, ars, _ = call("GET", "/api/hr-pay/arrears?year=1405&month=6", t_mgr)
st2, oas, _ = call("GET", "/api/hr-pay/onaccounts?year=1405&month=6", t_mgr)
check("معوقه/علی‌الحساب اعمال شدند", ars[0]["status"] == "Applied" and oas[0]["status"] == "Applied")
st, _, _ = call("POST", f"/api/hr-pay/runs/{rid}/calculate", t_mgr)
check("محاسبه مجدد دوره قفل رد می‌شود", st == 400, f"HTTP {st}")

section("مغایرت‌گیری")
st, run5, _ = call("POST", "/api/hr-pay/runs", t_mgr, {"year": 1405, "month": 5})
call("POST", f"/api/hr-pay/runs/{run5['id']}/calculate", t_mgr)
st, cmp, _ = call("GET", f"/api/hr-pay/runs/{rid}/compare", t_mgr)
check("مقایسه با ماه قبل", st == 200 and len(cmp) == 1 and cmp[0]["diff"] != 0,
      f"diff={cmp[0]['diff'] if st == 200 else '?'} flag={cmp[0]['flag'] if st == 200 else '?'}")

section("سند حسابداری")
st, tree, _ = call("GET", "/api/acc/accounts/tree", admin)


def leaves(nodes, out):
    for n in nodes if isinstance(nodes, list) else []:
        ch = n.get("children") or []
        if not ch and n.get("isPostable") and not n.get("requiresParty"):
            out.append(n.get("code"))
        leaves(ch, out)
    return out


codes = leaves(tree if isinstance(tree, list) else tree.get("items", []), [])
check("یافتن حساب‌های قابل ثبت", len(codes) >= 5, f"n={len(codes)}")
acc = {f"pay.acc.{k}": codes[i % len(codes)] for i, k in
       enumerate(["salaryExpense", "insuranceExpense", "payable", "taxPayable", "insurancePayable", "otherDed"])}
call("PUT", "/api/hr-pay/rules", t_mgr, acc)
st, v, _ = call("POST", f"/api/hr-pay/runs/{rid}/voucher", t_mgr)
check("صدور سند حقوق", st == 200 and v.get("voucherId"), f"HTTP {st} {v}")
st, vd, _ = call("GET", f"/api/acc/vouchers/{v['voucherId']}", admin)
dr = sum(l["debit"] for l in vd["lines"])
cr = sum(l["credit"] for l in vd["lines"])
check("سند متوازن است", st == 200 and dr == cr and dr > 0, f"dr={dr} cr={cr}")
st, _, _ = call("POST", f"/api/hr-pay/runs/{rid}/status", t_mgr, {"status": "Draft"})
check("بازکردن دوره سنددار رد می‌شود", st == 400, f"HTTP {st}")

section("خروجی‌ها")
st, txt, _ = call("GET", f"/api/hr-pay/exports/insurance?runId={rid}&format=txt", t_mgr)
check("فایل بیمه", st == 200 and "WS-77" in txt and "3333333333" in txt, f"HTTP {st}")
st, csv, _ = call("GET", f"/api/hr-pay/exports/tax?runId={rid}&format=csv", t_mgr)
check("فایل مالیات", st == 200 and "3333333333" in csv, f"HTTP {st}")
st, raw, _ = call("GET", f"/api/hr-pay/exports/bank?runId={rid}&format=xlsx", t_mgr, raw=True)
check("لیست بانکی Excel", st == 200 and raw[:2] == b"PK", f"HTTP {st}")

section("تسویه")
st, stl, _ = call("POST", "/api/hr-pay/settlements/calculate", t_mgr,
                  {"employeeId": eid, "leaveDate": days(0), "unusedLeaveDays": 5})
check("محاسبه تسویه", st == 200 and stl.get("leaveRefund") == 20000000, f"HTTP {st} {stl}")
check("سنوات حدود ۲ سال پایه", stl.get("severanceAmount", 0) > 200000000, f"sev={stl.get('severanceAmount')}")
st, _, _ = call("POST", f"/api/hr-pay/settlements/{stl['id']}/status", t_mgr, {"status": "Approved"})
st, _, _ = call("POST", f"/api/hr-pay/settlements/{stl['id']}/status", t_mgr, {"status": "Paid"})
st, stls, _ = call("GET", "/api/hr-pay/settlements", t_mgr)
check("گردش تسویه تا پرداخت", stls[0]["status"] == "Paid")

section("کارکرد ماه")
st, agg, _ = call("GET", f"/api/hr-pay/attendance-month?employeeId={eid}&year=1405&month=6", t_mgr)
check("تجمیع کارکرد", st == 200 and agg.get("daysPaid") == 30, f"HTTP {st} {agg}")

print(f"\nPASS={len(PASS)} FAIL={len(FAIL)}")
sys.exit(1 if FAIL else 0)
