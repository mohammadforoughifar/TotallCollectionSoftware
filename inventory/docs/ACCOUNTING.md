# ماژول حسابداری (Accounting)

این ماژول در همان راه‌حل `Inventory.sln` و روی همان `AppDbContext` (SQL Server) ساخته شده و
با ماژول انبارداری یکپارچه است: با قطعی شدن هر رسید/حواله، سند حسابداری متناظر به‌صورت خودکار صادر می‌شود.

---

## ۱) ساختار کدینگ حساب‌ها

کدینگ چهارسطحی استاندارد ایران:

| سطح | عنوان | نمونه کد | توضیح |
|---|---|---|---|
| ۰ | گروه | `1` | دارایی‌های جاری |
| ۱ | کل | `11` | موجودی نقد و بانک |
| ۲ | معین | `1101` | صندوق |
| ۳ | تفصیلی | `110101` | صندوق دفتر مرکزی |

قواعد اجباری که در سرویس اعمال می‌شود:

- کد هر زیرحساب باید با **کد والد** شروع شود.
- نوع حساب (دارایی/بدهی/…) و «دائم بودن» از **والد به ارث** می‌رسد.
- سطح حساب از روی سطح والد **به‌طور خودکار** محاسبه می‌شود.
- تنها روی حساب‌های **قابل ثبت (`IsPostable`) و فعال** می‌توان سند زد.
  به‌محض اینکه یک حساب صاحب فرزند شود، خودش از حالت «قابل ثبت» خارج می‌شود.
- حساب‌های **سیستمی**، حساب‌های دارای فرزند، حساب‌های به‌کاررفته در آرتیکل‌ها و حساب‌های
  استفاده‌شده در قواعد سند خودکار **قابل حذف نیستند**.
- ایجاد حلقه در درخت (انتخاب یکی از فرزندان به‌عنوان والد) مسدود است.

کدینگ پیش‌فرض (۴۹ حساب در ۶ گروه: دارایی جاری، دارایی ثابت، بدهی، سرمایه، درآمد، بهای تمام‌شده و هزینه)
در اولین اجرا توسط `DbInitializer.SeedAccounting` ساخته می‌شود.

---

## ۲) سال مالی

- هر سند به یک **سال مالی** تعلق دارد و شماره اسناد در هر دوره **از ۱ شروع** می‌شود.
- تنها یک دوره می‌تواند **جاری** باشد؛ اسناد جدید در همان دوره ثبت می‌شوند.
- دوره‌ی **بسته‌شده** اجازه‌ی ثبت یا قطعی‌سازی سند نمی‌دهد.
- سال مالی جاری (بر اساس سال شمسی روز نصب) هنگام راه‌اندازی ساخته می‌شود.

---

## ۳) سند حسابداری

وضعیت‌ها: **پیش‌نویس ← قطعی ← ابطال‌شده**

قواعد قطعی‌سازی:

1. حداقل **دو آرتیکل** داشته باشد.
2. جمع **بدهکار = بستانکار** و مخالف صفر باشد.
3. سال مالی سند **باز** باشد.
4. هر آرتیکل فقط **یا بدهکار یا بستانکار** (نه هر دو، نه منفی).
5. حساب آرتیکل **قابل ثبت و فعال** باشد؛ اگر حساب `RequiresParty` باشد، «طرف حساب» الزامی است.

فقط اسناد **قطعی** در دفتر روزنامه، دفتر حساب، تراز آزمایشی و داشبورد دیده می‌شوند.

---

## ۴) صدور خودکار سند از اسناد انبار

جدول `AccInvRules` هر **نوع سند انبار** را به دو حساب نگاشت می‌کند:

| ماهیت نوع سند | حساب موجودی کالا | حساب طرف مقابل |
|---|---|---|
| افزایشی (رسید) | بدهکار | بستانکار |
| کاهشی (حواله) | بستانکار | بدهکار |
| خنثی (انتقال) | — سند صادر نمی‌شود — | — |

- مبلغ سند: در حالت `UseCostValue` از **بهای تمام‌شده‌ی خروج (`OutCost`)** و در غیر این صورت از
  `مقدار × قیمت − تخفیف` سطرهای سند انبار گرفته می‌شود.
- نقاط اتصال در `InvDocsController`:
  `Confirm` → `PostInventoryDocAsync` و `Unconfirm` / `Cancel` / `Delete` → `UnpostInventoryDocAsync`.
- صدور مجدد، ابتدا سند خودکار قبلی را حذف می‌کند (بدون سند تکراری).
- قواعد پیش‌فرض هنگام راه‌اندازی ساخته می‌شوند اما **غیرفعال** هستند؛ پس از بررسی کدینگ،
  آن‌ها را در صفحه‌ی «سند خودکار انبار» فعال کنید.

---

## ۵) گزارش‌ها

| گزارش | مسیر | توضیح |
|---|---|---|
| داشبورد مالی | `/acc/dashboard` | جمع بدهکار/بستانکار، ترکیب ترازنامه، سود و زیان |
| دفتر روزنامه | `/acc/reports/journal` | همه‌ی آرتیکل‌های اسناد قطعی به ترتیب تاریخ |
| دفتر حساب | `/acc/reports/ledger` | گردش و مانده‌ی یک حساب با مانده‌ی ابتدای دوره (با/بدون زیرحساب‌ها) |
| تراز آزمایشی | `/acc/reports/trial-balance` | هشت‌ستونی، در سطح گروه/کل/معین/تفصیلی |

---

## ۶) صفحات کلاینت

| صفحه | مسیر |
|---|---|
| کدینگ حساب‌ها (درختی) | `/acc/accounts` |
| سال‌های مالی | `/acc/fiscal-years` |
| اسناد حسابداری | `/acc/vouchers` |
| فرم سند (جدید/ویرایش) | `/acc/vouchers/new` · `/acc/vouchers/edit/{id}` |
| قواعد سند خودکار انبار | `/acc/inv-rules` |
| داشبورد مالی | `/acc/dashboard` |

همه‌ی صفحات زیر گروه **ERP** در منوی کناری، در سه زیرعنوان
«حسابداری — تعاریف / عملیات / گزارش‌ها» قرار دارند.

---

## ۷) نقشه‌ی API

```
GET    api/acc/fiscal-years
POST   api/acc/fiscal-years
POST   api/acc/fiscal-years/{id}/set-current
DELETE api/acc/fiscal-years/{id}

GET    api/acc/accounts?activeOnly=&withBalances=
GET    api/acc/accounts/tree
GET    api/acc/accounts/lookups?search=
POST   api/acc/accounts
POST   api/acc/accounts/move
DELETE api/acc/accounts/{id}

GET    api/acc/vouchers?fiscalYearId=&status=&source=&search=&from=&to=&page=&pageSize=
GET    api/acc/vouchers/new
GET    api/acc/vouchers/{id}
POST   api/acc/vouchers
POST   api/acc/vouchers/{id}/confirm | /unconfirm | /cancel
DELETE api/acc/vouchers/{id}

GET    api/acc/reports/ledger?accountId=&from=&to=&includeChildren=
GET    api/acc/reports/journal?from=&to=&page=&pageSize=
GET    api/acc/reports/trial-balance?level=&from=&to=&hideZero=
GET    api/acc/reports/dashboard

GET    api/acc/inv-rules
POST   api/acc/inv-rules
POST   api/acc/inv-rules/post-doc/{invDocId}
```

---

## ۸) دسترسی‌ها (RBAC)

| ماژول | عملیات |
|---|---|
| `AccAccounts` | Create, Read, Update, Delete, Export |
| `AccVouchers` | Create, Read, Update, Delete, Export, **Confirm**, **Cancel** |

---

## ۹) پایگاه داده

جدول‌های جدید: `AccFiscalYears`, `AccAccounts`, `AccVouchers`, `AccVoucherLines`, `AccInvRules`.

مهاجرت: `Migrations/20260905212404_AddAccountingModule.cs` — به‌صورت **T-SQL محافظت‌شده**
(`IF OBJECT_ID(...) IS NULL`) نوشته شده تا روی دیتابیس‌هایی که اسکیمای‌شان با ModelSnapshot اختلاف دارد
بدون خطا اجرا شود. نسخه‌ی خام SQL نیز در `sql/accounting/AddAccountingModule.sql` موجود است.

اجرا:

```bash
dotnet ef database update -p src/Inventory.Api -s src/Inventory.Api
# یا صرفاً API را ری‌استارت کنید؛ DbInitializer خودش Migrate() را صدا می‌زند.
```
