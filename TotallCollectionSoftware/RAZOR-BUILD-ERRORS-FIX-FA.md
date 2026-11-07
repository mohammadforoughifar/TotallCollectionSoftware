# رفع خطاهای کامپایل Razor در پروژهٔ Inventory.Client — ۱۴۰۵/۰۷/۲۹ (2026-10-22)

این سند ریشه‌یابی و اصلاح خطاهای کامپایل گزارش‌شده از سمت کاربر را توضیح می‌دهد.
همهٔ خطاها در پروژهٔ `inventory/src/Inventory.Client` بودند و همگی اکنون رفع شده‌اند.

---

## خطای ۱) `CS1026` / `CS1002` / `CS1056` / `CS1525` / `CS1513` و `CS7036`
### فایل‌ها
- `Pages/Office/Letters/LetterCartable.razor`
- `Pages/Office/Letters/Outgoing/OutgoingLetterCartable.razor`
- `Pages/Office/Letters/Outgoing/OutgoingLetterView.razor`

(خطاهای متناظر در فایل‌های تولیدی `obj/.../RazorSourceGenerator/*_razor.g.cs` پیامدِ همین ریشه بودند، نه خطای مستقل.)

### ریشه
در صفت‌های HTML از کوتیشنِ escape‌شده با بک‌اسلش استفاده شده بود:

```razor
<button @onclick="async () => { await PrintRow(ll, \"A4\", false); }">
```

**در Razor، بک‌اسلش داخل صفت HTML معتبر نیست.** توکنایزرِ HTML مقدار صفت را در
**اولین** `"` بعدی می‌بندد؛ پس مقدار صفت فقط تا `await PrintRow(ll, \` خوانده می‌شد و
بقیهٔ خط به صفت‌های نامعتبر تبدیل می‌گشت. نتیجه:

- `CS1056 Unexpected character '\'` — بک‌اسلش در کد C# که به Razor رسیده بود.
- `CS1026 ) expected` / `CS1002 ; expected` / `CS1513 } expected` — عبارت ناقص.
- `CS1525 Invalid expression term ''` — آرگومان‌های رشته‌ای از دست رفته.
- `CS7036 There is no argument given that corresponds to the required parameter 'size'` — چون
  آرگومان‌های `"A4"`/`"A5"` در C# قابل‌تشخیص نبودند، متد `PrintSelected(string size, bool)`
  بدون آرگومان `size` دیده می‌شد.

### اصلاح
رشته‌های `A4`/`A5` به‌صورت ثابت در بلوک `@code` تعریف شدند و در صفت‌ها فقط نام ثابت آمد:

```razor
<button @onclick="async () => { await PrintRow(ll, SizeA4, false); }">
```

```csharp
private const string SizeA4 = "A4";
private const string SizeA5 = "A5";
```

> **نکتهٔ مهم برای آینده:** در Razor داخل صفت HTML نمی‌توان رشتهٔ C# نوشت.
> گزینه‌های درست: ثابت (`const`)، متد بدون پارامتر، یا صفت با کوتیشن تکی (`@onclick='...'`).
> استفاده از `&quot;` هم درست نیست، چون Razor داخل **مقدار صفتِ کدی** موجودیت‌های HTML را
> رمزگشایی نمی‌کند (رمزگشایی فقط در محتوای مارک‌آپ و صفت‌های متنی انجام می‌شود).

---

## خطای ۲) `CS1061` — `IBrowserFile` فاقد `GetMultipleFiles`
### فایل
- `Pages/System/SystemCompany.razor`

### ریشه
```csharp
var file = e.File.GetMultipleFiles(1).FirstOrDefault();
```

`GetMultipleFiles` **متد نمونهٔ `InputFileChangeEventArgs`** است، نه متدِ `IBrowserFile`.
رابط `IBrowserFile` فقط `Name`، `Size`، `ContentType`، `LastModified` و `OpenReadStream` دارد.
بنابراین `e.File.GetMultipleFiles(...)` هیچ‌گاه کامپایل نمی‌شد.

### اصلاح
هم‌راستا با بقیهٔ پروژه (که همه‌جا `e.GetMultipleFiles(...)` می‌نویسند):
```csharp
var file = e.GetMultipleFiles(1).FirstOrDefault();
```

---

## خطای ۳) `CS1061` — `IOutgoingLetterService` فاقد `GetActiveCompanyAsync`
### فایل
- `Pages/Office/Letters/Outgoing/ComposeOutgoingModal.razor`
- `Services/OutgoingLetterServices.cs`

### ریشه
متد `GetActiveCompanyAsync()` در کلاس `OutgoingLetterService` **پیاده‌سازی** شده بود،
ولی در **اینترفیس** `IOutgoingLetterService` اعلام نشده بود. چون کامپوننت
`ComposeOutgoingModal` سرویس را از طریق اینترفیس تزریق می‌کند (`@inject IOutgoingLetterService OutgoingSvc`)،
فراخوانی کامپایل نمی‌شد.

### اصلاح
اعلان متد به اینترفیس اضافه شد (پیاده‌سازی دست‌نخورده باقی ماند):
```csharp
/// <summary>شرکت فعال کاربر جاری — برای انتخاب سربرگ نامه صادره</summary>
Task<LetterCompanyDto?> GetActiveCompanyAsync();
```

> بررسی شد که هیچ متد دیگری از این اینترفیس در جای دیگری خارج از اعلان‌ها فراخوانی نشود.

---

## خطای ۴) `RZ1008` — مارک‌آپ در بدنهٔ یک‌خطیِ `if`
### فایل
- `Pages/System/SystemCompany.razor`

### ریشه
```razor
@if (!string.IsNullOrWhiteSpace(letterheadUploadError))
    <div class="text-danger small mt-1">@letterheadUploadError</div>
```

در Razor، بدنهٔ یک‌خطیِ `if` **نمی‌تواند** مارک‌آپ داشته باشد؛ باید داخل `{ }` باشد.

### اصلاح
```razor
@if (!string.IsNullOrWhiteSpace(letterheadUploadError))
{
    <div class="text-danger small mt-1">@letterheadUploadError</div>
}
```

---

## مورد جانبی (خطای کامپایل نبود، ولی اشکال واقعی بود)
### فایل
- `Shared/AttachmentBox.razor`

در `title` یک صفت HTML ساده، از escape یونیکدِ C# استفاده شده بود:
`title="\u0686\u0627\u067e \u0628\u0627 ..."`. چون این یک صفت متنی است (نه کدی)،
Razor آن را عیناً در HTML می‌نوشت و کاربر به‌جای متن فارسی، رشتهٔ خام
`\u0686\u0627\u067e ...` را در tooltip می‌دید. به متن واقعی فارسی تبدیل شد.

---

## بررسی‌های انجام‌شده روی کل پروژه
- اسکن همهٔ فایل‌های `.razor` برای `\"` در **مارک‌آپ** (نه داخل `@code`) → **۰ مورد باقی‌مانده**.
  (۹۰ مورد باقی‌مانده همگی داخل بلوک‌های `@code` و رشته‌های C# معتبر هستند.)
- اسکن همهٔ فایل‌های `.razor` برای الگوی `RZ1008` (کنترل‌فلو بدون `{}` و بلافاصله مارک‌آپ) → **۰ مورد**.
- بررسی تمام فراخوانی‌های `IOutgoingLetterService` در کل پروژه → همه در اینترفیس اعلام شده‌اند.
- بررسی همهٔ صفت‌های HTML برای escape یونیکدِ C# → تنها موردِ `AttachmentBox` که اصلاح شد.

---

## فایل‌های تغییرکرده
- `inventory/src/Inventory.Client/Pages/Office/Letters/LetterCartable.razor`
- `inventory/src/Inventory.Client/Pages/Office/Letters/Outgoing/OutgoingLetterCartable.razor`
- `inventory/src/Inventory.Client/Pages/Office/Letters/Outgoing/OutgoingLetterView.razor`
- `inventory/src/Inventory.Client/Pages/System/SystemCompany.razor`
- `inventory/src/Inventory.Client/Services/OutgoingLetterServices.cs`
- `inventory/src/Inventory.Client/Shared/AttachmentBox.razor`
