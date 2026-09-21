using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// سرویس بایگانی درختی نامه‌ها — پورت از ArchiveServices طرح کارفرما
/// (تطبیق: UserId عددی از توکن به‌جای Guid در درخواست، SematId اختیاری تا فاز چارت سازمانی،
/// TypeBayegani=1 برای بایگانی شخصی)
/// </summary>
public interface IArchiveService
{
    /// <summary>درخت کامل بایگانی کاربر (پوشه‌ها + نامه‌های بایگانی‌شده)</summary>
    Task<List<BayeganiNodeDto>> GetTreeAsync(int userId);

    /// <summary>ایجاد دسته اصلی (ریشه — ParentId=0)</summary>
    Task<BayeganiNodeDto> AddMainCategoryAsync(int userId, SaveBayeganiFolderDto dto);

    /// <summary>ایجاد زیرپوشه داخل یک پوشه موجود</summary>
    Task<BayeganiNodeDto> AddSubCategoryAsync(int userId, SaveBayeganiFolderDto dto);

    /// <summary>ویرایش عنوان پوشه (جابجایی فقط با MoveFolder)</summary>
    Task<BayeganiNodeDto> EditFolderAsync(int bayeganiId, int userId, SaveBayeganiFolderDto dto);

    /// <summary>جابجایی پوشه با جلوگیری از حلقه (Circular Reference)</summary>
    Task<BayeganiNodeDto> MoveFolderAsync(int bayeganiId, int newParentId, int userId);

    /// <summary>بایگانی یک یا چند نامه (ارجاع گیرنده یا نامه ارسالی فرستنده) داخل پوشه انتخابی</summary>
    Task AddLettersToArchiveAsync(int userId, ArchiveLettersDto dto);

    /// <summary>خروج نامه ارسالی از بایگانی بر اساس شناسه نامه (مسیر فرستنده)</summary>
    Task UnarchiveByLetterAsync(int letterId, int userId);

    /// <summary>جابجایی نامه بایگانی‌شده به پوشه دیگر</summary>
    Task<BayeganiNodeDto> MoveLetterAsync(int bayeganiId, int newParentId, int userId);

    /// <summary>حذف: پوشه خالی حذف می‌شود؛ نامه از بایگانی خارج می‌شود (Soft Delete)</summary>
    Task DeleteAsync(int bayeganiId, int userId);

    /// <summary>خروج نامه از بایگانی بر اساس شناسه ارجاع (برای دکمه بایگانی در نمایش نامه)</summary>
    Task UnarchiveByErjaAsync(int erjaId, int userId);

    // ==================== بایگانی دبیرخانه (نامه صادره — TypeBayegani=2) ====================
    // بایگانی دبیرخانه درخت پوشه‌های مستقل خودش را دارد تا با بایگانی شخصی
    // نامه‌های داخلی قاطی نشود.

    /// <summary>درخت بایگانی دبیرخانه (پوشه‌ها + نامه‌های صادرهٔ بایگانی‌شده)</summary>
    Task<List<BayeganiNodeDto>> GetOutgoingTreeAsync(int userId);

    /// <summary>ایجاد دسته اصلی در ریشهٔ بایگانی دبیرخانه</summary>
    Task<BayeganiNodeDto> AddOutgoingMainCategoryAsync(int userId, SaveBayeganiFolderDto dto);

    /// <summary>ایجاد زیرپوشه در بایگانی دبیرخانه</summary>
    Task<BayeganiNodeDto> AddOutgoingSubCategoryAsync(int userId, SaveBayeganiFolderDto dto);

    /// <summary>ویرایش عنوان پوشهٔ بایگانی دبیرخانه</summary>
    Task<BayeganiNodeDto> EditOutgoingFolderAsync(int bayeganiId, int userId, SaveBayeganiFolderDto dto);

    /// <summary>جابجایی پوشه در بایگانی دبیرخانه</summary>
    Task<BayeganiNodeDto> MoveOutgoingFolderAsync(int bayeganiId, int newParentId, int userId);

    /// <summary>حذف پوشه/نامه از بایگانی دبیرخانه</summary>
    Task DeleteOutgoingAsync(int bayeganiId, int userId);

    /// <summary>بایگانی یک یا چند نامه صادره در پوشهٔ انتخابی</summary>
    Task ArchiveOutgoingLettersAsync(int userId, ArchiveOutgoingLettersDto dto);

    /// <summary>خروج نامه صادره از بایگانی دبیرخانه</summary>
    Task UnarchiveOutgoingLetterAsync(int letterId, int userId);

    /// <summary>جابجایی نامه صادرهٔ بایگانی‌شده به پوشه‌ای دیگر</summary>
    Task<BayeganiNodeDto> MoveArchivedLetterAsync(MoveArchivedLetterDto dto, int userId);
}

public class ArchiveService : IArchiveService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;

    public ArchiveService(AppDbContext db, INotifyService notify)
    {
        _db = db;
        _notify = notify;
    }

    // ---------- اعتبارسنجی مشترک (معادل Validators طرح اصلی) ----------
    private static string ValidateTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new Exception("عنوان را وارد کنید.");
        var t = title.Trim();
        if (t.Length > 200)
            throw new Exception("عنوان نمی‌تواند بیشتر از ۲۰۰ کاراکتر باشد.");
        return t;
    }

    private async Task<LetterBayegani> GetOwnedAsync(int bayeganiId, int userId)
    {
        var entity = await _db.LetterBayeganis
            .SingleOrDefaultAsync(x => x.BayeganiId == bayeganiId && !x.IsDelete);

        if (entity == null) throw new Exception("مورد بایگانی یافت نشد.");
        if (entity.UserId != userId) throw new Exception("دسترسی به بایگانی کاربر دیگر مجاز نیست.");
        return entity;
    }

    /// <summary>نوع بایگانی شخصی نامه‌های داخلی</summary>
    private const int TypeInner = 1;

    /// <summary>نوع بایگانی دبیرخانه (نامه صادره)</summary>
    private const int TypeOutgoing = 2;

    /// <summary>بررسی وجود پوشه — type جداکنندهٔ بایگانی شخصی از بایگانی دبیرخانه است</summary>
    private async Task<bool> FolderExistsAsync(int folderId, int userId, int type = TypeInner) =>
        await _db.LetterBayeganis.AsNoTracking()
            .AnyAsync(x => x.BayeganiId == folderId && x.IsFolder && !x.IsDelete
                        && x.UserId == userId && x.TypeBayegani == type);

    /// <summary>جلوگیری از قرارگرفتن پوشه داخل زیرپوشه‌های خودش</summary>
    private async Task<bool> IsDescendantAsync(int folderId, int targetParentId, int type = TypeInner)
    {
        var currentParentId = targetParentId;
        var guard = 0;

        while (currentParentId > 0 && guard++ < 100)
        {
            if (currentParentId == folderId) return true;

            currentParentId = await _db.LetterBayeganis
                .Where(x => x.BayeganiId == currentParentId && x.IsFolder && !x.IsDelete && x.TypeBayegani == type)
                .Select(x => x.ParentId)
                .FirstOrDefaultAsync();
        }

        return false;
    }

    private static BayeganiNodeDto Map(LetterBayegani e) => new()
    {
        BayeganiId = e.BayeganiId,
        Title = e.Title,
        ParentId = e.ParentId,
        TypeBayegani = e.TypeBayegani,
        IsFolder = e.IsFolder,
        ErjaId = e.ErjaId,
        LetterId = e.LetterId,
        SourceType = e.TypeBayegani == TypeOutgoing ? 2 : 1
    };

    // ==================== درخت ====================

    public async Task<List<BayeganiNodeDto>> GetTreeAsync(int userId)
    {
        // مهاجرت تنبل: نامه‌هایی که با toggle قدیمی بایگانی شده‌اند ولی رکورد درختی ندارند
        var loose = await _db.Erjas
            .Where(e => e.ReciverUserId == userId && !e.IsDelete && e.IsBayegani == true
                        && e.Source.InnerLetter != null && !e.Source.InnerLetter.IsDelete
                        && !_db.LetterBayeganis.Any(b => b.ErjaId == e.ErjaId && !b.IsDelete))
            .Select(e => new { e.ErjaId, e.Source.InnerLetter!.Title })
            .ToListAsync();

        if (loose.Count > 0)
        {
            foreach (var l in loose)
                _db.LetterBayeganis.Add(new LetterBayegani
                {
                    Title = l.Title.Length > 200 ? l.Title[..200] : l.Title,
                    ErjaId = l.ErjaId,
                    ParentId = 0,
                    UserId = userId,
                    TypeBayegani = 1,
                    IsFolder = false,
                    IsDelete = false
                });
            await _db.SaveChangesAsync();
        }

        // فقط بایگانی شخصی (نامه‌های داخلی) — نامه‌های صادرهٔ بایگانی‌شده در
        // درخت جداگانهٔ دبیرخانه (TypeBayegani=2) نمایش داده می‌شوند.
        var rows = await _db.LetterBayeganis.AsNoTracking()
            .Where(b => b.UserId == userId && !b.IsDelete && b.TypeBayegani != TypeOutgoing)
            .OrderBy(b => b.BayeganiId)
            .ToListAsync();

        // اطلاعات نامه برای برگ‌ها
        var erjaIds = rows.Where(r => r.ErjaId.HasValue).Select(r => r.ErjaId!.Value).ToList();
        var letterInfo = await _db.Erjas.AsNoTracking()
            .Where(e => erjaIds.Contains(e.ErjaId) && e.Source.InnerLetter != null)
            .Select(e => new
            {
                e.ErjaId,
                LetterId = e.SourceId,
                LetterNumber = e.Source.InnerLetter!.LetterNumber ?? "",
                LetterTitle = e.Source.InnerLetter!.Title,
                Sender = string.IsNullOrEmpty(e.UserSender!.FirstName + e.UserSender.LastName)
                    ? e.UserSender.Username
                    : (e.UserSender.FirstName + " " + e.UserSender.LastName).Trim(),
                e.Date,
                e.Source.InnerLetter!.Foriat,
                e.Source.InnerLetter!.Mahramanegi,
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "InnerLetters" && a.RefId == e.SourceId)
            })
            .ToListAsync();
        var infoByErja = letterInfo.ToDictionary(x => x.ErjaId);
        var incomingInfo = await _db.Erjas.AsNoTracking()
            .Where(e => erjaIds.Contains(e.ErjaId) && e.Source.IncomingLetter != null)
            .Select(e => new
            {
                e.ErjaId,
                LetterId = e.SourceId,
                LetterNumber = e.Source.IncomingLetter!.LetterNumber ?? "",
                LetterTitle = e.Source.IncomingLetter!.Title,
                Sender = e.Source.IncomingLetter!.Ferestande,
                e.Source.IncomingLetter!.Date,
                e.Source.IncomingLetter!.Foriat,
                e.Source.IncomingLetter!.Mahramanegi,
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "IncomingLetters" && a.RefId == e.SourceId)
            }).ToListAsync();
        var infoByIncomingErja = incomingInfo.ToDictionary(x => x.ErjaId);

        // اطلاعات نامه‌های ارسالی بایگانی‌شده (مسیر فرستنده)
        var sentIds = rows.Where(r => r.LetterId.HasValue).Select(r => r.LetterId!.Value).ToList();
        var sentInfo = await _db.InnerLetters.AsNoTracking()
            .Where(l => sentIds.Contains(l.Id))
            .Select(l => new
            {
                l.Id,
                LetterNumber = l.LetterNumber ?? "",
                LetterTitle = l.Title,
                Recivers = l.Source.Erjas.Where(e => !e.IsDelete).Select(e =>
                    string.IsNullOrEmpty(e.UserReciver!.FirstName + e.UserReciver.LastName)
                        ? e.UserReciver.Username
                        : (e.UserReciver.FirstName + " " + e.UserReciver.LastName).Trim()).ToList(),
                l.DateSabt,
                l.Foriat,
                l.Mahramanegi,
                HasAttachment = _db.AppAttachments.Any(a => a.Module == "InnerLetters" && a.RefId == l.Id)
            })
            .ToListAsync();
        var infoByLetter = sentInfo.ToDictionary(x => x.Id);

        var nodes = new Dictionary<int, BayeganiNodeDto>();
        foreach (var r in rows)
        {
            var n = Map(r);
            if (r.ErjaId is { } eid && infoByErja.TryGetValue(eid, out var li))
            {
                n.LetterId = li.LetterId;
                n.LetterNumber = li.LetterNumber;
                n.Title = li.LetterTitle; // عنوان زنده نامه
                n.Sender = li.Sender;
                n.Date = li.Date;
                n.Foriat = li.Foriat;
                n.Mahramanegi = li.Mahramanegi;
                n.HasAttachment = li.HasAttachment;
            }
            else if (r.ErjaId is { } incomingErjaId && infoByIncomingErja.TryGetValue(incomingErjaId, out var ii))
            {
                n.LetterId = ii.LetterId;
                n.LetterNumber = ii.LetterNumber;
                n.Title = ii.LetterTitle;
                n.Sender = ii.Sender;
                n.Date = ii.Date;
                n.Foriat = ii.Foriat.ToString();
                n.Mahramanegi = ii.Mahramanegi.ToString();
                n.HasAttachment = ii.HasAttachment;
            }
            else if (r.LetterId is { } lid && infoByLetter.TryGetValue(lid, out var si))
            {
                n.LetterId = lid;
                n.LetterNumber = si.LetterNumber;
                n.Title = si.LetterTitle;
                n.Sender = "به: " + string.Join("، ", si.Recivers.Take(2)) + (si.Recivers.Count > 2 ? " و…" : "");
                n.Date = si.DateSabt;
                n.Foriat = si.Foriat;
                n.Mahramanegi = si.Mahramanegi;
                n.HasAttachment = si.HasAttachment;
            }
            nodes[n.BayeganiId] = n;
        }

        var roots = new List<BayeganiNodeDto>();
        foreach (var n in nodes.Values)
        {
            if (n.ParentId != 0 && nodes.TryGetValue(n.ParentId, out var parent) && parent.IsFolder)
                parent.Children.Add(n);
            else
                roots.Add(n); // ریشه یا والدِ ازدست‌رفته
        }

        SortTree(roots);
        return roots;
    }

    private static void SortTree(List<BayeganiNodeDto> list)
    {
        list.Sort((a, b) =>
        {
            if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1; // پوشه‌ها اول
            if (a.IsFolder) return string.Compare(a.Title, b.Title, StringComparison.Ordinal);
            return Comparer<DateTime?>.Default.Compare(b.Date, a.Date); // نامه‌ها: جدیدترین اول
        });
        foreach (var n in list) SortTree(n.Children);
    }

    // ==================== پوشه‌ها ====================

    public async Task<BayeganiNodeDto> AddMainCategoryAsync(int userId, SaveBayeganiFolderDto dto)
    {
        var title = ValidateTitle(dto.Title);
        if (dto.ParentId != 0)
            throw new Exception("دسته اصلی باید در ریشه ساخته شود.");

        var entity = new LetterBayegani
        {
            Title = title,
            ErjaId = null,
            IsFolder = true,
            UserId = userId,
            SematId = null, // فاز چارت سازمانی
            ParentId = 0,
            TypeBayegani = dto.TypeBayegani > 0 ? dto.TypeBayegani : 1,
            IsDelete = false
        };

        _db.LetterBayeganis.Add(entity);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
        return Map(entity);
    }

    public async Task<BayeganiNodeDto> AddSubCategoryAsync(int userId, SaveBayeganiFolderDto dto)
    {
        var title = ValidateTitle(dto.Title);
        if (dto.ParentId <= 0)
            throw new Exception("شناسه پوشه والد نامعتبر است.");

        if (!await FolderExistsAsync(dto.ParentId, userId))
            throw new Exception("پوشه والد یافت نشد.");

        var entity = new LetterBayegani
        {
            Title = title,
            ErjaId = null,
            IsFolder = true,
            UserId = userId,
            SematId = null,
            ParentId = dto.ParentId,
            TypeBayegani = dto.TypeBayegani > 0 ? dto.TypeBayegani : 1,
            IsDelete = false
        };

        _db.LetterBayeganis.Add(entity);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
        return Map(entity);
    }

    public async Task<BayeganiNodeDto> EditFolderAsync(int bayeganiId, int userId, SaveBayeganiFolderDto dto)
    {
        var title = ValidateTitle(dto.Title);
        var entity = await GetOwnedAsync(bayeganiId, userId);

        if (!entity.IsFolder)
            throw new Exception("رکورد انتخاب‌شده پوشه نیست.");

        // پوشه‌های بایگانی دبیرخانه فقط با متدهای مخصوص خودشان ویرایش می‌شوند
        if (entity.TypeBayegani == TypeOutgoing)
            throw new Exception("این پوشه متعلق به بایگانی دبیرخانه است.");

        // فقط مشخصات تغییر می‌کند — ParentId فقط توسط MoveFolder تغییر می‌کند.
        entity.Title = title;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
        return Map(entity);
    }

    public async Task<BayeganiNodeDto> MoveFolderAsync(int bayeganiId, int newParentId, int userId)
    {
        if (bayeganiId == newParentId)
            throw new Exception("پوشه نمی‌تواند داخل خودش قرار بگیرد.");

        var entity = await GetOwnedAsync(bayeganiId, userId);
        if (!entity.IsFolder)
            throw new Exception("رکورد انتخاب‌شده پوشه نیست.");
        if (entity.TypeBayegani == TypeOutgoing)
            throw new Exception("این پوشه متعلق به بایگانی دبیرخانه است.");

        if (newParentId != 0)
        {
            if (!await FolderExistsAsync(newParentId, userId))
                throw new Exception("پوشه مقصد یافت نشد.");

            if (await IsDescendantAsync(bayeganiId, newParentId))
                throw new Exception("پوشه نمی‌تواند داخل یکی از زیرپوشه‌های خودش قرار بگیرد.");
        }

        entity.ParentId = newParentId;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
        return Map(entity);
    }

    // ==================== نامه‌ها ====================

    public async Task AddLettersToArchiveAsync(int userId, ArchiveLettersDto dto)
    {
        var erjaIds = (dto.ErjaIds ?? new()).Where(x => x > 0).Distinct().ToList();
        var letterIds = (dto.LetterIds ?? new()).Where(x => x > 0).Distinct().ToList();

        if (erjaIds.Count == 0 && letterIds.Count == 0)
            throw new Exception("حداقل یک نامه برای بایگانی انتخاب کنید.");

        if (!await FolderExistsAsync(dto.FolderId, userId))
            throw new Exception("پوشه مقصد یافت نشد.");

        // ---------- نامه‌های دریافتی (ارجاع) ----------
        if (erjaIds.Count > 0)
        {
            // نامه‌هایی که قبلاً بایگانی شده‌اند
            var archivedIds = await _db.LetterBayeganis
                .Where(x => x.ErjaId.HasValue && erjaIds.Contains(x.ErjaId.Value) && !x.IsDelete)
                .Select(x => x.ErjaId!.Value)
                .ToListAsync();

            if (archivedIds.Count > 0)
                throw new Exception("برخی از نامه‌ها قبلاً بایگانی شده‌اند.");

            // دریافت ارجاع‌ها — فقط گیرنده می‌تواند نامه خودش را بایگانی کند
            var erjas = await _db.Erjas
                .Include(e => e.Source).ThenInclude(s => s!.InnerLetter)
                .Include(e => e.Source).ThenInclude(s => s!.IncomingLetter)
                .Where(x => erjaIds.Contains(x.ErjaId) && !x.IsDelete)
                .ToListAsync();

            var missing = erjaIds.Except(erjas.Select(x => x.ErjaId)).ToList();
            if (missing.Count > 0)
                throw new Exception("برخی از نامه‌ها یافت نشدند.");

            if (erjas.Any(e => e.ReciverUserId != userId))
                throw new Exception("فقط گیرنده می‌تواند نامه را بایگانی کند.");

            foreach (var erja in erjas)
            {
                var title = string.IsNullOrWhiteSpace(dto.Title)
                    ? (erja.Source?.InnerLetter?.Title ?? erja.Source?.IncomingLetter?.Title ?? "نامه")
                    : dto.Title.Trim();

                _db.LetterBayeganis.Add(new LetterBayegani
                {
                    Title = title.Length > 200 ? title[..200] : title,
                    ErjaId = erja.ErjaId,
                    IsFolder = false,
                    UserId = userId,
                    SematId = null,
                    ParentId = dto.FolderId,
                    TypeBayegani = 1,
                    IsDelete = false
                });

                // تغییر وضعیت نامه
                erja.IsBayegani = true;
            }
        }

        // ---------- نامه‌های ارسالی (فرستنده — بدون ارجاع) ----------
        if (letterIds.Count > 0)
        {
            var already = await _db.LetterBayeganis
                .Where(x => x.LetterId.HasValue && letterIds.Contains(x.LetterId.Value)
                            && x.UserId == userId && !x.IsDelete)
                .Select(x => x.LetterId!.Value)
                .ToListAsync();

            if (already.Count > 0)
                throw new Exception("برخی از نامه‌ها قبلاً بایگانی شده‌اند.");

            var letters = await _db.InnerLetters
                .Where(l => letterIds.Contains(l.Id) && !l.IsDelete)
                .ToListAsync();

            if (letters.Count != letterIds.Count)
                throw new Exception("برخی از نامه‌ها یافت نشدند.");

            if (letters.Any(l => l.CreatorUserId != userId))
                throw new Exception("فقط فرستنده می‌تواند نامه ارسالی خود را بایگانی کند.");

            foreach (var letter in letters)
            {
                var title = string.IsNullOrWhiteSpace(dto.Title) ? letter.Title : dto.Title.Trim();

                _db.LetterBayeganis.Add(new LetterBayegani
                {
                    Title = title.Length > 200 ? title[..200] : title,
                    ErjaId = null,
                    LetterId = letter.Id,
                    IsFolder = false,
                    UserId = userId,
                    SematId = null,
                    ParentId = dto.FolderId,
                    TypeBayegani = 1,
                    IsDelete = false
                });
            }
        }

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
    }

    public async Task UnarchiveByLetterAsync(int letterId, int userId)
    {
        var node = await _db.LetterBayeganis
            .FirstOrDefaultAsync(x => x.LetterId == letterId && !x.IsDelete && x.UserId == userId)
            ?? throw new Exception("این نامه در بایگانی شما نیست.");

        node.IsDelete = true;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
    }

    public async Task<BayeganiNodeDto> MoveLetterAsync(int bayeganiId, int newParentId, int userId)
    {
        var entity = await GetOwnedAsync(bayeganiId, userId);
        if (entity.IsFolder)
            throw new Exception("رکورد انتخاب‌شده نامه نیست.");
        if (entity.TypeBayegani == TypeOutgoing)
            throw new Exception("این نامه متعلق به بایگانی دبیرخانه است.");

        if (!await FolderExistsAsync(newParentId, userId))
            throw new Exception("پوشه مقصد یافت نشد.");

        entity.ParentId = newParentId;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
        return Map(entity);
    }

    // ==================== حذف ====================

    public async Task DeleteAsync(int bayeganiId, int userId)
    {
        var entity = await GetOwnedAsync(bayeganiId, userId);

        // گره‌های بایگانی دبیرخانه مسیر حذف خودشان را دارند (آزاد کردن پرچم IsArchived)
        if (entity.TypeBayegani == TypeOutgoing)
        {
            await DeleteOutgoingAsync(bayeganiId, userId);
            return;
        }

        if (entity.IsFolder)
        {
            var hasChildren = await _db.LetterBayeganis
                .AnyAsync(x => x.ParentId == bayeganiId && !x.IsDelete);

            if (hasChildren)
                throw new Exception("این پوشه دارای محتوا یا زیرپوشه است. ابتدا محتویات پوشه را حذف یا منتقل کنید.");
        }

        // Soft Delete
        entity.IsDelete = true;

        // اگر نامه است، وضعیت بایگانی ارجاع را آزاد کن
        if (entity.ErjaId is { } eid)
        {
            var erja = await _db.Erjas.SingleOrDefaultAsync(x => x.ErjaId == eid);
            if (erja != null) erja.IsBayegani = false;
        }

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
    }

    public async Task UnarchiveByErjaAsync(int erjaId, int userId)
    {
        var node = await _db.LetterBayeganis
            .FirstOrDefaultAsync(x => x.ErjaId == erjaId && !x.IsDelete && x.UserId == userId);

        if (node != null)
        {
            await DeleteAsync(node.BayeganiId, userId);
            return;
        }

        // حالت قدیمی: فقط پرچم ارجاع
        var erja = await _db.Erjas.SingleOrDefaultAsync(x => x.ErjaId == erjaId && !x.IsDelete)
            ?? throw new Exception("ارجاع پیدا نشد.");
        if (erja.ReciverUserId != userId)
            throw new Exception("فقط گیرنده می‌تواند نامه را از بایگانی خارج کند.");

        erja.IsBayegani = false;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("letters");
    }

    // ============================================================
    //  بایگانی دبیرخانه — نامه‌های صادره (TypeBayegani = 2)
    //  درخت پوشه‌ها مستقل از بایگانی شخصی نامه‌های داخلی است تا
    //  دبیرخانه بتواند نامه‌های ارسال‌شده را دسته‌بندی کند.
    // ============================================================

    private static BayeganiNodeDto MapOutgoing(LetterBayegani e) => new()
    {
        BayeganiId = e.BayeganiId,
        Title = e.Title,
        ParentId = e.ParentId,
        TypeBayegani = e.TypeBayegani,
        IsFolder = e.IsFolder,
        ErjaId = e.ErjaId,
        LetterId = e.LetterId,
        SourceType = 2
    };

    public async Task<List<BayeganiNodeDto>> GetOutgoingTreeAsync(int userId)
    {
        var rows = await _db.LetterBayeganis.AsNoTracking()
            .Where(b => b.UserId == userId && !b.IsDelete && b.TypeBayegani == TypeOutgoing)
            .OrderBy(b => b.BayeganiId)
            .ToListAsync();

        // اطلاعات نامه‌های صادرهٔ بایگانی‌شده
        var letterIds = rows.Where(r => r.LetterId.HasValue).Select(r => r.LetterId!.Value).Distinct().ToList();

        var info = new Dictionary<int, BayeganiNodeDto>();
        if (letterIds.Count > 0)
        {
            var letters = await _db.OutgoingLetters.AsNoTracking()
                .Where(l => letterIds.Contains(l.Id))
                .Select(l => new
                {
                    l.Id,
                    l.Title,
                    l.LetterNumber,
                    l.SadereNumber,
                    l.ReceiverOrganization,
                    l.DateSabt,
                    l.DateSadere,
                    l.Foriat,
                    l.Mahramanegi,
                    l.SendMethod,
                    Sender = l.Creator != null
                        ? (string.IsNullOrEmpty(l.Creator.FirstName + l.Creator.LastName)
                            ? l.Creator.Username
                            : (l.Creator.FirstName + " " + l.Creator.LastName).Trim())
                        : "",
                    HasAttachment = _db.AppAttachments.Any(a => a.Module == "OutgoingLetters" && a.RefId == l.Id)
                })
                .ToListAsync();

            foreach (var l in letters)
            {
                info[l.Id] = new BayeganiNodeDto
                {
                    LetterId = l.Id,
                    LetterNumber = l.SadereNumber ?? l.LetterNumber ?? "",
                    Title = l.Title,
                    Sender = l.Sender,
                    Date = l.DateSadere ?? l.DateSabt,
                    Foriat = l.Foriat,
                    Mahramanegi = l.Mahramanegi,
                    HasAttachment = l.HasAttachment,
                    ReceiverOrganization = l.ReceiverOrganization,
                    SadereNumber = l.SadereNumber,
                    SendMethod = l.SendMethod,
                    SourceType = 2
                };
            }
        }

        var nodes = new Dictionary<int, BayeganiNodeDto>();
        foreach (var r in rows)
        {
            var n = MapOutgoing(r);
            if (r.LetterId is { } lid && info.TryGetValue(lid, out var li))
            {
                n.LetterNumber = li.LetterNumber;
                n.Title = li.Title;                 // عنوان زندهٔ نامه
                n.Sender = li.Sender;
                n.Date = li.Date;
                n.Foriat = li.Foriat;
                n.Mahramanegi = li.Mahramanegi;
                n.HasAttachment = li.HasAttachment;
                n.ReceiverOrganization = li.ReceiverOrganization;
                n.SadereNumber = li.SadereNumber;
                n.SendMethod = li.SendMethod;
            }
            nodes[n.BayeganiId] = n;
        }

        var roots = new List<BayeganiNodeDto>();
        foreach (var n in nodes.Values)
        {
            if (n.ParentId != 0 && nodes.TryGetValue(n.ParentId, out var parent) && parent.IsFolder)
                parent.Children.Add(n);
            else
                roots.Add(n);
        }

        SortTree(roots);
        return roots;
    }

    public async Task<BayeganiNodeDto> AddOutgoingMainCategoryAsync(int userId, SaveBayeganiFolderDto dto)
    {
        var title = ValidateTitle(dto.Title);
        if (dto.ParentId != 0)
            throw new Exception("دسته اصلی باید در ریشه ساخته شود.");

        var entity = new LetterBayegani
        {
            Title = title,
            ErjaId = null,
            LetterId = null,
            IsFolder = true,
            UserId = userId,
            ParentId = 0,
            TypeBayegani = TypeOutgoing,
            IsDelete = false
        };

        _db.LetterBayeganis.Add(entity);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
        return MapOutgoing(entity);
    }

    public async Task<BayeganiNodeDto> AddOutgoingSubCategoryAsync(int userId, SaveBayeganiFolderDto dto)
    {
        var title = ValidateTitle(dto.Title);
        if (dto.ParentId <= 0)
            throw new Exception("شناسه پوشه والد نامعتبر است.");
        if (!await FolderExistsAsync(dto.ParentId, userId, TypeOutgoing))
            throw new Exception("پوشه والد یافت نشد.");

        var entity = new LetterBayegani
        {
            Title = title,
            ErjaId = null,
            LetterId = null,
            IsFolder = true,
            UserId = userId,
            ParentId = dto.ParentId,
            TypeBayegani = TypeOutgoing,
            IsDelete = false
        };

        _db.LetterBayeganis.Add(entity);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
        return MapOutgoing(entity);
    }

    public async Task<BayeganiNodeDto> EditOutgoingFolderAsync(int bayeganiId, int userId, SaveBayeganiFolderDto dto)
    {
        var title = ValidateTitle(dto.Title);
        var entity = await GetOwnedAsync(bayeganiId, userId);

        if (!entity.IsFolder) throw new Exception("رکورد انتخاب‌شده پوشه نیست.");
        if (entity.TypeBayegani != TypeOutgoing) throw new Exception("این پوشه متعلق به بایگانی دبیرخانه نیست.");

        entity.Title = title;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
        return MapOutgoing(entity);
    }

    public async Task<BayeganiNodeDto> MoveOutgoingFolderAsync(int bayeganiId, int newParentId, int userId)
    {
        if (bayeganiId == newParentId)
            throw new Exception("پوشه نمی‌تواند داخل خودش قرار بگیرد.");

        var entity = await GetOwnedAsync(bayeganiId, userId);
        if (!entity.IsFolder) throw new Exception("رکورد انتخاب‌شده پوشه نیست.");
        if (entity.TypeBayegani != TypeOutgoing) throw new Exception("این پوشه متعلق به بایگانی دبیرخانه نیست.");

        if (newParentId != 0)
        {
            if (!await FolderExistsAsync(newParentId, userId, TypeOutgoing))
                throw new Exception("پوشه مقصد یافت نشد.");
            if (await IsDescendantAsync(bayeganiId, newParentId, TypeOutgoing))
                throw new Exception("پوشه نمی‌تواند داخل یکی از زیرپوشه‌های خودش قرار بگیرد.");
        }

        entity.ParentId = newParentId;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
        return MapOutgoing(entity);
    }

    /// <summary>بایگانی یک یا چند نامه صادره در پوشهٔ انتخابی دبیرخانه</summary>
    public async Task ArchiveOutgoingLettersAsync(int userId, ArchiveOutgoingLettersDto dto)
    {
        var letterIds = (dto.LetterIds ?? new()).Where(x => x > 0).Distinct().ToList();
        if (letterIds.Count == 0)
            throw new Exception("حداقل یک نامه برای بایگانی انتخاب کنید.");

        // پوشه مقصد — FolderId=0 یعنی ریشهٔ بایگانی دبیرخانه
        if (dto.FolderId != 0 && !await FolderExistsAsync(dto.FolderId, userId, TypeOutgoing))
            throw new Exception("پوشه مقصد یافت نشد.");

        var already = await _db.LetterBayeganis
            .Where(x => x.LetterId.HasValue && letterIds.Contains(x.LetterId.Value)
                        && x.UserId == userId && x.TypeBayegani == TypeOutgoing && !x.IsDelete)
            .Select(x => x.LetterId!.Value)
            .ToListAsync();

        if (already.Count > 0)
            throw new Exception("برخی از نامه‌ها قبلاً بایگانی شده‌اند.");

        var letters = await _db.OutgoingLetters
            .Where(l => letterIds.Contains(l.Id) && !l.IsDelete)
            .ToListAsync();

        if (letters.Count != letterIds.Count)
            throw new Exception("برخی از نامه‌ها یافت نشدند.");

        // نامه صادره فقط بعد از امضا (دارا بودن شماره صادره) بایگانی می‌شود
        var notSigned = letters.Where(l => string.IsNullOrWhiteSpace(l.SadereNumber)).ToList();
        if (notSigned.Count > 0)
            throw new Exception("فقط نامه‌های امضا شده (دارای شماره صادره) قابل بایگانی در دبیرخانه هستند.");

        foreach (var letter in letters)
        {
            var title = string.IsNullOrWhiteSpace(dto.Title) ? letter.Title : dto.Title.Trim();

            _db.LetterBayeganis.Add(new LetterBayegani
            {
                Title = title.Length > 200 ? title[..200] : title,
                ErjaId = null,
                LetterId = letter.Id,
                IsFolder = false,
                UserId = userId,
                ParentId = dto.FolderId,
                TypeBayegani = TypeOutgoing,
                IsDelete = false
            });

            letter.IsArchived = true;
            letter.ArchivedAt = DateTime.Now;
            letter.ArchivedByUserId = userId;
        }

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
    }

    public async Task UnarchiveOutgoingLetterAsync(int letterId, int userId)
    {
        var node = await _db.LetterBayeganis
            .FirstOrDefaultAsync(x => x.LetterId == letterId && x.UserId == userId
                                   && x.TypeBayegani == TypeOutgoing && !x.IsDelete)
            ?? throw new Exception("این نامه در بایگانی دبیرخانه شما نیست.");

        node.IsDelete = true;

        var letter = await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == letterId);
        if (letter != null)
        {
            letter.IsArchived = false;
            letter.ArchivedAt = null;
            letter.ArchivedByUserId = null;
        }

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
    }

    public async Task<BayeganiNodeDto> MoveArchivedLetterAsync(MoveArchivedLetterDto dto, int userId)
    {
        var entity = await GetOwnedAsync(dto.BayeganiId, userId);
        if (entity.IsFolder) throw new Exception("رکورد انتخاب‌شده نامه نیست.");
        if (entity.TypeBayegani != TypeOutgoing) throw new Exception("این نامه متعلق به بایگانی دبیرخانه نیست.");
        if (!await FolderExistsAsync(dto.NewParentId, userId, TypeOutgoing))
            throw new Exception("پوشه مقصد یافت نشد.");

        entity.ParentId = dto.NewParentId;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
        return MapOutgoing(entity);
    }

    /// <summary>
    /// حذف از بایگانی دبیرخانه — پوشهٔ خالی حذف می‌شود؛ نامه از بایگانی خارج
    /// می‌شود (Soft Delete روی گره + آزاد شدن پرچم IsArchived روی نامه)
    /// </summary>
    public async Task DeleteOutgoingAsync(int bayeganiId, int userId)
    {
        var entity = await GetOwnedAsync(bayeganiId, userId);
        if (entity.TypeBayegani != TypeOutgoing)
            throw new Exception("این مورد متعلق به بایگانی دبیرخانه نیست.");

        if (entity.IsFolder)
        {
            var hasChildren = await _db.LetterBayeganis
                .AnyAsync(x => x.ParentId == bayeganiId && !x.IsDelete);

            if (hasChildren)
                throw new Exception("این پوشه دارای محتوا یا زیرپوشه است. ابتدا محتویات پوشه را حذف یا منتقل کنید.");
        }
        else if (entity.LetterId is { } lid)
        {
            var letter = await _db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == lid);
            if (letter != null)
            {
                letter.IsArchived = false;
                letter.ArchivedAt = null;
                letter.ArchivedByUserId = null;
            }
        }

        entity.IsDelete = true;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("outgoing-letters");
    }
}
