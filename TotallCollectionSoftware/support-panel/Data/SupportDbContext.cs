using Microsoft.EntityFrameworkCore;

namespace SupportPanel.Data;

/// <summary>
/// پایگاه‌دادهٔ پنل پشتیبانی. <b>هیچ ربطی به InventoryDb مشتری ندارد</b>.
///
/// توجه: موتور لایسنس از این پنل حذف شده است. جدول‌های قدیمی (<c>LicenseRequests</c>،
/// <c>IssuedLicenses</c>، <c>Installations</c>، …) در دیتابیس نصب‌های موجود باقی
/// می‌مانند ولی دیگر نگاشت نمی‌شوند؛ اسکریپت پاک‌سازی در
/// <c>docs/sql/drop-license-tables.sql</c> است.
/// </summary>
public sealed class SupportDbContext(DbContextOptions<SupportDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<ContactPerson> Contacts => Set<ContactPerson>();
    public DbSet<SupportUser> Users => Set<SupportUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<MailTemplate> MailTemplates => Set<MailTemplate>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Customer>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(150);
            e.Property(x => x.NationalId).HasMaxLength(20);
            e.Property(x => x.Address).HasMaxLength(400);
        });

        b.Entity<ContactPerson>(e =>
        {
            e.HasOne(x => x.Customer).WithMany(x => x.Contacts).HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(x => new { x.CustomerId, x.Name });
        });

        b.Entity<SupportUser>(e =>
        {
            e.HasIndex(x => x.UserName).IsUnique();
            e.Property(x => x.UserName).HasMaxLength(60).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(120);
            e.Property(x => x.PasswordHash).HasMaxLength(400).IsRequired();
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => x.At);
            e.HasIndex(x => x.Action);
            e.Property(x => x.Action).HasMaxLength(40).IsRequired();
            e.Property(x => x.Entity).HasMaxLength(60).IsRequired();
            e.Property(x => x.PrevHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.Hash).HasMaxLength(64).IsRequired();
        });

        b.Entity<MailTemplate>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(40).IsRequired();
        });

        b.Entity<Setting>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(60).IsRequired();
        });
    }
}
