using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

// =====================================================================
// موجودیت‌های ماژول حسابداری
//  • سال مالی
//  • درخت کدینگ حساب‌ها (گروه ← کل ← معین ← تفصیلی)
//  • سند حسابداری و آرتیکل‌های آن
//  • قاعده‌ی صدور خودکار سند از روی اسناد انبار
// =====================================================================

/// <summary>سال (دوره) مالی</summary>
public class AccFiscalYear
{
    public int Id { get; set; }

    [MaxLength(120)] public string Title { get; set; } = "";
    [MaxLength(20)] public string? Code { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// <summary>دوره جاری — اسناد جدید اینجا ثبت می‌شوند</summary>
    public bool IsCurrent { get; set; }

    /// <summary>بسته شده — ثبت/ویرایش سند ممنوع</summary>
    public bool IsClosed { get; set; }

    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>گره درخت کدینگ حساب‌ها</summary>
public class AccAccount
{
    public int Id { get; set; }

    [MaxLength(30)] public string Code { get; set; } = "";
    [MaxLength(180)] public string Name { get; set; } = "";
    [MaxLength(180)] public string? EnName { get; set; }

    public int? ParentId { get; set; }
    public AccAccount? Parent { get; set; }

    public AccountLevel Level { get; set; } = AccountLevel.Group;
    public AccountType Type { get; set; } = AccountType.Asset;
    public AccountNature Nature { get; set; } = AccountNature.Debit;

    /// <summary>حساب دائم (ترازنامه‌ای)؟ حساب‌های موقت در اختتامیه بسته می‌شوند</summary>
    public bool IsPermanent { get; set; } = true;

    /// <summary>قابل ثبت سند (معمولاً برگ درخت)</summary>
    public bool IsPostable { get; set; }

    /// <summary>ثبت طرف حساب روی آرتیکل الزامی است</summary>
    public bool RequiresParty { get; set; }

    /// <summary>حساب سیستمی — قابل حذف نیست</summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>سند حسابداری</summary>
public class AccVoucher
{
    public int Id { get; set; }

    /// <summary>شماره سند در دوره مالی</summary>
    public int Number { get; set; }

    [MaxLength(60)] public string? RefNumber { get; set; }

    public int FiscalYearId { get; set; }
    public AccFiscalYear? FiscalYear { get; set; }

    public DateTime Date { get; set; } = DateTime.Now;
    [MaxLength(600)] public string? Description { get; set; }

    public VoucherStatus Status { get; set; } = VoucherStatus.Draft;
    public VoucherSource Source { get; set; } = VoucherSource.Manual;

    /// <summary>شناسه سند مبدأ (مثلاً InvDoc.Id)</summary>
    public int? SourceId { get; set; }
    [MaxLength(200)] public string? SourceTitle { get; set; }

    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }

    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [MaxLength(120)] public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    public List<AccVoucherLine> Lines { get; set; } = new();
}

/// <summary>آرتیکل (سطر) سند حسابداری</summary>
public class AccVoucherLine
{
    public int Id { get; set; }

    public int VoucherId { get; set; }
    public AccVoucher? Voucher { get; set; }

    public int RowNo { get; set; }

    public int AccountId { get; set; }
    public AccAccount? Account { get; set; }

    public int? PartyId { get; set; }
    public int? ProjectId { get; set; }

    [MaxLength(600)] public string? Description { get; set; }
    [MaxLength(60)] public string? RefNumber { get; set; }

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

/// <summary>قاعده‌ی صدور خودکار سند حسابداری از روی نوع سند انبار</summary>
public class AccInvRule
{
    public int Id { get; set; }

    public int DocTypeId { get; set; }
    public InvDocType? DocType { get; set; }

    /// <summary>حساب موجودی کالا</summary>
    public int? InventoryAccountId { get; set; }

    /// <summary>حساب طرف مقابل (خرید، فروش، بهای تمام‌شده، ضایعات، …)</summary>
    public int? CounterAccountId { get; set; }

    /// <summary>مبنای مبلغ: بهای تمام‌شده (true) یا مبلغ سطرهای سند (false)</summary>
    public bool UseCostValue { get; set; } = true;

    public bool IsActive { get; set; } = true;
    [MaxLength(500)] public string? Description { get; set; }
}
