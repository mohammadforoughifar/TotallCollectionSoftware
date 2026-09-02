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
        mb.Entity<ProjectAttach>().HasIndex(a => a.ProjectId);

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
    }
}
