using Microsoft.EntityFrameworkCore;

namespace LicenseSigner.Data;

/// <summary>
/// پایگاه‌دادهٔ پنل پشتیبانی. <b>هیچ ربطی به InventoryDb مشتری ندارد</b> و هیچ‌گاه
/// کلید خصوصی/رمز در آن نوشته نمی‌شود.
/// </summary>
public sealed class SupportDbContext(DbContextOptions<SupportDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<ContactPerson> Contacts => Set<ContactPerson>();
    public DbSet<Installation> Installations => Set<Installation>();
    public DbSet<LicenseRequest> Requests => Set<LicenseRequest>();
    public DbSet<RequestEvent> RequestEvents => Set<RequestEvent>();
    public DbSet<RequestAttachment> Attachments => Set<RequestAttachment>();
    public DbSet<IdentityCheck> IdentityChecks => Set<IdentityCheck>();
    public DbSet<LicenseDraft> Drafts => Set<LicenseDraft>();
    public DbSet<IssuedLicense> IssuedLicenses => Set<IssuedLicense>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<Preset> Presets => Set<Preset>();
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

        b.Entity<Installation>(e =>
        {
            e.HasIndex(x => x.InstallationId).IsUnique();
            e.Property(x => x.InstallationId).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.Customer).WithMany(x => x.Installations).HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<LicenseRequest>(e =>
        {
            e.HasIndex(x => x.RequestNo).IsUnique();
            e.HasIndex(x => new { x.InstallationId, x.PayloadSha256 });
            e.Property(x => x.RequestNo).HasMaxLength(30).IsRequired();
            e.Property(x => x.InstallationId).HasMaxLength(64).IsRequired();
            e.Property(x => x.PayloadSha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.ContactName).HasMaxLength(100);
            e.Property(x => x.ContactPhone).HasMaxLength(30);
            e.Property(x => x.ContactEmail).HasMaxLength(150);
            e.Property(x => x.CustomerNote).HasMaxLength(500);
            e.HasOne(x => x.Customer).WithMany(x => x.Requests).HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.InstallationRef).WithMany(x => x.Requests).HasForeignKey(x => x.InstallationRefId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<RequestEvent>(e =>
        {
            e.HasOne(x => x.Request).WithMany(x => x.Events).HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Kind).HasMaxLength(30).IsRequired();
            e.HasIndex(x => new { x.RequestId, x.At });
        });

        b.Entity<RequestAttachment>(e =>
        {
            e.HasOne(x => x.Request).WithMany(x => x.Attachments).HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(200).IsRequired();
        });

        b.Entity<IdentityCheck>(e =>
        {
            e.HasOne(x => x.Request).WithMany(x => x.IdentityChecks).HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.VerifiedWith).HasMaxLength(150).IsRequired();
        });

        b.Entity<LicenseDraft>(e =>
        {
            e.HasOne(x => x.Request).WithMany(x => x.Drafts).HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.RequestId).IsUnique();
        });

        b.Entity<IssuedLicense>(e =>
        {
            e.HasIndex(x => x.Serial).IsUnique();
            e.Property(x => x.Serial).HasMaxLength(40).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.Request).WithMany(x => x.Licenses).HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Customer).WithMany(x => x.Licenses).HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Delivery>(e =>
        {
            e.HasOne(x => x.IssuedLicense).WithMany(x => x.Deliveries).HasForeignKey(x => x.IssuedLicenseId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Recipient).HasMaxLength(200);
        });

        b.Entity<Preset>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
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
