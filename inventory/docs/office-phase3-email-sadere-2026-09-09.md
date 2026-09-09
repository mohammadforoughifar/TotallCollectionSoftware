# فاز ۳ اتوماسیون اداری — نامه صادره، دبیرخانه الکترونیک و ایمیل سازمانی (۲۰۲۶-۰۹-۰۹)

پیاده‌سازی ۹ درخواست کارفرما در یک موج. تمام تغییرات سمت Api/Client/Shared بدون تغییر در جداول موجود (جداول جدید `Oto_*` با SQL خام تضمین می‌شوند و در EF هم مپ شده‌اند).

---

## ۱) گردش‌نامه صادره با نام امضاکننده‌ها
- `OutgoingLetterService.GetSignersAsync` → لیست `OutgoingSignerDto { Name, Role, IsSigned, SignNote, SignedAt }` (نام از `User`).
- `OutgoingGardeshModal.razor` — مودال «گردش نامه صادره» با ۳ نما: لیستی / درختواره / فلوچارتی؛ نمایش امضاکننده‌ها با وضعیت امضا و تاریخ.
- دکمه گردش در منوی هر ردیف و در نوار ابزار نامه خوانده‌شده (`OutgoingLetterCartable.razor`).

## ۲) نشان‌کردن (ستاره) نامه صادره سمت فرستنده
- `OutgoingLetter.IsNeshan` + `DestEmail` (SQL خام در `OfficeEmailSchemaV1` — if-missing).
- `POST api/outgoingletters/{id}/neshan` → `IOutgoingLetterService.ToggleLetterNeshanAsync(letterId, userId)` — فقط سازنده/مدیر.
- در کارتابل صادره: منوی «نشان‌کردن» روی ردیف‌های **ارسالی** (ستاره سمت گیرنده روی Erja است، سمت فرستنده روی خود نامه)، ستاره در سربرگ خواندن نامه، پوشه «نشان‌شده‌ها» = ترکیب دو نوع ستاره.

## ۳) ذخیره پیوست صادره روی دیسک در «فایل های صادره»
- `FileStore.SaveWebRootAsync/ReadWebRoot/DeleteWebRoot/SizeWebRoot` — ذخیره در `wwwroot/فایل های صادره/{شناسه}/{guid}_{نام}` با گارد مسیر (`..`).
- آپلود/دانلود/حذف پیوست نامه و پیش‌نویس صادره اکنون FilePath-first است؛ رکوردهای قدیمی (Data در دیتابیس) هم خوانده می‌شوند.
- `Program.cs`: سرو استاتیک «فایل های صادره» و «فایل های ایمیل» مسدود شده — دانلود فقط از طریق API با چک دسترسی.

## ۴) حذف گزینه «صادره شده» از وضعیت دستی
- کلاینت: دکمه‌های «علامت صادر شده» از منوی ردیف و سربرگ حذف شد.
- سرور: `UpdateStatusAsync` و `POST {id}/status` وضعیت ۳ را **رد** می‌کنند؛ وضعیت ۳ فقط با امضای آخرین امضاکننده ثبت می‌شود (رفتار موجود `SignAsync`).

## ۵) «افزودن» در پیش‌نویس صادره
- دکمه «افزودن پیش‌نویس» بالای لیست پوشه پیش‌نویس → باز شدن `ComposeOutgoingModal` (خالی).

## ۶) گردش کار دستورکار (WorkOrder) مثل نامه داخلی
- فقط کلاینت: `WorkOrders.razor` — دکمه «گردش کار» در سربرگ جزئیات + مودال ۳ نمایی (لیستی/درختواره/فلوچارتی) + تاریخچه از `logs`.
- گیرندگان از `detail.Assignees` (رویت/پاسخ/انجام/تصمیم دستوردهنده) — بدون تغییر Backend. استایل‌های gardesh از `letters.css`.

## ۷) ارسال نامه صادره با پست الکترونیک از دبیرخانه
- `OutgoingDabirkhane.razor`: چک‌باکس «ارسال نامه با پست الکترونیک» + ایمیل مقصد + انتخاب حساب دبیرخانه (فقط حساب‌های `IsDabirkhane`).
- `DabirkhaneRegisterAsync`: اگر روش/چک‌باکس ایمیل → `IEmailService.SendLetterEmailAsync(letterId, dto, userName)`:
  - PDF نامه روی سربرگ (A4) + پیوست‌های نامه → پیام در `Oto_TBL_Sent` ثبت و با SMTP ارسال می‌شود.
  - خطای SMTP ثبت دبیرخانه را خراب نمی‌کند؛ پیام خطا به کاربر و اعلان سازنده می‌رود.

## ۸) تنظیمات ایمیل — Gmail / Yahoo / وب‌میل
- `OtoEmailPresets` (در Shared): Gmail (`smtp.gmail.com:465` / `imap.gmail.com:993`)، Yahoo (`smtp.mail.yahoo.com:465` / `imap.mail.yahoo.com:993`)، Outlook، وب‌میل دلخواه.
- فرم حساب: پرست انتخابی، DisplayName، رمز، SSL، دکمه «تست اتصال» (SMTP + IMAP اختیاری؛ راهنمای App Password جیمیل).
- هر کاربر چند حساب دارد؛ حساب دبیرخانه با `IsDabirkhane` از شخصی جدا است (مدیریت فقط با مجوز دبیرخانه/حذف ایمیل).

## ۹) جداول ایمیل دقیقاً مطابق Otomasion
`Api/Entities/Office/OtoEmailEntities.cs` با `[Table]`/`[Column]` دقیق:
`Oto_TBL_Sent`، `Oto_TBL_Inbox`، `Oto_TBl_EmailFolder`، `Oto_TBL_Email`، `Oto_TBL_EmailAttachments`
- کلیدها/روابط در `AppDbContext.OnModelCreating` صریح مپ شده (نام ستون‌ها با قرارداد EF یکی نیست).
- `OfficeEmailSchemaV1.EnsureAsync` در `DbInitializer` (هر دو شاخه) جداول را اگر نبودند می‌سازد؛ ستون‌های `OutgoingLetters.IsNeshan/DestEmail` هم همین‌جا اضافه می‌شوند.
- قواعد دسترسی: پیام‌های حساب‌های شخصی فقط برای صاحبش؛ حساب‌های دبیرخانه برای کاربران دارای مجوز «دبیرخانه» مشترک.

## صفحه «ایمیل سازمانی»
`Client/Pages/Office/Email.razor` — تب‌های دریافت‌شده/ارسالی/نشان‌شده/بایگانی/حساب‌ها؛ همگام‌سازی IMAP (آخرین ۵۰ پیام، dedupe با UId)، خواندن/نشان/بایگانی پوشه‌ای + مدیریت پوشه در تب بایگانی، ارسال با پیوست (multipart تا ۲۰MB)، دانلود پیوست از API.

## نکات استقرار
- نیازمند `MailKit 4.8.0` (NuGet restore) — Api csproj به‌روز شد.
- بدون migration: روی SQL Server جداول/ستون‌ها در استارت‌بعدی ساخته می‌شوند (`EnsureAsync`)؛ snapshot migrations بعداً توسط تیم با `dotnet ef` همگام شود.
- مسیرهای استاتیک «فایل های صادره/فایل های ایمیل» از سرو مستقیم بلاک‌اند؛ دانلود فقط با مجوز.
