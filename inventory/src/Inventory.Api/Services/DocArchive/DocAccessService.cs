using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.DocArchive;

/// <summary>
/// محاسبه سطح دسترسی کاربر روی پوشه‌ها و مدارک آرشیو اسناد.
/// قواعد:
///   ۱) ادمین/دارنده مجوز DocArchive.Manage ⇒ Full
///   ۲) «نمایش برای همه» روی پوشه یا مدرک ⇒ حداقل Read
///   ۳) دسترسی مستقیم روی مدرک، دسترسی پوشه و همه پوشه‌های والد ⇒ بیشترین سطح برنده است
/// </summary>
public interface IDocAccessService
{
    Task<Dictionary<int, (DocAccessLevel Level, bool Download)>> FolderAccessMapAsync(int userId, bool isManager);
    Task<(DocAccessLevel Level, bool Download)> FolderAccessAsync(int userId, bool isManager, int folderId);
    Task<(DocAccessLevel Level, bool Download)> DocumentAccessAsync(int userId, bool isManager, int documentId);

    /// <summary>کاربرانی که روی این مدرک دسترسی «کامل» دارند (برای نشاندن کار در کارتابل).</summary>
    Task<List<int>> UsersWithFullAccessAsync(int documentId);
}

public class DocAccessService : IDocAccessService
{
    private readonly AppDbContext _db;
    public DocAccessService(AppDbContext db) => _db = db;

    private static (DocAccessLevel, bool) Max((DocAccessLevel L, bool D) a, (DocAccessLevel L, bool D) b)
        => ((DocAccessLevel)Math.Max((int)a.L, (int)b.L), a.D || b.D);

    /// <summary>نقشه دسترسی همه پوشه‌ها با اعمال ارث‌بری از والدها.</summary>
    public async Task<Dictionary<int, (DocAccessLevel, bool)>> FolderAccessMapAsync(int userId, bool isManager)
    {
        var folders = await _db.DocFolders.AsNoTracking()
            .Select(f => new { f.Id, f.ParentId, f.IsPublic, f.PublicCanDownload })
            .ToListAsync();

        var map = new Dictionary<int, (DocAccessLevel, bool)>();
        if (isManager)
        {
            foreach (var f in folders) map[f.Id] = (DocAccessLevel.Full, true);
            return map;
        }

        var perms = await _db.DocFolderPermissions.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync();
        var direct = perms.GroupBy(p => p.FolderId)
            .ToDictionary(g => g.Key,
                g => ((DocAccessLevel)g.Max(x => (int)x.Level), g.Any(x => x.CanDownload)));

        var byId = folders.ToDictionary(f => f.Id);

        (DocAccessLevel, bool) Resolve(int id, HashSet<int> guard)
        {
            if (map.TryGetValue(id, out var cached)) return cached;
            if (!guard.Add(id) || !byId.TryGetValue(id, out var f)) return (DocAccessLevel.None, false);

            var result = (DocAccessLevel.None, false);
            if (direct.TryGetValue(id, out var d)) result = Max(result, d);
            if (f.IsPublic) result = Max(result, (DocAccessLevel.Read, f.PublicCanDownload));
            if (f.ParentId is int pid) result = Max(result, Resolve(pid, guard));

            map[id] = result;
            return result;
        }

        foreach (var f in folders) Resolve(f.Id, new HashSet<int>());
        return map;
    }

    public async Task<(DocAccessLevel, bool)> FolderAccessAsync(int userId, bool isManager, int folderId)
    {
        if (isManager) return (DocAccessLevel.Full, true);
        var map = await FolderAccessMapAsync(userId, false);
        return map.TryGetValue(folderId, out var v) ? v : (DocAccessLevel.None, false);
    }

    public async Task<(DocAccessLevel, bool)> DocumentAccessAsync(int userId, bool isManager, int documentId)
    {
        if (isManager) return (DocAccessLevel.Full, true);

        var doc = await _db.Documents.AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => new { d.Id, d.FolderId, d.IsPublic, d.PublicCanDownload, d.CreatedByUserId })
            .FirstOrDefaultAsync();
        if (doc == null) return (DocAccessLevel.None, false);

        var result = (DocAccessLevel.None, false);

        // سازنده مدرک همیشه دسترسی کامل دارد
        if (doc.CreatedByUserId == userId) result = Max(result, (DocAccessLevel.Full, true));
        if (doc.IsPublic) result = Max(result, (DocAccessLevel.Read, doc.PublicCanDownload));

        var dp = await _db.DocumentPermissions.AsNoTracking()
            .Where(p => p.DocumentId == documentId && p.UserId == userId)
            .ToListAsync();
        foreach (var p in dp) result = Max(result, (p.Level, p.CanDownload));

        var folderAccess = await FolderAccessAsync(userId, false, doc.FolderId);
        result = Max(result, folderAccess);

        return result;
    }

    public async Task<List<int>> UsersWithFullAccessAsync(int documentId)
    {
        var doc = await _db.Documents.AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => new { d.FolderId, d.CreatedByUserId })
            .FirstOrDefaultAsync();
        if (doc == null) return new();

        var ids = new HashSet<int> { doc.CreatedByUserId };

        // دسترسی مستقیم کامل روی مدرک
        var direct = await _db.DocumentPermissions.AsNoTracking()
            .Where(p => p.DocumentId == documentId && p.Level == DocAccessLevel.Full)
            .Select(p => p.UserId).ToListAsync();
        foreach (var id in direct) ids.Add(id);

        // دسترسی کامل روی پوشه یا هر پوشه والد
        var folders = await _db.DocFolders.AsNoTracking()
            .Select(f => new { f.Id, f.ParentId }).ToListAsync();
        var byId = folders.ToDictionary(f => f.Id, f => f.ParentId);

        var chain = new List<int>();
        int? cur = doc.FolderId;
        var guard = new HashSet<int>();
        while (cur is int c && guard.Add(c))
        {
            chain.Add(c);
            cur = byId.TryGetValue(c, out var p) ? p : null;
        }

        var folderFull = await _db.DocFolderPermissions.AsNoTracking()
            .Where(p => chain.Contains(p.FolderId) && p.Level == DocAccessLevel.Full)
            .Select(p => p.UserId).ToListAsync();
        foreach (var id in folderFull) ids.Add(id);

        return ids.Where(i => i > 0).ToList();
    }
}
