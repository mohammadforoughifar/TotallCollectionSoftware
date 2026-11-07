# طرح ارتقای ماژول «ماشین‌های اداری» + پایش SNMP

> وضعیت: پیشنهاد طراحی (هنوز پیاده‌سازی نشده)
> فایل‌های فعلی: `Entities/Office/OfficeMachine.cs` · `Controllers/Office/OfficeMachinesController.cs` · `Client/Pages/Office/OfficeMachines.razor`

---

## ۱. وضعیت فعلی و ایرادهای موجود

ماژول الان در حد یک «دفترچه ثبت» است: مدل، سریال، محل، تاریخ نصب، IP یا سیستم متصل، و سابقهٔ تعمیر.

| # | ایراد | محل | شدت |
|---|------|-----|-----|
| B1 | `MachineDto` در `GET` فیلدهای `ConnectionType`، `IpAddress`، `LinkedSystemInfoId`، `LinkedSystemLabel` را برنمی‌گرداند ← هنگام **ویرایش**، IP خالی نمایش داده می‌شود و کاربر مجبور است دوباره واردش کند (سیستم متصل کابلی هم از دست می‌رود) | Controller خط `Get` | بالا |
| B2 | متغیر `searchText` وجود دارد ولی **هیچ کادر جستجویی در UI نیست** | Razor | متوسط |
| B3 | API هزینه‌ها (`/costs`) کامل است ولی **هیچ UI ندارد**؛ `TotalCost` هم نمایش داده نمی‌شود | Razor | متوسط |
| B4 | حذف ماشین **بدون تأیید** است و تمام سابقهٔ تعمیر/هزینه را هم پاک می‌کند (Hard delete) | Razor + Controller | بالا |
| B5 | `GoneDate/ReturnDate` هم روی خود ماشین هست هم روی تعمیر — تکراری و گیج‌کننده | Entity | کم |
| B6 | مجوز زیر ماژول `SystemUsers` است، نه ماژول مستقل | Controller | کم |
| B7 | `CompanyId` ندارد (برخلاف `ManagedServer`/`ItCredential`) — در حالت چندشرکتی همهٔ ماشین‌ها با هم قاطی‌اند | Entity | متوسط |
| B8 | `POST` فیلدهای اتصال را نرمال نمی‌کند (`PUT` می‌کند) | Controller | کم |

---

## ۲. چشم‌انداز: «مرکز مدیریت ماشین‌های اداری»

شش قابلیت اصلی:

1. **داشبورد زنده:** کارت هر دستگاه با وضعیت آنلاین/آفلاین، درصد تونر (CMYK)، کاغذ سینی‌ها، خطای فعلی و متن LCD دستگاه.
2. **پایش SNMP:** خواندن خودکار پیام‌ها، شمارندهٔ صفحه، سطح مواد مصرفی، سریال، MAC و Uptime.
3. **کشف خودکار (Auto-Discovery):** اسکن یک Subnet (از IPAM فعلی) و شناسایی پرینترها با SNMP، سپس افزودن با یک کلیک.
4. **هشدار و تیکت خودکار:** مثلاً «تونر مشکی زیر ۱۰٪»، «کاغذ گیر کرده»، «دستگاه ۳۰ دقیقه آفلاین» ← اعلان (Push/بله/پیامک) و ساخت **دستور کار** (WorkOrder) به‌صورت خودکار.
5. **انبار مواد مصرفی:** تعریف کارتریج سازگار با هر مدل، اتصال به ماژول انبار `inventory`، پیش‌بینی تاریخ اتمام تونر.
6. **گزارش‌ها و هزینه:** تیراژ ماهانه هر دستگاه/واحد، هزینه هر صفحه (CPP)، هزینهٔ کل مالکیت (TCO)، قرارداد سرویس و گارانتی.

---

## ۳. مدل داده (Entity) پیشنهادی

### ۳.۱ ارتقای `OfficeMachine`
```csharp
public class OfficeMachine
{
    // --- فیلدهای فعلی حفظ می‌شوند ---
    public int Id { get; set; }
    public int CompanyId { get; set; }                       // B7
    [MaxLength(30)]  public string DeviceType { get; set; } = "Printer";
                     // Printer | MFP | Copier | Scanner | Fax | Plotter | LabelPrinter
    [MaxLength(80)]  public string? Brand { get; set; }      // HP, Canon, Kyocera, Sharp, Ricoh...
    [MaxLength(150)] public string Model { get; set; } = "";
    [MaxLength(80)]  public string? AssetTag { get; set; }   // برچسب اموال / QR
    public bool IsColor { get; set; }
    public bool IsDuplex { get; set; }
    public int? DepartmentId { get; set; }                   // واحد سازمانی
    public int? ResponsibleUserId { get; set; }              // مسئول/تحویل‌گیرنده
    [MaxLength(20)]  public string Status { get; set; } = "InService";
                     // InService | InRepair | Standby | Retired  (جایگزین IsActive + GoneDate)
    [MaxLength(30)]  public string? MacAddress { get; set; }
    [MaxLength(100)] public string? Hostname { get; set; }
    [MaxLength(100)] public string? FirmwareVersion { get; set; }

    // --- خرید/گارانتی/قرارداد ---
    public DateTime? PurchaseDate { get; set; }
    public decimal? PurchasePrice { get; set; }
    public DateTime? WarrantyUntil { get; set; }
    [MaxLength(150)] public string? ServiceVendor { get; set; }
    public DateTime? ServiceContractUntil { get; set; }

    // --- SNMP ---
    public bool SnmpEnabled { get; set; }
    [MaxLength(5)] public string SnmpVersion { get; set; } = "v2c"; // v1 | v2c | v3
    public int SnmpPort { get; set; } = 161;
    public int? SnmpCredentialId { get; set; }  // ← ItCredential (Category = "Snmp") — رمزنگاری با CredentialVaultCrypto
    public int PollIntervalMinutes { get; set; } = 10;
    public int? SnmpProfileId { get; set; }     // پروفایل OID اختصاصی برند

    // --- آخرین وضعیت (کش برای لیست سریع) ---
    public DateTime? LastPollAt { get; set; }
    public DateTime? LastSeenOnlineAt { get; set; }
    [MaxLength(20)] public string OnlineState { get; set; } = "Unknown"; // Online | Offline | Warning | Error | Unknown
    public long? LastPageCount { get; set; }
    public int? LowestSupplyPercent { get; set; }
    [MaxLength(300)] public string? LastStatusText { get; set; }        // متن LCD / خطای فعلی
}
```

### ۳.۲ جدول‌های جدید
| جدول | کاربرد | فیلدهای کلیدی |
|------|--------|---------------|
| `OfficeMachineSupply` | وضعیت فعلی هر ماده مصرفی | MachineId, Index, Type (Toner/Drum/Waste/Fuser), Color, Description, Level, MaxCapacity, Percent, UpdatedAt |
| `OfficeMachineTray` | سینی‌های کاغذ | MachineId, Index, Name, MediaName, CurrentLevel, MaxCapacity, Status |
| `OfficeMachineCounterSnapshot` | تاریخچهٔ شمارنده (روزانه) برای گزارش تیراژ | MachineId, TakenAt, TotalPages, MonoPages?, ColorPages?, ScanCount?, CopyCount? |
| `OfficeMachineSupplySnapshot` | تاریخچهٔ درصد تونر ← پیش‌بینی اتمام | MachineId, SupplyIndex, TakenAt, Percent |
| `OfficeMachineEvent` | رویدادها و هشدارها | MachineId, OccurredAt, Severity (Info/Warning/Critical), Code (LowToner, Jam, Offline, DoorOpen…), Message, Source (Poll/Trap/User), IsResolved, ResolvedAt, WorkOrderId? |
| `OfficeMachineSnmpProfile` | OIDهای اختصاصی هر برند (قابل تنظیم بدون کامپایل) | Brand, ModelPattern, ColorCounterOid, MonoCounterOid, ScanCounterOid, … |
| `OfficeMachineAlertRule` | قواعد هشدار | Code, Threshold (مثلاً ۱۰٪), OfflineMinutes, NotifyUserIds/RoleIds, AutoCreateWorkOrder, CooldownMinutes |
| `OfficeConsumable` | تعریف کارتریج/درام سازگار + اتصال به کالای انبار | Title, PartNumber, Color, YieldPages, ProductId (انبار), CompatibleModels |
| `OfficeMachineCost` / `OfficeMachineRepair` | فعلی — حفظ می‌شوند | + `Category` برای هزینه (Toner, Paper, Service, Part) |

> حذف ماشین ← **Soft Delete** با `Status = Retired` (حل B4).

---

## ۴. لایهٔ SNMP

### ۴.۱ کتابخانه
**`Lextm.SharpSnmpLib`** (لایسنس MIT، کاملاً managed، پشتیبانی از v1/v2c/v3 با Auth SHA/MD5 و Privacy AES/DES، مناسب net8 روی Windows Server و Linux).
```xml
<PackageReference Include="Lextm.SharpSnmpLib" Version="12.*" />
```
اگر سرور به NuGet دسترسی ندارد، مثل `TaxCollectData.Library` در فید محلی `inventory/packages` قرار می‌گیرد.

### ۴.۲ OIDهای استاندارد (مستقل از برند)
از **Printer-MIB (RFC 3805)** و **Host-Resources-MIB (RFC 2790)** — تقریباً همهٔ پرینترهای شبکه‌ای HP، Canon، Kyocera، Ricoh، Sharp، Xerox، Brother و Konica پشتیبانی می‌کنند:

| اطلاعات | OID | نوع |
|---------|-----|-----|
| توضیح سیستم (برند/مدل/فریم‌ور) | `1.3.6.1.2.1.1.1.0` sysDescr | GET |
| شناسهٔ سازنده | `1.3.6.1.2.1.1.2.0` sysObjectID | GET |
| Uptime | `1.3.6.1.2.1.1.3.0` | GET |
| نام / محل دستگاه | `1.3.6.1.2.1.1.5.0` / `1.3.6.1.2.1.1.6.0` | GET |
| مدل دستگاه | `1.3.6.1.2.1.25.3.2.1.3.1` hrDeviceDescr | GET |
| وضعیت دستگاه (running/warning/down) | `1.3.6.1.2.1.25.3.2.1.5.1` hrDeviceStatus | GET |
| وضعیت چاپگر (idle/printing/warmup) | `1.3.6.1.2.1.25.3.5.1.1.1` hrPrinterStatus | GET |
| **پرچم‌های خطا** (کاغذ کم/تمام، تونر کم/تمام، در باز، گیر کاغذ، آفلاین، نیاز به سرویس…) | `1.3.6.1.2.1.25.3.5.1.2.1` hrPrinterDetectedErrorState | GET (Bit-string) |
| **سریال** | `1.3.6.1.2.1.43.5.1.1.17.1` prtGeneralSerialNumber | GET |
| **شمارندهٔ کل صفحات** | `1.3.6.1.2.1.43.10.2.1.4.1.1` prtMarkerLifeCount | GET |
| **مواد مصرفی** — نوع / توضیح / ظرفیت / سطح | `1.3.6.1.2.1.43.11.1.1.{5,6,8,9}` prtMarkerSupplies* | WALK |
| رنگ هر تونر | `1.3.6.1.2.1.43.12.1.1.4` prtMarkerColorantValue | WALK |
| سینی‌ها — ظرفیت / سطح فعلی / نام | `1.3.6.1.2.1.43.8.2.1.{9,10,13}` prtInput* | WALK |
| **هشدارهای فعال دستگاه** — شدت / کد / شرح | `1.3.6.1.2.1.43.18.1.1.{2,7,8}` prtAlert* | WALK |
| متن نمایشگر LCD | `1.3.6.1.2.1.43.16.5.1.2.1` prtConsoleDisplayBufferText | WALK |
| MAC | `1.3.6.1.2.1.2.2.1.6` ifPhysAddress | WALK |

**محاسبهٔ درصد تونر:** `Percent = Level * 100 / MaxCapacity`. مقادیر ویژه: `-3` یعنی «مقداری باقی‌مانده» (OK)، `-2` نامشخص، `-1` other.

**بیت‌های `hrPrinterDetectedErrorState`:** بایت اول از MSB: `lowPaper(0) noPaper(1) lowToner(2) noToner(3) doorOpen(4) jammed(5) offline(6) serviceRequested(7)` · بایت دوم: `inputTrayMissing(8) outputTrayMissing(9) markerSupplyMissing(10) outputNearFull(11) outputFull(12) inputTrayEmpty(13) overduePreventMaint(14)`.

**شمارندهٔ رنگی/سیاه‌وسفید و اسکن** در MIB استاندارد نیست و OID آن برای هر برند فرق دارد ← در جدول `OfficeMachineSnmpProfile` قابل‌تنظیم است و در کد هاردکد نمی‌شود.

### ۴.۳ سرویس‌ها (منطبق با الگوی موجود `ServerMonitoringWorker`)
```
Services/Office/
 ├─ SnmpClient.cs                  ← Get / Walk / BulkWalk ، v1/v2c/v3 ، Timeout ، Retry
 ├─ PrinterSnmpReader.cs           ← OIDها را به PrinterSnapshot (مدل تمیز) تبدیل می‌کند
 ├─ OfficeMachineMonitorWorker.cs  ← BackgroundService: دستگاه‌های موعدرسیده را موازی (SemaphoreSlim=16) پایش می‌کند
 ├─ OfficeMachineAlertEngine.cs    ← مقایسه با Rule ← Event ← Notify / WorkOrder (با Cooldown و Dedup)
 ├─ OfficeMachineDiscovery.cs      ← اسکن رنج IP: Ping ← SNMP sysObjectID ← تشخیص پرینتر (prtGeneral موجود)
 └─ SnmpTrapListener.cs (اختیاری) ← گوش‌دادن UDP 162 برای Trap فوری
```
- اعلان‌ها از `INotifyService` و `PushQueue` موجود ارسال می‌شوند (مثل `ServerAlertNotificationService`).
- WorkOrder خودکار با `SourceModule = "OfficeMachine"` و `SourceId = MachineId` ساخته می‌شود.
- Community/کاربر v3 در **گاوصندوق IT (`ItCredential`)** با `Category = "Snmp"` ذخیره می‌شود؛ community هیچ‌وقت به‌صورت plain در جدول ماشین قرار نمی‌گیرد.
- تنظیمات در `appsettings.json`:
```json
"OfficeMachineMonitoring": {
  "Enabled": true,
  "SchedulerCheckSeconds": 60,
  "MaxParallel": 16,
  "SnmpTimeoutMs": 2000,
  "SnmpRetries": 1,
  "SnapshotHourlyRetentionDays": 30,
  "TrapListener": { "Enabled": false, "Port": 162 }
}
```

### ۴.۴ نکات امنیتی و شبکه
- روی دستگاه‌ها SNMP را **فقط Read-Only** کنید و community پیش‌فرض `public` را عوض کنید. در صورت پشتیبانی دستگاه، **SNMPv3 (authPriv: SHA + AES)** را ترجیح دهید.
- ACL دستگاه را فقط به IP سرور برنامه محدود کنید.
- فایروال سرور: خروجی UDP 161، ورودی UDP 162 (فقط اگر Trap فعال باشد).
- این ماژول **هیچ عمل SET انجام نمی‌دهد** (فقط خواندن) — مثل پایش سرورها.

---

## ۵. API پیشنهادی
```
GET    /api/OfficeMachines?q=&status=&type=&dept=&state=      ← لیست + وضعیت زنده (رفع B1)
GET    /api/OfficeMachines/dashboard                          ← KPIها: آنلاین/آفلاین/هشدار/تونر کم/تیراژ ماه
GET    /api/OfficeMachines/{id}                               ← پروفایل کامل + supplies + trays
POST   /api/OfficeMachines/{id}/poll                          ← پایش فوری (دکمه «به‌روزرسانی»)
POST   /api/OfficeMachines/snmp/test                          ← تست اتصال قبل از ذخیره {ip, version, credentialId}
POST   /api/OfficeMachines/discover   {subnetId | cidr}       ← کشف خودکار ← لیست کاندیدها
POST   /api/OfficeMachines/discover/import                    ← افزودن کاندیدهای انتخاب‌شده
GET    /api/OfficeMachines/{id}/events                        ← رویدادها
POST   /api/OfficeMachines/events/{eventId}/resolve
GET    /api/OfficeMachines/{id}/counters?from=&to=            ← نمودار تیراژ
GET    /api/OfficeMachines/reports/usage?from=&to=&groupBy=dept|machine
GET    /api/OfficeMachines/reports/cost                        ← CPP و TCO
CRUD   /api/OfficeMachines/alert-rules · /snmp-profiles · /consumables
```
ماژول مجوز مستقل: `OfficeMachines` با اکشن‌های `Read/Create/Update/Delete/Poll/Configure` (رفع B6) — در `RbacSeeder` به Admin داده می‌شود.

---

## ۶. طراحی UI

صفحه `/office-machines` به ۴ تب تقسیم می‌شود:

1. **داشبورد:** کارت‌های KPI (کل / آنلاین / آفلاین / هشدار / تونر کم / تیراژ ماه) + گرید کارت دستگاه‌ها با نوار تونر CMYK + فیلتر سریع (همه، آفلاین، هشدار دار، تونر کم) + جستجو (رفع B2) + سوئیچ نمای کارت/جدول.
2. **پروفایل دستگاه (Drawer کناری یا صفحهٔ جدا):**
   - سربرگ: مدل، IP (لینک به Web UI دستگاه)، وضعیت زنده، متن LCD، دکمهٔ «پایش فوری»
   - زیرتب‌ها: نمای کلی · مواد مصرفی و سینی‌ها · شمارنده و نمودار · رویدادها · تعمیرات · هزینه‌ها (رفع B3) · قرارداد و گارانتی · تنظیمات SNMP
3. **هشدارها:** لیست رویدادهای باز همهٔ دستگاه‌ها، «رسیدگی شد»، «ساخت دستور کار».
4. **گزارش‌ها:** تیراژ ماهانه به تفکیک واحد، رتبه‌بندی پرمصرف‌ها، هزینه هر صفحه، خروجی Excel (ClosedXML موجود) / PDF (QuestPDF موجود).

**فرم افزودن، به‌صورت Wizard:** ① IP را وارد کنید ← ② «تست SNMP» ← فیلدهای مدل/سریال/MAC/برند **خودکار پر می‌شوند** ← ③ محل/واحد/مسئول ← ④ قرارداد و گارانتی.

نمونهٔ بصری: فایل `office-machines-mockup.html`.

---

## ۷. فازبندی پیاده‌سازی

| فاز | محتوا | تخمین |
|-----|-------|-------|
| **۰. رفع باگ‌ها** | B1 تا B4 و B8 — بدون تغییر دیتابیس | ۰٫۵ روز |
| **۱. هستهٔ SNMP** | پکیج، `SnmpClient`، `PrinterSnmpReader`، فیلدهای SNMP + Supply/Tray، Migration، endpoint تست و پایش فوری، Worker | ۲ تا ۳ روز |
| **۲. UI حرفه‌ای** | داشبورد کارت‌ها، پروفایل دستگاه، Wizard، UI هزینه‌ها | ۲ تا ۳ روز |
| **۳. هشدار و تیکت** | Event، AlertRule، Notify، WorkOrder خودکار، Cooldown | ۱٫۵ روز |
| **۴. کشف خودکار** | اسکن Subnet از IPAM + Import | ۱ روز |
| **۵. گزارش و انبار** | Snapshot شمارنده، نمودارها، CPP/TCO، اتصال کارتریج به انبار، پیش‌بینی اتمام تونر | ۲ تا ۳ روز |
| **۶. اختیاری** | Trap Listener، پروفایل OID برندها، اپ اندروید (QR روی دستگاه ← ثبت خرابی) | — |

### سازگاری با داده‌های فعلی
- Migration فقط ستون/جدول اضافه می‌کند؛ `IsActive=false` به `Status=Retired` و `IsActive=true` به `InService` نگاشت می‌شود.
- `IpamService` فعلی (که ماشین‌ها را با Source=OfficeMachine نشان می‌دهد) بدون تغییر کار می‌کند و می‌تواند از `OnlineState` هم استفاده کند.
