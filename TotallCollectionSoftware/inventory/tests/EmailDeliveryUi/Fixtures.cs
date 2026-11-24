using System.Reflection;
using Inventory.Client.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EmailDeliveryUiTests;

public sealed class Fixture
{
    public UnifiedSecretariatAvatarTests.Fixture Letters { get; } = new();
    public TestAuth Auth { get; } = new();
    public TestNavigation Navigation { get; } = new();
    public MemoryJs Js { get; } = new();
    public IEmailClientService Email { get; }
    public IOutgoingLetterService Outgoing { get; }
    public string Mode { get; set; } = "accept";
    public bool StatusFails { get; set; }
    public bool RejectReplay { get; set; }
    public int SendCalls, RegistrationCalls, StatusCalls, HistoryCalls, ReadWrites, SyncCalls;
    public bool LastPersonal;
    public int? LastLetter, LastAccount;
    public Guid? SuppressWith;
    public List<Guid> Keys { get; } = new();
    public Dictionary<Guid, EmailDeliveryAttemptDto> Attempts { get; } = new();
    public DabirkhaneRegisterDto? LastRegistration;
    public List<EmailAccountDto> Accounts { get; } = new()
    {
        new() { EmailId = 10, EmailAddress = "official@example.invalid", IsDabirkhane = true, IsActive = true, UserId = 1, IsMine = true },
        new() { EmailId = 11, EmailAddress = "personal@example.invalid", IsDabirkhane = false, IsActive = true, UserId = 1, IsMine = true }
    };
    public Fixture()
    {
        Email = DispatchProxy.Create<IEmailClientService, EmailProxy>(); ((EmailProxy)(object)Email).Fixture = this;
        Outgoing = DispatchProxy.Create<IOutgoingLetterService, OutgoingProxy>(); ((OutgoingProxy)(object)Outgoing).Fixture = this;
    }
    public EmailDeliveryResultDto Result(Guid id, int account, int? letter = null)
    {
        Keys.Add(id);
        if (RejectReplay) throw new ApiException("synthetic request rejected", 401, true);
        if (SuppressWith is { } old && Attempts.TryGetValue(old, out var prior))
        { var suppress = Clone(prior); suppress.DuplicateSuppressed = suppress.Replayed = true; return suppress; }
        if (Attempts.TryGetValue(id, out var existing)) { var replay = Clone(existing); replay.Replayed = true; return replay; }
        var status = Mode switch { "failed" => EmailDeliveryStatus.Failed, "unknown" => EmailDeliveryStatus.Unknown, "warning" => EmailDeliveryStatus.AcceptedWithWarnings, _ => EmailDeliveryStatus.Accepted };
        var result = new EmailDeliveryAttemptDto
        {
            RequestId = id, AttemptId = Attempts.Count + 1, SentId = Attempts.Count + 1, LetterSourceId = letter, EmailAccountId = account,
            AccountAddress = Accounts.First(a => a.EmailId == account).EmailAddress, Recipients = "receiver@example.invalid", Subject = "ارسال آزمایشی",
            Status = status, Code = status == EmailDeliveryStatus.Failed ? "SMTP_AUTH_ERROR" : "SMTP_ACCEPTED", Stage = "send",
            Message = status == EmailDeliveryStatus.Failed ? "احراز هویت حساب انجام نشد؛ پیام ارسال نشده است." : "نتیجهٔ ساختگی آزمون؛ تحویل قطعی به گیرنده تأیید نشده است.",
            StartedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, RequestedByUserId = 1,
            AcceptedAtUtc = status is EmailDeliveryStatus.Accepted or EmailDeliveryStatus.AcceptedWithWarnings ? DateTime.UtcNow : null
        };
        if (status == EmailDeliveryStatus.AcceptedWithWarnings) result.Events.Add(new() { Code = "CONTACTS_SAVE_WARNING", Stage = "contacts", Message = "بانک مخاطبین به‌روز نشد؛ ارسال را تکرار نکنید.", AtUtc = DateTime.UtcNow });
        Attempts[id] = result;
        return Mode == "http-loss" ? EmailDeliveryText.Uncertain(id) : Clone(result);
    }
    public static EmailDeliveryAttemptDto Clone(EmailDeliveryAttemptDto result) => System.Text.Json.JsonSerializer.Deserialize<EmailDeliveryAttemptDto>(System.Text.Json.JsonSerializer.Serialize(result))!;
    public void Register(IServiceCollection services, bool renderer)
    {
        Letters.Register(services, renderer);
        services.AddSingleton(DispatchProxy.Create<IEmailBankClientService, BankProxy>());
        services.AddSingleton(Email); services.AddSingleton(Outgoing); services.AddSingleton<IAuthState>(Auth);
        if (renderer) { services.AddSingleton<IJSRuntime>(Js); services.AddSingleton<NavigationManager>(Navigation); }
    }
    public void Reset()
    {
        Mode = "accept"; StatusFails = RejectReplay = false; SuppressWith = null; SendCalls = RegistrationCalls = StatusCalls = HistoryCalls = ReadWrites = SyncCalls = 0;
        Keys.Clear(); Attempts.Clear(); Js.Values.Clear(); Letters.Reset();
    }
}
public class EmailProxy : DispatchProxy
{
    public Fixture Fixture { get; set; } = default!;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var f = Fixture; var name = method!.Name;
        switch (name)
        {
            case nameof(IEmailClientService.GetAccountsAsync): return Task.FromResult(f.Accounts);
            case nameof(IEmailClientService.GetDabirkhaneAccountsAsync): return Task.FromResult(f.Accounts.Where(a => a.IsDabirkhane).ToList());
            case nameof(IEmailClientService.GetFoldersAsync): return Task.FromResult(new List<EmailFolderDto>());
            case nameof(IEmailClientService.GetInboxAsync): return Task.FromResult(new EmailListPageDto { Items = new() { new() { Id = 700, Box = "Inbox", EmailId = 10, Subject = "ایمیل دریافتی آزمایشی", FromOrTo = "outside@example.invalid", Date = DateTime.Now, AccountAddress = "official@example.invalid" } }, TotalCount = 1 });
            case nameof(IEmailClientService.GetSentAsync):
            case nameof(IEmailClientService.GetArchiveAsync):
                return Task.FromResult(new EmailListPageDto { Items = new() {
                    new() { Id = 1, Box = "Sent", EmailId = 10, Subject = "پذیرفته‌شده آزمایشی", FromOrTo = "receiver@example.invalid", Date = DateTime.Now, DeliveryStatus = EmailDeliveryStatus.Accepted },
                    new() { Id = 2, Box = "Sent", EmailId = 10, Subject = "سابقه قدیمی آزمایشی", FromOrTo = "receiver@example.invalid", Date = DateTime.Now },
                    new() { Id = 3, Box = "Sent", EmailId = 10, Subject = "ناموفق آزمایشی", FromOrTo = "receiver@example.invalid", Date = DateTime.Now, DeliveryStatus = EmailDeliveryStatus.Failed }
                }, TotalCount = 3 });
            case nameof(IEmailClientService.SendAsync):
                f.SendCalls++;
                return Task.FromResult(f.Result((Guid)args![6]!, (int)args[0]!));
            case nameof(IEmailClientService.GetDeliveryStatusAsync):
                f.StatusCalls++; f.LastPersonal = (bool)args![1]!;
                if (f.StatusFails) return Task.FromException<EmailDeliveryAttemptDto?>(new ApiException("synthetic history unavailable", 503));
                return Task.FromResult(f.Attempts.TryGetValue((Guid)args[0]!, out var found) ? Fixture.Clone(found) : null);
            case nameof(IEmailClientService.GetDeliveryHistoryAsync):
                f.HistoryCalls++; f.LastAccount = (int?)args![0]; f.LastLetter = (int?)args[1]; f.LastPersonal = (bool)args[6]!;
                var status = (EmailDeliveryStatus?)args[3]; var page = (int)args[4]!; var size = (int)args[5]!;
                var rows = f.Attempts.Values.Where(a => (!f.LastAccount.HasValue || a.EmailAccountId == f.LastAccount) &&
                    (!f.LastLetter.HasValue || a.LetterSourceId == f.LastLetter) && (!f.LastPersonal || a.EmailAccountId == 11) && (!status.HasValue || a.Status == status)).ToList();
                if (f.StatusFails) return Task.FromException<PagedResult<EmailDeliveryAttemptDto>>(new ApiException("synthetic history unavailable"));
                return Task.FromResult(new PagedResult<EmailDeliveryAttemptDto> { Items = rows.Skip((page - 1) * size).Take(size).Select(Fixture.Clone).ToList(), TotalCount = rows.Count, Page = page, PageSize = size });
            case nameof(IEmailClientService.GetMessageAsync): return Task.FromResult(new EmailMessageDetailDto { Id = (int)args![1]!, Box = (string)args[0]!, EmailId = 10, Subject = "synthetic detail", Body = "<p>synthetic</p>" });
            case nameof(IEmailClientService.MarkReadAsync): f.ReadWrites++; return Task.CompletedTask;
            case nameof(IEmailClientService.SyncInboxAsync):
            case nameof(IEmailClientService.SyncSentAsync):
            case nameof(IEmailClientService.SyncJunkAsync): f.SyncCalls++; return Task.FromResult(new EmailSyncResultDto());
            default: throw new InvalidOperationException("Unexpected synthetic email call " + name);
        }
    }
}
public class OutgoingProxy : DispatchProxy
{
    public Fixture Fixture { get; set; } = default!;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name != nameof(IOutgoingLetterService.DabirkhaneRegisterAsync)) return method.Invoke(Fixture.Letters.OutgoingService, args);
        Fixture.RegistrationCalls++; var dto = (DabirkhaneRegisterDto)args![1]!; Fixture.LastRegistration = dto;
        var result = new DabirkhaneRegisterResultDto { RegistrationSaved = Fixture.Mode == "http-loss" ? null : true,
            Email = dto.SendByEmail ? Fixture.Result(dto.EmailRequestId, dto.EmailAccountId!.Value, (int)args[0]!) : null };
        return Task.FromResult(result);
    }
}
public sealed class TestAuth : IAuthState
{
    public bool Personal { get; set; } = true;
    public bool IsLoggedIn => true; public bool IsAdmin => false; public bool IsOperator => true; public bool IsReferrer => false; public bool CanOperate => true;
    public string? Token => "synthetic-token"; public int UserId => 1; public string DisplayName => "کاربر آزمایشی"; public string Role => "Operator"; public int? ReferrerId => null;
    public bool Has(string permission) => permission != "Email.Personal" || Personal;
    public bool HasDirect(string permission) => Has(permission); public bool HasModule(string module) => true; public bool HasModuleDirect(string module) => true; public bool CanSee(string module) => true;
    public event Action? Changed { add { } remove { } }
    public Task InitializeAsync() => Task.CompletedTask; public Task SignInAsync(LoginResponse login) => Task.CompletedTask; public Task SignOutAsync() => Task.CompletedTask;
}
public sealed class TestNavigation : NavigationManager
{
    public TestNavigation() => Initialize("https://fixture.invalid/", "https://fixture.invalid/office/email");
    public void SetUri(string path) => Uri = "https://fixture.invalid/" + path.TrimStart('/');
    protected override void NavigateToCore(string uri, bool forceLoad) => SetUri(uri);
}
public sealed class MemoryJs : IJSRuntime
{
    public Dictionary<string, string> Values { get; } = new();
    public bool Fail;
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
    public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (Fail) throw new JSException("synthetic disabled storage");
        if (identifier == "sessionStorage.setItem") Values[(string)args![0]!] = (string)args[1]!;
        if (identifier == "sessionStorage.removeItem") Values.Remove((string)args![0]!);
        if (identifier == "sessionStorage.getItem") return ValueTask.FromResult(Values.TryGetValue((string)args![0]!, out var value) ? (T)(object)value : default!);
        if (identifier == "localStorage.getItem") return ValueTask.FromResult((T)(object)"42");
        return ValueTask.FromResult(default(T)!);
    }
}

public class BankProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        nameof(IEmailBankClientService.SuggestAsync) => Task.FromResult(new EmailBankSuggestDto()),
        nameof(IEmailBankClientService.FindAsync) => Task.FromResult<EmailBankEntryDto?>(null),
        nameof(IEmailBankClientService.RememberAsync) => Task.FromResult(new EmailBankSaveResultDto()),
        _ => throw new InvalidOperationException("unexpected synthetic address-book call")
    };
}
