using System.Data;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// Shared incoming secretariat archive. Controllers must enforce the secretariat permission.
/// Personal referral archives (type 0/1) are never read or modified by this interface.
/// </summary>
public interface IIncomingSecretariatArchiveService
{
    Task<PagedResult<IncomingLetterListItemDto>> GetIncomingSecretariatAsync(bool archived, string? search, int page = 1, int pageSize = 15);
    Task<List<BayeganiNodeDto>> GetIncomingSecretariatTreeAsync(Paging.Request? pagination = null);
    Task<BayeganiNodeDto> AddIncomingSecretariatFolderAsync(int userId, SaveBayeganiFolderDto dto, bool rootOnly);
    Task<BayeganiNodeDto> EditIncomingSecretariatFolderAsync(int id, SaveBayeganiFolderDto dto);
    Task<BayeganiNodeDto> MoveIncomingSecretariatFolderAsync(int id, int parentId);
    Task<BayeganiNodeDto> MoveIncomingSecretariatLetterAsync(MoveArchivedLetterDto dto);
    Task DeleteIncomingSecretariatNodeAsync(int id);
    Task ArchiveIncomingSecretariatLettersAsync(int userId, ArchiveSecretariatLettersDto dto);
    Task UnarchiveIncomingSecretariatLetterAsync(int letterId);
}

public partial class ArchiveService : IIncomingSecretariatArchiveService
{
    // These values are storage scopes, not arbitrary values accepted from a client.
    // UserId on shared nodes remains the creating/archiving actor for historical attribution.
    private const int TypeIncomingSecretariat = 3;

    private IQueryable<LetterBayegani> SharedArchiveRows(int type) =>
        _db.LetterBayeganis.Where(b => !b.IsDelete && b.TypeBayegani == type &&
            (b.IsFolder || (type == TypeOutgoing
                ? _db.OutgoingLetters.Any(l => l.Id == b.LetterId && !l.IsDelete && !l.Source.IsDelete
                    && l.SadereNumber != null && l.SadereNumber != "")
                : _db.IncomingLetters.Any(l => l.Id == b.LetterId && !l.IsDelete && !l.Source.IsDelete && l.IsBayegani))));

    private async Task<LetterBayegani> GetSharedNodeAsync(int type, int id) =>
        await SharedArchiveRows(type).SingleOrDefaultAsync(b => b.BayeganiId == id)
            ?? throw new InvalidOperationException("موردی در این بایگانی دبیرخانه یافت نشد.");

    private async Task RequireSharedFolderAsync(int type, int id)
    {
        if (id == 0) return; // the root is a valid destination for both letters and folders
        if (id < 0 || !await _db.LetterBayeganis.AnyAsync(b => b.BayeganiId == id
                && b.TypeBayegani == type && b.IsFolder && !b.IsDelete))
            throw new InvalidOperationException("پوشه مقصد در این بایگانی دبیرخانه یافت نشد.");
    }

    private static BayeganiNodeDto MapShared(LetterBayegani node)
    {
        var dto = MapOutgoing(node);
        dto.SourceType = node.TypeBayegani == TypeIncomingSecretariat ? 3 : 2;
        return dto;
    }

    private Task NotifySharedArchiveAsync(int type) =>
        _notify.BroadcastChangedAsync(type == TypeOutgoing ? "outgoing-letters" : "incoming-letters");

    private async Task<BayeganiNodeDto> AddSharedFolderAsync(int type, int userId, SaveBayeganiFolderDto dto, bool rootOnly)
    {
        var title = ValidateTitle(dto.Title);
        if (rootOnly ? dto.ParentId != 0 : dto.ParentId <= 0)
            throw new InvalidOperationException("شناسه پوشه والد نامعتبر است.");
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await RequireSharedFolderAsync(type, dto.ParentId);
        var node = new LetterBayegani
        {
            Title = title, ParentId = dto.ParentId, UserId = userId,
            TypeBayegani = type, IsFolder = true
        };
        _db.LetterBayeganis.Add(node);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifySharedArchiveAsync(type);
        return MapShared(node);
    }

    private async Task<BayeganiNodeDto> EditSharedFolderAsync(int type, int id, SaveBayeganiFolderDto dto)
    {
        var title = ValidateTitle(dto.Title);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var node = await GetSharedNodeAsync(type, id);
        if (!node.IsFolder) throw new InvalidOperationException("رکورد انتخاب‌شده پوشه نیست.");
        node.Title = title;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifySharedArchiveAsync(type);
        return MapShared(node);
    }

    private async Task<BayeganiNodeDto> MoveSharedNodeAsync(int type, int id, int parentId, bool isFolder)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var node = await GetSharedNodeAsync(type, id);
        if (node.IsFolder != isFolder) throw new InvalidOperationException("نوع رکورد بایگانی نامعتبر است.");
        await RequireSharedFolderAsync(type, parentId);
        if (node.IsFolder)
        {
            var current = parentId;
            var visited = new HashSet<int> { id };
            while (current != 0)
            {
                // Fail closed for corrupt legacy cycles and very deep trees as well.
                if (!visited.Add(current) || visited.Count > 1024)
                    throw new InvalidOperationException("پوشه نمی‌تواند داخل خودش یا زیرپوشه‌های خودش قرار بگیرد.");
                var parent = await _db.LetterBayeganis.AsNoTracking().SingleOrDefaultAsync(b =>
                    b.BayeganiId == current && b.TypeBayegani == type && b.IsFolder && !b.IsDelete);
                if (parent == null) throw new InvalidOperationException("ساختار پوشه مقصد نامعتبر است.");
                current = parent.ParentId;
            }
        }
        else
        {
            // Older outgoing versions permitted one archive link per user. Move every
            // surviving link of this one shared letter so an old copy cannot strand it.
            var copies = await _db.LetterBayeganis.Where(b => b.TypeBayegani == type && !b.IsDelete
                && !b.IsFolder && b.LetterId == node.LetterId).ToListAsync();
            foreach (var copy in copies) copy.ParentId = parentId;
        }
        node.ParentId = parentId;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifySharedArchiveAsync(type);
        return MapShared(node);
    }

    private async Task ArchiveSharedLettersAsync(int type, int userId, List<int>? ids, int parentId, string? title)
    {
        if (ids == null || ids.Count == 0 || ids.Any(id => id <= 0))
            throw new InvalidOperationException("حداقل یک نامه معتبر برای بایگانی انتخاب کنید.");
        var letterIds = ids.Distinct().OrderBy(id => id).ToList();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await RequireSharedFolderAsync(type, parentId);
        if (await _db.LetterBayeganis.AnyAsync(b => b.TypeBayegani == type && !b.IsDelete
                && !b.IsFolder && b.LetterId.HasValue && letterIds.Contains(b.LetterId.Value)))
            throw new InvalidOperationException("برخی از نامه‌ها قبلاً در بایگانی مشترک دبیرخانه هستند.");

        var outgoing = type == TypeOutgoing
            ? await _db.OutgoingLetters.Where(l => letterIds.Contains(l.Id) && !l.IsDelete && !l.Source.IsDelete).ToListAsync()
            : new List<OutgoingLetter>();
        var incoming = type == TypeIncomingSecretariat
            ? await _db.IncomingLetters.Where(l => letterIds.Contains(l.Id) && !l.IsDelete && !l.Source.IsDelete).ToListAsync()
            : new List<IncomingLetter>();
        if (outgoing.Count + incoming.Count != letterIds.Count)
            throw new InvalidOperationException("برخی از نامه‌ها در این دبیرخانه یافت نشدند.");
        if (outgoing.Any(l => string.IsNullOrWhiteSpace(l.SadereNumber)))
            throw new InvalidOperationException("فقط نامه‌های امضاشده قابل بایگانی در دبیرخانه هستند.");
        if (outgoing.Any(l => l.IsArchived) || incoming.Any(l => l.IsBayegani))
            throw new InvalidOperationException("برخی از نامه‌ها قبلاً در بایگانی مشترک دبیرخانه هستند.");

        var letters = outgoing.Select(l => (l.Id, l.Title)).Concat(incoming.Select(l => (l.Id, l.Title)));
        foreach (var letter in letters)
        {
            var displayTitle = string.IsNullOrWhiteSpace(title) ? letter.Title : title.Trim();
            _db.LetterBayeganis.Add(new LetterBayegani
            {
                LetterId = letter.Id, Title = displayTitle.Length > 200 ? displayTitle[..200] : displayTitle,
                UserId = userId, ParentId = parentId, TypeBayegani = type, IsFolder = false
            });
        }
        foreach (var letter in outgoing)
        {
            letter.IsArchived = true;
            letter.ArchivedAt = DateTime.Now;
            letter.ArchivedByUserId = userId;
        }
        foreach (var letter in incoming) letter.IsBayegani = true;
        // Deliberately no updates to Erja or personal type 0/1 nodes.
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifySharedArchiveAsync(type);
    }

    private async Task ClearSharedLetterAsync(int type, int letterId)
    {
        if (type == TypeOutgoing)
        {
            var letter = await _db.OutgoingLetters.SingleOrDefaultAsync(l => l.Id == letterId && !l.IsDelete && !l.Source.IsDelete)
                ?? throw new InvalidOperationException("نامه صادره یافت نشد.");
            letter.IsArchived = false;
            letter.ArchivedAt = null;
            letter.ArchivedByUserId = null;
        }
        else
        {
            var letter = await _db.IncomingLetters.SingleOrDefaultAsync(l => l.Id == letterId && !l.IsDelete && !l.Source.IsDelete)
                ?? throw new InvalidOperationException("نامه وارده یافت نشد.");
            letter.IsBayegani = false;
        }
        var nodes = await _db.LetterBayeganis.Where(b => b.TypeBayegani == type && !b.IsDelete
            && !b.IsFolder && b.LetterId == letterId).ToListAsync();
        foreach (var node in nodes) node.IsDelete = true;
    }

    private async Task UnarchiveSharedLetterAsync(int type, int letterId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        // Explicit unarchive is idempotent; repeating a stale UI action cannot re-archive.
        await ClearSharedLetterAsync(type, letterId);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifySharedArchiveAsync(type);
    }

    private async Task DeleteSharedNodeAsync(int type, int id)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var node = await GetSharedNodeAsync(type, id);
        if (node.IsFolder)
        {
            if (await _db.LetterBayeganis.AnyAsync(b => b.ParentId == id && b.TypeBayegani == type && !b.IsDelete))
                throw new InvalidOperationException("این پوشه خالی نیست؛ ابتدا محتوا و زیرپوشه‌ها را منتقل کنید.");
            node.IsDelete = true;
        }
        else if (node.LetterId is { } letterId)
            await ClearSharedLetterAsync(type, letterId);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifySharedArchiveAsync(type);
    }

    /// <summary>
    /// Compatibility for old registrar/secretariat archives: only the letter-level flag
    /// is shared. Referral flags and personal folder titles are never imported.
    /// Existing shared rows (including their actor and chosen folder) are left untouched.
    /// </summary>
    private async Task EnsureIncomingSecretariatNodesAsync()
    {
        IQueryable<IncomingLetter> Missing() => _db.IncomingLetters.Where(l => l.IsBayegani
            && !l.IsDelete && !l.Source.IsDelete && !_db.LetterBayeganis.Any(b =>
                b.TypeBayegani == TypeIncomingSecretariat && !b.IsFolder && !b.IsDelete && b.LetterId == l.Id));
        if (!await Missing().AnyAsync()) return;
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var letters = await Missing().AsNoTracking().OrderBy(l => l.Id)
            .Select(l => new { l.Id, l.Title, l.CreateUserId }).ToListAsync();
        foreach (var letter in letters)
            _db.LetterBayeganis.Add(new LetterBayegani
            {
                LetterId = letter.Id, Title = letter.Title.Length > 200 ? letter.Title[..200] : letter.Title,
                UserId = letter.CreateUserId, TypeBayegani = TypeIncomingSecretariat, ParentId = 0
            });
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<PagedResult<IncomingLetterListItemDto>> GetIncomingSecretariatAsync(bool archived, string? search, int page = 1, int pageSize = 15)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;
        if (archived) await EnsureIncomingSecretariatNodesAsync();
        var q = _db.IncomingLetters.AsNoTracking()
            .Where(l => !l.IsDelete && !l.Source.IsDelete && l.IsBayegani == archived);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(l => l.Title.Contains(term) || (l.LetterNumber ?? "").Contains(term)
                || (l.NumberLetterVarede ?? "").Contains(term) || l.Ferestande.Contains(term));
        }
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(l => l.DateErsal).ThenByDescending(l => l.Id)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
            .Select(l => new IncomingLetterListItemDto
            {
                LetterId = l.Id, LetterNumber = l.LetterNumber ?? "", NumberSabt = l.NumberSabt,
                NumberLetterVarede = l.NumberLetterVarede ?? "", Title = l.Title, Ferestande = l.Ferestande,
                SenderUserId = l.CreateUserId,
                Sender = _db.Users.Where(u => u.Id == l.CreateUserId)
                    .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() == ""
                        ? u.Username : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim()).FirstOrDefault() ?? "",
                Date = l.Date, DateErsal = l.DateErsal, TypeErsal = l.TypeErsal ?? "",
                DeliveryName = l.DeliveryName ?? "", Mahramanegi = l.Mahramanegi, Foriat = l.Foriat,
                IsBayegani = l.IsBayegani, IsRead = true,
                ReciverCount = l.Source.Erjas.Count(e => !e.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "IncomingLetters" && a.RefId == l.Id)
            }).ToListAsync();
        if (archived && items.Count > 0)
        {
            var ids = items.Select(l => l.LetterId).ToList();
            var links = await SharedArchiveRows(TypeIncomingSecretariat).AsNoTracking()
                .Where(b => !b.IsFolder && b.LetterId.HasValue && ids.Contains(b.LetterId.Value))
                .OrderBy(b => b.BayeganiId).ToListAsync();
            var parents = links.Select(b => b.ParentId).Distinct().ToList();
            var titles = await SharedArchiveRows(TypeIncomingSecretariat).AsNoTracking()
                .Where(b => b.IsFolder && parents.Contains(b.BayeganiId)).ToDictionaryAsync(b => b.BayeganiId, b => b.Title);
            var byLetter = links.GroupBy(b => b.LetterId!.Value).ToDictionary(g => g.Key, g => g.First());
            foreach (var row in items)
                if (byLetter.TryGetValue(row.LetterId, out var link))
                {
                    row.SecretariatBayeganiId = link.BayeganiId;
                    row.SecretariatFolderTitle = titles.GetValueOrDefault(link.ParentId);
                }
        }
        return new() { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    public async Task<List<BayeganiNodeDto>> GetIncomingSecretariatTreeAsync(Paging.Request? pagination = null)
    {
        await EnsureIncomingSecretariatNodesAsync();
        var scope = SharedArchiveRows(TypeIncomingSecretariat).AsNoTracking();
        IOrderedQueryable<LetterBayegani> Order(IQueryable<LetterBayegani> q) => q
            .OrderByDescending(b => b.IsFolder).ThenBy(b => b.Title).ThenBy(b => b.BayeganiId);
        var rows = await Order(scope).ReadTreePageAsync(
            Order(scope.Where(b => b.ParentId == 0 || !scope.Any(p => p.BayeganiId == b.ParentId && p.IsFolder))),
            ids => Order(scope.Where(b => ids.Contains(b.ParentId))), b => b.BayeganiId, pagination);
        var ids = rows.Where(b => b.LetterId.HasValue).Select(b => b.LetterId!.Value).Distinct().ToList();
        var info = await _db.IncomingLetters.AsNoTracking().Where(l => ids.Contains(l.Id))
            .Select(l => new { l.Id, l.Title, l.LetterNumber, l.DateErsal, l.CreateUserId,
                Sender = _db.Users.Where(u => u.Id == l.CreateUserId)
                    .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() == ""
                        ? u.Username : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim()).FirstOrDefault() ?? "" })
            .ToDictionaryAsync(l => l.Id);
        var nodes = rows.ToDictionary(b => b.BayeganiId, MapShared);
        var roots = new List<BayeganiNodeDto>();
        foreach (var node in nodes.Values)
        {
            if (node.LetterId is { } id && info.TryGetValue(id, out var letter))
            {
                node.Title = letter.Title; node.LetterNumber = letter.LetterNumber;
                node.Date = letter.DateErsal; node.CreatorUserId = letter.CreateUserId; node.Sender = letter.Sender;
            }
            if (node.ParentId != 0 && nodes.TryGetValue(node.ParentId, out var parent) && parent.IsFolder)
                parent.Children.Add(node);
            else roots.Add(node);
        }
        SortTree(roots);
        return roots;
    }

    public Task<BayeganiNodeDto> AddIncomingSecretariatFolderAsync(int userId, SaveBayeganiFolderDto dto, bool rootOnly) =>
        AddSharedFolderAsync(TypeIncomingSecretariat, userId, dto, rootOnly);
    public Task<BayeganiNodeDto> EditIncomingSecretariatFolderAsync(int id, SaveBayeganiFolderDto dto) =>
        EditSharedFolderAsync(TypeIncomingSecretariat, id, dto);
    public Task<BayeganiNodeDto> MoveIncomingSecretariatFolderAsync(int id, int parentId) =>
        MoveSharedNodeAsync(TypeIncomingSecretariat, id, parentId, isFolder: true);
    public Task<BayeganiNodeDto> MoveIncomingSecretariatLetterAsync(MoveArchivedLetterDto dto) =>
        MoveSharedNodeAsync(TypeIncomingSecretariat, dto.BayeganiId, dto.NewParentId, isFolder: false);
    public Task DeleteIncomingSecretariatNodeAsync(int id) => DeleteSharedNodeAsync(TypeIncomingSecretariat, id);
    public Task ArchiveIncomingSecretariatLettersAsync(int userId, ArchiveSecretariatLettersDto dto) =>
        ArchiveSharedLettersAsync(TypeIncomingSecretariat, userId, dto.LetterIds, dto.FolderId, dto.Title);
    public Task UnarchiveIncomingSecretariatLetterAsync(int letterId) =>
        UnarchiveSharedLetterAsync(TypeIncomingSecretariat, letterId);
}
