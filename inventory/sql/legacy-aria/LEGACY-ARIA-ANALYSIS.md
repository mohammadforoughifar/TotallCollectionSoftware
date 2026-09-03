# تحلیل بک‌آپ دیتابیس قدیمی «aria» (ویندوز فرم) برای مهاجرت به ماژول پروژه‌ها (وب)

- منبع: `https://github.com/mohammadforoughifar/Otomation-lak.git` ← `aria.rar` ← `aria.bak` (۸٫۱ مگابایت، فرمت MTF بک‌آپ SQL Server، بدون رمز و بدون فشرده‌سازی)
- نام دیتابیس/سرور قدیمی: `AriyaHamrahGhenerator` روی `SRV_FOROUGHARIA`
- روش تحلیل: چون در این محیط SQL Server در دسترس نیست، صفحات ۸K دیتا مستقیم از فایل `.bak` خوانده شد (`tools/dump.py`) و **تمام رکوردها با شمارنده سیستمی SQL Server (`sysrowsets.rcrows`) مطابقت داد**. خروجی CSV/JSON کنار همین فایل قرار دارد.

---

## ۱) جداول موجود در بک‌آپ و تعداد رکورد

| جدول قدیمی | رکورد | توضیح |
|---|---|---|
| `Entryandexitlist` | **۱۷۷۴** | جدول اصلی پروژه‌ها (نسخهٔ نهایی) |
| `Entryandexitlist1` | ۱۵۶۱ | نسخهٔ قدیمی‌تر/پشتیبان همان جدول (تا ۲۰۲۵/۱۱/۲۹) — **زیرمجموعه‌ی جدول اصلی است؛ ۸۰ رکورد آن بعداً در جدول اصلی ویرایش شده‌اند** |
| `ReportWork` | **۶۶۹۸** | گزارش کار (نسخهٔ نهایی؛ `IdList1` = کد پروژه به‌صورت متن) |
| `ReportWorklast` | ۶۶ | نسخهٔ آزمایشی قدیمی (`IdList1` عددی = Id پروژه) — ۶۵ رکورد از ۶۶ عیناً در `ReportWork` هست |
| `Karfarma_Tbl` | **۲۰۰** | کارفرما (نسخهٔ نهایی؛ Idهای بزرگ مثل ۱۲۲۱۴ هم استفاده شده‌اند) |
| `Karfarma_Tbl1` | ۲۰۲ | نسخهٔ قدیمی‌تر کارفرما — نام‌ها با نسخهٔ اصلی در ۱۴۴ مورد **جابه‌جا/متفاوت** است (به‌جز ۱ رکورد، هیچ آدرس/تلفنی ندارد) |
| `TypeFactorTbl` | **۱۱** | نوع فاکتور |
| `AttachAriya_Tbl` | **۱** | پیوست مستندات — فقط یک رکورد |
| `Pic_Tbl` | **۰** | تصاویر — خالی |

> جدول کاربر/سمت (`UserId`, `SematId`, `Operator`) در این بک‌آپ نیست (در دیتابیس دیگری بوده).

### ساختار ستون‌های `Entryandexitlist`
`ProjectCode nvarchar(200)`, `NameProject`, `SerialNumber`, `GhabzExit`, `FactorNumber`, `FactorType3 nvarchar` (متن نوع فاکتور — قدیمی), `KarshenasiAvalie`, `ReceiverProject`, `Description nvarchar(max)`, `Id int (PK, identity)`, `Employer int` (→ Karfarma), `DateKhoroj datetime`, `DateVorod datetime`, `FileDate date`, `DeliveryDate date`, `DateMovaqatExit date`, `SematId`, `UserId`, `DateSabtPoroje datetime`, `DateNiazMoshtari datetime`, `IsPoshe bit`, `FactorType int` (→ TypeFactorTbl)

### ساختار ستون‌های `ReportWork`
`Id`, `IdList1 nvarchar(200)` (= ProjectCode), `Date date`, `Operator int`, `DescriptionWork nvarchar(max)`, `SematId`, `UserId`, `BreakFastTime/LunchTime/TotalTime/SpentTime/DateStart/DateEnd time(7)`

### `TypeFactorTbl` (۱۱ رکورد)
۱ بدون فاکتور، ۲ RE، ۳ برگشتی، ۴ تایید کارفرما نشد، ۵ رسمی، ۶ عودت، ۷ عودت داده شد، ۸ غیر رسمی، ۹ فاکتور نشده است، ۱۰ گارانتی، ۱۱ متفرقه

### `AttachAriya_Tbl` (۱ رکورد)
`AttachTitleAriya = 4fa852c6-….jpg.png`, `PathDocument = E:\bin\Debug\DocumentAiya\866cbb88-cb02-4a0f-aa3a-ce52883c1a0f.png`, `ProjectId = 4`, `UserId = 28` — **فایل فیزیکی در بک‌آپ نیست.**

---

## ۲) نگاشت جدول قدیمی ← جدید (بر اساس Entity های `Entities/Projects/Projects.cs`)

### `Karfarma_Tbl` → `KarFarma`
| قدیمی | جدید | نکته |
|---|---|---|
| KarfarmaId | Id | با `IDENTITY_INSERT` حفظ می‌شود (Idهای ۱ تا ۱۲۲۱۴ با فاصله) |
| KarfarmaName | Name (200) | حداکثر طول ۳۴ ✔ |
| Address / ModiramelPhone / Phone / Fax / Shomaresabt | Address / ModirAmelPhone / Telephone / Fax / ShomareSabt | همه خالی هستند |
| — | IsDelete=0, CreatedAt | |

### `TypeFactorTbl` → `TypeFactor`
Id و Name عیناً (۱۱ رکورد). حداکثر طول ۱۷ ✔

### `Entryandexitlist` → `ProjectEntryExit`
| قدیمی | جدید | نکته |
|---|---|---|
| Id | Id | حفظ Id (تا ۲۴۶۲۳ با فاصله‌های بزرگ) |
| ProjectCode | CodeProject (60) | تبدیل `RE1/1139` → `RE1-1139` (فرمت وب) |
| — | ReturnProjectId | از کد استخراج: `RE2/1932` → ۲ ؛ کدهای عددی → ۰ |
| NameProject | ProjectName (250) | حداکثر ۷۲ ✔ |
| SerialNumber | SerialNumber (50) | حداکثر ۲۱ ✔ |
| GhabzExit | GhabzExit (50) | ✔ |
| FactorNumber | FactorNumber (50) | ✔ |
| KarshenasiAvalie | KarshenasiAvalie (50) | حداکثر ۸۱ — **۲ رکورد بلندتر از ۵۰** (بریده یا افزایش طول ستون) |
| ReceiverProject | ProjectReceiver (200) | ✔ |
| Description | Description (1000) | حداکثر ۱۶۶ ✔ |
| Employer | KarFarmaId | یک مورد `Employer=7` که در `Karfarma_Tbl` **وجود ندارد** (۱۸ پروژه؛ در `Karfarma_Tbl1` نامش «همیار موتور» است) |
| FactorType | FactorTypeId (nullable) | مقادیر `0` → NULL. ستون متنی قدیمی `FactorType3` (رسمی/غیر رسمی) با `FactorType` سازگار است و لازم نیست |
| DateKhoroj / DateVorod | ExitDate / EntryDate | |
| FileDate / DeliveryDate / DateMovaqatExit | FileDate / DeliveryDate / TemporaryExitDate | |
| DateSabtPoroje / DateNiazMoshtari | ProjectRegistrationDate / CustomerRequiredDate | |
| IsPoshe | IsFolder | |
| UserId (28، 29، 2028 یا NULL) | UserId (اجباری) | نیاز به نگاشت کاربر قدیمی → کاربر وب |
| SematId | — | حذف |
| — | FlowStatus = 3 (نهایی) | تا کارتابل‌ها پر نشوند |
| — | TotalSpentTime | جمع SpentTime گزارش‌ها بعد از انتقال |

### `ReportWork` → `ReportWork`
| قدیمی | جدید | نکته |
|---|---|---|
| Id | Id | حفظ |
| IdList1 (کد پروژه) | CodeProject + **ProjectId** | ProjectId از جستجوی کد در جدول پروژه‌ها |
| Date | ReportDate | ۵ رکورد با تاریخ `1900-01-01` (بدون تاریخ) |
| DescriptionWork | WorkDescription (1000) | حداکثر ۳۸۱ ✔ |
| DateStart / DateEnd | StartTime / EndTime | |
| BreakFastTime / LunchTime | BreakfastTime / LunchTime | |
| SpentTime | SpentTime | **۱۳۹۴ رکورد** SpentTime ذخیره‌شده با فرمول (پایان−شروع−صبحانه−ناهار) نمی‌خواند |
| Operator (کد پرسنل، ۴۹ مقدار مختلف؛ ۹۷ رکورد = 0) | **معادلی در جدول جدید نیست** | جدول جدید فقط `UserId` (کاربر لاگین) دارد |
| UserId (29: ۶۲۴۴ رکورد، 2028: ۴۵۲، 28: ۲) | UserId | نیاز به نگاشت |
| TotalTime, SematId | — | حذف |

### `AttachAriya_Tbl` + `Pic_Tbl` → `ProjectAttach`
فقط ۱ رکورد و فایل آن در بک‌آپ نیست. سیستم جدید فایل‌ها را **رمزنگاری‌شده** با نام تصادفی در `wwwroot/SecureFiles/<کد پروژه>/<نوع>` ذخیره می‌کند؛ بنابراین انتقال یعنی: خواندن فایل از مسیر قدیمی → `EncryptAndStoreAsync` → درج رکورد. بدون فایل فیزیکی، فقط می‌توان رکورد را نادیده گرفت.

---

## ۳) مشکلات دادهٔ کشف‌شده (نیاز به تصمیم)

1. **کد پروژه تکراری** در `Entryandexitlist` (۱۱ کد): `0` (۵ پروژه‌ی «برگشتی» بدون کد)، `1557`، `1326`، `1385`، `1453`، `1473`، `1485`، `1486`، `1487`، `2293`، `RE1/1643`. سیستم وب کد را یکتا فرض می‌کند (تولید کد بعدی و جستجوی گزارش بر اساس کد).
2. **گزارش‌های کار یتیم:** ۳ گزارش با کد `4`، `1551`، `241` که پروژه‌ای با آن کد وجود ندارد (کد `4` یک رکورد تستی «اللبفف» است).
3. **کارفرمای ناموجود:** `Employer=7` برای ۱۸ پروژه (در `Karfarma_Tbl` نیست؛ در `Karfarma_Tbl1` = «همیار موتور»).
4. **کارفرمای تکراری بر اساس نام:** «آب و فاضلاب سمنان»، «سیمان کیاسر»، «شاهانی»، «آریا همیار ژنراتور» (چند بار با Id مختلف).
5. **نگاشت کاربران:** `UserId`های قدیمی ۲۸، ۲۹، ۲۰۲۸ (و NULL برای ۱۰۹۰ پروژه) → باید به Id کاربران وب نگاشت شوند. ستون `Operator` گزارش کار (۴۹ کد پرسنل مختلف) هیچ جایی در مدل جدید ندارد.
6. **فرمت کد برگشتی:** قدیمی `RE1/1139` (۴۹ مورد) و `RE1-2654` (۳ مورد) — وب: `REn-کد`. برخی گزارش‌ها هم با همین کدها ثبت شده‌اند (`RE1/2536` ×۱۲، `RE1-2481` ×۸، `RE1/2479` ×۶، `RE1-2496` ×۳).
7. **۲ مقدار `KarshenasiAvalie` بلندتر از ۵۰ کاراکتر** (ستون جدید `MaxLength(50)`).
8. `SpentTime` ذخیره‌شده در ۱۳۹۴ گزارش با فرمول سیستم جدید مطابقت ندارد؛ ۱۵۵ گزارش ساعت شروع `00:00` دارند.
9. ۵ گزارش با تاریخ `1900-01-01`.
10. جدول‌های `*1` و `ReportWorklast` نسخه‌های قدیمی‌ترند — پیشنهاد: نادیده گرفته شوند (فقط `Karfarma_Tbl1` برای پیدا کردن نام کارفرمای Id=7 مفید است).

---

## ۴) تصمیم‌های نهایی (تأییدشده توسط کاربر)

| موضوع | تصمیم |
|---|---|
| کدهای تکراری | **همان‌طور که در جدول قدیمی است منتقل می‌شود** (تغییری در کد داده نمی‌شود). گزارش کارِ روی کد تکراری به پروژه‌ای وصل می‌شود که تاریخ ورودش نزدیک‌ترین تاریخ قبل از تاریخ گزارش باشد (فقط ۱ مورد: کد ۱۵۵۷). |
| کاربران قدیمی (`UserId`) | با **همان Id** در جدول `Users` ساخته می‌شوند اگر وجود نداشته باشند (`legacy_<id>`، غیرفعال، بدون امکان ورود). ادمین بعداً نام/فعال‌بودن را اصلاح می‌کند. |
| `Operator` گزارش کار | **انجام‌دهندهٔ کار** است نه ثبت‌کننده → ستون جدید `ReportWorks.OperatorId` (FK اختیاری به `Users`). اگر اپراتور در `Users` نبود، با همان Id ساخته می‌شود. |
| پیوست‌ها | نادیده گرفته شد (فایل فیزیکی در دسترس نیست). |
| روش اجرا | اسکریپت T‑SQL آماده (`Import-LegacyAria.sql`) با `IDENTITY_INSERT` و تراکنش کامل. |
| `TotalSpentTime` پروژه | نوع ستون از `time` به `bigint` (تیک) تغییر کرد — ۲۰۶ پروژه بیش از ۲۴ ساعت گزارش دارند (تا ۸۴۴ ساعت) و `time` آن را نمی‌پذیرفت. |
| `KarshenasiAvalie` | طول ستون از ۵۰ به ۱۰۰ افزایش یافت. |
| گزارش‌های یتیم (۳ مورد) | به پروژهٔ نگه‌دارندهٔ «گزارش‌های قدیمی بدون پروژه» با کد `LEGACY-UNKNOWN` (Id=24999) وصل می‌شوند؛ کد اصلی در ستون `CodeProject` خود گزارش می‌ماند. |
| کارفرمای Id=7 | از `Karfarma_Tbl1` («همیار موتور») تکمیل شد. |
| `FactorType = 0` | → `NULL` |
| تاریخ گزارش `1900-01-01` (۵ مورد) | → `0001-01-01` (یعنی «بدون تاریخ») |

## ۵) مراحل اجرا روی سرور

1. نسخهٔ جدید برنامه را دیپلوی و **یک بار اجرا** کنید تا مایگریشن `LegacyAriaImportPrep` اعمال شود
   (ستون `OperatorId`، طول `KarshenasiAvalie`، نوع `TotalSpentTime`).
2. از `InventoryDb` **بک‌آپ** بگیرید.
3. اگر قبلاً پروژه/گزارش آزمایشی در سیستم وب ثبت شده: `Clean-LegacyAria.sql` را اجرا کنید (همهٔ داده‌های ماژول پروژه پاک می‌شود).
4. `Import-LegacyAria.sql` را در SSMS روی `InventoryDb` اجرا کنید (حدود ۴ مگابایت، ~۹ هزار دستور؛ چند ثانیه).
   در پایان پیام «مهاجرت با موفقیت انجام شد ✅» و شمارش‌ها چاپ می‌شود؛ در صورت هر خطا کل تراکنش برمی‌گردد.
5. در برنامه، منوی «کاربران»: کاربران `legacy_*` را با نام واقعی پرسنل ویرایش و در صورت نیاز فعال کنید
   (تا در فیلتر «اپراتور» صفحهٔ گزارش‌های کار هم ظاهر شوند).

## ۶) فایل‌های این پوشه
- `Import-LegacyAria.sql` — **اسکریپت مهاجرت** (تولیدشده؛ دستی ویرایش نکنید)
- `Clean-LegacyAria.sql` — پاک‌سازی ماژول پروژه برای اجرای مجدد
- `DUPLICATE-CODES-decide.csv` — لیست کدهای تکراری (اطلاعاتی)
- `*.csv` — خروجی کامل هر جدول (UTF‑8 با BOM، قابل باز شدن در Excel)
- `aria-dump.json` — همهٔ جداول به‌صورت JSON (ورودی اسکریپت مهاجرت)
- `tools/pages.py, rec.py, dump.py` — استخراج مستقیم از `.bak` (`python3 dump.py` → `aria_dump.json`)
- `tools/build_import_sql.py` — ساخت `Import-LegacyAria.sql` از JSON
