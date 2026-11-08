# پیشنهاد تخصصی — لایسنس ماژول‌ها به ازای هر شرکت

**تاریخ:** ۱۸ آبان ۱۴۰۵ · **دامنه:** سامانهٔ یکپارچه (Inventory ERP) — چندشرکتی، روی‌محل (IIS) و تک‌سروره
**وضعیت فعلی کد:** هیچ سازوکار لایسنسی وجود ندارد؛ فقط RBAC (نقش/مجوز) و جداسازی دادهٔ شرکت‌ها.

> 📎 **پیوست ۲:** `MODULE-LICENSING-DEMO-AND-STORE-FA.md` — نسخهٔ دمو (محدودیت‌ها، **مدت دمو کاملاً قابل تنظیم توسط شما**، زمان‌سنجی ضد‌دستکاری، پاک‌سازی دادهٔ نمایشی، تبدیل دمو به پرداختی) و **فروش ماژول از طریق سایت فروشِ موجود شما** (قرارداد یکپارچه‌سازی شش اندپوینتی با HMAC و idempotency، کد فعال‌سازی آفلاین، ارتقا/تمدید/استرداد، آزادسازی دستگاه، پیش‌فاکتور و انتقال بانکی) به‌همراه جدول ۱۷ بندی «آینده دچار مشکل نشویم».

---

## ۱) خلاصهٔ اجرایی — پیشنهاد در هفت خط

1. **لایسنس را روی «ماژول × شرکت» تعریف کنید، ولی آن را نفروشید و مدیریت نکنید؛ در سطح «بسته» (Bundle) بفروشید.** ۷۶ ماژول فنی برای فروش و پشتیبانی غیرقابل‌مدیریت است؛ ۹ بستهٔ تجاری (انبار، حسابداری، فروشگاه، اداری، منابع انسانی، IT، تحلیل، همکاری، هسته) قابل قیمت‌گذاری و قابل مذاکره است.
2. **لایسنس و RBAC دو چیز مستقل‌اند و هرگز جای هم را نمی‌گیرند.** دسترسی مؤثر = «مجوز نقش کاربر» ∩ «لایسنس شرکت فعال» ∩ «فعال‌بودن شرکت».
3. **یک نقطهٔ اجرای مرکزی بسازید، نه ۷۶ نقطه.** با فیلتر سراسری + تزریق در `EffectivePermissions` + فیلترکردن فهرست مجوزها در ورود، منوی کلاینت هم **خودکار** مخفی می‌شود بدون دست‌زدن به منوها.
4. **رفتار پیش‌فرض = باز (Grandfathered).** شرکتی که هیچ ردیف لایسنسی ندارد، «نامحدود» است. بدون این قاعده، اولین انتشار همهٔ مشتریان فعلی را قفل می‌کند.
5. **انقضا = نرم، نه سخت.** سه حالت: فعال → دورهٔ مهلت (فقط خواندن، ۱۴ روز، با بنر) → منقضی (فقط خواندن + هشدار؛ **داده هیچ‌وقت حذف/قفل نمی‌شود**).
6. **برای نصب روی‌محل: توکن امضاشدهٔ آفلاین** (Ed25519) + ابزار CLI صدور، به‌جای تماس با سرور لایسنس. صادقانه بگوییم: روی‌محل قابل‌دورزدن است؛ هدف «حسابرسی‌پذیر و دستکاری‌آشکار» است، نه DRM شکست‌ناپذیر.
7. **اعطای لایسنس نباید در دست «ادمین همان شرکت» باشد.** یک مجوز جداگانهٔ `Licensing.Grant` که فقط شما (فروشنده) دارید، یا توکن امضاشده.

---

## ۲) تصمیم‌های معماری (و گزینه‌هایی که رد می‌کنم)

| # | تصمیم | چرا | گزینهٔ ردشده و دلیل رد |
|---|---|---|---|
| ۱ | لایسنس در **دیتابیس** (نه فایل config) | قابل حسابرسی، تاریخ‌دار، قابل گزارش «چه کسی چه وقتی داد»؛ با بکاپ دیتابیس همراه می‌رود | فایل `license.json` فقط: با هر انتشار IIS پاک می‌شود؛ کسی نمی‌داند چه کسی تغییرش داد |
| ۲ | واحد لایسنس = **ماژول**، واحد فروش = **بسته** | کنترل دقیق فنی + سادگی تجاری | لایسنس روی بسته: نمی‌شود یک ماژول را در وسط قرارداد برداشت یا اضافه کرد |
| ۳ | تفکیک کامل از RBAC | «شرکت X ماژول دستور کار را نخریده» با «کاربر Y اجازهٔ دیدن دستور کار را ندارد» دو پیام و دو فرایند متفاوت است | ادغام در RBAC: هر بار تغییر قرارداد باید نقش‌ها بازنویسی شوند؛ آشفتگی و خطای پشتیبانی |
| ۴ | گیت در **API**، نه فقط UI | مخفیکردن منو امنیت نیست؛ هر کسی می‌تواند `/api/workorders` را مستقیم صدا زند | فقط مخفی‌کردن منو — رد |
| ۵ | رفتار پیش‌فرض **باز** برای شرکتِ بدون لایسنس | سازگاری با مشتریان فعلی و نصب‌های قدیمی | پیش‌فرض بسته (fail-closed): اولین استقرار = قطع کامل سامانه |
| ۶ | **مهلت نرم** به‌جای قطع ناگهانی | قطع ناگهانی در آخر ماه مالی = فاجعهٔ پشتیبانی و از دست‌دادن مشتری | قطع در لحظهٔ انقضا — رد |
| ۷ | کش لایسنس در حافظه (۶۰ ثانیه) | بررسی مجوز در هر درخواست اجرا می‌شود؛ کوئری تازه در هر درخواست، کارایی را می‌خورد | بدون کش — رد |

---

## ۳) مدل داده

پیشنهاد: چهار جدول + یک فهرست ماژول. همه با الگوی «برچسب شرکت» ولی **بدون** `ICompanyOwned` و بدون فیلتر سراسری (لایسنس باید در اجرای پس‌زمینه و برای همهٔ شرکت‌ها خوانده شود).

```sql
-- ۱) فهرست ماژول‌ها: منبع واحد حقیقت، هم‌گام با RbacSeeder
CREATE TABLE SystemModuleCatalog (
    Module        nvarchar(50)  NOT NULL PRIMARY KEY,   -- مثلاً "WorkOrders"
    Title         nvarchar(150) NOT NULL,               -- «دستور کار»
    BundleCode    nvarchar(50)  NOT NULL,               -- «IT»
    IsCore        bit           NOT NULL DEFAULT 0,     -- هسته: همیشه لایسنس‌دار
    SortOrder     int           NOT NULL DEFAULT 0
);

-- ۲) لایسنس شرکت × ماژول
CREATE TABLE CompanyModuleLicenses (
    Id            int IDENTITY PRIMARY KEY,
    CompanyId     int           NOT NULL,
    Module        nvarchar(50)  NOT NULL,               -- "*" = همهٔ ماژول‌ها (قرارداد کامل)
    Status        nvarchar(20)  NOT NULL DEFAULT 'Active', -- Trial|Active|Suspended|Revoked
    StartsAt      datetime2     NOT NULL DEFAULT SYSUTCDATETIME(),
    ExpiresAt     datetime2     NULL,                   -- NULL = بی‌پایان
    GraceDays     int           NOT NULL DEFAULT 14,
    Seats         int           NULL,                   -- NULL = بی‌سقف
    LimitsJson    nvarchar(2000) NULL,                  -- {"MaxOrders":1000,"StorageGb":50}
    PlanCode      nvarchar(50)  NULL,                   -- «سالانه»، «سه‌ساله»، «آزمایشی»
    IsTrial       bit           NOT NULL DEFAULT 0,
    IssuedByName  nvarchar(150) NOT NULL DEFAULT '',
    IssuedAt      datetime2     NOT NULL DEFAULT SYSUTCDATETIME(),
    Note          nvarchar(500) NULL,
    CONSTRAINT FK_CML_Company FOREIGN KEY (CompanyId) REFERENCES SystemCompanies(Id)
);
CREATE UNIQUE INDEX IX_CML_Company_Module ON CompanyModuleLicenses(CompanyId, Module);

-- ۳) توکن امضاشده (برای نصب‌های روی‌محل / آفلاین)
CREATE TABLE CompanyLicenseTokens (
    Id            int IDENTITY PRIMARY KEY,
    CompanyId     int           NOT NULL,
    Token         nvarchar(max) NOT NULL,               -- متن امضاشده
    Fingerprint   nvarchar(64)  NOT NULL DEFAULT '',    -- SHA-256 توکن
    PayloadJson   nvarchar(max) NOT NULL,
    SignedAt      datetime2     NOT NULL,
    VerifiedAt    datetime2     NULL,
    RevokedAt     datetime2     NULL
);

-- ۴) حسابرسی تغییرات لایسنس
CREATE TABLE ModuleLicenseAudit (
    Id            bigint IDENTITY PRIMARY KEY,
    CompanyId     int           NOT NULL,
    Module        nvarchar(50)  NOT NULL,
    Action        nvarchar(30)  NOT NULL,               -- Grant|Renew|Revoke|Extend|TokenImport
    OldJson       nvarchar(max) NULL,
    NewJson       nvarchar(max) NULL,
    ByUserId      int           NOT NULL DEFAULT 0,
    ByName        nvarchar(150) NOT NULL DEFAULT '',
    At            datetime2     NOT NULL DEFAULT SYSUTCDATETIME()
);
```

**الگوی پیاده‌سازی در این مخزن:** فایل `Data/LicensingSchemaV1.cs` با همان ساختار `NotificationCompanySchemaV1`
(مسیر SQLite با `PRAGMA` + `CREATE TABLE IF NOT EXISTS`، مسیر SQL Server با `IF OBJECT_ID`) و فراخوانی در **هر دو**
شاخهٔ `DbInitializer` + مایگریشن `Migrations/2026xxxx_AddModuleLicensing.cs` + snapshot. کاتالوگ ماژول‌ها
از `RbacSeeder.ModuleActions` پر می‌شود تا دو فهرست موازی نداشته باشیم.

---

## ۴) بسته‌های تجاری (پیشنهاد نگاشت)

| کد بسته | عنوان | ماژول‌ها |
|---|---|---|
| `Core` | هستهٔ سیستم | `SystemUsers`, `SystemCompanies`, `SystemDepartments`, `Settings`, `PersonalSettings`, `Dashboards`, `MyCartable`, `MyArchive`, `MyDashboards` — **همیشه لایسنس‌دار** |
| `Warehouse` | انبار | `Products`, `Warehouses`, `InvDocs`, `InvDocTypes`, `Stock`, `GoodsIssues`, `StkSessions`, `StkBarcodes`, `Repairs` |
| `Accounting` | حسابداری و مالی | `AccAccounts`, `AccVouchers`, `AccDimensions`, `FixedAssets`, `Budgets`, `FacInvoices`, `TrsAccounts`, `TrsVouchers`, `TrsCheques`, `Moadian`, `Expenses`, `Reports` |
| `Shop` | سامانهٔ فروشگاهی | `ShopGuide`, `Orders`, `Customers`, `Suppliers`, `Parties`, `SalesVisit`, `Referrers`, `ReferrerWallets`, `ReferrerPanel` |
| `Office` | اتوماسیون اداری | `InnerLetters`, `IncomingLetters`, `OutgoingLetters`, `LetterStructures`, `MeetingMinutes`, `DocArchive`, `Email` |
| `HR` | منابع انسانی | `HrCore`, `HrPay`, `HrMain`, `HrTalent`, `LeaveRequests`, `Attendance`, `FaPay`, `FaLms`, `FaCom`, `FaAtt`, `RadisHr` |
| `IT` | فناوری اطلاعات | `ItRequests`, `WorkOrders`, `WorkProjects`, `MindFlow`, `Servers`, `Ipam`, `ItVault`, `RemoteDesktop`, `DevTeam`, `CctvCameras`, `CctvNvrs` |
| `Analytics` | گزارش و تحلیل | `ReportPages`, `ReportWorks`, `ReportStudio`, `Projects`, `ProjectAttach`, `ProjectCartable`, `Karfarmas`, `TypeFactors`, `MasterData` |
| `Collab` | همکاری | `Chat`, `AiAssistant` |

قاعده: **بسته = میان‌بُر، نه محدودیت.** جدول لایسنس همیشه ماژول‌به‌ماژول است؛ وقتی «بستهٔ HR» داده می‌شود،
۹ ردیف ساخته می‌شود. این‌طور می‌شود وسط قرارداد یک ماژول را جدا فروخت یا موقتاً برداشت، بدون تغییر مدل.

---

## ۵) نقاط اجرا — «یک بار بنویس، همه‌جا اثر بگذارد»

```
[مجوز نقش کاربر]  ∩  [لایسنس ماژول در شرکت فعال]  ∩  [شرکت فعال]
        ↓                     ↓                          ↓
   RBAC فعلی          LicensingService            ActiveCompanyMiddleware
```

### لایهٔ ۱ — سرویس مرکزی (قلب ماجرا)

```csharp
// Services/Licensing/ILicenseService.cs
public interface ILicenseService
{
    /// <summary>وضعیت مؤثر یک ماژول در یک شرکت: Active | Trial | Grace | Expired | NotLicensed | Unrestricted.</summary>
    Task<ModuleLicenseState> GetAsync(int companyId, string module, CancellationToken ct = default);

    /// <summary>فهرست ماژول‌های مجاز شرکت (مجموعهٔ کش‌شده؛ برای فیلترکردن مجوزهای ورود).</summary>
    Task<HashSet<string>> LicensedModulesAsync(int companyId, CancellationToken ct = default);

    /// <summary>آیا می‌توان روی این ماژول «نوشت»؟ (در حالت Grace/Expired فقط خواندن)</summary>
    Task<bool> CanWriteAsync(int companyId, string module, CancellationToken ct = default);
}
```

```csharp
// خدمات: کش ۶۰ ثانیه‌ای + بی‌اعتبارسازی هنگام تغییر
private static readonly MemoryCacheEntryOptions CacheOptions =
    new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) };
```

منطق وضعیت مؤثر (کوتاه و صریح):

```csharp
public static ModuleLicenseState Resolve(CompanyModuleLicense? row, DateTime nowUtc)
{
    if (row is null) return ModuleLicenseState.NotLicensed;                 // ردیفی نیست ⇒ قفل
    if (row.Status is "Revoked" or "Suspended") return ModuleLicenseState.Suspended;
    if (nowUtc < row.StartsAt) return ModuleLicenseState.Pending;
    if (row.ExpiresAt is null || nowUtc <= row.ExpiresAt) return row.IsTrial ? ModuleLicenseState.Trial : ModuleLicenseState.Active;
    if (nowUtc <= row.ExpiresAt.Value.AddDays(row.GraceDays)) return ModuleLicenseState.Grace;
    return ModuleLicenseState.Expired;
}
```

### لایهٔ ۲ — فیلتر سراسری API (توری امنیتی)

```csharp
// Infrastructure/ModuleLicenseFilter.cs  (به‌صورت GlobalFilter ثبت می‌شود)
public sealed class ModuleLicenseFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var module = ModuleOf(ctx);                 // از [ModulePermission("X")] یا map مسیر→ماژول
        if (module is null) return;                 // مسیر سیستمی/عمومی (ورود، سلامت، فایل‌های عمومی)

        var company = ActiveCompanyOf(ctx.HttpContext);   // از همان ItemKey میان‌افزار
        var state = await _licenses.GetAsync(company, module);

        if (state is ModuleLicenseState.Licensed || state is ModuleLicenseState.Trial) { await next(); return; }

        if (state is ModuleLicenseState.Grace or ModuleLicenseState.Expired && IsReadOnly(ctx))
        { await next(); return; }                    // خواندن آزاد، نوشتن بسته

        ctx.Result = new ObjectResult(new
        {
            code = "MODULE_NOT_LICENSED",
            module,
            state = state.ToString(),
            message = $"ماژول «{TitleOf(module)}» برای این شرکت فعال نیست؛ با پشتیبانی تماس بگیرید."
        })
        { StatusCode = StatusCodes.Status403Forbidden };
    }
}
```

نکتهٔ مهم: نگاشت مسیر→ماژول را از همان `ModulePermissionAttribute` بگیرید، و برای کنترلرهایی که آن را
ندارند یک جدول کوچک `routePrefix → module` نگه دارید (`/api/workorders → WorkOrders`، `/api/leave-requests → LeaveRequests` …).
حدود ۱۵ کنترلر صریح attribute دارند و بقیه از `RbacControllerBase.HasAsync` استفاده می‌کنند.

### لایهٔ ۳ — مچ‌کردن با RBAC (برای کنترلرهای با منطق دستی)

`RbacControllerBase.HasAsync` و `EffectivePermissions.GetAsync` را طوری تغییر دهید که **ابتدای کار**
ماژول را در برابر لایسنس بسنجد و در صورت نبود، `false` برگرداند:

```csharp
// RbacControllerBase.HasAsync
if (!await _licenses.IsLicensedAsync(await ActiveCompanyIdAsync(), module)) return false;
```

اثر جانبی و مهم: چون فهرست مجوزهای ورود (`LoginResponse.Permissions`) از همین سرویس ساخته می‌شود،
با فیلترکردن آن هم **منوی کلاینت خودکار مخفی می‌شود** (`ErpNavigation` با `Auth.HasModule` کار می‌کند و
هیچ تغییری در منو لازم نیست) و هم صفحه‌ها به ۴۰۳ می‌خورند.

### لایهٔ ۴ — نقاط بیرون از خط درخواست

این‌ها را فراموش نکنید؛ هر کدام می‌توانند لایسنس را دور بزنند یا نشت دهند:

| نقطه | کار لازم |
|---|---|
| SignalR (`ChatHub`, `RemoteHub`, `NotifyHub`) | بررسی لایسنس در `OnConnectedAsync` (مثل کاری که الان برای `CanSeeModuleAsync` می‌شود) |
| سرویس‌های پس‌زمینه (`WorkOrderReminderService`, `DocExpiryWatcher`, `BackupWorker`, `OfficeMachineMonitor`) | پیش از ارسال اعلان، لایسنس ماژول مربوطه را بسنجند (وگرنه برای ماژولِ نخریده اعلان می‌فرستند) |
| اندپوینت‌های `AllowAnonymous` (فالبک پوشهٔ PWA، `/api/itrequests/external/...`، `eitaa-app`) | فالبک کلاینت باید بماند (وگرنه برنامه باز نمی‌شود)؛ اندپوینت بیرونی باید لایسنس `ItRequests` را بسنجد |
| Agent / RemoteAgent | اگر `RemoteDesktop` لایسنس ندارد، ثبت‌نام agent رد شود |

---

## ۶) چرخهٔ عمر و رفتار دقیق هر حالت

| حالت | تعریف | UI | API — خواندن | API — نوشتن |
|---|---|---|---|---|
| `Unrestricted` | شرکت هیچ ردیف لایسنسی ندارد (نصب قدیمی) | بدون بنر | ✅ | ✅ |
| `Active` | در بازهٔ اعتبار | بدون بنر | ✅ | ✅ |
| `Trial` | `IsTrial=1` | بنر زرد «نسخهٔ آزمایشی — N روز» | ✅ | ✅ |
| `Grace` | تا `ExpiresAt + GraceDays` | بنر نارنجی «مهلت تمدید» | ✅ | ❌ (`403 MODULE_IN_GRACE`) |
| `Expired` | بعد از مهلت | بنر قرمز + صفحهٔ «تمدید» | ✅ **فقط خواندن** | ❌ |
| `Suspended` / `Revoked` | دستی توسط فروشنده | بنر قرمز | ✅ (برای خروج داده) | ❌ |
| `NotLicensed` | ماژول در قرارداد نیست | منو مخفی + صفحهٔ دوستانه | ❌ | ❌ |

**اصول غیرقابل‌مذاکره:**
- **هیچ داده‌ای حذف یا پنهان نمی‌شود.** فقط دسترسی نوشتن/خواندن ماژول بسته می‌شود.
- **خروجی/بکاپ همیشه آزاد است** تا مشتری در حالت انقضا گیر نکند (این هم حسن نیت است و هم پشتیبانی را نجات می‌دهد).
- **ماژول هسته (`Core`) قابل انقضا نیست**؛ فقط می‌توان تاریخ قرارداد را دید.

---

## ۷) نصب روی‌محل: توکن امضاشدهٔ آفلاین

فرض: سرور مشتری اینترنت ندارد یا نمی‌خواهید به سرور شما وصل شود.

```
payload = { company:"FAR", companyId:3, modules:["WorkOrders","ItRequests",…],
            bundles:["IT","Office"], expiresAt:"2027-03-20T00:00:00Z",
            seats:25, edition:"Pro", issued:"2026-11-08", nonce:"…" }
token   = base64url(payload) + "." + base64url(ed25519_sign(payload, vendorPrivateKey))
```

- کلید عمومی داخل برنامه (`appsettings`: `License:PublicKey`)؛ کلید خصوصی **هرگز** در مخزن یا چت نیست.
- ابزار صدور در `tools/LicenseTool` (همان الگوی `tools/GenerateVapidKeys`) با دستورهای `issue`، `verify`، `inspect`.
- توکن در `CompanyLicenseTokens` ذخیره و مالکش هم‌زمان به ردیف‌های `CompanyModuleLicenses` ترجمه می‌شود؛
  **منبع حقیقت در زمان اجرا همان ردیف‌های دیتابیس است** (سریع و قابل گزارش)، توکن برای اثبات و بازسازی نگه داشته می‌شود.
- صفحهٔ «لایسنس» در برنامه: اثر انگشت توکن، تاریخ صدور، تاریخ انقضا، فهرست ماژول‌ها، دکمهٔ «کپی توکن» برای پشتیبانی.

**صادقانه:** روی سرور خودِ مشتری، کدی که اجرا می‌شود در اختیار اوست؛ هر DRM قابل وصله است. کاری که
امضای دیجیتال می‌کند این است که «جعل لایسنس» را از «ویرایش یک عدد در دیتابیس» به «تغییر کد برنامه» ارتقا
می‌دهد و **مسئولیت را قابل‌اثبات** می‌کند. اگر روزی مدل SaaS شدید، همین payload به یک سرویس آنلاین با
heartbeat روزانه ارتقا می‌یابد و هیچ‌چیز دیگری تغییر نمی‌کند.

---

## ۸) صندلی‌ها و سقف‌های مقداری

| مفهوم | تعریف | نقطهٔ اجرا |
|---|---|---|
| **Seats** | حداکثر تعداد کاربر *فعال* که به ماژول دسترسی دارند | هنگام اعطای نقش: اگر تعداد کاربران متمایز با مجوز آن ماژول به `Seats` رسیده باشد، افزودن کاربر رد شود با پیام روشن |
| **LimitsJson** | سقف مقداری هر ماژول (`{"MaxOrdersPerMonth":1000}`) | در سرویس همان ماژول، پیش از Create؛ پیام `PLAN_LIMIT_REACHED` |
| **StorageGb** | سقف فضای پیوست‌ها | در `AttachmentsController.Upload` با `FileStore.Size` مجموع |

پیشنهاد من: **صندلی را در فاز اول اجرا نکنید.** اول ببینید مشتری واقعاً به آن نیاز دارد؛ سقف مقداری
(سفارش/ماه) برای فروش پله‌ای مفیدتر است و اجرای ساده‌تری دارد.

---

## ۹) تجربهٔ کاربری

- **منو:** آیتم ماژولِ لایسنس‌نشده اصلاً رندر نمی‌شود (خودکار، چون مجوز از سرور می‌آید).
- **دسترسی مستقیم به آدرس:** صفحهٔ دوستانه، نه ۴۰۳ خالی:

  > 🔒 **ماژول «دستور کار» در قرارداد شرکت «فروغ آریا» فعال نیست.**
  > برای فعال‌سازی با پشتیبانی تماس بگیرید — شناسهٔ شرکت: ۳
  > [کپی اطلاعات برای پشتیبانی]
- **بنر بالای برنامه** در حالت Trial/Grace/Expired، فقط برای مدیر شرکت.
- **صفحهٔ «مجوزها» در تنظیمات (فقط خواندنی برای مدیر شرکت):** جدول ماژول‌ها، وضعیت، تاریخ انقضا، توکن.
- **انتخاب‌گر شرکت** بالای صفحه: اگر شرکت دیگری اشتراک متفاوتی دارد، کنار نامش یک نشان کوچک از تعداد ماژول‌های فعال — تا کاربر «چرا اینجا منو کمتر است» گیج نشود.

---

## ۱۰) پنل مدیریت مجوز (برای شما / تیم فروش)

مسیر پیشنهادی: `/system/licenses` با دسترسی `Licensing.Manage`

1. **ماتریس شرکت × بسته** با رنگ وضعیت (سبز/زرد/نارنجی/قرمز) و جست‌وجو.
2. **اعطای گروهی:** انتخاب چند شرکت → بسته → مدت (۱/۳/۱۲/۳۶ ماه) → صدور. ردیف‌ها و audit یک‌جا ساخته می‌شوند.
3. **تمدید:** فقط `ExpiresAt` را جلو می‌برد؛ ردیف تکراری نمی‌سازد.
4. **برداشتن ماژول:** `Status=Suspended` با `Note` — داده‌ها دست‌نخورده می‌مانند.
5. **گزارش:** «شرکت‌هایی که در ۳۰ روز آینده منقضی می‌شوند» → خوراک تماس فروش.
6. **توکن:** صدور/بازبینی/کپی برای نصب آفلاین.
7. همهٔ تغییرات در `ModuleLicenseAudit` با نام کاربر.

**حساسیت:** اعطای لایسنس را پشت مجوز `Licensing.Grant` بگذارید و **به نقش Admin شرکت‌ها ندهید**؛ وگرنه هر
مشتری می‌تواند خودش را لایسنس‌دار کند. در نصب روی‌محل، یا فقط از طریق توکن امضاشده اعطا کنید، یا
دکمهٔ «اعطا» را در بیلد مشتری حذف/پنهان کنید (کامپایل با فلگ `VENDOR_BUILD`).

---

## ۱۱) سازگاری با نصب‌های موجود — مهم‌ترین ریسک

| قاعده | دلیل |
|---|---|
| شرکتِ بدون ردیف لایسنس ⇒ `Unrestricted` | هیچ مشتری فعلی با ارتقا قفل نمی‌شود |
| کلید `Licensing:Enforced` (پیش‌فرض `false`) | تا وقتی ماتریس لایسنس‌ها را پر نکرده‌اید، هیچ رفتاری تغییر نمی‌کند؛ روشن‌کردنش یک تصمیم آگاهانه است |
| `Core` همیشه معاف | حتی اگر کسی همهٔ ردیف‌ها را پاک کند، ورود/کاربران/تنظیمات کار کند |
| ردیف `Module="*"` = همهٔ ماژول‌ها | قراردادهای «کامل» یک ردیف دارند، نه ۷۶ ردیف |
| Migration فقط ADD می‌کند | هیچ ستون/جدولی حذف یا تغییر نمی‌شود؛ `Down` هم no-op |

---

## ۱۲) امنیت و ضد‌دستکاری

- **جعل از سمت کلاینت ممکن نیست:** تصمیم در سرور گرفته می‌شود؛ کلاینت فقط منو را نمایش/مخفی می‌کند.
- **دستکاری دیتابیس آشکار می‌شود:** اگر توکنی موجود باشد، hash توکن با payload دیتابیس مقایسه می‌شود و
  اختلاف در صفحهٔ مجوزها + لاگ ثبت می‌شود (وضعیت `Tampered`).
- **توکن‌ها کوتاه‌عمر نیستند ولی قابل ابطال‌اند:** `RevokedAt` + بررسی در `GetAsync`.
- **فهرست ماژول در payload امضا می‌شود**، پس مشتری نمی‌تواند با افزودن یک رشته به JSON، ماژول اضافه کند.
- **لاگ دسترسی به مجوز** (چه کسی کدام ماژول را دید/تغییر داد) کنار `AuditLog` موجود.

---

## ۱۳) کارایی

| نگرانی | راهکار |
|---|---|
| بررسی در هر درخواست | کش ۶۰ ثانیه‌ای به‌ازای `(CompanyId)` با `IMemoryCache`؛ تغییر لایسنس ⇒ `Remove(companyId)` |
| چند کوئری تازه در یک درخواست | `LicensedModulesAsync` یک‌بار در `EffectivePermissions` فراخوانی و در `HttpContext.Items` کش می‌شود |
| `ModuleLicenseFilter` روی همهٔ اکشن‌ها | هزینهٔ صفر برای مسیرهای بدون ماژول (ورود، سلامت، فایل استاتیک)؛ برای بقیه یک lookup حافظه‌ای |
| نصب بزرگ | ایندکس `(CompanyId, Module)` یکتاست؛ حجم جدول در حد چند هزار ردیف، ناچیز |

---

## ۱۴) نقشهٔ راه پیشنهادی

| فاز | محتوا | تخمین | خروجی قابل‌آزمون |
|---|---|---|---|
| **۰ — تصمیم** | تأیید شما روی سه پرسش بند ۱۶ | نیم روز | سند تصمیم |
| **۱ — ستون فقرات** | ۴ جدول + `LicensingSchemaV1` + `LicenseService` + کش + رفتار `Unrestricted` / `Enforced` + فیلتر سراسری + ماسک‌کردن در `EffectivePermissions` | ۲–۳ روز | با `Enforced=false` هیچ‌چیز عوض نمی‌شود؛ با `true` ماژول بدون لایسنس ۴۰۳ می‌دهد و از منو غیب می‌شود |
| **۲ — پنل** | ماتریس شرکت × بسته، اعطا/تمدید/تعلیق، audit، توکن دستی | ۲–۳ روز | فروش می‌تواند قرارداد را ثبت کند؛ گزارش «منقضی‌شونده‌ها» کار می‌کند |
| **۳ — آفلاین** | `tools/LicenseTool` (issue/verify)، امضا/بازبینی Ed25519، صفحهٔ «مجوزهای من» برای مشتری | ۲ روز | توکن جعلی رد می‌شود؛ توکن معتبر بدون اینترنت کار می‌کند |
| **۴ — سیاست‌ها** | صندلی، سقف مقداری، اعلان انقضا به مدیر، گزارش مصرف | ۲–۳ روز | افزودن کاربر بیش از صندلی رد می‌شود |

جمع: **۹ تا ۱۲ روز کاری** تا نسخهٔ کامل؛ **۳ روز** تا حداقلِ قابل‌عرضه (فاز ۱ + یک صفحهٔ سادهٔ اعطا).

---

## ۱۵) آزمون پذیرش

1. `Enforced=false` + بدون هیچ ردیف لایسنس → **دقیقاً** رفتار امروز (هیچ منو/اندپوینتی تغییر نمی‌کند).
2. شرکت A لایسنس `IT` دارد، شرکت B ندارد؛ کاربر هر دو شرکت است → با فعال‌بودن A منو و API دستور کار کار می‌کند، با فعال‌بودن B منو غیب است و `GET /api/workorders` پاسخ `403 MODULE_NOT_LICENSED` می‌دهد.
3. لایسنس `Office` منقضی می‌شود → روز اول مهلت: فهرست نامه‌ها باز می‌شود، ثبت نامهٔ تازه `403 MODULE_IN_GRACE`؛ بنر «N روز مهلت» دیده می‌شود.
4. بعد از مهلت: خواندن و **خروجی/بکاپ** آزاد، نوشتن بسته، هیچ داده‌ای حذف نشده.
5. `Suspended` کردن یک ماژول داده را پاک نمی‌کند؛ با `Active` برگشتن، همه‌چیز دقیقاً سر جایش است.
6. توکن با payload تغییر‌یافته (افزودن یک ماژول) رد می‌شود.
7. اعلان پس‌زمینه برای ماژول بدون لایسنس ارسال نمی‌شود.
8. تمدید تاریخ انقضا حداکثر ۶۰ ثانیه بعد اثر می‌کند (کش) — نه با ری‌استارت.

---

## ۱۶) سه تصمیم که باید شما بگیرید

1. **مدل فروش:** بسته‌ای (پیشنهاد من) یا ماژول‌به‌ماژول یا ترکیبی؟ آیا «سقف کاربر/سفارش» در قراردادهایتان هست؟
2. **آفلاین یا آنلاین:** مشتریان اینترنت دارند؟ می‌خواهید لایسنس به‌صورت توکن دستی باشد یا ترجیحاً یک صفحهٔ «فعال‌سازی» با کد کوتاه؟
3. **سختگیری در انقضا:** مهلت ۱۴ روز و سپس «فقط خواندن» کافی است، یا می‌خواهید در انقضای طولانی (بیش از ۹۰ روز) دسترسی خواندن هم بسته شود؟

---

## ۱۷) ریسک‌ها و هشدارها

| ریسک | شدت | کاهش |
|---|---|---|
| روشن‌کردن `Enforced` قبل از پرکردن ماتریس ⇒ قطع دسترسی مشتری | بالا | `Enforced` پیش‌فرض خاموش + پیش‌نمایش «گزارش اثر» (چه کسی فردا چه چیزی را از دست می‌دهد) |
| ادمین شرکت خودش لایسنس صادر کند | بالا | `Licensing.Grant` جدا + حذف دکمه در بیلد مشتری |
| لایسنس روی جدول‌های چندشرکتی با فیلتر سراسری تداخل کند | متوسط | بدون `ICompanyOwned`؛ خواندن همیشه با `CompanyId` صریح |
| فراموش‌کردن Hub/سرویس پس‌زمینه | متوسط | چک‌لیست بند ۵ + تست پذیرش ۷ |
| کندی صفحه در نصب با ۵۰ شرکت | پایین | کش + ایندکس یکتا |
| مشتری «قفل‌شدن» را نقض قرارداد بداند | متوسط | انقضا = فقط خواندن + خروجی آزاد + بنر ۳۰ روزه قبل از انقضا |
