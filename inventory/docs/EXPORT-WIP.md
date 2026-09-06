# ماژول ۵ — خروجی PDF و Excel (در حال ساخت)

> این فایل وضعیت نیمه‌کاره‌ی ماژول ۵ را ثبت می‌کند تا ادامه‌ی کار بدون
> بازکشف دوباره انجام شود. پس از تکمیل ماژول، این فایل با `EXPORT.md` جایگزین می‌شود.

## وضعیت فعلی

| بخش | فایل | وضعیت |
|---|---|---|
| مدل توصیف گزارش | `src/Inventory.Api/Services/Export/ExportSpec.cs` | ✅ نوشته شد — **کامپایل نشده** |
| موتور اکسل | `src/Inventory.Api/Services/Export/ExcelWriter.cs` | ✅ نوشته شد — **کامپایل نشده** |
| موتور PDF | `src/Inventory.Api/Services/Export/PdfWriter.cs` | ✅ نوشته شد — **کامپایل نشده** |
| سرویس گزارش‌ها | `Services/Export/ExportService.cs` | ⬜ نوشته نشده |
| کنترلر | `Controllers/Export/ExportController.cs` | ⬜ نوشته نشده |
| کلاینت | `Services/IExportApi.cs` + `ExportButtons.razor` | ⬜ نوشته نشده |
| مستندات | `docs/EXPORT.md` | ⬜ نوشته نشده |

**هیچ‌کدام از سه فایل نوشته‌شده هنوز کامپایل نشده‌اند** — محیط ساخت در میانه‌ی کار
از کار افتاد. اولین اقدام در ادامه: `dotnet build src/Inventory.Api`.

## یافته‌های مهم محیط (وقت‌گیر — دوباره کشف نشود)

- **QuestPDF 2024.12.3 و ClosedXML 0.102.2 از قبل در `Inventory.Api.csproj` هستند.** نیازی به افزودن پکیج نیست.
- **فونت فارسی از قبل موجود است:** `src/Inventory.Api/Resources/fonts/Vazirmatn-{Regular,Bold}.ttf`
  و در csproj با `CopyToOutputDirectory=PreserveNewest` کپی می‌شود.
- الگوی ثبت فونت و RTL در `Services/System/SystemInfoPdf.cs` و
  `Services/Office/Outgoing/OutgoingLetterPrintService.cs` (خط ۱۸۴: `page.ContentFromRightToLeft()`).
- الگوی اکسل RTL در `Services/Catalog/InventoryService.cs:724` (`ws.RightToLeft = true`).
- **دانلود سمت کلاینت از قبل آماده است:** تابع `window.downloadBlob(fileName, contentType, base64)`
  در `wwwroot/js/project-interop.js:228` — حتی MIME اکسل را درست ست می‌کند.
- **`IApiClient` متد دریافت فایل باینری ندارد** — باید `GetFileAsync` اضافه شود
  (الگوی `AddAuth` + `Url()` در `Services/ApiClient.cs`).

### ⚠️ تله‌ی محیط ساخت

نصب SDK با `/tmp` (tmpfs ۹۹۳ مگابایتی) **خراب می‌شود** و سه فایل زیر صفر بایت
استخراج می‌شوند که خطای گمراه‌کننده‌ی `CS0009 Image is too small` می‌دهند:

```
packs/Microsoft.NETCore.App.Ref/8.0.30/ref/net8.0/System.Security.AccessControl.dll
packs/Microsoft.NETCore.App.Ref/8.0.30/ref/net8.0/System.Security.Cryptography.Cng.dll
packs/Microsoft.NETCore.App.Ref/8.0.30/ref/net8.0/System.Security.Principal.Windows.dll
```

**راه‌حل قطعی — همیشه `TMPDIR` را جابه‌جا کنید:**

```bash
mkdir -p /home/user/tmpdl
export TMPDIR=/home/user/tmpdl
rm -rf /home/user/.dotnet8
bash /home/user/dotnet-install.sh --channel 8.0 --install-dir /home/user/.dotnet8 --no-path
```

بررسی سلامت نصب:
```bash
ls -la /home/user/.dotnet8/packs/Microsoft.NETCore.App.Ref/8.0.30/ref/net8.0/ | awk '$5==0'
# باید خروجی خالی باشد
```

## معماری تصمیم‌گرفته‌شده

هر گزارش **یک بار** به شکل `ExportSpec` توصیف می‌شود؛ دو موتور مستقل آن را رندر می‌کنند:

```
        داده‌های سرویس‌های موجود
                  │
                  ▼
            ExportSpec            ← منطق هر گزارش فقط یک جا
         (ستون‌ها، سطرها، جمع،
          فیلترها، خلاصه)
                  │
        ┌─────────┴─────────┐
        ▼                   ▼
   ExcelWriter          PdfWriter
   (ClosedXML)          (QuestPDF)
   عدد واقعی            ارقام فارسی
   AutoFilter           RTL + وزیرمتن
   FreezePanes          سربرگ تکرارشونده
        │                   │
        ▼                   ▼
      .xlsx               .pdf
```

**اصل کلیدی:** در اکسل اعداد به‌صورت **عدد واقعی** نوشته می‌شوند (نه متن فارسی) تا
کاربر بتواند Sum و فیلتر و نمودار بسازد؛ ارقام فارسی فقط در PDF که خروجی چاپی است.

### قابلیت‌های پیاده‌شده در دو موتور

| ویژگی | اکسل | PDF |
|---|---|---|
| راست‌به‌چپ | `ws.RightToLeft` | `page.ContentFromRightToLeft()` |
| سربرگ تکرارشونده | `SetRowsToRepeatAtTop` | `table.Header(...)` |
| فریز سربرگ | `FreezeRows` | — |
| فیلتر خودکار | `SetAutoFilter` | — |
| شماره صفحه | `PageSetup` | `CurrentPageNumber/TotalPages` |
| سطر جمع خودکار | ✅ `EffectiveTotalRow()` | ✅ همان |
| سبک سطر (کسری/اضافی) | رنگ پس‌زمینه | رنگ پس‌زمینه |
| تورفتگی درختی | `Alignment.Indent` | `PaddingRight` |
| ستون فقط-اکسل | ✅ | حذف می‌شود (`ExcelOnly`) |

## کارهای باقی‌مانده

1. **کامپایل سه فایل موجود** و رفع خطاهای احتمالی API کتابخانه‌ها.
2. `ExportService` — ساخت `ExportSpec` برای گزارش‌ها با **فراخوانی سرویس‌های موجود**
   (نه کوئری تکراری): کاردکس، موجودی، فهرست کالا، اسناد انبار، دفتر کل، روزنامه،
   تراز آزمایشی، فاکتورها، خلاصه فروش، اسناد خزانه، چک‌ها، گردش خزانه، مغایرت انبارگردانی.
3. `ExportController` — `GET api/exp/report/{key}?format=pdf|xlsx&…` با RBAC روی
   عملیات `Export` هر ماژول (این عملیات از قبل در `RbacSeeder` برای همه‌ی ماژول‌ها تعریف شده).
4. `IApiClient.GetFileAsync` + `IExportApi` + کامپوننت مشترک `<ExportButtons Report="…" Query="…" />`.
5. افزودن دکمه‌ها به صفحات گزارش موجود + صفحه‌ی «مرکز خروجی».
6. `docs/EXPORT.md`.

### ایده‌ی بعدی (خارج از دامنه‌ی فعلی)

`DocumentPdf` برای چاپ **تک‌سند**: فاکتور رسمی، سند انبار، سند حسابداری، سند خزانه —
با سربرگ شرکت، مشخصات طرف حساب، اقلام، جمع، مبلغ به حروف و محل امضا.
این شکل با گزارش جدولی فرق دارد و موتور جداگانه می‌خواهد.
