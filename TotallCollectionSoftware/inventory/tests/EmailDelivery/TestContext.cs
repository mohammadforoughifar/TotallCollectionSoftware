using Microsoft.EntityFrameworkCore;

// The email entities, service, coordinator, transport, store and schema are production files.
// Only the unrelated app context/letter/user/printing surface is minimal test infrastructure.
namespace Inventory.Api.Data
{
    public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<OtoEmail> OtoEmails => Set<OtoEmail>();
        public DbSet<OtoSentEmail> OtoSentEmails => Set<OtoSentEmail>();
        public DbSet<OtoInboxEmail> OtoInboxEmails => Set<OtoInboxEmail>();
        public DbSet<OtoEmailFolder> OtoEmailFolders => Set<OtoEmailFolder>();
        public DbSet<OtoEmailAttachment> OtoEmailAttachments => Set<OtoEmailAttachment>();
        public DbSet<OtoEmailBankEntry> OtoEmailBank => Set<OtoEmailBankEntry>();
        public DbSet<EmailSendAttempt> EmailSendAttempts => Set<EmailSendAttempt>();
        public DbSet<OutgoingLetter> OutgoingLetters => Set<OutgoingLetter>();
        public DbSet<OutgoingLetterSigner> OutgoingLetterSigners => Set<OutgoingLetterSigner>();
        public DbSet<AppAttachment> AppAttachments => Set<AppAttachment>();
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<OtoInboxEmail>().Ignore(e => e.Attachments);
            mb.Entity<OtoSentEmail>().Ignore(e => e.Attachments);
        }
    }
    public sealed class User { public int Id { get; set; } public string Username { get; set; } = ""; public string? FirstName { get; set; } public string? LastName { get; set; } }
    public sealed class LetterSource { public int Id { get; set; } }
    public sealed class OutgoingLetter
    {
        public int Id { get; set; }
        public bool IsDelete { get; set; }
        public int CreatorUserId { get; set; }
        public bool DabirkhaneSabt { get; set; }
        public int? DabirkhaneUserId { get; set; }
        public DateTime? DateDabirkhane { get; set; }
        public string? DestRegNumber { get; set; }
        public string? SendMethod { get; set; }
        public string? DabirkhaneNote { get; set; }
        public string? DestEmail { get; set; }
        public string? DelivererName { get; set; }
        public string? TrackingCode { get; set; }
        public string? DestFax { get; set; }
        public string? SadereNumber { get; set; }
        public string Title { get; set; } = "";
        public string? Text { get; set; }
        public DateTime DateSabt { get; set; }
        public DateTime? DateSadere { get; set; }
        public string ReceiverOrganization { get; set; } = "";
        public string? ReceiverName { get; set; }
        public LetterSource Source { get; set; } = new();
    }
    public sealed class OutgoingLetterSigner
    {
        public int Id { get; set; }
        public int SourceId { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public int Order { get; set; }
        public bool IsDelete { get; set; }
        public bool IsSigned { get; set; }
    }
    public sealed class AppAttachment
    {
        public int Id { get; set; }
        public string Module { get; set; } = "";
        public int RefId { get; set; }
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public string? FilePath { get; set; }
        public byte[]? Data { get; set; }
    }
}
namespace Inventory.Api.Services.Office.Outgoing
{
    public interface IOutgoingLetterPrintService
    {
        Task<byte[]?> GeneratePdfAsync(int letterId, string size, bool withCopy = true, bool withMargin = true, bool withLetterhead = true, bool requireSignatures = true);
    }
}
