#!/usr/bin/env python3
"""تست کامل API سامانه انبار و فروش — بررسی تمام نقاط پایانی و منطق کسب‌وکار"""
import json, sys, urllib.request, urllib.error, urllib.parse
import datetime as _dt
import time as _time
_UNIQ = str(int(_time.time() * 1000) % 100000000)
# زمان پایه داینامیک: همیشه جلوتر از همه اسناد قبلی — حتی در اجرای مجدد تست.
# فاصله گام‌ها میلی‌ثانیه‌ای است تا اجرای بعدی تست (که دیرتر شروع می‌شود) با اسناد این اجرا تداخل نکند.
_BASE = _dt.datetime.now() + _dt.timedelta(days=1)
def _T(step):
    return (_BASE + _dt.timedelta(milliseconds=step)).isoformat(timespec="milliseconds")
_D = _BASE.date().isoformat()
_D2 = (_BASE.date() + _dt.timedelta(days=2)).isoformat()


BASE = "http://localhost:5100"
PASS = 0
FAIL = 0
TOKEN = ""  # توکن JWT ادمین — بعد از ورود در ابتدای تست پر می‌شود (فاز ۱۰)

def req(method, path, body=None):
    url = BASE + path
    data = json.dumps(body).encode() if body is not None else None
    headers = {"Content-Type": "application/json"}
    if TOKEN:
        headers["Authorization"] = f"Bearer {TOKEN}"
    r = urllib.request.Request(url, data=data, method=method,
        headers=headers)
    try:
        with urllib.request.urlopen(r, timeout=30) as resp:
            txt = resp.read().decode()
            return resp.status, json.loads(txt) if txt else None
    except urllib.error.HTTPError as e:
        txt = e.read().decode()
        try: return e.code, json.loads(txt)
        except: return e.code, {"message": txt}
    except Exception as e:
        return -1, {"message": str(e)}

def check(name, cond, extra=""):
    global PASS, FAIL
    if cond:
        PASS += 1
        print(f"  ✅ {name}")
    else:
        FAIL += 1
        print(f"  ❌ {name} {extra}")

def approx(a, b, tol=1.0):
    return abs(float(a) - float(b)) <= tol

PROD_SEARCH = urllib.parse.quote("20W50")   # کد/نام یکتای روغن موتور

print("=" * 60)
print("1) سلامت سرویس و داده اولیه")
print("=" * 60)
s, h = req("GET", "/api/health")
check("health", s == 200 and h.get("status") == "ok", str(h))

# فاز ۱۰: همه‌ی APIهای عملیاتی محافظت‌شده‌اند — اول ورود ادمین
s, _login0 = req("POST", "/api/auth/login", {"username": "admin", "password": "admin"})
check("ورود ادمین برای اجرای تست‌ها", s == 200 and _login0 and _login0.get("token"), str(s))
TOKEN = (_login0 or {}).get("token", "")

# بررسی این‌که بدون توکن دسترسی بسته است
import urllib.request as _ur
try:
    _r = _ur.Request(BASE + "/api/products?pageSize=1", headers={"Content-Type": "application/json"})
    with _ur.urlopen(_r, timeout=10) as _resp:
        _code = _resp.status
except urllib.error.HTTPError as _e:
    _code = _e.code
check("API عملیاتی بدون توکن ممنوع (401)", _code == 401, str(_code))

s, p = req("GET", "/api/products?pageSize=100")
check("لیست کالاها", s == 200 and p["totalCount"] >= 14, f"total={p.get('totalCount')}")

s, w = req("GET", "/api/warehouses")
check("لیست انبارها", s == 200 and len(w) == 3, str(len(w)))

s, c = req("GET", "/api/parties?type=0")
s2, sup = req("GET", "/api/parties?type=1")
check("مشتریان و تأمین‌کنندگان", s == 200 and len(c) >= 4 and len(sup) >= 3, f"{len(c)}/{len(sup)}")

s, d = req("GET", "/api/dashboard")
check("داشبورد", s == 200 and d["productCount"] >= 14, str(d.get("productCount")))
check("ارزش موجودی > 0", d["inventoryValue"] > 0, str(d["inventoryValue"]))

print()
print("=" * 60)
print("2) CRUD کالا")
print("=" * 60)
new_prod = {"name": "کالای تستی", "unit": "عدد", "category": "تست",
    "salePrice": 123000, "purchasePrice": 98000, "reorderPoint": 5, "maxStock": 50, "isActive": True}
s, created = req("POST", "/api/products", new_prod)
check("ایجاد کالا", s == 200 and created["id"] > 0, str(s))
check("کد خودکار تولید شد", bool(created.get("code")), str(created.get("code")))
pid = created["id"]

s, got = req("GET", f"/api/products/{pid}")
check("خواندن کالا", s == 200 and got["name"] == "کالای تستی", str(s))

got["name"] = "کالای تستی ویرایش‌شده"
got["salePrice"] = 150000
s, upd = req("POST", "/api/products", got)
check("ویرایش کالا", s == 200 and upd["name"] == "کالای تستی ویرایش‌شده" and upd["salePrice"] == 150000, str(s))

s, _ = req("GET", "/api/products?search=" + urllib.parse.quote("تستی"))
check("جستجوی کالا", s == 200 and _["totalCount"] >= 1, str(_.get("totalCount")))

s, _ = req("DELETE", f"/api/products/{pid}")
check("حذف کالا (بدون سابقه)", s == 200, str(s))
s, _ = req("GET", f"/api/products/{pid}")
check("کالا پس از حذف ناپدید شد", _ is None or s != 200, str(s))

s, _ = req("POST", "/api/products", {**new_prod, "code": "P-0001"})
check("کد تکراری خطا می‌دهد", s == 400, str(s))

print()
print("=" * 60)
print("3) CRUD انبار و طرف حساب")
print("=" * 60)
s, wh = req("POST", "/api/warehouses", {"name": "انبار تست", "isActive": True})
check("ایجاد انبار", s == 200 and wh["id"] > 0, str(s))
whid = wh["id"]
s, _ = req("DELETE", f"/api/warehouses/{whid}")
check("حذف انبار (بدون سابقه)", s == 200, str(s))

s, pt = req("POST", "/api/parties", {"name": "مشتری تستی", "type": 0, "isActive": True})
check("ایجاد مشتری", s == 200 and pt["id"] > 0, str(s))
s, _ = req("DELETE", f"/api/parties/{pt['id']}")
check("حذف مشتری", s == 200, str(s))

print()
print("=" * 60)
print("4) اصلاح موجودی")
print("=" * 60)
s, st0 = req("GET", "/api/stock?search=" + PROD_SEARCH)
prod_id = st0["items"][0]["productId"]
wh_id = st0["items"][0]["warehouseId"]
qty_before = float(st0["items"][0]["quantity"])
s, _ = req("POST", "/api/stock/adjust", {"warehouseId": wh_id, "productId": prod_id, "quantity": 100, "date": _T(0), "description": "تست"})
s, st1 = req("GET", f"/api/stock?warehouseId={wh_id}&search=" + PROD_SEARCH)
check("اصلاح موجودی اعمال شد", s == 200 and approx(st1["items"][0]["quantity"], 100), str(st1["items"][0]["quantity"]))

print()
print("=" * 60)
print("5) خرید و فروش و کنترل موجودی")
print("=" * 60)
s, sup_list = req("GET", "/api/parties?type=1")
s, cus_list = req("GET", "/api/parties?type=0")
supplier_id = sup_list[0]["id"]
customer_id = cus_list[0]["id"]

# خرید 50 واحد
s, po = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": supplier_id, "type": 1,
    "date": _T(10), "description": "تست خرید",
    "lines": [{"productId": prod_id, "quantity": 50, "price": 90000}]})
check("ثبت فاکتور خرید", s == 200 and po["id"] > 0, str(s))
po_id = po["id"]
s, st2 = req("GET", f"/api/stock?warehouseId={wh_id}&search=" + PROD_SEARCH)
check("موجودی پس از خرید +۵۰", approx(st2["items"][0]["quantity"], 150), str(st2["items"][0]["quantity"]))

# فروش 20 واحد (تاریخ بعد از همه اسناد تا آخرین فروش شود)
s, so = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2,
    "date": _T(20), "description": "تست فروش",
    "lines": [{"productId": prod_id, "quantity": 20, "price": 150000}]})
check("ثبت فاکتور فروش", s == 200 and so["id"] > 0, str(s))
so_id = so["id"]
s, st3 = req("GET", f"/api/stock?warehouseId={wh_id}&search=" + PROD_SEARCH)
check("موجودی پس از فروش -۲۰", approx(st3["items"][0]["quantity"], 130), str(st3["items"][0]["quantity"]))

# فروش بیش از موجودی باید خطا بدهد
s, err = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2,
    "date": _T(30), "lines": [{"productId": prod_id, "quantity": 999999, "price": 150000}]})
check("فروش بیش از موجودی رد می‌شود", s == 400 and "موجودی" in err.get("message", ""), str(err))

# قیمت پیشنهادی فروش = آخرین فروش (سند 23:00 با قیمت 150000)
s, sug = req("GET", f"/api/orders/suggest-price?productId={prod_id}&type=2")
check("قیمت پیشنهادی فروش", s == 200 and float(sug) == 150000, str(sug))

print()
print("=" * 60)
print("6) کاردکس کالا")
print("=" * 60)
s, kx = req("GET", f"/api/kardex?productId={prod_id}&warehouseId={wh_id}")
check("کاردکس دارای سطر است", s == 200 and len(kx) > 0, str(len(kx)))
# بررسی سطر‌به‌سطر: مانده هر سطر باید با مجموع تجمعی ورودی/خروجی برابر باشد
running = 0
consistent = True
for row in kx:
    running += float(row["inQty"]) - float(row["outQty"])
    if abs(running - float(row["balance"])) > 0.01:
        consistent = False
check("مانده هر سطر کاردکس با مجموع تجمعی برابر است", consistent)
check("مانده نهایی = موجودی فعلی", approx(running, 130, 0.01), f"balance={running}")

s, kx2 = req("GET", f"/api/kardex?productId={prod_id}&warehouseId={wh_id}&from={_D}&to={_D2}")
check("کاردکس فیلتر تاریخ", s == 200 and len(kx2) >= 2, str(len(kx2)))

print()
print("=" * 60)
print("7) حذف سند و بازمحاسبه موجودی")
print("=" * 60)
s, _ = req("DELETE", f"/api/orders/{so_id}")
check("حذف فاکتور فروش", s == 200, str(s))
s, st4 = req("GET", f"/api/stock?warehouseId={wh_id}&search=" + PROD_SEARCH)
check("موجودی پس از حذف فروش بازگشت", approx(st4["items"][0]["quantity"], 150), str(st4["items"][0]["quantity"]))

s, _ = req("DELETE", f"/api/orders/{po_id}")
check("حذف فاکتور خرید", s == 200, str(s))
s, st5 = req("GET", f"/api/stock?warehouseId={wh_id}&search=" + PROD_SEARCH)
check("موجودی پس از حذف خرید بازگشت", approx(st5["items"][0]["quantity"], 100), str(st5["items"][0]["quantity"]))

print()
print("=" * 60)
print("8) گزارش نقطه سفارش")
print("=" * 60)
s, ro = req("GET", "/api/reorder")
check("گزارش نقطه سفارش", s == 200, str(s))
names = [r["productName"] for r in ro]
check("شامل اقلام زیر نقطه سفارش", any("لاستیک" in n or "دیسک" in n or "شمع" in n for n in names), str(names))
ok = all(r["totalStock"] <= r["reorderPoint"] for r in ro)
check("همه اقلام واقعاً زیر نقطه سفارش‌اند", ok)

print()
print("=" * 60)
print("9) فعالیت‌های اخیر و اعتبارسنجی‌ها")
print("=" * 60)
s, rec = req("GET", "/api/dashboard/recent?count=5")
check("فعالیت‌های اخیر", s == 200 and len(rec) > 0, str(len(rec)))

s, _ = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 1, "lines": []})
check("سند بدون سطر رد می‌شود", s == 400, str(s))

s, _ = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": supplier_id, "type": 1,
    "lines": [{"productId": prod_id, "quantity": -5, "price": 100}]})
check("مقدار منفی رد می‌شود", s == 400, str(s))

# بازگشت موجودی به حالت اولیه برای تمیزی
s, _ = req("POST", "/api/stock/adjust", {"warehouseId": wh_id, "productId": prod_id, "quantity": qty_before, "date": _T(40), "description": "بازگشت تست"})
check("بازگشت موجودی اولیه", s == 200, str(s))

print()
print("=" * 60)
print("10) احراز هویت و پنل معرف (فاز ۱۰)")
print("=" * 60)

def req_auth(method, path, token, body=None):
    url = BASE + path
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(url, data=data, method=method,
        headers={"Content-Type": "application/json", "Authorization": f"Bearer {token}"})
    try:
        with urllib.request.urlopen(r, timeout=30) as resp:
            txt = resp.read().decode()
            return resp.status, json.loads(txt) if txt else None
    except urllib.error.HTTPError as e:
        txt = e.read().decode()
        try: return e.code, json.loads(txt)
        except: return e.code, {"message": txt}
    except Exception as e:
        return -1, {"message": str(e)}

# ورود admin
s, login = req("POST", "/api/auth/login", {"username": "admin", "password": "admin"})
check("ورود admin/admin", s == 200 and login and login.get("token") and login.get("role") == "Admin", str(s))
admin_token = (login or {}).get("token", "")

# ورود با رمز غلط
s, _ = req("POST", "/api/auth/login", {"username": "admin", "password": "wrong"})
check("ورود با رمز غلط رد می‌شود (401)", s == 401, str(s))

# لیست کاربران بدون توکن ممنوع
try:
    _r = urllib.request.Request(BASE + "/api/users", headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(_r, timeout=10) as _resp:
        s = _resp.status
except urllib.error.HTTPError as e:
    s = e.code
check("لیست کاربران بدون توکن ممنوع (401)", s == 401, str(s))

# لیست کاربران با توکن ادمین
s, users = req_auth("GET", "/api/users", admin_token)
check("لیست کاربران با توکن ادمین", s == 200 and isinstance(users, list) and any(u["username"] == "admin" for u in users), str(s))

# ساخت معرف + کاربر معرف
s, ref10 = req("POST", "/api/referrers", {"id": 0, "name": "معرف فاز ده " + _UNIQ, "companyName": "پخش آزمون", "goodsCommissionPercent": 10, "serviceCommissionPercent": 5, "isActive": True})
ref10_id = (ref10 or {}).get("id", 0)
s, u10 = req_auth("POST", "/api/users", admin_token, {"id": 0, "username": "reftest" + _UNIQ, "password": "1234", "role": "Referrer", "referrerId": ref10_id, "isActive": True})
check("ساخت کاربر معرف", s == 200 and (u10 or {}).get("role") == "Referrer", str(s))

# ورود معرف
s, rlogin = req("POST", "/api/auth/login", {"username": "reftest" + _UNIQ, "password": "1234"})
check("ورود کاربر معرف", s == 200 and (rlogin or {}).get("referrerId") == ref10_id, str(s))
ref_token = (rlogin or {}).get("token", "")

# معرف به لیست کاربران دسترسی ندارد
s, _ = req_auth("GET", "/api/users", ref_token)
check("معرف به مدیریت کاربران دسترسی ندارد (403)", s == 403, str(s))

# مشتری با معرف → فروش → معرف خودکار از مشتری
s, cust10 = req("POST", "/api/parties", {"id": 0, "type": 0, "name": "مشتری فاز ده " + _UNIQ, "isActive": True, "referrerId": ref10_id})
cust10_id = (cust10 or {}).get("id", 0)
check("نام معرف در لیست مشتری", (cust10 or {}).get("referrerName") == "معرف فاز ده " + _UNIQ, str(cust10)[:80])

s, ord10 = req("POST", "/api/orders", {"id": 0, "warehouseId": wh_id, "partyId": cust10_id, "type": 2, "date": _T(50), "lines": [{"productId": prod_id, "quantity": 1, "price": 3000000}]})
check("فروش: معرف خودکار از مشتری", s == 200 and (ord10 or {}).get("referrerId") == ref10_id, str(s))
comm10 = (ord10 or {}).get("commissionAmount") or 0

# داشبورد معرف — فقط داده‌های خودش
s, dash = req_auth("GET", "/api/my/dashboard", ref_token)
check("داشبورد معرف", s == 200 and (dash or {}).get("referrerName") == "معرف فاز ده " + _UNIQ and dash.get("orderCount") == 1, str(s))
check("پورسانت داشبورد = پورسانت سند", dash is not None and approx(dash.get("totalCommission", -1), comm10), f"{(dash or {}).get('totalCommission')} vs {comm10}")

# کیف پول معرف (پرداخت‌ها)
s, pays = req_auth("GET", "/api/my/payments", ref_token)
check("کیف پول معرف (پرداخت‌ها)", s == 200 and isinstance(pays, list), str(s))

# داشبورد بدون توکن ممنوع (توکن ادمین هم نقش Referrer ندارد → 403)
try:
    _r = urllib.request.Request(BASE + "/api/my/dashboard", headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(_r, timeout=10) as _resp:
        s = _resp.status
except urllib.error.HTTPError as e:
    s = e.code
check("داشبورد معرف بدون توکن ممنوع (401)", s == 401, str(s))

# پورسانت هر سطر در جزئیات سند فروش
s, ord10full = req("GET", f"/api/orders/{(ord10 or {}).get('id', 0)}")
_lines_comm = sum((l.get("commission") or 0) for l in (ord10full or {}).get("lines", []))
check("پورسانت هر سطر در جزئیات سند", s == 200 and _lines_comm > 0 and approx(_lines_comm, (ord10full or {}).get("commissionAmount") or -1), f"lines={_lines_comm}")

# حذف کاربر تستی + پاکسازی
s, _ = req_auth("DELETE", f"/api/users/{(u10 or {}).get('id', 0)}", admin_token)
check("حذف کاربر معرف", s in (200, 204), str(s))
req("DELETE", f"/api/orders/{(ord10 or {}).get('id', 0)}")
s, _ = req("POST", "/api/stock/adjust", {"warehouseId": wh_id, "productId": prod_id, "quantity": qty_before, "date": _T(60), "description": "بازگشت تست فاز ده"})

# ============================================================
print()
print("=" * 60)
print("11) فاز ۱۱ — بانک معرف، انبار کالا، تغییر رمز")
print("=" * 60)

# شماره کارت و شبا معرف
s, refb = req("POST", "/api/referrers", {"id": 0, "name": "معرف بانک‌دار " + _UNIQ, "goodsCommissionPercent": 5, "serviceCommissionPercent": 5,
                                          "cardNumber": "6037991512345678", "iban": "IR140570028870010133089001", "isActive": True})
check("ثبت کارت و شبا معرف", s == 200 and (refb or {}).get("cardNumber") == "6037991512345678" and (refb or {}).get("iban") == "140570028870010133089001", str(refb)[:120])

s, refs_all = req("GET", "/api/referrers")
_rb = next((r for r in (refs_all or []) if r["id"] == (refb or {}).get("id")), None)
check("کارت و شبا در لیست معرف‌ها", _rb is not None and _rb.get("cardNumber") == "6037991512345678", str(_rb)[:100])

s, _ = req("POST", "/api/referrers", {"id": 0, "name": "معرف کارت بد " + _UNIQ, "goodsCommissionPercent": 1, "serviceCommissionPercent": 1, "cardNumber": "1234", "isActive": True})
check("کارت نامعتبر رد می‌شود", s == 400, str(s))

s, _ = req("POST", "/api/referrers", {"id": 0, "name": "معرف شبا بد " + _UNIQ, "goodsCommissionPercent": 1, "serviceCommissionPercent": 1, "iban": "IR12", "isActive": True})
check("شبا نامعتبر رد می‌شود", s == 400, str(s))

# نام انبار در لیست کالاها
s, prods = req("GET", "/api/products?pageSize=5")
check("نام انبار در لیست کالاها", s == 200 and any(p.get("warehouseName") for p in (prods or {}).get("items", [])), str(s))

# تغییر رمز توسط خود کاربر
s, _ = req("POST", "/api/auth/change-password", {"currentPassword": "wrong", "newPassword": "9999"})
check("تغییر رمز با رمز فعلی غلط رد می‌شود", s == 400, str(s))

s, _ = req("POST", "/api/auth/change-password", {"currentPassword": "admin", "newPassword": "temp1234"})
check("تغییر رمز موفق", s == 200, str(s))

s, _ = req("POST", "/api/auth/login", {"username": "admin", "password": "temp1234"})
check("ورود با رمز جدید", s == 200, str(s))

s, _relog = req("POST", "/api/auth/login", {"username": "admin", "password": "temp1234"})
TOKEN = (_relog or {}).get("token", TOKEN)
s, _ = req("POST", "/api/auth/change-password", {"currentPassword": "temp1234", "newPassword": "admin"})
check("بازگرداندن رمز admin", s == 200, str(s))

# ============================================================
print()
print("=" * 60)
print("12) تعمیرات — پذیرش، تعمیرکار، فاکتور یکپارچه")
print("=" * 60)

# تعمیرکار
s, tech = req("POST", "/api/technicians", {"id": 0, "name": "تعمیرکار تست " + _UNIQ, "phone": "09120000000", "specialty": "لپ‌تاپ", "isActive": True})
check("ثبت تعمیرکار", s == 200 and (tech or {}).get("id", 0) > 0, str(s))
tech_id = (tech or {}).get("id", 0)

s, techs = req("GET", "/api/technicians")
check("فهرست تعمیرکارها", s == 200 and any(t["id"] == tech_id for t in (techs or [])), str(s))

# موجودی قبل
s, pr = req("GET", f"/api/products?search={PROD_SEARCH}")
stock_before = (pr or {}).get("items", [{}])[0].get("totalStock", 0)

# پذیرش با ۲ ردیف: قطعه از انبار + اجرت
s, rep = req("POST", "/api/repairs", {
    "id": 0, "partyId": customer_id, "technicianId": tech_id,
    "deviceType": "لپ‌تاپ", "deviceModel": "Asus Test", "serialNumber": "SN-" + _UNIQ,
    "problemDescription": "روشن نمی‌شود", "quotedPrice": 9000000,
    "items": [
        {"description": "تعویض قطعه", "productId": prod_id, "quantity": 1, "cost": 2500000, "price": 3500000},
        {"description": "اجرت تعمیر برد", "productId": None, "quantity": 1, "cost": 500000, "price": 4000000},
    ]})
check("ثبت پذیرش تعمیر", s == 200 and (rep or {}).get("number", "").startswith("RP-"), str(rep)[:100])
rep_id = (rep or {}).get("id", 0)
check("جمع و سود پذیرش", rep is not None and approx(rep.get("totalPrice", 0), 7500000) and approx(rep.get("profit", 0), 4500000),
      f"price={(rep or {}).get('totalPrice')} profit={(rep or {}).get('profit')}")

# تغییر وضعیت
s, rep2 = req("POST", f"/api/repairs/{rep_id}/status/InProgress")
check("تغییر وضعیت به در حال تعمیر", s == 200 and (rep2 or {}).get("status") == 1, str(s))

# تحویل مستقیم ممنوع — فقط از طریق فاکتور
s, _ = req("POST", f"/api/repairs/{rep_id}/status/Delivered")
check("تحویل مستقیم ممنوع (فقط با فاکتور)", s == 400, str(s))

# صدور فاکتور
s, rep3 = req("POST", f"/api/repairs/{rep_id}/invoice", {"warehouseId": wh_id})
check("صدور فاکتور تعمیر", s == 200 and (rep3 or {}).get("invoiceNumber") and (rep3 or {}).get("status") == 3, str(rep3)[:100])
check("تاریخ خروج ثبت شد", (rep3 or {}).get("deliveredAt") is not None, "")

# کسر موجودی قطعه از انبار
s, pr2 = req("GET", f"/api/products?search={PROD_SEARCH}")
stock_after = (pr2 or {}).get("items", [{}])[0].get("totalStock", 0)
check("کسر قطعه از موجودی انبار", approx(stock_before - stock_after, 1), f"{stock_before} → {stock_after}")

# سند فروش یکپارچه (مبلغ = جمع دریافتی پذیرش)
inv_id = (rep3 or {}).get("invoiceTransactionId", 0)
s, inv = req("GET", f"/api/orders/{inv_id}")
check("سند فروش یکپارچه صادر شد", s == 200 and approx((inv or {}).get("totalAmount", 0), 7500000), str(s))

# فاکتور تکراری ممنوع
s, _ = req("POST", f"/api/repairs/{rep_id}/invoice", {"warehouseId": wh_id})
check("فاکتور تکراری ممنوع", s == 400, str(s))

# حذف پذیرش فاکتورشده ممنوع
s, _ = req("DELETE", f"/api/repairs/{rep_id}")
check("حذف پذیرش فاکتورشده ممنوع", s == 400, str(s))

# حذف تعمیرکار دارای سابقه ممنوع
s, _ = req("DELETE", f"/api/technicians/{tech_id}")
check("حذف تعمیرکار دارای سابقه ممنوع", s == 400, str(s))

# پاکسازی: حذف فاکتور فروش و برگرداندن موجودی
req("DELETE", f"/api/orders/{inv_id}")
s, _ = req("POST", "/api/stock/adjust", {"warehouseId": wh_id, "productId": prod_id, "quantity": qty_before, "date": _T(70), "description": "بازگشت تست تعمیرات"})

# ============================================================
print()
print("=" * 60)
print("13) روش‌های پرداخت + داشبورد ادمین + نقش اپراتور")
print("=" * 60)

# فروش نقدی POS
s, cash_o = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(80),
    "paymentMethod": 0, "cashType": 1, "lines": [{"productId": prod_id, "quantity": 1, "price": 3000000}]})
check("فروش نقدی (کارت‌خوان)", s == 200 and (cash_o or {}).get("paymentMethod") == 0 and (cash_o or {}).get("cashType") == 1, str(s))

# نسیه بدون سررسید رد می‌شود
s, _ = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(81),
    "paymentMethod": 1, "lines": [{"productId": prod_id, "quantity": 1, "price": 3000000}]})
check("نسیه بدون سررسید رد می‌شود", s == 400, str(s))

# نسیه با سررسید گذشته
import datetime as _dt2
_past = (_dt2.datetime.now() - _dt2.timedelta(days=3)).isoformat()
s, credit_o = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(82),
    "paymentMethod": 1, "dueDate": _past, "lines": [{"productId": prod_id, "quantity": 1, "price": 5000000}]})
check("فروش نسیه با سررسید", s == 200 and (credit_o or {}).get("dueDate"), str(s))

# چکی — جمع باید برابر فاکتور باشد
_today_s = _dt2.datetime.now().strftime("%Y-%m-%dT00:00:00")
s, chq_o = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(83),
    "paymentMethod": 2,
    "cheques": [{"number": "CH-" + _UNIQ, "bankName": "ملت", "amount": 6000000, "dueDate": _today_s}],
    "lines": [{"productId": prod_id, "quantity": 2, "price": 3000000}]})
check("فروش چکی", s == 200 and len((chq_o or {}).get("cheques", [])) == 1, str(s))

s, _ = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(84),
    "paymentMethod": 2, "cheques": [{"number": "X1", "amount": 100, "dueDate": _today_s}],
    "lines": [{"productId": prod_id, "quantity": 1, "price": 3000000}]})
check("چک با جمع نابرابر رد می‌شود", s == 400, str(s))

# اقساطی
s, inst_o = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(85),
    "paymentMethod": 3,
    "installments": [
        {"no": 1, "amount": 2000000, "dueDate": _past},
        {"no": 2, "amount": 2000000, "dueDate": "2030-01-01T00:00:00"},
        {"no": 3, "amount": 2000000, "dueDate": "2030-02-01T00:00:00"}],
    "lines": [{"productId": prod_id, "quantity": 2, "price": 3000000}]})
check("فروش اقساطی (دفترچه ۳ قسط)", s == 200 and len((inst_o or {}).get("installments", [])) == 3, str(s))

# داشبورد ادمین
s, adash = req("GET", "/api/dashboard/admin")
check("داشبورد ادمین: فروش دوره‌ای", s == 200 and (adash or {}).get("salesToday", 0) > 0 and adash.get("salesQuarter", 0) >= adash.get("salesMonth", 0), str(s))
check("داشبورد ادمین: سود دوره‌ای", adash is not None and adash.get("profitQuarter", 0) >= adash.get("profitToday", 0), "")
_debtors = (adash or {}).get("overdueDebtors", [])
check("بدهکاران سررسیدشده (نسیه + قسط)", any(d["kind"] == "نسیه" for d in _debtors) and any("قسط" in d["kind"] for d in _debtors), str(len(_debtors)))
_tch = (adash or {}).get("todayCheques", [])
check("چک‌های امروز", any(c["number"] == "CH-" + _UNIQ for c in _tch), str(len(_tch)))

# پاس کردن چک
_chid = next((c["chequeId"] for c in _tch if c["number"] == "CH-" + _UNIQ), 0)
s, _ = req("POST", f"/api/dashboard/cheques/{_chid}/clear")
check("پاس کردن چک", s == 200, str(s))
s, adash2 = req("GET", "/api/dashboard/admin")
check("چک پاس‌شده از لیست حذف شد", not any(c["number"] == "CH-" + _UNIQ for c in (adash2 or {}).get("todayCheques", [])), "")

# پرداخت قسط
_iid = (inst_o or {}).get("installments", [{}])[0].get("id", 0)
s, _ = req("POST", f"/api/dashboard/installments/{_iid}/pay")
check("ثبت پرداخت قسط", s == 200, str(s))

# نقش اپراتور
s, op_u = req_auth("POST", "/api/users", admin_token, {"id": 0, "username": "optest" + _UNIQ, "password": "1234", "role": "Operator", "isActive": True})
check("ساخت کاربر اپراتور", s == 200 and (op_u or {}).get("role") == "Operator", str(s))
s, op_login = req("POST", "/api/auth/login", {"username": "optest" + _UNIQ, "password": "1234"})
op_token = (op_login or {}).get("token", "")
s, _ = req_auth("GET", "/api/products?pageSize=1", op_token)
check("اپراتور به عملیات دسترسی دارد", s == 200, str(s))
s, _ = req_auth("GET", "/api/dashboard/admin", op_token)
check("اپراتور به داشبورد ادمین دسترسی ندارد (403)", s == 403, str(s))
s, _ = req_auth("GET", "/api/users", op_token)
check("اپراتور به مدیریت کاربران دسترسی دارد (فاز ۱۹)", s == 200, str(s))

# پاکسازی: حذف اسناد پرداختی این بخش + کاربر اپراتور
for _o in (cash_o, credit_o, chq_o, inst_o):
    if _o: req("DELETE", f"/api/orders/{_o['id']}")
req_auth("DELETE", f"/api/users/{(op_u or {}).get('id', 0)}", admin_token)
s, _ = req("POST", "/api/stock/adjust", {"warehouseId": wh_id, "productId": prod_id, "quantity": qty_before, "date": _T(99), "description": "بازگشت تست پرداخت"})

# ============================================================
print()
print("=" * 60)
print("14) پرداخت ترکیبی (نقد + چک/نسیه/اقساط)")
print("=" * 60)

# چکی ترکیبی: 6م = 2م نقد + 4م چک
s, mx1 = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(100),
    "paymentMethod": 2, "cashAmount": 2000000, "cashType": 1,
    "cheques": [{"number": "MX-" + _UNIQ, "bankName": "ملت", "amount": 4000000, "dueDate": "2030-01-01T00:00:00"}],
    "lines": [{"productId": prod_id, "quantity": 2, "price": 3000000}]})
check("چکی ترکیبی (2م نقد + 4م چک)", s == 200 and (mx1 or {}).get("cashAmount") == 2000000 and (mx1 or {}).get("settledAmount") == 2000000, str(s))

# چک ترکیبی با جمع کمتر از باقیمانده (2م نقد → باقیمانده 4م ولی چک فقط 1م)
s, _mxbad = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(101),
    "paymentMethod": 2, "cashAmount": 2000000,
    "cheques": [{"number": "MXBAD", "amount": 1000000, "dueDate": "2030-01-01T00:00:00"}],
    "lines": [{"productId": prod_id, "quantity": 2, "price": 3000000}]})
check("چک ترکیبی با جمع کمتر از باقیمانده رد می‌شود", s == 400, str(s))

# نقد >= فاکتور رد می‌شود
s, _ = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(102),
    "paymentMethod": 1, "cashAmount": 9000000, "dueDate": "2030-01-01T00:00:00",
    "lines": [{"productId": prod_id, "quantity": 2, "price": 3000000}]})
check("پیش‌دریافت >= فاکتور رد می‌شود", s == 400, str(s))

# نسیه ترکیبی با سررسید گذشته: بدهکار = فاکتور - نقد
s, mx2 = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(103),
    "paymentMethod": 1, "cashAmount": 2500000, "cashType": 0, "dueDate": _past,
    "lines": [{"productId": prod_id, "quantity": 2, "price": 3000000}]})
check("نسیه ترکیبی (2.5م نقد)", s == 200 and (mx2 or {}).get("settledAmount") == 2500000, str(s))
s, adash3 = req("GET", "/api/dashboard/admin")
_mydebt = next((b for b in (adash3 or {}).get("overdueDebtors", []) if b["transactionId"] == (mx2 or {}).get("id")), None)
check("بدهکار نسیه ترکیبی = باقیمانده (3.5م)", _mydebt is not None and approx(_mydebt["amount"], 3500000), str(_mydebt))

# اقساط ترکیبی: 3م نقد + 2 قسط 1.5م
s, mx3 = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(104),
    "paymentMethod": 3, "cashAmount": 3000000, "cashType": 2,
    "installments": [{"no": 1, "amount": 1500000, "dueDate": "2030-01-01T00:00:00"},
                     {"no": 2, "amount": 1500000, "dueDate": "2030-02-01T00:00:00"}],
    "lines": [{"productId": prod_id, "quantity": 2, "price": 3000000}]})
check("اقساط ترکیبی (3م نقد + 2 قسط)", s == 200 and len((mx3 or {}).get("installments", [])) == 2 and (mx3 or {}).get("cashAmount") == 3000000, str(s))

# پاکسازی
for _o in (mx1, mx2, mx3):
    if _o: req("DELETE", f"/api/orders/{_o['id']}")
s, _ = req("POST", "/api/stock/adjust", {"warehouseId": wh_id, "productId": prod_id, "quantity": qty_before, "date": _T(120), "description": "بازگشت تست ترکیبی"})

# ============================================================
print()
print("=" * 60)
print("15) اقساط/چک با سود (جمع بیشتر از فاکتور مجاز)")
print("=" * 60)

# اقساط با سود: 6م فاکتور + 540هزار سود → 3 قسط 2.18م
s, pr1 = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(130),
    "paymentMethod": 3,
    "installments": [{"no": 1, "amount": 2180000, "dueDate": "2030-01-01T00:00:00"},
                     {"no": 2, "amount": 2180000, "dueDate": "2030-02-01T00:00:00"},
                     {"no": 3, "amount": 2180000, "dueDate": "2030-03-01T00:00:00"}],
    "lines": [{"productId": prod_id, "quantity": 2, "price": 3000000}]})
check("اقساط با سود (6.54م > 6م فاکتور) قبول می‌شود", s == 200 and sum(i["amount"] for i in (pr1 or {}).get("installments", [])) == 6540000, str(s))

# اقساط کمتر از فاکتور رد می‌شود
s, _ = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(131),
    "paymentMethod": 3, "installments": [{"no": 1, "amount": 1000000, "dueDate": "2030-01-01T00:00:00"}],
    "lines": [{"productId": prod_id, "quantity": 2, "price": 3000000}]})
check("اقساط کمتر از فاکتور رد می‌شود", s == 400, str(s))

# چک با سود
s, pr2 = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(132),
    "paymentMethod": 2,
    "cheques": [{"number": "PRF-" + _UNIQ, "amount": 6300000, "dueDate": "2030-01-01T00:00:00"}],
    "lines": [{"productId": prod_id, "quantity": 2, "price": 3000000}]})
check("چک با سود (6.3م > 6م) قبول می‌شود", s == 200, str(s))

# ترکیبی با سود: 2م نقد + چک 4.2م
s, pr3 = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": customer_id, "type": 2, "date": _T(133),
    "paymentMethod": 2, "cashAmount": 2000000, "cashType": 1,
    "cheques": [{"number": "PRFM-" + _UNIQ, "amount": 4200000, "dueDate": "2030-01-01T00:00:00"}],
    "lines": [{"productId": prod_id, "quantity": 2, "price": 3000000}]})
check("ترکیبی با سود (نقد + چک بیشتر از باقیمانده)", s == 200 and (pr3 or {}).get("cashAmount") == 2000000, str(s))

# پاکسازی
for _o in (pr1, pr2, pr3):
    if _o: req("DELETE", f"/api/orders/{_o['id']}")
s, _ = req("POST", "/api/stock/adjust", {"warehouseId": wh_id, "productId": prod_id, "quantity": qty_before, "date": _T(140), "description": "بازگشت تست سود"})

# ============================================================
print()
print("=" * 60)
print("16) نمودار داشبورد + دسترسی کالا برای معرف")
print("=" * 60)

# نمودارهای داشبورد ادمین
s, adash4 = req("GET", "/api/dashboard/admin")
check("روند ۳۰ روزه داشبورد", s == 200 and len((adash4 or {}).get("dailyTrend", [])) == 30, str(len((adash4 or {}).get("dailyTrend", []))))
check("روند ۶ ماهه داشبورد", len((adash4 or {}).get("monthlyTrend", [])) == 6, str(len((adash4 or {}).get("monthlyTrend", []))))
check("تفکیک روش پرداخت", "payCash" in (adash4 or {}), "")

# معرف با دسترسی کالا
s, refv = req("POST", "/api/referrers", {"id": 0, "name": "معرف کالابین " + _UNIQ, "phone": "09121112233",
    "goodsCommissionPercent": 5, "serviceCommissionPercent": 5, "canViewProducts": True, "isActive": True})
check("ساخت معرف با دسترسی کالا", s == 200 and (refv or {}).get("canViewProducts") is True, str(s))
refv_id = (refv or {}).get("id", 0)
s, uv = req_auth("POST", "/api/users", admin_token, {"id": 0, "username": "refview" + _UNIQ, "password": "1234", "role": "Referrer", "referrerId": refv_id, "isActive": True})
s, lv = req("POST", "/api/auth/login", {"username": "refview" + _UNIQ, "password": "1234"})
rv_token = (lv or {}).get("token", "")

s, prods_r = req_auth("GET", "/api/my/products", rv_token)
check("معرف با دسترسی، کالاهای موجود را می‌بیند", s == 200 and isinstance(prods_r, list) and len(prods_r) > 0, str(s))
check("نمای محدود (بدون قیمت خرید/موجودی)", prods_r and "purchasePrice" not in prods_r[0] and "totalStock" not in prods_r[0], str(list((prods_r or [{}])[0].keys())))

# داشبورد معرف شامل فلگ
s, rdash = req_auth("GET", "/api/my/dashboard", rv_token)
check("فلگ دسترسی در داشبورد معرف", s == 200 and (rdash or {}).get("canViewProducts") is True and (rdash or {}).get("phone") == "09121112233", str(s))

# قطع دسترسی → 400
refv["canViewProducts"] = False
s, _ = req("POST", "/api/referrers", refv)
s, _ = req_auth("GET", "/api/my/products", rv_token)
check("بدون دسترسی، کالاها ممنوع (400)", s == 400, str(s))

# پاکسازی
req_auth("DELETE", f"/api/users/{(uv or {}).get('id', 0)}", admin_token)

# ============================================================
print()
print("=" * 60)
print("17) هزینه‌ها — دسته‌های قابل مدیریت + اسناد")
print("=" * 60)

s, ecats = req("GET", "/api/expense-categories")
check("دسته‌های هزینه پیش‌فرض", s == 200 and len(ecats or []) >= 9, str(len(ecats or [])))
_cat1 = (ecats or [{}])[0].get("id", 0)

s, exp1 = req("POST", "/api/expenses", {"id": 0, "categoryId": _cat1, "amount": 50000000, "date": _T(150), "payType": 2, "payee": "موجر", "description": "اجاره تست"})
check("ثبت سند هزینه", s == 200 and (exp1 or {}).get("number", "").startswith("EX-"), str(s))

s, _ = req("POST", "/api/expenses", {"id": 0, "categoryId": _cat1, "amount": 0, "date": _T(151)})
check("هزینه با مبلغ صفر رد می‌شود", s == 400, str(s))

s, newcat = req("POST", "/api/expense-categories", {"id": 0, "name": "دسته تست " + _UNIQ, "isActive": True})
check("ساخت دسته هزینه جدید", s == 200 and (newcat or {}).get("id", 0) > 0, str(s))

s, _ = req("DELETE", f"/api/expense-categories/{_cat1}")
check("حذف دسته دارای سند ممنوع", s == 400, str(s))

s, elist = req("GET", f"/api/expenses?categoryId={_cat1}")
check("فیلتر اسناد هزینه بر اساس دسته", s == 200 and (elist or {}).get("totalCount", 0) >= 1, str(s))

# ویرایش سند
exp1["amount"] = 60000000
s, exp2 = req("POST", "/api/expenses", exp1)
check("ویرایش سند هزینه", s == 200 and (exp2 or {}).get("amount") == 60000000, str(s))

# پاکسازی
req("DELETE", f"/api/expenses/{(exp1 or {}).get('id', 0)}")
req("DELETE", f"/api/expense-categories/{(newcat or {}).get('id', 0)}")

# ============================================================
print()
print("=" * 60)
print("18) پرداخت اعتباری در خرید + حذف قیمت از کالاهای معرف")
print("=" * 60)

# کالاهای معرف بدون قیمت
s, refp = req("POST", "/api/referrers", {"id": 0, "name": "معرف بی‌قیمت " + _UNIQ, "goodsCommissionPercent": 5, "serviceCommissionPercent": 5, "canViewProducts": True, "isActive": True})
s, up = req_auth("POST", "/api/users", admin_token, {"id": 0, "username": "refnp" + _UNIQ, "password": "1234", "role": "Referrer", "referrerId": (refp or {}).get("id", 0), "isActive": True})
s, lp = req("POST", "/api/auth/login", {"username": "refnp" + _UNIQ, "password": "1234"})
s, prods_np = req_auth("GET", "/api/my/products", (lp or {}).get("token", ""))
check("کالاهای معرف بدون قیمت فروش", s == 200 and prods_np and "salePrice" not in prods_np[0], str(list((prods_np or [{}])[0].keys())))
req_auth("DELETE", f"/api/users/{(up or {}).get('id', 0)}", admin_token)

# خرید نسیه
s, buy1 = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": supplier_id, "type": 1, "date": _T(160),
    "paymentMethod": 1, "dueDate": "2030-01-01T00:00:00",
    "lines": [{"productId": prod_id, "quantity": 5, "price": 2000000}]})
check("خرید نسیه با سررسید", s == 200 and (buy1 or {}).get("paymentMethod") == 1, str(s))

# خرید نسیه بدون سررسید رد
s, _ = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": supplier_id, "type": 1, "date": _T(161),
    "paymentMethod": 1, "lines": [{"productId": prod_id, "quantity": 1, "price": 2000000}]})
check("خرید نسیه بدون سررسید رد می‌شود", s == 400, str(s))

# خرید چکی
s, buy2 = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": supplier_id, "type": 1, "date": _T(162),
    "paymentMethod": 2,
    "cheques": [{"number": "PCH-" + _UNIQ, "bankName": "صادرات", "amount": 10000000, "dueDate": "2030-01-01T00:00:00"}],
    "lines": [{"productId": prod_id, "quantity": 5, "price": 2000000}]})
check("خرید چکی", s == 200 and len((buy2 or {}).get("cheques", [])) == 1, str(s))

# خرید اقساطی ترکیبی
s, buy3 = req("POST", "/api/orders", {"warehouseId": wh_id, "partyId": supplier_id, "type": 1, "date": _T(163),
    "paymentMethod": 3, "cashAmount": 4000000, "cashType": 1,
    "installments": [{"no": 1, "amount": 3000000, "dueDate": "2030-01-01T00:00:00"},
                     {"no": 2, "amount": 3000000, "dueDate": "2030-02-01T00:00:00"}],
    "lines": [{"productId": prod_id, "quantity": 5, "price": 2000000}]})
check("خرید اقساطی ترکیبی (نقد + قسط)", s == 200 and (buy3 or {}).get("cashAmount") == 4000000 and len((buy3 or {}).get("installments", [])) == 2, str(s))

# پاکسازی
for _o in (buy1, buy2, buy3):
    if _o: req("DELETE", f"/api/orders/{_o['id']}")
s, _ = req("POST", "/api/stock/adjust", {"warehouseId": wh_id, "productId": prod_id, "quantity": qty_before, "date": _T(170), "description": "بازگشت تست خرید اعتباری"})

print()
print("19) محدودیت اپراتور در مدیریت کاربران")
# ادمین یک اپراتور می‌سازد و با آن لاگین می‌کنیم
s, op19 = req_auth("POST", "/api/users", admin_token, {"id": 0, "username": "op19" + _UNIQ, "password": "1234", "role": "Operator", "isActive": True})
check("ساخت کاربر اپراتور برای تست ۱۹", s == 200, str(s))
s, login19 = req("POST", "/api/auth/login", {"username": "op19" + _UNIQ, "password": "1234"})
op19_token = (login19 or {}).get("token", "")
check("لاگین اپراتور تست ۱۹", s == 200 and op19_token, str(s))

# اپراتور کاربر عادی می‌سازد → OK
s, mk19 = req_auth("POST", "/api/users", op19_token, {"id": 0, "username": "op19made" + _UNIQ, "password": "1234", "role": "Operator", "isActive": True})
check("اپراتور کاربر عادی می‌سازد", s == 200 and (mk19 or {}).get("role") == "Operator", str(s))

# اپراتور کاربر Admin می‌سازد → 400
s, _ = req_auth("POST", "/api/users", op19_token, {"id": 0, "username": "op19adm" + _UNIQ, "password": "1234", "role": "Admin", "isActive": True})
check("اپراتور نمی‌تواند کاربر مدیر بسازد", s == 400, str(s))

# اپراتور نقش کاربر عادی را به Admin تغییر می‌دهد → 400
s, _ = req_auth("POST", "/api/users", op19_token, {"id": (mk19 or {}).get("id", 0), "username": "op19made" + _UNIQ, "role": "Admin", "isActive": True})
check("اپراتور نمی‌تواند نقش را به مدیر تغییر دهد", s == 400, str(s))

# اپراتور کاربر ادمین موجود (id=1) را ویرایش/حذف می‌کند → 400
s, _ = req_auth("POST", "/api/users", op19_token, {"id": 1, "username": "admin", "role": "Admin", "isActive": False})
check("اپراتور نمی‌تواند کاربر مدیر را ویرایش کند", s == 400, str(s))
s, _ = req_auth("DELETE", "/api/users/1", op19_token)
check("اپراتور نمی‌تواند کاربر مدیر را حذف کند", s == 400, str(s))

# اپراتور فهرست کاربران را می‌بیند → OK
s, ulist19 = req_auth("GET", "/api/users", op19_token)
check("اپراتور فهرست کاربران را می‌بیند", s == 200 and isinstance(ulist19, list), str(s))

# ادمین همچنان می‌تواند ادمین بسازد → OK
s, adm19 = req_auth("POST", "/api/users", admin_token, {"id": 0, "username": "adm19" + _UNIQ, "password": "1234", "role": "Admin", "isActive": True})
check("ادمین همچنان کاربر مدیر می‌سازد", s == 200 and (adm19 or {}).get("role") == "Admin", str(s))

# پاکسازی
for _u in (mk19, op19, adm19):
    if _u: req_auth("DELETE", f"/api/users/{_u.get('id', 0)}", admin_token)

print()
print("=" * 60)
print(f"نتیجه نهایی: {PASS} موفق / {FAIL} ناموفق")
print("=" * 60)
sys.exit(1 if FAIL else 0)
