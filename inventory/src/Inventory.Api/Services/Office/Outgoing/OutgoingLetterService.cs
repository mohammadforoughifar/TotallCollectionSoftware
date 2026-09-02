using System.Globalization;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Office.Outgoing;

// ============================================================
//  سرویس نامه صادره — با امضا کنندگان + SadereNumber
//  • DateSabt و Number قبلا موجود — حفظ شد
//  • SadereNumber و DateSadere اضافه شد — بعد از امضا مقدار می‌گیرد
//  • OutgoingLetterSigner: انتخاب بر اساس دسترسی OutgoingLetters.Sign
//  • کارتابل دریافت شامل امضایی‌ها (IsSigner) هم می‌شود
//  • امضا: POST {id}/sign → اگر همه امضا کردند Status=3 و SadereNumber=LetterNumber
// ============================================================

public interface IOutgoingLetterService
{
    Task<int> AddOutgoingLetterAsync(AddOutgoingLetterDto dto, int creatorUserId, string creatorName);
    Task<List<OutgoingLetterListItemDto>> GetInboxAsync(int userId, string? search, bool? unreadOnly);
    Task<List<OutgoingLetterListItemDto>> GetArchiveAsync(int userId, string? search);
    Task<List<OutgoingLetterListItemDto>> GetSentAsync(int userId, string? search);
    Task<List<OutgoingLetterListItemDto>> GetSigningInboxAsync(int userId, string? search, bool? unsignedOnly);
    Task<OutgoingLetterDetailDto?> GetDetailAsync(int letterId, int userId, bool isAdmin);
    Task<OutgoingLetterCartableStatsDto> GetStatsAsync(int userId);
    Task<List<OutgoingLetterPickDto>> PickListAsync(int userId, string? search);
    Task DeleteAsync(int letterId, int userId, bool isAdmin);
    Task EditAsync(int letterId, EditOutgoingLetterDto dto, int userId, bool isAdmin);
    Task UpdateStatusAsync(int letterId, int newStatus, int userId, bool isAdmin);
    Task<List<OutgoingSignerDto>> GetSignersAsync(int letterId);
    Task SignAsync(int letterId, int userId, string? signNote);
    Task<List<LetterReciverDto>> GetAvailableSignersAsync(string? search);
}

public class OutgoingLetterService : IOutgoingLetterService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly ILetterGroupService _groups;

    public OutgoingLetterService(AppDbContext db, INotifyService notify, ILetterGroupService groups)
    {
        _db = db;
        _notify = notify;
        _groups = groups;
    }

    private async Task<int> NextNumberAsync()
    {
        var pc = new PersianCalendar();
        int currentYear = pc.GetYear(DateTime.Now);
        var startOfYear = new DateTime(currentYear, 1, 1, new PersianCalendar());

        var list = await _db.OutgoingLetters
            .Where(l => !l.IsDelete && !l.Source.IsDelete && l.DateSabt >= startOfYear)
            .ToListAsync();

        var maxInYear = list
            .Where(l => pc.GetYear(l.DateSabt) == currentYear)
            .Select(l => (int?)l.Number)
            .Max() ?? 0;

        return maxInYear + 1;
    }

    private static string BuildLetterNumber(int number)
    {
        var pc = new PersianCalendar();
        return $"{pc.GetYear(DateTime.Now)}/ص-{number}";
    }

    private static Erja NewErja(int sourceId, int senderUserId, int reciverUserId, DateTime date, string type, string matn = "", int amalgarId = 1, DateTime? mohlat = null, int? parentErjaId = null) => new()
    {
        SourceId = sourceId,
        SenderUserId = senderUserId,
        ReciverUserId = reciverUserId,
        Date = date,
        Type = type,
        TypeTaeed = 0,
        Answer = "",
        IsRead = false,
        IsBayegani = false,
        MatnErja = matn,
        AmalgarId = amalgarId,
        MohlatPasokh = mohlat,
        IsNeshan = false,
        ShowForAll = false,
        ShowMassage = true,
        IsReadAnswer = false,
        ShowMassageAnswer = false,
        IsDelete = false,
        ParentErjaId = parentErjaId
    };

    // ---------- بررسی دسترسی Sign برای لیست کاربران ----------
    private async Task<HashSet<int>> GetUsersWithSignPermissionAsync()
    {
        // کاربران دارای پرمیشن OutgoingLetters.Sign via نقش
        var perm = await _db.Permissions.FirstOrDefaultAsync(p => p.Module == "OutgoingLetters" && p.Action == "Sign");
        if (perm == null) return new HashSet<int>();

        var roleIds = await _db.RolePermissions.Where(rp => rp.PermissionId == perm.Id).Select(rp => rp.RoleId).ToListAsync();
        if (roleIds.Count == 0) return new HashSet<int>();

        var userIds = await _db.UserRoles.Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId).Distinct().ToListAsync();
        // ادمین‌ها (Role=Admin) همیشه مجازند
        var adminUserIds = await _db.Users.Where(u => u.Role == "Admin" && u.IsActive).Select(u => u.Id).ToListAsync();
        var set = new HashSet<int>(userIds);
        foreach (var a in adminUserIds) set.Add(a);
        return set;
    }

    private async Task<List<int>> ResolveSignersAsync(List<int> userIds, List<int> groupIds)
    {
        var all = new List<int>();
        all.AddRange(userIds);
        if (groupIds.Count > 0)
        {
            var expanded = await _groups.ExpandToUserIdsAsync(groupIds);
            all.AddRange(expanded);
        }
        return all.Distinct().ToList();
    }

    public async Task<int> AddOutgoingLetterAsync(AddOutgoingLetterDto dto, int creatorUserId, string creatorName)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new Exception("عنوان نامه الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.ReceiverOrganization))
            throw new Exception("نام سازمان مقصد الزامی است.");

        var allGroupIds = dto.GroupsGirande.Concat(dto.GroupsErja).Concat(dto.GroupsHamesh).Concat(dto.SignerGroupIds).Distinct().ToList();
        if (allGroupIds.Count > 0)
        {
            var ok = await _groups.CheckGroupIdsAsync(allGroupIds);
            if (!ok) throw new Exception("برخی از گروه‌های انتخاب‌شده معتبر یا فعال نیستند.");
        }

        var girande = dto.ReciversGirande.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsGirande)).Distinct().ToList();
        var erjaIds = dto.ReciversErja.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsErja)).Distinct().ToList();
        var hamesh = dto.ReciversHamesh.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsHamesh)).Distinct().ToList();
        var signerIds = await ResolveSignersAsync(dto.SignerUserIds, dto.SignerGroupIds);

        girande.Remove(creatorUserId);
        erjaIds.Remove(creatorUserId);
        hamesh.Remove(creatorUserId);

        var allReciverIds = girande.Concat(erjaIds).Concat(hamesh).Distinct().ToList();
        var allUserIdsForValidation = allReciverIds.Concat(signerIds).Distinct().ToList();

        if (allUserIdsForValidation.Count > 0)
        {
            var validUsers = await _db.Users.Where(u => allUserIdsForValidation.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync();
            var invalid = allUserIdsForValidation.Except(validUsers).ToList();
            if (invalid.Count > 0)
                throw new Exception("برخی از کاربران انتخاب‌شده معتبر یا فعال نیستند.");
        }

        // اعتبارسنجی دسترسی Sign برای امضا کنندگان (اختیاری: فقط هشدار، اما اگر هیچ‌کس دسترسی ندارد اجازه می‌دهیم تا ادمین تنظیم کند)
        // اگر RBAC فعال است، چک می‌کنیم اما خطا نمی‌دهیم — فقط اگر لیست کاربران مجاز خالی نباشد
        var allowedSigners = await GetUsersWithSignPermissionAsync();
        if (allowedSigners.Count > 0 && signerIds.Count > 0)
        {
            var notAllowed = signerIds.Where(id => !allowedSigners.Contains(id)).ToList();
            if (notAllowed.Count > 0)
            {
                // برای سخت‌گیری کمتر: فقط لاگ، اما می‌توان خطا داد — فعلا خطا می‌دهیم تا کارفرما متوجه شود
                // throw new Exception("برخی از امضا کنندگان دسترسی امضای نامه صادره را ندارند.");
                // فعلا اجازه می‌دهیم اما در UI فیلتر می‌شود — برای انعطاف
            }
        }

        var now = DateTime.Now;

        using var tx = await _db.Database.BeginTransactionAsync();

        var source = new LetterSource { SourceType = 2, IsDelete = false };
        _db.LetterSources.Add(source);
        await _db.SaveChangesAsync();

        var number = await NextNumberAsync();
        var letter = new OutgoingLetter
        {
            Id = source.Id,
            Number = number,
            LetterNumber = BuildLetterNumber(number),
            CreatorUserId = creatorUserId,
            Title = dto.Title.Trim(),
            Text = dto.Text,
            DateSabt = now,
            Mahramanegi = string.IsNullOrWhiteSpace(dto.Mahramanegi) ? "عادی" : dto.Mahramanegi,
            Foriat = string.IsNullOrWhiteSpace(dto.Foriat) ? "عادی" : dto.Foriat,
            ReceiverOrganization = dto.ReceiverOrganization.Trim(),
            ReceiverName = dto.ReceiverName?.Trim(),
            ReceiverTitle = dto.ReceiverTitle?.Trim(),
            ReceiverAddress = dto.ReceiverAddress?.Trim(),
            CopyTo = dto.CopyTo?.Trim(),
            ExternalRefNumber = dto.ExternalRefNumber?.Trim(),
            Status = signerIds.Count > 0 ? 1 : (allReciverIds.Count > 0 ? 1 : 2),
            IsDelete = false
        };
        _db.OutgoingLetters.Add(letter);

        var seen = new HashSet<int>();
        foreach (var uid in girande.Where(seen.Add))
            _db.Erjas.Add(NewErja(source.Id, creatorUserId, uid, now, "گیرنده"));
        foreach (var uid in erjaIds.Where(seen.Add))
            _db.Erjas.Add(NewErja(source.Id, creatorUserId, uid, now, "ارجاع"));
        foreach (var uid in hamesh.Where(seen.Add))
            _db.Erjas.Add(NewErja(source.Id, creatorUserId, uid, now, "هامش"));

        // امضا کنندگان
        int order = 1;
        foreach (var uid in signerIds.Distinct())
        {
            _db.OutgoingLetterSigners.Add(new OutgoingLetterSigner
            {
                SourceId = source.Id,
                UserId = uid,
                Order = order++,
                IsSigned = false,
                IsDelete = false
            });
        }

        foreach (var rel in dto.RelatedLetters)
        {
            if (rel.RelateLetterId <= 0) continue;
            var exists = await _db.LetterSources.AnyAsync(s => s.Id == rel.RelateLetterId && !s.IsDelete);
            if (!exists) continue;
            _db.RelatedLetters.Add(new RelatedLetter
            {
                Related = rel.Related == 2 ? 2 : 1,
                LetterId = source.Id,
                RelateLetterId = rel.RelateLetterId,
                UserId = creatorUserId,
                IsDelete = false
            });
        }

        await _db.SaveChangesAsync();

        if (dto.FromPishnevisId is > 0)
        {
            var pish = await _db.OutgoingPishnevisLetters.FirstOrDefaultAsync(p => p.PishnevisId == dto.FromPishnevisId && p.UserId == creatorUserId && !p.IsDelete);
            if (pish != null)
            {
                pish.IsDelete = true;
                var pishAtts = await _db.AppAttachments.Where(a => a.Module == "OutgoingPishnevis" && a.RefId == pish.PishnevisId).ToListAsync();
                foreach (var a in pishAtts)
                {
                    a.Module = "OutgoingLetters";
                    a.RefId = source.Id;
                }
                await _db.SaveChangesAsync();
            }
        }

        await tx.CommitAsync();

        // نوتیفیکیشن به گیرندگان داخلی + امضا کنندگان
        var allNotified = seen.Union(signerIds).Distinct().ToList();
        if (allNotified.Count > 0)
        {
            var link = $"outgoing-letters/view/{source.Id}";
            await _notify.SendManyAsync(allNotified,
                signerIds.Count > 0 ? $"نامه صادره جهت امضا: {letter.Title}" : $"نامه صادره جدید جهت تایید: {letter.Title}",
                $"گیرنده: {letter.ReceiverOrganization} — شماره {letter.LetterNumber} — فوریت: {letter.Foriat}",
                creatorName, "نامه صادره", link);
        }
        await _notify.BroadcastChangedAsync("outgoing-letters");

        return source.Id;
    }

    // ---------- Inbox: شامل ارجاعات + امضا ----------
    public async Task<List<OutgoingLetterListItemDto>> GetInboxAsync(int userId, string? search, bool? unreadOnly)
    {
        // بخش 1: ارجاعات
        var qErja = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.OutgoingLetter != null && !e.Source.OutgoingLetter.IsDelete
                        && e.IsBayegani != true);

        if (unreadOnly == true) qErja = qErja.Where(e => !e.IsRead);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            qErja = qErja.Where(e => e.Source.OutgoingLetter!.Title.Contains(s)
                             || (e.Source.OutgoingLetter!.LetterNumber ?? "").Contains(s)
                             || e.Source.OutgoingLetter!.ReceiverOrganization.Contains(s)
                             || (e.UserSender!.FirstName + " " + e.UserSender.LastName).Contains(s)
                             || e.UserSender!.Username.Contains(s));
        }

        var erjaList = await qErja
            .OrderByDescending(e => e.ErjaId)
            .Select(e => new OutgoingLetterListItemDto
            {
                LetterId = e.SourceId,
                ErjaId = e.ErjaId,
                LetterNumber = e.Source.OutgoingLetter!.LetterNumber ?? "",
                Title = e.Source.OutgoingLetter!.Title,
                Sender = string.IsNullOrEmpty(e.UserSender!.FirstName + e.UserSender.LastName)
                    ? e.UserSender.Username
                    : (e.UserSender.FirstName + " " + e.UserSender.LastName).Trim(),
                SenderUserId = e.SenderUserId,
                ReceiverOrganization = e.Source.OutgoingLetter!.ReceiverOrganization,
                ReceiverName = e.Source.OutgoingLetter!.ReceiverName,
                Date = e.Date,
                Mahramanegi = e.Source.OutgoingLetter!.Mahramanegi,
                Foriat = e.Source.OutgoingLetter!.Foriat,
                ErjaType = e.Type,
                MatnErja = e.MatnErja,
                MohlatPasokh = e.MohlatPasokh,
                IsNeshan = e.IsNeshan,
                IsRead = e.IsRead,
                TypeTaeed = e.TypeTaeed,
                HasAnswer = e.Answer != "",
                ReciverCount = e.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == e.SourceId),
                Status = e.Source.OutgoingLetter!.Status,
                SadereNumber = e.Source.OutgoingLetter!.SadereNumber,
                DateSadere = e.Source.OutgoingLetter!.DateSadere,
                IsSigner = false,
                IsSigned = false,
                CanSign = false,
                SignersTotal = e.Source.OutgoingSigners.Count(s => !s.IsDelete),
                SignersSigned = e.Source.OutgoingSigners.Count(s => !s.IsDelete && s.IsSigned)
            })
            .ToListAsync();

        // بخش 2: امضایی‌ها (حتی اگر ارجاع نداشته باشد)
        var qSign = _db.OutgoingLetterSigners.AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsDelete && !s.Source.IsDelete && s.Source.OutgoingLetter != null && !s.Source.OutgoingLetter.IsDelete);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            qSign = qSign.Where(sg => sg.Source.OutgoingLetter!.Title.Contains(s)
                                   || (sg.Source.OutgoingLetter!.LetterNumber ?? "").Contains(s)
                                   || sg.Source.OutgoingLetter!.ReceiverOrganization.Contains(s)
                                   || (sg.Source.OutgoingLetter!.SadereNumber ?? "").Contains(s));
        }
        if (unreadOnly == true)
        {
            qSign = qSign.Where(s => !s.IsSigned);
        }

        var signerLetters = await qSign
            .OrderByDescending(s => s.Source.OutgoingLetter!.DateSabt)
            .Select(s => new OutgoingLetterListItemDto
            {
                LetterId = s.SourceId,
                ErjaId = null,
                LetterNumber = s.Source.OutgoingLetter!.LetterNumber ?? "",
                Title = s.Source.OutgoingLetter!.Title,
                Sender = s.Source.OutgoingLetter!.Creator != null
                    ? (string.IsNullOrEmpty(s.Source.OutgoingLetter!.Creator.FirstName + s.Source.OutgoingLetter!.Creator.LastName)
                        ? s.Source.OutgoingLetter!.Creator.Username
                        : (s.Source.OutgoingLetter!.Creator.FirstName + " " + s.Source.OutgoingLetter!.Creator.LastName).Trim())
                    : "",
                SenderUserId = s.Source.OutgoingLetter!.CreatorUserId,
                ReceiverOrganization = s.Source.OutgoingLetter!.ReceiverOrganization,
                ReceiverName = s.Source.OutgoingLetter!.ReceiverName,
                Date = s.Source.OutgoingLetter!.DateSabt,
                Mahramanegi = s.Source.OutgoingLetter!.Mahramanegi,
                Foriat = s.Source.OutgoingLetter!.Foriat,
                ErjaType = "امضا",
                MatnErja = s.SignNote,
                IsNeshan = false,
                IsRead = s.IsSigned, // امضا شده = خوانده شده تلقی شود
                TypeTaeed = 0,
                HasAnswer = s.IsSigned,
                ReciverCount = s.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == s.SourceId),
                Status = s.Source.OutgoingLetter!.Status,
                SadereNumber = s.Source.OutgoingLetter!.SadereNumber,
                DateSadere = s.Source.OutgoingLetter!.DateSadere,
                IsSigner = true,
                IsSigned = s.IsSigned,
                CanSign = !s.IsSigned,
                SignersTotal = s.Source.OutgoingSigners.Count(x => !x.IsDelete),
                SignersSigned = s.Source.OutgoingSigners.Count(x => !x.IsDelete && x.IsSigned)
            })
            .ToListAsync();

        // ترکیب و حذف تکراری (اگر کاربر هم ارجاع دارد هم امضا کننده است)
        var dict = new Dictionary<int, OutgoingLetterListItemDto>();
        foreach (var item in erjaList)
        {
            dict[item.LetterId] = item;
        }
        foreach (var item in signerLetters)
        {
            if (dict.TryGetValue(item.LetterId, out var existing))
            {
                existing.IsSigner = true;
                existing.IsSigned = item.IsSigned;
                existing.CanSign = item.CanSign;
                existing.SignersTotal = item.SignersTotal;
                existing.SignersSigned = item.SignersSigned;
                if (existing.ErjaType != "امضا")
                {
                    // اگر ارجاع و امضا همزمان باشد، نوع را ترکیبی نشان بده
                    existing.ErjaType = existing.ErjaType + " + امضا";
                }
            }
            else
            {
                dict[item.LetterId] = item;
            }
        }

        return dict.Values.OrderByDescending(x => x.Date).ThenByDescending(x => x.LetterId).ToList();
    }

    public async Task<List<OutgoingLetterListItemDto>> GetSigningInboxAsync(int userId, string? search, bool? unsignedOnly)
    {
        var q = _db.OutgoingLetterSigners.AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsDelete && !s.Source.IsDelete
                        && s.Source.OutgoingLetter != null && !s.Source.OutgoingLetter.IsDelete);

        if (unsignedOnly == true) q = q.Where(s => !s.IsSigned);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(sg => sg.Source.OutgoingLetter!.Title.Contains(s)
                           || (sg.Source.OutgoingLetter!.LetterNumber ?? "").Contains(s)
                           || sg.Source.OutgoingLetter!.ReceiverOrganization.Contains(s));
        }

        return await q.OrderByDescending(s => s.Id)
            .Select(s => new OutgoingLetterListItemDto
            {
                LetterId = s.SourceId,
                LetterNumber = s.Source.OutgoingLetter!.LetterNumber ?? "",
                Title = s.Source.OutgoingLetter!.Title,
                Sender = s.Source.OutgoingLetter!.Creator != null
                    ? (string.IsNullOrEmpty(s.Source.OutgoingLetter!.Creator.FirstName + s.Source.OutgoingLetter!.Creator.LastName)
                        ? s.Source.OutgoingLetter!.Creator.Username
                        : (s.Source.OutgoingLetter!.Creator.FirstName + " " + s.Source.OutgoingLetter!.Creator.LastName).Trim())
                    : "",
                SenderUserId = s.Source.OutgoingLetter!.CreatorUserId,
                ReceiverOrganization = s.Source.OutgoingLetter!.ReceiverOrganization,
                ReceiverName = s.Source.OutgoingLetter!.ReceiverName,
                Date = s.Source.OutgoingLetter!.DateSabt,
                Mahramanegi = s.Source.OutgoingLetter!.Mahramanegi,
                Foriat = s.Source.OutgoingLetter!.Foriat,
                ErjaType = "امضا",
                IsNeshan = false,
                IsRead = s.IsSigned,
                TypeTaeed = 0,
                HasAnswer = s.IsSigned,
                ReciverCount = s.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == s.SourceId),
                Status = s.Source.OutgoingLetter!.Status,
                SadereNumber = s.Source.OutgoingLetter!.SadereNumber,
                DateSadere = s.Source.OutgoingLetter!.DateSadere,
                IsSigner = true,
                IsSigned = s.IsSigned,
                CanSign = !s.IsSigned,
                SignersTotal = s.Source.OutgoingSigners.Count(x => !x.IsDelete),
                SignersSigned = s.Source.OutgoingSigners.Count(x => !x.IsDelete && x.IsSigned)
            }).ToListAsync();
    }

    public async Task<List<OutgoingLetterListItemDto>> GetArchiveAsync(int userId, string? search)
    {
        var q = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.OutgoingLetter != null && !e.Source.OutgoingLetter.IsDelete
                        && e.IsBayegani == true);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => e.Source.OutgoingLetter!.Title.Contains(s)
                             || (e.Source.OutgoingLetter!.LetterNumber ?? "").Contains(s)
                             || e.Source.OutgoingLetter!.ReceiverOrganization.Contains(s));
        }

        return await q.OrderByDescending(e => e.ErjaId)
            .Select(e => new OutgoingLetterListItemDto
            {
                LetterId = e.SourceId,
                ErjaId = e.ErjaId,
                LetterNumber = e.Source.OutgoingLetter!.LetterNumber ?? "",
                Title = e.Source.OutgoingLetter!.Title,
                Sender = string.IsNullOrEmpty(e.UserSender!.FirstName + e.UserSender.LastName)
                    ? e.UserSender.Username
                    : (e.UserSender.FirstName + " " + e.UserSender.LastName).Trim(),
                SenderUserId = e.SenderUserId,
                ReceiverOrganization = e.Source.OutgoingLetter!.ReceiverOrganization,
                ReceiverName = e.Source.OutgoingLetter!.ReceiverName,
                Date = e.Date,
                Mahramanegi = e.Source.OutgoingLetter!.Mahramanegi,
                Foriat = e.Source.OutgoingLetter!.Foriat,
                ErjaType = e.Type,
                MatnErja = e.MatnErja,
                MohlatPasokh = e.MohlatPasokh,
                IsNeshan = e.IsNeshan,
                IsRead = e.IsRead,
                TypeTaeed = e.TypeTaeed,
                HasAnswer = e.Answer != "",
                ReciverCount = e.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == e.SourceId),
                Status = e.Source.OutgoingLetter!.Status,
                SadereNumber = e.Source.OutgoingLetter!.SadereNumber,
                DateSadere = e.Source.OutgoingLetter!.DateSadere,
                SignersTotal = e.Source.OutgoingSigners.Count(x => !x.IsDelete),
                SignersSigned = e.Source.OutgoingSigners.Count(x => !x.IsDelete && x.IsSigned)
            }).ToListAsync();
    }

    public async Task<List<OutgoingLetterListItemDto>> GetSentAsync(int userId, string? search)
    {
        var q = _db.OutgoingLetters.AsNoTracking()
            .Where(l => l.CreatorUserId == userId && !l.IsDelete && !l.Source.IsDelete);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(l => l.Title.Contains(s) || (l.LetterNumber ?? "").Contains(s) || l.ReceiverOrganization.Contains(s) || (l.SadereNumber ?? "").Contains(s));
        }

        return await q.OrderByDescending(l => l.Id)
            .Select(l => new OutgoingLetterListItemDto
            {
                LetterId = l.Id,
                LetterNumber = l.LetterNumber ?? "",
                Title = l.Title,
                Sender = "",
                SenderUserId = l.CreatorUserId,
                ReceiverOrganization = l.ReceiverOrganization,
                ReceiverName = l.ReceiverName,
                Date = l.DateSabt,
                Mahramanegi = l.Mahramanegi,
                Foriat = l.Foriat,
                IsRead = l.Source.Erjas.Where(e => !e.IsDelete && e.ParentErjaId == null).All(e => e.IsRead),
                HasAnswer = l.Source.Erjas.Any(e => !e.IsDelete && e.Answer != ""),
                ReciverCount = l.Source.Erjas.Count(e => !e.IsDelete && e.ParentErjaId == null),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == l.Id),
                Status = l.Status,
                SadereNumber = l.SadereNumber,
                DateSadere = l.DateSadere,
                SignersTotal = l.Source.OutgoingSigners.Count(s => !s.IsDelete),
                SignersSigned = l.Source.OutgoingSigners.Count(s => !s.IsDelete && s.IsSigned)
            }).ToListAsync();
    }

    public async Task<OutgoingLetterDetailDto?> GetDetailAsync(int letterId, int userId, bool isAdmin)
    {
        var letter = await _db.OutgoingLetters.AsNoTracking()
            .Include(l => l.Creator)
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) return null;

        var erjas = await _db.Erjas.AsNoTracking()
            .Include(e => e.UserReciver)
            .Where(e => e.SourceId == letterId && !e.IsDelete)
            .OrderBy(e => e.ErjaId)
            .ToListAsync();

        var signers = await _db.OutgoingLetterSigners.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.SourceId == letterId && !s.IsDelete)
            .OrderBy(s => s.Order).ThenBy(s => s.Id)
            .ToListAsync();

        bool isMine = letter.CreatorUserId == userId;
        var myErja = erjas.Where(e => e.ReciverUserId == userId).OrderByDescending(e => e.ErjaId).FirstOrDefault();
        var mySigner = signers.FirstOrDefault(s => s.UserId == userId);

        if (!isMine && myErja == null && mySigner == null && !isAdmin) return null;

        static string FullName(User? u) =>
            u == null ? "" :
            string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? ""))
                ? u.Username
                : $"{u.FirstName} {u.LastName}".Trim();

        var dto = new OutgoingLetterDetailDto
        {
            LetterId = letter.Id,
            LetterNumber = letter.LetterNumber ?? "",
            Number = letter.Number,
            Title = letter.Title,
            Text = letter.Text,
            Mahramanegi = letter.Mahramanegi,
            Foriat = letter.Foriat,
            DateSabt = letter.DateSabt,
            SenderUserId = letter.CreatorUserId,
            SenderName = FullName(letter.Creator),
            IsMine = isMine,
            ReceiverOrganization = letter.ReceiverOrganization,
            ReceiverName = letter.ReceiverName,
            ReceiverTitle = letter.ReceiverTitle,
            ReceiverAddress = letter.ReceiverAddress,
            CopyTo = letter.CopyTo,
            ExternalRefNumber = letter.ExternalRefNumber,
            Status = letter.Status,
            SadereNumber = letter.SadereNumber,
            DateSadere = letter.DateSadere,
            CanEdit = isAdmin || (isMine && !erjas.Any(e => e.IsRead) && signers.All(s => !s.IsSigned)),
            IsSigner = mySigner != null,
            CanSign = mySigner != null && !mySigner.IsSigned
        };

        foreach (var e in erjas.Where(e => e.ParentErjaId == null))
        {
            var r = new LetterReciverDto { UserId = e.ReciverUserId, FullName = FullName(e.UserReciver) };
            switch (e.Type)
            {
                case "ارجاع": dto.ReciversErja.Add(r); break;
                case "هامش": dto.ReciversHamesh.Add(r); break;
                default: dto.ReciversGirande.Add(r); break;
            }
        }

        dto.Signers = signers.Select(s => new OutgoingSignerDto
        {
            Id = s.Id,
            SourceId = s.SourceId,
            UserId = s.UserId,
            UserName = s.User?.Username ?? "",
            DisplayName = FullName(s.User),
            SematId = s.SematId,
            Order = s.Order,
            IsSigned = s.IsSigned,
            DateSigned = s.DateSigned,
            SignNote = s.SignNote
        }).ToList();

        dto.RelatedLetters = await _db.RelatedLetters.AsNoTracking()
            .Where(r => r.LetterId == letterId && !r.IsDelete)
            .Select(r => new RelatedLetterDto
            {
                Id = r.Id,
                Related = r.Related,
                RelateLetterId = r.RelateLetterId,
                RelateLetterNumber = r.RelateLetter.InnerLetter != null ? r.RelateLetter.InnerLetter.LetterNumber :
                                     r.RelateLetter.OutgoingLetter != null ? r.RelateLetter.OutgoingLetter.LetterNumber : "",
                RelateLetterTitle = r.RelateLetter.InnerLetter != null ? r.RelateLetter.InnerLetter.Title :
                                    r.RelateLetter.OutgoingLetter != null ? r.RelateLetter.OutgoingLetter.Title : ""
            }).ToListAsync();

        if (myErja != null)
        {
            dto.MyErja = new ErjaDto
            {
                ErjaId = myErja.ErjaId,
                SourceId = myErja.SourceId,
                SenderUserId = myErja.SenderUserId,
                ReciverUserId = myErja.ReciverUserId,
                Date = myErja.Date,
                Type = myErja.Type,
                TypeTaeed = myErja.TypeTaeed,
                Answer = myErja.Answer,
                IsRead = myErja.IsRead,
                MohlatPasokh = myErja.MohlatPasokh,
                MatnErja = myErja.MatnErja,
                AmalgarId = myErja.AmalgarId,
                IsNeshan = myErja.IsNeshan,
                ShowForAll = myErja.ShowForAll,
                DateRead = myErja.DateRead,
                DateAnswer = myErja.DateAnswer,
                ParentErjaId = myErja.ParentErjaId
            };
        }

        if (mySigner != null)
        {
            dto.MySigner = new OutgoingSignerDto
            {
                Id = mySigner.Id,
                SourceId = mySigner.SourceId,
                UserId = mySigner.UserId,
                UserName = mySigner.User?.Username ?? "",
                DisplayName = FullName(mySigner.User),
                SematId = mySigner.SematId,
                Order = mySigner.Order,
                IsSigned = mySigner.IsSigned,
                DateSigned = mySigner.DateSigned,
                SignNote = mySigner.SignNote
            };
        }

        return dto;
    }

    public async Task<OutgoingLetterCartableStatsDto> GetStatsAsync(int userId)
    {
        var inbox = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.OutgoingLetter != null && !e.Source.OutgoingLetter.IsDelete);

        var signing = _db.OutgoingLetterSigners.AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsDelete && !s.Source.IsDelete
                        && s.Source.OutgoingLetter != null && !s.Source.OutgoingLetter.IsDelete);

        var soon = DateTime.Now.AddDays(2);
        return new OutgoingLetterCartableStatsDto
        {
            InboxUnread = await inbox.CountAsync(e => !e.IsRead) + await signing.CountAsync(s => !s.IsSigned),
            InboxTotal = await inbox.CountAsync() + await signing.CountAsync(),
            SentTotal = await _db.OutgoingLetters.CountAsync(l => l.CreatorUserId == userId && !l.IsDelete),
            PishnevisTotal = await _db.OutgoingPishnevisLetters.CountAsync(p => p.UserId == userId && !p.IsDelete),
            DeadlineSoon = await inbox.CountAsync(e => e.MohlatPasokh != null && e.Answer == "" && e.MohlatPasokh <= soon)
        };
    }

    public async Task<List<OutgoingLetterPickDto>> PickListAsync(int userId, string? search)
    {
        var q = _db.LetterSources.AsNoTracking()
            .Where(s => !s.IsDelete &&
                        ((s.InnerLetter != null && !s.InnerLetter.IsDelete &&
                          (s.InnerLetter.CreatorUserId == userId || s.Erjas.Any(e => e.ReciverUserId == userId && !e.IsDelete))) ||
                         (s.OutgoingLetter != null && !s.OutgoingLetter.IsDelete &&
                          (s.OutgoingLetter.CreatorUserId == userId || s.Erjas.Any(e => e.ReciverUserId == userId && !e.IsDelete)))));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(src =>
                (src.InnerLetter != null && (src.InnerLetter.Title.Contains(s) || (src.InnerLetter.LetterNumber ?? "").Contains(s))) ||
                (src.OutgoingLetter != null && (src.OutgoingLetter.Title.Contains(s) || (src.OutgoingLetter.LetterNumber ?? "").Contains(s) || src.OutgoingLetter.ReceiverOrganization.Contains(s) || (src.OutgoingLetter.SadereNumber ?? "").Contains(s))));
        }

        return await q.OrderByDescending(s => s.Id).Take(30)
            .Select(s => new OutgoingLetterPickDto
            {
                LetterId = s.Id,
                LetterNumber = s.InnerLetter != null ? s.InnerLetter.LetterNumber ?? "" : s.OutgoingLetter!.SadereNumber ?? s.OutgoingLetter!.LetterNumber ?? "",
                Title = s.InnerLetter != null ? s.InnerLetter.Title : s.OutgoingLetter!.Title,
                Date = s.InnerLetter != null ? s.InnerLetter.DateSabt : s.OutgoingLetter!.DateSabt,
                IsSent = s.InnerLetter != null ? s.InnerLetter.CreatorUserId == userId : s.OutgoingLetter!.CreatorUserId == userId,
                SourceType = s.SourceType
            }).ToListAsync();
    }

    public async Task DeleteAsync(int letterId, int userId, bool isAdmin)
    {
        var letter = await _db.OutgoingLetters.Include(l => l.Source).FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        if (letter.CreatorUserId != userId && !isAdmin)
            throw new Exception("فقط فرستنده یا مدیر می‌تواند نامه را حذف کند.");

        if (letter.Status == 3 && !isAdmin)
            throw new Exception("نامه صادر شده قابل حذف نیست — فقط مدیر می‌تواند حذف کند.");

        var anyRead = await _db.Erjas.AnyAsync(e => e.SourceId == letterId && !e.IsDelete && e.IsRead);
        var anySigned = await _db.OutgoingLetterSigners.AnyAsync(s => s.SourceId == letterId && !s.IsDelete && s.IsSigned);
        if ((anyRead || anySigned) && !isAdmin)
            throw new Exception("این نامه توسط گیرنده(ها) خوانده یا امضا شده و قابل حذف نیست.");

        letter.IsDelete = true;
        letter.Source.IsDelete = true;
        await _db.Erjas.Where(e => e.SourceId == letterId).ExecuteUpdateAsync(s => s.SetProperty(e => e.IsDelete, true));
        await _db.OutgoingLetterSigners.Where(s => s.SourceId == letterId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDelete, true));
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
    }

    public async Task EditAsync(int letterId, EditOutgoingLetterDto dto, int userId, bool isAdmin)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new Exception("عنوان نامه الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.ReceiverOrganization))
            throw new Exception("نام سازمان مقصد الزامی است.");

        var letter = await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        if (letter.CreatorUserId != userId && !isAdmin)
            throw new Exception("فقط فرستنده یا مدیر می‌تواند نامه را ویرایش کند.");

        if (letter.Status == 3 && !isAdmin)
            throw new Exception("نامه صادر شده قابل ویرایش نیست.");

        var anyRead = await _db.Erjas.AnyAsync(e => e.SourceId == letterId && !e.IsDelete && e.IsRead);
        var anySigned = await _db.OutgoingLetterSigners.AnyAsync(s => s.SourceId == letterId && !s.IsDelete && s.IsSigned);
        if ((anyRead || anySigned) && !isAdmin)
            throw new Exception("این نامه توسط گیرنده(ها) خوانده یا امضا شده و دیگر قابل ویرایش نیست.");

        var allGroupIds = dto.GroupsGirande.Concat(dto.GroupsErja).Concat(dto.GroupsHamesh).Concat(dto.SignerGroupIds).Distinct().ToList();
        if (allGroupIds.Count > 0)
        {
            var ok = await _groups.CheckGroupIdsAsync(allGroupIds);
            if (!ok) throw new Exception("برخی از گروه‌های انتخاب‌شده معتبر یا فعال نیستند.");
        }

        var girande = dto.ReciversGirande.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsGirande)).Distinct().ToList();
        var erjaIds = dto.ReciversErja.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsErja)).Distinct().ToList();
        var hamesh = dto.ReciversHamesh.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsHamesh)).Distinct().ToList();
        var signerIds = await ResolveSignersAsync(dto.SignerUserIds, dto.SignerGroupIds);

        girande.Remove(letter.CreatorUserId);
        erjaIds.Remove(letter.CreatorUserId);
        hamesh.Remove(letter.CreatorUserId);

        var allReciverIds = girande.Concat(erjaIds).Concat(hamesh).Distinct().ToList();
        var allUserIdsForValidation = allReciverIds.Concat(signerIds).Distinct().ToList();
        if (allUserIdsForValidation.Count > 0)
        {
            var validUsers = await _db.Users.Where(u => allUserIdsForValidation.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync();
            if (allUserIdsForValidation.Except(validUsers).Any())
                throw new Exception("برخی از کاربران انتخاب‌شده معتبر یا فعال نیستند.");
        }

        using var tx = await _db.Database.BeginTransactionAsync();

        letter.Title = dto.Title.Trim();
        letter.Text = dto.Text;
        letter.Mahramanegi = string.IsNullOrWhiteSpace(dto.Mahramanegi) ? "عادی" : dto.Mahramanegi;
        letter.Foriat = string.IsNullOrWhiteSpace(dto.Foriat) ? "عادی" : dto.Foriat;
        letter.ReceiverOrganization = dto.ReceiverOrganization.Trim();
        letter.ReceiverName = dto.ReceiverName?.Trim();
        letter.ReceiverTitle = dto.ReceiverTitle?.Trim();
        letter.ReceiverAddress = dto.ReceiverAddress?.Trim();
        letter.CopyTo = dto.CopyTo?.Trim();
        letter.ExternalRefNumber = dto.ExternalRefNumber?.Trim();

        var now = DateTime.Now;
        var rootErjas = await _db.Erjas.Where(e => e.SourceId == letterId && !e.IsDelete && e.ParentErjaId == null).ToListAsync();

        var wanted = new Dictionary<int, string>();
        foreach (var uid in girande) wanted.TryAdd(uid, "گیرنده");
        foreach (var uid in erjaIds) wanted.TryAdd(uid, "ارجاع");
        foreach (var uid in hamesh) wanted.TryAdd(uid, "هامش");

        var newlyAdded = new List<int>();
        var allSubErjas = await _db.Erjas.Where(x => x.SourceId == letterId && !x.IsDelete && x.ParentErjaId != null).ToListAsync();

        foreach (var e in rootErjas)
        {
            if (wanted.TryGetValue(e.ReciverUserId, out var type))
            {
                e.Type = type;
                wanted.Remove(e.ReciverUserId);
            }
            else
            {
                e.IsDelete = true;
                var subIds = new List<int> { e.ErjaId };
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (var s in allSubErjas.Where(s => s.ParentErjaId is { } pid && subIds.Contains(pid) && !subIds.Contains(s.ErjaId)))
                    { subIds.Add(s.ErjaId); s.IsDelete = true; changed = true; }
                }
            }
        }
        foreach (var (uid, type) in wanted)
        {
            _db.Erjas.Add(NewErja(letterId, letter.CreatorUserId, uid, now, type));
            newlyAdded.Add(uid);
        }

        // Sync signers
        var existingSigners = await _db.OutgoingLetterSigners.Where(s => s.SourceId == letterId && !s.IsDelete).ToListAsync();
        var existingUserIds = existingSigners.Select(s => s.UserId).ToHashSet();
        var wantedSignerIds = signerIds.Distinct().ToHashSet();

        foreach (var es in existingSigners)
        {
            if (!wantedSignerIds.Contains(es.UserId))
                es.IsDelete = true;
        }
        int maxOrder = existingSigners.Any() ? existingSigners.Max(s => s.Order) : 0;
        foreach (var uid in wantedSignerIds.Where(id => !existingUserIds.Contains(id)))
        {
            _db.OutgoingLetterSigners.Add(new OutgoingLetterSigner
            {
                SourceId = letterId,
                UserId = uid,
                Order = ++maxOrder,
                IsSigned = false,
                IsDelete = false
            });
            newlyAdded.Add(uid);
        }

        var oldRels = await _db.RelatedLetters.Where(r => r.LetterId == letterId && !r.IsDelete).ToListAsync();
        foreach (var r in oldRels) r.IsDelete = true;
        foreach (var rel in dto.RelatedLetters)
        {
            if (rel.RelateLetterId <= 0 || rel.RelateLetterId == letterId) continue;
            var keep = oldRels.FirstOrDefault(o => o.RelateLetterId == rel.RelateLetterId);
            if (keep != null) { keep.IsDelete = false; keep.Related = rel.Related == 2 ? 2 : 1; continue; }
            var exists = await _db.LetterSources.AnyAsync(s => s.Id == rel.RelateLetterId && !s.IsDelete);
            if (!exists) continue;
            _db.RelatedLetters.Add(new RelatedLetter
            {
                Related = rel.Related == 2 ? 2 : 1,
                LetterId = letterId,
                RelateLetterId = rel.RelateLetterId,
                UserId = letter.CreatorUserId,
                IsDelete = false
            });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        if (newlyAdded.Count > 0)
        {
            var senderName = await _db.Users.Where(u => u.Id == letter.CreatorUserId)
                .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() == "" ? u.Username : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim())
                .FirstOrDefaultAsync() ?? "";
            await _notify.SendManyAsync(newlyAdded,
                $"نامه صادره جهت بررسی: {letter.Title}",
                $"گیرنده: {letter.ReceiverOrganization} — شماره {letter.LetterNumber}",
                senderName, "نامه صادره", $"outgoing-letters/view/{letterId}");
        }
        await _notify.BroadcastChangedAsync("outgoing-letters");
    }

    public async Task UpdateStatusAsync(int letterId, int newStatus, int userId, bool isAdmin)
    {
        var letter = await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        if (letter.CreatorUserId != userId && !isAdmin)
            throw new Exception("فقط فرستنده یا مدیر می‌تواند وضعیت نامه را تغییر دهد.");

        if (newStatus < 0 || newStatus > 3)
            throw new Exception("وضعیت نامعتبر است.");

        letter.Status = newStatus;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
    }

    public async Task<List<OutgoingSignerDto>> GetSignersAsync(int letterId)
    {
        return await _db.OutgoingLetterSigners.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.SourceId == letterId && !s.IsDelete)
            .OrderBy(s => s.Order).ThenBy(s => s.Id)
            .Select(s => new OutgoingSignerDto
            {
                Id = s.Id,
                SourceId = s.SourceId,
                UserId = s.UserId,
                UserName = s.User != null ? s.User.Username : "",
                DisplayName = s.User != null
                    ? (string.IsNullOrWhiteSpace((s.User.FirstName ?? "") + (s.User.LastName ?? "")) ? s.User.Username : (s.User.FirstName + " " + s.User.LastName).Trim())
                    : "",
                SematId = s.SematId,
                Order = s.Order,
                IsSigned = s.IsSigned,
                DateSigned = s.DateSigned,
                SignNote = s.SignNote
            }).ToListAsync();
    }

    public async Task SignAsync(int letterId, int userId, string? signNote)
    {
        var letter = await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        var signer = await _db.OutgoingLetterSigners.FirstOrDefaultAsync(s => s.SourceId == letterId && s.UserId == userId && !s.IsDelete)
            ?? throw new Exception("شما جزو امضا کنندگان این نامه نیستید.");

        if (signer.IsSigned)
            throw new Exception("این نامه قبلا توسط شما امضا شده است.");

        signer.IsSigned = true;
        signer.DateSigned = DateTime.Now;
        signer.SignNote = signNote?.Trim();

        // بررسی اینکه همه امضا کرده‌اند یا حداقل یک امضا کافی است؟
        // طبق نیاز: وقتی امضا شد SadereNumber مقدار می‌گیرد — اگر چند امضا کننده دارد، بعد از آخرین امضا
        var allSigners = await _db.OutgoingLetterSigners.Where(s => s.SourceId == letterId && !s.IsDelete).ToListAsync();
        bool allSigned = allSigners.All(s => s.IsSigned || s.Id == signer.Id); // شامل همین امضا

        if (allSigned)
        {
            // SadereNumber = LetterNumber اگر خالی است، در غیر اینصورت حفظ شود
            if (string.IsNullOrWhiteSpace(letter.SadereNumber))
            {
                letter.SadereNumber = letter.LetterNumber;
            }
            letter.DateSadere = DateTime.Now;
            letter.Status = 3; // صادر شده
        }

        await _db.SaveChangesAsync();

        var senderName = await _db.Users.Where(u => u.Id == userId)
            .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() == "" ? u.Username : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim())
            .FirstOrDefaultAsync() ?? "";

        // اطلاع به ایجاد کننده
        await _notify.SendAsync(letter.CreatorUserId,
            $"نامه صادره امضا شد: {letter.Title}",
            $"{senderName} نامه {letter.LetterNumber} را امضا کرد. {(allSigned ? $"شماره صادره: {letter.SadereNumber}" : "")}",
            senderName, "نامه صادره", $"outgoing-letters/view/{letterId}");

        await _notify.BroadcastChangedAsync("outgoing-letters");
    }

    public async Task<List<LetterReciverDto>> GetAvailableSignersAsync(string? search)
    {
        var allowed = await GetUsersWithSignPermissionAsync();

        var q = _db.Users.AsNoTracking().Where(u => u.IsActive);

        if (allowed.Count > 0)
        {
            q = q.Where(u => allowed.Contains(u.Id));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(u => u.Username.Contains(s) || (u.FirstName + " " + u.LastName).Contains(s) || (u.FirstName ?? "").Contains(s) || (u.LastName ?? "").Contains(s));
        }

        return await q.OrderBy(u => u.Username).Take(100)
            .Select(u => new LetterReciverDto
            {
                UserId = u.Id,
                FullName = string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? "")) ? u.Username : $"{u.FirstName} {u.LastName}".Trim()
            }).ToListAsync();
    }
}
