using System.Globalization;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

// ============================================================
//  سرویس نامه داخلی — منطق بر اساس InerletterService کارفرما:
//  • شماره‌گذاری اندیکاتور بر اساس سال شمسی (هر سال از ۱ شروع می‌شود)
//  • سه نوع گیرنده: گیرنده (اصلی) / ارجاع (جهت اقدام) / هامش (جهت اطلاع)
//  • هر گیرنده یک رکورد Erja دریافت می‌کند (گردش نامه)
//  • عطف/پیرو با RelatedLetter
//  • نوتیفیکیشن لحظه‌ای با INotifyService (SignalR + دیتابیس)
// ============================================================

public interface IInnerLetterService
{
    Task<int> AddInnerLetterAsync(AddInnerLetterDto dto, int creatorUserId, string creatorName);
    Task<List<InnerLetterListItemDto>> GetInboxAsync(int userId, string? search, bool? unreadOnly);
    Task<List<InnerLetterListItemDto>> GetArchiveAsync(int userId, string? search);
    Task<List<InnerLetterListItemDto>> GetSentAsync(int userId, string? search);
    Task<InnerLetterDetailDto?> GetDetailAsync(int letterId, int userId, bool isAdmin);
    Task<LetterCartableStatsDto> GetStatsAsync(int userId);
    Task<List<LetterPickDto>> PickListAsync(int userId, string? search);
    Task DeleteAsync(int letterId, int userId, bool isAdmin);
    Task EditAsync(int letterId, EditInnerLetterDto dto, int userId, bool isAdmin);
}

public class InnerLetterService : IInnerLetterService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly ILetterGroupService _groups;

    public InnerLetterService(AppDbContext db, INotifyService notify, ILetterGroupService groups)
    {
        _db = db;
        _notify = notify;
        _groups = groups;
    }

    // ---------- شماره‌گذاری بر اساس سال شمسی — نسخه اصلاح‌شده ----------
    // باگ قبلی: فقط آخرین نامه (بر اساس Id) بررسی می‌شد و فیلتر IsDelete نداشت.
    // نسخه جدید: بیشترین شماره در سال شمسی جاری بین نامه‌های غیرحذفی محاسبه می‌شود.
    private async Task<int> NextNumberAsync()
    {
        var pc = new PersianCalendar();
        int currentYear = pc.GetYear(DateTime.Now);

        // تقریب شروع سال شمسی به میلادی — برای فیلتر اولیه در SQL
        DateTime startOfYear;
        try { startOfYear = new DateTime(currentYear, 1, 1, pc); }
        catch { startOfYear = DateTime.Now.AddDays(-400); } // fallback

        var candidates = await _db.InnerLetters
            .Where(l => !l.IsDelete && !l.Source.IsDelete && l.DateSabt >= startOfYear)
            .Select(l => new { l.Number, l.DateSabt })
            .ToListAsync();

        var maxInYear = candidates
            .Where(x => pc.GetYear(x.DateSabt) == currentYear)
            .Select(x => (int?)x.Number)
            .Max() ?? 0;

        // اگر هیچ نامه‌ای در سال جاری نبود، از ۱ شروع می‌شود
        return maxInYear + 1;
    }

    /// <summary>ساخت شماره اندیکاتور: «سال شمسی/شماره» — مثل 1404/12</summary>
    private static string BuildLetterNumber(int number)
    {
        var pc = new PersianCalendar();
        return $"{pc.GetYear(DateTime.Now)}/{number}";
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

    public async Task<int> AddInnerLetterAsync(AddInnerLetterDto dto, int creatorUserId, string creatorName)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new Exception("عنوان نامه الزامی است.");

        // ---------- باز کردن گروه‌ها به کاربران (منطق Reciver_Groups کارفرما) ----------
        var allGroupIds = dto.GroupsGirande.Concat(dto.GroupsErja).Concat(dto.GroupsHamesh).Distinct().ToList();
        if (allGroupIds.Count > 0 && !await _groups.CheckGroupIdsAsync(allGroupIds))
            throw new Exception("برخی از گروه‌های انتخاب‌شده معتبر یا فعال نیستند.");

        var girande = dto.ReciversGirande.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsGirande)).Distinct().ToList();
        var erjaIds = dto.ReciversErja.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsErja)).Distinct().ToList();
        var hamesh  = dto.ReciversHamesh.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsHamesh)).Distinct().ToList();

        // خود فرستنده از فهرست گیرندگان حذف می‌شود (ممکن است عضو گروه باشد)
        girande.Remove(creatorUserId);
        erjaIds.Remove(creatorUserId);
        hamesh.Remove(creatorUserId);

        if (girande.Count == 0)
            throw new Exception("حداقل یک گیرنده باید انتخاب شود.");

        // گیرنده نباید خود فرستنده باشد و باید کاربر فعال باشد
        var allReciverIds = girande
            .Concat(erjaIds)
            .Concat(hamesh)
            .Distinct()
            .ToList();

        var validUsers = await _db.Users
            .Where(u => allReciverIds.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync();
        var invalid = allReciverIds.Except(validUsers).ToList();
        if (invalid.Count > 0)
            throw new Exception("برخی از گیرندگان انتخاب‌شده معتبر یا فعال نیستند.");

        var now = DateTime.Now;

        using var tx = await _db.Database.BeginTransactionAsync();

        // ---------- کلید مرجع (SourceKeyID) ----------
        var source = new LetterSource { SourceType = 1, IsDelete = false };
        _db.LetterSources.Add(source);
        await _db.SaveChangesAsync();

        // ---------- نامه ----------
        var number = await NextNumberAsync();
        var letter = new InnerLetter
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
            IsDelete = false
        };
        _db.InnerLetters.Add(letter);

        // ---------- ارجاع‌ها (یک رکورد به ازای هر گیرنده — منطق CreateErja کارفرما) ----------
        var seen = new HashSet<int>();
        foreach (var uid in girande.Where(seen.Add))
            _db.Erjas.Add(NewErja(source.Id, creatorUserId, uid, now, "گیرنده"));
        foreach (var uid in erjaIds.Where(seen.Add))
            _db.Erjas.Add(NewErja(source.Id, creatorUserId, uid, now, "ارجاع"));
        foreach (var uid in hamesh.Where(seen.Add))
            _db.Erjas.Add(NewErja(source.Id, creatorUserId, uid, now, "هامش"));

        // ---------- عطف / پیرو ----------
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

        // ---------- حذف پیش‌نویس مبدأ + انتقال پیوست‌های آن به نامه ----------
        if (dto.FromPishnevisId is > 0)
        {
            var pish = await _db.PishnevisLetters
                .FirstOrDefaultAsync(p => p.PishnevisId == dto.FromPishnevisId && p.UserId == creatorUserId && !p.IsDelete);
            if (pish != null)
            {
                pish.IsDelete = true;

                // پیوست‌های پیش‌نویس → پیوست نامه ارسال‌شده
                var pishAtts = await _db.AppAttachments
                    .Where(a => a.Module == "Pishnevis" && a.RefId == pish.PishnevisId)
                    .ToListAsync();
                foreach (var a in pishAtts)
                {
                    a.Module = "InnerLetters";
                    a.RefId = source.Id;
                }
                await _db.SaveChangesAsync();
            }
        }

        await tx.CommitAsync();

        // ---------- نوتیفیکیشن (خارج از تراکنش) ----------
        var link = $"letters/view/{source.Id}";
        await _notify.SendManyAsync(seen,
            $"نامه داخلی جدید: {letter.Title}",
            $"شماره {letter.LetterNumber} — فوریت: {letter.Foriat}",
            creatorName, "نامه داخلی", link);
        await _notify.BroadcastChangedAsync("letters");

        return source.Id;
    }

    public async Task<List<InnerLetterListItemDto>> GetInboxAsync(int userId, string? search, bool? unreadOnly)
    {
        var q = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.InnerLetter != null && !e.Source.InnerLetter.IsDelete
                        && e.IsBayegani != true); // نامه‌های بایگانی‌شده در پوشه بایگانی نمایش داده می‌شوند

        if (unreadOnly == true) q = q.Where(e => !e.IsRead);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => e.Source.InnerLetter!.Title.Contains(s)
                             || (e.Source.InnerLetter!.LetterNumber ?? "").Contains(s)
                             || (e.UserSender!.FirstName + " " + e.UserSender.LastName).Contains(s)
                             || e.UserSender!.Username.Contains(s));
        }

        return await q
            .OrderByDescending(e => e.ErjaId)
            .Select(e => new InnerLetterListItemDto
            {
                LetterId = e.SourceId,
                ErjaId = e.ErjaId,
                LetterNumber = e.Source.InnerLetter!.LetterNumber ?? "",
                Title = e.Source.InnerLetter!.Title,
                Sender = string.IsNullOrEmpty(e.UserSender!.FirstName + e.UserSender.LastName)
                    ? e.UserSender.Username
                    : (e.UserSender.FirstName + " " + e.UserSender.LastName).Trim(),
                SenderUserId = e.SenderUserId,
                Date = e.Date,
                Mahramanegi = e.Source.InnerLetter!.Mahramanegi,
                Foriat = e.Source.InnerLetter!.Foriat,
                ErjaType = e.Type,
                MatnErja = e.MatnErja,
                MohlatPasokh = e.MohlatPasokh,
                IsNeshan = e.IsNeshan,
                IsRead = e.IsRead,
                TypeTaeed = e.TypeTaeed,
                HasAnswer = e.Answer != "",
                ReciverCount = e.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "InnerLetters" && a.RefId == e.SourceId)
            })
            .ToListAsync();
    }

    /// <summary>پوشه بایگانی — ارجاع‌های کاربر که IsBayegani=true دارند</summary>
    public async Task<List<InnerLetterListItemDto>> GetArchiveAsync(int userId, string? search)
    {
        var q = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.InnerLetter != null && !e.Source.InnerLetter.IsDelete
                        && e.IsBayegani == true);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => e.Source.InnerLetter!.Title.Contains(s)
                             || (e.Source.InnerLetter!.LetterNumber ?? "").Contains(s)
                             || (e.UserSender!.FirstName + " " + e.UserSender.LastName).Contains(s));
        }

        return await q
            .OrderByDescending(e => e.ErjaId)
            .Select(e => new InnerLetterListItemDto
            {
                LetterId = e.SourceId,
                ErjaId = e.ErjaId,
                LetterNumber = e.Source.InnerLetter!.LetterNumber ?? "",
                Title = e.Source.InnerLetter!.Title,
                Sender = string.IsNullOrEmpty(e.UserSender!.FirstName + e.UserSender.LastName)
                    ? e.UserSender.Username
                    : (e.UserSender.FirstName + " " + e.UserSender.LastName).Trim(),
                SenderUserId = e.SenderUserId,
                Date = e.Date,
                Mahramanegi = e.Source.InnerLetter!.Mahramanegi,
                Foriat = e.Source.InnerLetter!.Foriat,
                ErjaType = e.Type,
                MatnErja = e.MatnErja,
                MohlatPasokh = e.MohlatPasokh,
                IsNeshan = e.IsNeshan,
                IsRead = e.IsRead,
                TypeTaeed = e.TypeTaeed,
                HasAnswer = e.Answer != "",
                ReciverCount = e.Source.Erjas.Count(x => !x.IsDelete),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "InnerLetters" && a.RefId == e.SourceId)
            })
            .ToListAsync();
    }

    public async Task<List<InnerLetterListItemDto>> GetSentAsync(int userId, string? search)
    {
        var q = _db.InnerLetters.AsNoTracking()
            .Where(l => l.CreatorUserId == userId && !l.IsDelete && !l.Source.IsDelete);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(l => l.Title.Contains(s) || (l.LetterNumber ?? "").Contains(s));
        }

        return await q
            .OrderByDescending(l => l.Id)
            .Select(l => new InnerLetterListItemDto
            {
                LetterId = l.Id,
                LetterNumber = l.LetterNumber ?? "",
                Title = l.Title,
                Sender = "",
                SenderUserId = l.CreatorUserId,
                // نام اولین گیرنده اصلی + تعداد بقیه
                Reciver = l.Source.Erjas
                    .Where(e => !e.IsDelete && e.Type == "گیرنده" && e.ParentErjaId == null)
                    .OrderBy(e => e.ErjaId)
                    .Select(e => string.IsNullOrEmpty(e.UserReciver!.FirstName + e.UserReciver.LastName)
                        ? e.UserReciver.Username
                        : (e.UserReciver.FirstName + " " + e.UserReciver.LastName).Trim())
                    .FirstOrDefault() ?? "",
                ReciverCount = l.Source.Erjas.Count(e => !e.IsDelete && e.ParentErjaId == null),
                Date = l.DateSabt,
                Mahramanegi = l.Mahramanegi,
                Foriat = l.Foriat,
                // خوانده‌شدن توسط همه گیرندگان اولیه
                IsRead = l.Source.Erjas.Where(e => !e.IsDelete && e.ParentErjaId == null).All(e => e.IsRead),
                // آیا پاسخی از طرف گیرندگان ثبت شده؟ (برای فیلتر پاسخ داده شده/بدون پاسخ)
                HasAnswer = l.Source.Erjas.Any(e => !e.IsDelete && e.Answer != ""),
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "InnerLetters" && a.RefId == l.Id)
            })
            .ToListAsync();
    }

    public async Task<InnerLetterDetailDto?> GetDetailAsync(int letterId, int userId, bool isAdmin)
    {
        var letter = await _db.InnerLetters.AsNoTracking()
            .Include(l => l.Creator)
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) return null;

        var erjas = await _db.Erjas.AsNoTracking()
            .Include(e => e.UserReciver)
            .Where(e => e.SourceId == letterId && !e.IsDelete)
            .OrderBy(e => e.ErjaId)
            .ToListAsync();

        // ---------- کنترل دسترسی: فرستنده، یکی از گیرندگان (در هر سطح گردش) یا ادمین ----------
        bool isMine = letter.CreatorUserId == userId;
        var myErja = erjas
            .Where(e => e.ReciverUserId == userId)
            .OrderByDescending(e => e.ErjaId)
            .FirstOrDefault();
        if (!isMine && myErja == null && !isAdmin) return null;

        static string FullName(Inventory.Api.Data.User? u) =>
            u == null ? "" :
            string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? ""))
                ? u.Username
                : $"{u.FirstName} {u.LastName}".Trim();

        var dto = new InnerLetterDetailDto
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
            // قابل ویرایش: فرستنده (یا ادمین) و هنوز هیچ گیرنده‌ای نامه را نخوانده باشد
            CanEdit = (isMine || isAdmin) && !erjas.Any(e => e.IsRead)
        };

        // فقط ارجاع‌های اولیه (نه گردش‌های بعدی) به‌عنوان گیرندگان نامه
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

        // عطف/پیرو
        dto.RelatedLetters = await _db.RelatedLetters.AsNoTracking()
            .Where(r => r.LetterId == letterId && !r.IsDelete)
            .Select(r => new RelatedLetterDto
            {
                Id = r.Id,
                Related = r.Related,
                RelateLetterId = r.RelateLetterId,
                RelateLetterNumber = r.RelateLetter.InnerLetter != null ? r.RelateLetter.InnerLetter.LetterNumber : "",
                RelateLetterTitle = r.RelateLetter.InnerLetter != null ? r.RelateLetter.InnerLetter.Title : ""
            })
            .ToListAsync();

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

        return dto;
    }

    public async Task<LetterCartableStatsDto> GetStatsAsync(int userId)
    {
        var inbox = _db.Erjas.AsNoTracking()
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && !e.Source.IsDelete
                        && e.Source.InnerLetter != null && !e.Source.InnerLetter.IsDelete);

        var soon = DateTime.Now.AddDays(2);
        return new LetterCartableStatsDto
        {
            InboxUnread = await inbox.CountAsync(e => !e.IsRead),
            InboxTotal = await inbox.CountAsync(),
            SentTotal = await _db.InnerLetters.CountAsync(l => l.CreatorUserId == userId && !l.IsDelete),
            PishnevisTotal = await _db.PishnevisLetters.CountAsync(p => p.UserId == userId && !p.IsDelete),
            DeadlineSoon = await inbox.CountAsync(e =>
                e.MohlatPasokh != null && e.Answer == "" && e.MohlatPasokh <= soon)
        };
    }

    public async Task<List<LetterPickDto>> PickListAsync(int userId, string? search)
    {
        // نامه‌هایی که کاربر فرستنده یا گیرنده‌ی آن‌ها بوده — برای انتخاب عطف/پیرو
        var q = _db.InnerLetters.AsNoTracking()
            .Where(l => !l.IsDelete &&
                        (l.CreatorUserId == userId ||
                         l.Source.Erjas.Any(e => e.ReciverUserId == userId && !e.IsDelete)));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(l => l.Title.Contains(s) || (l.LetterNumber ?? "").Contains(s));
        }

        return await q.OrderByDescending(l => l.Id)
            .Take(30)
            .Select(l => new LetterPickDto
            {
                LetterId = l.Id,
                LetterNumber = l.LetterNumber ?? "",
                Title = l.Title,
                Date = l.DateSabt,
                IsSent = l.CreatorUserId == userId
            })
            .ToListAsync();
    }

    public async Task DeleteAsync(int letterId, int userId, bool isAdmin)
    {
        var letter = await _db.InnerLetters
            .Include(l => l.Source)
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        if (letter.CreatorUserId != userId && !isAdmin)
            throw new Exception("فقط فرستنده یا مدیر می‌تواند نامه را حذف کند.");

        // حذف نرم — اگر گیرنده‌ای نامه را خوانده باشد حذف مجاز نیست (منطق کارفرما)
        var anyRead = await _db.Erjas.AnyAsync(e => e.SourceId == letterId && !e.IsDelete && e.IsRead);
        if (anyRead && !isAdmin)
            throw new Exception("این نامه توسط گیرنده(ها) خوانده شده و قابل حذف نیست.");

        letter.IsDelete = true;
        letter.Source.IsDelete = true;
        await _db.Erjas.Where(e => e.SourceId == letterId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsDelete, true));
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
    }

    /// <summary>
    /// ویرایش نامه — فقط فرستنده و فقط تا وقتی هیچ گیرنده‌ای آن را نخوانده است
    /// (همان قاعده حذف؛ مدیر می‌تواند بعد از خوانده‌شدن هم ویرایش کند)
    /// </summary>
    public async Task EditAsync(int letterId, EditInnerLetterDto dto, int userId, bool isAdmin)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new Exception("عنوان نامه الزامی است.");

        var letter = await _db.InnerLetters
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        if (letter.CreatorUserId != userId && !isAdmin)
            throw new Exception("فقط فرستنده یا مدیر می‌تواند نامه را ویرایش کند.");

        // قانون: به محض این‌که حتی یک گیرنده نامه را بخواند، ویرایش برای کاربران عادی بسته می‌شود
        // باگ قبلی: حتی ادمین هم نمی‌توانست ویرایش کند در حالی که کامنت کنترلر می‌گوید «مدیر: همیشه»
        // رفع شد: ادمین می‌تواند حتی بعد از خوانده‌شدن ویرایش کند (همانند حذف)
        var anyRead = await _db.Erjas.AnyAsync(e => e.SourceId == letterId && !e.IsDelete && e.IsRead);
        if (anyRead && !isAdmin)
            throw new Exception("این نامه توسط گیرنده(ها) خوانده شده و دیگر قابل ویرایش نیست.");

        // ---------- گیرندگان جدید (همان منطق فرم ایجاد: گروه‌ها باز و فرستنده حذف می‌شود) ----------
        var allGroupIds = dto.GroupsGirande.Concat(dto.GroupsErja).Concat(dto.GroupsHamesh).Distinct().ToList();
        if (allGroupIds.Count > 0 && !await _groups.CheckGroupIdsAsync(allGroupIds))
            throw new Exception("برخی از گروه‌های انتخاب‌شده معتبر یا فعال نیستند.");

        var girande = dto.ReciversGirande.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsGirande)).Distinct().ToList();
        var erjaIds = dto.ReciversErja.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsErja)).Distinct().ToList();
        var hamesh  = dto.ReciversHamesh.Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsHamesh)).Distinct().ToList();
        girande.Remove(letter.CreatorUserId);
        erjaIds.Remove(letter.CreatorUserId);
        hamesh.Remove(letter.CreatorUserId);

        if (girande.Count == 0)
            throw new Exception("حداقل یک گیرنده باید انتخاب شود.");

        var allReciverIds = girande.Concat(erjaIds).Concat(hamesh).Distinct().ToList();
        var validUsers = await _db.Users
            .Where(u => allReciverIds.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync();
        if (allReciverIds.Except(validUsers).Any())
            throw new Exception("برخی از گیرندگان انتخاب‌شده معتبر یا فعال نیستند.");

        using var tx = await _db.Database.BeginTransactionAsync();

        // ---------- فیلدهای نامه ----------
        letter.Title = dto.Title.Trim();
        letter.Text = dto.Text;
        letter.Mahramanegi = string.IsNullOrWhiteSpace(dto.Mahramanegi) ? "عادی" : dto.Mahramanegi;
        letter.Foriat = string.IsNullOrWhiteSpace(dto.Foriat) ? "عادی" : dto.Foriat;

        // ---------- همگام‌سازی ارجاع‌های سطح اول (گیرندگان اولیه) ----------
        // چون هیچ ارجاعی هنوز خوانده نشده (شرط بالا)، بازسازی امن است؛
        // موارد بدون تغییر حفظ می‌شوند تا شناسه/نشان/بایگانی کاربر از دست نرود.
        var now = DateTime.Now;
        var rootErjas = await _db.Erjas
            .Where(e => e.SourceId == letterId && !e.IsDelete && e.ParentErjaId == null)
            .ToListAsync();

        var wanted = new Dictionary<int, string>(); // userId → type (اولویت: گیرنده > ارجاع > هامش)
        foreach (var uid in girande) wanted.TryAdd(uid, "گیرنده");
        foreach (var uid in erjaIds) wanted.TryAdd(uid, "ارجاع");
        foreach (var uid in hamesh) wanted.TryAdd(uid, "هامش");

        var newlyAdded = new List<int>();
        // باگ قبلی: برای هر گیرنده حذف‌شده، تمام زیردرخت‌ها دوباره از دیتابیس لود می‌شد
        // رفع شد: یک بار همه زیردرخت‌ها لود می‌شوند
        var allSubErjas = await _db.Erjas.Where(x => x.SourceId == letterId && !x.IsDelete && x.ParentErjaId != null).ToListAsync();

        foreach (var e in rootErjas)
        {
            if (wanted.TryGetValue(e.ReciverUserId, out var type))
            {
                e.Type = type;          // نوع دریافت ممکن است عوض شده باشد
                wanted.Remove(e.ReciverUserId);
            }
            else
            {
                // گیرنده حذف‌شده — ارجاع او و گردش‌های زیرشاخه‌اش حذف نرم می‌شود
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

        // ---------- همگام‌سازی نامه‌های مرتبط (عطف/پیرو) ----------
        var oldRels = await _db.RelatedLetters
            .Where(r => r.LetterId == letterId && !r.IsDelete)
            .ToListAsync();
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

        // ---------- اطلاع‌رسانی به گیرندگان تازه‌اضافه‌شده ----------
        if (newlyAdded.Count > 0)
        {
            var senderName = await _db.Users.Where(u => u.Id == letter.CreatorUserId)
                .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() == "" ? u.Username : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim())
                .FirstOrDefaultAsync() ?? "";
            await _notify.SendManyAsync(newlyAdded,
                $"نامه داخلی جدید: {letter.Title}",
                $"شماره {letter.LetterNumber} — فوریت: {letter.Foriat}",
                senderName, "نامه داخلی", $"letters/view/{letterId}");
        }
        await _notify.BroadcastChangedAsync("letters");
    }
}
