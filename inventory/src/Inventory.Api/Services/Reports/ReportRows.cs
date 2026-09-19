namespace Inventory.Api.Services.Reports;

// =====================================================================
//  ردیف‌های تخت (flat row) دیتاست‌های گزارش‌ساز
//
//  هر دیتاست یک کوئری projection است که ستون‌های لازم را از چند جدول
//  (با navigation یا subquery) بیرون می‌کشد؛ ستون‌های گزارش‌ساز به
//  فیلدهای همین کلاس‌ها وصل می‌شوند.
// =====================================================================

// ---------- فروش و خرید ----------
public class InvoiceLineRow
{
    public DateTime Date { get; set; }
    public int InvoiceNo { get; set; }
    public string? Customer { get; set; }
    public string? Product { get; set; }
    public string? ProductCode { get; set; }
    public string? Category { get; set; }
    public string? Warehouse { get; set; }
    public string? Unit { get; set; }
    public int Status { get; set; }
    public int Settlement { get; set; }
    public decimal Qty { get; set; }
    public decimal Price { get; set; }
    public decimal Discount { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
}

public class InvoiceRow
{
    public DateTime Date { get; set; }
    public int Number { get; set; }
    public int Kind { get; set; }
    public int Status { get; set; }
    public int Settlement { get; set; }
    public string? Party { get; set; }
    public string? Warehouse { get; set; }
    public int LineCount { get; set; }
    public decimal TotalGross { get; set; }
    public decimal Discount { get; set; }
    public decimal Vat { get; set; }
    public decimal Shipping { get; set; }
    public decimal TotalNet { get; set; }
}

public class MoadianRow
{
    public DateTime Date { get; set; }
    public int Number { get; set; }
    public int Kind { get; set; }
    public int Status { get; set; }
    public string? Seller { get; set; }
    public string? Buyer { get; set; }
    public string? Period { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalNet { get; set; }
}

// ---------- خزانه ----------
public class ChequeRow
{
    public DateTime IssueDate { get; set; }
    public DateTime DueDate { get; set; }
    public string? Number { get; set; }
    public int Kind { get; set; }
    public int Status { get; set; }
    public string? Party { get; set; }
    public string? Bank { get; set; }
    public string? Branch { get; set; }
    public string? Account { get; set; }
    public decimal Amount { get; set; }
}

public class TreasuryRow
{
    public DateTime Date { get; set; }
    public int Number { get; set; }
    public int Kind { get; set; }
    public int Status { get; set; }
    public string? Party { get; set; }
    public string? FromAccount { get; set; }
    public string? ToAccount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal CashAmount { get; set; }
    public decimal ChequeAmount { get; set; }
    public decimal FeeAmount { get; set; }
    public decimal DiscountAmount { get; set; }
}

public class TreasuryLineRow
{
    public DateTime Date { get; set; }
    public int VoucherNo { get; set; }
    public int VoucherKind { get; set; }
    public int Method { get; set; }
    public string? Account { get; set; }
    public string? Party { get; set; }
    public string? ChequeNo { get; set; }
    public decimal Amount { get; set; }
}

// ---------- حسابداری ----------
public class AccVoucherRow
{
    public DateTime Date { get; set; }
    public int Number { get; set; }
    public int Status { get; set; }
    public int Source { get; set; }
    public string? FiscalYear { get; set; }
    public int LineCount { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
}

public class AccLineRow
{
    public DateTime Date { get; set; }
    public int VoucherNo { get; set; }
    public int Status { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public int AccountLevel { get; set; }
    public int AccountType { get; set; }
    public string? Party { get; set; }
    public string? FiscalYear { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

public class ExpenseRow
{
    public DateTime Date { get; set; }
    public string? Number { get; set; }
    public string? Category { get; set; }
    public int PayType { get; set; }
    public string? Payee { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
}

public class BudgetItemRow
{
    public string? FiscalYear { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public decimal Planned { get; set; }
    public decimal Committed { get; set; }
    public decimal Actual { get; set; }
}

public class AssetRow
{
    public DateTime PurchaseDate { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? Location { get; set; }
    public string? Vendor { get; set; }
    public int Status { get; set; }
    public int Method { get; set; }
    public int UsefulLifeMonths { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SalvageValue { get; set; }
    public decimal AccumulatedDepreciation { get; set; }
}

// ---------- انبار و کالا ----------
public class StockRow
{
    public string? Product { get; set; }
    public string? ProductCode { get; set; }
    public string? Category { get; set; }
    public string? Unit { get; set; }
    public string? Warehouse { get; set; }
    public decimal Qty { get; set; }
    public decimal AvgCost { get; set; }
    public decimal ReorderPoint { get; set; }
}

public class InvStockRow
{
    public DateTime UpdatedAt { get; set; }
    public string? Product { get; set; }
    public string? Category { get; set; }
    public string? Warehouse { get; set; }
    public decimal Qty { get; set; }
    public decimal AvgCost { get; set; }
    public decimal Value { get; set; }
}

public class InvDocRow
{
    public DateTime Date { get; set; }
    public string? DocType { get; set; }
    public int Nature { get; set; }
    public int Status { get; set; }
    public string? Warehouse { get; set; }
    public string? Party { get; set; }
    public string? Description { get; set; }
    public int LineCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalValue { get; set; }
}

public class InvDocLineRow
{
    public DateTime Date { get; set; }
    public string? DocType { get; set; }
    public string? Warehouse { get; set; }
    public string? Product { get; set; }
    public string? Unit { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
}

public class ProductRow
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Unit { get; set; }
    public string? Category { get; set; }
    public string? Barcode { get; set; }
    public string? Brand { get; set; }
    public bool IsActive { get; set; }
    public bool IsService { get; set; }
    public decimal SalePrice { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal ReorderPoint { get; set; }
    public decimal MaxStock { get; set; }
}

public class StkCountRow
{
    public DateTime Date { get; set; }
    public string? Session { get; set; }
    public string? Warehouse { get; set; }
    public string? Product { get; set; }
    public bool IsCounted { get; set; }
    public decimal SystemQty { get; set; }
    public decimal CountedQty { get; set; }
    public decimal UnitCost { get; set; }
    public int ScanCount { get; set; }
}

public class PartyRow
{
    public string? Name { get; set; }
    public int Type { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; }
    public string? Referrer { get; set; }
}

// ---------- عملیات و پشتیبانی ----------
public class WorkOrderRow
{
    public DateTime CreatedAt { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? Number { get; set; }
    public string? Title { get; set; }
    public string? Owner { get; set; }
    public string? Status { get; set; }
    public string? SourceModule { get; set; }
    public string? Tags { get; set; }
    public int Priority { get; set; }
    public int AssigneeCount { get; set; }
    public int ExtensionCount { get; set; }
}

public class ItRequestRow
{
    public DateTime CreatedAt { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Number { get; set; }
    public string? Title { get; set; }
    public string? Requester { get; set; }
    public string? System { get; set; }
    public string? RequestType { get; set; }
    public string? Status { get; set; }
}

public class RepairOrderRow
{
    public DateTime ReceivedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? Number { get; set; }
    public string? Party { get; set; }
    public string? Technician { get; set; }
    public string? DeviceType { get; set; }
    public string? DeviceModel { get; set; }
    public int Status { get; set; }
    public int ItemCount { get; set; }
    public decimal QuotedPrice { get; set; }
}

public class RepairItemRow
{
    public DateTime ReceivedAt { get; set; }
    public string? OrderNumber { get; set; }
    public int OrderStatus { get; set; }
    public string? Description { get; set; }
    public string? Product { get; set; }
    public decimal Qty { get; set; }
    public decimal Cost { get; set; }
    public decimal Price { get; set; }
}

// ---------- منابع انسانی ----------
public class EmployeeRow
{
    public DateTime HireDate { get; set; }
    public string? Code { get; set; }
    public string? FullName { get; set; }
    public string? OrgUnit { get; set; }
    public string? PostTitle { get; set; }
    public string? Mobile { get; set; }
    public string? Degree { get; set; }
    public int EmploymentType { get; set; }
    public int Status { get; set; }
    public bool IsActive { get; set; }
    public decimal BaseSalary { get; set; }
}

public class AttDailyRow
{
    public DateTime Date { get; set; }
    public string? Employee { get; set; }
    public string? EmployeeCode { get; set; }
    public DateTime? FirstIn { get; set; }
    public DateTime? LastOut { get; set; }
    public int Status { get; set; }
    public bool IsIncomplete { get; set; }
    public int WorkMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public int NightMinutes { get; set; }
}

public class AttRecordRow
{
    public DateTime WorkDate { get; set; }
    public string? UserName { get; set; }
    public string? ShiftGroup { get; set; }
    public DateTime? EnterAt { get; set; }
    public DateTime? ExitAt { get; set; }
    public string? FinalStatus { get; set; }
    public bool HasApprovedLeave { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }
    public int WorkMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public int DeficitMinutes { get; set; }
}

public class LeaveRow
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? Number { get; set; }
    public string? Type { get; set; }
    public string? Requester { get; set; }
    public string? Status { get; set; }
    public string? ApprovedBy { get; set; }
    public double Days { get; set; }
    public double Hours { get; set; }
}

public class PaySlipRow
{
    public DateTime CreatedAt { get; set; }
    public string? Employee { get; set; }
    public string? EmployeeCode { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public double DaysPaid { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal GrossEarnings { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal InsuranceAmount { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal NetPay { get; set; }
}

// ---------- اسناد، نامه‌ها، پروژه ----------
public class DocRow
{
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpireDate { get; set; }
    public string? Title { get; set; }
    public string? Code { get; set; }
    public string? Folder { get; set; }
    public string? CustomerCode { get; set; }
    public string? CreatedBy { get; set; }
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
}

public class InnerLetterRow
{
    public DateTime DateSabt { get; set; }
    public string? LetterNumber { get; set; }
    public string? Title { get; set; }
    public string? Creator { get; set; }
    public string? Confidentiality { get; set; }
    public string? Urgency { get; set; }
    public bool IsNeshan { get; set; }
}

public class OutLetterRow
{
    public DateTime DateSabt { get; set; }
    public DateTime? DateSadere { get; set; }
    public int Number { get; set; }
    public int Status { get; set; }
    public string? Creator { get; set; }
    public bool IsNeshan { get; set; }
    public int SignerCount { get; set; }
}

public class ReportWorkRow
{
    public DateTime ReportDate { get; set; }
    public string? CodeProject { get; set; }
    public string? User { get; set; }
    public string? Operator { get; set; }
    public string? Description { get; set; }
}

// =====================================================================
//  ردیف‌های ترکیبی (join) مکاتبات اداری
//  هر ردیف = یک «ارجاع» که با نامه، فرستنده، گیرنده و عملگر join شده
//  تا بتوان گزارش‌های وضعیتی داد: خوانده‌نشده، بی‌پاسخ، تاییدنشده، معوق
// =====================================================================

/// <summary>گردش نامه — هر ردیف یک ارجاع به‌همراه مشخصات نامه و طرفین.</summary>
public class LetterFlowRow
{
    // --- مشخصات نامه (از LetterSource + InnerLetter/Incoming/Outgoing) ---
    public DateTime LetterDate { get; set; }
    public string? LetterNumber { get; set; }
    public string? Title { get; set; }
    public int SourceType { get; set; }            // 1=داخلی 2=صادره 3=وارده
    public string? Confidentiality { get; set; }
    public string? Urgency { get; set; }
    public string? Sender { get; set; }            // ثبت‌کننده نامه

    // --- مشخصات ارجاع (Erja) ---
    public DateTime ErjaDate { get; set; }
    public string? ErjaFrom { get; set; }          // ارجاع‌دهنده
    public string? ErjaTo { get; set; }            // گیرنده ارجاع
    public string? ErjaType { get; set; }          // گیرنده / ارجاع / هامش
    public string? Amalgar { get; set; }           // جهت اقدام / جهت اطلاع / ...
    public string? MatnErja { get; set; }

    // --- وضعیت‌ها (همان چیزی که برای گزارش لازم است) ---
    public bool IsRead { get; set; }
    public bool IsUnread { get; set; }             // معکوس IsRead — برای شمارش مستقیم
    public bool HasAnswer { get; set; }
    public bool NoAnswer { get; set; }             // بی‌پاسخ
    public int TypeTaeed { get; set; }             // 0=بدون اقدام 1=تایید 2=رد
    public bool IsApproved { get; set; }
    public bool IsRejected { get; set; }
    public bool IsNotApproved { get; set; }        // هنوز تایید/رد نشده
    public bool IsBayegani { get; set; }
    public bool IsNeshan { get; set; }

    // --- مهلت و تاخیر ---
    public DateTime? Deadline { get; set; }
    public bool IsOverdue { get; set; }            // مهلت گذشته و هنوز پاسخ نداده
    public DateTime? ReadDate { get; set; }
    public DateTime? AnswerDate { get; set; }

    // --- سنجه‌های عددی (برای میانگین/جمع) ---
    public int Count1 { get; set; }                // همیشه ۱ — برای شمارش
    public int UnreadCount { get; set; }
    public int NoAnswerCount { get; set; }
    public int PendingApprovalCount { get; set; }
    public int OverdueCount { get; set; }
    public double HoursToRead { get; set; }        // ساعت تا خواندن
    public double HoursToAnswer { get; set; }      // ساعت تا پاسخ
}

/// <summary>کارتابل کاربران — یک ردیف به ازای هر کاربر با شمارش وضعیت‌ها.</summary>
public class CartableSummaryRow
{
    public string? User { get; set; }
    public string? FullName { get; set; }
    public int Total { get; set; }
    public int Unread { get; set; }
    public int NoAnswer { get; set; }
    public int Answered { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int PendingApproval { get; set; }
    public int Overdue { get; set; }
    public int Bayegani { get; set; }
    public double AvgHoursToRead { get; set; }
    public double AvgHoursToAnswer { get; set; }
}

/// <summary>نامه داخلی + آمار ارجاع‌هایش (یک ردیف به ازای هر نامه).</summary>
public class InnerLetterFlowRow
{
    public DateTime DateSabt { get; set; }
    public string? LetterNumber { get; set; }
    public string? Title { get; set; }
    public string? Creator { get; set; }
    public string? Confidentiality { get; set; }
    public string? Urgency { get; set; }
    public bool IsNeshan { get; set; }

    // --- آمار ارجاع‌ها (subquery روی Erjas) ---
    public int ErjaCount { get; set; }
    public int ReceiverCount { get; set; }
    public int ReadCount { get; set; }
    public int UnreadCount { get; set; }
    public int AnsweredCount { get; set; }
    public int NoAnswerCount { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
    public int PendingApprovalCount { get; set; }
    public int OverdueCount { get; set; }

    // --- وضعیت کلی نامه ---
    public bool FullyRead { get; set; }            // همه گیرندگان خوانده‌اند
    public bool FullyAnswered { get; set; }        // همه پاسخ داده‌اند
    public bool HasOverdue { get; set; }
    public double ReadPercent { get; set; }        // درصد خوانده‌شده
    public double AnswerPercent { get; set; }
}

/// <summary>
/// همه نامه‌ها (داخلی + صادره + وارده) در یک دیتاست واحد — مخصوص نمودار.
/// هر ردیف = یک نامه، با بعدهای مشترک (نوع، وضعیت، محرمانگی، فوریت) و
/// آمار ارجاع‌ها. برای نمودار میله‌ای/دایره‌ای بر اساس نوع نامه.
/// </summary>
public class LetterAllRow
{
    // --- بعدهای مشترک ---
    public DateTime Date { get; set; }             // تاریخ ثبت
    public DateTime? DateClosed { get; set; }      // صدور (صادره) یا ورود (وارده)
    public string? LetterNumber { get; set; }
    public string? Title { get; set; }
    public int SourceType { get; set; }            // 1=داخلی 2=صادره 3=وارده
    public string? TypeName { get; set; }          // «داخلی» / «صادره» / «وارده»
    public string? Creator { get; set; }
    public string? Confidentiality { get; set; }   // یکسان‌سازی‌شده به متن
    public string? Urgency { get; set; }
    public string? Counterparty { get; set; }      // گیرنده بیرونی (صادره) یا فرستنده بیرونی (وارده)
    public string? Status { get; set; }            // وضعیت یکسان‌سازی‌شده
    public string? SendMethod { get; set; }        // روش ارسال/دریافت
    public bool IsNeshan { get; set; }
    public bool IsBayegani { get; set; }

    // --- آمار ارجاع ---
    public int ErjaCount { get; set; }
    public int UnreadCount { get; set; }
    public int NoAnswerCount { get; set; }
    public int OverdueCount { get; set; }
    public bool HasUnread { get; set; }
    public bool HasNoAnswer { get; set; }
    public bool HasOverdue { get; set; }

    // --- سنجه‌ها ---
    public int Count1 { get; set; }                // همیشه ۱ — شمارش نامه
    public int AttachmentCount { get; set; }
    public double DaysOpen { get; set; }           // روزهای باز بودن
}
