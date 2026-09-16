# ممیزی «مجوز مشاهده» و نمایش منو — ۲۰۲۶/۰۹/۱۶

بررسی کامل زنجیرهٔ دسترسی: `تنظیمات ← نقش‌ها و دسترسی‌ها` → جدول `Permissions` → توکن/نشست کاربر → منوی کناری.

قانون مورد انتظار: **هر بخش از منو باید یک مجوز «مشاهده» داشته باشد و اگر تیک آن برای نقش کاربر
خوردە نشدە باشد، آن آیتم (و گروه خالی‌شدە) اصلاً رندر نشود.**

---

## ۱) یافتهٔ بحرانی: کاتالوگ پرمیشن‌ها در مرج `9ab91a7` پاک شده بود

`RbacSeeder.ModuleActions` تنها منبع ساخت رکوردهای جدول `Permissions` است
(هیچ Seeder یا SQL دیگری در پروژه به این جدول نمی‌نویسد).

| | تعداد ماژول در کاتالوگ |
|---|---|
| والد `975c052` (خط HR) | ۵۸ |
| والد `a252529` (main ریموت) | ۵۰ |
| **نتیجهٔ مرج `9ab91a7` (وضعیت فعلی main)** | **۱۷** ❌ |

یعنی **۴۱ ماژول** در مرج از دست رفته بودند. اثر آن:

* آن ماژول‌ها در جدول `Permissions` ساخته نمی‌شوند → در صفحهٔ «نقش‌ها و دسترسی‌ها» **اصلاً لیست نمی‌شوند** که مدیر بتواند تیک مشاهده بزند.
* `Auth.HasModule(...)` همیشه `false` → آیتم منو برای همه (حتی Admin) پنهان.
* در دیتابیس‌های قدیمی چون Seeder فقط «اضافه» می‌کند و حذف نمی‌کند، رکوردهای قبلی باقی مانده‌اند؛
  بنابراین باگ فقط روی **نصب تازه / ماژول جدید** خودش را نشان می‌داد.

ماژول‌های از دست رفته: `ItRequests, Settings, Warehouses, InvDocs, InvDocTypes, AccAccounts,
AccVouchers, AccDimensions, FixedAssets, Budgets, Moadian, FiscalPrinter, FacInvoices, TrsAccounts,
TrsVouchers, TrsCheques, StkSessions, StkBarcodes, Referrers, ReferrerWallets, ReferrerPanel,
LeaveRequests, Attendance, RadisHr, Dashboards, ReportPages, Projects, ReportWorks, Karfarmas,
TypeFactors, ProjectAttach, ProjectCartable, InnerLetters, DocArchive, HrCore, HrPay, HrMain,
FaAtt, FaPay, FaLms, FaCom`

**اقدام:** کاتالوگ کامل از والد `975c052` بازگردانی شد + تغییر خودِ main (`WorkOrders.Delete`) حفظ شد.
همچنین کامنت شکستهٔ `// ==== درخواست خدمت آی‌تی ==== // ==== پیام‌رسان ==== ` که یادگار همان مرج بود اصلاح شد.

---

## ۲) آیتم‌های بدون هیچ گیت دسترسی

| آیتم | مسیر | وضعیت قبلی |
|---|---|---|
| کارتابل من | `/cartable` | بدون گیت در منو **و** بدون گیت در `RouteGuards` |
| بایگانی شخصی | `/my-archive` | بدون گیت در منو **و** بدون گیت در `RouteGuards` |
| نوار پایین موبایل: خرید / فروش / تعمیرات | — | بدون گیت (فقط بدنهٔ صفحه گارد داشت) |
| سایدبار و نوار پایین معرف: کیف پول / کالاها / کارت من | — | فقط با `Auth.IsReferrer`، بدون بررسی `ReferrerPanel.*` |

**اقدام:**
* دو ماژول جدید `MyCartable.View` و `MyArchive.View` به کاتالوگ اضافه شد و در `RbacSeeder`
  **به همهٔ نقش‌های فعال اعطای پیش‌فرض می‌شود** تا بعد از ارتقا از منوی کسی نپرد؛ مدیر می‌تواند
  بعداً برای هر نقش بردارد.
* هر دو مسیر به `RouteGuards` در `MainLayout.razor` اضافه شد.
* آیتم‌های نوار پایین موبایل و سایدبار معرف گیت خوردند.

---

## ۳) عنوان گروه‌ها حتی وقتی همهٔ آیتم‌ها پنهان بودند رندر می‌شد

شش گروه در `NavMenu.razor` هیچ شرطی نداشتند: **داشبوردها، عملیات، مدیریت سیستم، گزارش‌ها،
اطلاعات پایه، سیستم**. کاربر بدون هیچ دسترسی، ۶ عنوان گروه خالی می‌دید.

(در مقابل `HrWorkspaceNav` و `ErpWorkspaceNav` درست پیاده شده بودند: `Visible()` آیتم‌های بدون
مجوز را فیلتر و سکشن خالی را حذف می‌کند.)

**اقدام:** هر شش گروه داخل `@if (ShowXxxGroup)` قرار گرفتند؛ این شرط‌ها در `@code` از
`ModuleAccess.CanSeeAny(...)` ساخته می‌شوند.

---

## ۴) معنای `HasModule` با «مشاهده» یکی نبود

`HasModule("X")` یعنی «حداقل یک دسترسی از ماژول X» — پس کاربری که فقط `X.Delete` داشت و
`X.Read` نداشت، آیتم را در منو می‌دید. این دقیقاً خلاف خواستهٔ «تیک مشاهده» است.

**اقدام:** متد `CanSee(module)` به `IAuthState` اضافه شد (`Services/ModuleAccess.cs`):

1. `X.View` یا `X.Read` یا `X.Access` → نمایش
2. ماژول‌هایی که اکشن مشاهدهٔ استاندارد ندارند (`ItRequests`, `ReferrerPanel`, `LeaveRequests`,
   `Attendance`, `Dashboards`, `ReportPages`) → «حداقل یک دسترسی از ماژول»
3. در غیر این صورت → پنهان

همهٔ گیت‌های منو در `NavMenu.razor` از `HasModule` به `CanSee` تبدیل شدند (۳۶ مورد)،
به‌علاوهٔ `ErpWorkspaceNav` و دکمه‌های پیام‌رسان.

> **تصمیم عمدی:** برخلاف `HrNavigation`، در `CanSee` «دورزدن مدیر» (`IsAdmin`) اعمال نمی‌شود.
> نقش Admin همهٔ پرمیشن‌ها را از Seeder/AuthService می‌گیرد، پس به‌طور پیش‌فرض همه‌چیز را می‌بیند؛
> ولی اگر تیک مشاهده را برداشتید، منو باید به آن احترام بگذارد (وگرنه نمی‌شود نتیجه را تست کرد).

---

## ۵) موارد بررسی‌شدهٔ سالم (تغییر نکردند)

* `HrWorkspaceNav` / `HrNavigation` — هر آیتم پرمیشن صریح دارد (`FaAtt.Read`, `HrCore.Manage`, …)
  و سکشن خالی حذف می‌شود. ✅
* `ErpWorkspaceNav` / `ErpNavigation` — ساختار درست؛ فقط ملاکش از `HasModule` به `CanSee` تغییر کرد. ✅
* گیت‌های سمت API (`RbacControllerBase.ForbiddenUnlessAsync`) — پنهان‌کردن منو صرفاً UI است و
  امنیت واقعی سمت سرور اعمال می‌شود. ✅ دست‌نخورده.
* `RouteGuards` در `MainLayout` — ۴۵ مسیر نگاشت داشت؛ دو مورد ناقص اضافه شد.

---

## ۶) نکتهٔ باز (نیازمند تصمیم شما)

`HrNavigation.Entry.Allowed(has, admin) => admin || Permissions.Any(has)` — یعنی کاربری که
`Role == "Admin"` دارد، **فارغ از تیک‌ها** کل منوی «منابع انسانی اصلی» را می‌بیند.
این در ۵ نقطه استفاده می‌شود (`HrWorkspaceNav`, `HrWorkspaceShell`, `HrMainHub`, `HrSection`, `HrSettings`).

اگر بخواهید آنجا هم دقیقاً مثل بقیه رفتار کند، کافی است `admin ||` از همان یک خط حذف شود.
به‌خاطر ریسک قفل‌شدن دسترسی مدیر، فعلاً دست‌نخورده ماند.

---

## فایل‌های تغییر یافته

| فایل | تغییر |
|---|---|
| `inventory/src/Inventory.Api/Data/RbacSeeder.cs` | بازگردانی ۴۱ ماژول + افزودن `MyCartable`/`MyArchive` + اعطای پیش‌فرض به نقش‌های فعال |
| `inventory/src/Inventory.Client/Services/ModuleAccess.cs` | **جدید** — منطق «مجوز مشاهده» |
| `inventory/src/Inventory.Client/Services/AuthState.cs` | افزودن `CanSee` به `IAuthState` و `AuthState` |
| `inventory/src/Inventory.Client/Layout/NavMenu.razor` | گیت مشاهدهٔ همهٔ آیتم‌ها + پنهان‌کردن گروه خالی |
| `inventory/src/Inventory.Client/Layout/MainLayout.razor` | گارد `/cartable` و `/my-archive` + گیت نوار پایین موبایل و سایدبار معرف |
| `inventory/src/Inventory.Client/Shared/ErpWorkspaceNav.razor` | ملاک نمایش = `CanSee` |
| `inventory/src/Inventory.Client/Pages/Chat/ChatPage.razor` | گارد ورود = `CanSee("Chat")` |
| `inventory/src/Inventory.Client/Pages/ItAssets/WorkOrders.razor` | دکمهٔ گفتگو = `CanSee("Chat")` + راست‌چین‌کردن تب‌ها |
| `inventory/src/Inventory.Client/Pages/Settings/Roles.razor` | برچسب و دستهٔ دو ماژول جدید |

## تأیید

```
dotnet build Inventory.Api     → Build succeeded, 0 Error(s)
dotnet build Inventory.Client  → Build succeeded, 0 Error(s)
```

## ⚠️ بعد از استقرار

دسترسی‌ها داخل نشست کاربر (`localStorage → authSession`) ذخیره می‌شوند؛ بنابراین **همهٔ کاربران
باید یک‌بار خارج و دوباره وارد شوند** تا پرمیشن‌های تازه (از جمله `MyCartable.View` و
`MyArchive.View`) در منویشان اعمال شود.
