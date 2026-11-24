using System.Collections.Concurrent;
using System.Reflection;
using Inventory.Client.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components;

namespace UnifiedSecretariatAvatarTests;

public sealed record ServiceCall(string Module, string Method, int? Id = null, int? Page = null, int? Size = null, bool? Registered = null, string? Search = null, int? Creator = null);

public sealed class Fixture
{
    public static readonly DateTime Created = new(2026, 11, 18, 10, 0, 0);
    public const string Outside = "سازمان بیرونی آزمایشی";
    public ConcurrentQueue<ServiceCall> Calls { get; } = new();
    public List<DabirkhaneListItemDto> Outgoing { get; } = new();
    public List<IncomingLetterListItemDto> Registered { get; } = new();
    public List<IncomingLetterListItemDto> Archived { get; } = new();
    public List<(string Method, object? Payload)> ArchiveCommands { get; } = new();
    public List<BayeganiNodeDto> IncomingFolders { get; } = new();
    public List<BayeganiNodeDto> IncomingArchiveTree() => IncomingFolders.Select(f => new BayeganiNodeDto
    {
        BayeganiId = f.BayeganiId, Title = f.Title, ParentId = f.ParentId, TypeBayegani = 3, SourceType = 3, IsFolder = true,
        Children = f.BayeganiId == 9100 ? Archived.Select(l => new BayeganiNodeDto
        { BayeganiId = l.SecretariatBayeganiId ?? 0, ParentId = 9100, LetterId = l.LetterId, Title = l.Title,
            LetterNumber = l.LetterNumber, SourceType = 3, TypeBayegani = 3, CreatorUserId = l.SenderUserId, Sender = l.Sender }).ToList() : new()
    }).ToList();
    public List<BayeganiNodeDto> ArchiveTree { get; private set; } = new();
    public int IncomingDetailReads { get; set; }
    public int OutgoingReadWrites { get; set; }
    public IOutgoingLetterService OutgoingService { get; }
    public IIncomingLetterService IncomingService { get; }
    public TestAuth Auth { get; } = new();
    public TestNavigation Navigation { get; } = new();

    public Fixture()
    {
        OutgoingService = DispatchProxy.Create<IOutgoingLetterService, OutgoingProxy>();
        ((OutgoingProxy)(object)OutgoingService).Fixture = this;
        IncomingService = DispatchProxy.Create<IIncomingLetterService, IncomingProxy>();
        ((IncomingProxy)(object)IncomingService).Fixture = this;
        Reset();
    }
    public void Reset()
    {
        ArchiveCommands.Clear(); IncomingFolders.Clear();
        IncomingFolders.Add(new() { BayeganiId = 9100, Title = "پوشه مشترک وارده", IsFolder = true, TypeBayegani = 3, SourceType = 3 });
        Calls.Clear(); IncomingDetailReads = 0; OutgoingReadWrites = 0; Outgoing.Clear(); Registered.Clear(); Archived.Clear(); Auth.CanRegister = true;
        void Out(int id, int? creator, string creatorName, bool registered = false, int? registrar = null, string? registrarName = null, bool archived = false)
            => Outgoing.Add(new()
            {
                LetterId = id, Title = "نامه صادره آزمایشی " + id, LetterNumber = id.ToString(), SadereNumber = "OUT-" + id,
                CreatorUserId = creator, CreatorName = creatorName, ReceiverOrganization = Outside, ReceiverName = "گیرنده بیرونی",
                DateSabt = Created, DateSadere = Created.AddDays(1), DabirkhaneSabt = registered,
                DabirkhaneUserId = registrar, DabirkhaneUserName = registrarName, DateDabirkhane = registered ? Created.AddDays(2) : null,
                SendMethod = registered ? "پیک" : null, DelivererName = registered ? "تحویل‌گیرنده بیرونی" : null,
                IsArchived = archived, ArchiveFolderTitle = archived ? "پوشه آزمایشی" : null,
                SignersTotal = 2, SignersSigned = 2, CompanyName = "سربرگ آزمایشی"
            });
        Out(1000, 11, "ایجادکننده هم‌نام");
        Out(1001, 12, "ایجادکننده هم‌نام");
        Out(1002, 99, "کاربر بدون عکس");
        Out(1003, null, "");
        Out(1004, -7, "شناسه نامعتبر");
        for (var i = 0; i < 16; i++) Out(1100 + i, 80 + i, "ایجادکننده آزمایشی " + (i + 1));
        Out(1200, 11, "ایجادکننده هم‌نام", true, 51, "ثبت‌کننده دبیرخانه هم‌نام", true);
        Out(1201, 12, "ایجادکننده هم‌نام", true, 52, "ثبت‌کننده دبیرخانه هم‌نام", true);
        Out(1202, 13, "ایجادکننده نامه قدیمی", true, null, "ایجادکننده نامه قدیمی", true);
        Out(1203, 14, "کاربر آزمایشی", true, 99, "ثبت‌کننده بدون عکس");
        Out(1204, 15, "ایجادکننده با نام و عنوان سازمانی طولانی برای آزمون چیدمان", true, 53, "ثبت‌کننده دبیرخانه", true);
        ArchiveTree = new()
        {
            new() { BayeganiId = 8000, Title = "پوشه آزمایشی", IsFolder = true, SourceType = 2, CreatorUserId = 777, Sender = "پوشه کاربر نیست",
                Children = new()
                {
                    new() { BayeganiId = 8001, ParentId = 8000, LetterId = 1200, Title = "نامه بایگانی اول", SourceType = 2, SadereNumber = "OUT-1200", CreatorUserId = 11, Sender = "ایجادکننده هم‌نام", ReceiverOrganization = Outside, Date = Created },
                    new() { BayeganiId = 8002, ParentId = 8000, LetterId = 1201, Title = "نامه بایگانی دوم", SourceType = 2, SadereNumber = "OUT-1201", CreatorUserId = 12, Sender = "ایجادکننده هم‌نام", ReceiverOrganization = Outside, Date = Created },
                    new() { BayeganiId = 8003, ParentId = 8000, LetterId = 1202, Title = "بایگانی قدیمی فاقد شناسه", SourceType = 2, Sender = "ایجادکننده هم‌نام", ReceiverOrganization = Outside, Date = Created },
                    new() { BayeganiId = 8004, ParentId = 8000, LetterId = 1204, Title = "نامه بایگانی با نام بلند", SourceType = 2, CreatorUserId = 15, Sender = "ایجادکننده با نام و عنوان سازمانی طولانی برای آزمون چیدمان", ReceiverOrganization = Outside, Date = Created }
                }
            }
        };
        static IncomingLetterListItemDto In(int id, int sender, string name, int? erja = null, bool archived = false) => new()
        {
            LetterId = id, ErjaId = erja, NumberSabt = id, LetterNumber = id.ToString(), Title = "نامه وارده آزمایشی " + id,
            Ferestande = Outside, SenderUserId = sender, Sender = name, Date = Created, DateErsal = Created.AddDays(1), IsBayegani = archived, SecretariatBayeganiId = archived ? id + 6000 : null
        };
        Registered.Add(In(2000, 21, "ثبت‌کننده وارده هم‌نام"));
        Registered.Add(In(2001, 22, "ثبت‌کننده وارده هم‌نام"));
        Registered.Add(In(2002, 99, "ثبت‌کننده بدون عکس"));
        Registered.Add(In(2003, 0, ""));
        for (var i = 0; i < 12; i++) Registered.Add(In(2100 + i, 110 + i, "ثبت‌کننده با نام طولانی برای آزمون عکس " + (i + 1)));
        Archived.Add(In(2200, 25, "ثبت‌کننده بایگانی مستقیم", archived: true));
        Archived.Add(In(2201, 61, "ثبت‌کننده بایگانی مشترک", archived: true));
        Archived.Add(In(2202, 0, Outside, archived: true)); // Old API: external text is not an internal name.
        Archived.Add(In(2203, -8, "ثبت‌کننده با شناسه نامعتبر", archived: true));
    }
    public OutgoingLetterDetailDto OutgoingDetail(int id)
    {
        var row = Outgoing.Single(r => r.LetterId == id);
        return new()
        {
            LetterId = id, LetterNumber = row.LetterNumber, SadereNumber = row.SadereNumber, Title = row.Title,
            Text = "<p>متن نامه آزمایشی، نه نامه واقعی.</p>", DateSabt = row.DateSabt,
            SenderUserId = row.CreatorUserId ?? 0, SenderName = row.CreatorName,
            ReceiverOrganization = row.ReceiverOrganization, ReceiverName = row.ReceiverName,
            Status = 3, DabirkhaneSabt = row.DabirkhaneSabt, DabirkhaneUserId = row.DabirkhaneUserId,
            DabirkhaneUserName = row.DabirkhaneUserName, DateDabirkhane = row.DateDabirkhane, SendMethod = row.SendMethod,
            Signers = Signers(id)
        };
    }
    public IncomingLetterDetailDto IncomingDetail(int id)
    {
        IncomingDetailReads++;
        var row = Registered.Concat(Archived).Single(r => r.LetterId == id);
        row.IsRead = true;
        return new()
        {
            LetterId = id, SourceId = id, LetterNumber = row.LetterNumber, Title = row.Title, NumberSabt = row.NumberSabt,
            Ferestande = row.Ferestande, Date = row.Date, DateErsal = row.DateErsal, CreatorUserId = 21, CreatorName = "ثبت‌کننده اولیه وارده",
            Description = "<p>متن وارده آزمایشی.</p>",
            Erjas = new() { new() { ErjaId = 701, SourceId = id, SenderUserId = 61, SenderName = "ارجاع‌دهنده", ReciverUserId = 31, ReciverName = "گیرنده", Date = Created, Type = "ارجاع", MatnErja = "پاراف آزمایشی" } }
        };
    }
    public static List<OutgoingSignerDto> Signers(int id) => new()
    {
        new() { Id = 1, SourceId = id, UserId = 41, DisplayName = "امضاکننده اول", IsSigned = true, Order = 1, DateSigned = Created },
        new() { Id = 2, SourceId = id, UserId = 42, DisplayName = "امضاکننده دوم", IsSigned = true, Order = 2, DateSigned = Created }
    };
    public static List<ErjaTreeNodeDto> Tree(int id, bool incoming) => new()
    {
        new() { ErjaId = 1, SenderUserId = incoming ? 21 : 11, Sender = "ایجادکننده اولیه", ReciverUserId = 61, Reciver = "ارجاع‌دهنده", Date = Created,
            Children = new() { new() { ErjaId = 2, SenderUserId = 61, Sender = "ارجاع‌دهنده", ReciverUserId = 31, Reciver = "گیرنده", MatnErja = "پاراف آزمایشی", Answer = "پاسخ آزمایشی", IsRead = true, Date = Created.AddDays(1) } } }
    };
    public static PagedResult<T> Page<T>(IEnumerable<T> rows, int page, int size)
    {
        var list = rows.ToList();
        return new() { Items = list.Skip((page - 1) * size).Take(size).ToList(), TotalCount = list.Count, Page = page, PageSize = size };
    }
    public void Register(IServiceCollection services, bool staticRenderer)
    {
        // Reuse only the existing dependency registration; our service doubles override its rows/calls.
        new OutgoingLetterAvatarTests.Fixture().Register(services, staticRenderer);
        services.AddSingleton(OutgoingService);
        services.AddSingleton(IncomingService);
        services.AddSingleton<IAuthState>(Auth);
        if (staticRenderer) services.AddSingleton<NavigationManager>(Navigation);
    }
}
public class OutgoingProxy : DispatchProxy
{
    public Fixture Fixture { get; set; } = default!;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var name = method!.Name;
        if (name == nameof(IOutgoingLetterService.GetDabirkhanePageAsync))
        {
            var f = (DabirkhaneSearchDto)args![0]!; var page = (int)args[1]!; var size = (int)args[2]!;
            Fixture.Calls.Enqueue(new("outgoing", name, Page: page, Size: size, Registered: f.RegisteredOnly, Search: f.Text, Creator: f.CreatorUserId));
            IEnumerable<DabirkhaneListItemDto> rows = Fixture.Outgoing;
            if (f.RegisteredOnly is bool registered) rows = rows.Where(r => r.DabirkhaneSabt == registered);
            if (f.ArchivedOnly is bool archived) rows = rows.Where(r => r.IsArchived == archived);
            if (f.CreatorUserId is > 0) rows = rows.Where(r => r.CreatorUserId == f.CreatorUserId);
            if (!string.IsNullOrWhiteSpace(f.Text)) rows = rows.Where(r => (r.Title + r.ReceiverOrganization).Contains(f.Text));
            return Task.FromResult(UnifiedSecretariatAvatarTests.Fixture.Page(rows, page, size));
        }
        var id = args?.FirstOrDefault() is int n ? n : 0;
        Fixture.Calls.Enqueue(new("outgoing", name, id > 0 ? id : null));
        switch (name)
        {
            case nameof(IOutgoingLetterService.GetDabirkhaneCreatorsAsync): return Task.FromResult(Fixture.Outgoing.Where(r => r.CreatorUserId > 0).DistinctBy(r => r.CreatorUserId).Select(r => new LetterReciverDto { UserId = r.CreatorUserId!.Value, FullName = r.CreatorName }).ToList());
            case nameof(IOutgoingLetterService.GetCompaniesAsync): return Task.FromResult(new List<LetterCompanyDto>());
            case nameof(IOutgoingLetterService.GetDabirkhaneStatsAsync): return Task.FromResult(new DabirkhaneStatsDto { Pending = Fixture.Outgoing.Count(r => !r.DabirkhaneSabt), Registered = Fixture.Outgoing.Count(r => r.DabirkhaneSabt), Archived = Fixture.Outgoing.Count(r => r.IsArchived) });
            case nameof(IOutgoingLetterService.GetDabirkhaneBayeganiTreeAsync): return Task.FromResult(Fixture.ArchiveTree);
            case nameof(IOutgoingLetterService.GetDetailAsync): return Task.FromResult(Fixture.OutgoingDetail(id));
            case nameof(IOutgoingLetterService.GetGardeshAsync): return Task.FromResult(UnifiedSecretariatAvatarTests.Fixture.Tree(id, false));
            case nameof(IOutgoingLetterService.GetSignersAsync): return Task.FromResult(UnifiedSecretariatAvatarTests.Fixture.Signers(id));
            case nameof(IOutgoingLetterService.GetAttachmentsAsync): return Task.FromResult(new List<LetterAttachmentDto>());
            case nameof(IOutgoingLetterService.GetUserSignatureDataUrlAsync): return Task.FromResult<string?>(null);
            case nameof(IOutgoingLetterService.MarkLetterReadAsync): Fixture.OutgoingReadWrites++; return Task.CompletedTask;
            default: throw new InvalidOperationException("Unexpected outgoing service call: " + name);
        }
    }
}
public class IncomingProxy : DispatchProxy
{
    public Fixture Fixture { get; set; } = default!;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var name = method!.Name;
        if (name == nameof(IIncomingLetterService.GetSecretariatAsync))
        {
            var archived = (bool)args![0]!; var search = (string?)args[1]; var page = (int)args[2]!; var size = (int)args[3]!;
            Fixture.Calls.Enqueue(new("incoming", name, Page: page, Size: size, Search: search));
            IEnumerable<IncomingLetterListItemDto> rows = archived ? Fixture.Archived : Fixture.Registered;
            if (!string.IsNullOrWhiteSpace(search)) rows = rows.Where(r => (r.Title + r.Ferestande).Contains(search));
            return Task.FromResult(UnifiedSecretariatAvatarTests.Fixture.Page(rows, page, size));
        }
        var id = args?.FirstOrDefault() is int n ? n : 0;
        Fixture.Calls.Enqueue(new("incoming", name, id));
        if (name == nameof(IIncomingLetterService.GetSecretariatTreeAsync)) return Task.FromResult(Fixture.IncomingArchiveTree());
        if (name.Contains("Secretariat"))
        {
            Fixture.ArchiveCommands.Add((name, args?.FirstOrDefault()));
            if (name == nameof(IIncomingLetterService.ArchiveSecretariatLettersAsync))
            {
                var command = (ArchiveSecretariatLettersDto)args![0]!;
                foreach (var letterId in command.LetterIds)
                {
                    var row = Fixture.Registered.Single(l => l.LetterId == letterId);
                    Fixture.Registered.Remove(row); row.IsBayegani = true; row.SecretariatBayeganiId = row.LetterId + 6000; Fixture.Archived.Add(row);
                }
                return Task.CompletedTask;
            }
            if (name == nameof(IIncomingLetterService.UnarchiveSecretariatLetterAsync))
            {
                var row = Fixture.Archived.Single(l => l.LetterId == id);
                Fixture.Archived.Remove(row); row.IsBayegani = false; row.SecretariatBayeganiId = null; Fixture.Registered.Add(row);
                return Task.CompletedTask;
            }
            if (name == nameof(IIncomingLetterService.MoveSecretariatLetterAsync))
            {
                var command = (MoveArchivedLetterDto)args![0]!;
                return Task.FromResult(new BayeganiNodeDto { BayeganiId = command.BayeganiId, ParentId = command.NewParentId, TypeBayegani = 3 });
            }
            if (name is nameof(IIncomingLetterService.AddSecretariatMainCategoryAsync) or nameof(IIncomingLetterService.AddSecretariatSubCategoryAsync))
            {
                var command = (SaveBayeganiFolderDto)args![0]!;
                var folder = new BayeganiNodeDto { BayeganiId = 9100 + Fixture.IncomingFolders.Count, Title = command.Title, ParentId = command.ParentId, TypeBayegani = 3, SourceType = 3, IsFolder = true };
                Fixture.IncomingFolders.Add(folder); return Task.FromResult(folder);
            }
            if (name == nameof(IIncomingLetterService.EditSecretariatFolderAsync))
            {
                var folder = Fixture.IncomingFolders.Single(f => f.BayeganiId == id);
                folder.Title = ((SaveBayeganiFolderDto)args![1]!).Title;
                return Task.FromResult(folder);
            }
            if (name == nameof(IIncomingLetterService.DeleteSecretariatNodeAsync))
            {
                Fixture.IncomingFolders.RemoveAll(f => f.BayeganiId == id); return Task.CompletedTask;
            }
        }
        return name switch
        {
            nameof(IIncomingLetterService.GetDetailAsync) => Task.FromResult(Fixture.IncomingDetail(id)),
            nameof(IIncomingLetterService.GetGardeshAsync) => Task.FromResult(UnifiedSecretariatAvatarTests.Fixture.Tree(id, true)),
            _ => throw new InvalidOperationException("Unexpected incoming service call: " + name)
        };
    }
}
public sealed class TestNavigation : NavigationManager
{
    public string? LastUri { get; private set; }
    public TestNavigation() => Initialize("https://fixture.invalid/", "https://fixture.invalid/");
    protected override void NavigateToCore(string uri, bool forceLoad) => LastUri = uri;
}
public sealed class TestAuth : IAuthState
{
    public bool CanRegister { get; set; } = true;
    public bool IsLoggedIn => true;
    public bool IsAdmin => false;
    public bool IsOperator => true;
    public bool IsReferrer => false;
    public bool CanOperate => true;
    public string? Token => null;
    public int UserId => 900;
    public string DisplayName => "کاربر واردشده، نه سازندهٔ فرضی";
    public string Role => "Operator";
    public int? ReferrerId => null;
    public bool Has(string permission) => permission is "IncomingLetters.Read" or "OutgoingLetters.Read" or "IncomingLetters.Erja" or "OutgoingLetters.Erja" || (CanRegister && permission is "IncomingLetters.Dabirkhane" or "OutgoingLetters.Dabirkhane");
    public bool HasDirect(string permission) => Has(permission);
    public bool HasModule(string module) => false;
    public bool HasModuleDirect(string module) => false;
    public bool CanSee(string module) => false;
    public event Action? Changed { add { } remove { } }
    public Task InitializeAsync() => Task.CompletedTask;
    public Task SignInAsync(LoginResponse login) => Task.CompletedTask;
    public Task SignOutAsync() => Task.CompletedTask;
}
