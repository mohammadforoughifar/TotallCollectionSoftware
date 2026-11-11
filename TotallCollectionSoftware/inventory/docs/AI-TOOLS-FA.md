# ابزارهای دستیار هوشمند و لایسنس

> **سند مرجع بازبینی چهارم — مورد ۳.**
> فهرست کامل ۵۵ ابزار، تفکیک نوشتاری/خواندنی، رفتار در حالت فقط‌خواندنی، و هزینهٔ API بیرونی.

---

## ۱) مسئله

دستیار (AI Agent) ابزارهایی دارد که مدل زبانی می‌تواند در حلقهٔ استدلال صدا بزند. بعضی از
آن‌ها **خواندنی‌اند** (مانده مرخصی، جست‌وجوی راهنما) و بعضی **نوشتاری/ارسالی** (ثبت مرخصی،
ارسال نامه، ساخت صورت‌جلسه). اگر در حالت بدون لایسنس فقط لایهٔ HTTP را ببندیم، مدل هنوز
می‌تواند ابزار نوشتاری را در همان حلقه اجرا کند — چون ابزار از **داخل سرویس** صدا زده
می‌شود، نه از بیرون با یک درخواست جدا.

**راه‌حل:** طبقه‌بندی صریح هر ابزار + فیلتر در رجیستری + میان‌بُر پیش از تماس با مدل.

---

## ۲) طبقه‌بندی — ۵۵ ابزار

| دسته | تعداد | رفتار در `Missing`/`Invalid` |
|---|---|---|
| نوشتاری/ارسالی (`IsWrite = true`، پیش‌فرض) | ۲۲ | **دیده نمی‌شود**: نه در فهرست ابزارهای مدل، نه قابل اجرا |
| خواندنی (`IsWrite => false`) | ۳۳ | مجاز |
| **جمع** | **۵۵** | |

### چرا پیش‌فرض `IsWrite = true`؟

**fail-closed.** ابزار تازه‌ای که فردا اضافه می‌شود، تا کسی صریحاً آن را خواندنی اعلام نکند،
نوشتاری حساب می‌شود. اگر برعکس بود (پیش‌فرض خواندنی)، هر ابزار جدید بی‌صدا در حالت بدون
لایسنس باز می‌شد — و کسی متوجه نمی‌شد.

### ۲۲ ابزار نوشتاری/ارسالی

| # | ابزار | کلاس |
|---|---|---|
| 1 | `answer_referral` | AnswerReferralTool |
| 2 | `answer_ticket` | AnswerTicketTool |
| 3 | `cancel_action` | CancelActionTool |
| 4 | `cancel_reminder` | CancelReminderTool |
| 5 | `cancel_schedule` | CancelScheduleTool |
| 6 | `clock` | ClockTool |
| 7 | `confirm_action` | ConfirmActionTool |
| 8 | `create_alert` | CreateAlertTool |
| 9 | `create_letter_draft` | CreateLetterDraftTool |
| 10 | `create_minutes` | CreateMinutesTool |
| 11 | `create_ticket` | CreateTicketTool |
| 12 | `decide_leave` | DecideLeaveTool |
| 13 | `decide_leave_bulk` | DecideLeaveBulkTool |
| 14 | `delete_alert` | DeleteAlertTool |
| 15 | `refer_letter` | ReferLetterTool |
| 16 | `register_cheque` | RegisterChequeTool |
| 17 | `reindex_docs` | ReindexDocsTool |
| 18 | `report_work` | ReportWorkTool |
| 19 | `request_leave` | RequestLeaveTool |
| 20 | `request_mission` | RequestMissionTool |
| 21 | `schedule_report` | ScheduleReportTool |
| 22 | `set_reminder` | SetReminderTool |

### ۳۳ ابزار خواندنی

| # | ابزار | کلاس |
|---|---|---|
| 1 | `audit_stats` | `AuditStatsTool` |
| 2 | `build_report` | `BuildReportTool` |
| 3 | `cash_status` | `CashStatusTool` |
| 4 | `cheques_due` | `ChequesDueTool` |
| 5 | `data_catalog` | `DataCatalogTool` |
| 6 | `draft_minutes` | `DraftMinutesTool` |
| 7 | `explore_data` | `ExploreDataTool` |
| 8 | `feedback_stats` | `FeedbackStatsTool` |
| 9 | `guide_search` | `GuideSearchTool` |
| 10 | `letter_detail` | `LetterDetailTool` |
| 11 | `my_alert_rules` | `MyAlertRulesTool` |
| 12 | `my_alerts` | `MyAlertsTool` |
| 13 | `my_attendance_today` | `MyAttendanceTool` |
| 14 | `my_leave_balance` | `MyLeaveBalanceTool` |
| 15 | `my_leaves` | `MyLeavesTool` |
| 16 | `my_letters_inbox` | `MyLettersInboxTool` |
| 17 | `my_letters_stats` | `MyLettersStatsTool` |
| 18 | `my_loans` | `MyLoansTool` |
| 19 | `my_minutes_actions` | `MyMinutesActionsTool` |
| 20 | `my_missions` | `MyMissionsTool` |
| 21 | `my_payslip` | `MyPayslipTool` |
| 22 | `my_pending_actions` | `PendingActionsTool` |
| 23 | `my_referrals_pending` | `MyReferralsPendingTool` |
| 24 | `my_reminders` | `MyRemindersTool` |
| 25 | `my_schedules` | `MySchedulesTool` |
| 26 | `pending_approvals` | `PendingApprovalsTool` |
| 27 | `recent_invoices` | `RecentInvoicesTool` |
| 28 | `sales_summary` | `SalesSummaryTool` |
| 29 | `search_docs` | `SearchDocsTool` |
| 30 | `stock_status` | `StockStatusTool` |
| 31 | `top_debtors` | `TopDebtorsTool` |
| 32 | `users_lookup` | `UsersLookupTool` |
| 33 | `weekly_digest` | `WeeklyDigestTool` |

---

## ۳) مکانیزم اجرا

```csharp
// Services/Ai/AiTools.cs
public interface IAiTool
{
    string Name { get; }
    string Description { get; }
    object ParametersSchema { get; }

    /// <summary>
    /// آیا این ابزار چیزی می‌نویسد/می‌فرستد؟ پیش‌فرض true است (fail-closed):
    /// ابزار تازه تا صریحاً خواندنی اعلام نشود، در حالت فقط‌خواندنی خاموش است.
    /// </summary>
    bool IsWrite => true;

    Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx);
}
```

رجیستری سه نقطهٔ کنترل دارد — هر سه با همان `allowWrite`:

| متد | نقش |
|---|---|
| `Schemas(allowWrite)` | فهرست ابزارهایی که به مدل داده می‌شود (در حالت فقط‌خواندنی، نوشتاری‌ها **حذف** می‌شوند) |
| `IsWriteTool(name)` | پرسش طبقه‌بندی یک ابزار |
| `ExecuteAsync(name, args, ctx, allowWrite)` | اگر `allowWrite=false` و ابزار نوشتاری: `{"error":"license_readonly", …}` بدون اجرا |
| `Catalog()` | فهرست کامل `(name, isWrite)` — برای تست و همین سند |

---

## ۴) میان‌بُر پیش از مدل (مهم‌ترین بخش هزینه)

در `AiAgentService.ChatAsync` پیش از هر تماس با مدل زبانی:

```csharp
if (!CanWrite())
{
    // ۱) هیچ درخواستی به مدل بیرونی نمی‌رود — هزینهٔ API = صفر
    // ۲) جواب از موتور محلی (_fallback) می‌آید + پیام فارسی «سقف/لایسنس»
    // ۳) در لاگ حسابرسی با برچسب offline-readonly ثبت می‌شود
    return LocalReadOnlyAnswer(...);
}
```

**چرا؟** اگر در حالت بدون لایسنس، هر سؤال کاربر یک تماس API پولی بسازد، قفل ما دارد
هزینهٔ مشتری را بالا می‌برد بی‌آنکه کاری انجام دهد. با این میان‌بُر: **صفر توکن، صفر هزینه**.

`CanWrite()` سه حالت دارد:

| وضعیت | نتیجه |
|---|---|
| `ILicenseService` تزریق نشده (تست/توسعه) | `true` — رفتار قبلی |
| `GetOverallStatus() == Valid` | `true` |
| `Missing` / `Invalid` | `false` |
| استثنا در خواندن وضعیت | `false` (محافظه‌کارانه) |

> ⚠️ Grace محلی حذف شده است (مورد ۱). پنجرهٔ موقت فقط با «لایسنس پُل» امضاشده ممکن است،
> و آن هم یک سند `Valid` است — نه حالت سوم. پس `CanWrite()` فقط دو نتیجهٔ واقعی دارد.

---

## ۵) اکشن‌های `AiController` — از ۱۳ به ۲

| اکشن | قبل | بعد | دلیل |
|---|---|---|---|
| `chat` | ✅ مجاز | ✅ **مجاز** | تنها کانال توضیح «چرا بسته است»؛ در این حالت مدل صدا زده نمی‌شود و ابزار نوشتاری خاموش است |
| `conversations/{id}` (حذف) | ✅ | ✅ **مجاز** | حذف حافظهٔ گفتگوی خودِ کاربر — دادهٔ کسب‌وکار نیست |
| `briefing/send-now` | ✅ | ❌ | **ارسال** خلاصه به کاربران — کنش بیرونی |
| `digest/send-now` | ✅ | ❌ | **ارسال** |
| `feedback/submit` | ✅ | ❌ | نوشتن ردیف امتیاز در دیتابیس (نه ارسال پیام — توضیح قبلی غلط بود) |
| `letters/draft` | ✅ | ❌ | پیش‌نویس نامه = ثبت داده |
| `letters/{id}/summarize` | ✅ | ❌ | تماس با مدل (هزینهٔ API) |
| `letters/{id}/suggest-referral` | ✅ | ❌ | تماس با مدل |
| `letters/{id}/extract-tasks` | ✅ | ❌ | تماس با مدل + ساخت ساختار داده |
| `letters/{id}/categorize` | ✅ | ❌ | نوشتن دسته روی نامه |
| `letters/{id}/draft-reply` | ✅ | ❌ | پیش‌نویس پاسخ = ثبت داده |
| `letters/suggest-receivers` | ✅ | ❌ | تماس با مدل |
| `minutes/draft` | ✅ | ❌ | ساخت صورت‌جلسه = ثبت داده |

**اصل حاکم:** در حالت فقط‌خواندنی، «جست‌وجو/پیش‌نمایش/تست» آزاد است (چون همان کار GET را
می‌کند) ولی **هر چیزی که تماس با مدل بیرونی یا نوشتن رکورد می‌سازد** بسته است.

---

## ۶) دو استثنای عمدی

### `build_report` و `explore_data(excel=true)` خواندنی علامت خورده‌اند

فایل گزارش‌شان در یک کش **۱۵ دقیقه‌ای در حافظه** ساخته می‌شود، نه در دیتابیس. هیچ ردیفی
نمی‌نویسد و با ری‌استارت پاک می‌شود ⇒ «نوشتن» به معنای لایسنس نیست.

### هزینهٔ API بیرونیِ بدون لایسنس

| مسیر | هزینه |
|---|---|
| `chat` در حالت فقط‌خواندنی | **صفر** (میان‌بُر پیش از مدل) |
| اکشن‌های `letters/*` و `minutes/*` | بسته‌اند ⇒ صفر |
| ابزار نوشتاری در حلقه | از فهرست ابزار مدل حذف می‌شود ⇒ صفر |

---

## ۷) تست

`tests/LicensingDb`:

- شمارش ابزارها: `Catalog()` باید ۵۵ ردیف بدهد (۲۲ نوشتاری + ۳۳ خواندنی).
- در `Missing`: `Schemas(allowWrite:false)` هیچ ابزار نوشتاری برنگرداند.
- در `Missing`: `ExecuteAsync("request_leave", …, allowWrite:false)` ⇒ `license_readonly`.
- فهرست `[AllowInReadOnly]` روی `AiController` دقیقاً همان دو اکشن باشد (بخش ۱۸).
