using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

// ============================================================
//  سرویس ارجاع (گردش نامه) — منطق بر اساس ErjaService کارفرما:
//  • ارجاع نامه به کاربر(ان) دیگر با دستور/پاراف + عملگر + مهلت پاسخ
//  • پاسخ به ارجاع (+ تایید/رد برای عملگرهای امضادار)
//  • درخت گردش کامل نامه (GetGardeshErjaTreeView)
//  • نشان‌کردن (ستاره) و ثبت زمان خواندن
// ============================================================

public interface IErjaService
{
    Task AddErjaAsync(AddErjaDto dto, int senderUserId, string senderName);
    Task AnswerAsync(int erjaId, AnswerErjaDto dto, int userId, string userName);
    Task<List<ErjaTreeNodeDto>> GetGardeshTreeAsync(int sourceId, int userId, bool isAdmin);
    Task MarkReadAsync(int erjaId, int userId);
    Task<bool> ToggleNeshanAsync(int erjaId, int userId);
    Task<bool> ToggleBayeganiAsync(int erjaId, int userId);
    Task<List<AmalgarDto>> GetAmalgarsAsync();
}

public class ErjaService : IErjaService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly ILetterGroupService _groups;

    public ErjaService(AppDbContext db, INotifyService notify, ILetterGroupService groups)
    {
        _db = db;
        _notify = notify;
        _groups = groups;
    }

    public async Task AddErjaAsync(AddErjaDto dto, int senderUserId, string senderName)
    {
        // ---------- باز کردن گروه‌ها (منطق Reciver_GroupsErja/Hamesh کارفرما) ----------
        var allGroupIds = dto.GroupsGirandegan.Concat(dto.GroupsHamesh).Distinct().ToList();
        if (allGroupIds.Count > 0 && !await _groups.CheckGroupIdsAsync(allGroupIds))
            throw new Exception("برخی از گروه‌های انتخاب‌شده معتبر یا فعال نیستند.");

        var girandegan = dto.ReciversGirandegan
            .Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsGirandegan)).Distinct().ToList();
        var hameshIds = dto.ReciversHamesh
            .Concat(await _groups.ExpandToUserIdsAsync(dto.GroupsHamesh)).Distinct().ToList();

        // خود فرستنده اگر عضو گروه بود، حذف می‌شود
        girandegan.Remove(senderUserId);
        hameshIds.Remove(senderUserId);

        // فرم واحد ارجاع/هامش: بسته به انتخاب کاربر ممکن است فقط گیرنده ارجاع یا فقط گیرنده هامش داشته باشیم
        if (girandegan.Count == 0 && hameshIds.Count == 0)
            throw new Exception("حداقل یک گیرنده باید انتخاب شود.");

        var letter = await _db.InnerLetters.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == dto.LetterId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        // فرستنده باید خودش گیرنده نامه یا فرستنده اصلی باشد
        bool allowed = letter.CreatorUserId == senderUserId ||
                       await _db.Erjas.AnyAsync(e => e.SourceId == dto.LetterId && e.ReciverUserId == senderUserId && !e.IsDelete);
        if (!allowed)
            throw new Exception("شما در گردش این نامه نیستید و امکان ارجاع ندارید.");

        var amalgarOk = await _db.Amalgars.AnyAsync(a => a.AmalgarId == dto.AmalgarId && !a.IsDelete);
        if (!amalgarOk) throw new Exception("عملگر ارجاع نامعتبر است.");

        var allIds = girandegan.Concat(hameshIds).Distinct().ToList();
        if (allIds.Contains(senderUserId))
            throw new Exception("ارجاع نامه به خودتان امکان‌پذیر نیست.");

        var validUsers = await _db.Users
            .Where(u => allIds.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id).ToListAsync();
        if (allIds.Except(validUsers).Any())
            throw new Exception("برخی از گیرندگان معتبر یا فعال نیستند.");

        var now = DateTime.Now;
        var seen = new HashSet<int>();
        foreach (var uid in girandegan.Where(seen.Add))
        {
            _db.Erjas.Add(new Erja
            {
                SourceId = dto.LetterId,
                SenderUserId = senderUserId,
                ReciverUserId = uid,
                Date = now,
                Type = "ارجاع",
                MatnErja = dto.TextErja?.Trim() ?? "",
                AmalgarId = dto.AmalgarId,
                MohlatPasokh = dto.DeadlineAnswer,
                ParentErjaId = dto.ParentErjaId,
                Answer = "",
                ShowMassage = true
            });
        }
        foreach (var uid in hameshIds.Where(seen.Add))
        {
            _db.Erjas.Add(new Erja
            {
                SourceId = dto.LetterId,
                SenderUserId = senderUserId,
                ReciverUserId = uid,
                Date = now,
                Type = "هامش",
                MatnErja = dto.TextErja?.Trim() ?? "",
                AmalgarId = dto.AmalgarId,
                ParentErjaId = dto.ParentErjaId,
                Answer = "",
                ShowMassage = true
            });
        }
        await _db.SaveChangesAsync();

        var link = $"letters/view/{dto.LetterId}";
        await _notify.SendManyAsync(seen,
            $"ارجاع نامه: {letter.Title}",
            string.IsNullOrWhiteSpace(dto.TextErja) ? $"شماره {letter.LetterNumber}" : dto.TextErja,
            senderName, "ارجاع نامه داخلی", link);
        await _notify.BroadcastChangedAsync("letters");
    }

    public async Task AnswerAsync(int erjaId, AnswerErjaDto dto, int userId, string userName)
    {
        var erja = await _db.Erjas
            .Include(e => e.Source).ThenInclude(s => s.InnerLetter)
            .FirstOrDefaultAsync(e => e.ErjaId == erjaId && !e.IsDelete)
            ?? throw new Exception("ارجاع پیدا نشد.");

        if (erja.ReciverUserId != userId)
            throw new Exception("فقط گیرنده‌ی ارجاع می‌تواند پاسخ ثبت کند.");

        var now = DateTime.Now;
        erja.Answer = dto.Answer?.Trim() ?? "";
        erja.ShowForAll = dto.ShowForAll;
        erja.TypeTaeed = dto.TypeTaeed;
        erja.DateAnswer = now;
        if (dto.TypeTaeed != 0) erja.DateEmza = now;
        erja.IsReadAnswer = false;
        erja.ShowMassageAnswer = true;
        if (!erja.IsRead) { erja.IsRead = true; erja.DateRead = now; }
        await _db.SaveChangesAsync();

        var title = erja.Source.InnerLetter?.Title ?? "";
        var taeedText = dto.TypeTaeed switch { 1 => " (تایید شد ✅)", 2 => " (رد شد ❌)", _ => "" };
        await _notify.SendAsync(erja.SenderUserId,
            $"پاسخ به ارجاع نامه: {title}{taeedText}",
            erja.Answer,
            userName, "پاسخ ارجاع", $"letters/view/{erja.SourceId}");
        await _notify.BroadcastChangedAsync("letters");
    }

    public async Task<List<ErjaTreeNodeDto>> GetGardeshTreeAsync(int sourceId, int userId, bool isAdmin)
    {
        var letter = await _db.InnerLetters.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == sourceId && !l.IsDelete)
            ?? throw new Exception("نامه پیدا نشد.");

        bool inFlow = letter.CreatorUserId == userId ||
                      await _db.Erjas.AnyAsync(e => e.SourceId == sourceId && e.ReciverUserId == userId && !e.IsDelete);
        if (!inFlow && !isAdmin)
            throw new Exception("شما در گردش این نامه نیستید.");

        var erjas = await _db.Erjas.AsNoTracking()
            .Include(e => e.UserSender)
            .Include(e => e.UserReciver)
            .Include(e => e.Amalgar)
            .Where(e => e.SourceId == sourceId && !e.IsDelete)
            .OrderBy(e => e.ErjaId)
            .ToListAsync();

        static string FullName(Inventory.Api.Data.User? u) =>
            u == null ? "" :
            string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? ""))
                ? u.Username
                : $"{u.FirstName} {u.LastName}".Trim();

        // پاسخ‌های خصوصی فقط برای فرستنده/گیرنده همان ارجاع یا ادمین نمایش داده می‌شوند
        ErjaTreeNodeDto ToNode(Erja e) => new()
        {
            ErjaId = e.ErjaId,
            SenderUserId = e.SenderUserId,
            ReciverUserId = e.ReciverUserId,
            Sender = FullName(e.UserSender),
            Reciver = FullName(e.UserReciver),
            Type = e.Type,
            MatnErja = e.MatnErja,
            AmalgarTitle = e.Amalgar?.Title,
            TypeTaeed = e.TypeTaeed,
            Answer = (e.ShowForAll || e.SenderUserId == userId || e.ReciverUserId == userId || isAdmin) ? e.Answer : "",
            IsRead = e.IsRead,
            Date = e.Date,
            DateRead = e.DateRead,
            DateAnswer = e.DateAnswer,
            MohlatPasokh = e.MohlatPasokh,
            ShowForAll = e.ShowForAll
        };

        var map = erjas.ToDictionary(e => e.ErjaId, ToNode);
        var roots = new List<ErjaTreeNodeDto>();
        foreach (var e in erjas)
        {
            if (e.ParentErjaId is int pid && map.TryGetValue(pid, out var parent))
                parent.Children.Add(map[e.ErjaId]);
            else
                roots.Add(map[e.ErjaId]);
        }
        return roots;
    }

    public async Task MarkReadAsync(int erjaId, int userId)
    {
        var erja = await _db.Erjas.FirstOrDefaultAsync(e => e.ErjaId == erjaId && !e.IsDelete)
            ?? throw new Exception("ارجاع پیدا نشد.");
        if (erja.ReciverUserId != userId) return;
        if (erja.IsRead) return;

        erja.IsRead = true;
        erja.DateRead = DateTime.Now;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
    }

    public async Task<bool> ToggleNeshanAsync(int erjaId, int userId)
    {
        var erja = await _db.Erjas.FirstOrDefaultAsync(e => e.ErjaId == erjaId && !e.IsDelete)
            ?? throw new Exception("ارجاع پیدا نشد.");
        if (erja.ReciverUserId != userId)
            throw new Exception("فقط گیرنده می‌تواند نامه را نشان کند.");

        erja.IsNeshan = !erja.IsNeshan;
        await _db.SaveChangesAsync();
        return erja.IsNeshan;
    }

    /// <summary>بایگانی/خروج از بایگانی نامه دریافتی (روی ارجاع کاربر جاری)</summary>
    public async Task<bool> ToggleBayeganiAsync(int erjaId, int userId)
    {
        var erja = await _db.Erjas.FirstOrDefaultAsync(e => e.ErjaId == erjaId && !e.IsDelete)
            ?? throw new Exception("ارجاع پیدا نشد.");
        if (erja.ReciverUserId != userId)
            throw new Exception("فقط گیرنده می‌تواند نامه را بایگانی کند.");

        erja.IsBayegani = erja.IsBayegani != true;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
        return erja.IsBayegani == true;
    }

    public Task<List<AmalgarDto>> GetAmalgarsAsync() =>
        _db.Amalgars.AsNoTracking()
            .Where(a => !a.IsDelete)
            .OrderBy(a => a.AmalgarId)
            .Select(a => new AmalgarDto { AmalgarId = a.AmalgarId, Title = a.Title, TaeedEmza = a.TaeedEmza })
            .ToListAsync();
}
