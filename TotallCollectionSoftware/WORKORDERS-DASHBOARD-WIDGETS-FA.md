# ویجت‌های دستور کار در داشبورد شخصی + رفع سقف داشبورد

این تغییرات **داخل سورس اصلی** و در همان مسیرهای موجود اعمال شده‌اند؛ هیچ
پروژه، سرویس یا درخت موازی ساخته نشده است.

---

## ۱) ۲۳ ویجت دستور کار

پیش از این کاتالوگ فقط **۳** ویجت دستور کار داشت
(`wo-open`، `wo-overdue`، `wo-by-status`). اکنون **۲۳** ویجت دارد و
کاتالوگ کل از **۴۸** به **۶۸** رسیده است.

محل ظهور: `/my-dashboards` («داشبورد من»). این صفحه کاتالوگ را از
`api/my-dashboards/widget-catalog` می‌گیرد و خودکار گروه‌بندی می‌کند، پس
هیچ تغییری در کلاینت لازم نبود. دو گروه تازه دیده می‌شود:
**دستور کار** (۲۲ ویجت) و **پروژه کاری** (۱ ویجت).

| کلید | عنوان |
|---|---|
| `wo-open` | دستور کارهای باز |
| `wo-overdue` | دستور کارهای معوق |
| `wo-by-status` | وضعیت دستور کارها |
| `wo-my-inbox` | محول‌شده به من |
| `wo-my-unseen` | دستورهای دیده‌نشده |
| `wo-due-today` | مهلت امروز |
| `wo-due-week` | مهلت ۷ روز آینده |
| `wo-my-next` | نزدیک‌ترین مهلت‌ها |
| `wo-ontime-rate` | نرخ انجام به‌موقع |
| `wo-outcome-mix` | ترکیب نتیجهٔ دستورها |
| `wo-closed-nodone` | بسته بدون انجام |
| `wo-open-age` | سن معوق‌ها |
| `wo-created-vs-closed` | ایجاد در برابر بستن |
| `wo-first-response` | زمان تا اولین پاسخ |
| `wo-pending-approval` | در انتظار تأیید من |
| `wo-pending-approval-list` | فهرست در انتظار تأیید |
| `wo-team-load` | بار کاری هر نفر |
| `wo-team-scoreboard` | عملکرد گیرندگان |
| `wo-unanswered` | بی‌پاسخ‌ترین گیرندگان |
| `wo-extension-hot` | تمدیدهای مکرر |
| `wo-checklist-rate` | تکمیل چک‌لیست |
| `wo-source-mix` | مبدأ دستورها |
| `wp-my-progress` | پیشرفت پروژه‌های من |

### دامنهٔ دسترسی

هر ۲۳ ویجت فقط دستورهای کاری را می‌شمارند که:

```
شرکت فعال کاربر  AND  (صادرکننده = من  OR  گیرنده = من)
```

این همان قاعده‌ای است که `WorkOrdersController` در `Stats` / `Calendar` /
`Export` استفاده می‌کند. نمای «کل شرکت» **عمداً** اضافه نشده تا بدون مجوز
تازه، دادهٔ دیگران قابل دیدن نباشد. این قاعده برای همه — از جمله مدیر —
یکسان است.

گزینهٔ «دامنه» در تنظیمات ویجت فقط سه انتخاب شخصی دارد:
`هر دو` / `محول‌شده به من` / `صادرشده توسط من`.

اگر شرکت فعال انتخاب نشده باشد، ویجت به‌جای عدد **صفر** پیام خطا می‌دهد
تا کاربر فکر نکند «دستوری ندارد».

---

## ۲) ایندکس‌ها داخل سورس (بدون مرحلهٔ دستی)

کلاس تازه:

```
inventory/src/Inventory.Api/Data/WorkOrderDashboardIndexSchemaV1.cs
```

مثل بقیهٔ کلاس‌های `*SchemaV1` خود مخزن، خودتعمیر و idempotent است و در
`DbInitializer` کنار `DashboardSchemaV1` اجرا می‌شود. **لازم نیست کسی
روی دیتابیس اسکریپت اجرا کند.**

| ایندکس | برای چه |
|---|---|
| `IX_WorkOrderAssignees_UserId_OrderId` | مسیر «دستورهای من» — حیاتی‌ترین |
| `IX_WorkOrderAssignees_OrderId` | گیرندگان یک دستور |
| `IX_WorkOrders_Company_Status_DueAt` | کوئری غالب: شرکت + وضعیت + مهلت |
| `IX_WorkOrders_Company_CreatedAt` | ویجت‌های بازه‌دار |
| `IX_WorkOrders_OwnerUserId` | دستورهای صادرشده توسط من |
| `IX_WorkOrderChecklistItems_OrderId_IsDone` | چک‌لیست و پیشرفت پروژه |

چرا لازم بود: جدول `WorkOrderAssignees` در مایگریشن اولیه فقط کلید اصلی
داشت، ولی هر ویجت یک نیمه‌جوین روی آن می‌زند:

```sql
WHERE EXISTS (SELECT 1 FROM WorkOrderAssignees a
              WHERE a.OrderId = w.Id AND a.UserId = @me)
```

بدون ایندکس، با رشد داده به اسکن کامل تبدیل می‌شد.

هر ۶ ایندکس هم **SQL Server** و هم **SQLite** را پوشش می‌دهند. اگر جدول
هنوز وجود نداشته باشد، راه‌اندازی برنامه نمی‌شکند.

فایل `inventory/sql/WorkOrderDashboardIndexes.sql` فقط برای مرجع/اجرای
دستی راهبر دیتابیس نگه داشته شده و دیگر لازم نیست.

---

## ۳) رفع سقف ۱۰ داشبورد

در `DashboardsController.Create` شرط `count >= 10` وجود داشت که پیام
«حداکثر ۱۰ داشبورد می‌توانید بسازید» می‌داد. **برداشته شد.**

متغیر `count` نگه داشته شد چون برای نام‌گذاری خودکار (`داشبورد ۱۱`،
`داشبورد ۱۲`، …) و `SortOrder` لازم است.

هیچ سقف دیگری روی تعداد داشبورد یا تعداد ویجت در هر داشبورد وجود ندارد.
موارد باقی‌مانده **هندسهٔ گرید** هستند، نه سهمیه: `Col` ۰–۱۱، `W` ۱–۱۲،
`H` ۱–۸، طول نام ۱۲۰ نویسه.

---

## ۴) فایل‌های تغییریافته

### تازه (۴)

| مسیر | نقش |
|---|---|
| `Inventory.Api/Services/Dashboards/WorkOrderWidgets.cs` | موتور ۲۳ ویجت |
| `Inventory.Api/Services/ItAssets/WorkOrderTone.cs` | منطق لحن/رنگ |
| `Inventory.Api/Data/WorkOrderDashboardIndexSchemaV1.cs` | ایندکس‌ها |
| `inventory/sql/WorkOrderDashboardIndexes.sql` | مرجع (اختیاری) |

### ویرایش‌شده (۷)

| مسیر | تغییر |
|---|---|
| `Inventory.Api/Services/Dashboards/WidgetCatalog.cs` | ۲۳ تعریف تازه، کاتالوگ ۴۸ ← ۶۸ |
| `Inventory.Api/Services/Dashboards/WidgetDataService.cs` | واگذاری به موتور تازه |
| `Inventory.Api/Data/DbInitializer.cs` | اجرای خودکار ایندکس‌ها |
| `Inventory.Api/Program.cs` | ثبت DI |
| `Inventory.Api/Controllers/Dashboards/DashboardsController.cs` | رفع سقف ۱۰ تایی |
| `Inventory.Shared/Dtos/DashboardDtos.cs` | `HigherIsBetter` |
| `Inventory.Client/Shared/DashWidgetCard.razor` | جهت درست دلتا |

`HigherIsBetter` لازم بود چون برای بعضی ویجت‌ها (مثل «معوق») کاهش عدد
خبر خوب است، ولی کارت داشبورد پیش‌تر همیشه افزایش را سبز نشان می‌داد.

---

## ۵) دست‌نخورده

این فایل‌ها عمداً تغییر نکردند:

- `Pages/DashboardLive.razor` («داشبورد مدیریت») — موتور ویجت ندارد و
  آمار دوربین/سیستم/NVR را از SignalR می‌گیرد. ویجت‌های دستور کار فقط در
  «داشبورد من» ارائه می‌شوند.
- `Layout/NavMenu.razor` — منو تغییر نکرده.
- `Data/RbacSeeder.cs` — **هیچ مجوز تازه‌ای اضافه نشده.** ویجت‌ها از
  مجوزهای موجود `WorkOrders.View` و `WorkProjects.View` استفاده می‌کنند.
- `Pages/Panel/MyDashboard.razor` و `Pages/My/MyBoard.razor`.

---

## ۶) تست‌ها

| مجموعه | نتیجه |
|---|---|
| لحن/رنگ | ۱۸ / ۰ |
| کاتالوگ | ۷۷ / ۰ |
| یکپارچگی | ۸۵ / ۰ |
| انزوا | ۲۴ / ۰ |
| **جمع ویجت** | **۲۰۴ / ۰** |
| ایندکس‌ها روی SQLite واقعی | ۱۵ / ۰ |
| رفع سقف داشبورد | ۱۰ / ۰ |

تست انزوا ۱۵۰+ رکورد بیگانه تزریق می‌کند و ثابت می‌کند هیچ‌یک از ۲۳ ویجت
تغییر نمی‌کند. همچنین تلاش برای شکستن دامنه با مقادیر `all` / `Company` /
`*` / `همه` / `کل شرکت` / `Org` / `null` / `../` / `Mine;Issued` رد می‌شود.

برنامهٔ کوئری واقعی که کارکرد ایندکس را ثابت می‌کند:

```
SEARCH WorkOrderAssignees USING COVERING INDEX
IX_WorkOrderAssignees_UserId_OrderId (UserId=? AND OrderId=?)
```

تست رفع سقف، بدنهٔ واقعی متد `Create` را با Roslyn از کنترلر استخراج و
روی SQLite اجرا می‌کند؛ ۶۰ داشبورد پشت سر هم ساخته شد.

---

## ۷) نکتهٔ استقرار

ایندکس‌ها در اولین اجرای برنامه ساخته می‌شوند. روی دیتابیس بزرگ، ساخت
ایندکس ممکن است چند ثانیه طول بکشد؛ پیام آن در لاگ دیده می‌شود:

```
[DB] ✔ ایندکس‌های داشبورد دستور کار آماده‌اند.
```

اگر کاربری ویجت‌های تازه را نمی‌بیند، مجوز `WorkOrders.View` را بررسی
کنید. `GET /api/my-dashboards/widget-catalog` برای مدیر باید **۶۸** ویجت
برگرداند.
