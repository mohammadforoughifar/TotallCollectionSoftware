# راه‌اندازی چاپ نامه صادره با Stimulsoft MRT

## فایل‌های قالب

قالب هر شرکت باید در پوشه‌ای با نام شناسه همان شرکت قرار بگیرد:

```text
inventory/src/Inventory.Api/Reports/OutgoingLetters/Companies/{CompanyId}/Outgoing-A4.mrt
inventory/src/Inventory.Api/Reports/OutgoingLetters/Companies/{CompanyId}/Outgoing-A5.mrt
```

طبق اطلاعات اجرای فعلی، شناسه شرکت فروغ آریا `2` است؛ بنابراین قالب‌ها در پوشه `Companies/2` قرار گرفته‌اند. برای شرکت‌های دیگر نیز پوشه‌ای با شناسه همان شرکت ایجاد کنید.

برای مشاهده شناسه:

```sql
SELECT Id, Name FROM SystemCompanies ORDER BY Id;
```

نامه نیز باید همان شناسه شرکت را داشته باشد:

```sql
SELECT Id, LetterNumber, CompanyId FROM OutgoingLetters ORDER BY Id DESC;
```

## بازیابی پکیج‌ها و اجرا

```powershell
cd inventory
dotnet restore
dotnet clean
dotnet build Inventory.sln
dotnet run --project src\Inventory.Api
```

در اجرای Visual Studio نیز یک‌بار Restore NuGet Packages و سپس Rebuild Solution انجام شود.

## تشخیص موتور استفاده‌شده

پس از چاپ، در Output برنامه API باید این پیام دیده شود:

```text
نامه ... با Stimulsoft و قالب ... رندر شد.
```

اگر قالب پیدا نشود یا Stimulsoft خطا دهد، پیام زیر ثبت و QuestPDF اجرا می‌شود:

```text
چاپ نامه ... با MRT انجام نشد؛ QuestPDF به‌عنوان fallback استفاده می‌شود.
```

جزئیات خطا درست قبل از آن در Log درج می‌شود.

## امضای آزمایشی

در `appsettings.json` گزینه زیر برای تست فعال است:

```json
"UseTestSignature": true
```

تصویر آزمایشی فقط برای ردیف‌های `OutgoingLetterSigners` که `IsSigned = 1` دارند، درج می‌شود. پیش از استقرار واقعی آن را `false` کنید. بعداً این قسمت باید به جدول امن امضاهای کاربران متصل شود.

## لایسنس

در حال حاضر پکیج Stimulsoft بدون کلید production اجرا می‌شود و ممکن است محدودیت یا علامت نسخه آزمایشی داشته باشد. کلید نهایی نباید در Git ذخیره شود و باید از متغیر محیطی یا Secret Store بارگذاری گردد.
