using System.Data.Common;
using System.Reflection;
using Inventory.Api.Data;
using Inventory.Api.Services.Office.Email;
using Inventory.Api.Services.Office.Outgoing;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace EmailDeliveryTests
{
    public sealed class ServerFixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "email-delivery-synthetic-" + Guid.NewGuid().ToString("N"));
        public AppDbContext Db { get; }
        public DbContextOptions<AppDbContext> Options { get; }
        public EmailDispatchStore Store { get; }
        public FakeSmtp Smtp { get; } = new();
        public FakePrint Print { get; } = new();
        public AttachmentFailure AttachmentFailure { get; } = new();
        public FakeNotification Notify { get; } = new();
        public EmailService Email { get; }
        public EmailDispatchCoordinator Coordinator { get; }
        public BankProxy Bank { get; }
        public OutgoingLetterService Letters { get; }
        public ServerFixture()
        {
            Directory.CreateDirectory(Root); Directory.CreateDirectory(Path.Combine(Root, "wwwroot"));
            Options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={Path.Combine(Root, "test.db")};Pooling=False;Default Timeout=3")
                .AddInterceptors(AttachmentFailure).Options;
            Db = new(Options); Store = new(Db); Coordinator = new(Store, Smtp, NullLogger<EmailDispatchCoordinator>.Instance);
            var bank = DispatchProxy.Create<IEmailBankService, BankProxy>(); Bank = (BankProxy)(object)bank;
            Bank.Inner = new EmailBankService(Db);
            Email = new(Db, new TestEnvironment(Root), NullLogger<EmailService>.Instance, Print, bank, Store, Coordinator);
            Letters = new(Db, Email, Notify);
        }
        public async Task Initialize()
        {
            await Db.Database.EnsureCreatedAsync();
            // Exercise the additive production bootstrap, not just EF-created tables.
            await Db.Database.ExecuteSqlRawAsync("DROP TABLE Oto_EmailSendAttempts;");
            await EmailDeliverySchemaV1.EnsureAsync(Db); await EmailDeliverySchemaV1.EnsureAsync(Db);
            Db.Users.AddRange(new User { Id = 1, Username = "synthetic-one" }, new User { Id = 2, Username = "synthetic-two" });
            Db.OtoEmails.AddRange(Account(10, 1, true), Account(11, 1, false), Account(12, 2, false), Account(13, 2, true));
            Db.OutgoingLetters.AddRange(Letter(100), Letter(101), Letter(102));
            await Db.SaveChangesAsync(); Db.ChangeTracker.Clear();
        }
        public static OtoEmail Account(int id = 10, int owner = 1, bool official = true) => new()
        {
            EmailId = id, UserId = owner, IsDabirkhane = official, IsActive = true, EmailAddress = $"synthetic{id}@example.invalid",
            Smtp = "127.0.0.1", SmtpPort = 9, SmtpSsl = false, Password = "synthetic-password-only"
        };
        public static OutgoingLetter Letter(int id) => new()
        {
            Id = id, CreatorUserId = 1, SadereNumber = "TEST/" + id, Title = "نامه آزمایشی", Text = "<p>synthetic body only</p>",
            ReceiverOrganization = "synthetic organization", Source = new() { Id = id }, DateSabt = DateTime.UtcNow
        };
        public static EmailComposeDto Compose(int account = 10) => new() { RequestId = Guid.NewGuid(), EmailAccountId = account, To = "test@example.invalid", Subject = "synthetic subject", Body = "synthetic body only" };
        public static DabirkhaneRegisterDto Registration() => new() { EmailRequestId = Guid.NewGuid(), EmailAccountId = 10, DestEmail = "receiver@example.invalid", SendMethod = "ایمیل", SendByEmail = true };
        public static EmailDispatchIdentity Identity(Guid? id = null, string? guard = null, Guid? previous = null, bool risk = false) => new(
            id ?? Guid.NewGuid(), new string('a', 64), 1, 10, "test@example.invalid", "receiver@example.invalid", "synthetic", "synthetic@example.invalid", guard == null ? null : 100, guard, previous, risk);
        public static MimeMessage Message()
        {
            var message = new MimeMessage { Subject = "synthetic", MessageId = $"{Guid.NewGuid():N}@example.invalid", Body = new TextPart("plain") { Text = "synthetic body only" } };
            message.From.Add(MailboxAddress.Parse("sender@example.invalid")); message.To.Add(MailboxAddress.Parse("receiver@example.invalid"));
            return message;
        }
        public static Task<PreparedEmail> Prepared(EmailSendAttempt attempt) => Task.FromResult(new PreparedEmail(Account(), Message(), 55));
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); Directory.Delete(Root, true); }
    }
    public sealed class FakeSmtp : IEmailSmtpTransport
    {
        public int Calls;
        public EmailDeliveryStatus Outcome { get; set; } = EmailDeliveryStatus.Accepted;
        public bool Throw { get; set; }
        public Func<Task>? Before { get; set; }
        public string? MessageId { get; private set; }
        public int Attachments { get; private set; }
        public async Task<EmailSmtpOutcome> SendAsync(OtoEmail account, MimeMessage message)
        {
            Interlocked.Increment(ref Calls); MessageId = message.MessageId; Attachments = message.Attachments.Count();
            if (Before != null) await Before();
            if (Throw) throw new IOException("synthetic transport interrupted with private detail");
            return new() { Status = Outcome, Stage = "send", Code = Outcome == EmailDeliveryStatus.Accepted ? "SMTP_ACCEPTED" : Outcome == EmailDeliveryStatus.Failed ? "SMTP_REJECTED" : "SMTP_RESULT_UNKNOWN", Message = "safe synthetic outcome", AcceptedAtUtc = Outcome == EmailDeliveryStatus.Accepted ? DateTime.UtcNow : null };
        }
    }
    public sealed class FakePrint : IOutgoingLetterPrintService
    {
        public bool Throw { get; set; }
        public bool Empty { get; set; }
        public bool RequiresSignatures { get; private set; }
        public Task<byte[]?> GeneratePdfAsync(int letterId, string size, bool withCopy = true, bool withMargin = true, bool withLetterhead = true, bool requireSignatures = true)
        {
            RequiresSignatures = requireSignatures;
            if (Throw) throw new IOException("synthetic print fault containing private paths");
            return Task.FromResult<byte[]?>(Empty ? Array.Empty<byte>() : "%PDF-synthetic"u8.ToArray());
        }
    }
    public class BankProxy : DispatchProxy
    {
        public IEmailBankService Inner { get; set; } = default!;
        public bool Fail { get; set; }
        public int RememberCalls { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IEmailBankService.RememberAsync))
            {
                RememberCalls++;
                if (Fail) return Task.FromException<EmailBankSaveResultDto>(new IOException("synthetic contacts private detail"));
            }
            return targetMethod.Invoke(Inner, args);
        }
    }
    public sealed class AttachmentFailure : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Fail && data.Context!.ChangeTracker.Entries<OtoEmailAttachment>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("synthetic attachment metadata save fault");
            return ValueTask.FromResult(result);
        }
    }
    public sealed class FaultStore(IEmailDispatchStore inner) : IEmailDispatchStore
    {
        public bool FailClaim;
        public int FailSaveAt, Saves;
        public Task<EmailDispatchClaim> ClaimAsync(EmailDispatchIdentity identity) => FailClaim ? Task.FromException<EmailDispatchClaim>(new IOException("synthetic DB fault")) : inner.ClaimAsync(identity);
        public Task SaveAsync(EmailSendAttempt attempt) => ++Saves == FailSaveAt ? Task.FromException(new IOException("synthetic final-save fault")) : inner.SaveAsync(attempt);
        public Task<EmailSendAttempt?> FindAsync(Guid id) => inner.FindAsync(id);
        public Task<EmailDeliveryAttemptDto?> GetStatusAsync(Guid id, int userId, bool isDabirkhaneAdmin, bool personalOnly = false) => inner.GetStatusAsync(id, userId, isDabirkhaneAdmin, personalOnly);
        public Task<PagedResult<EmailDeliveryAttemptDto>> GetHistoryAsync(int userId, bool admin, bool personalOnly, int? account, int? letter, int? sent, EmailDeliveryStatus? status, int page, int size) => inner.GetHistoryAsync(userId, admin, personalOnly, account, letter, sent, status, page, size);
    }
    public sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SyntheticEmailTests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
    }
    public sealed class FakeNotification
    {
        public int Calls;
        public bool Fail, FailBroadcast;
        public Task SendAsync(int user, string title, string body, string sender, string source, string url)
        { Calls++; return Fail ? Task.FromException(new IOException("synthetic notification error")) : Task.CompletedTask; }
        public Task BroadcastChangedAsync(string scope) => FailBroadcast ? Task.FromException(new IOException("synthetic broadcast error")) : Task.CompletedTask;
    }
    public sealed class NoSqlServerNetwork : DbConnectionInterceptor
    {
        public int Suppressed;
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        { Suppressed++; return ValueTask.FromResult(InterceptionResult.Suppress()); }
    }
    public sealed class CaptureSqlServerCommand : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Commands.Add(command.CommandText); return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1)); }
    }
}
// Compile the actual controller action implementations against a permission stub. Account
// authorization inside the real EmailService/EmailDispatchStore is NOT stubbed.
namespace Inventory.Api.Controllers
{
    public class RbacControllerBase(AppDbContext db) : ControllerBase
    {
        protected AppDbContext Db => db;
        public int UserId { get; set; } = 1;
        protected int MyUserId => UserId;
        public HashSet<string> Permissions { get; } = new() { "Email.Read", "Email.Create", "Email.Personal", "OutgoingLetters.Dabirkhane" };
        protected Task<bool> HasAsync(string module, string action) => Task.FromResult(Permissions.Contains(module + "." + action));
        protected async Task<IActionResult?> ForbiddenUnlessAsync(string module, string action) => await HasAsync(module, action) ? null : StatusCode(403);
    }
}
namespace Inventory.Api.Services.Office.Outgoing
{
    // The large, unrelated letter service is omitted. Its production registration partial is linked.
    public partial class OutgoingLetterService(AppDbContext db, IEmailService email, EmailDeliveryTests.FakeNotification notify)
    {
        private readonly AppDbContext _db = db;
        private readonly IEmailService _email = email;
        private readonly EmailDeliveryTests.FakeNotification _notify = notify;
        private readonly ILogger<OutgoingLetterService> _deliveryLog = NullLogger<OutgoingLetterService>.Instance;
    }
}
