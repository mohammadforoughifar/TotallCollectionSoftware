using System.Globalization;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Office;

public interface IIncomingLetterService
{
    Task<PagedResult<IncomingLetterListItemDto>> GetInboxAsync(int userId, string? search, bool? unreadOnly, int page = 1, int pageSize = 15);
    Task<PagedResult<IncomingLetterListItemDto>> GetArchiveAsync(int userId, string? search, int page = 1, int pageSize = 15);
    Task<PagedResult<IncomingLetterListItemDto>> GetSentAsync(int userId, string? search, int page = 1, int pageSize = 15);
    Task<IncomingLetterDetailDto?> GetDetailAsync(int letterId, int userId, bool isAdmin);
    Task<IncomingLetterCartableStatsDto> GetStatsAsync(int userId);
    Task<int> AddAsync(AddIncomingLetterDto dto, int userId);
    Task EditAsync(int letterId, EditIncomingLetterDto dto, int userId, bool isAdmin);
    Task DeleteAsync(int letterId, int userId, bool isAdmin);
    Task<bool> ToggleLetterNeshanAsync(int letterId, int userId);
    Task<PagedResult<IncomingLetterPickDto>> PickListAsync(int userId, string? search, int page = 1, int pageSize = 15);

    // رزرو شماره
    Task<List<LetterNumberReservationDto>> GetReservationsAsync(int userId, int typeForm = 3);
    Task<List<LetterNumberReservationDto>> ReserveNumberAsync(int userId, int typeForm = 3, int count = 1);
}

public class IncomingLetterService : IIncomingLetterService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly FileStore _store;

    public IncomingLetterService(AppDbContext db, INotifyService notify, FileStore store)
    {
        _db = db;
        _notify = notify;
        _store = store;
    }

    public async Task<PagedResult<IncomingLetterListItemDto>> GetInboxAsync(int userId, string? search, bool? unreadOnly, int page = 1, int pageSize = 15)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;

        var q = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.IncomingLetter != null && !e.Source.IncomingLetter.IsDelete
                        && e.IsBayegani != true);

        if (unreadOnly == true) q = q.Where(e => !e.IsRead);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => e.Source.IncomingLetter!.Title.Contains(s)
                             || (e.Source.IncomingLetter!.LetterNumber ?? "").Contains(s)
                             || (e.Source.IncomingLetter!.NumberLetterVarede ?? "").Contains(s)
                             || e.Source.IncomingLetter!.Ferestande.Contains(s)
                             || (e.UserSender!.FirstName + " " + e.UserSender.LastName).Contains(s));
        }

        var totalCount = await q.CountAsync();

        var items = await q
            .OrderByDescending(e => e.ErjaId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new IncomingLetterListItemDto
            {
                LetterId = e.SourceId,
                ErjaId = e.ErjaId,
                LetterNumber = e.Source.IncomingLetter!.LetterNumber ?? "",
                NumberSabt = e.Source.IncomingLetter!.NumberSabt,
                NumberLetterVarede = e.Source.IncomingLetter!.NumberLetterVarede ?? "",
                Title = e.Source.IncomingLetter!.Title,
                Ferestande = e.Source.IncomingLetter!.Ferestande,
                Sender = string.IsNullOrEmpty(e.UserSender!.FirstName + e.UserSender.LastName)
                    ? e.UserSender.Username
                    : (e.UserSender.FirstName + " " + e.UserSender.LastName).Trim(),
                SenderUserId = e.SenderUserId,
                Date = e.Source.IncomingLetter!.Date,
                DateErsal = e.Source.IncomingLetter!.DateErsal,
                TypeErsal = e.Source.IncomingLetter!.TypeErsal ?? "",
                DeliveryName = e.Source.IncomingLetter!.DeliveryName ?? "",
                Mahramanegi = e.Source.IncomingLetter!.Mahramanegi,
                Foriat = e.Source.IncomingLetter!.Foriat,
                ErjaType = e.Type,
                MatnErja = e.MatnErja,
                MohlatPasokh = e.MohlatPasokh,
                IsNeshan = e.IsNeshan,
                IsRead = e.IsRead,
                IsBayegani = e.IsBayegani == true,
                HasAnswer = !string.IsNullOrEmpty(e.Answer),
                ReciverCount = e.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "IncomingLetters" && a.RefId == e.SourceId)
            })
            .ToListAsync();

        return new PagedResult<IncomingLetterListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PagedResult<IncomingLetterListItemDto>> GetArchiveAsync(int userId, string? search, int page = 1, int pageSize = 15)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;

        var q = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.IncomingLetter != null && !e.Source.IncomingLetter.IsDelete
                        && e.IsBayegani == true);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => e.Source.IncomingLetter!.Title.Contains(s)
                             || (e.Source.IncomingLetter!.LetterNumber ?? "").Contains(s)
                             || e.Source.IncomingLetter!.Ferestande.Contains(s));
        }

        var totalCount = await q.CountAsync();

        var items = await q
            .OrderByDescending(e => e.ErjaId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new IncomingLetterListItemDto
            {
                LetterId = e.SourceId,
                ErjaId = e.ErjaId,
                LetterNumber = e.Source.IncomingLetter!.LetterNumber ?? "",
                NumberSabt = e.Source.IncomingLetter!.NumberSabt,
                NumberLetterVarede = e.Source.IncomingLetter!.NumberLetterVarede ?? "",
                Title = e.Source.IncomingLetter!.Title,
                Ferestande = e.Source.IncomingLetter!.Ferestande,
                Sender = string.IsNullOrEmpty(e.UserSender!.FirstName + e.UserSender.LastName)
                    ? e.UserSender.Username
                    : (e.UserSender.FirstName + " " + e.UserSender.LastName).Trim(),
                SenderUserId = e.SenderUserId,
                Date = e.Source.IncomingLetter!.Date,
                DateErsal = e.Source.IncomingLetter!.DateErsal,
                TypeErsal = e.Source.IncomingLetter!.TypeErsal ?? "",
                DeliveryName = e.Source.IncomingLetter!.DeliveryName ?? "",
                Mahramanegi = e.Source.IncomingLetter!.Mahramanegi,
                Foriat = e.Source.IncomingLetter!.Foriat,
                ErjaType = e.Type,
                MatnErja = e.MatnErja,
                MohlatPasokh = e.MohlatPasokh,
                IsNeshan = e.IsNeshan,
                IsRead = e.IsRead,
                IsBayegani = true,
                HasAnswer = !string.IsNullOrEmpty(e.Answer),
                ReciverCount = e.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "IncomingLetters" && a.RefId == e.SourceId)
            })
            .ToListAsync();

        return new PagedResult<IncomingLetterListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PagedResult<IncomingLetterListItemDto>> GetSentAsync(int userId, string? search, int page = 1, int pageSize = 15)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;

        var q = _db.IncomingLetters.AsNoTracking()
            .Where(l => l.CreateUserId == userId && !l.IsDelete && !l.Source.IsDelete);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(l => l.Title.Contains(s) || (l.LetterNumber ?? "").Contains(s) || l.Ferestande.Contains(s));
        }

        var totalCount = await q.CountAsync();

        var items = await q
            .OrderByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new IncomingLetterListItemDto
            {
                LetterId = l.Id,
                ErjaId = null,
                LetterNumber = l.LetterNumber ?? "",
                NumberSabt = l.NumberSabt,
                NumberLetterVarede = l.NumberLetterVarede ?? "",
                Title = l.Title,
                Ferestande = l.Ferestande,
                Sender = "شما",
                SenderUserId = userId,
                Date = l.Date,
                DateErsal = l.DateErsal,
                TypeErsal = l.TypeErsal ?? "",
                DeliveryName = l.DeliveryName ?? "",
                Mahramanegi = l.Mahramanegi,
                Foriat = l.Foriat,
                IsNeshan = l.IsNeshan,
                IsRead = true,
                IsBayegani = l.IsBayegani,
                ReciverCount = l.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "IncomingLetters" && a.RefId == l.Id)
            })
            .ToListAsync();

        return new PagedResult<IncomingLetterListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<IncomingLetterCartableStatsDto> GetStatsAsync(int userId)
    {
        var inboxQuery = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.IncomingLetter != null && !e.Source.IncomingLetter.IsDelete
                        && e.IsBayegani != true);

        var totalInbox = await inboxQuery.CountAsync();
        var unreadCount = await inboxQuery.CountAsync(e => !e.IsRead);

        var totalSent = await _db.IncomingLetters.AsNoTracking()
            .CountAsync(l => l.CreateUserId == userId && !l.IsDelete && !l.Source.IsDelete);

        var totalArchive = await _db.Erjas.AsNoTracking()
            .CountAsync(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                             && e.Source.IncomingLetter != null && !e.Source.IncomingLetter.IsDelete
                             && e.IsBayegani == true);

        var totalStarred = await inboxQuery.CountAsync(e => e.IsNeshan);

        return new IncomingLetterCartableStatsDto
        {
            TotalInbox = totalInbox,
            UnreadCount = unreadCount,
            TotalSent = totalSent,
            TotalArchive = totalArchive,
            TotalStarred = totalStarred
        };
    }

    public async Task<IncomingLetterDetailDto?> GetDetailAsync(int letterId, int userId, bool isAdmin)
    {
        var letter = await _db.IncomingLetters
            .Include(l => l.Source)
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);

        if (letter == null) return null;

        var myErjas = await _db.Erjas.Where(e => e.SourceId == letterId && e.ReciverUserId == userId && !e.IsDelete).ToListAsync();
        foreach (var e in myErjas)
        {
            if (!e.IsRead)
            {
                e.IsRead = true;
                e.ReadDate = DateTime.Now;
            }
        }
        if (myErjas.Any(e => !e.IsRead)) await _db.SaveChangesAsync();

        var creator = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == letter.CreateUserId);
        var creatorName = creator != null
            ? (string.IsNullOrEmpty(creator.FirstName + creator.LastName) ? creator.Username : $"{creator.FirstName} {creator.LastName}".Trim())
            : "ثبت‌کننده سیستم";

        var allErjas = await _db.Erjas.AsNoTracking()
            .Where(e => e.SourceId == letterId && !e.IsDelete)
            .OrderBy(e => e.ErjaId)
            .Select(e => new ErjaDto
            {
                ErjaId = e.ErjaId,
                SenderUserId = e.SenderUserId,
                SenderName = string.IsNullOrEmpty(e.UserSender!.FirstName + e.UserSender.LastName)
                    ? e.UserSender.Username
                    : (e.UserSender.FirstName + " " + e.UserSender.LastName).Trim(),
                ReciverUserId = e.ReciverUserId,
                ReciverName = string.IsNullOrEmpty(e.UserReciver!.FirstName + e.UserReciver.LastName)
                    ? e.UserReciver.Username
                    : (e.UserReciver.FirstName + " " + e.UserReciver.LastName).Trim(),
                Date = e.Date,
                Type = e.Type,
                MatnErja = e.MatnErja,
                MohlatPasokh = e.MohlatPasokh,
                IsRead = e.IsRead,
                ReadDate = e.ReadDate,
                Answer = e.Answer,
                AnswerDate = e.AnswerDate
            })
            .ToListAsync();

        var curErja = allErjas.LastOrDefault(e => e.ReciverUserId == userId);

        return new IncomingLetterDetailDto
        {
            LetterId = letter.Id,
            SourceId = letter.Id,
            LetterNumber = letter.LetterNumber ?? "",
            Number = letter.Number,
            NumberSabt = letter.NumberSabt,
            Title = letter.Title,
            Description = letter.Description,
            Ferestande = letter.Ferestande,
            NumberLetterVarede = letter.NumberLetterVarede ?? "",
            Date = letter.Date,
            DateErsal = letter.DateErsal,
            TypeErsal = letter.TypeErsal ?? "",
            DeliveryName = letter.DeliveryName ?? "",
            CreatorUserId = letter.CreateUserId,
            CreatorName = creatorName,
            Mahramanegi = letter.Mahramanegi,
            Foriat = letter.Foriat,
            IsNeshan = letter.IsNeshan,
            IsMine = letter.CreateUserId == userId,
            CurrentErja = curErja,
            Erjas = allErjas
        };
    }

    public async Task<int> AddAsync(AddIncomingLetterDto dto, int userId)
    {
        var pc = new PersianCalendar();
        var currentYear = pc.GetYear(DateTime.Now);

        var startOfYear = new DateTime(currentYear, 1, 1, new PersianCalendar());
        var endOfYear = new DateTime(currentYear + 1, 1, 1, new PersianCalendar());

        var maxNumber = await _db.IncomingLetters
            .Where(l => l.DateErsal >= startOfYear && l.DateErsal < endOfYear)
            .MaxAsync(l => (int?)l.Number) ?? 0;

        var nextNumber = maxNumber + 1;

        int numberSabt;
        if (dto.NumberSabt is > 0)
        {
            numberSabt = dto.NumberSabt.Value;
            var res = await _db.LetterNumberReservations.FirstOrDefaultAsync(r => r.TypeForm == 3 && r.NumberSabt == numberSabt && !r.IsUsed);
            if (res != null) res.IsUsed = true;
        }
        else
        {
            var maxSabt = await _db.IncomingLetters.MaxAsync(l => (int?)l.NumberSabt) ?? 0;
            numberSabt = maxSabt + 1;
        }

        var letterNumber = $"{currentYear}/V/{numberSabt}";

        var source = new LetterSource
        {
            SourceType = 3, // 3 = نامه وارده
            IsDelete = false
        };
        _db.LetterSources.Add(source);
        await _db.SaveChangesAsync();

        var letter = new IncomingLetter
        {
            Id = source.Id,
            LetterNumber = letterNumber,
            Number = nextNumber,
            NumberSabt = numberSabt,
            Title = dto.Title.Trim(),
            Ferestande = dto.Ferestande.Trim(),
            NumberLetterVarede = dto.NumberLetterVarede?.Trim(),
            Date = dto.Date,
            DateErsal = dto.DateErsal,
            TypeErsal = dto.TypeErsal?.Trim(),
            DeliveryName = dto.DeliveryName?.Trim(),
            Description = dto.Description,
            Mahramanegi = dto.Mahramanegi,
            Foriat = dto.Foriat,
            CreateUserId = userId,
            Creator = 1,
            IsNeshan = false,
            IsBayegani = false,
            IsDelete = false
        };

        _db.IncomingLetters.Add(letter);

        // اضافه کردن خود ثبت‌کننده یا گیرندگان اولیه در جدول Erja
        var recivers = dto.InitialReciverUserIds.Distinct().ToList();
        if (!recivers.Contains(userId)) recivers.Insert(0, userId);

        foreach (var rId in recivers)
        {
            _db.Erjas.Add(new Erja
            {
                SourceId = source.Id,
                SenderUserId = userId,
                ReciverUserId = rId,
                Date = DateTime.Now,
                Type = 1,
                MatnErja = string.IsNullOrWhiteSpace(dto.InitialMatnErja) ? "ثبت نامه وارده" : dto.InitialMatnErja.Trim(),
                MohlatPasokh = dto.InitialMohlatPasokh,
                IsRead = rId == userId,
                ReadDate = rId == userId ? DateTime.Now : null
            });
        }

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("incoming-letters");
        return letter.Id;
    }

    public async Task EditAsync(int letterId, EditIncomingLetterDto dto, int userId, bool isAdmin)
    {
        var letter = await _db.IncomingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) throw new InvalidOperationException("نامه یافت نشد.");
        if (letter.CreateUserId != userId && !isAdmin)
            throw new UnauthorizedAccessException("فقط ثبت‌کننده یا مدیر می‌تواند نامه را ویرایش کند.");

        letter.Title = dto.Title.Trim();
        letter.Ferestande = dto.Ferestande.Trim();
        letter.NumberLetterVarede = dto.NumberLetterVarede?.Trim();
        letter.Date = dto.Date;
        letter.DateErsal = dto.DateErsal;
        letter.TypeErsal = dto.TypeErsal?.Trim();
        letter.DeliveryName = dto.DeliveryName?.Trim();
        letter.Description = dto.Description;
        letter.Mahramanegi = dto.Mahramanegi;
        letter.Foriat = dto.Foriat;

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("incoming-letters");
    }

    public async Task DeleteAsync(int letterId, int userId, bool isAdmin)
    {
        var letter = await _db.IncomingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) return;
        if (letter.CreateUserId != userId && !isAdmin)
            throw new UnauthorizedAccessException("دستور حذف صادر نشد.");

        letter.IsDelete = true;
        var source = await _db.LetterSources.FirstOrDefaultAsync(s => s.Id == letterId);
        if (source != null) source.IsDelete = true;

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("incoming-letters");
    }

    public async Task<bool> ToggleLetterNeshanAsync(int letterId, int userId)
    {
        var letter = await _db.IncomingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) return false;
        letter.IsNeshan = !letter.IsNeshan;
        await _db.SaveChangesAsync();
        return letter.IsNeshan;
    }

    public async Task<PagedResult<IncomingLetterPickDto>> PickListAsync(int userId, string? search, int page = 1, int pageSize = 15)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;

        var q = _db.IncomingLetters.AsNoTracking()
            .Where(l => !l.IsDelete && (l.CreateUserId == userId || l.Source.Erjas.Any(e => e.ReciverUserId == userId && !e.IsDelete)));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(l => l.Title.Contains(s) || (l.LetterNumber ?? "").Contains(s) || l.Ferestande.Contains(s));
        }

        var totalCount = await q.CountAsync();

        var items = await q.OrderByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new IncomingLetterPickDto
            {
                SourceId = l.Id,
                LetterNumber = l.LetterNumber ?? "",
                NumberSabt = l.NumberSabt,
                Title = l.Title,
                Ferestande = l.Ferestande,
                Date = l.Date
            })
            .ToListAsync();

        return new PagedResult<IncomingLetterPickDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<List<LetterNumberReservationDto>> GetReservationsAsync(int userId, int typeForm = 3)
    {
        var list = await _db.LetterNumberReservations.AsNoTracking()
            .Where(r => r.TypeForm == typeForm && !r.IsUsed && !r.IsDelete)
            .OrderByDescending(r => r.NumberSabt)
            .Take(50)
            .ToListAsync();

        var userIds = list.Select(r => r.UserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);

        return list.Select(r => new LetterNumberReservationDto
        {
            Id = r.Id,
            TypeForm = r.TypeForm,
            NumberSabt = r.NumberSabt,
            DateRezerv = r.DateRezerv,
            UserId = r.UserId,
            UserName = users.TryGetValue(r.UserId, out var u) ? (string.IsNullOrEmpty(u.FirstName + u.LastName) ? u.Username : $"{u.FirstName} {u.LastName}".Trim()) : "سیستم",
            IsUsed = r.IsUsed
        }).ToList();
    }

    public async Task<List<LetterNumberReservationDto>> ReserveNumberAsync(int userId, int typeForm = 3, int count = 1)
    {
        count = Math.Clamp(count, 1, 20);

        var lastUsedSabt = await _db.IncomingLetters.MaxAsync(l => (int?)l.NumberSabt) ?? 0;
        var lastResSabt = await _db.LetterNumberReservations.Where(r => r.TypeForm == typeForm).MaxAsync(r => (int?)r.NumberSabt) ?? 0;

        var startSabt = Math.Max(lastUsedSabt, lastResSabt) + 1;
        var created = new List<LetterNumberReservation>();

        for (int i = 0; i < count; i++)
        {
            var res = new LetterNumberReservation
            {
                TypeForm = typeForm,
                NumberSabt = startSabt + i,
                DateRezerv = DateTime.Now,
                UserId = userId,
                IsUsed = false,
                IsDelete = false
            };
            _db.LetterNumberReservations.Add(res);
            created.Add(res);
        }

        await _db.SaveChangesAsync();
        return await GetReservationsAsync(userId, typeForm);
    }
}
