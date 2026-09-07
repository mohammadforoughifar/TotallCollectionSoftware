using Microsoft.EntityFrameworkCore;
using Inventory.Shared.Entities;

namespace Inventory.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<Referrer> Referrers => Set<Referrer>();
    public DbSet<ReferrerPayment> ReferrerPayments => Set<ReferrerPayment>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<MeasureUnit> MeasureUnits => Set<MeasureUnit>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionLine> TransactionLines => Set<TransactionLine>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Cheque> Cheques => Set<Cheque>();
    public DbSet<InstallmentLine> Installments => Set<InstallmentLine>();
    public DbSet<Technician> Technicians => Set<Technician>();
    public DbSet<RepairOrder> RepairOrders => Set<RepairOrder>();
    public DbSet<RepairItem> RepairItems => Set<RepairItem>();
    public DbSet<SystemInfo> SystemInfos => Set<SystemInfo>();

    // ================== درخواست خدمت آی‌تی ==================
    public DbSet<ItRequest> ItRequests => Set<ItRequest>();
    public DbSet<ItRequestAssignment> ItRequestAssignments => Set<ItRequestAssignment>();
    public DbSet<ItRequestAttachment> ItRequestAttachments => Set<ItRequestAttachment>();
    public DbSet<ItRequestLog> ItRequestLogs => Set<ItRequestLog>();
    public DbSet<ItRequestSeen> ItRequestSeens => Set<ItRequestSeen>();

    // ================== اعلان‌ها (نوتیفیکیشن) ==================
    public DbSet<AppNotification> AppNotifications => Set<AppNotification>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    // ================== دستور کار ==================
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderAssignee> WorkOrderAssignees => Set<WorkOrderAssignee>();
    public DbSet<WorkOrderLog> WorkOrderLogs => Set<WorkOrderLog>();
    public DbSet<WorkOrderAttachment> WorkOrderAttachments => Set<WorkOrderAttachment>();
    public DbSet<WorkOrderAllowedAssignee> WorkOrderAllowedAssignees => Set<WorkOrderAllowedAssignee>();

    // ================== بایگانی و پیوست جامع (عمومی) ==================
    public DbSet<ArchiveFolder> ArchiveFolders => Set<ArchiveFolder>();
    public DbSet<ArchiveItem> ArchiveItems => Set<ArchiveItem>();
    public DbSet<AppAttachment> AppAttachments => Set<AppAttachment>();
    public DbSet<SystemInfoChangeLog> SystemInfoChangeLogs => Set<SystemInfoChangeLog>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<CompanyHoliday> CompanyHolidays => Set<CompanyHoliday>();
    public DbSet<WorkCalendarDay> WorkCalendarDays => Set<WorkCalendarDay>();
    public DbSet<WorkCalendarSettings> WorkCalendarSettings => Set<WorkCalendarSettings>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CctvCamera> CctvCameras => Set<CctvCamera>();
    public DbSet<CctvNvr> CctvNvrs => Set<CctvNvr>();
    public DbSet<OfficeMachine> OfficeMachines => Set<OfficeMachine>();
    public DbSet<OfficeMachineRepair> OfficeMachineRepairs => Set<OfficeMachineRepair>();

    // ================== حضور و غیاب ==================
    public DbSet<ShiftGroup> ShiftGroups => Set<ShiftGroup>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<AttendanceSegment> AttendanceSegments => Set<AttendanceSegment>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<AttendanceAlert> AttendanceAlerts => Set<AttendanceAlert>();
    public DbSet<AttendanceAreaSetting> AttendanceAreaSettings => Set<AttendanceAreaSetting>();
    public DbSet<OfficeMachineCost> OfficeMachineCosts => Set<OfficeMachineCost>();
    // ---- جدول‌های قطعات کامپیوتر ----
    public DbSet<SystemCpu> SystemCpus => Set<SystemCpu>();
    public DbSet<SystemBoard> SystemBoards => Set<SystemBoard>();
    public DbSet<SystemRam> SystemRams => Set<SystemRam>();
    public DbSet<SystemDisk> SystemDisks => Set<SystemDisk>();
    public DbSet<SystemGpu> SystemGpus => Set<SystemGpu>();
    public DbSet<SystemMonitor> SystemMonitors => Set<SystemMonitor>();
    public DbSet<SystemNetAdapter> SystemNetAdapters => Set<SystemNetAdapter>();
    public DbSet<SystemVolume> SystemVolumes => Set<SystemVolume>();
    public DbSet<SystemInfoUserHistory> SystemInfoUserHistories => Set<SystemInfoUserHistory>();
    public DbSet<SystemHandover> SystemHandovers => Set<SystemHandover>();
    public DbSet<SystemRemoteCommand> SystemRemoteCommands => Set<SystemRemoteCommand>();
    public DbSet<SystemCompany> SystemCompanies => Set<SystemCompany>();
    public DbSet<SystemDepartment> SystemDepartments => Set<SystemDepartment>();
    public DbSet<SystemUser> SystemUsers => Set<SystemUser>();

    // ==================== مدیریت پروژه‌ها (ورود/خروج، گزارش کار، پیوست) ====================
    public DbSet<KarFarma> KarFarmas => Set<KarFarma>();
    public DbSet<TypeFactor> TypeFactors => Set<TypeFactor>();
    public DbSet<ProjectEntryExit> ProjectEntryExits => Set<ProjectEntryExit>();
    public DbSet<ReportWork> ReportWorks => Set<ReportWork>();
    public DbSet<ProjectAttach> ProjectAttaches => Set<ProjectAttach>();

    // ==================== اتوماسیون اداری — نامه داخلی ====================
    public DbSet<LetterSource> LetterSources => Set<LetterSource>();
    public DbSet<InnerLetter> InnerLetters => Set<InnerLetter>();
    public DbSet<Erja> Erjas => Set<Erja>();
    public DbSet<Amalgar> Amalgars => Set<Amalgar>();
    public DbSet<PishnevisLetter> PishnevisLetters => Set<PishnevisLetter>();
    public DbSet<RelatedLetter> RelatedLetters => Set<RelatedLetter>();
    public DbSet<LetterBayegani> LetterBayeganis => Set<LetterBayegani>();
    public DbSet<LetterGroup> LetterGroups => Set<LetterGroup>();
    public DbSet<LetterGroupMember> LetterGroupMembers => Set<LetterGroupMember>();
    public DbSet<LetterStrature> LetterStratures => Set<LetterStrature>();

    // ==================== اتوماسیون اداری — نامه صادره (فاز دوم + امضا کنندگان) ====================
    public DbSet<OutgoingLetter> OutgoingLetters => Set<OutgoingLetter>();
    public DbSet<OutgoingPishnevisLetter> OutgoingPishnevisLetters => Set<OutgoingPishnevisLetter>();
    public DbSet<OutgoingLetterSigner> OutgoingLetterSigners => Set<OutgoingLetterSigner>();

    // ==================== آرشیو اسناد و مدارک (DocArchive) ====================
    public DbSet<DocFolder> DocFolders => Set<DocFolder>();
    public DbSet<DocFolderPermission> DocFolderPermissions => Set<DocFolderPermission>();
    public DbSet<ArchiveDocument> Documents => Set<ArchiveDocument>();
    public DbSet<DocumentPermission> DocumentPermissions => Set<DocumentPermission>();
    public DbSet<DocumentApprover> DocumentApprovers => Set<DocumentApprover>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentLink> DocumentLinks => Set<DocumentLink>();
    public DbSet<DocCartableTask> DocCartableTasks => Set<DocCartableTask>();
    public DbSet<DocumentLog> DocumentLogs => Set<DocumentLog>();
    public DbSet<DocExpiryAlert> DocExpiryAlerts => Set<DocExpiryAlert>();
    public DbSet<DocTag> DocTags => Set<DocTag>();
    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();
    public DbSet<DocExtractedText> DocExtractedTexts => Set<DocExtractedText>();
    public DbSet<DocEntityLink> DocEntityLinks => Set<DocEntityLink>();

    // ==================== ماژول پیام‌رسان سازمانی (Chat) ====================
    public DbSet<Inventory.Api.Entities.Chat.ChatConversation> ChatConversations => Set<Inventory.Api.Entities.Chat.ChatConversation>();
    public DbSet<Inventory.Api.Entities.Chat.ChatMember> ChatMembers => Set<Inventory.Api.Entities.Chat.ChatMember>();
    public DbSet<Inventory.Api.Entities.Chat.ChatMessage> ChatMessages => Set<Inventory.Api.Entities.Chat.ChatMessage>();

    // ==================== ماژول انبارداری ====================
    public DbSet<ProductAttributeDef> ProductAttributeDefs => Set<ProductAttributeDef>();
    public DbSet<ProductAttributeOption> ProductAttributeOptions => Set<ProductAttributeOption>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<InvDocType> InvDocTypes => Set<InvDocType>();
    public DbSet<InvDoc> InvDocs => Set<InvDoc>();
    public DbSet<InvDocLine> InvDocLines => Set<InvDocLine>();
    public DbSet<InvLedgerEntry> InvLedger => Set<InvLedgerEntry>();
    public DbSet<InvStock> InvStocks => Set<InvStock>();

    // ==================== ماژول حسابداری ====================
    public DbSet<AccFiscalYear> AccFiscalYears => Set<AccFiscalYear>();
    public DbSet<AccAccount> AccAccounts => Set<AccAccount>();
    public DbSet<AccVoucher> AccVouchers => Set<AccVoucher>();
    public DbSet<AccVoucherLine> AccVoucherLines => Set<AccVoucherLine>();
    public DbSet<AccInvRule> AccInvRules => Set<AccInvRule>();
    // ==================== ابعاد تحلیلی (مراکز هزینه / شعبه) ====================
    public DbSet<AccDimension> AccDimensions => Set<AccDimension>();
    public DbSet<AccDimensionValue> AccDimensionValues => Set<AccDimensionValue>();
    // ==================== دارایی ثابت و استهلاک ====================
    public DbSet<FixedAssetCategory> FixedAssetCategories => Set<FixedAssetCategory>();
    public DbSet<FixedAsset> FixedAssets => Set<FixedAsset>();
    public DbSet<FixedAssetDepreciationRun> FixedAssetDepreciationRuns => Set<FixedAssetDepreciationRun>();
    public DbSet<FixedAssetDepreciationLine> FixedAssetDepreciationLines => Set<FixedAssetDepreciationLine>();
    // ==================== بودجه و کنترل بودجه ====================
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetItem> BudgetItems => Set<BudgetItem>();
    public DbSet<BudgetTransaction> BudgetTransactions => Set<BudgetTransaction>();
    // ==================== سامانه مودیان (فاکتور الکترونیکی) ====================
    public DbSet<MoadianSetting> MoadianSettings => Set<MoadianSetting>();
    public DbSet<MoadianFiscalPeriod> MoadianFiscalPeriods => Set<MoadianFiscalPeriod>();
    public DbSet<MoadianInvoice> MoadianInvoices => Set<MoadianInvoice>();
    public DbSet<MoadianInvoiceLine> MoadianInvoiceLines => Set<MoadianInvoiceLine>();
    public DbSet<MoadianLog> MoadianLogs => Set<MoadianLog>();
    public DbSet<MoadianCpc> MoadianCpcList => Set<MoadianCpc>();
    public DbSet<FiscalPrinterSetting> FiscalPrinterSettings => Set<FiscalPrinterSetting>();

    // ---------- ماژول فاکتور ----------
    public DbSet<FacInvoice> FacInvoices => Set<FacInvoice>();
    public DbSet<FacInvoiceLine> FacInvoiceLines => Set<FacInvoiceLine>();
    public DbSet<FacRule> FacRules => Set<FacRule>();

    // ---------- ماژول خزانه‌داری ----------
    public DbSet<TrsAccount> TrsAccounts => Set<TrsAccount>();
    public DbSet<TrsVoucher> TrsVouchers => Set<TrsVoucher>();
    public DbSet<TrsVoucherLine> TrsVoucherLines => Set<TrsVoucherLine>();
    public DbSet<TrsCheque> TrsCheques => Set<TrsCheque>();
    public DbSet<TrsChequeAction> TrsChequeActions => Set<TrsChequeAction>();
    public DbSet<TrsRule> TrsRules => Set<TrsRule>();

    // ---------- ماژول انبارگردانی و بارکد ----------
    public DbSet<BcdBarcode> BcdBarcodes => Set<BcdBarcode>();
    public DbSet<StkSession> StkSessions => Set<StkSession>();
    public DbSet<StkLine> StkLines => Set<StkLine>();
    public DbSet<StkScan> StkScans => Set<StkScan>();

    // ==================== RBAC ====================
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        mb.Entity<User>().HasIndex(u => u.Username).IsUnique();
        mb.Entity<ProductCategory>().HasIndex(c => c.Name).IsUnique();
        mb.Entity<MeasureUnit>().HasIndex(u => u.Name).IsUnique();
        mb.Entity<Stock>().HasIndex(s => new { s.WarehouseId, s.ProductId }).IsUnique();
        mb.Entity<Product>().HasIndex(p => p.Code).IsUnique();
        mb.Entity<Transaction>().HasIndex(t => t.Type);
        mb.Entity<Transaction>().HasIndex(t => t.Date);
        mb.Entity<TransactionLine>().HasIndex(l => l.ProductId);

        // ==================== آرشیو اسناد و مدارک ====================
        // کد مدرک در کل سیستم یکتاست
        mb.Entity<ArchiveDocument>().HasIndex(d => d.Code).IsUnique();
        mb.Entity<ArchiveDocument>().HasIndex(d => d.FolderId);
        mb.Entity<ArchiveDocument>().HasIndex(d => d.ExpireDate);
        mb.Entity<DocFolder>().HasIndex(f => f.ParentId);
        mb.Entity<DocFolderPermission>().HasIndex(p => new { p.FolderId, p.UserId }).IsUnique();
        mb.Entity<DocumentPermission>().HasIndex(p => new { p.DocumentId, p.UserId }).IsUnique();
        mb.Entity<DocumentVersion>().HasIndex(v => new { v.DocumentId, v.VersionNo }).IsUnique();
        // هر مدرک برای هر آستانه و هر تاریخ انقضا فقط یک‌بار هشدار می‌گیرد
        mb.Entity<DocExpiryAlert>().HasIndex(a => new { a.DocumentId, a.ThresholdDays, a.ExpireDate }).IsUnique();
        mb.Entity<DocumentApprover>().HasIndex(a => new { a.DocumentId, a.VersionId });
        mb.Entity<DocumentLink>().HasIndex(l => new { l.DocumentId, l.LinkedDocumentId }).IsUnique();
        mb.Entity<DocCartableTask>().HasIndex(t => new { t.UserId, t.Status });
        mb.Entity<DocumentLog>().HasIndex(l => l.DocumentId);
        mb.Entity<DocTag>().HasIndex(t => t.Name).IsUnique();
        mb.Entity<DocumentTag>().HasIndex(t => new { t.DocumentId, t.TagId }).IsUnique();
        mb.Entity<DocExtractedText>().HasIndex(e => new { e.DocumentId, e.AttachmentId });
        mb.Entity<DocExtractedText>().HasIndex(e => e.AttachmentId);
        mb.Entity<DocEntityLink>().HasIndex(l => new { l.DocumentId, l.Module, l.EntityId });
        mb.Entity<DocEntityLink>().HasIndex(l => new { l.Module, l.EntityId });

        // ---------- ماژول پیام‌رسان سازمانی (Chat) ----------
        mb.Entity<Inventory.Api.Entities.Chat.ChatMember>().HasIndex(m => new { m.ConversationId, m.UserId }).IsUnique();
        mb.Entity<Inventory.Api.Entities.Chat.ChatMember>().HasIndex(m => m.UserId);
        mb.Entity<Inventory.Api.Entities.Chat.ChatMessage>().HasIndex(m => new { m.ConversationId, m.CreatedAt });
        mb.Entity<Inventory.Api.Entities.Chat.ChatMessage>().HasIndex(m => m.SenderUserId);
        mb.Entity<Inventory.Api.Entities.Chat.ChatConversation>().HasIndex(c => c.LastMessageAt);

        // ---------- امنیت حضور و غیاب: ایندکس‌های دستگاه‌ها و هشدارها ----------
        // هر کاربر برای هر Device ID فقط یک رکورد دستگاه دارد (دستگاه یکتا)
        mb.Entity<UserDevice>().HasIndex(d => new { d.UserId, d.DeviceId }).IsUnique();
        mb.Entity<UserDevice>().HasIndex(d => d.DeviceId);
        mb.Entity<AttendanceAlert>().HasIndex(a => new { a.UserId, a.Status });
        mb.Entity<AttendanceAlert>().HasIndex(a => new { a.Status, a.CreatedAt });

        // ---------- دقت صریح اعداد اعشاری برای SQL Server ----------
        // قیمت‌ها و مبالغ: 2 رقم اعشار
        mb.Entity<Product>().Property(p => p.SalePrice).HasPrecision(18, 2);
        mb.Entity<Product>().Property(p => p.PurchasePrice).HasPrecision(18, 2);
        mb.Entity<Stock>().Property(s => s.AvgCost).HasPrecision(18, 4);
        mb.Entity<Transaction>().Property(t => t.Amount).HasPrecision(18, 2);
        mb.Entity<TransactionLine>().Property(l => l.Price).HasPrecision(18, 2);

        // مقادیر (موجودی/مقدار) با 3 رقم اعشار
        mb.Entity<Product>().Property(p => p.ReorderPoint).HasPrecision(18, 3);
        mb.Entity<Product>().Property(p => p.MaxStock).HasPrecision(18, 3);
        mb.Entity<Stock>().Property(s => s.Quantity).HasPrecision(18, 3);
        mb.Entity<TransactionLine>().Property(l => l.Quantity).HasPrecision(18, 3);
        mb.Entity<Referrer>().Property(r => r.GoodsCommissionPercent).HasPrecision(5, 2);
        mb.Entity<Referrer>().Property(r => r.ServiceCommissionPercent).HasPrecision(5, 2);
        mb.Entity<ReferrerPayment>().Property(p => p.Amount).HasPrecision(18, 2);
        mb.Entity<ReferrerPayment>().HasIndex(p => p.ReferrerId);

        // ---------- هزینه‌ها ----------
        mb.Entity<ExpenseCategory>().HasIndex(c => c.Name).IsUnique();
        mb.Entity<Expense>().Property(e => e.Amount).HasPrecision(18, 2);
        mb.Entity<Expense>().HasIndex(e => e.Date);
        mb.Entity<Expense>().HasIndex(e => e.CategoryId);

        // ---------- پرداخت فروش: چک و اقساط ----------
        mb.Entity<Transaction>().Property(t => t.SettledAmount).HasPrecision(18, 2);
        mb.Entity<Transaction>().Property(t => t.CashAmount).HasPrecision(18, 2);
        mb.Entity<Cheque>().Property(c => c.Amount).HasPrecision(18, 2);
        mb.Entity<Cheque>().HasIndex(c => c.DueDate);
        mb.Entity<Cheque>().HasIndex(c => c.IsCleared);
        mb.Entity<InstallmentLine>().Property(i => i.Amount).HasPrecision(18, 2);
        mb.Entity<InstallmentLine>().HasIndex(i => i.DueDate);
        mb.Entity<Transaction>()
            .HasMany(t => t.Cheques)
            .WithOne()
            .HasForeignKey(c => c.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Transaction>()
            .HasMany(t => t.Installments)
            .WithOne()
            .HasForeignKey(i => i.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ---------- تعمیرات ----------
        mb.Entity<RepairOrder>().HasIndex(r => r.Status);
        mb.Entity<RepairOrder>().HasIndex(r => r.PartyId);
        mb.Entity<RepairOrder>().HasIndex(r => r.TechnicianId);
        mb.Entity<RepairOrder>().Property(r => r.QuotedPrice).HasPrecision(18, 2);
        mb.Entity<RepairOrder>()
            .HasMany(r => r.Items)
            .WithOne()
            .HasForeignKey(i => i.RepairOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<RepairItem>().Property(i => i.Quantity).HasPrecision(18, 3);
        mb.Entity<RepairItem>().Property(i => i.Cost).HasPrecision(18, 2);
        mb.Entity<RepairItem>().Property(i => i.Price).HasPrecision(18, 2);

        // ==================== مدیریت پروژه‌ها ====================
        mb.Entity<KarFarma>().HasIndex(k => k.Name);
        mb.Entity<ProjectEntryExit>().HasIndex(p => p.CodeProject);
        mb.Entity<ProjectEntryExit>().HasIndex(p => p.SerialNumber);
        mb.Entity<ProjectEntryExit>().HasIndex(p => p.KarFarmaId);
        mb.Entity<ProjectEntryExit>().HasIndex(p => p.UserId);
        mb.Entity<ReportWork>().HasIndex(r => r.ProjectId);
        mb.Entity<ReportWork>().HasIndex(r => r.UserId);
        mb.Entity<ReportWork>().HasIndex(r => r.OperatorId);
        mb.Entity<ProjectAttach>().HasIndex(a => a.ProjectId);

        // جمع ساعات پروژه به‌صورت تیک (bigint) — نوع time فقط تا ۲۴ ساعت را می‌پذیرد
        mb.Entity<ProjectEntryExit>().Property(p => p.TotalSpentTime).HasConversion<long>();

        // رکوردهای ورود/خروج → مراجع (حذف نرم — جلوگیری از حذف فیزیکی مرجع‌های درحال‌استفاده)
        mb.Entity<ProjectEntryExit>()
            .HasOne(p => p.KarFarma)
            .WithMany(k => k.Projects)
            .HasForeignKey(p => p.KarFarmaId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<ProjectEntryExit>()
            .HasOne(p => p.TypeFactor)
            .WithMany(t => t.Projects)
            .HasForeignKey(p => p.FactorTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<ProjectEntryExit>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // گزارش‌های کار و پیوست‌ها زیرمجموعه‌ی پروژه هستند
        mb.Entity<ReportWork>()
            .HasOne(r => r.Project)
            .WithMany(p => p.ReportWorks)
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<ReportWork>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        // اپراتور (انجام‌دهندهٔ کار) — جدا از ثبت‌کننده؛ اختیاری
        mb.Entity<ReportWork>()
            .HasOne(r => r.Operator)
            .WithMany()
            .HasForeignKey(r => r.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<ProjectAttach>()
            .HasOne(a => a.Project)
            .WithMany(p => p.Attaches)
            .HasForeignKey(a => a.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<ProjectAttach>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ---------- ماژول شناسنامه سیستم: تاریخچه کاربر، تحویل، دستور از راه دور ----------
        mb.Entity<SystemInfoUserHistory>().HasIndex(h => h.SystemInfoId);
        mb.Entity<SystemHandover>().HasIndex(h => h.SystemInfoId);
        mb.Entity<SystemRemoteCommand>().HasIndex(c => c.SystemInfoId);
        mb.Entity<SystemRemoteCommand>().HasIndex(c => c.Status);

        // ==================== اتوماسیون اداری — نامه داخلی و صادره ====================
        // کلیدهای اصلی صریح (نام‌گذاری مطابق طرح کارفرما)
        mb.Entity<Erja>().HasKey(e => e.ErjaId);
        mb.Entity<Amalgar>().HasKey(a => a.AmalgarId);

        mb.Entity<Amalgar>().HasKey(a => a.AmalgarId);
        mb.Entity<PishnevisLetter>().HasKey(p => p.PishnevisId);
        mb.Entity<OutgoingPishnevisLetter>().HasKey(p => p.PishnevisId);
        mb.Entity<LetterBayegani>().HasKey(b => b.BayeganiId);

        // InnerLetter با LetterSource کلید مشترک دارد (الگوی SourceKeyID طرح اصلی)
        mb.Entity<InnerLetter>()
            .HasOne(l => l.Source)
            .WithOne(s => s.InnerLetter!)
            .HasForeignKey<InnerLetter>(l => l.Id)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<InnerLetter>()
            .HasOne(l => l.Creator)
            .WithMany()
            .HasForeignKey(l => l.CreatorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<InnerLetter>().HasIndex(l => l.Number);
        mb.Entity<InnerLetter>().HasIndex(l => l.DateSabt);
        mb.Entity<InnerLetter>().HasIndex(l => l.CreatorUserId);

        // OutgoingLetter — همان الگوی کلید مشترک با SourceType=2
        mb.Entity<OutgoingLetter>()
            .HasOne(l => l.Source)
            .WithOne(s => s.OutgoingLetter!)
            .HasForeignKey<OutgoingLetter>(l => l.Id)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<OutgoingLetter>()
            .HasOne(l => l.Creator)
            .WithMany()
            .HasForeignKey(l => l.CreatorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<OutgoingLetter>().HasIndex(l => l.Number);
        mb.Entity<OutgoingLetter>().HasIndex(l => l.DateSabt);
        mb.Entity<OutgoingLetter>().HasIndex(l => l.CreatorUserId);
        mb.Entity<OutgoingLetter>().HasIndex(l => l.ReceiverOrganization);
        mb.Entity<OutgoingLetter>().HasIndex(l => l.SadereNumber);
        mb.Entity<OutgoingPishnevisLetter>().HasIndex(p => p.UserId);

        // امضا کنندگان نامه صادره
        mb.Entity<OutgoingLetterSigner>()
            .HasOne(s => s.Source)
            .WithMany(src => src.OutgoingSigners)
            .HasForeignKey(s => s.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<OutgoingLetterSigner>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<OutgoingLetterSigner>().HasIndex(s => s.SourceId);
        mb.Entity<OutgoingLetterSigner>().HasIndex(s => s.UserId);
        mb.Entity<OutgoingLetterSigner>().HasIndex(s => new { s.SourceId, s.UserId }).IsUnique();
        mb.Entity<OutgoingLetterSigner>().HasIndex(s => new { s.UserId, s.IsSigned });

        mb.Entity<Erja>()
            .HasOne(e => e.Source)
            .WithMany(s => s.Erjas)
            .HasForeignKey(e => e.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Erja>()
            .HasOne(e => e.UserSender)
            .WithMany()
            .HasForeignKey(e => e.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<Erja>()
            .HasOne(e => e.UserReciver)
            .WithMany()
            .HasForeignKey(e => e.ReciverUserId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<Erja>()
            .HasOne(e => e.Amalgar)
            .WithMany(a => a.Erjas)
            .HasForeignKey(e => e.AmalgarId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<Erja>().HasIndex(e => e.SourceId);
        mb.Entity<Erja>().HasIndex(e => e.ReciverUserId);
        mb.Entity<Erja>().HasIndex(e => e.SenderUserId);
        mb.Entity<Erja>().HasIndex(e => new { e.ReciverUserId, e.IsRead });

        mb.Entity<RelatedLetter>()
            .HasOne(r => r.Letter)
            .WithMany(s => s.RelatedLetters)
            .HasForeignKey(r => r.LetterId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<RelatedLetter>()
            .HasOne(r => r.RelateLetter)
            .WithMany(s => s.RelatedToLetters)
            .HasForeignKey(r => r.RelateLetterId)
            .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<PishnevisLetter>().HasIndex(p => p.UserId);
        mb.Entity<LetterBayegani>().HasIndex(b => b.UserId);

        // گروه‌های گیرندگان (پورت جدول Groups طرح کارفرما)
        mb.Entity<LetterGroup>().HasKey(g => g.GroupId);
        mb.Entity<LetterGroup>()
            .HasOne(g => g.Creator)
            .WithMany()
            .HasForeignKey(g => g.CreatorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<LetterGroupMember>()
            .HasOne(m => m.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<LetterGroupMember>()
            .HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<LetterGroupMember>().HasIndex(m => new { m.GroupId, m.UserId }).IsUnique();

        // ساختار شماره اندیکاتور (LetterStrature طرح کارفرما)
        mb.Entity<LetterStrature>().HasKey(s => s.StratureId);
        mb.Entity<LetterStrature>().HasIndex(s => s.TypeForm);

        // ==================== RBAC Configuration ====================
        mb.Entity<Role>().HasIndex(r => r.Name).IsUnique();
        mb.Entity<Permission>().HasIndex(p => new { p.Module, p.Action }).IsUnique();

        mb.Entity<RolePermission>()
            .HasKey(rp => new { rp.RoleId, rp.PermissionId });

        mb.Entity<RolePermission>()
            .HasOne(rp => rp.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(rp => rp.RoleId);

        mb.Entity<RolePermission>()
            .HasOne(rp => rp.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(rp => rp.PermissionId);

        mb.Entity<UserRole>()
            .HasKey(ur => new { ur.UserId, ur.RoleId });

        // ---------- تعطیلات شرکتی ----------
        mb.Entity<CompanyHoliday>().HasIndex(h => h.HolidayDate);
        mb.Entity<WorkCalendarDay>().HasIndex(d => d.Date).IsUnique();
        mb.Entity<WorkCalendarSettings>().HasIndex(s => s.Id).IsUnique();
        mb.Entity<AuditLog>().HasIndex(a => a.At);
        mb.Entity<AuditLog>().HasIndex(a => a.UserId);

        // ---------- بازه‌های ورود/خروج روزانه (حداکثر ۵ بازه در روز) ----------
        mb.Entity<AttendanceSegment>()
            .HasIndex(s => new { s.UserId, s.WorkDate, s.Seq })
            .IsUnique();
        mb.Entity<AttendanceSegment>()
            .HasOne(s => s.LinkedLeaveRequest)
            .WithMany()
            .HasForeignKey(s => s.LinkedLeaveRequestId)
            .OnDelete(DeleteBehavior.SetNull);

        // ---------- اتصال مبدأ دستور کار (اتصال عمومی بخش‌ها به دستور کار) ----------
        mb.Entity<WorkOrder>().HasIndex(w => new { w.SourceModule, w.SourceId });

        // ==================== ماژول انبارداری ====================

        // ---------- کالا: فیلدهای تکمیلی ----------
        mb.Entity<Product>().HasIndex(p => p.CategoryId);
        mb.Entity<Product>().HasIndex(p => p.TaxCode);
        mb.Entity<Product>().Property(p => p.SalePrice2).HasPrecision(18, 2);
        mb.Entity<Product>().Property(p => p.VatRate).HasPrecision(6, 2);
        mb.Entity<Product>().Property(p => p.DutyRate).HasPrecision(6, 2);
        mb.Entity<Product>().Property(p => p.OtherTaxRate).HasPrecision(6, 2);
        mb.Entity<Product>().Property(p => p.MinOrderQty).HasPrecision(18, 3);
        mb.Entity<Product>().Property(p => p.UnitFactor).HasPrecision(18, 4);
        mb.Entity<Product>().Property(p => p.Weight).HasPrecision(18, 3);
        mb.Entity<Product>().Property(p => p.Length).HasPrecision(18, 3);
        mb.Entity<Product>().Property(p => p.Width).HasPrecision(18, 3);
        mb.Entity<Product>().Property(p => p.Height).HasPrecision(18, 3);

        // ---------- انبار و گروه کالا ----------
        mb.Entity<Warehouse>().HasIndex(w => w.Code);
        mb.Entity<ProductCategory>().HasIndex(c => c.ParentId);

        // ---------- ویژگی‌های کالا ----------
        mb.Entity<ProductAttributeDef>().HasIndex(a => a.Name);
        mb.Entity<ProductAttributeDef>().HasIndex(a => a.CategoryId);
        mb.Entity<ProductAttributeOption>().HasIndex(o => o.AttributeId);
        mb.Entity<ProductAttributeValue>().HasIndex(v => new { v.ProductId, v.AttributeId }).IsUnique();
        mb.Entity<ProductAttributeValue>().Property(v => v.NumberValue).HasPrecision(18, 4);

        // ---------- نوع رسید و حواله ----------
        mb.Entity<InvDocType>().HasIndex(t => t.Code).IsUnique();
        mb.Entity<InvDocType>().HasIndex(t => t.Nature);

        // ---------- سند انبار ----------
        mb.Entity<InvDoc>().HasIndex(d => d.Number).IsUnique();
        mb.Entity<InvDoc>().HasIndex(d => d.Date);
        mb.Entity<InvDoc>().HasIndex(d => d.DocTypeId);
        mb.Entity<InvDoc>().HasIndex(d => d.WarehouseId);
        mb.Entity<InvDoc>().HasIndex(d => d.Status);
        mb.Entity<InvDoc>().Property(d => d.TotalQuantity).HasPrecision(18, 3);
        mb.Entity<InvDoc>().Property(d => d.TotalValue).HasPrecision(18, 2);
        mb.Entity<InvDoc>()
            .HasMany(d => d.Lines)
            .WithOne(l => l.Doc!)
            .HasForeignKey(l => l.DocId)
            .OnDelete(DeleteBehavior.Cascade);

        mb.Entity<InvDocLine>().HasIndex(l => l.ProductId);
        mb.Entity<InvDocLine>().Property(l => l.Quantity).HasPrecision(18, 3);
        mb.Entity<InvDocLine>().Property(l => l.UnitPrice).HasPrecision(18, 2);
        mb.Entity<InvDocLine>().Property(l => l.Discount).HasPrecision(18, 2);
        mb.Entity<InvDocLine>().Property(l => l.OutCost).HasPrecision(18, 4);

        // ---------- دفتر کاردکس ----------
        mb.Entity<InvLedgerEntry>().HasIndex(e => new { e.ProductId, e.WarehouseId, e.Date });
        mb.Entity<InvLedgerEntry>().HasIndex(e => e.DocId);
        mb.Entity<InvLedgerEntry>().Property(e => e.QtyIn).HasPrecision(18, 3);
        mb.Entity<InvLedgerEntry>().Property(e => e.QtyOut).HasPrecision(18, 3);
        mb.Entity<InvLedgerEntry>().Property(e => e.RemainingQty).HasPrecision(18, 3);
        mb.Entity<InvLedgerEntry>().Property(e => e.BalanceQty).HasPrecision(18, 3);
        mb.Entity<InvLedgerEntry>().Property(e => e.UnitCost).HasPrecision(18, 4);
        mb.Entity<InvLedgerEntry>().Property(e => e.ValueIn).HasPrecision(18, 2);
        mb.Entity<InvLedgerEntry>().Property(e => e.ValueOut).HasPrecision(18, 2);
        mb.Entity<InvLedgerEntry>().Property(e => e.BalanceValue).HasPrecision(18, 2);

        // ---------- مانده انبارداری ----------
        mb.Entity<InvStock>().HasIndex(s => new { s.ProductId, s.WarehouseId }).IsUnique();
        mb.Entity<InvStock>().Property(s => s.Quantity).HasPrecision(18, 3);
        mb.Entity<InvStock>().Property(s => s.Value).HasPrecision(18, 2);
        mb.Entity<InvStock>().Property(s => s.AvgCost).HasPrecision(18, 4);

        // ==================== ماژول حسابداری ====================
        mb.Entity<AccFiscalYear>().HasIndex(f => f.Title).IsUnique();
        mb.Entity<AccFiscalYear>().HasIndex(f => f.IsCurrent);

        mb.Entity<AccAccount>().HasIndex(a => a.Code).IsUnique();
        mb.Entity<AccAccount>().HasIndex(a => a.ParentId);
        mb.Entity<AccAccount>().HasIndex(a => a.Level);
        mb.Entity<AccAccount>()
          .HasOne(a => a.Parent).WithMany()
          .HasForeignKey(a => a.ParentId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AccVoucher>().HasIndex(v => new { v.FiscalYearId, v.Number }).IsUnique();
        // ---------- ابعاد تحلیلی ----------
        mb.Entity<AccDimension>().HasIndex(d => d.Code).IsUnique();
        mb.Entity<AccDimensionValue>().HasIndex(v => new { v.DimensionId, v.Code }).IsUnique();
        mb.Entity<AccDimensionValue>().HasIndex(v => v.ParentId);
        mb.Entity<AccDimensionValue>().HasIndex(v => v.CodeTree);
        // ---------- دارایی ثابت ----------
        mb.Entity<FixedAssetCategory>().HasIndex(c => c.Code).IsUnique();
        mb.Entity<FixedAsset>().HasIndex(a => a.Code).IsUnique();
        mb.Entity<FixedAsset>().HasIndex(a => a.CategoryId);
        mb.Entity<FixedAsset>().HasIndex(a => a.DimensionValueId);
        mb.Entity<FixedAssetDepreciationRun>().HasIndex(r => new { r.Year, r.Month }).IsUnique();
        mb.Entity<FixedAssetDepreciationLine>().HasIndex(l => new { l.RunId, l.AssetId }).IsUnique();
        mb.Entity<FixedAsset>().Property(a => a.PurchasePrice).HasPrecision(18, 2);
        mb.Entity<FixedAsset>().Property(a => a.SalvageValue).HasPrecision(18, 2);
        mb.Entity<FixedAsset>().Property(a => a.AccumulatedDepreciation).HasPrecision(18, 2);
        mb.Entity<FixedAssetDepreciationRun>().Property(r => r.TotalAmount).HasPrecision(18, 2);
        mb.Entity<FixedAssetDepreciationLine>().Property(l => l.Amount).HasPrecision(18, 2);
        mb.Entity<FixedAssetDepreciationLine>().Property(l => l.AccumulatedAfter).HasPrecision(18, 2);
        mb.Entity<FixedAssetDepreciationLine>().Property(l => l.BookValueAfter).HasPrecision(18, 2);
        // ---------- بودجه ----------
        mb.Entity<Budget>().HasIndex(b => new { b.FiscalYearId, b.DimensionValueId, b.Name }).IsUnique();
        mb.Entity<BudgetItem>().HasIndex(i => new { i.BudgetId, i.AccAccountId }).IsUnique();
        mb.Entity<BudgetTransaction>().HasIndex(t => t.BudgetId);
        mb.Entity<BudgetTransaction>().HasIndex(t => t.VoucherId);
        mb.Entity<Budget>().Property(b => b.TotalAmount).HasPrecision(18, 2);
        mb.Entity<BudgetItem>().Property(i => i.PlannedAmount).HasPrecision(18, 2);
        mb.Entity<BudgetItem>().Property(i => i.CommittedAmount).HasPrecision(18, 2);
        mb.Entity<BudgetItem>().Property(i => i.ActualAmount).HasPrecision(18, 2);
        mb.Entity<BudgetTransaction>().Property(t => t.Amount).HasPrecision(18, 2);

        // ---------- رفتار حذف ماژول‌های جدید (سازگار با SQL Server) ----------
        // SQL Server با «مسیرهای آبشاری چندگانه» (multiple cascade paths) سازگار نیست؛
        // برای همین FKهای الزامی به Restrict و FKهای اختیاری به SetNull تنظیم شده‌اند.
        // نمونه: BudgetTransaction هم مستقیم و هم از مسیر BudgetItem به Budget می‌رسد.
        mb.Entity<AccVoucher>()
          .HasOne(v => v.FiscalYear).WithMany()
          .HasForeignKey(v => v.FiscalYearId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AccVoucherLine>()
          .HasOne(l => l.Voucher).WithMany(v => v.Lines)
          .HasForeignKey(l => l.VoucherId)
          .OnDelete(DeleteBehavior.Cascade); // حذف سند = حذف اقلام (ترکیب)

        mb.Entity<AccVoucherLine>()
          .HasOne(l => l.Account).WithMany()
          .HasForeignKey(l => l.AccountId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AccVoucherLine>()
          .HasOne(l => l.DimensionValue).WithMany()
          .HasForeignKey(l => l.DimensionValueId)
          .OnDelete(DeleteBehavior.SetNull);

        mb.Entity<AccDimensionValue>()
          .HasOne(v => v.Dimension).WithMany()
          .HasForeignKey(v => v.DimensionId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AccDimensionValue>()
          .HasOne(v => v.Parent).WithMany()
          .HasForeignKey(v => v.ParentId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<FixedAsset>()
          .HasOne(a => a.Category).WithMany()
          .HasForeignKey(a => a.CategoryId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<FixedAsset>()
          .HasOne(a => a.DimensionValue).WithMany()
          .HasForeignKey(a => a.DimensionValueId)
          .OnDelete(DeleteBehavior.SetNull);

        mb.Entity<FixedAssetDepreciationLine>()
          .HasOne(l => l.Run).WithMany(r => r.Lines)
          .HasForeignKey(l => l.RunId)
          .OnDelete(DeleteBehavior.Cascade); // حذف اجرا = حذف اقلام (ترکیب)

        mb.Entity<FixedAssetDepreciationLine>()
          .HasOne(l => l.Asset).WithMany()
          .HasForeignKey(l => l.AssetId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<Budget>()
          .HasOne(b => b.FiscalYear).WithMany()
          .HasForeignKey(b => b.FiscalYearId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<Budget>()
          .HasOne(b => b.DimensionValue).WithMany()
          .HasForeignKey(b => b.DimensionValueId)
          .OnDelete(DeleteBehavior.SetNull);

        mb.Entity<BudgetItem>()
          .HasOne(i => i.Budget).WithMany(b => b.Items)
          .HasForeignKey(i => i.BudgetId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<BudgetItem>()
          .HasOne(i => i.AccAccount).WithMany()
          .HasForeignKey(i => i.AccAccountId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<BudgetTransaction>()
          .HasOne(t => t.Budget).WithMany()
          .HasForeignKey(t => t.BudgetId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<BudgetTransaction>()
          .HasOne(t => t.BudgetItem).WithMany()
          .HasForeignKey(t => t.BudgetItemId)
          .OnDelete(DeleteBehavior.Restrict);

        // ---------- سامانه مودیان ----------
        mb.Entity<MoadianInvoice>().HasIndex(i => new { i.FiscalPeriodId, i.Number }).IsUnique();
        // حذف دوره نباید فاکتورهای آن را آبشار بزند
        mb.Entity<MoadianInvoice>()
          .HasOne(i => i.FiscalPeriod).WithMany()
          .HasForeignKey(i => i.FiscalPeriodId)
          .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<MoadianInvoice>().HasIndex(i => i.Status);
        mb.Entity<MoadianInvoice>().HasIndex(i => i.ReferenceId);
        mb.Entity<MoadianInvoice>().HasIndex(i => i.FacInvoiceId);
        mb.Entity<MoadianFiscalPeriod>().HasIndex(p => new { p.Year, p.Month }).IsUnique();
        mb.Entity<MoadianCpc>().HasIndex(c => c.Code).IsUnique();
        mb.Entity<MoadianLog>().HasIndex(l => l.InvoiceId);
        mb.Entity<MoadianInvoice>().Property(i => i.TotalGross).HasPrecision(18, 2);
        mb.Entity<MoadianInvoice>().Property(i => i.TotalDiscount).HasPrecision(18, 2);
        mb.Entity<MoadianInvoice>().Property(i => i.TotalTaxable).HasPrecision(18, 2);
        mb.Entity<MoadianInvoice>().Property(i => i.TotalVat).HasPrecision(18, 2);
        mb.Entity<MoadianInvoice>().Property(i => i.TotalNet).HasPrecision(18, 2);
        mb.Entity<MoadianInvoiceLine>().Property(l => l.Quantity).HasPrecision(18, 3);
        mb.Entity<MoadianInvoiceLine>().Property(l => l.UnitPrice).HasPrecision(18, 2);
        mb.Entity<MoadianInvoiceLine>().Property(l => l.Discount).HasPrecision(18, 2);
        mb.Entity<MoadianInvoiceLine>().Property(l => l.VatRate).HasPrecision(5, 2);
        mb.Entity<MoadianInvoiceLine>().Property(l => l.VatAmount).HasPrecision(18, 2);
        mb.Entity<MoadianInvoiceLine>().Property(l => l.Total).HasPrecision(18, 2);
        mb.Entity<MoadianSetting>().Property(s => s.DefaultVatRate).HasPrecision(5, 2);
        mb.Entity<AccVoucher>().HasIndex(v => v.Date);
        mb.Entity<AccVoucher>().HasIndex(v => v.Status);
        mb.Entity<AccVoucher>().HasIndex(v => new { v.Source, v.SourceId });
        mb.Entity<AccVoucher>().Property(v => v.TotalDebit).HasPrecision(18, 2);
        mb.Entity<AccVoucher>().Property(v => v.TotalCredit).HasPrecision(18, 2);
        mb.Entity<AccVoucher>()
          .HasOne(v => v.FiscalYear).WithMany()
          .HasForeignKey(v => v.FiscalYearId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AccVoucherLine>().HasIndex(l => l.AccountId);
        mb.Entity<AccVoucherLine>().HasIndex(l => l.PartyId);
        mb.Entity<AccVoucherLine>().Property(l => l.Debit).HasPrecision(18, 2);
        mb.Entity<AccVoucherLine>().Property(l => l.Credit).HasPrecision(18, 2);
        mb.Entity<AccVoucherLine>()
          .HasOne(l => l.Voucher).WithMany(v => v.Lines)
          .HasForeignKey(l => l.VoucherId)
          .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<AccVoucherLine>()
          .HasOne(l => l.Account).WithMany()
          .HasForeignKey(l => l.AccountId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AccInvRule>().HasIndex(r => r.DocTypeId).IsUnique();
        mb.Entity<AccInvRule>()
          .HasOne(r => r.DocType).WithMany()
          .HasForeignKey(r => r.DocTypeId)
          .OnDelete(DeleteBehavior.Cascade);

        // ============ ماژول فاکتور ============
        mb.Entity<FacInvoice>().HasIndex(i => new { i.Kind, i.Number }).IsUnique();
        mb.Entity<FacInvoice>().HasIndex(i => i.Date);
        mb.Entity<FacInvoice>().HasIndex(i => i.Status);
        mb.Entity<FacInvoice>().HasIndex(i => i.PartyId);
        mb.Entity<FacInvoice>().HasIndex(i => i.InvDocId);
        mb.Entity<FacInvoice>().HasIndex(i => i.VoucherId);
        foreach (var prop in new[] { "TotalGross", "TotalLineDiscount", "InvoiceDiscount",
                                     "TotalTaxable", "TotalVat", "ShippingCost", "TotalNet" })
            mb.Entity<FacInvoice>().Property(prop).HasPrecision(18, 2);
        mb.Entity<FacInvoice>()
          .HasOne(i => i.Party).WithMany()
          .HasForeignKey(i => i.PartyId)
          .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<FacInvoice>()
          .HasOne(i => i.Warehouse).WithMany()
          .HasForeignKey(i => i.WarehouseId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<FacInvoiceLine>().HasIndex(l => l.InvoiceId);
        mb.Entity<FacInvoiceLine>().HasIndex(l => l.ProductId);
        foreach (var prop in new[] { "Quantity", "UnitPrice", "DiscountPercent", "Discount",
                                     "VatRate", "VatAmount", "Taxable", "Total" })
            mb.Entity<FacInvoiceLine>().Property(prop).HasPrecision(18, 2);
        mb.Entity<FacInvoiceLine>()
          .HasOne(l => l.Invoice).WithMany(i => i.Lines)
          .HasForeignKey(l => l.InvoiceId)
          .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<FacInvoiceLine>()
          .HasOne(l => l.Product).WithMany()
          .HasForeignKey(l => l.ProductId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<FacRule>().HasIndex(r => r.Kind).IsUnique();
        mb.Entity<FacRule>()
          .HasOne(r => r.DocType).WithMany()
          .HasForeignKey(r => r.DocTypeId)
          .OnDelete(DeleteBehavior.SetNull);

        // ============ ماژول خزانه‌داری ============

        // ---------- صندوق / بانک ----------
        mb.Entity<TrsAccount>().HasIndex(a => a.Code).IsUnique();
        mb.Entity<TrsAccount>().HasIndex(a => a.Kind);
        mb.Entity<TrsAccount>().Property(a => a.OpeningBalance).HasPrecision(18, 2);
        mb.Entity<TrsAccount>()
          .HasOne(a => a.Account).WithMany()
          .HasForeignKey(a => a.AccountId)
          .OnDelete(DeleteBehavior.SetNull);

        // ---------- سند خزانه ----------
        mb.Entity<TrsVoucher>().HasIndex(v => new { v.Kind, v.Number }).IsUnique();
        mb.Entity<TrsVoucher>().HasIndex(v => v.Date);
        mb.Entity<TrsVoucher>().HasIndex(v => v.Status);
        mb.Entity<TrsVoucher>().HasIndex(v => v.PartyId);
        mb.Entity<TrsVoucher>().HasIndex(v => v.VoucherId);
        mb.Entity<TrsVoucher>().HasIndex(v => v.InvoiceId);
        foreach (var prop in new[] { "TotalAmount", "CashAmount", "ChequeAmount",
                                     "DiscountAmount", "FeeAmount" })
            mb.Entity<TrsVoucher>().Property(prop).HasPrecision(18, 2);
        mb.Entity<TrsVoucher>()
          .HasOne(v => v.Party).WithMany()
          .HasForeignKey(v => v.PartyId)
          .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<TrsVoucher>()
          .HasOne(v => v.FromAccount).WithMany()
          .HasForeignKey(v => v.FromAccountId)
          .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<TrsVoucher>()
          .HasOne(v => v.ToAccount).WithMany()
          .HasForeignKey(v => v.ToAccountId)
          .OnDelete(DeleteBehavior.Restrict);

        // ---------- سطر سند خزانه ----------
        mb.Entity<TrsVoucherLine>().HasIndex(l => l.TrsVoucherId);
        mb.Entity<TrsVoucherLine>().HasIndex(l => l.TrsAccountId);
        mb.Entity<TrsVoucherLine>().HasIndex(l => l.ChequeId);
        mb.Entity<TrsVoucherLine>().Property(l => l.Amount).HasPrecision(18, 2);
        mb.Entity<TrsVoucherLine>()
          .HasOne(l => l.TrsVoucher).WithMany(v => v.Lines)
          .HasForeignKey(l => l.TrsVoucherId)
          .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<TrsVoucherLine>()
          .HasOne(l => l.TrsAccount).WithMany()
          .HasForeignKey(l => l.TrsAccountId)
          .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<TrsVoucherLine>()
          .HasOne(l => l.Cheque).WithMany()
          .HasForeignKey(l => l.ChequeId)
          .OnDelete(DeleteBehavior.SetNull);

        // ---------- چک ----------
        mb.Entity<TrsCheque>().HasIndex(c => new { c.Kind, c.Number });
        mb.Entity<TrsCheque>().HasIndex(c => c.DueDate);
        mb.Entity<TrsCheque>().HasIndex(c => c.Status);
        mb.Entity<TrsCheque>().HasIndex(c => c.PartyId);
        mb.Entity<TrsCheque>().HasIndex(c => c.TrsVoucherId);
        mb.Entity<TrsCheque>().Property(c => c.Amount).HasPrecision(18, 2);
        mb.Entity<TrsCheque>()
          .HasOne(c => c.Party).WithMany()
          .HasForeignKey(c => c.PartyId)
          .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<TrsCheque>()
          .HasOne(c => c.TrsAccount).WithMany()
          .HasForeignKey(c => c.TrsAccountId)
          .OnDelete(DeleteBehavior.Restrict);

        // ---------- عملیات چک ----------
        mb.Entity<TrsChequeAction>().HasIndex(a => a.ChequeId);
        mb.Entity<TrsChequeAction>()
          .HasOne(a => a.Cheque).WithMany(c => c.Actions)
          .HasForeignKey(a => a.ChequeId)
          .OnDelete(DeleteBehavior.Cascade);

        // ---------- قواعد خزانه ----------
        mb.Entity<TrsRule>().HasIndex(r => r.Kind).IsUnique();

        // ============ ماژول انبارگردانی و بارکد ============

        // ---------- بارکد ----------
        mb.Entity<BcdBarcode>().HasIndex(b => b.Code).IsUnique();
        mb.Entity<BcdBarcode>().HasIndex(b => b.ProductId);
        mb.Entity<BcdBarcode>().Property(b => b.PackQty).HasPrecision(18, 3);
        mb.Entity<BcdBarcode>()
          .HasOne(b => b.Product).WithMany()
          .HasForeignKey(b => b.ProductId)
          .OnDelete(DeleteBehavior.Cascade);

        // ---------- دوره انبارگردانی ----------
        mb.Entity<StkSession>().HasIndex(s => s.Number).IsUnique();
        mb.Entity<StkSession>().HasIndex(s => s.WarehouseId);
        mb.Entity<StkSession>().HasIndex(s => s.Status);
        mb.Entity<StkSession>().HasIndex(s => s.Date);
        mb.Entity<StkSession>()
          .HasOne(s => s.Warehouse).WithMany()
          .HasForeignKey(s => s.WarehouseId)
          .OnDelete(DeleteBehavior.Restrict);

        // ---------- اقلام شمارش ----------
        mb.Entity<StkLine>().HasIndex(l => l.SessionId);
        mb.Entity<StkLine>().HasIndex(l => new { l.SessionId, l.ProductId }).IsUnique();
        mb.Entity<StkLine>().Property(l => l.SystemQty).HasPrecision(18, 3);
        mb.Entity<StkLine>().Property(l => l.CountedQty).HasPrecision(18, 3);
        mb.Entity<StkLine>().Property(l => l.UnitCost).HasPrecision(18, 2);
        mb.Entity<StkLine>()
          .HasOne(l => l.Session).WithMany(s => s.Lines)
          .HasForeignKey(l => l.SessionId)
          .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<StkLine>()
          .HasOne(l => l.Product).WithMany()
          .HasForeignKey(l => l.ProductId)
          .OnDelete(DeleteBehavior.Restrict);

        // ---------- تاریخچه اسکن ----------
        mb.Entity<StkScan>().HasIndex(s => s.SessionId);
        mb.Entity<StkScan>().HasIndex(s => s.LineId);
        mb.Entity<StkScan>().Property(s => s.Quantity).HasPrecision(18, 3);
        mb.Entity<StkScan>()
          .HasOne(s => s.Session).WithMany()
          .HasForeignKey(s => s.SessionId)
          .OnDelete(DeleteBehavior.Cascade);
    }
}
